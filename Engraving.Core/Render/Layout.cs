using System;
using System.Collections.Generic;
using System.Linq;
using Engraving.Core.Geometry;
using Engraving.Core.Language;

namespace Engraving.Core.Render
{
    public struct Box
    {
        public float X0, Y0, X1, Y1;
        public Box(float x0, float y0, float x1, float y1) { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
        public float W => X1 - X0;
        public float H => Y1 - Y0;
        public float CX => (X0 + X1) * 0.5f;
        public float CY => (Y0 + Y1) * 0.5f;
        public bool Contains(float x, float y) => x >= X0 && x < X1 && y >= Y0 && y < Y1;
        public Box Inset(float d) => new Box(X0 + d, Y0 + d, X1 - d, Y1 - d);
        public override string ToString() => $"[{X0:0.###},{Y0:0.###} .. {X1:0.###},{Y1:0.###}]";
    }

    /// <summary>One charge instance with its final transform (metres, y up).</summary>
    public class Placement
    {
        public ChargeSpec Spec;
        public IShape Shape;
        public float X, Y, Scale, Cos = 1, Sin;
        public bool Flip;
        public float Reach; // bounding radius in metres, for culling

        public float Distance(float x, float y)
        {
            float dx = x - X, dy = y - Y;
            float lx = (dx * Cos + dy * Sin) / Scale;
            float ly = (-dx * Sin + dy * Cos) / Scale;
            if (Flip) lx = -lx;
            return Shape.Distance(lx, ly) * Scale;
        }

        /// <summary>Distance to the shape's inner cut lines in metres (figures), or a large number.</summary>
        public float CutDistance(float x, float y)
        {
            if (!(Shape is Figures.ICutLines cuts)) return float.MaxValue;
            float dx = x - X, dy = y - Y;
            float lx = (dx * Cos + dy * Sin) / Scale;
            float ly = (-dx * Sin + dy * Cos) / Scale;
            if (Flip) lx = -lx;
            return cuts.CutDistance(lx, ly) * Scale;
        }
    }

    /// <summary>
    /// Automatic charge sizing, in the spirit of heraldic blazon: the field is split into the rows that
    /// are used, each row into the columns used in it, and every charge group fills its cell.
    /// A lone charge in the centre gets the whole field; "top" and "bottom" split it in half, and so on.
    /// </summary>
    public static class Layout
    {
        public static List<Placement> Place(List<(ChargeSpec spec, IShape shape)> charges, Box field)
        {
            var result = new List<Placement>();
            if (charges.Count == 0) return result;
            // a scene lays itself out over the whole field; anything else is placed as usual, over it
            var scene = charges.Where(c => c.spec.Slot != SceneSlot.None).ToList();
            if (scene.Count > 0)
            {
                PlaceScene(scene, field, result);
                charges = charges.Where(c => c.spec.Slot == SceneSlot.None).ToList();
                if (charges.Count == 0) return result;
            }

            var rows = charges.Select(c => c.spec.Position.Row).Distinct().OrderBy(r => r).ToList();
            float rowH = field.H / rows.Count;
            foreach (var group in charges.GroupBy(c => (c.spec.Position.Row, c.spec.Position.Col)))
            {
                int row = group.Key.Row, col = group.Key.Col;
                int ri = rows.IndexOf(row);
                var colsInRow = charges.Where(c => c.spec.Position.Row == row).Select(c => c.spec.Position.Col).Distinct().OrderBy(c => c).ToList();
                float colW = field.W / colsInRow.Count;
                int ci = colsInRow.IndexOf(col);
                // y is up: row index 0 (top) is the highest band of the field
                var cell = new Box(field.X0 + ci * colW, field.Y1 - (ri + 1) * rowH, field.X0 + (ci + 1) * colW, field.Y1 - ri * rowH);
                cell = cell.Inset(Math.Min(cell.W, cell.H) * 0.05f);
                // several statements at the same position overlay each other (e.g. a ring with a star inside)
                foreach (var (spec, shape) in group)
                    PlaceGroup(spec, shape, cell, result);
            }
            LockFigureScale(result);
            return result;
        }

