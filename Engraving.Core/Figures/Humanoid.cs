using System;
using System.Collections.Generic;
using System.Linq;
using Engraving.Core.Geometry;

namespace Engraving.Core.Figures
{
    /// <summary>
    /// The humanoid rig (Hippo, 2026-10-30): a side-on (profile) figure facing right, built as a skeleton whose
    /// limbs are tapered strands, posed from hand-authored presets, with held items on the hands. The result is a
    /// stack of filled parts, back to front; <see cref="FigureShape"/> turns it into a silhouette plus the thin cut
    /// lines where parts overlap, which is the whole of the Viking-carving look (solid shapes, detail from cut lines).
    ///
    /// One rig for every two-legged figure (Hippo, 2026-10-31): Vikings, shieldmaidens and the game's humanoid
    /// enemies differ only in their body data (<see cref="BuiltinBodies"/>): proportions, a hunch, a size, and
    /// features (head style, helmet, beard, braid, clothes, bones, digitigrade legs).
    ///
    /// Units: figure metres (a standing Viking is about 1.85 tall); the layout scales the figure to its cell.
    /// Poses come from <see cref="PoseLibrary"/> (JSON: built in, plus players' own files).
    /// </summary>
    public static class Humanoid
    {
        /// <summary>
        /// The bodies. Lengths and radii in metres before "scale" (the figure's overall size: 2 = twice a Viking).
        /// head: r, style (human / skull / greydwarf / troll / goblin / wolf). spine (hip joint to shoulders), neck.
        /// chestR, waistR, hipR. arm: upper, fore, r0, r1, foreR1, hand. leg: thigh, shin, r0, r1, shinR1, footLen,
        /// footR, style (plantigrade / digitigrade). hunch: degrees the body leans forward in every pose.
        /// helmet (nasal / none), beard, braid (1/0), clothes (tunic / long / loincloth / ragged / none), belt, boots,
        /// bones (1 = a skeleton: rib cage, bare spine and pelvis).
        /// </summary>
        public const string BuiltinBodies = @"{ ""bodies"": [
  { ""name"": ""viking"", ""words"": [""vikings"", ""warrior"", ""warriors"", ""man"", ""men"", ""figure"", ""figures"", ""humanoid"",
      ""humanoids"", ""person"", ""people"", ""fighter"", ""fighters""],
    ""helmet"": ""nasal"", ""beard"": 1, ""clothes"": ""tunic"", ""belt"": 1, ""boots"": 1 },

  { ""name"": ""shieldmaiden"", ""words"": [""shieldmaidens"", ""woman"", ""women"", ""maiden"", ""maidens"", ""lady"", ""ladies""],
    ""chestR"": 0.185, ""waistR"": 0.135, ""hipR"": 0.15,
    ""helmet"": ""nasal"", ""braid"": 1, ""clothes"": ""long"", ""belt"": 1, ""boots"": 1 },

  { ""name"": ""greydwarf"", ""words"": [""greydwarfs"", ""greydwarves"", ""greyling"", ""greylings""], ""scale"": 0.85, ""hunch"": 14,
    ""head"": { ""r"": 0.21, ""style"": ""greydwarf"" }, ""spine"": 0.42, ""neck"": 0.02,
    ""chestR"": 0.15, ""waistR"": 0.12, ""hipR"": 0.11,
    ""arm"": { ""upper"": 0.36, ""fore"": 0.36, ""r0"": 0.055, ""r1"": 0.045, ""foreR1"": 0.038, ""hand"": 0.055 },
    ""leg"": { ""thigh"": 0.38, ""shin"": 0.4, ""r0"": 0.07, ""r1"": 0.05, ""shinR1"": 0.04, ""footLen"": 0.15, ""footR"": 0.036 },
    ""helmet"": ""none"", ""clothes"": ""none"" },

  { ""name"": ""troll"", ""words"": [""trolls""], ""scale"": 1.9, ""hunch"": 24,
    ""head"": { ""r"": 0.11, ""style"": ""troll"" }, ""spine"": 0.55, ""neck"": 0.02,
    ""chestR"": 0.27, ""waistR"": 0.26, ""hipR"": 0.18,
    ""arm"": { ""upper"": 0.38, ""fore"": 0.38, ""r0"": 0.12, ""r1"": 0.1, ""foreR1"": 0.09, ""hand"": 0.1 },
    ""leg"": { ""thigh"": 0.34, ""shin"": 0.32, ""r0"": 0.15, ""r1"": 0.115, ""shinR1"": 0.1, ""footLen"": 0.22, ""footR"": 0.065 },
    ""helmet"": ""none"", ""clothes"": ""loincloth"", ""belt"": 1 },

  { ""name"": ""skeleton"", ""words"": [""skeletons"", ""bones""],
    ""head"": { ""r"": 0.125, ""style"": ""skull"" }, ""chestR"": 0.17, ""waistR"": 0.05, ""hipR"": 0.12,
    ""arm"": { ""r0"": 0.035, ""r1"": 0.03, ""foreR1"": 0.028, ""hand"": 0.045 },
    ""leg"": { ""r0"": 0.045, ""r1"": 0.036, ""shinR1"": 0.032, ""footR"": 0.035 },
    ""helmet"": ""none"", ""clothes"": ""none"", ""bones"": 1 },

  { ""name"": ""draugr"", ""words"": [""draugrs"", ""draugar"", ""draug""], ""hunch"": 16,
    ""chestR"": 0.19, ""waistR"": 0.145,
    ""arm"": { ""upper"": 0.33, ""fore"": 0.3 },
    ""helmet"": ""nasal"", ""beard"": 1, ""clothes"": ""ragged"", ""belt"": 1 },

  { ""name"": ""fuling"", ""words"": [""fulings"", ""goblin"", ""goblins""], ""scale"": 0.85, ""hunch"": 20,
    ""head"": { ""r"": 0.14, ""style"": ""goblin"" }, ""chestR"": 0.17, ""waistR"": 0.14, ""hipR"": 0.12,
    ""arm"": { ""upper"": 0.3, ""fore"": 0.29, ""r0"": 0.075, ""r1"": 0.06, ""foreR1"": 0.05, ""hand"": 0.058 },
    ""leg"": { ""thigh"": 0.38, ""shin"": 0.38, ""r0"": 0.1, ""r1"": 0.075, ""shinR1"": 0.06 },
    ""helmet"": ""none"", ""clothes"": ""loincloth"", ""belt"": 1 },

  { ""name"": ""fenring"", ""words"": [""fenrings"", ""werewolf"", ""werewolves""], ""scale"": 1.2, ""hunch"": 22,
    ""head"": { ""r"": 0.12, ""style"": ""wolf"" }, ""chestR"": 0.21, ""waistR"": 0.14, ""hipR"": 0.12,
    ""arm"": { ""upper"": 0.36, ""fore"": 0.34, ""r0"": 0.085, ""r1"": 0.065, ""foreR1"": 0.05, ""hand"": 0.06 },
    ""leg"": { ""thigh"": 0.4, ""shin"": 0.52, ""r0"": 0.11, ""r1"": 0.07, ""shinR1"": 0.045, ""style"": ""digitigrade"" },
    ""helmet"": ""none"", ""clothes"": ""none"", ""fur"": 1 },

  { ""name"": ""dverger"", ""words"": [""dvergers"", ""dvergr"", ""dwarf"", ""dwarves""], ""scale"": 0.75,
    ""head"": { ""r"": 0.15 }, ""spine"": 0.45, ""chestR"": 0.22, ""waistR"": 0.2, ""hipR"": 0.15,
    ""arm"": { ""upper"": 0.27, ""fore"": 0.25, ""r0"": 0.09, ""r1"": 0.075, ""foreR1"": 0.065, ""hand"": 0.07 },
    ""leg"": { ""thigh"": 0.32, ""shin"": 0.3, ""r0"": 0.12, ""r1"": 0.095, ""shinR1"": 0.08 },
    ""helmet"": ""round"", ""beard"": 1, ""clothes"": ""tunic"", ""belt"": 1, ""boots"": 1 },

  { ""name"": ""berserker"", ""words"": [""berserkers"", ""fulingberserker"", ""fulingberserkers"", ""goblinbrute""], ""scale"": 1.3, ""hunch"": 22,
    ""head"": { ""r"": 0.14, ""style"": ""goblin"" }, ""chestR"": 0.24, ""waistR"": 0.2, ""hipR"": 0.14,
    ""arm"": { ""upper"": 0.34, ""fore"": 0.33, ""r0"": 0.11, ""r1"": 0.09, ""foreR1"": 0.08, ""hand"": 0.08 },
    ""leg"": { ""thigh"": 0.38, ""shin"": 0.36, ""r0"": 0.13, ""r1"": 0.1, ""shinR1"": 0.08 },
    ""helmet"": ""horned"", ""clothes"": ""loincloth"", ""belt"": 1 },

