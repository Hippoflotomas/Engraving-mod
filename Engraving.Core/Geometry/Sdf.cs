using System;

namespace Engraving.Core.Geometry
{
    /// <summary>Small float maths helpers (net48 has no MathF / Math.Clamp).</summary>
    public static class M
    {
        public const float Pi = 3.14159265f;
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
        public static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float Smooth(float e0, float e1, float x)
        {
            float t = Clamp01((x - e0) / (e1 - e0));
            return t * t * (3 - 2 * t);
        }
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float Sin(float v) => (float)Math.Sin(v);
        public static float Cos(float v) => (float)Math.Cos(v);
        public static float Abs(float v) => v < 0 ? -v : v;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Len(float x, float y) => (float)Math.Sqrt(x * x + y * y);
        public static float Floor(float v) => (float)Math.Floor(v);
        public static float Round(float v) => (float)Math.Round(v);
        /// <summary>Wraps v into [-half, half) for period 2*half.</summary>
        public static float Wrap(float v, float period)
        {
            float r = v - period * Floor(v / period + 0.5f);
            return r;
        }
        /// <summary>Deterministic hash in [0,1).</summary>
        public static float Hash(int x, int y, int seed = 0)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
    }

    /// <summary>
    /// Signed distance primitives. Negative inside, positive outside, in whatever units the inputs use.
    /// Formulas follow Inigo Quilez's 2D distance functions.
    /// </summary>
    public static class Sdf
    {
        public static float Circle(float x, float y, float r) => M.Len(x, y) - r;

        public static float Box(float x, float y, float hx, float hy)
        {
            float dx = M.Abs(x) - hx, dy = M.Abs(y) - hy;
            float ox = M.Max(dx, 0), oy = M.Max(dy, 0);
            return M.Len(ox, oy) + M.Min(M.Max(dx, dy), 0);
        }

        /// <summary>Distance to segment a-b (unsigned).</summary>
        public static float Segment(float x, float y, float ax, float ay, float bx, float by)
        {
            float pax = x - ax, pay = y - ay, bax = bx - ax, bay = by - ay;
            float h = M.Clamp01((pax * bax + pay * bay) / (bax * bax + bay * bay + 1e-12f));
            return M.Len(pax - bax * h, pay - bay * h);
        }

        /// <summary>Signed distance to a closed polygon given as x0,y0,x1,y1,...</summary>
        public static float Polygon(float x, float y, float[] v)
        {
            int n = v.Length / 2;
            float dx0 = x - v[0], dy0 = y - v[1];
            float d = dx0 * dx0 + dy0 * dy0;
            float s = 1;
            for (int i = 0, j = n - 1; i < n; j = i, i++)
            {
                float vix = v[2 * i], viy = v[2 * i + 1], vjx = v[2 * j], vjy = v[2 * j + 1];
                float ex = vjx - vix, ey = vjy - viy;
                float wx = x - vix, wy = y - viy;
                float h = M.Clamp01((wx * ex + wy * ey) / (ex * ex + ey * ey));
                float bx = wx - ex * h, by = wy - ey * h;
                d = M.Min(d, bx * bx + by * by);
                bool c1 = y >= viy, c2 = y < vjy, c3 = ex * wy > ey * wx;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
            }
            return s * M.Sqrt(d);
        }

        /// <summary>Unsigned distance to an open polyline.</summary>
        public static float Polyline(float x, float y, float[] v)
        {
            float d = float.MaxValue;
            for (int i = 0; i + 3 < v.Length; i += 2)
                d = M.Min(d, Segment(x, y, v[i], v[i + 1], v[i + 2], v[i + 3]));
            return d;
        }

        /// <summary>Regular star / polygon vertices: points alternate outer and inner radius. inner == outer gives a regular polygon.</summary>
        public static float[] StarPoints(int points, float outer, float inner, float rotation = 0)
        {
            bool regular = inner >= outer;
            int n = regular ? points : points * 2;
            var v = new float[n * 2];
            for (int i = 0; i < n; i++)
            {
                float a = M.Pi / 2 + rotation + i * 2 * M.Pi / n;
                float r = regular || i % 2 == 0 ? outer : inner;
                v[2 * i] = r * M.Cos(a);
                v[2 * i + 1] = r * M.Sin(a);
            }
            return v;
        }
    }
}
