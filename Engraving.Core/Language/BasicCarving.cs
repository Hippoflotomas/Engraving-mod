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
            /// <summary>A figure's rig ("humanoid", "quadruped"...), or null for a shape or picture.</summary>
            public string Rig;
            public bool IsDivider => One == null;
            public bool IsFigure => Rig != null;
            public override string ToString() => Label;
        }

        static ChargeChoice Shape(string label, string one, string many) => new ChargeChoice { Label = label, One = one, Many = many };
        static ChargeChoice Divider(string label) => new ChargeChoice { Label = label };

        /// <summary>A figure on the menu, by its name in the catalog (the plural is the first plural word it answers to).</summary>
        static ChargeChoice Figure(string label, string name)
        {
            var type = Figures.Catalog.Find(name);
            string many = type?.Species.Words.FirstOrDefault(w => w.EndsWith("s") && Figures.Catalog.Find(w)?.Id == type.Id) ?? name;
            return new ChargeChoice { Label = label, One = name, Many = many, Rig = type?.Rig ?? "humanoid" };
        }

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

        /// <summary>
        /// The figures, in groups so no list is long (Hippo, 2026-11-01): the editor shows one group at a time, picked
        /// from <see cref="Groups"/>. Roughly in the order a player meets them.
        /// </summary>
        public static readonly (string group, ChargeChoice[] figures)[] FigureGroups =
        {
            ("People", new[] { Figure("Viking", "viking"), Figure("Shieldmaiden", "shieldmaiden"), Figure("Odin (the wanderer)", "odin"),
                Figure("Bog witch", "bogwitch"), Figure("Valkyrie", "valkyrie") }),
            ("Meadows", new[] { Figure("Boar", "boar"), Figure("Deer", "deer"), Figure("Neck", "neck"), Figure("Raven", "raven"),
                Figure("Chicken", "chicken"), Figure("Eikthyr", "eikthyr") }),
            ("Black Forest", new[] { Figure("Greydwarf", "greydwarf"), Figure("Greydwarf brute", "greydwarfbrute"),
                Figure("Greydwarf shaman", "greydwarfshaman"), Figure("Skeleton", "skeleton"), Figure("Troll", "troll"), Figure("Bear", "bear"),
                Figure("Ghost", "ghost"), Figure("The Elder", "elder") }),
            ("Ocean", new[] { Figure("Serpent", "serpent"), Figure("Seal", "seal") }),
            ("Swamp", new[] { Figure("Blob", "blob"), Figure("Oozer", "oozer"), Figure("Leech", "leech"), Figure("Draugr", "draugr"),
                Figure("Surtling", "surtling"), Figure("Wraith", "wraith"), Figure("Abomination", "abomination"), Figure("Bonemass", "bonemass") }),
            ("Mountains", new[] { Figure("Wolf", "wolf"), Figure("Drake", "drake"), Figure("Bat", "bat"), Figure("Ulv", "ulv"),
                Figure("Fenring", "fenring"), Figure("Stone golem", "golem"), Figure("Moder (dragon)", "dragon") }),
            ("Plains", new[] { Figure("Deathsquito", "deathsquito"), Figure("Fuling", "fuling"), Figure("Fuling berserker", "berserker"),
                Figure("Fuling shaman", "fulingshaman"), Figure("Growth", "growth"), Figure("Lox", "lox"), Figure("Lox calf", "calf"),
                Figure("Unbjorn", "unbjorn"), Figure("Yagluth", "yagluth") }),
            ("Mistlands", new[] { Figure("Hare", "hare"), Figure("Seeker", "seeker"), Figure("Seeker soldier", "seekersoldier"),
                Figure("Tick", "tick"), Figure("Gjall", "gjall"), Figure("Dverger", "dverger"), Figure("The Queen", "queen") }),
            ("Ashlands", new[] { Figure("Charred", "charred"), Figure("Morgen", "morgen"), Figure("Volture", "vulture"),
                Figure("Fallen valkyrie", "fallenwarrior"), Figure("Bonemaw", "bonemaw"), Figure("Barka", "barka") }),
            ("Deep North", new[] { Figure("Moose", "moose"), Figure("Jotun", "jotun"), Figure("Jotun witch", "jotunwitch") }),
        };

        public const string ShapesGroup = "Shapes", PicturesGroup = "Your pictures";

        /// <summary>The first dropdown of "Carve": shapes, each group of figures, then the player's pictures.</summary>
        public static List<string> Groups(bool pictures)
        {
            var g = new List<string> { ShapesGroup };
            g.AddRange(FigureGroups.Select(f => f.group));
            if (pictures) g.Add(PicturesGroup);
            return g;
        }

        /// <summary>The group a charge belongs to (pictures are anything not built in).</summary>
        public static string GroupOf(ChargeChoice c)
        {
            if (c == null || BuiltInCharges.Contains(c)) return ShapesGroup;
            foreach (var (group, figures) in FigureGroups) if (figures.Contains(c)) return group;
            return PicturesGroup;
        }

        /// <summary>A figure's poses, its rig's default first.</summary>
        public static List<Option> PosesFor(string rig)
        {
            string first = Figures.PoseLibrary.NameFor("standing", rig, null) ?? "standing";
            return Figures.PoseLibrary.All.Where(p => p.Rig == rig).Select(p => p.Name).Distinct()
                .OrderBy(n => n == first ? 0 : 1).Select(n => new Option(Cap(n), n)).ToList();
        }

        /// <summary>What a person holds.</summary>
        public static readonly Option[] Holdings =
        {
            new Option("Holding nothing", ""),
            new Option("Axe", "an axe"), new Option("Axe and shield", "an axe and a shield"),
            new Option("Sword", "a sword"), new Option("Sword and shield", "a sword and a shield"),
            new Option("Spear", "a spear"), new Option("Spear and shield", "a spear and a shield"),
            new Option("Hammer", "a hammer"), new Option("Club", "a club"), new Option("Staff", "a staff"),
            new Option("Bow", "a bow"), new Option("Pickaxe", "a pickaxe"), new Option("Shield", "a shield"),
            new Option("Thumbs up", "a thumbsup"), new Option("Thumbs down", "a thumbsdown"),
        };

        public static readonly Option[] Facings = { new Option("Facing right", "right"), new Option("Facing left", "left") };

        // ------------------------------------------------------------------ the scene tab

        public static readonly Option[] SceneKinds =
        {
            new Option("War", "war"), new Option("Hunting", "hunt"), new Option("Gathering", "gathering"), new Option("Victory", "victory"),
        };

        public static readonly Option[] Heroes = { new Option("A Viking", "viking"), new Option("A shieldmaiden", "shieldmaiden") };

        /// <summary>The hero's weapon: the best carried (chosen when carved), each kind the player carries, or bare hands.</summary>
        public static List<Option> SceneWeapons(SceneProgress progress)
        {
            var list = new List<Option> { new Option("Best you carry", "") };
            if (progress != null)
                foreach (var kv in progress.Weapons.OrderByDescending(k => k.Value))
                    list.Add(new Option(Cap(kv.Key), kv.Key));
            list.Add(new Option("Bare-handed", "fists"));
            return list;
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

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
        /// <summary>A figure's pose (null: its rig's default), what it holds ("an axe and a shield"), which way it faces.</summary>
        public string Pose, Holding = "", Facing = "right";

        /// <summary>The Scene tab is showing: the carving is background, border and one scene.</summary>
        public bool SceneMode;
        public SceneKind Scene = SceneKind.War;
        /// <summary>A scene's foe, prey, material or Forsaken (null: the furthest along, chosen when carved).</summary>
        public string SceneSubject;
        public string Hero = "viking";
        /// <summary>The hero's weapon kind ("sword"), "fists", or null for the best carried.</summary>
        public string SceneWeapon;
        public bool Ground = true;

        // ------------------------------------------------------------------ what's greyed out

        public bool HasBorder => Top || Bottom || Left || Right;
        public bool BorderHasStrands => HasBorder && BorderStyle == "knotwork";
        public bool BorderHasLines => HasBorder && BorderStyle == "lines";
        public bool BorderHasInscription => HasBorder && BorderStyle == "runes";
        public bool HasCharge => Charge != null && !string.IsNullOrEmpty(Charge.One);
        public bool HasArrangement => HasCharge && Count > 1;
        public bool IsFigure => HasCharge && Charge.IsFigure;
        public bool CanHold => IsFigure && Charge.Rig == "humanoid";

        /// <summary>The scene's subject menu: the furthest along first (chosen when carved), then what the player has earned.</summary>
        public static List<Option> SceneSubjects(SceneKind kind, SceneProgress progress)
        {
            string auto = kind == SceneKind.Gathering ? "Furthest you can gather" : kind == SceneKind.Victory ? "Last Forsaken beaten" : "Furthest along";
            var list = new List<Option> { new Option(auto, "") };
            list.AddRange(Language.Scene.Earned(kind, progress).Select(e => new Option(e.label, e.value)));
            return list;
        }

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
            if (!IsFigure) { Pose = null; Holding = ""; Facing = "right"; }
            else if (Pose != null && PosesFor(Charge.Rig).All(o => o.Value != Pose)) Pose = null;
            if (!CanHold) Holding = "";
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

            if (SceneMode)
            {
                var sc = new SceneSpec
                {
                    Kind = Scene, Subject = string.IsNullOrEmpty(SceneSubject) || SceneSubject == "nothing" ? null : SceneSubject,
                    Nothing = SceneSubject == "nothing", Hero = Hero, Ground = Ground,
                    Weapon = Scene == SceneKind.Victory || string.IsNullOrEmpty(SceneWeapon) ? null : SceneWeapon,
                    Mode = Style == "incised" ? CarveMode.Incised : Style == "sunken" ? CarveMode.Sunken : CarveMode.Relief,
                };
                sb.Append(Language.Scene.Write(sc)).Append('\n');
            }
            else if (HasCharge)
            {
                string noun = Count > 1 ? Charge.Many : Charge.One;
                string count = Count > 1 ? CountWords[Count] : Article(noun);
                sb.Append(Position).Append(": ").Append(count).Append(' ').Append(noun);
                if (IsFigure && Pose != null && Pose != PosesFor(Charge.Rig).FirstOrDefault()?.Value) sb.Append(' ').Append(Pose);
                if (Count > 1 && Arrangement != "grid") sb.Append(" in a ").Append(Arrangement);
                if (CanHold && Holding.Length > 0) sb.Append(", holding ").Append(Holding);
                if (IsFigure && Facing == "left") sb.Append(", facing left");
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
            // figures: each one in each pose, alone and as a pair, facing each way; people with each thing to hold
            foreach (var figure in FigureGroups.SelectMany(g => g.figures))
                foreach (var pose in PosesFor(figure.Rig))
                    foreach (int count in new[] { 1, 2 })
                        foreach (var hold in figure.Rig == "humanoid" ? Holdings : Holdings.Take(1))
                            yield return new BasicCarving
                            {
                                Charge = figure, Pose = pose.Value, Count = count, Holding = hold.Value, Facing = count == 2 ? "left" : "right",
                                Arrangement = "row", Top = false, Bottom = false, Left = false, Right = false,
                            };
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
            // scenes: each kind, each style, and each subject a well-travelled player could pick
            var everything = new SceneProgress();
            foreach (var t in new[] { "troll", "deer", "draugr", "lox", "eikthyr", "dragon", "queen" }) everything.Trophies.Add(t);
            foreach (var m in new[] { "copper", "iron", "obsidian" }) everything.Materials.Add(m);
            everything.Weapons["sword"] = 30; everything.Weapons["bow"] = 20; everything.Weapons["pickaxe"] = 10;
            foreach (var kind in SceneKinds)
            {
                var k = (SceneKind)System.Array.IndexOf(SceneKinds, kind);
                foreach (var subject in SceneSubjects(k, everything))
                    foreach (var weapon in SceneWeapons(everything))
                        foreach (var hero in Heroes)
                            yield return new BasicCarving
                            {
                                SceneMode = true, Scene = k, SceneSubject = subject.Value.Length == 0 ? null : subject.Value,
                                SceneWeapon = weapon.Value.Length == 0 ? null : weapon.Value, Hero = hero.Value, Ground = hero.Value == "viking",
                                Style = Styles[(int)k % 3].Value,
                            };
            }
        }
    }
}
