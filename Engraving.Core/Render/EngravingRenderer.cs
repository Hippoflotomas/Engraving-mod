using System;
using System.Collections.Generic;
using System.Linq;
using Engraving.Core.Charges;
using Engraving.Core.Geometry;
using Engraving.Core.Language;

namespace Engraving.Core.Render
{
    /// <summary>Greyscale heightmap, row 0 at the top. 1 = surface, lower = cut deeper.</summary>
    public class HeightMap
    {
        public readonly int Width, Height;
        public readonly float[] H;
        public HeightMap(int w, int h) { Width = w; Height = h; H = new float[w * h]; }
        public float this[int x, int y] { get => H[y * Width + x]; set => H[y * Width + x] = value; }
    }

    public class RenderOptions
    {
        public float PixelsPerMetre = 256;
        /// <summary>Overrides the script's size (the mod passes the piece's real face size here).</summary>
        public float? Width, Height;
        public Levels Levels = new Levels();
        /// <summary>Hard cap on texture size along either axis.</summary>
        public int MaxPixels = 2048;
    }

    public class RenderResult
    {
        public HeightMap Map;
        public List<Diagnostic> Diagnostics = new List<Diagnostic>();
        public Box Field;
    }

    /// <summary>
    /// Turns an EngravingSpec into a heightmap. Everything is evaluated per pixel from signed distance
    /// fields in metres, so output is resolution-independent and anti-aliased by the bevels themselves.
    /// Pure C#, no Unity: the mod converts the heightmap to a normal map and texture.
    /// </summary>
    public static class EngravingRenderer
    {
        public static float BandWidth(BorderSpec b, float panelMin)
        {
            float w = Math.Min(0.22f, panelMin * 0.11f);
            if (b.Width == Engraving.Core.Language.BorderWidth.Narrow) w *= 0.65f;
            if (b.Width == Engraving.Core.Language.BorderWidth.Wide) w *= 1.4f;
            return w;
        }

        public static RenderResult Render(EngravingSpec spec, ChargeLibrary library, RenderOptions opt = null)
        {
            opt = opt ?? new RenderOptions();
            var lv = opt.Levels;
            var res = new RenderResult();
            float W = opt.Width ?? spec.Width, H = opt.Height ?? spec.Height;
            float ppm = opt.PixelsPerMetre;
            if (W * ppm > opt.MaxPixels) ppm = opt.MaxPixels / W;
            if (H * ppm > opt.MaxPixels) ppm = opt.MaxPixels / H;
            int pw = Math.Max(1, (int)Math.Round(W * ppm)), ph = Math.Max(1, (int)Math.Round(H * ppm));
            var map = new HeightMap(pw, ph);
            res.Map = map;

            // ---- borders and field rectangle
            float minDim = Math.Min(W, H);
            var bw = new float[4];
            var bs = new BorderSpec[4];
            for (int i = 0; i < 4; i++)
            {
                bs[i] = spec.Border((Side)i);
                bw[i] = bs[i] != null ? BandWidth(bs[i], minDim) : 0;
            }
            bool top = bs[0] != null, right = bs[1] != null, bottom = bs[2] != null, left = bs[3] != null;
            var field = new Box(bw[3], bw[2], W - bw[1], H - bw[0]);
            res.Field = field;

            // band extents: a band stops at a neighbouring band (meeting in a corner square), else runs to the panel edge
            var bands = new Band[4];
            for (int i = 0; i < 4; i++)
            {
                if (bs[i] == null) continue;
                var p = BandPainter.Create(bs[i]);
                var band = new Band { Side = (Side)i, Painter = p, Width = bw[i] };
                switch ((Side)i)
                {
                    case Side.Top:
                    case Side.Bottom:
                        band.Start = left ? bw[3] : 0; band.End = right ? W - bw[1] : W;
                        band.BevelStart = left; band.BevelEnd = right;
                        break;
                    case Side.Right:
                    case Side.Left:
                        band.Start = bottom ? bw[2] : 0; band.End = top ? H - bw[0] : H;
                        band.BevelStart = bottom; band.BevelEnd = top;
                        break;
                }
                // clockwise reading direction flips which end s=0 is on for right and bottom bands
                if ((Side)i == Side.Right || (Side)i == Side.Bottom)
                {
                    bool t = band.BevelStart; band.BevelStart = band.BevelEnd; band.BevelEnd = t;
                }
                p.Setup(band.End - band.Start, band.Width, lv);
                bands[i] = band;
            }

            // ---- charges
            var resolved = new List<(ChargeSpec, IShape)>();
            foreach (var c in spec.Charges)
            {
                var shape = library?.Resolve(c.Name);
                if (shape == null)
                {
                    res.Diagnostics.Add(new Diagnostic(Severity.Warning, c.SourceLine, $"no charge called '{c.Name}' (not a built-in shape, and no matching image found)"));
                    continue;
                }
                resolved.Add((c, shape));
            }
            var placements = Layout.Place(resolved, field);

            // the field is framed only where a border meets it; open edges continue onto the next panel
            var framed = new[] { top, right, bottom, left };

            // ---- per pixel
            for (int py = 0; py < ph; py++)
            {
                float y = H - (py + 0.5f) / ppm;
                for (int px = 0; px < pw; px++)
                {
                    float x = (px + 0.5f) / ppm;
                    float h;
                    if (field.Contains(x, y))
                        h = FieldHeight(x, y, field, framed, spec.Background, placements, lv);
                    else
                        h = BorderHeight(x, y, W, H, bw, bands, lv);
                    map.H[py * pw + px] = h;
                }
            }
            return res;
        }

