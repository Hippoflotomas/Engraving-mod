using System;
using System.Collections.Generic;
using System.Linq;
using Engraving.Core.Geometry;

namespace Engraving.Core.Figures
{
    /// <summary>A quadruped's body: bone lengths, thicknesses and features, read from JSON (flattened like poses).</summary>
    public sealed class Species
    {
        public string Name;
        public List<string> Words = new List<string>();
        public Dictionary<string, float> Values = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public float Get(string key, float fallback) => Values.TryGetValue(key, out var v) ? v : fallback;
        public string Str(string key, string fallback) => Strings.TryGetValue(key, out var v) ? v : fallback;
    }

    /// <summary>
    /// The quadruped species (Hippo, 2026-10-31: one quadruped rig, the animals differ only in data). Built in as
    /// JSON, in the same style as the poses, so species can later be added from files as poses are.
    /// </summary>
    public static class SpeciesLibrary
    {
        /// <summary>
        /// Lengths and radii in metres (true-ish sizes, so figures sharing a carving keep their proportions).
        /// body: length (hip joint to shoulder joint), hipR, chestR, bellyR. neck: length, r0 (at the body), r1 (at the
        /// head). head: skullR, snout (skull centre to nose), snoutR, ears (pointed / round / flap / none), earSize.
        /// fore: upper, lower, foot lengths, r0 (top), r1 (wrist), footR. hind: thigh, shin, foot, r0, r1, footR.
        /// feet: paw / hoof / pad. tail: length, r0, r1, bushy (1 = thickest in the middle).
        /// Features (0 = none): antlers, tusks, mane, trunk, shaggy (1 = fur cuts on the body).
        /// </summary>
        public const string BuiltinJson = @"{ ""species"": [
  { ""name"": ""wolf"", ""words"": [""wolves"", ""wolfs""], ""feet"": ""paw"",
    ""body"": { ""length"": 0.62, ""hipR"": 0.12, ""chestR"": 0.165, ""bellyR"": 0.095 },
    ""neck"": { ""length"": 0.2, ""r0"": 0.12, ""r1"": 0.085 },
    ""head"": { ""skullR"": 0.09, ""snout"": 0.19, ""snoutR"": 0.04, ""ears"": ""pointed"", ""earSize"": 0.08 },
    ""fore"": { ""upper"": 0.2, ""lower"": 0.23, ""foot"": 0.09, ""r0"": 0.075, ""r1"": 0.034, ""footR"": 0.036 },
    ""hind"": { ""thigh"": 0.24, ""shin"": 0.24, ""foot"": 0.14, ""r0"": 0.1, ""r1"": 0.034, ""footR"": 0.036 },
    ""tail"": { ""length"": 0.4, ""r0"": 0.04, ""r1"": 0.03, ""bushy"": 1 } },

  { ""name"": ""deer"", ""words"": [""deers"", ""stag"", ""stags"", ""hart"", ""harts""], ""feet"": ""hoof"",
    ""body"": { ""length"": 0.68, ""hipR"": 0.13, ""chestR"": 0.155, ""bellyR"": 0.115 },
    ""neck"": { ""length"": 0.36, ""r0"": 0.1, ""r1"": 0.06 },
    ""head"": { ""skullR"": 0.075, ""snout"": 0.18, ""snoutR"": 0.035, ""ears"": ""pointed"", ""earSize"": 0.09 },
    ""fore"": { ""upper"": 0.27, ""lower"": 0.3, ""foot"": 0.16, ""r0"": 0.065, ""r1"": 0.025, ""footR"": 0.026 },
    ""hind"": { ""thigh"": 0.3, ""shin"": 0.32, ""foot"": 0.22, ""r0"": 0.1, ""r1"": 0.026, ""footR"": 0.026 },
    ""tail"": { ""length"": 0.1, ""r0"": 0.035, ""r1"": 0.025 },
    ""antlers"": 0.42 },

  { ""name"": ""boar"", ""words"": [""boars"", ""pig"", ""pigs"", ""hog"", ""hogs""], ""feet"": ""hoof"",
    ""body"": { ""length"": 0.62, ""hipR"": 0.145, ""chestR"": 0.185, ""bellyR"": 0.14 },
    ""neck"": { ""length"": 0.1, ""r0"": 0.18, ""r1"": 0.14 },
    ""head"": { ""skullR"": 0.1, ""snout"": 0.24, ""snoutR"": 0.05, ""ears"": ""pointed"", ""earSize"": 0.07 },
    ""fore"": { ""upper"": 0.15, ""lower"": 0.15, ""foot"": 0.07, ""r0"": 0.07, ""r1"": 0.032, ""footR"": 0.032 },
    ""hind"": { ""thigh"": 0.18, ""shin"": 0.16, ""foot"": 0.09, ""r0"": 0.1, ""r1"": 0.032, ""footR"": 0.032 },
    ""tail"": { ""length"": 0.14, ""r0"": 0.022, ""r1"": 0.016 },
    ""tusks"": 0.12, ""mane"": 0.08 },

