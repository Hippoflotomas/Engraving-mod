using System;
using System.Collections.Generic;
using Engraving.Core.Geometry;
using Engraving.Core.Language;
using Engraving.Core.Runes;

namespace Engraving.Core.Render
{
    /// <summary>
    /// Paints one border band in band space: s runs along the band (0..Length) in reading/clockwise
    /// direction, t runs across it from the inner edge (0) to the panel edge (Width). Heights 0..1.
    /// Edge bevels are applied by the renderer, so painters fill the whole rectangle.
    /// </summary>
    public abstract class BandPainter
    {
        protected float L, W;
        protected Levels Lv;

        public void Setup(float length, float width, Levels lv)
        {
            L = length; W = width; Lv = lv;
            Prepare();
        }

        protected virtual void Prepare() { }

        public abstract float Height(float s, float t);

        /// <summary>The level the band sits at where nothing is drawn (used for edge bevels).</summary>
        public virtual float BaseLevel => Lv.Ground;

        /// <summary>Picks a repeat count so the pattern tiles exactly along the band (seamless across panels).</summary>
        protected static int Repeats(float length, float nominal) => Math.Max(1, (int)Math.Round(length / nominal));

        /// <summary>Flat-topped raised ribbon with rounded shoulders. d = distance from centre line.</summary>
        protected float Ribbon(float d, float halfWidth)
        {
            if (d >= halfWidth) return Lv.Ground;
            return M.Lerp(Lv.Ground, Lv.Surface, M.Smooth(0, Lv.Bevel, halfWidth - d));
        }

        /// <summary>V-cut groove centred on d = 0, lowering 'h'.</summary>
        protected float Groove(float h, float d, float halfWidth)
        {
            if (d >= halfWidth) return h;
            return M.Min(h, M.Lerp(Lv.Groove, h, M.Smooth(0, halfWidth, d)));
        }

        public static BandPainter Create(BorderSpec b)
        {
            switch (b.Style)
            {
                case BorderStyle.Knotwork: return new KnotworkBand(b.Strands);
                case BorderStyle.RingChain: return new RingChainBand();
                case BorderStyle.Runes: return new RuneBand(b.Text, b.Lines);
                case BorderStyle.Lines: return new LinesBand(b.Lines);
                case BorderStyle.Beads: return new BeadsBand();
                case BorderStyle.Zigzag: return new ZigzagBand();
            }
            return null;
        }
    }

    /// <summary>
    /// A regular N-strand plait. Strands are 45° lines reflected at the band edges; at each crossing
    /// over/under alternates along every strand (decided by lattice parity). Strands carry a median groove,
    /// as on Mammen/Ringerike work.
    /// </summary>
    public sealed class KnotworkBand : BandPainter
    {
        readonly int _k;
        float _cu, _cv, _margin, _c;
        const float HalfRibbon = 0.30f;   // lattice units (strand spacing is 1.414)
        const float Gap = 0.12f;

        public KnotworkBand(int strands) { _k = Math.Max(2, strands); }

        protected override void Prepare()
        {
            // margin keeps the rounded turns inside the band
            float approxC = W / (_k + 2 * HalfRibbon + 0.2f);
            _margin = approxC * (HalfRibbon + 0.1f);
            _cv = (W - 2 * _margin) / _k;
            _cu = L / (2 * Repeats(L, 2 * _cv));
            _c = (_cu + _cv) * 0.5f;
        }

        public override float Height(float s, float t)
        {
            float u = s / _cu, v = (t - _margin) / _cv;
            float r = HalfRibbon;

            // beyond the band edges only the rounded turns exist
            if (v < 0 || v > _k)
            {
                float ev = v < 0 ? 0 : _k;
                // turn points: integer i with i + ev odd
                float i0 = M.Floor(u);
                float best = float.MaxValue;
                for (int di = -1; di <= 2; di++)
                {
                    int i = (int)i0 + di;
                    if (((i + (int)ev) & 1) == 0) continue;
                    best = M.Min(best, M.Len(u - i, v - ev));
                }
                return StrandProfile(best, r);
            }

            float dp = M.Abs(M.Wrap(u - v - 1, 2)) * 0.70710678f;
            float dm = M.Abs(M.Wrap(u + v - 1, 2)) * 0.70710678f;

            // nearest lattice event (crossing or edge turn): integer (i,j), i+j odd, 0<=j<=k
            int bi = 0, bj = 0;
            float bd = float.MaxValue;
            int ui = (int)M.Floor(u), vj = (int)M.Floor(v);
            for (int di = -1; di <= 2; di++)
                for (int dj = -1; dj <= 2; dj++)
                {
                    int i = ui + di, j = vj + dj;
                    if (j < 0 || j > _k || ((i + j) & 1) == 0) continue;
                    float d = M.Len(u - i, v - j);
                    if (d < bd) { bd = d; bi = i; bj = j; }
                }

            if (bj == 0 || bj == _k)
                return M.Max(StrandProfile(dp, r), StrandProfile(dm, r));

            bool plusOnTop = (bi & 1) != 0;
            float dTop = plusOnTop ? dp : dm;
            float dUnder = plusOnTop ? dm : dp;
            if (dTop < r) return StrandProfile(dTop, r);
            if (dUnder < r)
            {
                if (dTop < r + Gap) return Lv.Ground;
                return StrandProfile(dUnder, r);
            }
            return Lv.Ground;
        }