        class Band
        {
            public Side Side;
            public BandPainter Painter;
            public float Width, Start, End;
            public bool BevelStart, BevelEnd;
        }

        /// <summary>Raises 'h' towards the surface near an edge: a flat fillet then a bevel down to the work.</summary>
        static float EdgeRise(float h, float distToEdge, Levels lv)
        {
            float f = M.Smooth(lv.FilletHalfWidth, lv.FilletHalfWidth + lv.Bevel, distToEdge);
            return M.Lerp(lv.Surface, h, f);
        }

        static float BorderHeight(float x, float y, float W, float H, float[] bw, Band[] bands, Levels lv)
        {
            // which band (or corner) owns this point?
            bool inTop = y >= H - bw[0] && bands[0] != null;
            bool inBottom = y < bw[2] && bands[2] != null;
            bool inLeft = x < bw[3] && bands[3] != null;
            bool inRight = x >= W - bw[1] && bands[1] != null;

            int corners = (inTop ? 1 : 0) + (inBottom ? 1 : 0) + (inLeft ? 1 : 0) + (inRight ? 1 : 0);
            if (corners >= 2)
            {
                // corner square: ring and boss on cut ground, bevelled where it meets the bands
                float cx0 = inLeft ? 0 : W - bw[1], cx1 = inLeft ? bw[3] : W;
                float cy0 = inBottom ? 0 : H - bw[0], cy1 = inBottom ? bw[2] : H;
                float ccx = (cx0 + cx1) * 0.5f, ccy = (cy0 + cy1) * 0.5f;
                float size = M.Min(cx1 - cx0, cy1 - cy0);
                float r = M.Len(x - ccx, y - ccy);
                float hh = lv.Ground;
                float ring = M.Abs(r - size * 0.3f) - size * 0.075f;
                hh = M.Max(hh, M.Lerp(lv.Ground, lv.Surface, M.Smooth(0, lv.Bevel, -ring)));
                float boss = r / (size * 0.14f);
                if (boss < 1) hh = M.Max(hh, M.Lerp(lv.Ground, lv.Surface, M.Sqrt(1 - boss * boss)));
                // inner edges of the corner square (towards the bands)
                float dIn = float.MaxValue;
                if (inLeft) dIn = M.Min(dIn, cx1 - x); else dIn = M.Min(dIn, x - cx0);
                if (inBottom) dIn = M.Min(dIn, cy1 - y); else dIn = M.Min(dIn, y - cy0);
                return EdgeRise(hh, dIn, lv);
            }

            Band b = inTop ? bands[0] : inBottom ? bands[2] : inLeft ? bands[3] : inRight ? bands[1] : null;
            if (b == null) return lv.Surface;   // strip beside a missing border (shouldn't happen)

            float s, t;   // s along (clockwise), t from inner edge outward
            switch (b.Side)
            {
                case Side.Top: s = x - b.Start; t = y - (H - b.Width); break;
                case Side.Right: s = b.End - y; t = x - (W - b.Width); break;
                case Side.Bottom: s = b.End - x; t = b.Width - y; break;
                default: s = y - b.Start; t = b.Width - x; break;
            }
            float L = b.End - b.Start;
            float h = b.Painter.Height(s, t);
            float d = t;
            if (b.BevelStart) d = M.Min(d, s);
            if (b.BevelEnd) d = M.Min(d, L - s);
            return EdgeRise(h, d, lv);
        }