        /// <summary>
        /// Several figures in one carving share one scale, the smallest any of them got, so a Viking is the same size
        /// whatever he holds (Hippo, 2026-10-31); figures in one row share a ground line. A lone figure fills its cell.
        /// </summary>
        static void LockFigureScale(List<Placement> placements)
        {
            var figures = placements.Where(p => p.Shape is Figures.FigureShape && p.Spec.Slot == SceneSlot.None).ToList();
            if (figures.Count < 2) return;
            float scale = figures.Min(p => p.Scale);
            // figures in the same row stand on one ground line: the lowest feet among them
            foreach (var row in figures.GroupBy(p => p.Spec.Position.Row))
            {
                float ground = row.Min(p => p.Y - p.Shape.HalfHeight * p.Scale);
                foreach (var p in row)
                {
                    p.Scale = scale;
                    p.Y = ground + p.Shape.HalfHeight * scale;
                    p.Reach = scale * M.Len(p.Shape.HalfWidth, p.Shape.HalfHeight) + 0.05f;
                }
            }
        }

        /// <summary>
        /// A scene (Hippo, 2026-10-30): the hero on the left and the counterpart on the right, standing on one ground
        /// line, at their true relative sizes, fudged so a dragon or a tree doesn't shrink the Viking to a speck. The
        /// ground line runs the full width of the field, so a row of scene panels shares one ground.
        /// </summary>
        static void PlaceScene(List<(ChargeSpec spec, IShape shape)> scene, Box field, List<Placement> outList)
        {
            var hero = scene.FirstOrDefault(c => c.spec.Slot == SceneSlot.Hero);
            var foe = scene.FirstOrDefault(c => c.spec.Slot == SceneSlot.Foe);
            var above = scene.FirstOrDefault(c => c.spec.Slot == SceneSlot.Above);
            var ground = scene.FirstOrDefault(c => c.spec.Slot == SceneSlot.Ground);

            float t = M.Clamp(field.H * 0.022f, 0.012f, 0.05f);           // the ground line's thickness
            float groundY = field.Y0 + field.H * 0.07f;
            float floor = ground.spec != null ? groundY + t * 0.5f : field.Y0 + field.H * 0.04f;
            if (ground.spec != null)
            {
                float half = field.W / 2;
                outList.Add(new Placement
                {
                    Spec = ground.spec, Shape = new FuncShape((x, y) => Sdf.Box(x, y, 1, t * 0.5f / half), 1, t * 0.5f / half),
                    X = field.CX, Y = groundY, Scale = half, Reach = half + t,
                });
            }
            if (hero.spec == null) return;

            // sizes in metres; the hero's height is the yardstick
            float heroH = 2 * hero.shape.HalfHeight;
            float Fudge(IShape s) => s == null ? 1 : M.Min(1, M.Min(2.1f * heroH / (2 * s.HalfHeight), 2.4f * heroH / (2 * s.HalfWidth)));
            float ff = Fudge(foe.shape), fa = above.shape != null ? M.Min(1, 0.5f * heroH / (2 * M.Max(above.shape.HalfHeight, above.shape.HalfWidth * 0.8f))) : 1;
            float gap = foe.spec != null ? foe.spec.SceneGap * heroH : 0;
            float lift = foe.spec != null ? foe.spec.SceneLift * heroH : 0;
            float heroW = 2 * hero.shape.HalfWidth, foeW = foe.shape != null ? 2 * foe.shape.HalfWidth * ff : 0;
            float aboveH = above.shape != null ? 2 * above.shape.HalfHeight * fa + 0.08f * heroH : 0;
            float totalW = heroW + (foe.shape != null ? gap + foeW : 0);
            totalW = M.Max(totalW, above.shape != null ? 2 * above.shape.HalfWidth * fa : 0);
            float totalH = M.Max(heroH + aboveH, foe.shape != null ? 2 * foe.shape.HalfHeight * ff + lift : 0);

            float s = M.Min(field.W * 0.92f / totalW, (field.Y1 - field.H * 0.05f - floor) / totalH);
            float x0 = field.CX - totalW * s / 2;

            Placement Put((ChargeSpec spec, IShape shape) c, float x, float y, float scale) => new Placement
            {
                Spec = c.spec, Shape = c.shape, X = x, Y = y, Scale = scale, Flip = c.spec.Flipped,
                Reach = scale * M.Len(c.shape.HalfWidth, c.shape.HalfHeight) + 0.05f,
            };
            var h = Put(hero, x0 + hero.shape.HalfWidth * s, floor + hero.shape.HalfHeight * s, s);
            outList.Add(h);
            if (foe.shape != null)
                outList.Add(Put(foe, x0 + (heroW + gap + foe.shape.HalfWidth * ff) * s, floor + (lift + foe.shape.HalfHeight * ff) * s, s * ff));
            if (above.shape != null)
                outList.Add(Put(above, h.X, floor + (heroH + 0.08f * heroH + above.shape.HalfHeight * fa) * s, s * fa));
        }