        float StrandProfile(float dLattice, float rLattice)
        {
            float d = dLattice * _c, hw = rLattice * _c;
            float h = Ribbon(d, hw);
            if (d < hw) h = Groove(h, d, hw * 0.2f);
            return h;
        }
    }

    /// <summary>
    /// Borre-style ring-chain: two strands twisting into a chain of eyes, a ring in each eye and a
    /// lozenge clasp over each crossing.
    /// </summary>
    public sealed class RingChainBand : BandPainter
    {
        float _p, _a, _r, _mid, _ringR, _ringW, _gap;

        protected override void Prepare()
        {
            _mid = W * 0.5f;
            _a = W * 0.28f;          // strand amplitude
            _r = W * 0.075f;         // strand half width
            _gap = W * 0.045f;
            _ringR = _a - _r - _gap - W * 0.055f;
            _ringW = W * 0.05f;
            _p = L / (2 * Repeats(L, 2 * W * 0.95f)); // eye spacing; alternation repeats every 2 eyes
        }

        float StrandDist(float s, float t, float sign)
        {
            float k = M.Pi / _p;
            float f = _mid + sign * _a * M.Cos(k * s);
            float df = -sign * _a * k * M.Sin(k * s);
            return M.Abs(t - f) / M.Sqrt(1 + df * df);
        }

        float Lozenge(float s, float t, float sc)
        {
            // diamond centred on the crossing: half-diagonals along s and t
            float hs = W * 0.12f, ht = W * 0.2f;
            return (M.Abs(s - sc) / hs + M.Abs(t - _mid) / ht - 1) * M.Min(hs, ht) * 0.7f;
        }

        public override float Height(float s, float t)
        {
            float dA = StrandDist(s, t, 1), dB = StrandDist(s, t, -1);
            // eyes sit at s = k*p, crossings halfway between
            int crossing = (int)M.Round(s / _p - 0.5f);
            float sc = (crossing + 0.5f) * _p;

            float lz = Lozenge(s, t, sc);
            if (lz < 0)
            {
                float h = M.Lerp(Lv.Ground, Lv.Surface, M.Smooth(0, Lv.Bevel, -lz));
                return Groove(h, M.Abs(lz + W * 0.045f), W * 0.012f);
            }
            if (lz < _gap * 0.8f) return Lv.Ground;

            bool aTop = (crossing & 1) == 0;
            float dTop = aTop ? dA : dB, dUnder = aTop ? dB : dA;
            float hs;
            if (dTop < _r) hs = Ribbon(dTop, _r);
            else if (dUnder < _r && dTop >= _r + _gap) hs = Ribbon(dUnder, _r);
            else hs = Lv.Ground;

            // ring in the eye
            float eye = M.Round(s / _p) * _p;
            float dr = M.Abs(M.Len(s - eye, t - _mid) - _ringR);
            float hr = Ribbon(dr, _ringW);
            return M.Max(hs, hr);
        }
    }

    /// <summary>An inscription in Elder Futhark between incised rails. Band stays at the surface; letters are V-cut.</summary>
    public sealed class RuneBand : BandPainter
    {
        readonly string _runes;
        readonly int _lines;
        struct Placed { public RuneGlyph G; public bool Divider; public float S0, S1; }
        readonly List<Placed> _placed = new List<Placed>();
        float _letterH, _t0, _sx, _stroke;
        readonly List<float> _rails = new List<float>();

        public RuneBand(string text, int lines)
        {
            _runes = Futhark.Transliterate(text ?? "");
            _lines = lines;
        }

        public override float BaseLevel => Lv.Surface;

