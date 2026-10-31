using System;
using System.Collections.Generic;
using System.Linq;
using Engraving.Core.Figures;

namespace Engraving.Core.Language
{
    public enum SceneKind { War, Hunt, Gathering, Victory }

    /// <summary>The part a charge plays in a scene.</summary>
    public enum SceneSlot { None, Hero, Foe, Above, Ground }

    /// <summary>"a scene of war against a troll, the viking holding a sword and a shield".</summary>
    public class SceneSpec
    {
        public SceneKind Kind;
        /// <summary>The Viking (or shieldmaiden).</summary>
        public string Hero = "viking";
        /// <summary>The foe, the prey, what is gathered (scenery name) or the boss beaten; null until chosen.</summary>
        public string Subject;
        /// <summary>Victory over nothing: no Forsaken beaten yet (Odin's thumbs down).</summary>
        public bool Nothing;
        /// <summary>What the hero holds: an item, "fists", or null until chosen.</summary>
        public string Weapon;
        public bool Shield;
        public bool Ground = true;
        public CarveMode Mode = CarveMode.Relief;
        public int SourceLine;
    }

    /// <summary>
    /// What a player has done, for filling in a scene at carve time (Hippo, 2026-10-30): the trophies they have ever
    /// taken, the materials they know, and their weapons. Filled in by the mod from the player; empty in the preview tool.
    /// </summary>
    public class SceneProgress
    {
        /// <summary>Figure names of trophies taken ("troll", "eikthyr").</summary>
        public HashSet<string> Trophies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Scenery names of materials known ("copper", "tree").</summary>
        public HashSet<string> Materials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Weapon kind ("sword", "bow", "pickaxe") to how good the best one is (damage; ties go to the last equipped).</summary>
        public Dictionary<string, float> Weapons = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public bool Shield;
    }

    /// <summary>
    /// Scenes (Hippo, 2026-10-30): "a scene of war|hunt|gathering|victory" fills the field with a Viking and a
    /// counterpart chosen from the carver's own progress, so a hallway can be carved panel by panel as a journey.
    /// The choices are made when the carving is cut and written into its text, so everyone sees the same scene.
    /// </summary>
    public static class Scene
    {
        // ---------------------------------------------------------------- who is what

        /// <summary>Everything a scene can show, roughly in the order a player meets it (later = further along).</summary>
        static readonly string[] Journey =
        {
            "boar", "deer", "neck", "eikthyr",
            "greydwarf", "greydwarfshaman", "greydwarfbrute", "skeleton", "bear", "troll", "ghost", "elder",
            "blob", "leech", "draugr", "surtling", "wraith", "abomination", "bonemass",
            "serpent",
            "wolf", "drake", "bat", "ulv", "fenring", "golem", "dragon",
            "deathsquito", "fuling", "fulingshaman", "berserker", "growth", "lox", "unbjorn", "yagluth",
            "hare", "seeker", "seekersoldier", "tick", "gjall", "dverger", "queen",
            "charred", "morgen", "vulture", "valkyrie", "bonemaw", "fallenwarrior",
            "moose", "seal", "jotun", "jotunwitch", "barka",
        };

        static readonly string[] Bosses = { "eikthyr", "elder", "bonemass", "dragon", "yagluth", "queen" };
        static readonly HashSet<string> Game = new HashSet<string> { "boar", "deer", "neck", "hare", "lox", "moose", "seal" };
        static readonly string[] Materials = { "tree", "rock", "birch", "oak", "copper", "tin", "pine", "iron", "silver", "obsidian", "flametal" };
        static readonly HashSet<string> MeleeKinds = new HashSet<string> { "sword", "axe", "spear", "hammer", "club", "staff" };

        static int Rank(string name) { int i = Array.IndexOf(Journey, name); return i < 0 ? -1 : i; }
        public static bool IsBoss(string name) => Bosses.Contains(name);

        /// <summary>Trophy prefab names that don't simply name their creature.</summary>
        static readonly Dictionary<string, string> TrophyAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["theelder"] = "elder", ["dragonqueen"] = "dragon", ["goblinking"] = "yagluth", ["seekerqueen"] = "queen",
            ["foresttroll"] = "troll", ["frosttroll"] = "troll", ["hatchling"] = "drake", ["sgolem"] = "golem",
            ["goblin"] = "fuling", ["goblinbrute"] = "berserker", ["goblinshaman"] = "fulingshaman",
            ["seekerbrute"] = "seekersoldier", ["dvergr"] = "dverger", ["fallenvalkyrie"] = "valkyrie", ["volture"] = "vulture",
            ["bonemawserpent"] = "bonemaw", ["bjorn"] = "bear", ["bjornundead"] = "unbjorn", ["jotunwarrior"] = "jotun",
            ["cultist"] = "fenring", ["deerwhite"] = "deer",
        };