  { ""name"": ""fulingshaman"", ""words"": [""fulingshamans"", ""goblinshaman""], ""scale"": 0.85, ""hunch"": 20,
    ""head"": { ""r"": 0.14, ""style"": ""goblin"" }, ""horns"": 0.14, ""chestR"": 0.16, ""waistR"": 0.13, ""hipR"": 0.12,
    ""arm"": { ""upper"": 0.3, ""fore"": 0.29, ""r0"": 0.07, ""r1"": 0.058, ""foreR1"": 0.05, ""hand"": 0.056 },
    ""leg"": { ""thigh"": 0.38, ""shin"": 0.38, ""r0"": 0.095, ""r1"": 0.072, ""shinR1"": 0.058 },
    ""helmet"": ""none"", ""clothes"": ""robe"" },

  { ""name"": ""greydwarfbrute"", ""words"": [""greydwarfbrutes"", ""brute"", ""brutes""], ""scale"": 1.35, ""hunch"": 16,
    ""head"": { ""r"": 0.2, ""style"": ""greydwarf"" }, ""spine"": 0.46, ""neck"": 0.02, ""chestR"": 0.21, ""waistR"": 0.17, ""hipR"": 0.14,
    ""arm"": { ""upper"": 0.38, ""fore"": 0.38, ""r0"": 0.09, ""r1"": 0.075, ""foreR1"": 0.07, ""hand"": 0.08 },
    ""leg"": { ""thigh"": 0.38, ""shin"": 0.38, ""r0"": 0.11, ""r1"": 0.085, ""shinR1"": 0.07, ""footLen"": 0.17, ""footR"": 0.045 },
    ""helmet"": ""none"", ""clothes"": ""none"" },

  { ""name"": ""greydwarfshaman"", ""words"": [""greydwarfshamans"", ""shaman"", ""shamans""], ""scale"": 0.8, ""hunch"": 14,
    ""head"": { ""r"": 0.2, ""style"": ""greydwarf"" }, ""horns"": 0.22, ""spine"": 0.42, ""neck"": 0.02,
    ""chestR"": 0.14, ""waistR"": 0.11, ""hipR"": 0.1,
    ""arm"": { ""upper"": 0.36, ""fore"": 0.36, ""r0"": 0.05, ""r1"": 0.042, ""foreR1"": 0.036, ""hand"": 0.05 },
    ""leg"": { ""thigh"": 0.38, ""shin"": 0.4, ""r0"": 0.065, ""r1"": 0.048, ""shinR1"": 0.038, ""footLen"": 0.15, ""footR"": 0.035 },
    ""helmet"": ""none"", ""clothes"": ""none"" },

  { ""name"": ""surtling"", ""words"": [""surtlings"", ""frysling"", ""fryslings"", ""imp"", ""imps""], ""scale"": 0.9, ""hunch"": 10,
    ""head"": { ""r"": 0.16, ""style"": ""imp"" }, ""horns"": 0.16, ""chestR"": 0.17, ""waistR"": 0.12, ""hipR"": 0.11,
    ""arm"": { ""upper"": 0.3, ""fore"": 0.28, ""r0"": 0.07, ""r1"": 0.055, ""foreR1"": 0.045, ""hand"": 0.055 },
    ""leg"": { ""thigh"": 0.38, ""shin"": 0.38, ""r0"": 0.09, ""r1"": 0.065, ""shinR1"": 0.05 },
    ""helmet"": ""none"", ""clothes"": ""none"" },

  { ""name"": ""charred"", ""words"": [""charredwarrior"", ""charredwarriors"", ""twitcher"", ""twitchers"", ""charredarcher""],
    ""head"": { ""r"": 0.125, ""style"": ""skull"" }, ""chestR"": 0.17, ""waistR"": 0.05, ""hipR"": 0.12, ""hunch"": 8,
    ""arm"": { ""r0"": 0.038, ""r1"": 0.032, ""foreR1"": 0.03, ""hand"": 0.047 },
    ""leg"": { ""r0"": 0.048, ""r1"": 0.038, ""shinR1"": 0.034, ""footR"": 0.036 },
    ""helmet"": ""nasal"", ""clothes"": ""ragged"", ""bones"": 1 },

  { ""name"": ""golem"", ""words"": [""golems"", ""stonegolem"", ""stonegolems""], ""scale"": 2.3, ""hunch"": 18,
    ""head"": { ""r"": 0.14, ""style"": ""rock"" }, ""neck"": 0.0, ""chestR"": 0.3, ""waistR"": 0.24, ""hipR"": 0.2,
    ""arm"": { ""upper"": 0.4, ""fore"": 0.42, ""r0"": 0.16, ""r1"": 0.13, ""foreR1"": 0.15, ""hand"": 0.15 },
    ""leg"": { ""thigh"": 0.32, ""shin"": 0.3, ""r0"": 0.17, ""r1"": 0.14, ""shinR1"": 0.14, ""footR"": 0.08 },
    ""helmet"": ""none"", ""clothes"": ""none"" },

  { ""name"": ""elder"", ""words"": [""theelder""], ""scale"": 2.6, ""hunch"": 8,
    ""head"": { ""r"": 0.2, ""style"": ""greydwarf"" }, ""chestR"": 0.2, ""waistR"": 0.17, ""hipR"": 0.15,
    ""arm"": { ""upper"": 0.45, ""fore"": 0.45, ""r0"": 0.1, ""r1"": 0.085, ""foreR1"": 0.075, ""hand"": 0.08 },
    ""leg"": { ""thigh"": 0.5, ""shin"": 0.5, ""r0"": 0.13, ""r1"": 0.1, ""shinR1"": 0.09, ""footR"": 0.05 },
    ""helmet"": ""none"", ""clothes"": ""none"" },

  { ""name"": ""barka"", ""words"": [""barkas""], ""scale"": 1.5, ""hunch"": 12,
    ""head"": { ""r"": 0.18, ""style"": ""greydwarf"" }, ""chestR"": 0.16, ""waistR"": 0.13, ""hipR"": 0.12,
    ""arm"": { ""upper"": 0.42, ""fore"": 0.42, ""r0"": 0.07, ""r1"": 0.06, ""foreR1"": 0.05, ""hand"": 0.06 },
    ""leg"": { ""thigh"": 0.45, ""shin"": 0.45, ""r0"": 0.09, ""r1"": 0.07, ""shinR1"": 0.06, ""footR"": 0.04 },
    ""helmet"": ""none"", ""clothes"": ""none"" },

  { ""name"": ""yagluth"", ""words"": [""goblinking"", ""fulingking""], ""scale"": 2.2, ""hunch"": 12,
    ""head"": { ""r"": 0.14, ""style"": ""skull"" }, ""horns"": 0.3, ""chestR"": 0.19, ""waistR"": 0.06, ""hipR"": 0.12,
    ""arm"": { ""upper"": 0.36, ""fore"": 0.36, ""r0"": 0.045, ""r1"": 0.038, ""foreR1"": 0.034, ""hand"": 0.06 },
    ""helmet"": ""none"", ""clothes"": ""float"", ""bones"": 1 },

  { ""name"": ""odin"", ""words"": [""wanderer"", ""allfather""], ""scale"": 1.05,
    ""helmet"": ""hood"", ""beard"": 1, ""clothes"": ""robe"" },

  { ""name"": ""bogwitch"", ""words"": [""bogwitches"", ""witch"", ""witches"", ""hag""], ""hunch"": 24,
    ""chestR"": 0.17, ""waistR"": 0.15, ""helmet"": ""hood"", ""clothes"": ""robe"" },

  { ""name"": ""morgen"", ""words"": [""morgens""], ""scale"": 1.4, ""hunch"": 16,
    ""head"": { ""r"": 0.13, ""style"": ""morgen"" }, ""chestR"": 0.17, ""waistR"": 0.12, ""hipR"": 0.12,
    ""arm"": { ""upper"": 0.42, ""fore"": 0.42, ""r0"": 0.065, ""r1"": 0.05, ""foreR1"": 0.04, ""hand"": 0.06 },
    ""leg"": { ""thigh"": 0.5, ""shin"": 0.5, ""r0"": 0.075, ""r1"": 0.055, ""shinR1"": 0.045 },
    ""helmet"": ""none"", ""clothes"": ""none"" },

  { ""name"": ""ghost"", ""words"": [""ghosts"", ""spirit"", ""spirits""], ""helmet"": ""hood"", ""clothes"": ""float"" },

  { ""name"": ""wraith"", ""words"": [""wraiths""], ""scale"": 1.3, ""hunch"": 10, ""helmet"": ""hood"", ""clothes"": ""float"",
    ""arm"": { ""upper"": 0.36, ""fore"": 0.34, ""r0"": 0.06, ""r1"": 0.05, ""foreR1"": 0.045, ""hand"": 0.06 } },

