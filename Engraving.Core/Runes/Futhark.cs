using System.Collections.Generic;
using System.Text;

namespace Engraving.Core.Runes
{
    /// <summary>
    /// Elder Futhark as stroke geometry, so runes carve as clean V-cut lines at any size.
    /// Each glyph is a list of polylines in a box 1 unit tall, Width units wide (x right, y up).
    /// </summary>
    public sealed class RuneGlyph
    {
        public readonly char Codepoint;
        public readonly float Width;
        public readonly float[][] Strokes;
        public RuneGlyph(char cp, float width, params float[][] strokes) { Codepoint = cp; Width = width; Strokes = strokes; }
    }

    public static class Futhark
    {
        static readonly Dictionary<char, RuneGlyph> Glyphs = new Dictionary<char, RuneGlyph>();

        static void G(char c, float w, params float[][] s) => Glyphs[c] = new RuneGlyph(c, w, s);
        static float[] P(params float[] pts) => pts;

        static readonly float[] Staff = { 0, 0, 0, 1 };

        static Futhark()
        {
            G('ᚠ', 0.5f, Staff, P(0, 0.55f, 0.5f, 0.85f), P(0, 0.8f, 0.4f, 1.02f));           // ᚠ fehu f
            G('ᚢ', 0.5f, P(0, 0, 0, 1, 0.5f, 0.7f, 0.5f, 0));                                  // ᚢ uruz u
            G('ᚦ', 0.42f, Staff, P(0, 0.78f, 0.42f, 0.5f, 0, 0.22f));                          // ᚦ thurisaz th
            G('ᚨ', 0.48f, Staff, P(0, 1, 0.48f, 0.72f), P(0, 0.72f, 0.48f, 0.44f));           // ᚨ ansuz a
            G('ᚱ', 0.48f, Staff, P(0, 1, 0.42f, 0.78f, 0, 0.55f, 0.48f, 0));                   // ᚱ raido r
            G('ᚲ', 0.4f, P(0.4f, 0.85f, 0, 0.5f, 0.4f, 0.15f));                                 // ᚲ kaunan k/c
            G('ᚷ', 0.62f, P(0, 0, 0.62f, 1), P(0, 1, 0.62f, 0));                               // ᚷ gebo g
            G('ᚹ', 0.42f, Staff, P(0, 1, 0.42f, 0.78f, 0, 0.56f));                              // ᚹ wunjo w
            G('ᚺ', 0.5f, Staff, P(0.5f, 0, 0.5f, 1), P(0, 0.68f, 0.5f, 0.32f));                // ᚺ hagalaz h
            G('ᚾ', 0.5f, P(0.25f, 0, 0.25f, 1), P(0, 0.68f, 0.5f, 0.32f));                     // ᚾ naudiz n
            G('ᛁ', 0.1f, Staff);                                                                // ᛁ isa i
            G('ᛃ', 0.5f, P(0.22f, 0.98f, 0, 0.76f, 0.22f, 0.54f), P(0.28f, 0.46f, 0.5f, 0.24f, 0.28f, 0.02f)); // ᛃ jera j
            G('ᛇ', 0.5f, P(0.25f, 0, 0.25f, 1), P(0.25f, 1, 0.5f, 0.8f), P(0.25f, 0, 0, 0.2f)); // ᛇ eihwaz
            G('ᛈ', 0.46f, Staff, P(0, 1, 0.3f, 0.74f, 0.46f, 0.92f), P(0, 0, 0.3f, 0.26f, 0.46f, 0.08f)); // ᛈ perth p
            G('ᛉ', 0.56f, P(0.28f, 0, 0.28f, 1), P(0, 0.98f, 0.28f, 0.6f, 0.56f, 0.98f));      // ᛉ algiz z
            G('ᛊ', 0.46f, P(0.42f, 1, 0.04f, 0.66f, 0.42f, 0.34f, 0.04f, 0));                   // ᛊ sowilo s
            G('ᛏ', 0.56f, P(0.28f, 0, 0.28f, 1), P(0, 0.72f, 0.28f, 1, 0.56f, 0.72f));         // ᛏ tiwaz t
            G('ᛒ', 0.44f, Staff, P(0, 1, 0.44f, 0.76f, 0, 0.5f, 0.44f, 0.24f, 0, 0));           // ᛒ berkanan b
            G('ᛖ', 0.56f, P(0, 0, 0, 1, 0.28f, 0.68f, 0.56f, 1, 0.56f, 0));                      // ᛖ ehwaz e
            G('ᛗ', 0.56f, Staff, P(0.56f, 0, 0.56f, 1), P(0, 1, 0.56f, 0.5f), P(0.56f, 1, 0, 0.5f)); // ᛗ mannaz m
            G('ᛚ', 0.42f, Staff, P(0, 1, 0.42f, 0.74f));                                        // ᛚ laguz l
            G('ᛜ', 0.5f, P(0.25f, 0.78f, 0.5f, 0.5f, 0.25f, 0.22f, 0, 0.5f, 0.25f, 0.78f));      // ᛜ ingwaz ng
            G('ᛞ', 0.66f, P(0, 0, 0, 1, 0.66f, 0, 0.66f, 1, 0, 0));                              // ᛞ dagaz d
            G('ᛟ', 0.56f, P(0.28f, 1, 0.56f, 0.7f, 0, 0), P(0.28f, 1, 0, 0.7f, 0.56f, 0));      // ᛟ othala o
        }