        /// <summary>The figure a trophy prefab ("TrophyForestTroll") stands for, or null if no figure draws it.</summary>
        public static string FigureForTrophy(string prefab)
        {
            string n = new string((prefab ?? "").ToLowerInvariant().Where(char.IsLetter).ToArray());
            if (n.StartsWith("trophy")) n = n.Substring(6);
            if (n.Length == 0) return null;
            if (TrophyAliases.TryGetValue(n, out var a)) return a;
            if (Journey.Contains(n)) return n;
            // variants: "skeletonpoison", "draugrelite", "charredarcher", "blobfrost"
            var best = Journey.Where(j => n.StartsWith(j)).OrderByDescending(j => j.Length).FirstOrDefault();
            if (best != null) return best;
            var f = Catalog.Find(n);
            return f != null && f.Posable ? f.Id : null;
        }

        /// <summary>The scenery a known-material token ("$item_copperore") lets you gather, or null.</summary>
        public static string MaterialFor(string token)
        {
            string n = new string((token ?? "").ToLowerInvariant().Where(char.IsLetter).ToArray());
            if (n.StartsWith("item")) n = n.Substring(4);
            switch (n)
            {
                case "wood": return "tree";
                case "stone": return "rock";
                case "finewood": return "birch";
                case "roundlog": return "pine";
                case "copperore": return "copper";
                case "tinore": return "tin";
                case "ironscrap": case "ironore": return "iron";
                case "silverore": return "silver";
                case "obsidian": return "obsidian";
                case "flametalore": case "flametalorenew": return "flametal";
            }
            return null;
        }

        // ---------------------------------------------------------------- reading

        public static bool IsScene(IList<string> words) => words.Take(4).Contains("scene") || words.Take(4).Contains("scenes");

        static readonly HashSet<string> Skip = new HashSet<string>
        {
            "a", "an", "the", "of", "scene", "scenes", "with", "holding", "wielding", "carrying", "bearing", "and", "by", "his", "her",
            "their", "in", "at", "to", "from", "is", "showing", "on", "s",
        };

        /// <summary>A scene statement's words as a scene, with any problems.</summary>
        public static SceneSpec Read(IList<string> words, int line, List<string> problems)
        {
            var sc = new SceneSpec { SourceLine = line };
            bool kindSet = false, after = false;
            string material = null;
            for (int i = 0; i < words.Count; i++)
            {
                string w = words[i];
                switch (w)
                {
                    case "war": case "battle": case "fight": case "fighting": case "combat": case "duel": case "slaying":
                        sc.Kind = SceneKind.War; kindSet = true; after = true; continue;
                    case "hunt": case "hunting": case "chase": case "chasing": case "hunter":
                        sc.Kind = SceneKind.Hunt; kindSet = true; after = true; continue;
                    case "gathering": case "gather": case "harvest": case "harvesting": case "labour": case "labor": case "work":
                        sc.Kind = SceneKind.Gathering; kindSet = true; after = true; continue;
                    case "mining": case "mine":
                        sc.Kind = SceneKind.Gathering; kindSet = true; after = true; material = material ?? "rock"; continue;
                    case "chopping": case "woodcutting": case "felling": case "logging": case "lumberjacking":
                        sc.Kind = SceneKind.Gathering; kindSet = true; after = true; material = material ?? "tree"; continue;
                    case "victory": case "triumph": case "glory": case "conquest":
                        sc.Kind = SceneKind.Victory; kindSet = true; after = true; continue;
                    case "against": case "versus": case "vs": case "over": case "for": case "after":
                        after = true; continue;
                    case "nothing": case "none": case "noone": case "nobody": case "naught":
                        sc.Nothing = true; continue;
                    case "without": case "no":
                        if (i + 1 < words.Count && words[i + 1] == "ground") { sc.Ground = false; i++; }
                        continue;
                    case "ground": sc.Ground = true; continue;
                    case "bare-handed": case "barehanded": case "unarmed": case "fists": case "fist": sc.Weapon = "fists"; continue;
                    case "relief": case "raised": case "embossed": sc.Mode = CarveMode.Relief; continue;
                    case "incised": case "outlined": case "outline": case "engraved": case "lined": sc.Mode = CarveMode.Incised; continue;
                    case "sunken": case "sunk": case "hollow": case "intaglio": sc.Mode = CarveMode.Sunken; continue;
                }
                if (Skip.Contains(w)) continue;
                var item = Props.ItemFor(w);
                if (item != null)
                {
                    if (item == "shield") sc.Shield = true;
                    else if (item != "thumbsup" && item != "thumbsdown") sc.Weapon = item;
                    continue;
                }
                var f = Catalog.Find(w);
                if (f != null && f.Rig == "scenery") { material = f.Id; continue; }
                if (f != null && f.Posable)
                {
                    bool heroWord = f.Rig == "humanoid" && (f.Id == "viking" || f.Id == "shieldmaiden");
                    if (heroWord && (!after || sc.Subject != null)) sc.Hero = f.Id;
                    else sc.Subject = f.Id;
                    continue;
                }
                problems.Add($"a scene doesn't know '{w}'");
            }
            if (!kindSet) problems.Add("which scene? war, hunt, gathering or victory, e.g. 'a scene of war'");
            if (sc.Kind == SceneKind.Gathering) sc.Subject = material ?? (sc.Subject != null && Catalog.Find(sc.Subject)?.Rig == "scenery" ? sc.Subject : null);
            else if (material != null) problems.Add($"'{material}' is for a scene of gathering");
            if (sc.Kind == SceneKind.Victory && sc.Subject != null && !IsBoss(sc.Subject))
                problems.Add($"a victory is over one of the Forsaken (Eikthyr, the Elder, Bonemass, Moder, Yagluth, the Queen), not '{sc.Subject}'");
            if (sc.Kind == SceneKind.Victory && sc.Nothing) sc.Subject = null;
            return sc;
        }

