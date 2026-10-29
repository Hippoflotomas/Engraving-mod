using System;

namespace Engraving.Core.Geometry
{
    /// <summary>A coverage mask (0..1), row 0 at the TOP like image files.</summary>
    public sealed class AlphaMask
    {
        public readonly int Width, Height;
        public readonly float[] Alpha;
        /// <summary>Optional luminance (0..1) for interior detail; null if the source was a pure silhouette.</summary>
        public float[] Luma;

        public AlphaMask(int w, int h)
        {
            Width = w; Height = h;
            Alpha = new float[w * h];
        }

        public float this[int x, int y] => Alpha[y * Width + x];
    }

    /// <summary>
    /// Turns an image silhouette into a signed distance field so it can be bevelled, outlined
    /// and sunk exactly like the built-in shapes. Uses the Felzenszwalb-Huttenlocher exact EDT.
    /// </summary>
    public sealed class ImageShape : IShape
    {
        readonly float[] _sdf;   // in mask pixels, negative inside
        readonly int _w, _h;
        readonly float _unitsPerPixel;
        readonly int _x0, _y0;   // crop offset into the padded field

        public float HalfWidth { get; }
        public float HalfHeight { get; }

        public ImageShape(AlphaMask mask, float threshold = 0.5f)
        {
            // crop to content so placement isn't thrown off by transparent padding
            int minX = mask.Width, minY = mask.Height, maxX = -1, maxY = -1;
            for (int y = 0; y < mask.Height; y++)
                for (int x = 0; x < mask.Width; x++)
                    if (mask[x, y] >= threshold)
                    {
                        if (x < minX) minX = x; if (x > maxX) maxX = x;
                        if (y < minY) minY = y; if (y > maxY) maxY = y;
                    }
            if (maxX < 0) { minX = minY = 0; maxX = maxY = 0; }

            const int pad = 4;
            _w = maxX - minX + 1 + pad * 2;
            _h = maxY - minY + 1 + pad * 2;
            _x0 = minX - pad; _y0 = minY - pad;

            var inside = new bool[_w * _h];
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _w; x++)
                {
                    int sx = x + _x0, sy = y + _y0;
                    inside[y * _w + x] = sx >= 0 && sy >= 0 && sx < mask.Width && sy < mask.Height && mask[sx, sy] >= threshold;
                }

            var dOut = Edt(inside, _w, _h, true);   // distance from outside pixels to the shape
            var dIn = Edt(inside, _w, _h, false);   // distance from inside pixels to the background
            _sdf = new float[_w * _h];
            for (int i = 0; i < _sdf.Length; i++)
                _sdf[i] = inside[i] ? -(M.Sqrt(dIn[i]) - 0.5f) : M.Sqrt(dOut[i]) - 0.5f;

            int cw = maxX - minX + 1, ch = maxY - minY + 1;
            float longest = Math.Max(cw, ch);
            _unitsPerPixel = 2f / longest;
            HalfWidth = cw / longest;
            HalfHeight = ch / longest;
        }

        public float Distance(float x, float y)
        {
            // local units -> padded field pixels (row 0 at top)
            float px = (x + HalfWidth) / _unitsPerPixel + 4 - 0.5f;
            float py = (HalfHeight - y) / _unitsPerPixel + 4 - 0.5f;
            float cx = M.Clamp(px, 0, _w - 1), cy = M.Clamp(py, 0, _h - 1);
            int ix = (int)cx, iy = (int)cy;
            int ix1 = Math.Min(ix + 1, _w - 1), iy1 = Math.Min(iy + 1, _h - 1);
            float fx = cx - ix, fy = cy - iy;
            float a = _sdf[iy * _w + ix], b = _sdf[iy * _w + ix1], c = _sdf[iy1 * _w + ix], d = _sdf[iy1 * _w + ix1];
            float v = M.Lerp(M.Lerp(a, b, fx), M.Lerp(c, d, fx), fy);
            // outside the padded field, add the distance to it
            float ox = px - cx, oy = py - cy;
            if (ox != 0 || oy != 0) v += M.Len(ox, oy);
            return v * _unitsPerPixel;
        }

        // squared EDT; when 'toShape' is true, feature pixels are the inside ones
        static float[] Edt(bool[] inside, int w, int h, bool toShape)
        {
            const float Inf = 1e20f;
            var f = new float[w * h];
            for (int i = 0; i < f.Length; i++) f[i] = inside[i] == toShape ? 0 : Inf;
            int n = Math.Max(w, h);
            var col = new float[n];
            var outv = new float[n];
            var v = new int[n];
            var z = new float[n + 1];
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++) col[y] = f[y * w + x];
                Edt1D(col, h, outv, v, z);
                for (int y = 0; y < h; y++) f[y * w + x] = outv[y];
            }
            for (int y = 0; y < h; y++)
            {
                Array.Copy(f, y * w, col, 0, w);
                Edt1D(col, w, outv, v, z);
                Array.Copy(outv, 0, f, y * w, w);
            }
            return f;
        }

        static void Edt1D(float[] f, int n, float[] d, int[] v, float[] z)
        {
            int k = 0;
            v[0] = 0;
            z[0] = float.NegativeInfinity;
            z[1] = float.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                float s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2 * q - 2 * v[k]);
                while (s <= z[k])
                {
                    k--;
                    s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2 * q - 2 * v[k]);
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = float.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                float dq = q - v[k];
                d[q] = dq * dq + f[v[k]];
            }
        }
    }
}
