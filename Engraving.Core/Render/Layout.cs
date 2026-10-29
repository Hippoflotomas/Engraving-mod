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
            return result;
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
