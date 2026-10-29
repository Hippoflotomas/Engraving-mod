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
            Add("triskele", Baked(Fit(Triskele)), "triskelion");
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

        /// <summary>
        /// For shapes that are slow to evaluate (many segments): the distance is worked out once on a grid over
        /// [-1.6, 1.6], on first use, then read back with bilinear filtering. Beyond the grid, the distance from the
        /// grid's edge is added on (shapes fit in [-1, 1], so that's far outside anything drawn).
        /// 2026-10-29: the spiral triskele took a panel from 0.2 s to 3 s unbaked.
        /// </summary>
        static Func<float, float, float> Baked(Func<float, float, float> f, int res = 320)
        {
            const float Extent = 1.6f;
            float[] grid = null;
            object gate = new object();
            return (x, y) =>
            {
                if (grid == null)
                    lock (gate)
                        if (grid == null)
                        {
                            var g = new float[res * res];
                            for (int j = 0; j < res; j++)
                                for (int i = 0; i < res; i++)
                                    g[j * res + i] = f(-Extent + 2 * Extent * i / (res - 1), -Extent + 2 * Extent * j / (res - 1));
                            grid = g;
                        }
                float cx = M.Clamp(x, -Extent, Extent), cy = M.Clamp(y, -Extent, Extent);
                float outside = M.Len(x - cx, y - cy);
                float fx = (cx + Extent) / (2 * Extent) * (res - 1), fy = (cy + Extent) / (2 * Extent) * (res - 1);
                int ix = Math.Min((int)fx, res - 2), iy = Math.Min((int)fy, res - 2);
                float tx = fx - ix, ty = fy - iy;
                int o = iy * res + ix;
                return M.Lerp(M.Lerp(grid[o], grid[o + 1], tx), M.Lerp(grid[o + res], grid[o + res + 1], tx), ty) + outside;
            };
        }

        /// <summary>
        /// Measures a shape by sampling and rescales it so it is centred and fills [-1,1]. The measuring happens on
        /// first use, not when the shape table is built, so slow shapes don't delay every other shape.
        /// </summary>
        static Func<float, float, float> Fit(Func<float, float, float> f, float margin = 0.97f)
        {
            float cx = 0, cy = 0, half = 0;
            bool measured = false;
            object gate = new object();
            return (x, y) =>
            {
                if (!measured)
                    lock (gate)
                        if (!measured)
                        {
                            float minX = 9, maxX = -9, minY = 9, maxY = -9;
                            const int n = 160;
                            for (int j = 0; j < n; j++)
                                for (int i = 0; i < n; i++)
                                {
                                    float sx = -2 + 4f * (i + 0.5f) / n, sy = -2 + 4f * (j + 0.5f) / n;
                                    if (f(sx, sy) > 0) continue;
                                    minX = M.Min(minX, sx); maxX = M.Max(maxX, sx); minY = M.Min(minY, sy); maxY = M.Max(maxY, sy);
                                }
                            cx = (minX + maxX) / 2; cy = (minY + maxY) / 2;
                            half = M.Max(maxX - minX, maxY - minY) / 2 / margin;
                            measured = true;
                        }
                return f(x * half + cx, y * half + cy) / half;
            };
        }

        /// <summary>
        /// Triple spiral: three arms leave the centre, each sweeping out and curling inward around its own eye,
        /// tapering to a point (Hippo, 2026-10-29: "the ends of the arms spiral around more and taper in").
        /// Each arm is a polyline with a width per point; the distance is to the nearest swept circle.
        /// </summary>
        static readonly (float[] pts, float[] radius)[] TriskeleArms = MakeTriskele();

        static (float[], float[])[] MakeTriskele()
        {
            const int N = 90;
            const float Eye = 0.5f;          // distance from the centre to each arm's spiral eye
            const float Turns = 1.35f;       // how far each arm curls round its eye
            var arms = new (float[], float[])[3];
            for (int k = 0; k < 3; k++)
            {
                float a = M.Pi / 2 + k * 2 * M.Pi / 3;
                float px = Eye * M.Cos(a), py = Eye * M.Sin(a);
                // the arm starts at the centre, so its first angle round the eye points back at the centre
                float phi0 = (float)Math.Atan2(-py, -px);
                var pts = new float[N * 2];
                var rad = new float[N];
                for (int i = 0; i < N; i++)
                {
                    float t = i / (float)(N - 1);
                    float rho = Eye * (float)Math.Pow(1 - t, 1.15) + 0.02f * t;   // spirals in towards the eye
                    float phi = phi0 - t * Turns * 2 * M.Pi;                       // the same way round for every arm
                    pts[2 * i] = px + rho * M.Cos(phi);
                    pts[2 * i + 1] = py + rho * M.Sin(phi);
                    rad[i] = 0.115f * (float)Math.Pow(1 - t, 0.75) + 0.008f;         // tapers to a point
                }
                arms[k] = (pts, rad);
            }
            return arms;
        }

        static float Triskele(float x, float y)
        {
            float d = Sdf.Circle(x, y, 0.13f);   // the hub where the arms meet
            foreach (var (pts, rad) in TriskeleArms)
            {
                for (int i = 0; i + 1 < rad.Length; i++)
                {
                    float ax = pts[2 * i], ay = pts[2 * i + 1], bx = pts[2 * i + 2], by = pts[2 * i + 3];
                    float abx = bx - ax, aby = by - ay;
                    float h = M.Clamp01(((x - ax) * abx + (y - ay) * aby) / (abx * abx + aby * aby + 1e-12f));
                    float dist = M.Len(x - ax - abx * h, y - ay - aby * h) - M.Lerp(rad[i], rad[i + 1], h);
                    if (dist < d) d = dist;
                }
            }
            return d;
        }
    }
}