        static void PlaceGroup(ChargeSpec spec, IShape shape, Box cell, List<Placement> outList)
        {
            int n = Math.Max(1, spec.Count);
            float rot = spec.Rotation * M.Pi / 180f;
            // rotated bounding box of the shape in local units
            float c = M.Abs(M.Cos(rot)), s = M.Abs(M.Sin(rot));
            float hw = shape.HalfWidth * c + shape.HalfHeight * s;
            float hh = shape.HalfWidth * s + shape.HalfHeight * c;
            const float fill = 0.92f;

            var centres = new List<(float x, float y)>();
            float scale;
            var arr = spec.Arrangement;
            if (n == 1)
            {
                scale = Math.Min(cell.W / (2 * hw), cell.H / (2 * hh)) * fill;
                centres.Add((cell.CX, cell.CY));
            }
            else if (arr == Arrangement.Ring)
            {
                float half = Math.Min(cell.W, cell.H) * 0.5f;
                float sinA = M.Sin(M.Pi / n);
                float itemR = half * sinA / (1 + sinA) * 0.95f;  // items just short of touching
                float ringR = half - itemR;
                scale = itemR / M.Max(hw, hh) * fill;
                for (int i = 0; i < n; i++)
                {
                    float a = M.Pi / 2 - i * 2 * M.Pi / n;
                    centres.Add((cell.CX + ringR * M.Cos(a), cell.CY + ringR * M.Sin(a)));
                }
            }
            else
            {
                int cols;
                if (arr == Arrangement.Row) cols = n;
                else if (arr == Arrangement.Column) cols = 1;
                else
                {
                    // grid: choose the column count that gives the biggest charges, preferring square-ish
                    cols = 1;
                    float best = 0;
                    for (int k = 1; k <= n; k++)
                    {
                        int r = (n + k - 1) / k;
                        float sz = Math.Min(cell.W / k / (2 * hw), cell.H / r / (2 * hh));
                        if (arr == Arrangement.Grid && Math.Abs(k - r) > 1 && k * r != n) sz *= 0.9f;
                        if (sz > best * 1.0001f) { best = sz; cols = k; }
                    }
                }
                int rowsN = (n + cols - 1) / cols;
                float slotW = cell.W / cols, slotH = cell.H / rowsN;
                scale = Math.Min(slotW / (2 * hw), slotH / (2 * hh)) * fill;
                for (int i = 0; i < n; i++)
                {
                    int r = i / cols, k = i % cols;
                    int inRow = Math.Min(cols, n - r * cols);
                    float rowStart = cell.CX - inRow * slotW * 0.5f;   // last row centred
                    centres.Add((rowStart + (k + 0.5f) * slotW, cell.Y1 - (r + 0.5f) * slotH - (cell.H - rowsN * slotH) * 0.5f));
                }
            }

            scale *= spec.Scale;
            foreach (var (x, y) in centres)
                outList.Add(new Placement
                {
                    Spec = spec, Shape = shape, X = x, Y = y, Scale = scale,
                    Cos = M.Cos(rot), Sin = M.Sin(rot), Flip = spec.Flipped,
                    Reach = scale * M.Len(shape.HalfWidth, shape.HalfHeight) + 0.05f,
                });
        }
    }
}