  { ""name"": ""lox"", ""words"": [""loxen"", ""loxes""], ""feet"": ""pad"", ""neckTilt"": -40, ""headTilt"": -25,
    ""body"": { ""length"": 1.05, ""hipR"": 0.38, ""chestR"": 0.5, ""bellyR"": 0.38 },
    ""neck"": { ""length"": 0.3, ""r0"": 0.36, ""r1"": 0.3 },
    ""head"": { ""skullR"": 0.27, ""snout"": 0.34, ""snoutR"": 0.2, ""ears"": ""none"" },
    ""fore"": { ""upper"": 0.36, ""lower"": 0.36, ""foot"": 0.1, ""r0"": 0.2, ""r1"": 0.15, ""footR"": 0.15 },
    ""hind"": { ""thigh"": 0.4, ""shin"": 0.32, ""foot"": 0.12, ""r0"": 0.24, ""r1"": 0.15, ""footR"": 0.15 },
    ""tail"": { ""length"": 0.3, ""r0"": 0.05, ""r1"": 0.035 },
    ""mane"": 0.32, ""shaggy"": 1 }
  ,
  { ""name"": ""bear"", ""words"": [""bears"", ""bjorn"", ""bjorns""], ""feet"": ""paw"",
    ""body"": { ""length"": 0.8, ""hipR"": 0.26, ""chestR"": 0.32, ""bellyR"": 0.25 },
    ""neck"": { ""length"": 0.2, ""r0"": 0.22, ""r1"": 0.16 }, ""neckTilt"": -15,
    ""head"": { ""skullR"": 0.15, ""snout"": 0.24, ""snoutR"": 0.07, ""ears"": ""round"", ""earSize"": 0.09 },
    ""fore"": { ""upper"": 0.3, ""lower"": 0.3, ""foot"": 0.1, ""r0"": 0.15, ""r1"": 0.09, ""footR"": 0.08 },
    ""hind"": { ""thigh"": 0.32, ""shin"": 0.28, ""foot"": 0.12, ""r0"": 0.18, ""r1"": 0.09, ""footR"": 0.08 },
    ""tail"": { ""length"": 0.08, ""r0"": 0.05, ""r1"": 0.04 }, ""shaggy"": 1 },

  { ""name"": ""unbjorn"", ""words"": [""unbjorns""], ""feet"": ""paw"",
    ""body"": { ""length"": 0.85, ""hipR"": 0.27, ""chestR"": 0.34, ""bellyR"": 0.26 },
    ""neck"": { ""length"": 0.2, ""r0"": 0.23, ""r1"": 0.17 }, ""neckTilt"": -15,
    ""head"": { ""skullR"": 0.16, ""snout"": 0.24, ""snoutR"": 0.075, ""ears"": ""round"", ""earSize"": 0.09 },
    ""fore"": { ""upper"": 0.3, ""lower"": 0.3, ""foot"": 0.1, ""r0"": 0.16, ""r1"": 0.095, ""footR"": 0.085 },
    ""hind"": { ""thigh"": 0.32, ""shin"": 0.28, ""foot"": 0.12, ""r0"": 0.19, ""r1"": 0.095, ""footR"": 0.085 },
    ""tail"": { ""length"": 0.08, ""r0"": 0.05, ""r1"": 0.04 }, ""mane"": 0.2, ""shaggy"": 1 },

  { ""name"": ""moose"", ""words"": [""mooses"", ""elk"", ""elks""], ""feet"": ""hoof"",
    ""body"": { ""length"": 0.95, ""hipR"": 0.2, ""chestR"": 0.27, ""bellyR"": 0.19 },
    ""neck"": { ""length"": 0.32, ""r0"": 0.16, ""r1"": 0.1 }, ""neckTilt"": -10, ""headTilt"": -15,
    ""head"": { ""skullR"": 0.11, ""snout"": 0.32, ""snoutR"": 0.08, ""ears"": ""pointed"", ""earSize"": 0.1 },
    ""fore"": { ""upper"": 0.4, ""lower"": 0.42, ""foot"": 0.2, ""r0"": 0.09, ""r1"": 0.04, ""footR"": 0.04 },
    ""hind"": { ""thigh"": 0.42, ""shin"": 0.44, ""foot"": 0.26, ""r0"": 0.14, ""r1"": 0.04, ""footR"": 0.04 },
    ""tail"": { ""length"": 0.1, ""r0"": 0.04, ""r1"": 0.03 }, ""antlers"": 0.55, ""antlerStyle"": ""palmate"", ""mane"": 0.06 },