        static float FieldHeight(float x, float y, Box field, bool[] framed, BackgroundKind bg, List<Placement> placements, Levels lv)
        {
            float ground = Background(x, y, bg, lv);
            float baseGround = bg == BackgroundKind.Plain ? lv.Surface : lv.Ground;
            float h = ground;

            foreach (var p in placements)
            {
                if (M.Abs(x - p.X) > p.Reach + lv.Moat || M.Abs(y - p.Y) > p.Reach + lv.Moat) continue;
                float d = p.Distance(x, y);
                switch (p.Spec.Mode)
                {
                    case CarveMode.Relief:
                        if (bg == BackgroundKind.Plain)
                        {
                            // outline relief: the figure stays at the surface, a moat is cut round it
                            float moat = M.Min(lv.Moat, p.Scale * 0.12f);
                            if (d < 0)
                                h = M.Lerp(lv.Ground, lv.Surface, M.Smooth(0, lv.Bevel, -d));
                            else if (d < moat)
                                h = M.Min(h, M.Lerp(lv.Ground, lv.Surface, M.Smooth(moat - lv.Bevel * 0.7f, moat, d)));
                        }
                        else if (d < 0)
                            h = M.Lerp(baseGround, lv.Surface, M.Smooth(0, lv.Bevel, -d));
                        break;
                    case CarveMode.Incised:
                    {
                        float hw = M.Min(lv.GrooveHalfWidth, p.Scale * 0.05f);
                        float ad = M.Abs(d);
                        if (ad < hw) h = M.Min(h, M.Lerp(lv.Groove, h, M.Smooth(0, hw, ad)));
                        break;
                    }
                    case CarveMode.Sunken:
                        if (d < 0)
                            h = M.Min(h, M.Lerp(baseGround, baseGround - lv.SinkDepth, M.Smooth(0, lv.Bevel, -d)));
                        break;
                }
            }

            // bevel up to the frame where the field meets a border or panel edge
            if (bg != BackgroundKind.Plain)
            {
                float dEdge = float.MaxValue;
                if (framed[0]) dEdge = M.Min(dEdge, field.Y1 - y);
                if (framed[1]) dEdge = M.Min(dEdge, field.X1 - x);
                if (framed[2]) dEdge = M.Min(dEdge, y - field.Y0);
                if (framed[3]) dEdge = M.Min(dEdge, x - field.X0);
                h = EdgeRise(h, dEdge, lv);
            }
            return h;
        }

        static float Background(float x, float y, BackgroundKind bg, Levels lv)
        {
            switch (bg)
            {
                case BackgroundKind.Recessed:
                    return lv.Ground;
                case BackgroundKind.Stippled:
                {
                    const float cell = 0.028f;
                    int ix = (int)Math.Floor(x / cell), iy = (int)Math.Floor(y / cell);
                    float best = float.MaxValue;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int cx = ix + dx, cy = iy + dy;
                            float jx = (cx + 0.2f + 0.6f * M.Hash(cx, cy, 1)) * cell;
                            float jy = (cy + 0.2f + 0.6f * M.Hash(cx, cy, 2)) * cell;
                            best = M.Min(best, M.Len(x - jx, y - jy));
                        }
                    float r = cell * 0.3f;
                    float pit = best < r ? M.Sqrt(1 - (best / r) * (best / r)) : 0;
                    return lv.Ground - 0.12f * pit;
                }
                case BackgroundKind.Hatched:
                {
                    const float period = 0.032f;
                    float u = (x + y) * 0.70710678f;
                    float d = M.Abs(M.Wrap(u, period));
                    float hw = period * 0.22f;
                    return d < hw ? lv.Ground - 0.12f * (1 - d / hw) : lv.Ground;
                }
                default:
                    return lv.Surface;
            }
        }
    }
}
