namespace Engraving.Core.Render
{
    /// <summary>
    /// Height levels of the carving, 0..1. 1 is the untouched wood surface. Every cut is relative to these,
    /// so the normal-map strength can be tuned in one place.
    /// </summary>
    public class Levels
    {
        /// <summary>Untouched surface.</summary>
        public float Surface = 1f;
        /// <summary>Cut-away ground behind relief work (recessed backgrounds, band grounds).</summary>
        public float Ground = 0.55f;
        /// <summary>Bottom of incised lines.</summary>
        public float Groove = 0.2f;
        /// <summary>How far a sunken charge drops below its local ground.</summary>
        public float SinkDepth = 0.3f;

        /// <summary>Width of the rounded edge on raised work, metres.</summary>
        public float Bevel = 0.012f;
        /// <summary>Half width of an incised V line, metres.</summary>
        public float GrooveHalfWidth = 0.009f;
        /// <summary>Half width of the raised frame line between borders and the field, metres.</summary>
        public float FilletHalfWidth = 0.008f;
        /// <summary>Width of the moat cut around relief charges on a plain background, metres.</summary>
        public float Moat = 0.032f;
    }
}
