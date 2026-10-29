using System;
using System.Collections.Generic;
using System.Linq;

namespace Engraving.Core.Language
{
    public class ParseResult
    {
        public EngravingSpec Spec;
        public List<Diagnostic> Diagnostics = new List<Diagnostic>();
        public bool HasErrors => Diagnostics.Any(d => d.Severity == Severity.Error);
    }

    /// <summary>
    /// Parses the engraving language. One statement per line (or separated by ';' / '.').
    /// The parser is forgiving: filler words are skipped and problems are reported as diagnostics
    /// rather than exceptions, so a half-typed script still renders what it can.
    /// See docs/LANGUAGE.md for the grammar.
    /// </summary>
    public class Parser
    {
        static readonly Dictionary<string, int> CountWords = new Dictionary<string, int>
        {
            ["a"] = 1, ["an"] = 1, ["one"] = 1, ["single"] = 1, ["two"] = 2, ["pair"] = 2, ["three"] = 3, ["four"] = 4,
            ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10,
            ["eleven"] = 11, ["twelve"] = 12,
        };

        static readonly HashSet<string> Filler = new HashSet<string> { "the", "of", "with", "on", "at", "along", "and", "is", "are" };

        List<Token> _t;
        int _p;
        ParseResult _r;
        EngravingSpec _s;

        public static ParseResult Parse(string source)
        {
            var parser = new Parser();
            return parser.Run(source ?? "");
        }

        ParseResult Run(string source)
        {
            _r = new ParseResult();
            _s = new EngravingSpec();
            _r.Spec = _s;
            _t = Lexer.Tokenize(source, _r.Diagnostics);
            _p = 0;

            var runeGroups = new List<(BorderSpec[] sides, string text)>();

            while (_p < _t.Count)
            {
                // collect one statement
                int start = _p;
                while (_t[_p].Kind != TokenKind.End) _p++;
                var stmt = _t.GetRange(start, _p - start);
                _p++;
                if (stmt.Count == 0) continue;
                try
                {
                    ParseStatement(stmt, runeGroups);
                }
                catch (StatementError e)
                {
                    Error(stmt[0].Line, e.Message);
                }
            }

            SplitInscriptions(runeGroups);
            return _r;
        }

        class StatementError : Exception { public StatementError(string m) : base(m) { } }

        void Error(int line, string msg) => _r.Diagnostics.Add(new Diagnostic(Severity.Error, line, msg));
        void Warn(int line, string msg) => _r.Diagnostics.Add(new Diagnostic(Severity.Warning, line, msg));

        void ParseStatement(List<Token> st, List<(BorderSpec[] sides, string text)> runeGroups)
        {
            var head = st[0];
            if (head.Kind == TokenKind.Word)
            {
                switch (head.Text)
                {
                    case "size":
                    case "panel":
                        ParseSize(st);
                        return;
                    case "background":
                    case "ground":
                    case "field":
                        ParseBackground(st);
                        return;
                    case "border":
                    case "borders":
                    case "band":
                    case "bands":
                        ParseBorder(st, runeGroups);
                        return;
                }
            }
            ParseCharges(st);
        }

        // ---------------------------------------------------------------- size

        void ParseSize(List<Token> st)
        {
            var nums = st.Where(t => t.Kind == TokenKind.Number).Select(t => t.Number).ToList();
            if (nums.Count == 1) nums.Add(nums[0]);
            if (nums.Count < 2) throw new StatementError("size needs a width and height, e.g. 'size 2 by 2'");
            if (nums[0] <= 0 || nums[1] <= 0 || nums[0] > 16 || nums[1] > 16)
                throw new StatementError("size must be between 0 and 16 metres");
            _s.Width = nums[0];
            _s.Height = nums[1];
            _s.SizeGiven = true;
        }

        // ---------------------------------------------------------------- background

