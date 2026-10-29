using System;
using System.Collections.Generic;

namespace Engraving.Core.Geometry
{
    /// <summary>
    /// A charge outline as a signed distance field in local units. The shape fits inside
    /// [-HalfWidth, HalfWidth] x [-HalfHeight, HalfHeight], with the larger of the two equal to 1.
    /// +y is up.
    /// </summary>
    public interface IShape
    {
        float HalfWidth { get; }
        float HalfHeight { get; }
        float Distance(float x, float y);
    }

    public sealed class FuncShape : IShape
    {
        readonly Func<float, float, float> _f;
        public float HalfWidth { get; }
        public float HalfHeight { get; }
        public FuncShape(Func<float, float, float> f, float hw = 1, float hh = 1) { _f = f; HalfWidth = hw; HalfHeight = hh; }
        public float Distance(float x, float y) => _f(x, y);
    }

    /// <summary>The geometric charges that need no image files.</summary>
    public static class BuiltinShapes
    {
        static readonly Dictionary<string, IShape> Shapes = new Dictionary<string, IShape>();
        static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>();

        static BuiltinShapes()
        {
            Add("circle", (x, y) => Sdf.Circle(x, y, 1f), "disc", "disk", "roundel", "boss", "ball", "orb");
            Add("ring", (x, y) => M.Abs(Sdf.Circle(x, y, 0.8f)) - 0.2f, "annulet", "hoop");
            Add("square", (x, y) => Sdf.Box(x, y, 0.9f, 0.9f), "box", "block");
            var diamond = new[] { 0f, 1f, 0.72f, 0f, 0f, -1f, -0.72f, 0f };
            Add("diamond", (x, y) => Sdf.Polygon(x, y, diamond), 0.72f, 1f, "lozenge", "rhombus");
            var tri = Sdf.StarPoints(3, 1.15f, 2f);
            for (int i = 1; i < tri.Length; i += 2) tri[i] -= 0.15f;
            Add("triangle", (x, y) => Sdf.Polygon(x, y, tri), "pyramid");
            var star = Sdf.StarPoints(5, 1f, 0.42f);
            Add("star", (x, y) => Sdf.Polygon(x, y + 0.05f, star), "mullet", "pentagram");
            var star6 = Sdf.StarPoints(6, 1f, 0.55f);
            Add("star6", (x, y) => Sdf.Polygon(x, y, star6), "six-pointed-star", "hexagram");
            var star8 = Sdf.StarPoints(8, 1f, 0.5f);
            Add("star8", (x, y) => Sdf.Polygon(x, y, star8), "eight-pointed-star", "compass", "compass-star");
            var hex = Sdf.StarPoints(6, 1f, 2f, M.Pi / 6);
            Add("hexagon", (x, y) => Sdf.Polygon(x, y, hex));
            var oct = Sdf.StarPoints(8, 1f, 2f, M.Pi / 8);
            Add("octagon", (x, y) => Sdf.Polygon(x, y, oct));
            Add("cross", (x, y) => M.Min(Sdf.Box(x, y, 0.26f, 0.95f), Sdf.Box(x, y, 0.95f, 0.26f)), "plus");
            Add("saltire-cross", (x, y) =>
            {
                float c = 0.70710678f;
                float u = (x + y) * c, v = (x - y) * c;
                return M.Min(Sdf.Box(u, v, 0.24f, 1.25f), Sdf.Box(u, v, 1.25f, 0.24f));
            }, "x");
            Add("crescent", (x, y) => M.Max(Sdf.Circle(x, y, 0.95f), -Sdf.Circle(x - 0.42f, y + 0.05f, 0.78f)), "moon");
            var rays = Sdf.StarPoints(12, 1f, 0.62f);
            Add("sun", (x, y) =>
            {
                float body = Sdf.Circle(x, y, 0.5f);
                float r = Sdf.Polygon(x, y, rays);
                // rays separated from the disc by a thin gap reads better when carved
                float ring = M.Abs(Sdf.Circle(x, y, 0.58f)) - 0.035f;
                return M.Min(body, M.Max(r, -ring));
            }, "sunburst");
            Add("heart", Heart);
            Add("arrow", (x, y) =>
            {
                var head = new[] { 0f, 1f, 0.45f, 0.4f, -0.45f, 0.4f };
                return M.Min(Sdf.Polygon(x, y, head), Sdf.Box(x, y + 0.3f, 0.11f, 0.72f));
            }, 0.45f, 1f, "spear");
            Add("valknut", Valknut, "valknute");
            Add("triskele", Fit(Triskele), "triskelion");
        }

