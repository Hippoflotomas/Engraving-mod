using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Engraving.Core.Language
{
    /// <summary>
    /// Basic mode (Hippo, 2026-10-30): the dropdown editor's choices, and the carving text they make. Copied from
    /// Heraldry Mod's BasicBlazon: pure C#, so the preview tool can check that every combination parses. The text is
    /// the only output; saving, syncing and drawing are the same as for written carvings. One charge group only;
    /// anything richer is advanced mode.
    /// </summary>
    public sealed class BasicCarving
    {
        // ------------------------------------------------------------------ the menus

        /// <summary>A dropdown entry: what the player sees, and the word(s) it stands for (null = a divider).</summary>
        public sealed class Option
        {
            public string Label, Value;
            public Option(string label, string value) { Label = label; Value = value; }
            public bool IsDivider => Value == null;
        }

        public static readonly Option[] Backgrounds =
        {
            new Option("Plain", "plain"),
            new Option("Recessed (cut down)", "recessed"),
            new Option("Stippled (dotted)", "stippled"),
            new Option("Hatched (lined)", "hatched"),
        };

        public static readonly Option[] BorderStyles =
        {
            new Option("Knotwork", "knotwork"),
            new Option("Ring-chain", "ring-chain"),
            new Option("Runes", "runes"),
            new Option("Lines", "lines"),
            new Option("Beads", "beads"),
            new Option("Zigzag", "zigzag"),
        };

        /// <summary>Strands for knotwork (2–6).</summary>
        public static readonly Option[] Strands =
        {
            new Option("2 strands", "2"), new Option("3 strands", "3"), new Option("4 strands", "4"),
            new Option("5 strands", "5"), new Option("6 strands", "6"),
        };

        /// <summary>Lines for a lines border (1–4).</summary>
        public static readonly Option[] LineCounts =
        {
            new Option("1 line", "1"), new Option("2 lines", "2"), new Option("3 lines", "3"), new Option("4 lines", "4"),
        };

        public static readonly Option[] Widths =
        {
            new Option("Normal width", "normal"),
            new Option("Wide", "wide"),
            new Option("Narrow", "narrow"),
        };

        public static readonly Option[] Counts =
        {
            new Option("One", "1"), new Option("Two", "2"), new Option("Three", "3"), new Option("Four", "4"),
        };

        public static readonly Option[] Arrangements =
        {
            new Option("In a grid", "grid"),
            new Option("In a ring", "ring"),
            new Option("In a row", "row"),
            new Option("In a column", "column"),
        };

        public static readonly Option[] Positions =
        {
            new Option("Centre", "center"),
            new Option("Top", "top"),
            new Option("Bottom", "bottom"),
            new Option("Left", "left"),
            new Option("Right", "right"),
            new Option("- - - - - - - -", null),
            new Option("Top left", "top left"),
            new Option("Top right", "top right"),
            new Option("Bottom left", "bottom left"),
            new Option("Bottom right", "bottom right"),
            new Option("- - - - - - - -", null),
            new Option("All four corners", "top left and top right and bottom left and bottom right"),
            new Option("Top and bottom", "top and bottom"),
            new Option("Left and right", "left and right"),
        };

        public static readonly Option[] Styles =
        {
            new Option("Relief (raised)", "relief"),
            new Option("Incised (outline)", "incised"),
            new Option("Sunken (cut in)", "sunken"),
        };

        /// <summary>A charge on the menu: the words for one and for several ("star" / "stars").</summary>
        public sealed class ChargeChoice
        {
            public string Label, One, Many;
            public bool IsDivider => One == null;
            public override string ToString() => Label;
        }

        static ChargeChoice Shape(string label, string one, string many) => new ChargeChoice { Label = label, One = one, Many = many };
        static ChargeChoice Divider(string label) => new ChargeChoice { Label = label };

        public static readonly ChargeChoice NoCharge = new ChargeChoice { Label = "None", One = "", Many = "" };

        /// <summary>The built-in shapes. Pictures and item icons are added after these by the editor.</summary>
        public static readonly ChargeChoice[] BuiltInCharges =
        {
            NoCharge,
            Divider("--- Shapes ---"),
            Shape("Sun", "sun", "suns"),
            Shape("Star", "star", "stars"),
            Shape("Six-pointed star", "star6", "star6s"),
            Shape("Eight-pointed star", "star8", "star8s"),
            Shape("Crescent moon", "crescent", "crescents"),
            Shape("Valknut", "valknut", "valknuts"),
            Shape("Triskele (spiral)", "triskele", "triskeles"),
            Shape("Cross", "cross", "crosses"),
            Shape("Saltire (an X)", "saltire-cross", "saltire-crosses"),
            Shape("Heart", "heart", "hearts"),
            Shape("Arrow", "arrow", "arrows"),
            Shape("Circle", "circle", "circles"),
            Shape("Ring", "ring", "rings"),
            Shape("Square", "square", "squares"),
            Shape("Diamond", "diamond", "diamonds"),
            Shape("Triangle", "triangle", "triangles"),
            Shape("Hexagon", "hexagon", "hexagons"),
            Shape("Octagon", "octagon", "octagons"),
        };

        public static ChargeChoice PicturesDivider => Divider("--- Your pictures ---");
        public static ChargeChoice ItemsDivider => Divider("--- Item icons ---");

        /// <summary>A picture or item icon: its name in quotes, the same for one or several.</summary>
        public static ChargeChoice Named(string label, string name)
        {
            string q = "\"" + Clean(name) + "\"";
            return new ChargeChoice { Label = label, One = q, Many = q };
        }

        // ------------------------------------------------------------------ the choices

        public string Background = "plain";
        public bool Top = true, Bottom = true, Left = true, Right = true;
        public string BorderStyle = "knotwork";
        public int StrandCount = 3, LineCount = 2;
        public string Width = "normal";
        public string Inscription = "carved by my hand";
        public ChargeChoice Charge = BuiltInCharges[2];   // a sun
        public int Count = 1;
        public string Arrangement = "grid", Position = "center", Style = "relief";

        // ------------------------------------------------------------------ what's greyed out

        public bool HasBorder => Top || Bottom || Left || Right;
        public bool BorderHasStrands => HasBorder && BorderStyle == "knotwork";
        public bool BorderHasLines => HasBorder && BorderStyle == "lines";
        public bool BorderHasInscription => HasBorder && BorderStyle == "runes";
        public bool HasCharge => Charge != null && !string.IsNullOrEmpty(Charge.One);
        public bool HasArrangement => HasCharge && Count > 1;

        /// <summary>Puts dependent choices back in range after a change.</summary>
        public void Tidy()
        {
            if (Count < 1) Count = 1;
            if (Count > 4) Count = 4;
            if (StrandCount < 2) StrandCount = 2;
            if (StrandCount > 6) StrandCount = 6;
            if (LineCount < 1) LineCount = 1;
            if (LineCount > 4) LineCount = 4;
            if (Charge == null || Charge.IsDivider) Charge = NoCharge;
            if (Positions.All(p => p.Value != Position)) Position = "center";
        }

        // ------------------------------------------------------------------ the carving text

        static readonly string[] CountWords = { "", "a", "two", "three", "four" };

        public string Build()
        {
            Tidy();
            var sb = new StringBuilder();
            sb.Append("background ").Append(Background).Append('\n');

            if (HasBorder)
            {
                sb.Append("border ").Append(SideWords()).Append(": ");
                switch (BorderStyle)
                {
                    case "runes":
                        string text = Clean(Inscription);
                        if (text.Length == 0) text = "carved by my hand";
                        sb.Append("runes \"").Append(text).Append('"');
                        break;
                    case "lines":
                        sb.Append(LineCount).Append(LineCount == 1 ? " line" : " lines");
                        break;
                    case "knotwork":
                        sb.Append("knotwork");
                        if (StrandCount != 3) sb.Append(", ").Append(StrandCount).Append(" strands");
                        break;
                    default:
                        sb.Append(BorderStyle);
                        break;
                }
                if (Width != "normal") sb.Append(", ").Append(Width);
                sb.Append('\n');
            }

            if (HasCharge)
            {
                string noun = Count > 1 ? Charge.Many : Charge.One;
                string count = Count > 1 ? CountWords[Count] : Article(noun);
                sb.Append(Position).Append(": ").Append(count).Append(' ').Append(noun);
                if (Count > 1 && Arrangement != "grid") sb.Append(" in a ").Append(Arrangement);
                if (Style != "relief") sb.Append(", ").Append(Style);
                sb.Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        string SideWords()
        {
            if (Top && Bottom && Left && Right) return "all";
            var sides = new List<string>();
            if (Top) sides.Add("top");
            if (Bottom) sides.Add("bottom");
            if (Left) sides.Add("left");
            if (Right) sides.Add("right");
            return string.Join(" and ", sides);
        }

        static string Article(string word)
        {
            string w = word.TrimStart('"').ToLowerInvariant();
            return w.Length > 0 && "aeiou".IndexOf(w[0]) >= 0 ? "an" : "a";
        }

        /// <summary>Text safe inside quotes on one line.</summary>
        static string Clean(string s) =>
            new string((s ?? "").Where(c => c != '"' && c != '\n' && c != '\r' && c != ';' && c != '#').ToArray()).Trim();

        // ------------------------------------------------------------------ for the self-test

        /// <summary>Every combination worth checking (the preview tool parses each one).</summary>
        public static IEnumerable<BasicCarving> Combinations(IEnumerable<ChargeChoice> extraCharges = null)
        {
            var charges = BuiltInCharges.Where(c => !c.IsDivider).Concat(extraCharges ?? Enumerable.Empty<ChargeChoice>()).ToList();
            foreach (var bg in Backgrounds)
                for (int sides = 0; sides < 16; sides++)
                    foreach (var style in BorderStyles)
                        foreach (var width in Widths)
                            yield return new BasicCarving
                            {
                                Background = bg.Value, Top = (sides & 1) != 0, Bottom = (sides & 2) != 0, Left = (sides & 4) != 0,
                                Right = (sides & 8) != 0, BorderStyle = style.Value, Width = width.Value, StrandCount = 2 + sides % 5,
                                LineCount = 1 + sides % 4, Charge = BuiltInCharges[0],
                            };
            foreach (var charge in charges)
                foreach (var count in Counts)
                    foreach (var arr in Arrangements)
                        foreach (var pos in Positions.Where(p => !p.IsDivider))
                            foreach (var style in Styles)
                                yield return new BasicCarving
                                {
                                    Charge = charge, Count = int.Parse(count.Value), Arrangement = arr.Value, Position = pos.Value,
                                    Style = style.Value, Top = false, Bottom = false, Left = false, Right = false,
                                };
        }
    }
}
