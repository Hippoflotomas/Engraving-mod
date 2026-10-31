using System.Collections.Generic;

namespace Engraving.Core.Language
{
    /// <summary>Which edge of the panel a border runs along. Clockwise order matters for text flow.</summary>
    public enum Side { Top = 0, Right = 1, Bottom = 2, Left = 3 }

    public enum BackgroundKind { Plain, Recessed, Stippled, Hatched }

    public enum BorderStyle { None, Knotwork, RingChain, Runes, Lines, Beads, Zigzag }

    public enum BorderWidth { Narrow, Normal, Wide }

    /// <summary>How a charge is cut into the wood.</summary>
    public enum CarveMode
    {
        /// <summary>Shape stands proud of the ground (default).</summary>
        Relief,
        /// <summary>Only the outline is cut as a groove.</summary>
        Incised,
        /// <summary>Whole shape is cut down below the surface.</summary>
        Sunken,
    }

    public enum Arrangement { Auto, Grid, Ring, Row, Column }

    /// <summary>Cell in the 3x3 placement grid. Row 0 is the top, column 0 is the left.</summary>
    public struct Position
    {
        public int Row;
        public int Col;

        public Position(int row, int col) { Row = row; Col = col; }

        public static readonly Position Center = new Position(1, 1);

        public override string ToString()
        {
            string[] rows = { "top", "", "bottom" };
            string[] cols = { "left", "", "right" };
            string r = rows[Row], c = cols[Col];
            if (r.Length == 0 && c.Length == 0) return "center";
            if (r.Length == 0) return c;
            if (c.Length == 0) return r;
            return r + " " + c;
        }
    }

    public class BorderSpec
    {
        public Side Side;
        public BorderStyle Style = BorderStyle.None;
        public BorderWidth Width = BorderWidth.Normal;
        /// <summary>Knotwork: number of strands across the band.</summary>
        public int Strands = 3;
        /// <summary>Runes: the text to cut (already split across sides when several share one inscription).</summary>
        public string Text = "";
        /// <summary>Runes: number of rail lines enclosing the text (0 = none). Lines style: number of lines.</summary>
        public int Lines = 2;
        public int SourceLine;
    }

    public class ChargeSpec
    {
        /// <summary>Name as written, e.g. "wolves" or "my banner". Resolved against the charge library.</summary>
        public string Name = "";
        public int Count = 1;
        public Arrangement Arrangement = Arrangement.Auto;
        public CarveMode Mode = CarveMode.Relief;
        public Position Position = Position.Center;
        /// <summary>Scale factor relative to the automatic size (1 = fill available space).</summary>
        public float Scale = 1f;
        /// <summary>Degrees, counter-clockwise.</summary>
        public float Rotation;
        public bool Flipped;
        public int SourceLine;
        /// <summary>Figures only: the pose ("striking"), or null for the default.</summary>
        public string Pose;
        /// <summary>Figures only: what it holds ("axe", "shield").</summary>
        public System.Collections.Generic.List<string> Held = new System.Collections.Generic.List<string>();
        /// <summary>Scenes only: the part this charge plays (the scene lays these out itself, not the 3x3 grid).</summary>
        public SceneSlot Slot;
        /// <summary>Scenes only: the space before this charge, and how high it floats (in the hero's heights).</summary>
        public float SceneGap, SceneLift;

        public ChargeSpec Clone()
        {
            var c = (ChargeSpec)MemberwiseClone();
            c.Held = new System.Collections.Generic.List<string>(Held);
            return c;
        }
    }

    /// <summary>The parsed description of one engraved panel.</summary>
    public class EngravingSpec
    {
        /// <summary>Panel size in metres. Normally supplied by the piece being carved; the script may override for previews.</summary>
        public float Width = 2f;
        public float Height = 2f;
        public bool SizeGiven;
        /// <summary>Poses recorded in the carving's hidden "#pose:" lines (players' own poses travel with the carving).</summary>
        public System.Collections.Generic.Dictionary<string, Figures.Pose> Poses = new System.Collections.Generic.Dictionary<string, Figures.Pose>();
        public BackgroundKind Background = BackgroundKind.Plain;
        /// <summary>Indexed by (int)Side. Null or Style == None means no border on that side.</summary>
        public BorderSpec[] Borders = new BorderSpec[4];
        public List<ChargeSpec> Charges = new List<ChargeSpec>();
        /// <summary>The scene, if the carving has one ("a scene of war against a troll").</summary>
        public SceneSpec Scene;

        public BorderSpec Border(Side s)
        {
            var b = Borders[(int)s];
            return b != null && b.Style != BorderStyle.None ? b : null;
        }
    }

    public enum Severity { Warning, Error }

    public class Diagnostic
    {
        public Severity Severity;
        public int Line;
        public string Message;

        public Diagnostic(Severity severity, int line, string message)
        {
            Severity = severity; Line = line; Message = message;
        }

        public override string ToString() => $"line {Line}: {(Severity == Severity.Error ? "error" : "warning")}: {Message}";
    }
}