  { ""name"": ""hare"", ""words"": [""hares"", ""rabbit"", ""rabbits""], ""feet"": ""paw"",
    ""body"": { ""length"": 0.24, ""hipR"": 0.11, ""chestR"": 0.09, ""bellyR"": 0.08 },
    ""neck"": { ""length"": 0.05, ""r0"": 0.07, ""r1"": 0.06 },
    ""head"": { ""skullR"": 0.065, ""snout"": 0.08, ""snoutR"": 0.035, ""ears"": ""pointed"", ""earSize"": 0.17, ""earWidth"": 0.12 },
    ""fore"": { ""upper"": 0.08, ""lower"": 0.09, ""foot"": 0.04, ""r0"": 0.035, ""r1"": 0.018, ""footR"": 0.02 },
    ""hind"": { ""thigh"": 0.13, ""shin"": 0.13, ""foot"": 0.12, ""r0"": 0.07, ""r1"": 0.02, ""footR"": 0.022 },
    ""tail"": { ""length"": 0.04, ""r0"": 0.035, ""r1"": 0.035 } },

  { ""name"": ""eikthyr"", ""words"": [], ""feet"": ""hoof"",
    ""body"": { ""length"": 1.0, ""hipR"": 0.19, ""chestR"": 0.24, ""bellyR"": 0.17 },
    ""neck"": { ""length"": 0.5, ""r0"": 0.15, ""r1"": 0.09 },
    ""head"": { ""skullR"": 0.11, ""snout"": 0.26, ""snoutR"": 0.05, ""ears"": ""pointed"", ""earSize"": 0.12 },
    ""fore"": { ""upper"": 0.4, ""lower"": 0.45, ""foot"": 0.24, ""r0"": 0.09, ""r1"": 0.035, ""footR"": 0.036 },
    ""hind"": { ""thigh"": 0.44, ""shin"": 0.48, ""foot"": 0.32, ""r0"": 0.15, ""r1"": 0.036, ""footR"": 0.036 },
    ""tail"": { ""length"": 0.14, ""r0"": 0.05, ""r1"": 0.035 }, ""antlers"": 0.95, ""mane"": 0.1 },

  { ""name"": ""ulv"", ""words"": [""ulvs""], ""feet"": ""paw"",
    ""body"": { ""length"": 0.75, ""hipR"": 0.15, ""chestR"": 0.21, ""bellyR"": 0.12 },
    ""neck"": { ""length"": 0.2, ""r0"": 0.15, ""r1"": 0.11 },
    ""head"": { ""skullR"": 0.11, ""snout"": 0.22, ""snoutR"": 0.05, ""ears"": ""pointed"", ""earSize"": 0.1 },
    ""fore"": { ""upper"": 0.25, ""lower"": 0.28, ""foot"": 0.1, ""r0"": 0.09, ""r1"": 0.042, ""footR"": 0.045 },
    ""hind"": { ""thigh"": 0.3, ""shin"": 0.3, ""foot"": 0.16, ""r0"": 0.12, ""r1"": 0.042, ""footR"": 0.045 },
    ""tail"": { ""length"": 0.45, ""r0"": 0.05, ""r1"": 0.035, ""bushy"": 1 }, ""mane"": 0.14 },

  { ""name"": ""seal"", ""words"": [""seals""], ""feet"": ""flipper"",
    ""body"": { ""length"": 0.85, ""hipR"": 0.16, ""chestR"": 0.24, ""bellyR"": 0.2 },
    ""neck"": { ""length"": 0.16, ""r0"": 0.19, ""r1"": 0.13 }, ""neckTilt"": 10,
    ""head"": { ""skullR"": 0.12, ""snout"": 0.14, ""snoutR"": 0.07, ""ears"": ""none"" },
    ""fore"": { ""upper"": 0.06, ""lower"": 0.06, ""foot"": 0.14, ""r0"": 0.06, ""r1"": 0.05, ""footR"": 0.04 },
    ""hind"": { ""thigh"": 0.04, ""shin"": 0.04, ""foot"": 0.04, ""r0"": 0.05, ""r1"": 0.04, ""footR"": 0.035 },
    ""tail"": { ""length"": 0.28, ""r0"": 0.12, ""r1"": 0.05 } },