        void ParseBackground(List<Token> st)
        {
            foreach (var t in st.Skip(1))
            {
                if (t.Kind != TokenKind.Word) continue;
                switch (t.Text)
                {
                    case "plain": case "flat": case "smooth": case "none":
                        _s.Background = BackgroundKind.Plain; return;
                    case "recessed": case "sunk": case "sunken": case "cut": case "lowered": case "carved":
                        _s.Background = BackgroundKind.Recessed; return;
                    case "stippled": case "dotted": case "pitted": case "punched":
                        _s.Background = BackgroundKind.Stippled; return;
                    case "hatched": case "lined": case "striped":
                        _s.Background = BackgroundKind.Hatched; return;
                }
            }
            throw new StatementError("background can be plain, recessed, stippled or hatched");
        }

        // ---------------------------------------------------------------- borders

        static bool TrySide(string w, List<Side> sides)
        {
            switch (w)
            {
                case "all": case "around": case "everywhere": case "each": case "every":
                    sides.AddRange(new[] { Side.Top, Side.Right, Side.Bottom, Side.Left }); return true;
                case "top": case "upper": sides.Add(Side.Top); return true;
                case "bottom": case "lower": sides.Add(Side.Bottom); return true;
                case "left": sides.Add(Side.Left); return true;
                case "right": sides.Add(Side.Right); return true;
                case "sides": sides.Add(Side.Left); sides.Add(Side.Right); return true;
                case "ends": sides.Add(Side.Top); sides.Add(Side.Bottom); return true;
                case "side": case "sided": case "edges": case "edge": return true; // "all sides", "both edges"
                case "both": return true;
            }
            return false;
        }

        void ParseBorder(List<Token> st, List<(BorderSpec[] sides, string text)> runeGroups)
        {
            int line = st[0].Line;
            var sides = new List<Side>();
            int i = 1;
            // sides come before the style (optionally ended by a colon)
            for (; i < st.Count; i++)
            {
                var t = st[i];
                if (t.Kind == TokenKind.Colon) { i++; break; }
                if (t.Kind == TokenKind.Comma) continue;
                if (t.Kind != TokenKind.Word) break;
                if (Filler.Contains(t.Text)) continue;
                if (!TrySide(t.Text, sides)) break;
            }
            if (sides.Count == 0) sides.AddRange(new[] { Side.Top, Side.Right, Side.Bottom, Side.Left });
            sides = sides.Distinct().ToList();

            var proto = new BorderSpec { SourceLine = line };
            bool styleSet = false;
            string text = null;

            for (; i < st.Count; i++)
            {
                var t = st[i];
                if (t.Kind == TokenKind.Comma || t.Kind == TokenKind.Colon) continue;
                if (t.Kind == TokenKind.String) { text = t.Text; if (!styleSet) { proto.Style = BorderStyle.Runes; styleSet = true; } continue; }
                if (t.Kind == TokenKind.Number)
                {
                    // "3 lines", "4 strands", "between 2 lines"
                    string next = i + 1 < st.Count && st[i + 1].Kind == TokenKind.Word ? st[i + 1].Text : "";
                    int n = (int)t.Number;
                    if (next.StartsWith("strand")) { proto.Strands = Clamp(n, 2, 6); i++; continue; }
                    if (next.StartsWith("line") || next.StartsWith("rail"))
                    {
                        proto.Lines = Clamp(n, 0, 8);
                        if (!styleSet) { proto.Style = BorderStyle.Lines; styleSet = true; }
                        i++; continue;
                    }
                    Warn(line, $"don't know what the number {t.Text} refers to here");
                    continue;
                }
                if (CountWords.TryGetValue(t.Text, out int cw) && i + 1 < st.Count && st[i + 1].Kind == TokenKind.Word)
                {
                    string next = st[i + 1].Text;
                    if (next.StartsWith("strand")) { proto.Strands = Clamp(cw, 2, 6); i++; continue; }
                    if (next.StartsWith("line") || next.StartsWith("rail"))
                    {
                        proto.Lines = Clamp(cw, 0, 8);
                        if (!styleSet) { proto.Style = BorderStyle.Lines; styleSet = true; }
                        i++; continue;
                    }
                }
                switch (t.Text)
                {
                    case "knotwork": case "knot": case "knots": case "plait": case "plaited": case "interlace": case "braid": case "braided":
                        proto.Style = BorderStyle.Knotwork; styleSet = true; break;
                    case "ring-chain": case "ringchain": case "borre": case "rings": case "chain":
                        proto.Style = BorderStyle.RingChain; styleSet = true; break;
                    case "ring":
                        if (i + 1 < st.Count && st[i + 1].Text == "chain") i++;
                        proto.Style = BorderStyle.RingChain; styleSet = true; break;
                    case "runes": case "rune": case "inscription": case "text": case "letters": case "writing":
                        proto.Style = BorderStyle.Runes; styleSet = true; break;
                    case "line": case "lines": case "rails": case "rail":
                        if (!styleSet) { proto.Style = BorderStyle.Lines; styleSet = true; if (t.Text == "line") proto.Lines = 1; }
                        break;
                    case "double":
                        proto.Lines = 2; break;
                    case "beads": case "bead": case "pellets": case "dots": case "pearls":
                        proto.Style = BorderStyle.Beads; styleSet = true; break;
                    case "zigzag": case "zig-zag": case "chevrons": case "chevron": case "wolf-teeth": case "teeth":
                        proto.Style = BorderStyle.Zigzag; styleSet = true; break;
                    case "none": case "nothing": case "no": case "blank":
                        proto.Style = BorderStyle.None; styleSet = true; break;
                    case "wide": case "broad": case "thick": proto.Width = BorderWidth.Wide; break;
                    case "narrow": case "thin": case "slim": proto.Width = BorderWidth.Narrow; break;
                    case "without":
                        // "without lines"
                        proto.Lines = 0; i++; break;
                    case "between": case "with": case "in": case "and": case "of": case "the": case "a": case "an":
                    case "strands": case "border": case "band":
                        break;
                    default:
                        Warn(line, $"unknown border word '{t.Text}'");
                        break;
                }
            }

            if (!styleSet) throw new StatementError("border needs a style: knotwork, ring-chain, runes \"text\", lines, beads, zigzag or none");
            if (proto.Style == BorderStyle.Runes && string.IsNullOrWhiteSpace(text))
                throw new StatementError("runes need their text in quotes, e.g. runes \"Hippo made this\"");
            if (proto.Style == BorderStyle.Lines && proto.Lines == 0) proto.Lines = 1;

            var made = new List<BorderSpec>();
            foreach (var side in sides)
            {
                var b = new BorderSpec
                {
                    Side = side, Style = proto.Style, Width = proto.Width, Strands = proto.Strands,
                    Lines = proto.Lines, SourceLine = line, Text = text ?? "",
                };
                _s.Borders[(int)side] = b;
                made.Add(b);
            }
            if (proto.Style == BorderStyle.Runes && made.Count > 1)
                runeGroups.Add((made.ToArray(), text));
        }