        static void Add(string name, Func<float, float, float> f, params string[] aliases) => Add(name, f, 1, 1, aliases);

        static void Add(string name, Func<float, float, float> f, float hw, float hh, params string[] aliases)
        {
            Shapes[name] = new FuncShape(f, hw, hh);
            Aliases[Key(name)] = name;
            foreach (var a in aliases) Aliases[Key(a)] = name;
        }

        public static IEnumerable<string> Names => Shapes.Keys;

        static string Key(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s.ToLowerInvariant()) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>Looks up a shape by name or alias, ignoring case, spaces and hyphens.</summary>
        public static bool TryGet(string name, out IShape shape)
        {
            shape = null;
            return Aliases.TryGetValue(Key(name), out var real) && Shapes.TryGetValue(real, out shape);
        }

        static float Heart(float x, float y)
        {
            // iq's heart, rescaled to fit [-1,1]
            x = M.Abs(x) / 1.05f;
            y = (y + 1f) / 1.05f;
            float d;
            if (x + y > 1f)
            {
                float dx = x - 0.25f, dy = y - 0.75f;
                d = M.Sqrt(dx * dx + dy * dy) - 0.35355339f;
            }
            else
            {
                float a = x, b = y - 1f;
                float m = 0.5f * M.Max(a + y, 0f);
                float d1 = a * a + b * b;
                float d2 = (a - m) * (a - m) + (y - m) * (y - m);
                d = M.Sqrt(M.Min(d1, d2)) * (x > y ? 1 : -1);
            }
            return d * 1.05f;
        }

        /// <summary>Three interlocked triangle outlines, as on the Gotland picture stones.</summary>
        static readonly float[][] ValknutTris = MakeValknut();

        static float[][] MakeValknut()
        {
            // three upright triangles whose centroids sit on a small circle, then fitted to [-1,1]
            var tris = new float[3][];
            float minX = 9, maxX = -9, minY = 9, maxY = -9;
            for (int k = 0; k < 3; k++)
            {
                float a = M.Pi / 2 + k * 2 * M.Pi / 3;
                float ox = 0.36f * M.Cos(a), oy = 0.36f * M.Sin(a);
                var t = Sdf.StarPoints(3, 0.62f, 2f);
                for (int i = 0; i < t.Length; i += 2)
                {
                    t[i] += ox; t[i + 1] += oy;
                    minX = M.Min(minX, t[i]); maxX = M.Max(maxX, t[i]);
                    minY = M.Min(minY, t[i + 1]); maxY = M.Max(maxY, t[i + 1]);
                }
                tris[k] = t;
            }
            float cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            float sc = 0.92f * 2 / M.Max(maxX - minX, maxY - minY);
            foreach (var t in tris)
                for (int i = 0; i < t.Length; i += 2) { t[i] = (t[i] - cx) * sc; t[i + 1] = (t[i + 1] - cy) * sc; }
            return tris;
        }

        static float Valknut(float x, float y)
        {
            float d = float.MaxValue;
            foreach (var t in ValknutTris) d = M.Min(d, M.Abs(Sdf.Polygon(x, y, t)) - 0.07f);
            return d;
        }

        /// <summary>Measures a shape by sampling and rescales it so it is centred and fills [-1,1].</summary>
        static Func<float, float, float> Fit(Func<float, float, float> f, float margin = 0.97f)
        {
            float minX = 9, maxX = -9, minY = 9, maxY = -9;
            const int n = 200;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float x = -2 + 4f * (i + 0.5f) / n, y = -2 + 4f * (j + 0.5f) / n;
                    if (f(x, y) > 0) continue;
                    minX = M.Min(minX, x); maxX = M.Max(maxX, x); minY = M.Min(minY, y); maxY = M.Max(maxY, y);
                }
            float cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            float half = M.Max(maxX - minX, maxY - minY) / 2 / margin;
            return (x, y) => f(x * half + cx, y * half + cy) / half;
        }

        static float Triskele(float x, float y)
        {
            float d = Sdf.Circle(x, y, 0.16f);
            for (int k = 0; k < 3; k++)
            {
                float a = k * 2 * M.Pi / 3;
                float c = M.Cos(-a), s = M.Sin(-a);
                float u = x * c - y * s, v = x * s + y * c;
                // spiral arm: a half ring leaving the centre
                float arm = M.Abs(Sdf.Circle(u - 0.42f, v, 0.42f)) - 0.13f;
                arm = M.Max(arm, -v);
                d = M.Min(d, arm);
            }
            return d;
        }
    }
}