        public static bool TryGet(char c, out RuneGlyph g) => Glyphs.TryGetValue(c, out g);

        /// <summary>Word separator, carved as two stacked dots.</summary>
        public const char Divider = '᛬'; // ᛬

        /// <summary>
        /// Transliterates English into Elder Futhark. Rune characters already in the text pass through,
        /// so players can write runes directly. Spaces become dividers; other punctuation is dropped.
        /// </summary>
        public static string Transliterate(string text)
        {
            var sb = new StringBuilder();
            string s = text.ToLowerInvariant();
            bool lastWasDivider = true;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                char n = i + 1 < s.Length ? s[i + 1] : '\0';
                if (Glyphs.ContainsKey(c)) { sb.Append(c); lastWasDivider = false; continue; }
                string add = null;
                switch (c)
                {
                    case 't' when n == 'h': add = "ᚦ"; i++; break;
                    case 'n' when n == 'g': add = "ᛜ"; i++; break;
                    case 'e' when n == 'i': add = "ᛇ"; i++; break;
                    case 'a': add = "ᚨ"; break;
                    case 'b': add = "ᛒ"; break;
                    case 'c': add = n == 'k' ? null : "ᚲ"; break; // "ck" -> one k
                    case 'd': add = "ᛞ"; break;
                    case 'e': add = "ᛖ"; break;
                    case 'f': add = "ᚠ"; break;
                    case 'g': add = "ᚷ"; break;
                    case 'h': add = "ᚺ"; break;
                    case 'i': add = "ᛁ"; break;
                    case 'j': add = "ᛃ"; break;
                    case 'k': add = "ᚲ"; break;
                    case 'l': add = "ᛚ"; break;
                    case 'm': add = "ᛗ"; break;
                    case 'n': add = "ᚾ"; break;
                    case 'o': add = "ᛟ"; break;
                    case 'p': add = "ᛈ"; break;
                    case 'q': add = "ᚲ"; if (n == 'u') { add = "ᚲᚹ"; i++; } break;
                    case 'r': add = "ᚱ"; break;
                    case 's': add = "ᛊ"; break;
                    case 't': add = "ᛏ"; break;
                    case 'u': add = "ᚢ"; break;
                    case 'v': add = "ᚠ"; break;  // Norse had no v; f covered it
                    case 'w': add = "ᚹ"; break;
                    case 'x': add = "ᚲᛊ"; break;
                    case 'y': add = "ᛁ"; break;
                    case 'z': add = "ᛉ"; break;
                    case ' ': case ':': case '᛫': case '᛬': case '᛭': case '-':
                        if (!lastWasDivider) { sb.Append(Divider); lastWasDivider = true; }
                        continue;
                }
                if (add != null) { sb.Append(add); lastWasDivider = false; }
            }
            // no trailing divider
            while (sb.Length > 0 && sb[sb.Length - 1] == Divider) sb.Length--;
            return sb.ToString();
        }
    }
}