        /// <summary>
        /// One inscription given to several sides flows clockwise around them (top, right, bottom, left),
        /// split between sides in proportion to their length.
        /// </summary>
        void SplitInscriptions(List<(BorderSpec[] sides, string text)> groups)
        {
            foreach (var (sides, text) in groups)
            {
                // a later statement may have replaced some of these sides
                var live = sides.Where(b => _s.Borders[(int)b.Side] == b).OrderBy(b => (int)b.Side).ToList();
                if (live.Count <= 1) continue;
                var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                float[] lens = live.Select(b => b.Side == Side.Top || b.Side == Side.Bottom ? _s.Width : _s.Height).ToArray();
                float total = lens.Sum();
                // each word goes to the side where its middle falls, in proportion to side length
                int totalChars = Math.Max(1, words.Sum(w => w.Length + 1));
                var parts = live.Select(_ => new List<string>()).ToList();
                int used = 0;
                foreach (var word in words)
                {
                    float mid = (used + word.Length * 0.5f) / totalChars * total;
                    int k = 0;
                    float acc = lens[0];
                    while (k < live.Count - 1 && mid >= acc) { k++; acc += lens[k]; }
                    parts[k].Add(word);
                    used += word.Length + 1;
                }
                for (int k = 0; k < live.Count; k++) live[k].Text = string.Join(" ", parts[k]);
            }
        }

        // ---------------------------------------------------------------- charges