  { ""name"": ""fallenwarrior"", ""words"": [""fallenwarriors""], ""chestR"": 0.23, ""waistR"": 0.17,
    ""arm"": { ""r0"": 0.1, ""r1"": 0.082, ""foreR1"": 0.07 }, ""helmet"": ""none"", ""clothes"": ""loincloth"", ""belt"": 1, ""boots"": 1 },

  { ""name"": ""valkyrie"", ""words"": [""valkyries"", ""fallenvalkyrie"", ""fallenvalkyries""], ""wings"": 0.95,
    ""chestR"": 0.185, ""waistR"": 0.135, ""hipR"": 0.15, ""helmet"": ""nasal"", ""braid"": 1, ""clothes"": ""long"", ""belt"": 1, ""boots"": 1 },

  { ""name"": ""jotun"", ""words"": [""jotuns"", ""jotunn"", ""jotnar"", ""giant"", ""giants"", ""jotunwarrior""], ""scale"": 1.8,
    ""chestR"": 0.25, ""waistR"": 0.2, ""hipR"": 0.15,
    ""arm"": { ""r0"": 0.11, ""r1"": 0.09, ""foreR1"": 0.08, ""hand"": 0.08 }, ""leg"": { ""r0"": 0.14, ""r1"": 0.11, ""shinR1"": 0.09 },
    ""helmet"": ""horned"", ""beard"": 1, ""clothes"": ""tunic"", ""belt"": 1, ""boots"": 1 },