        /// <summary>The scene written out in full, as the carving stores it once the choices are made.</summary>
        public static string Write(SceneSpec sc)
        {
            string Article(string n) => "aeiou".IndexOf(n[0]) >= 0 ? "an " + n : "a " + n;
            string Named(string n) => IsBoss(n) ? (n == "eikthyr" || n == "bonemass" || n == "yagluth" ? n : n == "dragon" ? "moder" : "the " + n) : Article(n);
            string head;
            switch (sc.Kind)
            {
                case SceneKind.War: head = "a scene of war" + (sc.Subject != null ? " against " + Named(sc.Subject) : ""); break;
                case SceneKind.Hunt: head = "a scene of hunting" + (sc.Subject != null ? " " + Article(sc.Subject) : ""); break;
                case SceneKind.Gathering: head = "a scene of gathering" + (sc.Subject != null ? " " + (sc.Subject == "tree" ? "wood" : sc.Subject == "rock" ? "stone" : sc.Subject) : ""); break;
                default: head = "a scene of victory" + (sc.Nothing ? " over nothing" : sc.Subject != null ? " over " + Named(sc.Subject) : ""); break;
            }
            var tail = new List<string>();
            var held = new List<string>();
            if (sc.Weapon != null && sc.Weapon != "fists") held.Add(Article(sc.Weapon));
            if (sc.Shield) held.Add("a shield");
            string hero = "the " + sc.Hero;
            if (held.Count > 0) tail.Add(hero + " holding " + string.Join(" and ", held));
            else if (sc.Weapon == "fists") tail.Add(hero + " bare-handed");
            else if (sc.Hero != "viking") tail.Add(hero);
            if (!sc.Ground) tail.Add("without ground");
            if (sc.Mode == CarveMode.Incised) tail.Add("incised");
            if (sc.Mode == CarveMode.Sunken) tail.Add("sunken");
            return head + (tail.Count > 0 ? ", " + string.Join(", ", tail) : "");
        }

        // ---------------------------------------------------------------- choosing, at carve time

