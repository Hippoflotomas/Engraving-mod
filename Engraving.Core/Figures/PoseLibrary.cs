using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Engraving.Core.Figures
{
    /// <summary>
    /// A pose for one rig ("humanoid", "quadruped"): named angles in degrees. In JSON the angles are grouped
    /// ("nearArm": { "upper": 90 }); here they're flat ("nearArm.upper"), so any rig can use the same files and code.
    /// </summary>
    public sealed class Pose
    {
        public string Name;
        public string Rig = "humanoid";
        public List<string> Words = new List<string>();
        public Dictionary<string, float> Values = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        public float Get(string key, float fallback) => Values.TryGetValue(key, out var v) ? v : fallback;
        public bool Flag(string key) => Get(key, 0) > 0.5f;
        public string Key => PoseLibrary.KeyOf(Rig, Name);
    }

    /// <summary>
    /// The poses figures can take (Hippo, 2026-10-31: poses live outside the mod, in JSON players can edit and share).
    /// Built-in poses first, then every *.json file in the poses folder, which can add poses or replace built-in ones
    /// (same rig and name). A carving records any pose that isn't the built-in one (a hidden "#pose:" line, like the
    /// pictures' "#charges:"), so every player draws the same figure even without the pose file.
    /// </summary>
    public static class PoseLibrary
    {
        public const string TailPrefix = "#pose:";

        /// <summary>The built-in poses, also written out as an example for players.</summary>
        public const string BuiltinJson = @"{
  // Engraving figure poses. Angles in degrees. Figures face right; 'facing left' mirrors them.
  // ""rig"" says which figures a pose is for: ""humanoid"" (the default) or ""quadruped"".
  // ""words"" are other words that ask for the pose in a carving ('a viking strikes').
  //
  // humanoid: arms and legs in degrees from straight down, positive swinging forward (90 = forward, 180 = up);
  //   lean, head: forward from upright (negative leans back);
  //   grip: how far what the hand holds turns from the forearm (90 = a haft standing up out of a forward fist);
  //   near = the side towards the viewer; inFront: a far arm without a shield comes in front of the body.
  //
  // quadruped: legs in degrees from straight down, positive forward ('fore' = front legs, 'hind' = back legs:
  //   upper/lower/foot and thigh/shin/foot); body: the back's tilt (positive = front end up);
  //   neck: above horizontal; head: the snout's direction above horizontal; tail: above straight back;
  //   tailCurl: extra bend along the tail.
  ""poses"": [
    { ""name"": ""standing"",   ""words"": [""stands"", ""still"", ""waiting""], ""lean"": 2,
      ""nearArm"": { ""upper"": 12, ""fore"": 30 },     ""farArm"": { ""upper"": -6, ""fore"": 12 },
      ""nearLeg"": { ""thigh"": 9, ""shin"": 2 },       ""farLeg"": { ""thigh"": -9, ""shin"": -6 } },

    { ""name"": ""walking"",    ""words"": [""walks"", ""marching"", ""striding""], ""lean"": 5,
      ""nearArm"": { ""upper"": -25, ""fore"": -5 },    ""farArm"": { ""upper"": 28, ""fore"": 55 },
      ""nearLeg"": { ""thigh"": 28, ""shin"": 5 },      ""farLeg"": { ""thigh"": -22, ""shin"": -40, ""foot"": 65 } },

    { ""name"": ""running"",    ""words"": [""runs"", ""charging"", ""rushing""], ""lean"": 16, ""head"": -6,
      ""nearArm"": { ""upper"": -45, ""fore"": 30 },    ""farArm"": { ""upper"": 55, ""fore"": 120 },
      ""nearLeg"": { ""thigh"": 62, ""shin"": -5 },     ""farLeg"": { ""thigh"": -38, ""shin"": -95, ""foot"": 20 } },

    { ""name"": ""striking"",   ""words"": [""strikes"", ""attacking"", ""swinging"", ""fighting""], ""lean"": -4, ""head"": -4,
      ""nearArm"": { ""upper"": 168, ""fore"": 215, ""grip"": 70 }, ""farArm"": { ""upper"": 55, ""fore"": 95 },
      ""nearLeg"": { ""thigh"": 32, ""shin"": 6 },      ""farLeg"": { ""thigh"": -26, ""shin"": -18, ""foot"": 80 } },

    { ""name"": ""thrusting"",  ""words"": [""thrusts"", ""stabbing"", ""lunging""], ""lean"": 14,
      ""nearArm"": { ""upper"": 70, ""fore"": 88, ""grip"": 0 }, ""farArm"": { ""upper"": 40, ""fore"": 85 },
      ""nearLeg"": { ""thigh"": 40, ""shin"": 4 },      ""farLeg"": { ""thigh"": -30, ""shin"": -22, ""foot"": 75 } },

    { ""name"": ""guarding"",   ""words"": [""guards"", ""defending"", ""braced"", ""ready""], ""lean"": 6,
      ""nearArm"": { ""upper"": 115, ""fore"": 165, ""grip"": 60 }, ""farArm"": { ""upper"": 55, ""fore"": 110 },
      ""nearLeg"": { ""thigh"": 22, ""shin"": 0 },      ""farLeg"": { ""thigh"": -20, ""shin"": -16, ""foot"": 80 } },

    { ""name"": ""triumphant"", ""words"": [""victorious"", ""cheering"", ""arms-raised"", ""raised"", ""celebrating""], ""lean"": -3, ""head"": -12,
      ""nearArm"": { ""upper"": 140, ""fore"": 150 },   ""farArm"": { ""upper"": 218, ""fore"": 205 },
      ""nearLeg"": { ""thigh"": 12, ""shin"": 2 },      ""farLeg"": { ""thigh"": -12, ""shin"": -8 } },

    { ""name"": ""aiming"",     ""words"": [""aims"", ""drawing"", ""shooting"", ""loosing""], ""lean"": 2, ""head"": -2,
      ""nearArm"": { ""upper"": 86, ""fore"": 90, ""grip"": 90 }, ""farArm"": { ""upper"": -80, ""fore"": 88, ""inFront"": 1 },
      ""nearLeg"": { ""thigh"": 22, ""shin"": 2 },      ""farLeg"": { ""thigh"": -20, ""shin"": -12, ""foot"": 80 } },

    { ""name"": ""throwing"",   ""words"": [""throws"", ""hurling"", ""casting""], ""lean"": 6, ""head"": -6,
      ""nearArm"": { ""upper"": 205, ""fore"": 165, ""grip"": -75 }, ""farArm"": { ""upper"": 75, ""fore"": 85 },
      ""nearLeg"": { ""thigh"": 36, ""shin"": 4 },      ""farLeg"": { ""thigh"": -28, ""shin"": -20, ""foot"": 75 } },

    { ""name"": ""judging"",    ""words"": [""judges"", ""pointing"", ""condemning"", ""approving""], ""lean"": 0, ""head"": -4,
      ""nearArm"": { ""upper"": 82, ""fore"": 92, ""grip"": 0 }, ""farArm"": { ""upper"": 18, ""fore"": 38, ""grip"": 142 },
      ""nearLeg"": { ""thigh"": 6, ""shin"": 1 },       ""farLeg"": { ""thigh"": -6, ""shin"": -4 } },

    { ""rig"": ""quadruped"", ""name"": ""standing"", ""words"": [""stands"", ""still"", ""waiting""],
      ""body"": 0, ""neck"": 38, ""head"": -18, ""tail"": -55, ""tailCurl"": 8,
      ""nearFore"": { ""upper"": -6, ""lower"": 2, ""foot"": 12 },  ""farFore"": { ""upper"": 6, ""lower"": 4, ""foot"": 14 },
      ""nearHind"": { ""thigh"": 28, ""shin"": -32, ""foot"": 4 },  ""farHind"": { ""thigh"": 18, ""shin"": -38, ""foot"": 0 } },

    { ""rig"": ""quadruped"", ""name"": ""walking"", ""words"": [""walks"", ""prowling"", ""stalking""],
      ""body"": 0, ""neck"": 30, ""head"": -20, ""tail"": -45, ""tailCurl"": 8,
      ""nearFore"": { ""upper"": 18, ""lower"": 8, ""foot"": 25 },  ""farFore"": { ""upper"": -14, ""lower"": -8, ""foot"": 6 },
      ""nearHind"": { ""thigh"": 6, ""shin"": -46, ""foot"": -10 }, ""farHind"": { ""thigh"": 38, ""shin"": -22, ""foot"": 10 } },

    { ""rig"": ""quadruped"", ""name"": ""running"", ""words"": [""runs"", ""galloping"", ""fleeing"", ""bounding""],
      ""body"": 2, ""neck"": 18, ""head"": -8, ""tail"": 5, ""tailCurl"": 4,
      ""nearFore"": { ""upper"": 55, ""lower"": 70, ""foot"": 95 }, ""farFore"": { ""upper"": 30, ""lower"": 40, ""foot"": 70 },
      ""nearHind"": { ""thigh"": -20, ""shin"": -75, ""foot"": -55 }, ""farHind"": { ""thigh"": -5, ""shin"": -65, ""foot"": -40 } },

    { ""rig"": ""quadruped"", ""name"": ""charging"", ""words"": [""charges"", ""attacking"", ""lunging"", ""fighting""],
      ""body"": -4, ""neck"": 4, ""head"": -16, ""tail"": 15, ""tailCurl"": 0,
      ""nearFore"": { ""upper"": 45, ""lower"": 55, ""foot"": 80 }, ""farFore"": { ""upper"": 15, ""lower"": 20, ""foot"": 45 },
      ""nearHind"": { ""thigh"": -10, ""shin"": -70, ""foot"": -45 }, ""farHind"": { ""thigh"": 10, ""shin"": -55, ""foot"": -25 } },

    { ""rig"": ""quadruped"", ""name"": ""grazing"", ""words"": [""grazes"", ""feeding"", ""eating"", ""drinking""],
      ""body"": -3, ""neck"": -62, ""head"": -80, ""tail"": -50, ""tailCurl"": 6,
      ""nearFore"": { ""upper"": -4, ""lower"": 0, ""foot"": 10 },  ""farFore"": { ""upper"": 8, ""lower"": 6, ""foot"": 14 },
      ""nearHind"": { ""thigh"": 26, ""shin"": -32, ""foot"": 2 },  ""farHind"": { ""thigh"": 16, ""shin"": -38, ""foot"": -2 } },

    { ""rig"": ""quadruped"", ""name"": ""howling"", ""words"": [""howls"", ""calling"", ""bellowing""],
      ""body"": 6, ""neck"": 72, ""head"": 58, ""tail"": -60, ""tailCurl"": 6,
      ""nearFore"": { ""upper"": -2, ""lower"": 2, ""foot"": 12 },  ""farFore"": { ""upper"": 8, ""lower"": 6, ""foot"": 14 },
      ""nearHind"": { ""thigh"": 34, ""shin"": -28, ""foot"": 6 },  ""farHind"": { ""thigh"": 24, ""shin"": -34, ""foot"": 2 } },

    { ""rig"": ""quadruped"", ""name"": ""leaping"", ""words"": [""leaps"", ""jumping"", ""springing""],
      ""body"": 14, ""neck"": 25, ""head"": -5, ""tail"": 10, ""tailCurl"": -4,
      ""nearFore"": { ""upper"": 75, ""lower"": 140, ""foot"": 115 }, ""farFore"": { ""upper"": 60, ""lower"": 125, ""foot"": 100 },
      ""nearHind"": { ""thigh"": -45, ""shin"": -95, ""foot"": -80 }, ""farHind"": { ""thigh"": -35, ""shin"": -90, ""foot"": -70 } },

    // flyer: body (tilt), wing (degrees the wings rise above straight back), wingFold (0 open .. 1 folded),
    //   neck, head (degrees), legs (leg angle from straight down), tail, tailCurl; perched = 1 stands on the ground
    { ""rig"": ""flyer"", ""name"": ""flying"", ""words"": [""flies"", ""soaring"", ""flapping""],
      ""body"": 5, ""wing"": 70, ""wingFold"": 0, ""neck"": 25, ""head"": 0, ""legs"": 60, ""tail"": 0, ""tailCurl"": 5 },
    { ""rig"": ""flyer"", ""name"": ""gliding"", ""words"": [""glides"", ""circling""],
      ""body"": 0, ""wing"": 20, ""wingFold"": 0, ""neck"": 20, ""head"": -5, ""legs"": 70, ""tail"": 0, ""tailCurl"": 3 },
    { ""rig"": ""flyer"", ""name"": ""diving"", ""words"": [""dives"", ""swooping"", ""attacking""],
      ""body"": -35, ""wing"": 40, ""wingFold"": 0.4, ""neck"": 10, ""head"": -10, ""legs"": 40, ""tail"": 10, ""tailCurl"": -4 },
    { ""rig"": ""flyer"", ""name"": ""standing"", ""words"": [""stands"", ""perched"", ""perching"", ""still""],
      ""body"": 25, ""wing"": -10, ""wingFold"": 1, ""neck"": 50, ""head"": -10, ""legs"": 0, ""tail"": -25, ""tailCurl"": 6, ""perched"": 1 },

    // serpent: wave (height of the body's curves), waves (how many), rear (0..1 lifts the front to strike),
    //   mouth (jaw opening, degrees)
    { ""rig"": ""serpent"", ""name"": ""swimming"", ""words"": [""swims"", ""slithering"", ""standing"", ""still""],
      ""wave"": 0.14, ""waves"": 1.6, ""rear"": 0, ""mouth"": 8 },
    { ""rig"": ""serpent"", ""name"": ""striking"", ""words"": [""strikes"", ""attacking"", ""rearing"", ""biting""],
      ""wave"": 0.1, ""waves"": 1.2, ""rear"": 0.8, ""mouth"": 40 },

    // blob: squash (height, 1 = resting), lift (off the ground, metres)
    { ""rig"": ""blob"", ""name"": ""standing"", ""words"": [""stands"", ""still"", ""resting"", ""lurking""], ""squash"": 1, ""lift"": 0 },
    { ""rig"": ""blob"", ""name"": ""jumping"", ""words"": [""jumps"", ""bouncing"", ""attacking""], ""squash"": 1.3, ""lift"": 0.25 },

    // insect: step (legs swing, degrees), raise (front claws lift, degrees)
    { ""rig"": ""insect"", ""name"": ""standing"", ""words"": [""stands"", ""still""], ""step"": 0, ""raise"": 0 },
    { ""rig"": ""insect"", ""name"": ""walking"", ""words"": [""walks"", ""crawling"", ""scuttling""], ""step"": 15, ""raise"": 0 },
    { ""rig"": ""insect"", ""name"": ""attacking"", ""words"": [""attacks"", ""striking"", ""charging""], ""step"": 8, ""raise"": 45 }
  ]
}";

        static readonly Dictionary<string, Pose> Builtin;
        static Dictionary<string, Pose> _current;
        static Dictionary<string, string> _words;   // "rig|word" → pose name

        static PoseLibrary()
        {
            Builtin = FromJson(BuiltinJson).ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);
            Install(Builtin.Values);
        }

        public static string KeyOf(string rig, string name) => (rig ?? "humanoid").ToLowerInvariant() + "|" + (name ?? "").ToLowerInvariant();

        // ---------------------------------------------------------------- reading and writing

        /// <summary>Poses from JSON: { "poses": [ ... ] }, a bare [ ... ], or a single pose object.</summary>
        public static List<Pose> FromJson(string json)
        {
            var root = MiniJson.Parse(json);
            var list = new List<Pose>();
            IEnumerable<object> items =
                root is Dictionary<string, object> o && o.TryGetValue("poses", out var ps) && ps is List<object> pl ? pl :
                root is List<object> l ? l :
                root is Dictionary<string, object> single ? new object[] { single } : new object[0];
            int n = 0;
            foreach (var item in items)
            {
                n++;
                if (!(item is Dictionary<string, object> d)) throw new FormatException($"pose {n} isn't an object");
                list.Add(Read(d, n));
            }
            return list;
        }

        static Pose Read(Dictionary<string, object> d, int n)
        {
            string name = d.TryGetValue("name", out var nv) ? nv as string : null;
            if (string.IsNullOrWhiteSpace(name)) throw new FormatException($"pose {n} has no \"name\"");
            var p = new Pose { Name = Clean(name) };
            if (p.Name.Length == 0) throw new FormatException($"pose {n}: the name needs letters");
            if (d.TryGetValue("rig", out var rv) && rv is string rig && Clean(rig).Length > 0) p.Rig = Clean(rig);
            if (d.TryGetValue("words", out var w) && w is List<object> wl)
                foreach (var x in wl) if (x is string s && Clean(s).Length > 0) p.Words.Add(Clean(s));
            Flatten(d, "", p.Values);
            return p;
        }

        /// <summary>Every number (and true/false) in the object, keyed by its path: "nearArm.upper".</summary>
        static void Flatten(Dictionary<string, object> d, string prefix, Dictionary<string, float> into)
        {
            foreach (var kv in d)
            {
                if (prefix.Length == 0 && (kv.Key == "name" || kv.Key == "words" || kv.Key == "rig")) continue;
                string key = prefix + kv.Key;
                switch (kv.Value)
                {
                    case double x when !double.IsNaN(x) && !double.IsInfinity(x):
                        into[key] = (float)Math.Max(-720, Math.Min(720, x)); break;
                    case bool b: into[key] = b ? 1 : 0; break;
                    case Dictionary<string, object> sub: Flatten(sub, key + ".", into); break;
                }
            }
        }

        /// <summary>Lower case letters and hyphens only: a word a carving can use.</summary>
        static string Clean(string s) => new string((s ?? "").ToLowerInvariant().Where(c => char.IsLetter(c) || c == '-').ToArray());

        /// <summary>One pose as JSON on one line (for the carving's hidden "#pose:" line).</summary>
        public static string ToJson(Pose p)
        {
            var d = new Dictionary<string, object> { ["name"] = p.Name };
            if (p.Rig != "humanoid") d["rig"] = p.Rig;
            d["words"] = p.Words;
            foreach (var kv in p.Values.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var parts = kv.Key.Split('.');
                var at = d;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (!(at.TryGetValue(parts[i], out var next) && next is Dictionary<string, object> nd))
                        at[parts[i]] = nd = new Dictionary<string, object>();
                    at = nd;
                }
                at[parts[parts.Length - 1]] = kv.Value;
            }
            return MiniJson.Write(d);
        }

        /// <summary>The rig and angles alone, for comparing poses and keying caches.</summary>
        public static string AnglesKey(Pose p) =>
            p.Rig + ":" + string.Join(",", p.Values.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                .Select(k => k.Key.ToLowerInvariant() + "=" + k.Value.ToString("0.###", CultureInfo.InvariantCulture)));

        // ---------------------------------------------------------------- the poses in use

        /// <summary>
        /// Replaces the players' poses: the built-in ones, then each file's in order (a later pose replaces an earlier
        /// one of the same rig and name). Returns problems, one per bad file, naming the file.
        /// </summary>
        public static List<string> Load(IEnumerable<(string source, string json)> files)
        {
            var problems = new List<string>();
            var all = new List<Pose>(Builtin.Values);
            foreach (var (source, json) in files)
            {
                try { all.AddRange(FromJson(json)); }
                catch (Exception e) { problems.Add($"{source}: {e.Message}"); }
            }
            Install(all);
            return problems;
        }

        static void Install(IEnumerable<Pose> poses)
        {
            var current = new Dictionary<string, Pose>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in poses) current[p.Key] = p;
            _words = WordsOf(current.Values);
            _current = current;
        }

        static Dictionary<string, string> WordsOf(IEnumerable<Pose> poses)
        {
            var words = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in poses) foreach (var w in p.Words) words[KeyOf(p.Rig, w)] = p.Name;
            foreach (var p in poses) words[KeyOf(p.Rig, p.Name)] = p.Name;   // a pose's own name always wins
            return words;
        }

        public static IEnumerable<Pose> All => _current.Values;

        public static Pose Get(string rig, string name) => name != null && _current.TryGetValue(KeyOf(rig, name), out var p) ? p : null;

        /// <summary>The pose a word asks for on a rig: the carving's own recorded poses first, then the library.</summary>
        public static string NameFor(string word, string rig, IDictionary<string, Pose> local)
        {
            if (string.IsNullOrEmpty(word)) return null;
            if (local != null)
                foreach (var p in local.Values)
                    if (p.Rig == rig && (string.Equals(p.Name, word, StringComparison.OrdinalIgnoreCase) ||
                                         p.Words.Contains(word, StringComparer.OrdinalIgnoreCase)))
                        return p.Name;
            return _words.TryGetValue(KeyOf(rig, word), out var name) ? name : null;
        }

        /// <summary>
        /// The pose to draw: the carving's recorded one if it has it, else the built-in one, else the library's. A pose
        /// a carving didn't record is built in as it was carved, so players' own changes to built-in poses don't
        /// redraw other people's carvings differently (a carving made with a changed pose records it).
        /// </summary>
        public static Pose Resolve(string rig, string name, IDictionary<string, Pose> local)
        {
            name = name ?? "standing";
            string key = KeyOf(rig, name);
            if (local != null && local.TryGetValue(key, out var p)) return p;
            if (Builtin.TryGetValue(key, out var b)) return b;
            return Get(rig, name) ?? Get(rig, "standing") ?? Get(rig, NameFor("standing", rig, null))
                   ?? _current.Values.FirstOrDefault(v => v.Rig == rig) ?? new Pose { Name = "standing", Rig = rig };
        }

        // ---------------------------------------------------------------- recorded in carvings

        /// <summary>The poses a script carries in its hidden "#pose:" lines, keyed rig|name (bad lines are ignored).</summary>
        public static Dictionary<string, Pose> ReadTail(string script)
        {
            var map = new Dictionary<string, Pose>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in (script ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string l = line.Trim();
                if (!l.StartsWith(TailPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                try { foreach (var p in FromJson(l.Substring(TailPrefix.Length))) map[p.Key] = p; }
                catch (FormatException) { }
            }
            return map;
        }

        /// <summary>The script without its "#pose:" lines, as players see it.</summary>
        public static string WithoutTail(string script) =>
            string.Join("\n", (script ?? "").Replace("\r\n", "\n").Split('\n')
                .Where(l => !l.TrimStart().StartsWith(TailPrefix, StringComparison.OrdinalIgnoreCase))).TrimEnd();

        /// <summary>
        /// The script to store: as written, plus a "#pose:" line for each pose its figures use that isn't the built-in
        /// pose of that name (a player's own, or a changed built-in), so other players draw the same figure.
        /// </summary>
        public static string Record(string script)
        {
            string visible = WithoutTail(script);
            var spec = Language.Parser.Parse(visible).Spec;
            var lines = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in spec.Charges)
            {
                var type = Catalog.Find(c.Name);
                if (type == null || c.Pose == null || !seen.Add(KeyOf(type.Rig, c.Pose))) continue;
                var p = Get(type.Rig, c.Pose);
                if (p == null) continue;
                if (Builtin.TryGetValue(p.Key, out var b) && AnglesKey(b) == AnglesKey(p)) continue;
                lines.Add(TailPrefix + " " + ToJson(p));
            }
            return lines.Count == 0 ? visible : visible + "\n" + string.Join("\n", lines);
        }
    }
}