  { ""name"": ""jotunwitch"", ""words"": [""jotunwitches"", ""giantess""], ""scale"": 1.7,
    ""chestR"": 0.19, ""waistR"": 0.14, ""hipR"": 0.15, ""helmet"": ""horned"", ""braid"": 1, ""clothes"": ""robe"" }
] }";

        static readonly Dictionary<string, Species> ByWord = new Dictionary<string, Species>(StringComparer.OrdinalIgnoreCase);
        public static readonly List<Species> Bodies = new List<Species>();

        static Humanoid()
        {
            var root = (Dictionary<string, object>)MiniJson.Parse(BuiltinBodies);
            foreach (Dictionary<string, object> d in (List<object>)root["bodies"])
            {
                var b = SpeciesLibrary.Read(d);
                Bodies.Add(b);
                ByWord[b.Name] = b;
                foreach (var w in b.Words) ByWord[w] = b;
            }
        }

        /// <summary>The body a name asks for ("viking", "trolls"), or null if it isn't a humanoid figure.</summary>
        public static Species BodyFor(string name) => name != null && ByWord.TryGetValue(Norm(name), out var b) ? b : null;

        static string Norm(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetter).ToArray());

        // ---- the body being built (set by Build; the parts' shapes read it while the figure is baked)
        [ThreadStatic] static Species _b;
        static float B(string key, float viking) => _b.Get(key, viking);
        static string S(string key, string viking) => _b.Str(key, viking);

        static float HeadR => B("head.r", 0.135f);
        static float NeckLen => B("neck", 0.07f);
        static float SpineLen => B("spine", 0.52f);
        static float ChestR => B("chestR", 0.2f);
        static float WaistR => B("waistR", 0.16f);
        static float HipR => B("hipR", 0.13f);
        static float UpperArm => B("arm.upper", 0.31f);
        static float ForeArm => B("arm.fore", 0.27f);
        static float ArmR0 => B("arm.r0", 0.088f);
        static float ArmR1 => B("arm.r1", 0.072f);
        static float ForeR1 => B("arm.foreR1", 0.06f);
        static float HandR => B("arm.hand", 0.066f);
        static float Thigh => B("leg.thigh", 0.45f);
        static float Shin => B("leg.shin", 0.42f);
        static float ThighR0 => B("leg.r0", 0.12f);
        static float ThighR1 => B("leg.r1", 0.09f);
        static float ShinR1 => B("leg.shinR1", 0.07f);
        static float FootLen => B("leg.footLen", 0.19f);
        static float FootR => B("leg.footR", 0.046f);

        // ---------------------------------------------------------------- building

        static Vector2 Dir(float degFromDown) { float a = degFromDown * M.Pi / 180f; return new Vector2(M.Sin(a), -M.Cos(a)); }
        static Vector2 UpDir(float degLean) { float a = degLean * M.Pi / 180f; return new Vector2(M.Sin(a), M.Cos(a)); }

        /// <summary>A humanoid pose's angles, read from a <see cref="Pose"/> (with the defaults for anything left out).</summary>
        sealed class HPose
        {
            public float Lean, HeadTilt, NearUpper, NearFore, FarUpper, FarFore, NearThigh, NearShin, FarThigh, FarShin;
            public float NearFoot, FarFoot, NearGrip, FarGrip;
            public bool FarArmInFront;

            public static HPose From(Pose p) => new HPose
            {
                Lean = p.Get("lean", 0), HeadTilt = p.Get("head", 0),
                NearUpper = p.Get("nearArm.upper", 10), NearFore = p.Get("nearArm.fore", 20), NearGrip = p.Get("nearArm.grip", 90),
                FarUpper = p.Get("farArm.upper", -5), FarFore = p.Get("farArm.fore", 10), FarGrip = p.Get("farArm.grip", 90),
                FarArmInFront = p.Flag("farArm.inFront"),
                NearThigh = p.Get("nearLeg.thigh", 8), NearShin = p.Get("nearLeg.shin", 0), NearFoot = p.Get("nearLeg.foot", 90),
                FarThigh = p.Get("farLeg.thigh", -8), FarShin = p.Get("farLeg.shin", -4), FarFoot = p.Get("farLeg.foot", 90),
            };
        }

        /// <summary>The posed figure as parts, back to front. Feet rest on y = 0.</summary>
        public static List<Part> Build(Species body, Pose posed, IList<string> held)
        {
            _b = body ?? Bodies[0];
            var pose = HPose.From(posed ?? PoseLibrary.Resolve("humanoid", "standing", null));
            // a hunched body leans further forward in every pose; the head tilts back to keep looking ahead
            float hunch = B("hunch", 0);
            pose.Lean += hunch;
            pose.HeadTilt -= hunch * 0.8f;
            held = held ?? new string[0];
            bool bones = B("bones", 0) > 0.5f;

            // which hand holds what: the first weapon in the near (right) hand, a shield in the far hand,
            // a second weapon in the far hand
            string nearItem = null, farItem = null;
            foreach (var h in held)
            {
                if (h == "shield") { if (farItem == null || farItem != "shield") farItem = "shield"; continue; }
                if (nearItem == null) nearItem = h;
                else if (farItem == null) farItem = h;
            }

            // ---- skeleton (hip at the origin for now)
            var hip = new Vector2(0, 0);
            var up = UpDir(pose.Lean);
            var shoulderLine = hip + up * SpineLen;
            var shoulder = shoulderLine + new Vector2(0.02f, -0.04f).Rotate(pose.Lean);
            var neckBase = shoulderLine + up * 0.02f;
            var headUp = UpDir(pose.Lean + pose.HeadTilt);
            var headC = neckBase + headUp * (NeckLen + HeadR * 0.9f) + new Vector2(0.03f, 0);

            Vector2 nearElbow = shoulder + Dir(pose.NearUpper) * UpperArm, nearWrist = nearElbow + Dir(pose.NearFore) * ForeArm;
            Vector2 farShoulder = shoulder + new Vector2(-0.02f, 0.01f);
            Vector2 farElbow = farShoulder + Dir(pose.FarUpper) * UpperArm, farWrist = farElbow + Dir(pose.FarFore) * ForeArm;
            Vector2 farHip = hip + new Vector2(-0.02f, 0.01f);
            var nearLegPts = LegJoints(hip, pose.NearThigh, pose.NearShin);
            var farLegPts = LegJoints(farHip, pose.FarThigh, pose.FarShin);

            var parts = new List<Part>();

            // back to front (Hippo, 2026-10-31): a shield is the first thing drawn, behind everything, held in the far
            // hand; the far arm lies over it, and the body over both. "inFront" brings a far arm without a shield forward.
            bool shield = farItem == "shield";
            bool farFront = pose.FarArmInFront && !shield;
            var farLeg = Leg("far leg", farHip, farLegPts, pose.FarFoot);
            var farArm = Arm("far arm", farShoulder, farElbow, farWrist);
            var farHand = Hand("far hand", farWrist, pose.FarFore);
            string clothesStyle = S("clothes", "tunic");
            bool floating = clothesStyle == "float";
            float wings = B("wings", 0);
            if (wings > 0) parts.Add(Wings(shoulderLine, up, pose.Lean, wings));
            if (shield) parts.Add(Props.RoundShield(farWrist + Dir(pose.FarFore) * 0.035f));
            if (!farFront)
            {
                parts.Add(farArm);
                if (farItem != null && !shield) parts.Add(Props.Weapon(farItem, farWrist, pose.FarFore + pose.FarGrip));
                parts.Add(farHand);
            }
            if (!floating) parts.Add(farLeg);

            // body
            float tilt = pose.Lean + pose.HeadTilt;
            parts.Add(bones ? Ribcage(hip, shoulderLine, up, pose.Lean) : Torso(hip, shoulderLine, up, pose.Lean));
            if (B("braid", 0) > 0.5f) parts.Add(Braid(headC, shoulderLine, up, tilt));
            string helmet = S("helmet", "nasal");
            float horns = B("horns", helmet == "horned" ? 0.16f : 0);
            if (horns > 0) parts.Add(Horn("far horn", headC + new Vector2(-0.03f, 0.01f), tilt, horns));
            if (helmet == "hood") parts.Add(Hood(headC, shoulderLine, up, tilt));
            parts.Add(Head(headC, tilt));
            if (helmet == "nasal" || helmet == "horned") parts.Add(Helmet(headC, tilt));
            if (helmet == "round") parts.Add(RoundCap(headC, tilt));
            if (B("beard", 0) > 0.5f) parts.Add(Beard(headC, tilt));
            if (horns > 0) parts.Add(Horn("horn", headC, tilt, horns));

            // near leg, then clothes over the tops of both legs
            if (!floating) parts.Add(Leg("near leg", hip, nearLegPts, pose.NearFoot));
            var clothes = Clothes(hip, up, pose.Lean, nearLegPts.knee, farLegPts.knee, M.Min(nearLegPts.foot.Y, farLegPts.foot.Y));
            if (clothes != null) parts.Add(clothes);

            if (farFront)
            {
                parts.Add(farArm);
                if (farItem != null) parts.Add(Props.Weapon(farItem, farWrist, pose.FarFore + pose.FarGrip));
                parts.Add(farHand);
            }

            // near arm and what it holds, in front of everything
            parts.Add(Arm("near arm", shoulder, nearElbow, nearWrist));
            if (nearItem != null) parts.Add(Props.Weapon(nearItem, nearWrist, pose.NearFore + pose.NearGrip));
            parts.Add(Hand("near hand", nearWrist, pose.NearFore));

            // stand the figure on y = 0, and size it
            // a floating figure (ghost, wraith) hovers a little above the ground line
            float low = floating ? hip.Y - (Thigh + Shin) * 0.95f : M.Min(nearLegPts.foot.Y, farLegPts.foot.Y) - FootR;
            var shift = new Vector2(0, -low);
            float scale = B("scale", 1);
            foreach (var p in parts) { p.Offset = shift; p.Scale = scale; }
            return parts;
        }

        // ---------------------------------------------------------------- legs

        /// <summary>
        /// Knee, ankle and where the foot meets the ground. Plantigrade legs (people) bend once; digitigrade legs
        /// (the fenring) stand on their toes, with the hock bending back between a short shin and a long foot.
        /// </summary>
        static (Vector2 knee, Vector2 ankle, Vector2 foot) LegJoints(Vector2 hip, float thighDeg, float shinDeg)
        {
            if (S("leg.style", "plantigrade") == "digitigrade")
            {
                var knee = hip + Dir(thighDeg + 22) * Thigh;
                var hock = knee + Dir(shinDeg - 38) * (Shin * 0.55f);
                var ball = hock + Dir(shinDeg + 12) * (Shin * 0.5f);
                return (knee, hock, ball);
            }
            var k = hip + Dir(thighDeg) * Thigh;
            var a = k + Dir(shinDeg) * Shin;
            return (k, a, a);
        }

        static Part Leg(string name, Vector2 hip, (Vector2 knee, Vector2 ankle, Vector2 foot) j, float footDeg)
        {
            bool digi = j.foot.X != j.ankle.X || j.foot.Y != j.ankle.Y;
            var toe = j.foot + Dir(digi ? 92 : footDeg) * (digi ? FootLen * 0.7f : FootLen);
            var heel = j.foot + Dir(footDeg) * -0.04f;
            float thighR0 = ThighR0, thighR1 = ThighR1, shinR1 = ShinR1, footR = FootR;
            bool boots = B("boots", 0) > 0.5f, bones = B("bones", 0) > 0.5f;
            // boot top: a cut line across the shin a little above the ankle
            var bootAt = Vector2.Lerp(j.ankle, j.knee, 0.3f);
            var across = (j.knee - j.ankle).Normalized.Perp * (shinR1 + 0.03f);
            var footFrom = digi ? j.foot : heel;
            var footDir = (toe - footFrom).Normalized;
            var footA = footFrom + footDir * (footR * 0.5f);
            var footB = toe - footDir * (footR * 0.5f);
            return new Part(name, (x, y) =>
            {
                float d = Sdf2.Capsule(x, y, hip, j.knee, thighR0, thighR1);
                d = M.Min(d, Sdf2.Capsule(x, y, j.knee, j.ankle, thighR1, shinR1));
                if (digi) d = M.Min(d, Sdf2.Capsule(x, y, j.ankle, j.foot, shinR1, footR));
                // the foot half as thick again as it was (Hippo, 2026-10-31), its ends pulled in so it's no longer
                d = M.Min(d, Sdf2.Capsule(x, y, footA, footB, footR * 1.5f + 0.006f, footR * 1.5f - 0.008f));
                // bare bones get knobbly joints
                if (bones)
                {
                    d = M.Min(d, Sdf.Circle(x - j.knee.X, y - j.knee.Y, thighR1 * 1.5f));
                    d = M.Min(d, Sdf.Circle(x - j.ankle.X, y - j.ankle.Y, shinR1 * 1.4f));
                }
                return d;
            }, boots ? (Func<float, float, float>)((x, y) => Sdf.Segment(x, y, bootAt.X - across.X, bootAt.Y - across.Y, bootAt.X + across.X, bootAt.Y + across.Y)) : null);
        }

        static Part Arm(string name, Vector2 shoulder, Vector2 elbow, Vector2 wrist)
        {
            float r0 = ArmR0, r1 = ArmR1, fr = ForeR1;
            bool bones = B("bones", 0) > 0.5f;
            return new Part(name, (x, y) =>
            {
                float d = M.Min(Sdf2.Capsule(x, y, shoulder, elbow, r0, r1), Sdf2.Capsule(x, y, elbow, wrist, r1, fr));
                if (bones) d = M.Min(d, Sdf.Circle(x - elbow.X, y - elbow.Y, r1 * 1.5f));
                return d;
            });
        }

        static Part Hand(string name, Vector2 wrist, float foreDeg)
        {
            var c = wrist + Dir(foreDeg) * 0.035f;
            float r = HandR;
            return new Part(name, (x, y) => Sdf.Circle(x - c.X, y - c.Y, r));
        }

        // ---------------------------------------------------------------- body

        static Part Torso(Vector2 hip, Vector2 shoulderLine, Vector2 up, float lean)
        {
            // a deep chest, a narrower waist and a rounded seat, leaning with the spine
            float waistR = WaistR, chestR = ChestR, hipR = HipR, neck = NeckLen;
            var chest = shoulderLine - up * 0.12f + new Vector2(0.02f, 0).Rotate(lean);
            var belly = hip + up * 0.16f + new Vector2(0.01f, 0).Rotate(lean);
            var seat = hip + new Vector2(-0.03f, -0.02f).Rotate(lean);
            bool belt = B("belt", 0) > 0.5f;
            // belt: two cut lines across the waist
            var beltC = hip + up * 0.12f;
            var across = up.Perp * (waistR + 0.04f);
            var belt2 = beltC + up * 0.045f;
            return new Part("torso", (x, y) =>
            {
                float d = Sdf2.Capsule(x, y, belly, chest, waistR, chestR);
                d = M.Min(d, Sdf.Circle(x - seat.X, y - seat.Y, hipR));
                d = M.Min(d, Sdf2.Capsule(x, y, shoulderLine, shoulderLine + up * (neck + 0.03f), 0.06f, 0.055f));
                return d;
            }, belt ? (Func<float, float, float>)((x, y) => M.Min(Sdf.Segment(x, y, beltC.X - across.X, beltC.Y - across.Y, beltC.X + across.X, beltC.Y + across.Y),
                                Sdf.Segment(x, y, belt2.X - across.X, belt2.Y - across.Y, belt2.X + across.X, belt2.Y + across.Y))) : null);
        }

        /// <summary>A skeleton's body: a rib cage with the gaps between ribs cut, a bare spine and a pelvis.</summary>
        static Part Ribcage(Vector2 hip, Vector2 shoulderLine, Vector2 up, float lean)
        {
            float chestR = ChestR, hipR = HipR, neck = NeckLen;
            var fwd = up.Perp * -1f;   // towards the face
            var cageTop = shoulderLine - up * 0.06f;
            var cageBot = shoulderLine - up * 0.3f + fwd * 0.02f;
            var cageC = Vector2.Lerp(cageTop, cageBot, 0.5f);
            var spineBack = fwd * -(chestR * 0.75f);
            var pelvis = hip + up * 0.03f;
            return new Part("ribcage", (x, y) =>
            {
                float d = Sdf2.Capsule(x, y, cageTop, cageBot, chestR * 0.85f, chestR * 0.7f);
                d = M.Min(d, Sdf2.Capsule(x, y, hip + spineBack * 0.6f, shoulderLine + spineBack, 0.03f, 0.03f));
                d = M.Min(d, Sdf2.Capsule(x, y, pelvis - fwd * (hipR * 0.5f), pelvis + fwd * (hipR * 0.5f), hipR * 0.6f, hipR * 0.45f));
                d = M.Min(d, Sdf2.Capsule(x, y, shoulderLine, shoulderLine + up * (neck + 0.03f), 0.03f, 0.03f));
                return d;
            }, (x, y) =>
            {
                // ribs: curved cuts across the cage, sloping down towards the front
                float best = float.MaxValue;
                for (int i = 1; i <= 5; i++)
                {
                    var c = Vector2.Lerp(cageTop, cageBot, i / 6f);
                    var a = c + spineBack * 0.9f;
                    var b = c + fwd * (chestR * 0.9f) - up * 0.04f;
                    best = M.Min(best, Sdf.Segment(x, y, a.X, a.Y, b.X, b.Y));
                }
                return best;
            });
        }

        /// <summary>What's worn over the hips: a tunic skirt, a long one, a loincloth, a ragged one (draugr), or nothing.</summary>
        static Part Clothes(Vector2 hip, Vector2 up, float lean, Vector2 nearKnee, Vector2 farKnee, float footY)
        {
            string style = S("clothes", "tunic");
            if (style == "none") return null;
            if (style == "robe" || style == "float")
            {
                // a robe from the chest to the ankles (or, floating, tapering into tatters where the legs would be)
                float chestR = ChestR;
                var topF = hip + up * (SpineLen * 0.75f) + new Vector2(chestR * 0.9f, 0).Rotate(lean);
                var topB = hip + up * (SpineLen * 0.75f) + new Vector2(-chestR * 0.95f, 0).Rotate(lean);
                float bottom = style == "float" ? hip.Y - (Thigh + Shin) * 0.9f : footY + 0.05f;
                float spread = style == "float" ? 0.6f : 1f;
                var robe = new List<Vector2> { topB, topF, new Vector2(hip.X + (chestR + 0.12f) * spread, bottom + (style == "float" ? 0.25f : 0)) };
                int tatters = style == "float" ? 4 : 0;
                for (int i = 1; i <= tatters * 2 - 1; i++)
                {
                    float t = i / (tatters * 2f);
                    float x = M.Lerp(hip.X + (chestR + 0.12f) * spread, hip.X - (chestR + 0.2f) * spread, t);
                    robe.Add(new Vector2(x, i % 2 == 1 ? bottom : bottom + 0.22f));
                }
                robe.Add(new Vector2(hip.X - (chestR + 0.2f) * spread, bottom + (style == "float" ? 0.2f : 0)));
                var robePoly = robe.SelectMany(v => new[] { v.X, v.Y }).ToArray();
                var foldA = hip + up * 0.1f; var foldB = new Vector2(hip.X + 0.02f, bottom + 0.1f);
                return new Part("robe", (x, y) => Sdf.Polygon(x, y, robePoly) - 0.015f,
                    (x, y) => Sdf.Segment(x, y, foldA.X, foldA.Y, foldB.X, foldB.Y));
            }
            float waistR = WaistR, thighR0 = ThighR0;
            var waistF = hip + up * 0.1f + new Vector2(waistR + 0.005f, 0).Rotate(lean);
            var waistB = hip + up * 0.1f + new Vector2(-waistR - 0.005f, 0).Rotate(lean);
            if (style == "loincloth")
            {
                // a flap hanging in front between the legs, and the waistband
                var a = hip + up * 0.08f + new Vector2(waistR * 0.2f, 0).Rotate(lean);
                var b = hip + up * 0.08f + new Vector2(waistR + 0.01f, 0).Rotate(lean);
                var c = hip - up * (Thigh * 0.55f) + new Vector2(waistR * 0.85f, 0).Rotate(lean);
                var d = hip - up * (Thigh * 0.5f) + new Vector2(waistR * 0.15f, 0).Rotate(lean);
                var flap = new[] { a.X, a.Y, b.X, b.Y, c.X, c.Y, d.X, d.Y };
                var band = new[] { waistB.X, waistB.Y, waistF.X, waistF.Y, (waistF - up * 0.08f).X, (waistF - up * 0.08f).Y, (waistB - up * 0.08f).X, (waistB - up * 0.08f).Y };
                return new Part("loincloth", (x, y) => M.Min(Sdf.Polygon(x, y, flap), Sdf.Polygon(x, y, band)) - 0.012f);
            }
            float hemAt = style == "long" ? 0.85f : 0.62f;
            // the hem a way down the thighs, a little wider than the legs there
            var nearHem = Vector2.Lerp(hip, nearKnee, hemAt);
            var farHem = Vector2.Lerp(hip, farKnee, hemAt);
            var front = nearHem.X >= farHem.X ? nearHem : farHem;
            var back = nearHem.X >= farHem.X ? farHem : nearHem;
            var hemF = front + new Vector2(thighR0 + 0.03f, -0.01f);
            var hemB = back + new Vector2(-thighR0 - 0.03f, -0.01f);
            var pts = new List<Vector2> { waistB, waistF, hemF };
            if (style == "ragged")
            {
                // a torn hem: points hanging between the corners
                for (int i = 1; i <= 5; i++)
                {
                    var p = Vector2.Lerp(hemF, hemB, i / 6f);
                    pts.Add(p + new Vector2(0, i % 2 == 1 ? -0.09f : 0.02f));
                }
            }
            pts.Add(hemB);
            var poly = pts.SelectMany(p => new[] { p.X, p.Y }).ToArray();
            return new Part("tunic", (x, y) => Sdf.Polygon(x, y, poly) - 0.015f);
        }

        /// <summary>A plait from under the back of the helmet down the back, with cuts across it.</summary>
        static Part Braid(Vector2 headC, Vector2 shoulderLine, Vector2 up, float tilt)
        {
            var top = headC + new Vector2(-0.10f, -0.03f).Rotate(tilt);
            var end = shoulderLine + up * -0.26f + new Vector2(-ChestR - 0.03f, 0).Rotate(tilt);
            var along = end - top;
            return new Part("braid", (x, y) => M.Min(Sdf2.Capsule(x, y, top, end, 0.05f, 0.032f), Sdf.Circle(x - end.X, y - end.Y, 0.04f)),
                (x, y) =>
                {
                    // a cut across the plait every few centimetres
                    float best = float.MaxValue;
                    for (int i = 1; i <= 6; i++)
                    {
                        var c = Vector2.Lerp(top, end, i / 7f);
                        var a = along.Normalized.Perp * 0.05f + along.Normalized * -0.02f;
                        best = M.Min(best, Sdf.Segment(x, y, c.X - a.X, c.Y - a.Y, c.X + a.X, c.Y + a.Y));
                    }
                    return best;
                });
        }

        // ---------------------------------------------------------------- heads

        static Part Head(Vector2 c, float tilt)
        {
            float r = HeadR;
            Vector2 At(float x, float y) => c + new Vector2(x, y).Rotate(tilt);
            float[] Poly(params Vector2[] p) => p.SelectMany(v => new[] { v.X, v.Y }).ToArray();
            switch (S("head.style", "human"))
            {
                case "skull":
                {
                    // a bare skull: cranium, a jaw under the front, a big eye socket and a nose hole cut in
                    var jawA = At(r * 0.25f, -r * 0.65f); var jawB = At(r * 0.75f, -r * 0.7f);
                    var eye = At(r * 0.5f, -r * 0.05f); var noseH = At(r * 0.85f, -r * 0.35f);
                    var teethA = At(r * 0.35f, -r * 0.55f); var teethB = At(r * 0.95f, -r * 0.55f);
                    return new Part("head", (x, y) => M.Min(Sdf.Circle(x - c.X, y - c.Y, r), Sdf2.Capsule(x, y, jawA, jawB, r * 0.32f, r * 0.26f)),
                        (x, y) => M.Min(M.Min(M.Abs(Sdf.Circle(x - eye.X, y - eye.Y, r * 0.24f)), M.Abs(Sdf.Circle(x - noseH.X, y - noseH.Y, r * 0.08f))),
                                        Sdf.Segment(x, y, teethA.X, teethA.Y, teethB.X, teethB.Y)));
                }
                case "greydwarf":
                {
                    // a big lumpy head with twigs sticking out of the top and back, and a single glowing eye
                    var twigs = new List<(Vector2, Vector2)>();
                    for (int i = 0; i < 7; i++)
                    {
                        float a = 30 + i * 24;   // fanning from the brow over the top to the back
                        var root = c + UpDir(tilt - 90 + a) * (r * 0.8f);
                        var tip = c + UpDir(tilt - 90 + a + (i % 2 == 0 ? 8 : -6)) * (r * (1.45f + 0.12f * (i % 3)));
                        twigs.Add((root, tip));
                    }
                    var eye = At(r * 0.55f, r * 0.05f);
                    var brow = At(r * 0.95f, -r * 0.1f);
                    return new Part("head", (x, y) =>
                    {
                        float d = Sdf.Circle(x - c.X, y - c.Y, r);
                        d = M.Min(d, Sdf.Circle(x - brow.X, y - brow.Y, r * 0.3f));
                        foreach (var (a0, b0) in twigs) d = M.Min(d, Sdf2.Capsule(x, y, a0, b0, r * 0.07f, r * 0.03f));
                        return d;
                    }, (x, y) => M.Abs(Sdf.Circle(x - eye.X, y - eye.Y, r * 0.13f)));
                }
                case "troll":
                {
                    // a small head with a heavy brow and a big drooping nose
                    var nose = Poly(At(r * 0.7f, r * 0.15f), At(r * 1.75f, -r * 0.55f), At(r * 1.35f, -r * 0.85f), At(r * 0.75f, -r * 0.55f));
                    var browA = At(r * 0.2f, r * 0.45f); var browB = At(r * 1.0f, r * 0.25f);
                    var eye = At(r * 0.6f, r * 0.05f);
                    var mouthA = At(r * 0.45f, -r * 0.75f); var mouthB = At(r * 0.95f, -r * 0.8f);
                    return new Part("head", (x, y) => M.Min(M.Min(Sdf.Circle(x - c.X, y - c.Y, r), Sdf.Polygon(x, y, nose) - r * 0.08f),
                                                          Sdf2.Capsule(x, y, browA, browB, r * 0.28f, r * 0.22f)),
                        (x, y) => M.Min(M.Abs(Sdf.Circle(x - eye.X, y - eye.Y, r * 0.1f)), Sdf.Segment(x, y, mouthA.X, mouthA.Y, mouthB.X, mouthB.Y)));
                }
                case "goblin":
                {
                    // a long pointed nose, a long ear swept back, and a top-knot
                    var nose = Poly(At(r * 0.75f, r * 0.15f), At(r * 1.55f, -r * 0.35f), At(r * 0.75f, -r * 0.45f));
                    var ear = Poly(At(-r * 0.2f, r * 0.25f), At(-r * 1.6f, r * 0.75f), At(-r * 0.35f, -r * 0.25f));
                    var knotA = At(-r * 0.2f, r * 0.85f); var knotB = At(-r * 0.75f, r * 1.45f);
                    var eye = At(r * 0.5f, r * 0.1f);
                    var mouthA = At(r * 0.35f, -r * 0.55f); var mouthB = At(r * 0.85f, -r * 0.5f);
                    return new Part("head", (x, y) =>
                    {
                        float d = Sdf.Circle(x - c.X, y - c.Y, r);
                        d = M.Min(d, Sdf.Polygon(x, y, nose));
                        d = M.Min(d, Sdf.Polygon(x, y, ear) - r * 0.05f);
                        d = M.Min(d, Sdf2.Capsule(x, y, knotA, knotB, r * 0.22f, r * 0.12f));
                        return d;
                    }, (x, y) => M.Min(M.Abs(Sdf.Circle(x - eye.X, y - eye.Y, r * 0.1f)), Sdf.Segment(x, y, mouthA.X, mouthA.Y, mouthB.X, mouthB.Y)));
                }
                case "imp":
                {
                    // the surtling's: a round head with a pointed chin and a sharp ear, glowing eye
                    var chin = Poly(At(r * 0.3f, -r * 0.6f), At(r * 0.9f, -r * 1.3f), At(r * 0.95f, -r * 0.3f));
                    var ear = Poly(At(-r * 0.3f, r * 0.2f), At(-r * 1.3f, r * 0.55f), At(-r * 0.4f, -r * 0.2f));
                    var eye = At(r * 0.5f, r * 0.15f);
                    return new Part("head", (x, y) => M.Min(M.Min(Sdf.Circle(x - c.X, y - c.Y, r), Sdf.Polygon(x, y, chin)), Sdf.Polygon(x, y, ear)),
                        (x, y) => M.Abs(Sdf.Circle(x - eye.X, y - eye.Y, r * 0.14f)));
                }
                case "rock":
                {
                    // a stone golem's: a blocky lump with a crack for a face
                    var block = Poly(At(-r * 0.9f, -r * 0.7f), At(-r * 0.7f, r * 0.9f), At(r * 0.8f, r * 1.0f), At(r * 1.1f, -r * 0.2f), At(r * 0.7f, -r * 0.9f));
                    var crackA = At(r * 0.2f, r * 0.1f); var crackB = At(r * 1.0f, r * 0.0f);
                    return new Part("head", (x, y) => Sdf.Polygon(x, y, block) - r * 0.06f,
                        (x, y) => Sdf.Segment(x, y, crackA.X, crackA.Y, crackB.X, crackB.Y));
                }
                case "morgen":
                {
                    // a long, eyeless head with feelers hanging from the jaw
                    var snout = At(r * 1.3f, -r * 0.35f);
                    var feelers = new List<(Vector2, Vector2)>();
                    for (int i = 0; i < 4; i++) feelers.Add((At(r * (0.4f + i * 0.25f), -r * 0.6f), At(r * (0.2f + i * 0.3f), -r * (1.5f + 0.2f * (i % 2)))));
                    return new Part("head", (x, y) =>
                    {
                        float d = M.Min(Sdf.Circle(x - c.X, y - c.Y, r), Sdf2.Capsule(x, y, c, snout, r * 0.75f, r * 0.4f));
                        foreach (var (a0, b0) in feelers) d = M.Min(d, Sdf2.Capsule(x, y, a0, b0, r * 0.1f, r * 0.05f));
                        return d;
                    });
                }
                case "wolf":
                {
                    // a wolf's head on a man's body: a long snout, pointed ears, a cut along the mouth
                    var nose = At(r * 2.0f, -r * 0.35f);
                    var ear = Poly(At(-r * 0.1f, r * 0.6f), At(-r * 0.45f, r * 1.75f), At(-r * 0.75f, r * 0.45f));
                    var eye = At(r * 0.6f, r * 0.2f);
                    var mouthA = At(r * 1.9f, -r * 0.6f); var mouthB = At(r * 0.6f, -r * 0.5f);
                    return new Part("head", (x, y) => M.Min(M.Min(Sdf.Circle(x - c.X, y - c.Y, r), Sdf2.Capsule(x, y, c, nose, r * 0.8f, r * 0.38f)),
                                                          Sdf.Polygon(x, y, ear) - r * 0.04f),
                        (x, y) => M.Min(M.Abs(Sdf.Circle(x - eye.X, y - eye.Y, r * 0.11f)), Sdf.Segment(x, y, mouthA.X, mouthA.Y, mouthB.X, mouthB.Y)));
                }
                default:
                {
                    var nose = new[] { new Vector2(0.10f, 0.02f), new Vector2(0.165f, -0.035f), new Vector2(0.10f, -0.05f) }
                        .Select(p => c + p.Rotate(tilt)).ToArray();
                    var nosePoly = new[] { nose[0].X, nose[0].Y, nose[1].X, nose[1].Y, nose[2].X, nose[2].Y };
                    var eye = c + new Vector2(0.07f, 0.0f).Rotate(tilt);
                    return new Part("head", (x, y) => M.Min(Sdf.Circle(x - c.X, y - c.Y, r), Sdf.Polygon(x, y, nosePoly)),
                        (x, y) => M.Abs(Sdf.Circle(x - eye.X, y - eye.Y, 0.012f)));
                }
            }
        }

        /// <summary>A rounded cap with a brim and a nasal bar, like the game's own helmets (no horns).</summary>
        static Part Helmet(Vector2 c, float tilt)
        {
            float headR = HeadR;
            var up = UpDir(tilt);
            var brimC = c + up * 0.01f;
            var nasalTop = c + new Vector2(0.105f, 0.02f).Rotate(tilt);
            var nasalBot = c + new Vector2(0.125f, -0.06f).Rotate(tilt);
            var ridgeA = c + up * (headR + 0.02f);
            return new Part("helmet", (x, y) =>
            {
                float cap = Sdf.Circle(x - c.X, y - c.Y, headR + 0.022f);
                // keep the part above the brim line
                float above = -((x - brimC.X) * up.X + (y - brimC.Y) * up.Y);
                float d = M.Max(cap, above);
                d = M.Min(d, Sdf2.Capsule(x, y, nasalTop, nasalBot, 0.018f, 0.014f));
                return d;
            }, (x, y) => Sdf.Segment(x, y, ridgeA.X, ridgeA.Y, brimC.X + up.X * 0.05f, brimC.Y + up.Y * 0.05f));
        }

        /// <summary>A curved horn rising from the side of the head and sweeping forward (surtling, horned helmets).</summary>
        static Part Horn(string name, Vector2 c, float tilt, float size)
        {
            float r = HeadR;
            var pts = new List<Vector2> { c + new Vector2(-r * 0.15f, r * 0.75f).Rotate(tilt) };
            var dir = UpDir(tilt - 25);
            for (int i = 0; i < 4; i++)
            {
                pts.Add(pts[pts.Count - 1] + dir * (size / 4));
                dir = (dir + UpDir(tilt + 80) * 0.35f).Normalized;
            }
            float w = size * 0.22f;
            return new Part(name, (x, y) =>
            {
                float d = float.MaxValue;
                for (int i = 0; i + 1 < pts.Count; i++) d = M.Min(d, Sdf2.Capsule(x, y, pts[i], pts[i + 1], w * (1 - i / 4f), w * (1 - (i + 1) / 4f) + 0.004f));
                return d;
            });
        }

        /// <summary>A hood: a cowl round the head falling to the shoulders, with the face opening cut.</summary>
        static Part Hood(Vector2 c, Vector2 shoulderLine, Vector2 up, float tilt)
        {
            float r = HeadR;
            var back = shoulderLine + new Vector2(-ChestR * 0.6f, -0.02f).Rotate(tilt);
            var peak = c + new Vector2(r * 0.4f, r * 0.9f).Rotate(tilt);
            var face = c + new Vector2(r * 0.55f, -r * 0.1f).Rotate(tilt);
            return new Part("hood", (x, y) => M.Min(M.Min(Sdf.Circle(x - c.X, y - c.Y, r * 1.3f), Sdf2.Capsule(x, y, c, back, r * 1.25f, r * 0.9f)),
                                                  Sdf2.Capsule(x, y, c, peak, r * 1.1f, r * 0.5f)),
                (x, y) => M.Abs(Sdf.Circle(x - face.X, y - face.Y, r * 0.75f)));
        }

        /// <summary>A round cap with a wide rim (the dverger's).</summary>
        static Part RoundCap(Vector2 c, float tilt)
        {
            float r = HeadR;
            var rimA = c + new Vector2(-r * 1.25f, r * 0.15f).Rotate(tilt);
            var rimB = c + new Vector2(r * 1.3f, r * 0.15f).Rotate(tilt);
            var dome = c + new Vector2(0, r * 0.45f).Rotate(tilt);
            return new Part("cap", (x, y) => M.Min(Sdf2.Capsule(x, y, rimA, rimB, r * 0.13f, r * 0.13f), Sdf.Circle(x - dome.X, y - dome.Y, r * 0.95f) + M.Max(0, -((x - c.X) * UpDir(tilt).X + (y - c.Y) * UpDir(tilt).Y - r * 0.1f))),
                (x, y) => Sdf.Segment(x, y, rimA.X, rimA.Y + r * 0.13f, rimB.X, rimB.Y + r * 0.13f));
        }

        /// <summary>Feathered wings rising behind the shoulders (the valkyrie), with feather cuts.</summary>
        static Part Wings(Vector2 shoulderLine, Vector2 up, float lean, float size)
        {
            var root = shoulderLine - up * 0.05f + new Vector2(-0.08f, 0).Rotate(lean);
            var tip = root + UpDir(lean - 40) * size;
            var mid = root + UpDir(lean - 95) * (size * 0.65f);
            var low = root + UpDir(lean - 160) * (size * 0.55f);
            var feathers = new List<(Vector2, Vector2)>();
            for (int i = 1; i <= 5; i++)
            {
                var a = Vector2.Lerp(root, Vector2.Lerp(tip, low, i / 6f), 0.45f);
                var b = Vector2.Lerp(tip, low, i / 6f);
                feathers.Add((a, b));
            }
            var poly = new[] { root.X, root.Y, tip.X, tip.Y, mid.X, mid.Y, low.X, low.Y };
            return new Part("wings", (x, y) => Sdf.Polygon(x, y, poly) - 0.02f,
                (x, y) => feathers.Min(f => Sdf.Segment(x, y, f.Item1.X, f.Item1.Y, f.Item2.X, f.Item2.Y)));
        }

        static Part Beard(Vector2 c, float tilt)
        {
            var pts = new[]
            {
                new Vector2(0.14f, -0.06f), new Vector2(0.13f, -0.30f), new Vector2(0.05f, -0.24f), new Vector2(0.02f, -0.09f),
            }.Select(p => c + p.Rotate(tilt)).ToArray();
            var poly = pts.SelectMany(p => new[] { p.X, p.Y }).ToArray();
            return new Part("beard", (x, y) => Sdf.Polygon(x, y, poly) - 0.01f);
        }
    }

    /// <summary>Held items, drawn in the same chunky style (the game's icons wouldn't match it).</summary>
    public static class Props
    {
        public static readonly string[] Items = { "axe", "sword", "spear", "shield", "hammer", "club", "staff", "bow", "pickaxe", "thumbsup", "thumbsdown" };

        /// <summary>The item a word names ("axes", "battleaxe", "round-shield"), or null.</summary>
        public static string ItemFor(string word)
        {
            string w = new string((word ?? "").ToLowerInvariant().Where(char.IsLetter).ToArray());
            if (w.EndsWith("es") && Items.Contains(w.Substring(0, w.Length - 2))) w = w.Substring(0, w.Length - 2);
            else if (w.EndsWith("s") && Items.Contains(w.Substring(0, w.Length - 1))) w = w.Substring(0, w.Length - 1);
            switch (w)
            {
                case "axe": case "battleaxe": case "waraxe": case "ax": return "axe";
                case "sword": case "blade": return "sword";
                case "spear": case "lance": case "atgeir": return "spear";
                case "shield": case "roundshield": case "buckler": return "shield";
                case "hammer": case "warhammer": case "sledge": case "mace": return "hammer";
                case "club": case "log": case "cudgel": return "club";
                case "staff": case "stave": case "wand": case "crook": return "staff";
                case "bow": case "longbow": return "bow";
                case "pickaxe": case "pick": case "mattock": return "pickaxe";
                // not items, but the hand does the same job: Odin's verdict (Hippo, 2026-10-30)
                case "thumbsup": case "thumbup": return "thumbsup";
                case "thumbsdown": case "thumbdown": return "thumbsdown";
            }
            return null;
        }

        static float Rad(float d) => d * M.Pi / 180f;

        /// <summary>A weapon in a fist at <paramref name="wrist"/>, its haft pointing along <paramref name="deg"/> (from straight down).</summary>
        public static Part Weapon(string item, Vector2 wrist, float deg)
        {
            var u = new Vector2(M.Sin(Rad(deg)), -M.Cos(Rad(deg)));   // along the haft, away from the grip
            var v = u.Perp;                                           // the blade side
            var grip = wrist + new Vector2(M.Sin(Rad(deg - 90)), -M.Cos(Rad(deg - 90))) * 0.035f;
            Vector2 P(float a, float b) => grip + u * a + v * b;
            float[] Poly(params Vector2[] pts) => pts.SelectMany(p => new[] { p.X, p.Y }).ToArray();

            switch (item)
            {
                case "thumbsup":
                case "thumbsdown":
                {
                    // a thumb standing out of the fist, straight up or down whatever the arm does
                    var fist = wrist + new Vector2(M.Sin(Rad(deg)), -M.Cos(Rad(deg))) * 0.035f;
                    var tip = fist + new Vector2(0.015f, item == "thumbsup" ? 0.15f : -0.15f);
                    return new Part("thumb", (x, y) => Sdf2.Capsule(x, y, fist, tip, 0.036f, 0.03f));
                }
                case "pickaxe":
                {
                    // a haft with a curved head, a point either side
                    var h0 = P(-0.12f, 0); var h1 = P(0.68f, 0);
                    var head = new List<Vector2>();
                    for (int i = 0; i <= 8; i++)
                    {
                        float t = i / 8f * 2 - 1;
                        head.Add(P(0.66f - 0.09f * t * t, 0.36f * t));
                    }
                    return new Part("pickaxe", (x, y) =>
                    {
                        float d = Sdf2.Capsule(x, y, h0, h1, 0.024f, 0.024f);
                        for (int i = 0; i + 1 < head.Count; i++)
                        {
                            float r0 = M.Lerp(0.034f, 0.01f, M.Abs(i / 4f - 1)), r1 = M.Lerp(0.034f, 0.01f, M.Abs((i + 1) / 4f - 1));
                            d = M.Min(d, Sdf2.Capsule(x, y, head[i], head[i + 1], r0, r1));
                        }
                        return d;
                    });
                }
                case "axe":
                {
                    var h0 = P(-0.12f, 0); var h1 = P(0.70f, 0);
                    // a bearded blade on the forward side of the haft's head
                    var blade = Poly(P(0.52f, 0.01f), P(0.73f, 0.01f), P(0.76f, -0.20f), P(0.58f, -0.24f), P(0.56f, -0.10f));
                    return new Part("axe", (x, y) => M.Min(Sdf2.Capsule(x, y, h0, h1, 0.024f, 0.024f), Sdf.Polygon(x, y, blade) - 0.006f));
                }
                case "hammer":
                {
                    var h0 = P(-0.10f, 0); var h1 = P(0.58f, 0);
                    var head = Poly(P(0.50f, 0.11f), P(0.66f, 0.11f), P(0.66f, -0.11f), P(0.50f, -0.11f));
                    return new Part("hammer", (x, y) => M.Min(Sdf2.Capsule(x, y, h0, h1, 0.026f, 0.026f), Sdf.Polygon(x, y, head) - 0.008f));
                }
                case "staff":
                {
                    // a long staff with a knot at the top
                    var a = P(-0.85f, 0); var b = P(0.75f, 0); var k = P(0.8f, 0);
                    return new Part("staff", (x, y) => M.Min(Sdf2.Capsule(x, y, a, b, 0.022f, 0.026f), Sdf.Circle(x - k.X, y - k.Y, 0.055f)));
                }
                case "bow":
                {
                    // a longbow held upright by its grip: belly forward, string towards the archer (Hippo: it was backwards)
                    var pts = new List<Vector2>();
                    for (int i = 0; i <= 8; i++)
                    {
                        float t = i / 8f * 2 - 1;
                        // the grip in the fist, the tips curving back towards the archer, the string behind
                        pts.Add(P(t * 0.75f, 0.18f * t * t));
                    }
                    var s0 = pts[0]; var s1 = pts[8];
                    return new Part("bow", (x, y) =>
                    {
                        float d = float.MaxValue;
                        for (int i = 0; i + 1 < pts.Count; i++) d = M.Min(d, Sdf2.Capsule(x, y, pts[i], pts[i + 1], 0.024f, 0.024f));
                        return M.Min(d, Sdf2.Capsule(x, y, s0, s1, 0.007f, 0.007f));
                    });
                }
                case "club":
                {
                    // a knotted log, thick at the striking end (a troll's weapon)
                    var a = P(-0.15f, 0); var b = P(0.85f, 0);
                    var k1 = P(0.45f, 0.06f); var k2 = P(0.7f, -0.07f);
                    return new Part("club", (x, y) => M.Min(M.Min(Sdf2.Capsule(x, y, a, b, 0.035f, 0.08f), Sdf.Circle(x - k1.X, y - k1.Y, 0.04f)),
                                                          Sdf.Circle(x - k2.X, y - k2.Y, 0.045f)),
                        (x, y) => M.Min(Sdf.Segment(x, y, P(0.3f, 0.01f).X, P(0.3f, 0.01f).Y, P(0.55f, -0.02f).X, P(0.55f, -0.02f).Y),
                                        Sdf.Segment(x, y, P(0.6f, 0.02f).X, P(0.6f, 0.02f).Y, P(0.8f, -0.01f).X, P(0.8f, -0.01f).Y)));
                }
                case "spear":
                {
                    var h0 = P(-0.75f, 0); var h1 = P(0.95f, 0);
                    var head = Poly(P(0.92f, 0.035f), P(1.08f, 0.045f), P(1.22f, 0), P(1.08f, -0.045f), P(0.92f, -0.035f));
                    return new Part("spear", (x, y) => M.Min(Sdf2.Capsule(x, y, h0, h1, 0.02f, 0.02f), Sdf.Polygon(x, y, head)),
                        (x, y) => Sdf.Segment(x, y, P(0.95f, 0).X, P(0.95f, 0).Y, P(1.15f, 0).X, P(1.15f, 0).Y));
                }
                default: // sword
                {
                    var blade = Poly(P(0.07f, 0.035f), P(0.80f, 0.03f), P(0.88f, 0), P(0.80f, -0.03f), P(0.07f, -0.035f));
                    var guardA = P(0.06f, 0.10f); var guardB = P(0.06f, -0.10f);
                    var g0 = P(-0.08f, 0); var g1 = P(0.06f, 0);
                    var pommel = P(-0.11f, 0);
                    return new Part("sword", (x, y) =>
                    {
                        float d = Sdf.Polygon(x, y, blade);
                        d = M.Min(d, Sdf2.Capsule(x, y, guardA, guardB, 0.022f, 0.022f));
                        d = M.Min(d, Sdf2.Capsule(x, y, g0, g1, 0.022f, 0.022f));
                        d = M.Min(d, Sdf.Circle(x - pommel.X, y - pommel.Y, 0.038f));
                        return d;
                    }, (x, y) => Sdf.Segment(x, y, P(0.12f, 0).X, P(0.12f, 0).Y, P(0.74f, 0).X, P(0.74f, 0).Y));
                }
            }
        }

        /// <summary>A round shield seen face-on (as carvers drew them), with a rim and a boss.</summary>
        public static Part RoundShield(Vector2 c)
        {
            const float R = 0.33f;
            return new Part("shield", (x, y) => Sdf.Circle(x - c.X, y - c.Y, R),
                (x, y) =>
                {
                    float r = M.Len(x - c.X, y - c.Y);
                    return M.Min(M.Abs(r - (R - 0.05f)), M.Abs(r - 0.075f));
                });
        }
    }

    /// <summary>One filled part of a figure: its signed distance, and optional cut lines drawn on it.</summary>
    public sealed class Part
    {
        public readonly string Name;
        readonly Func<float, float, float> _d, _lines;
        /// <summary>Applied after building: the part moves by Offset (figure units), then the figure is scaled.</summary>
        public Vector2 Offset;
        public float Scale = 1;

        public Part(string name, Func<float, float, float> distance, Func<float, float, float> lines = null)
        {
            Name = name; _d = distance; _lines = lines;
        }

        public float Distance(float x, float y) => _d(x / Scale - Offset.X, y / Scale - Offset.Y) * Scale;
        /// <summary>The distance before the figure is stood on the ground (for parts built from other parts).</summary>
        public float Raw(float x, float y) => _d(x, y);
        /// <summary>Distance to the part's own detail lines (belt, boot top, shield rim), or MaxValue.</summary>
        public float Lines(float x, float y) => _lines == null ? float.MaxValue : _lines(x / Scale - Offset.X, y / Scale - Offset.Y) * Scale;
    }

    /// <summary>A small 2D vector for the rig.</summary>
    public struct Vector2
    {
        public float X, Y;
        public Vector2(float x, float y) { X = x; Y = y; }
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.X + b.X, a.Y + b.Y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.X - b.X, a.Y - b.Y);
        public static Vector2 operator *(Vector2 a, float s) => new Vector2(a.X * s, a.Y * s);
        public float Length => M.Len(X, Y);
        public Vector2 Normalized { get { float l = Length; return l > 0 ? new Vector2(X / l, Y / l) : this; } }
        /// <summary>Turned 90° anticlockwise.</summary>
        public Vector2 Perp => new Vector2(-Y, X);
        /// <summary>Turned clockwise by <paramref name="deg"/> (a forward lean).</summary>
        public Vector2 Rotate(float deg)
        {
            float a = -deg * M.Pi / 180f, c = M.Cos(a), s = M.Sin(a);
            return new Vector2(X * c - Y * s, X * s + Y * c);
        }
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => new Vector2(M.Lerp(a.X, b.X, t), M.Lerp(a.Y, b.Y, t));
    }

    static class Sdf2
    {
        /// <summary>A tapered capsule (radius ra at a, rb at b): iq's uneven capsule.</summary>
        public static float Capsule(float x, float y, Vector2 a, Vector2 b, float ra, float rb)
        {
            float px = x - a.X, py = y - a.Y, bx = b.X - a.X, by = b.Y - a.Y;
            float h = bx * bx + by * by;
            if (h < 1e-10f) return M.Len(px, py) - M.Max(ra, rb);
            float qx = (px * by - py * bx) / h, qy = (px * bx + py * by) / h;
            qx = M.Abs(qx);
            float bb = ra - rb;
            if (bb * bb >= h) return M.Min(M.Len(px, py) - ra, M.Len(x - b.X, y - b.Y) - rb);
            float cx = M.Sqrt(h - bb * bb), cy = bb;
            float k = cx * qy - cy * qx;
            float m = cx * qx + cy * qy;
            float n = qx * qx + qy * qy;
            if (k < 0) return M.Sqrt(h * n) - ra;
            if (k > cx) return M.Sqrt(h * (n + 1 - 2 * qy)) - rb;
            return m - ra;
        }
    }
}