        /// <summary>
        /// Fills in every scene the script leaves open from what the player has done, and checks the ones it names
        /// were earned. The result is what the carving stores. Problems stop the carving.
        /// </summary>
        public static string Fill(string script, SceneProgress progress, List<string> problems)
        {
            if (progress == null || string.IsNullOrEmpty(script)) return script;
            var lines = script.Split('\n');
            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                int hash = line.IndexOf('#');
                string body = hash >= 0 ? line.Substring(0, hash) : line, comment = hash >= 0 ? line.Substring(hash) : "";
                var segs = body.Split(';');
                bool changed = false;
                for (int si = 0; si < segs.Length; si++)
                {
                    var words = Words(segs[si]);
                    if (!IsScene(words)) continue;
                    var sc = Read(words, li + 1, new List<string>());
                    Choose(sc, progress, problems);
                    // written out in full, so the choices travel with the carving
                    segs[si] = Write(sc);
                    changed = true;
                }
                if (changed) lines[li] = string.Join("; ", segs.Select(s => s.Trim()).Where(s => s.Length > 0)) + (comment.Length > 0 ? " " + comment : "");
            }
            return string.Join("\n", lines);
        }

        static List<string> Words(string s)
        {
            var toks = Lexer.Tokenize(s, new List<Diagnostic>());
            return toks.Where(t => t.Kind == TokenKind.Word).Select(t => t.Text).ToList();
        }

        static string Show(string n) => IsBoss(n) && n == "dragon" ? "Moder" : n;

        static void Choose(SceneSpec sc, SceneProgress p, List<string> problems)
        {
            string Best(IEnumerable<string> pool) => pool.OrderByDescending(Rank).FirstOrDefault();
            switch (sc.Kind)
            {
                case SceneKind.War:
                case SceneKind.Hunt:
                {
                    bool hunt = sc.Kind == SceneKind.Hunt;
                    if (sc.Subject == null)
                    {
                        var pool = p.Trophies.Where(t => Rank(t) >= 0 && !IsBoss(t) && Game.Contains(t) == hunt);
                        sc.Subject = Best(pool);
                        if (sc.Subject == null)
                            problems.Add(hunt ? "No prey yet: take a trophy from a deer, a boar or the like first."
                                              : "No foes yet: take a trophy from something you've fought first.");
                    }
                    else if (!p.Trophies.Contains(sc.Subject))
                        problems.Add($"You haven't taken a {Show(sc.Subject)} trophy yet.");
                    break;
                }
                case SceneKind.Gathering:
                {
                    var known = new HashSet<string>(p.Materials) { "tree", "rock" };
                    if (known.Contains("birch")) known.Add("oak");     // fine wood comes from both
                    if (sc.Subject == null)
                    {
                        // the furthest material the player can cut with what they have
                        bool axe = p.Weapons.ContainsKey("axe"), pick = p.Weapons.ContainsKey("pickaxe");
                        sc.Subject = Materials.Reverse().FirstOrDefault(m => known.Contains(m) && (Scenery.IsWood(m) ? axe : pick)) ?? "tree";
                    }
                    else if (!known.Contains(sc.Subject))
                        problems.Add($"You haven't found any {sc.Subject} yet.");
                    break;
                }
                case SceneKind.Victory:
                    if (sc.Subject == null && !sc.Nothing)
                    {
                        sc.Subject = Bosses.Reverse().FirstOrDefault(b => p.Trophies.Contains(b));
                        // no Forsaken beaten yet: not an error, Odin has a verdict (Hippo, 2026-10-30)
                        if (sc.Subject == null) sc.Nothing = true;
                    }
                    else if (sc.Subject != null && !p.Trophies.Contains(sc.Subject))
                        problems.Add($"You haven't beaten {(sc.Subject == "dragon" ? "Moder" : sc.Subject)} yet.");
                    return;   // the victor holds nothing up but the trophy
            }

            if (sc.Weapon == null)
            {
                IEnumerable<string> kinds;
                if (sc.Kind == SceneKind.Gathering) kinds = new[] { Scenery.IsWood(sc.Subject ?? "tree") ? "axe" : "pickaxe" };
                else if (sc.Kind == SceneKind.Hunt) kinds = p.Weapons.Keys.Where(k => k == "bow" || k == "spear").ToList() is var r && r.Any() ? r : p.Weapons.Keys.Where(MeleeKinds.Contains);
                else kinds = p.Weapons.Keys.Where(MeleeKinds.Contains);
                sc.Weapon = kinds.Where(p.Weapons.ContainsKey).OrderByDescending(k => p.Weapons[k]).FirstOrDefault() ?? "fists";
            }
            if (sc.Kind == SceneKind.War && p.Shield && sc.Weapon != "bow" && sc.Weapon != "staff") sc.Shield = true;
        }

        // ---------------------------------------------------------------- what it shows

        /// <summary>Stand-ins for a scene left open where no player is around to choose (the preview tool).</summary>
        static string Placeholder(SceneKind k) => k == SceneKind.War ? "greydwarf" : k == SceneKind.Hunt ? "deer" : k == SceneKind.Gathering ? "tree" : null;

        /// <summary>What humanoid foes fight with.</summary>
        static readonly Dictionary<string, string[]> FoeArms = new Dictionary<string, string[]>
        {
            ["troll"] = new[] { "club" }, ["skeleton"] = new[] { "sword", "shield" }, ["draugr"] = new[] { "axe" },
            ["fuling"] = new[] { "club" }, ["berserker"] = new[] { "sword" }, ["fulingshaman"] = new[] { "staff" },
            ["greydwarfshaman"] = new[] { "staff" }, ["dverger"] = new[] { "staff" }, ["charred"] = new[] { "sword" },
            ["fallenwarrior"] = new[] { "sword", "shield" }, ["jotun"] = new[] { "club" }, ["jotunwitch"] = new[] { "staff" },
            ["viking"] = new[] { "axe", "shield" }, ["shieldmaiden"] = new[] { "sword", "shield" },
        };

        static string AttackPose(string rig)
        {
            switch (rig)
            {
                case "humanoid": return "striking";
                case "quadruped": return "charging";
                case "flyer": return "flying";
                case "serpent": return "striking";
                case "blob": return "jumping";
                case "insect": return "attacking";
            }
            return null;
        }

        /// <summary>The scene as charges: the hero, the counterpart, anything above, and the ground line.</summary>
        public static List<ChargeSpec> Expand(SceneSpec sc)
        {
            var list = new List<ChargeSpec>();
            ChargeSpec Charge(string name, SceneSlot slot) { var c = new ChargeSpec { Name = name, Slot = slot, Mode = sc.Mode, SourceLine = sc.SourceLine }; list.Add(c); return c; }

            var hero = Charge(sc.Hero, SceneSlot.Hero);
            string subject = sc.Subject ?? (sc.Kind == SceneKind.Victory ? null : Placeholder(sc.Kind));
            string weapon = sc.Weapon == "fists" ? null : sc.Weapon;
            if (weapon != null) hero.Held.Add(weapon);
            if (sc.Shield && sc.Kind == SceneKind.War) hero.Held.Add("shield");
            var type = subject != null ? Catalog.Find(subject) : null;

            switch (sc.Kind)
            {
                case SceneKind.War:
                {
                    hero.Pose = weapon == "spear" ? "thrusting" : weapon == "bow" ? "aiming" : "striking";
                    var foe = Charge(subject, SceneSlot.Foe);
                    foe.Flipped = true;     // face to face
                    foe.Pose = AttackPose(type?.Rig);
                    foe.SceneGap = 0.15f;
                    if (type?.Rig == "humanoid" && FoeArms.TryGetValue(subject, out var arms)) foe.Held.AddRange(arms);
                    if (type?.Rig == "flyer") foe.SceneLift = 0.45f;
                    break;
                }
                case SceneKind.Hunt:
                {
                    // both run the same way, the hunter behind
                    hero.Pose = weapon == "bow" ? "aiming" : weapon == "spear" ? "throwing" : "running";
                    var prey = Charge(subject, SceneSlot.Foe);
                    prey.Pose = type?.Rig == "quadruped" ? "running" : type?.Rig == "flyer" ? "flying" : type?.Rig == "serpent" ? "swimming" : null;
                    prey.SceneGap = 0.6f;
                    if (type?.Rig == "flyer") prey.SceneLift = 0.6f;
                    break;
                }
                case SceneKind.Gathering:
                {
                    hero.Pose = "striking";
                    var target = Charge(subject, SceneSlot.Foe);
                    target.SceneGap = Scenery.IsWood(subject) ? -0.05f : 0.05f;
                    break;
                }
                case SceneKind.Victory:
                {
                    hero.Pose = "triumphant";
                    if (subject == null)
                    {
                        // no Forsaken beaten: the triumphant Viking holds up nothing, and Odin isn't impressed
                        var odin = Charge("odin", SceneSlot.Foe);
                        odin.Pose = "judging";
                        odin.Held.Add("thumbsdown");
                        odin.Held.Add("staff");
                        odin.Flipped = true;
                        odin.SceneGap = 0.5f;
                    }
                    else
                    {
                        Charge("trophy of " + subject, SceneSlot.Above);
                        Charge("altar of " + subject, SceneSlot.Foe).SceneGap = 0.45f;
                    }
                    break;
                }
            }
            if (sc.Ground) Charge("", SceneSlot.Ground);
            return list;
        }
    }
}