        static bool TryPositionWord(string w, ref int row, ref int col)
        {
            switch (w)
            {
                case "top": case "upper": case "high": row = 0; return true;
                case "bottom": case "lower": case "low": case "base": row = 2; return true;
                case "left": col = 0; return true;
                case "right": col = 2; return true;
                case "center": case "centre": case "middle": case "centred": case "centered":
                    return true;
            }
            return false;
        }

        static bool IsPositionWord(string w)
        {
            int r = 1, c = 1;
            if (w.Contains("-")) return w.Split('-').All(p => TryPositionWord(p, ref r, ref c));
            return TryPositionWord(w, ref r, ref c);
        }

        static Arrangement? ArrangementWord(string w)
        {
            switch (w)
            {
                case "ring": case "circle": case "round": return Arrangement.Ring;
                case "grid": case "square": case "block": return Arrangement.Grid;
                case "row": case "line": return Arrangement.Row;
                case "column": case "stack": case "pile": return Arrangement.Column;
            }
            return null;
        }

        /// <summary>
        /// [positions [:]] [count] name [arrangement] [, modifiers...] [and [count] name ...]
        /// Positions may also trail: "a wolf at the top left".
        /// </summary>
        void ParseCharges(List<Token> st)
        {
            int line = st[0].Line;
            var positions = new List<Position>();
            var charges = new List<ChargeSpec>();
            ChargeSpec cur = null;
            var nameWords = new List<string>();
            int curRow = 1, curCol = 1;
            bool inPosition = false;

            void FlushPosition()
            {
                if (inPosition) positions.Add(new Position(curRow, curCol));
                inPosition = false;
                curRow = 1; curCol = 1;
            }
            void FlushName()
            {
                if (cur != null && nameWords.Count > 0)
                {
                    cur.Name = string.Join(" ", nameWords);
                    nameWords.Clear();
                }
            }
            ChargeSpec Begin(int count)
            {
                FlushName();
                cur = new ChargeSpec { Count = count, SourceLine = line };
                charges.Add(cur);
                return cur;
            }

            for (int i = 0; i < st.Count; i++)
            {
                var t = st[i];
                string next = i + 1 < st.Count && st[i + 1].Kind == TokenKind.Word ? st[i + 1].Text : "";
                string next2 = i + 2 < st.Count && st[i + 2].Kind == TokenKind.Word ? st[i + 2].Text : "";

                if (t.Kind == TokenKind.Colon || t.Kind == TokenKind.Comma)
                {
                    FlushPosition();
                    if (t.Kind == TokenKind.Comma) FlushName();
                    continue;
                }

                if (t.Kind == TokenKind.String)
                {
                    FlushPosition();
                    if (cur == null || cur.Name.Length > 0 || nameWords.Count > 0) Begin(1);
                    nameWords.Add(t.Text);
                    FlushName();
                    continue;
                }

                if (t.Kind == TokenKind.Number)
                {
                    // "rotated 45" handled below; a bare number before a name is a count
                    FlushPosition();
                    Begin(Clamp((int)t.Number, 1, 64));
                    continue;
                }

                string w = t.Text;

                // positions: "top left", "top-left", "in the centre", "at the top"
                if (IsPositionWord(w) && !(nameWords.Count > 0 && w == "base"))
                {
                    if (!inPosition) { inPosition = true; curRow = 1; curCol = 1; }
                    foreach (var part in w.Split('-')) TryPositionWord(part, ref curRow, ref curCol);
                    continue;
                }
                if (inPosition && (w == "and" || Filler.Contains(w)))
                {
                    if (w == "and") FlushPosition();
                    continue;
                }
                FlushPosition();

                // arrangement: "in a ring", "in a grid", "in a row"
                if (w == "in")
                {
                    string aw = (next == "a" || next == "an" || next == "the") ? next2 : next;
                    var arr = ArrangementWord(aw);
                    if (arr.HasValue && cur != null)
                    {
                        FlushName();
                        cur.Arrangement = arr.Value;
                        i += (next == aw) ? 1 : 2;
                        continue;
                    }
                    if (IsPositionWord(next) || ((next == "the" || next == "a") && IsPositionWord(next2))) continue;
                }

                // a count starts a new charge: "a wolf and two ravens"
                if (CountWords.TryGetValue(w, out int count))
                {
                    // "a" directly after a name word would be odd; always treat as a new charge
                    Begin(count);
                    continue;
                }

                // modifiers
                bool handled = true;
                switch (w)
                {
                    case "relief": case "raised": case "embossed":
                        Mods(line, ref cur, c => c.Mode = CarveMode.Relief); break;
                    case "incised": case "outlined": case "outline": case "engraved": case "lined":
                        Mods(line, ref cur, c => c.Mode = CarveMode.Incised); break;
                    case "sunken": case "sunk": case "hollow": case "countersunk": case "intaglio":
                        Mods(line, ref cur, c => c.Mode = CarveMode.Sunken); break;
                    case "small": case "little": Mods(line, ref cur, c => c.Scale = 0.65f); break;
                    case "tiny": Mods(line, ref cur, c => c.Scale = 0.4f); break;
                    case "large": case "big": case "great": case "huge": Mods(line, ref cur, c => c.Scale = 1f); break;
                    case "flipped": case "mirrored": case "reversed":
                        Mods(line, ref cur, c => c.Flipped = !c.Flipped); break;
                    case "upside-down": Mods(line, ref cur, c => c.Rotation += 180); break;
                    case "facing":
                        if (next == "left") { Mods(line, ref cur, c => c.Flipped = true); i++; }
                        else if (next == "right") { i++; }
                        break;
                    case "rotated": case "turned": case "tilted":
                    {
                        float deg = 90;
                        if (i + 1 < st.Count && st[i + 1].Kind == TokenKind.Number) { deg = st[i + 1].Number; i++; }
                        if (i + 1 < st.Count && st[i + 1].Kind == TokenKind.Word && st[i + 1].Text.StartsWith("deg")) i++;
                        // "rotated 45 left" = counter-clockwise (default), "right"/"clockwise" = clockwise
                        if (i + 1 < st.Count && st[i + 1].Kind == TokenKind.Word &&
                            (st[i + 1].Text == "right" || st[i + 1].Text == "clockwise")) { deg = -deg; i++; }
                        else if (i + 1 < st.Count && st[i + 1].Kind == TokenKind.Word &&
                            (st[i + 1].Text == "left" || st[i + 1].Text == "anticlockwise" || st[i + 1].Text == "counterclockwise")) i++;
                        float d = deg;
                        Mods(line, ref cur, c => c.Rotation += d);
                        break;
                    }
                    case "and":
                        FlushName();
                        break;
                    default:
                        handled = false; break;
                }
                if (handled) continue;

                // filler leading into a trailing position: "a wolf at the top left"
                if (Filler.Contains(w) || w == "in")
                {
                    int j = i + 1;
                    while (j < st.Count && st[j].Kind == TokenKind.Word && (Filler.Contains(st[j].Text) || st[j].Text == "in")) j++;
                    if (j < st.Count && st[j].Kind == TokenKind.Word && IsPositionWord(st[j].Text)) { FlushName(); continue; }
                    if (nameWords.Count == 0) continue;
                }

                if (cur == null) Begin(1);
                else if (cur.Name.Length > 0) Begin(1);
                nameWords.Add(w);
            }
            FlushName();
            FlushPosition();

            charges.RemoveAll(c =>
            {
                if (c.Name.Length > 0) return false;
                Warn(line, "a count without a charge name was ignored");
                return true;
            });
            if (charges.Count == 0) throw new StatementError("expected a charge, e.g. 'center: a wolf'");
            if (positions.Count == 0) positions.Add(Position.Center);

            foreach (var pos in positions.Distinct())
                foreach (var c in charges)
                {
                    var copy = c.Clone();
                    copy.Position = pos;
                    _s.Charges.Add(copy);
                }
        }

        void Mods(int line, ref ChargeSpec cur, Action<ChargeSpec> apply)
        {
            if (cur == null) { Warn(line, "modifier before any charge was ignored"); return; }
            apply(cur);
        }

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