  { ""name"": ""neck"", ""words"": [""necks"", ""lizard"", ""lizards""], ""feet"": ""paw"",
    ""body"": { ""length"": 0.45, ""hipR"": 0.1, ""chestR"": 0.11, ""bellyR"": 0.09 },
    ""neck"": { ""length"": 0.14, ""r0"": 0.08, ""r1"": 0.07 }, ""neckTilt"": -20, ""headTilt"": 10,
    ""head"": { ""skullR"": 0.07, ""snout"": 0.16, ""snoutR"": 0.04, ""ears"": ""none"" },
    ""fore"": { ""upper"": 0.1, ""lower"": 0.1, ""foot"": 0.05, ""r0"": 0.045, ""r1"": 0.03, ""footR"": 0.03 },
    ""hind"": { ""thigh"": 0.12, ""shin"": 0.11, ""foot"": 0.06, ""r0"": 0.06, ""r1"": 0.03, ""footR"": 0.03 },
    ""tail"": { ""length"": 0.6, ""r0"": 0.07, ""r1"": 0.015 }, ""mane"": 0.04 },

  { ""name"": ""calf"", ""words"": [""calves"", ""loxcalf"", ""loxcalves""], ""feet"": ""pad"", ""neckTilt"": -30, ""headTilt"": -20,
    ""body"": { ""length"": 0.55, ""hipR"": 0.2, ""chestR"": 0.26, ""bellyR"": 0.2 },
    ""neck"": { ""length"": 0.15, ""r0"": 0.19, ""r1"": 0.16 },
    ""head"": { ""skullR"": 0.15, ""snout"": 0.18, ""snoutR"": 0.11, ""ears"": ""none"" },
    ""fore"": { ""upper"": 0.2, ""lower"": 0.2, ""foot"": 0.06, ""r0"": 0.1, ""r1"": 0.08, ""footR"": 0.08 },
    ""hind"": { ""thigh"": 0.22, ""shin"": 0.18, ""foot"": 0.07, ""r0"": 0.12, ""r1"": 0.08, ""footR"": 0.08 },
    ""tail"": { ""length"": 0.16, ""r0"": 0.03, ""r1"": 0.02 }, ""mane"": 0.12, ""shaggy"": 1 }
] }";

        static readonly Dictionary<string, Species> ByWord = new Dictionary<string, Species>(StringComparer.OrdinalIgnoreCase);
        public static readonly List<Species> All = new List<Species>();

        static SpeciesLibrary()
        {
            var root = (Dictionary<string, object>)MiniJson.Parse(BuiltinJson);
            foreach (Dictionary<string, object> d in (List<object>)root["species"])
            {
                var s = Read(d);
                All.Add(s);
                ByWord[s.Name] = s;
                foreach (var word in s.Words) ByWord[word] = s;
            }
        }

        /// <summary>A body (quadruped species or humanoid body) from its JSON object.</summary>
        public static Species Read(Dictionary<string, object> d)
        {
            var s = new Species { Name = ((string)d["name"]).ToLowerInvariant() };
            if (d.TryGetValue("words", out var w) && w is List<object> wl) s.Words.AddRange(wl.OfType<string>().Select(x => x.ToLowerInvariant()));
            Flatten(d, "", s);
            return s;
        }

        static void Flatten(Dictionary<string, object> d, string prefix, Species s)
        {
            foreach (var kv in d)
            {
                if (prefix.Length == 0 && (kv.Key == "name" || kv.Key == "words")) continue;
                string key = prefix + kv.Key;
                switch (kv.Value)
                {
                    case double x: s.Values[key] = (float)x; break;
                    case string str: s.Strings[key] = str.ToLowerInvariant(); break;
                    case Dictionary<string, object> sub: Flatten(sub, key + ".", s); break;
                }
            }
        }

        public static Species Find(string word) => word != null && ByWord.TryGetValue(word, out var s) ? s : null;
    }

    /// <summary>
    /// The quadruped rig: a side-on animal facing right. A spine from hip to shoulder, a neck and head, a tail, and
    /// four three-part legs (fore: upper, lower, foot; hind: thigh, shin, foot, with the hock bending back), all tapered
    /// strands. Parts back to front: far legs, tail, far antler, body, mane, neck, head and its features, near legs.
    /// The same cut lines as the humanoid's come from the overlaps.
    /// </summary>
    public static class Quadruped
    {
        static Vector2 Down(float deg) { float a = deg * M.Pi / 180f; return new Vector2(M.Sin(a), -M.Cos(a)); }
        static Vector2 Ahead(float deg) { float a = deg * M.Pi / 180f; return new Vector2(M.Cos(a), M.Sin(a)); }

        public static List<Part> Build(Species s, Pose pose)
        {
            float G(string k, float f) => s.Get(k, f);
            float P(string k, float f) => pose.Get(k, f);

            float len = G("body.length", 0.6f), hipR = G("body.hipR", 0.12f), chestR = G("body.chestR", 0.16f), bellyR = G("body.bellyR", 0.1f);
            float tilt = P("body", 0);

            // spine: hip joint at the origin, shoulders ahead along the tilt
            var hip = new Vector2(0, 0);
            var spine = Ahead(tilt);
            var up = spine.Perp;
            var shoulder = hip + spine * len;

            var parts = new List<Part>();

            // ---- legs (computed now, drawn far ones first)
            Vector2 foreTop = shoulder + up * (-chestR * 0.35f) + spine * (-chestR * 0.15f);
            Vector2 hindTop = hip + up * (-hipR * 0.25f) + spine * (hipR * 0.1f);
            var farShift = new Vector2(-0.025f, 0.012f);
            string feet = s.Str("feet", "paw");
            Part ForeLeg(string name, string key, Vector2 top) =>
                Leg(name, top, G("fore.upper", 0.2f), G("fore.lower", 0.22f), G("fore.foot", 0.09f),
                    P(key + ".upper", 0), P(key + ".lower", 0), P(key + ".foot", 10),
                    G("fore.r0", 0.07f), G("fore.r1", 0.03f), G("fore.footR", 0.03f), feet, false);
            Part HindLeg(string name, string key, Vector2 top) =>
                Leg(name, top, G("hind.thigh", 0.24f), G("hind.shin", 0.24f), G("hind.foot", 0.14f),
                    P(key + ".thigh", 25), P(key + ".shin", -35), P(key + ".foot", 0),
                    G("hind.r0", 0.1f), G("hind.r1", 0.03f), G("hind.footR", 0.03f), feet, true);

            var farHind = HindLeg("far hind leg", "farHind", hindTop + farShift);
            var farFore = ForeLeg("far fore leg", "farFore", foreTop + farShift);
            var nearHind = HindLeg("near hind leg", "nearHind", hindTop);
            var nearFore = ForeLeg("near fore leg", "nearFore", foreTop);
            parts.Add(farHind);
            parts.Add(farFore);

            // ---- tail
            parts.Add(Tail(hip + spine * (-hipR * 0.8f) + up * (hipR * 0.35f), tilt, P("tail", -50), P("tailCurl", 6),
                G("tail.length", 0.3f), G("tail.r0", 0.03f), G("tail.r1", 0.02f), G("tail.bushy", 0) > 0.5f));

            // ---- neck and head geometry
            float neckLen = G("neck.length", 0.2f), neckR0 = G("neck.r0", 0.1f), neckR1 = G("neck.r1", 0.08f);
            var neckBase = shoulder + up * (chestR * 0.3f) + spine * (chestR * 0.1f);
            var headC = neckBase + Ahead(P("neck", 35) + G("neckTilt", 0)) * neckLen;
            float skullR = G("head.skullR", 0.09f), snout = G("head.snout", 0.18f), snoutR = G("head.snoutR", 0.04f);
            float headDeg = P("head", -20) + G("headTilt", 0);
            var face = Ahead(headDeg);
            var faceUp = face.Perp;
            var nose = headC + face * snout;

            // far antler behind everything of the body
            float antlers = G("antlers", 0);
            if (antlers > 0) parts.Add(Antler("far antler", headC + faceUp * (skullR * 0.7f) - face * (skullR * 0.35f) + new Vector2(-0.03f, 0.01f), face, faceUp, antlers * 0.92f));

            // ---- body
            // a mane is drawn behind the body and neck, so only its bristles stand above the back line
            float mane = G("mane", 0);
            if (mane > 0) parts.Add(Mane(headC - face * (skullR * 0.6f) + faceUp * (skullR * 0.5f), shoulder + up * (chestR * 0.75f),
                                          hip + up * (hipR * 0.85f) + spine * (len * 0.3f), up, mane, chestR));
            parts.Add(Body(hip, shoulder, spine, up, hipR, chestR, bellyR, len, G("shaggy", 0) > 0.5f));
            parts.Add(new Part("neck", (x, y) => Sdf2.Capsule(x, y, neckBase, headC, neckR0, neckR1)));

            // ---- head
            string ears = s.Str("head.ears", "pointed");
            float earSize = G("head.earSize", 0.08f);
            if (ears == "flap") parts.Add(EarFlap(headC - face * (skullR * 0.45f) + faceUp * (skullR * 0.2f), face, faceUp, earSize));
            var eye = headC + face * (skullR * 0.55f) + faceUp * (skullR * 0.35f);
            var mouthA = nose - faceUp * (snoutR * 0.55f);
            var mouthB = headC + face * (skullR * 0.5f) - faceUp * (skullR * 0.45f);
            parts.Add(new Part("head", (x, y) => M.Min(Sdf.Circle(x - headC.X, y - headC.Y, skullR),
                    Sdf2.Capsule(x, y, headC, nose, skullR * 0.8f, snoutR)),
                (x, y) => M.Min(M.Abs(Sdf.Circle(x - eye.X, y - eye.Y, M.Max(0.012f, skullR * 0.12f))),
                                G("trunk", 0) > 0 ? float.MaxValue : Sdf.Segment(x, y, mouthA.X, mouthA.Y, mouthB.X, mouthB.Y))));
            if (ears == "pointed" || ears == "round")
                parts.Add(Ear(headC - face * (skullR * 0.3f) + faceUp * (skullR * 0.75f), face, faceUp, earSize, ears == "round", G("head.earWidth", 0.3f)));
            if (antlers > 0) parts.Add(s.Str("antlerStyle", "") == "palmate"
                ? PalmateAntler(headC + faceUp * (skullR * 0.7f) - face * (skullR * 0.25f), face, faceUp, antlers)
                : Antler("antler", headC + faceUp * (skullR * 0.7f) - face * (skullR * 0.25f), face, faceUp, antlers));
            float trunk = G("trunk", 0);
            if (trunk > 0) parts.Add(Trunk(nose, headDeg, snoutR, trunk));
            float tusks = G("tusks", 0);
            if (tusks > 0) parts.Add(Tusk(nose - face * (snout * 0.25f) - faceUp * (snoutR * 0.6f), face, faceUp, tusks, trunk > 0));

            // ---- near legs in front
            parts.Add(nearHind);
            parts.Add(nearFore);

            // stand on y = 0
            float low = float.MaxValue;
            for (float y = -2.5f; y < 2.5f && low == float.MaxValue; y += 0.005f)
                for (float x = -2.5f; x < 3.5f; x += 0.01f)
                    if (parts.Any(p => p.Distance(x, y) < 0)) { low = y; break; }
            var shift = new Vector2(0, low == float.MaxValue ? 0 : -low);
            foreach (var p in parts) p.Offset = shift;
            return parts;
        }

        // ---------------------------------------------------------------- parts

        static Part Leg(string name, Vector2 top, float l1, float l2, float l3, float a1, float a2, float a3,
                        float r0, float r1, float footR, string feet, bool hind)
        {
            var j1 = top + Down(a1) * l1;
            var j2 = j1 + Down(a2) * l2;
            var toe = j2 + Down(a3) * l3;
            float rMid = hind ? M.Lerp(r0, r1, 0.55f) : M.Lerp(r0, r1, 0.4f);
            // hoof: a small wedge; paw: a rounded pad reaching forward; pad (lox): a broad round foot
            var fwd = new Vector2(1, 0);
            return new Part(name, (x, y) =>
            {
                float d = Sdf2.Capsule(x, y, top, j1, r0, rMid);
                d = M.Min(d, Sdf2.Capsule(x, y, j1, j2, rMid, r1));
                d = M.Min(d, Sdf2.Capsule(x, y, j2, toe, r1, footR));
                switch (feet)
                {
                    case "hoof":
                        d = M.Min(d, Sdf2.Capsule(x, y, toe, toe + fwd * (footR * 0.9f), footR * 1.15f, footR * 0.6f));
                        break;
                    case "flipper":
                        d = M.Min(d, Sdf2.Capsule(x, y, toe, toe + fwd * (hind ? -footR * 3f : footR * 0.5f) + new Vector2(0, -footR * 0.3f), footR * 1.2f, footR * 0.7f));
                        break;
                    case "pad":
                        d = M.Min(d, Sdf2.Capsule(x, y, toe + fwd * (-footR * 0.3f), toe + fwd * (footR * 0.4f), footR * 1.1f, footR * 1.1f));
                        break;
                    default:
                        d = M.Min(d, Sdf2.Capsule(x, y, toe, toe + fwd * (footR * 1.6f), footR * 1.2f, footR * 0.8f));
                        break;
                }
                return d;
            }, feet == "hoof"
                ? (Func<float, float, float>)((x, y) => Sdf.Segment(x, y, toe.X - footR * 1.2f, toe.Y + footR * 0.9f, toe.X + footR * 1.6f, toe.Y + footR * 0.9f))
                : null);
        }

        static Part Body(Vector2 hip, Vector2 shoulder, Vector2 spine, Vector2 up, float hipR, float chestR, float bellyR, float len, bool shaggy)
        {
            var chest = shoulder - up * (chestR * 0.25f);
            var bellyA = hip + spine * (len * 0.2f) - up * (hipR * 0.35f);
            var bellyB = shoulder - spine * (len * 0.25f) - up * (chestR * 0.45f);
            Func<float, float, float> lines = null;
            if (shaggy)
            {
                // fur: short slanting cuts hanging along the belly and flank
                var strokes = new List<(Vector2, Vector2)>();
                for (int i = 0; i <= 9; i++)
                {
                    var a = Vector2.Lerp(bellyA, bellyB, i / 9f) - up * (bellyR * 0.55f);
                    strokes.Add((a, a - up * (bellyR * 0.45f) - spine * (bellyR * 0.15f)));
                    var b = Vector2.Lerp(hip, shoulder, (i + 0.5f) / 10f) + up * (hipR * 0.1f);
                    strokes.Add((b, b - up * (hipR * 0.35f) - spine * (hipR * 0.12f)));
                }
                lines = (x, y) => strokes.Min(st => Sdf.Segment(x, y, st.Item1.X, st.Item1.Y, st.Item2.X, st.Item2.Y));
            }
            return new Part("body", (x, y) =>
            {
                float d = Sdf2.Capsule(x, y, hip, shoulder, hipR, chestR * 0.85f);
                d = M.Min(d, Sdf.Circle(x - chest.X, y - chest.Y, chestR));
                d = M.Min(d, Sdf2.Capsule(x, y, bellyA, bellyB, bellyR, bellyR));
                return d;
            }, lines);
        }

        static Part Tail(Vector2 root, float tilt, float angle, float curl, float length, float r0, float r1, bool bushy)
        {
            // three segments, each turning by the curl; angle is above straight back
            var pts = new List<Vector2> { root };
            float a = 180 - angle + tilt;   // pointing backwards, measured like Ahead()
            for (int i = 0; i < 3; i++)
            {
                var next = pts[pts.Count - 1] + Ahead(a) * (length / 3);
                pts.Add(next);
                a += curl;
            }
            float rMid = bushy ? M.Max(r0, r1) * 1.7f : (r0 + r1) / 2;
            return new Part("tail", (x, y) =>
            {
                float d = Sdf2.Capsule(x, y, pts[0], pts[1], r0, bushy ? rMid * 0.9f : rMid);
                d = M.Min(d, Sdf2.Capsule(x, y, pts[1], pts[2], bushy ? rMid * 0.9f : rMid, bushy ? rMid : rMid));
                d = M.Min(d, Sdf2.Capsule(x, y, pts[2], pts[3], bushy ? rMid : rMid, bushy ? r1 * 0.6f : r1));
                return d;
            });
        }

        static Part Ear(Vector2 at, Vector2 face, Vector2 faceUp, float size, bool round, float width)
        {
            if (round) { var c = at + faceUp * (size * 0.4f); return new Part("ear", (x, y) => Sdf.Circle(x - c.X, y - c.Y, size * 0.5f)); }
            var tip = at + faceUp * size - face * (size * 0.45f);
            var a = at + face * (size * width * 0.93f);
            var b = at - face * (size * width * 1.07f);
            var poly = new[] { a.X, a.Y, tip.X, tip.Y, b.X, b.Y };
            return new Part("ear", (x, y) => Sdf.Polygon(x, y, poly) - 0.006f);
        }

        static Part EarFlap(Vector2 at, Vector2 face, Vector2 faceUp, float size)
        {
            // a big hanging ear (lox), drawn over the neck behind the eye
            var top = at + faceUp * (size * 0.5f);
            var bottom = at - faceUp * (size * 0.9f) - face * (size * 0.15f);
            return new Part("ear", (x, y) => Sdf2.Capsule(x, y, top, bottom, size * 0.55f, size * 0.38f));
        }

        static Part Antler(string name, Vector2 root, Vector2 face, Vector2 faceUp, float size)
        {
            // a main beam sweeping up and back, with tines forward
            var beam = new List<Vector2> { root };
            var dir = (faceUp * 1f - face * 0.35f).Normalized;
            for (int i = 0; i < 4; i++)
            {
                beam.Add(beam[beam.Count - 1] + dir * (size * 0.28f));
                dir = (dir + face * 0.18f).Normalized;   // curving forward at the top
            }
            var tines = new List<(Vector2, Vector2)>();
            for (int i = 1; i < 4; i++)
                tines.Add((beam[i], beam[i] + (face * 0.9f + faceUp * 0.5f).Normalized * (size * (0.3f - i * 0.04f))));
            float r = M.Max(0.012f, size * 0.045f);
            return new Part(name, (x, y) =>
            {
                float d = float.MaxValue;
                for (int i = 0; i + 1 < beam.Count; i++) d = M.Min(d, Sdf2.Capsule(x, y, beam[i], beam[i + 1], r * (1.2f - i * 0.15f), r * (1.05f - i * 0.15f)));
                foreach (var (a, b) in tines) d = M.Min(d, Sdf2.Capsule(x, y, a, b, r * 0.85f, r * 0.45f));
                return d;
            });
        }

        /// <summary>A moose's broad, flat antler: a plate with points along its top edge.</summary>
        static Part PalmateAntler(Vector2 root, Vector2 face, Vector2 faceUp, float size)
        {
            var back = (faceUp * 0.45f - face * 0.9f).Normalized;
            var stemTop = root + faceUp * (size * 0.18f) - face * (size * 0.05f);
            var pts = new List<Vector2> { root - face * 0.02f, stemTop - back * (size * 0.1f) };
            int n = 6;
            for (int i = 0; i <= n; i++)
            {
                var along = stemTop + back * (size * 0.85f * i / n);
                float lift = size * (0.25f + 0.1f * M.Sin(M.Pi * i / n)) * (i % 2 == 0 ? 1.25f : 0.85f);
                pts.Add(along + faceUp * lift);
            }
            pts.Add(stemTop + back * (size * 0.85f) - faceUp * (size * 0.05f));
            pts.Add(stemTop + back * (size * 0.1f) - faceUp * (size * 0.02f));
            var poly = pts.SelectMany(p => new[] { p.X, p.Y }).ToArray();
            return new Part("antler", (x, y) => Sdf.Polygon(x, y, poly) - 0.01f);
        }

        static Part Tusk(Vector2 root, Vector2 face, Vector2 faceUp, float size, bool long_)
        {
            // short and up-curved (boar) or long, forward, down then up (lox)
            var pts = new List<Vector2> { root };
            var dir = long_ ? (face * 0.6f - faceUp * 0.8f).Normalized : (face * 0.6f + faceUp * 0.8f).Normalized;
            int n = long_ ? 5 : 3;
            for (int i = 0; i < n; i++)
            {
                pts.Add(pts[pts.Count - 1] + dir * (size / n));
                dir = (dir + faceUp * (long_ ? 0.35f : 0.25f)).Normalized;
            }
            float r = long_ ? size * 0.07f : size * 0.16f;
            return new Part("tusk", (x, y) =>
            {
                float d = float.MaxValue;
                for (int i = 0; i + 1 < pts.Count; i++) d = M.Min(d, Sdf2.Capsule(x, y, pts[i], pts[i + 1], r * (1 - 0.6f * i / n), r * (1 - 0.6f * (i + 1) / n)));
                return d;
            });
        }

        static Part Trunk(Vector2 nose, float headDeg, float r0, float length)
        {
            // hanging down from the face and curling forward at the tip, with ring cuts along it
            var pts = new List<Vector2> { nose };
            float a = headDeg - 60;
            for (int i = 0; i < 6; i++)
            {
                pts.Add(pts[pts.Count - 1] + Ahead(a) * (length / 6));
                a -= i < 3 ? 6 : -25;
            }
            float r1 = r0 * 0.45f;
            return new Part("trunk", (x, y) =>
            {
                float d = float.MaxValue;
                for (int i = 0; i + 1 < pts.Count; i++) d = M.Min(d, Sdf2.Capsule(x, y, pts[i], pts[i + 1], M.Lerp(r0, r1, i / 6f), M.Lerp(r0, r1, (i + 1) / 6f)));
                return d;
            }, (x, y) =>
            {
                float d = float.MaxValue;
                for (int i = 1; i < pts.Count - 1; i++)
                {
                    var along = (pts[i + 1] - pts[i - 1]).Normalized.Perp * M.Lerp(r0, r1, i / 6f) * 0.8f;
                    d = M.Min(d, Sdf.Segment(x, y, pts[i].X - along.X, pts[i].Y - along.Y, pts[i].X + along.X, pts[i].Y + along.Y));
                }
                return d;
            });
        }

        static Part Mane(Vector2 behindHead, Vector2 withers, Vector2 midBack, Vector2 up, float height, float depth)
        {
            // bristles along the top line, from behind the head over the withers to the middle of the back; the strip
            // reaches down into the body, which is drawn over it
            var line = new List<Vector2>();
            int n = 12;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                line.Add(t < 0.35f ? Vector2.Lerp(behindHead, withers, t / 0.35f) : Vector2.Lerp(withers, midBack, (t - 0.35f) / 0.65f));
            }
            var pts = new List<float>();
            for (int i = 0; i <= n; i++)
            {
                float h = (i % 2 == 0 ? 1f : 0.35f) * height * (1 - 0.5f * i / n);
                var q = line[i] + up * h;
                pts.Add(q.X); pts.Add(q.Y);
            }
            for (int i = n; i >= 0; i--)
            {
                var p = line[i] - up * (depth * 0.5f);
                pts.Add(p.X); pts.Add(p.Y);
            }
            var poly = pts.ToArray();
            return new Part("mane", (x, y) => Sdf.Polygon(x, y, poly));
        }
    }
}
