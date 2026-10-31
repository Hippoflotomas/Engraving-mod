using System;
using System.Collections.Generic;
using System.Linq;
using Engraving.Core.Geometry;

namespace Engraving.Core.Figures
{
    /// <summary>A shape with thin lines cut inside it (where parts of a figure overlap, belts, shield rims).</summary>
    public interface ICutLines
    {
        /// <summary>Distance to the nearest cut line inside the shape, in shape units (1 or more if none near).</summary>
        float CutDistance(float x, float y);
    }

    /// <summary>
    /// A posed figure as a charge: the silhouette of all its parts, plus cut lines where a part lies over parts behind
    /// it (Hippo's reference carvings: solid figures, detail only from thin cuts). Both are baked onto grids once per
    /// pose and held items, so drawing costs the same as any picture.
    /// </summary>
    public sealed class FigureShape : IShape, ICutLines
    {
        const int Res = 420;

        readonly float[] _sil, _cut;
        readonly int _w, _h;
        readonly float _x0, _y0, _cell;
        public float HalfWidth { get; }
        public float HalfHeight { get; }

        static readonly Dictionary<string, FigureShape> Cache = new Dictionary<string, FigureShape>();

        /// <summary>The figure for a pose and held items, built once and kept.</summary>
        public static FigureShape Get(FigureType type, Pose pose, IList<string> held)
        {
            pose = pose ?? PoseLibrary.Resolve(type.Rig, "standing", null);
            // keyed by the angles, so an edited pose of the same name is built afresh (scenery has no pose)
            string angles = type.Posable ? PoseLibrary.AnglesKey(pose) : "-";
            string key = type.Rig + "|" + type.Id + "|" + angles + "|" + string.Join(",", held ?? new string[0]);
            lock (Cache)
            {
                if (!Cache.TryGetValue(key, out var shape))
                    Cache[key] = shape = new FigureShape(BuildParts(type, pose, held));
                return shape;
            }
        }

        /// <summary>A figure's parts, back to front, standing on y = 0 (before baking).</summary>
        public static List<Part> BuildParts(FigureType type, Pose pose, IList<string> held)
        {
            switch (type.Rig)
            {
                case "scenery": return Scenery.Build(type.Species);
                case "trophy": return Scenery.Trophy(type.Base);
                case "altar": return Scenery.Altar(type.Base);
                case "quadruped": return Quadruped.Build(type.Species, pose);
                case "flyer": return Flyer.Build(type.Species, pose);
                case "serpent": return Serpent.Build(type.Species, pose);
                case "blob": return Blob.Build(type.Species, pose);
                case "insect": return Insect.Build(type.Species, pose);
                default: return Humanoid.Build(type.Species, pose, held);
            }
        }

        /// <summary>The body's size (a troll is 1.9): the grids are baked at size 1 and scaled when sampled.</summary>
        readonly float _scale;

        public FigureShape(List<Part> parts)
        {
            _scale = parts.Count > 0 ? parts.Max(p => p.Scale) : 1;
            foreach (var p in parts) p.Scale = 1;
            // bounds, from a coarse scan
            float bx0 = float.MaxValue, by0 = float.MaxValue, bx1 = float.MinValue, by1 = float.MinValue;
            for (float y = -0.3f; y < 4f; y += 0.02f)
                for (float x = -4f; x < 4f; x += 0.02f)
                    if (parts.Any(p => p.Distance(x, y) < 0))
                    {
                        bx0 = M.Min(bx0, x); bx1 = M.Max(bx1, x); by0 = M.Min(by0, y); by1 = M.Max(by1, y);
                    }
            const float Margin = 0.06f;
            bx0 -= Margin; by0 -= Margin; bx1 += Margin; by1 += Margin;
            _cell = M.Max(bx1 - bx0, by1 - by0) / Res;
            _w = (int)Math.Ceiling((bx1 - bx0) / _cell) + 1;
            _h = (int)Math.Ceiling((by1 - by0) / _cell) + 1;
            _x0 = bx0; _y0 = by0;
            HalfWidth = (bx1 - bx0 - 2 * Margin) / 2 * _scale;
            HalfHeight = (by1 - by0 - 2 * Margin) / 2 * _scale;
            float cx = (bx0 + bx1) / 2, cy = (by0 + by1) / 2;
            // the grids are stored relative to the figure's centre
            _x0 -= cx; _y0 -= cy;

            _sil = new float[_w * _h];
            _cut = new float[_w * _h];
            int n = parts.Count;
            var d = new float[n];
            var front = new float[n + 1];
            for (int j = 0; j < _h; j++)
                for (int i = 0; i < _w; i++)
                {
                    float x = bx0 + i * _cell, y = by0 + j * _cell;
                    float sil = float.MaxValue;
                    for (int k = 0; k < n; k++) { d[k] = parts[k].Distance(x, y); sil = M.Min(sil, d[k]); }
                    // in front of part k: the nearest edge of anything drawn after it
                    front[n] = float.MaxValue;
                    for (int k = n - 1; k >= 0; k--) front[k] = M.Min(front[k + 1], d[k]);
                    float cut = 1f, behind = float.MaxValue;
                    for (int k = 0; k < n; k++)
                    {
                        bool uncovered = front[k + 1] > 0;
                        // a part's outline is cut where it lies over something behind it
                        if (uncovered && behind < 0) cut = M.Min(cut, M.Abs(d[k]));
                        // its own detail lines, where it's the part on top
                        if (uncovered && d[k] < 0) cut = M.Min(cut, parts[k].Lines(x, y));
                        behind = M.Min(behind, d[k]);
                    }
                    _sil[j * _w + i] = sil;
                    _cut[j * _w + i] = cut;
                }
        }

        public float Distance(float x, float y) => Sample(_sil, x / _scale, y / _scale, true) * _scale;
        public float CutDistance(float x, float y) => Sample(_cut, x / _scale, y / _scale, false) * _scale;

        float Sample(float[] g, float x, float y, bool extend)
        {
            float fx = (x - _x0) / _cell, fy = (y - _y0) / _cell;
            float cx = M.Clamp(fx, 0, _w - 1.001f), cy = M.Clamp(fy, 0, _h - 1.001f);
            int ix = (int)cx, iy = (int)cy;
            float tx = cx - ix, ty = cy - iy;
            int o = iy * _w + ix;
            float v = M.Lerp(M.Lerp(g[o], g[o + 1], tx), M.Lerp(g[o + _w], g[o + _w + 1], tx), ty);
            if (!extend) return (cx != fx || cy != fy) ? 1f : v;
            return v + M.Len((fx - cx) * _cell, (fy - cy) * _cell);
        }
    }
}