        protected override void Prepare()
        {
            _rails.Clear();
            float railStep = W * 0.075f;
            int outer = (_lines + 1) / 2, inner = _lines / 2;
            float pad = W * 0.09f;
            for (int i = 0; i < inner; i++) _rails.Add(pad + i * railStep);
            for (int i = 0; i < outer; i++) _rails.Add(W - pad - i * railStep);
            float lo = inner > 0 ? pad + (inner - 1) * railStep : 0;
            float hi = outer > 0 ? W - pad - (outer - 1) * railStep : W;
            float room = hi - lo;
            _letterH = room * 0.72f;
            _t0 = lo + (room - _letterH) * 0.5f;
            _stroke = M.Max(_letterH * 0.065f, 0.004f);

            float gap = _letterH * 0.3f, divW = _letterH * 0.16f;
            float total = 0;
            foreach (char c in _runes)
                total += (c == Futhark.Divider ? divW : (Futhark.TryGet(c, out var g) ? g.Width * _letterH : 0)) + gap;
            total -= gap;
            float avail = L * 0.96f;
            _sx = total > avail ? avail / total : 1f;
            float s = (L - total * _sx) * 0.5f;
            _placed.Clear();
            foreach (char c in _runes)
            {
                if (c == Futhark.Divider)
                {
                    _placed.Add(new Placed { Divider = true, S0 = s, S1 = s + divW * _sx });
                    s += (divW + gap) * _sx;
                }
                else if (Futhark.TryGet(c, out var g))
                {
                    _placed.Add(new Placed { G = g, S0 = s, S1 = s + g.Width * _letterH * _sx });
                    s += (g.Width * _letterH + gap) * _sx;
                }
            }
        }

        public override float Height(float s, float t)
        {
            float h = Lv.Surface;
            foreach (var r in _rails) h = Groove(h, M.Abs(t - r), _stroke * 1.1f);
            if (t < _t0 - _stroke * 2 || t > _t0 + _letterH + _stroke * 2) return h;

            foreach (var p in _placed)
            {
                if (s < p.S0 - _stroke * 2 || s > p.S1 + _stroke * 2) continue;
                if (p.Divider)
                {
                    float cx = (p.S0 + p.S1) * 0.5f;
                    float d1 = M.Len(s - cx, t - (_t0 + _letterH * 0.33f));
                    float d2 = M.Len(s - cx, t - (_t0 + _letterH * 0.67f));
                    h = Groove(h, M.Min(d1, d2), _letterH * 0.08f);
                    continue;
                }
                // to glyph space (glyph units: height 1)
                float gx = (s - p.S0) / (_letterH * _sx), gy = (t - _t0) / _letterH;
                float best = float.MaxValue;
                foreach (var stroke in p.G.Strokes)
                    for (int i = 0; i + 3 < stroke.Length; i += 2)
                    {
                        // segments evaluated in metres so squeezed text keeps an even line width
                        float ax = stroke[i] * _letterH * _sx, ay = stroke[i + 1] * _letterH;
                        float bx = stroke[i + 2] * _letterH * _sx, by = stroke[i + 3] * _letterH;
                        best = M.Min(best, Sdf.Segment(gx * _letterH * _sx, gy * _letterH, ax, ay, bx, by));
                    }
                h = Groove(h, best, _stroke);
            }
            return h;
        }
    }

    /// <summary>Parallel incised lines along the band.</summary>
    public sealed class LinesBand : BandPainter
    {
        readonly int _n;
        public LinesBand(int n) { _n = Math.Max(1, n); }
        public override float BaseLevel => Lv.Surface;
        public override float Height(float s, float t)
        {
            float h = Lv.Surface;
            float hw = M.Min(W / (_n * 4f), Lv.GrooveHalfWidth * 1.4f);
            for (int i = 0; i < _n; i++)
                h = Groove(h, M.Abs(t - W * (i + 0.5f) / _n), hw);
            return h;
        }
    }

    /// <summary>A row of domed pellets on a cut-down ground.</summary>
    public sealed class BeadsBand : BandPainter
    {
        float _p;
        protected override void Prepare() => _p = L / Repeats(L, W * 0.8f);
        public override float Height(float s, float t)
        {
            float cs = (M.Floor(s / _p) + 0.5f) * _p;
            float d = M.Len(s - cs, t - W * 0.5f);
            float r = M.Min(_p, W) * 0.33f;
            if (d >= r) return Lv.Ground;
            float dome = M.Sqrt(1 - (d / r) * (d / r));
            return M.Lerp(Lv.Ground, Lv.Surface, dome);
        }
    }

    /// <summary>A raised zigzag ribbon between two rails ("wolf's teeth").</summary>
    public sealed class ZigzagBand : BandPainter
    {
        float _p;
        protected override void Prepare() => _p = L / Repeats(L, W * 1.1f);
        public override float Height(float s, float t)
        {
            float railW = W * 0.07f;
            float inner = W * 0.2f, outer = W * 0.8f;
            // triangle wave between inner and outer
            float ph = s / _p - M.Floor(s / _p);
            float tri = ph < 0.5f ? ph * 2 : 2 - ph * 2;
            float slope = (outer - inner) * 2 / _p;
            float d = M.Abs(t - (inner + (outer - inner) * tri)) / M.Sqrt(1 + slope * slope);
            float h = Ribbon(d, W * 0.07f);
            h = M.Max(h, Ribbon(M.Abs(t - railW), railW * 0.6f));
            h = M.Max(h, Ribbon(M.Abs(t - (W - railW)), railW * 0.6f));
            return h;
        }
    }
}
