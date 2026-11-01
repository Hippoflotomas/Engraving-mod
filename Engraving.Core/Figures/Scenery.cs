using System;
using System.Collections.Generic;
using System.Linq;
using Engraving.Core.Geometry;

namespace Engraving.Core.Figures
{
    /// <summary>
    /// Things that stand in a scene but don't move: trees and rocks to gather from, a boss's trophy, and the
    /// sacrificial stone it belongs on. Built from parts like the figures, so overlaps get the same cut lines.
    /// Sizes are true-ish metres (a Viking is about 1.9); a scene shrinks anything too big to share the panel.
    /// </summary>
    public static class Scenery
    {
        /// <summary>The scenery names, each with the words that ask for it.</summary>
        static readonly Dictionary<string, string[]> Names = new Dictionary<string, string[]>
        {
            ["tree"] = new[] { "trees", "beech", "beeches", "wood", "woods", "timber", "log", "logs" },
            ["pine"] = new[] { "pines", "fir", "firs", "spruce", "corewood" },
            ["birch"] = new[] { "birches" },
            ["oak"] = new[] { "oaks" },
            ["rock"] = new[] { "rocks", "boulder", "boulders", "stone", "stones" },
            ["copper"] = new[] { "copperore" },
            ["tin"] = new[] { "tinore" },
            ["iron"] = new[] { "ironore", "scrap", "scrapiron", "muddyscrap", "muddyscrappile" },
            ["silver"] = new[] { "silverore" },
            ["obsidian"] = new string[0],
            ["flametal"] = new[] { "flametalore" },
            // crafting stations, for a scene of progress (Hippo, 2026-11-01)
            ["workbench"] = new[] { "workbenches", "bench", "craftingbench" },
            ["forge"] = new[] { "forges", "smithy" },
            ["cauldron"] = new[] { "cauldrons" },
            ["stonecutter"] = new[] { "stonecutters" },
            ["artisantable"] = new[] { "artisantables", "artisanstation", "artisan" },
            ["blackforge"] = new[] { "blackforges" },
            ["galdrtable"] = new[] { "galdrtables", "galdr", "magetable" },
        };

        /// <summary>The crafting stations, in the order a player builds them.</summary>
        public static readonly string[] Stations = { "workbench", "forge", "cauldron", "stonecutter", "artisantable", "blackforge", "galdrtable" };
        public static bool IsStation(string name) => Array.IndexOf(Stations, name) >= 0;

        static readonly Dictionary<string, Species> ByWord = new Dictionary<string, Species>(StringComparer.OrdinalIgnoreCase);

        static Scenery()
        {
            foreach (var kv in Names)
            {
                var s = new Species { Name = kv.Key };
                s.Words.AddRange(kv.Value);
                ByWord[kv.Key] = s;
                foreach (var w in kv.Value) ByWord[w] = s;
            }
        }

        /// <summary>The scenery a (letters-only, lower case) name asks for: "copper", "copperdeposit", "pinetree".</summary>
        public static Species Find(string n)
        {
            if (string.IsNullOrEmpty(n)) return null;
            if (ByWord.TryGetValue(n, out var s)) return s;
            foreach (var suffix in new[] { "deposits", "deposit", "veins", "vein", "ore", "trees", "tree", "pile", "rock", "rocks" })
                if (n.Length > suffix.Length && n.EndsWith(suffix) && ByWord.TryGetValue(n.Substring(0, n.Length - suffix.Length), out s))
                    return s;
            return null;
        }

        /// <summary>True for the scenery cut with an axe (trees); everything else wants a pickaxe.</summary>
        public static bool IsWood(string name) => name == "tree" || name == "pine" || name == "birch" || name == "oak";

        // ---------------------------------------------------------------- building

        public static List<Part> Build(Species s)
        {
            var parts = new List<Part>();
            float scale = 1;
            switch (s.Name)
            {
                case "pine": Pine(parts); scale = 1.5f; break;
                case "birch": Birch(parts); scale = 1.4f; break;
                case "oak": Oak(parts); scale = 1.5f; break;
                case "tree": Beech(parts); scale = 1.4f; break;
                case "iron": ScrapPile(parts); break;
                case "obsidian": Crystals(parts); break;
                case "tin": Boulder(parts, 0.75f, 7, true); break;
                case "workbench": Workbench(parts); break;
                case "forge": Forge(parts, false); break;
                case "blackforge": Forge(parts, true); break;
                case "cauldron": Cauldron(parts); break;
                case "stonecutter": Stonecutter(parts); break;
                case "artisantable": ArtisanTable(parts); break;
                case "galdrtable": GaldrTable(parts); break;
                default: Boulder(parts, 1.15f, 3, s.Name != "rock"); break;
            }
            Geo.Ground(parts, 0, scale);
            return parts;
        }

        static float[] Poly(params Vector2[] pts) => Geo.Poly(pts);
        static Vector2 V(float x, float y) => new Vector2(x, y);

        /// <summary>A trunk flaring into roots, with a few bark lines.</summary>
        static Part Trunk(float rootW, float w0, float w1, float top, int seed, bool birch = false)
        {
            var poly = Poly(V(-rootW, 0), V(-w0 * 1.2f, 0.12f), V(-w0, 0.35f), V(-w1, top), V(w1, top), V(w0, 0.35f), V(w0 * 1.2f, 0.12f), V(rootW, 0));
            return new Part("trunk", (x, y) => Sdf.Polygon(x, y, poly), (x, y) =>
            {
                if (y < 0.2f || y > top) return float.MaxValue;
                float w = M.Lerp(w0, w1, (y - 0.35f) / (top - 0.35f));
                if (birch)
                {
                    // dark bands across a birch's bark
                    float band = M.Wrap(y + M.Hash((int)(y * 3), seed) * 0.2f, 0.32f);
                    float across = M.Abs(x - (M.Hash((int)(y * 3), seed + 1) - 0.5f) * w) - w * 0.45f;
                    return M.Max(M.Abs(band - 0.16f) - 0.004f, across);
                }
                // two long furrows up the trunk
                return M.Min(M.Abs(x + w * 0.35f), M.Abs(x - w * 0.3f)) + (y > top - 0.25f ? 1 : 0);
            });
        }

        /// <summary>Clumps of leaves: each a circle, drawn back (top) to front (bottom) so each clump's edge is cut over the one behind.</summary>
        static void Clumps(List<Part> parts, params (float x, float y, float r)[] clumps)
        {
            foreach (var c in clumps.OrderByDescending(c => c.y))
            {
                var cc = c;
                parts.Add(new Part("leaves", (x, y) => Sdf.Circle(x - cc.x, y - cc.y, cc.r)));
            }
        }

        static void Beech(List<Part> parts)
        {
            parts.Add(Trunk(0.32f, 0.16f, 0.1f, 2.1f, 1));
            Clumps(parts, (0, 3.05f, 0.55f), (-0.45f, 2.85f, 0.5f), (0.5f, 2.8f, 0.52f), (-0.8f, 2.3f, 0.48f), (0.78f, 2.3f, 0.5f), (0, 2.45f, 0.6f), (-0.35f, 1.95f, 0.42f), (0.38f, 1.95f, 0.4f));
        }

        static void Oak(List<Part> parts)
        {
            parts.Add(Trunk(0.48f, 0.26f, 0.18f, 1.6f, 2));
            // two thick boughs into the crown
            parts.Add(new Part("bough", (x, y) => M.Min(Sdf2.Capsule(x, y, V(-0.05f, 1.4f), V(-0.75f, 2.0f), 0.13f, 0.07f),
                                                        Sdf2.Capsule(x, y, V(0.05f, 1.45f), V(0.7f, 2.05f), 0.12f, 0.07f))));
            Clumps(parts, (0, 2.75f, 0.6f), (-0.7f, 2.55f, 0.55f), (0.72f, 2.55f, 0.55f), (-1.25f, 2.15f, 0.42f), (1.25f, 2.15f, 0.42f), (-0.35f, 2.15f, 0.48f), (0.35f, 2.15f, 0.5f));
        }

        static void Birch(List<Part> parts)
        {
            parts.Add(Trunk(0.16f, 0.085f, 0.055f, 2.6f, 3, birch: true));
            Clumps(parts, (0.05f, 3.25f, 0.38f), (-0.3f, 2.95f, 0.36f), (0.36f, 2.9f, 0.34f), (-0.1f, 2.6f, 0.4f), (0.3f, 2.45f, 0.3f));
        }

        static void Pine(List<Part> parts)
        {
            parts.Add(Trunk(0.24f, 0.11f, 0.05f, 3.2f, 4));
            // tiers from the bottom up: each higher tier is cut over the one below it
            const int tiers = 5;
            for (int i = 0; i < tiers; i++)
            {
                float t = i / (float)(tiers - 1);
                float y0 = 0.7f + i * 0.52f, w = M.Lerp(1.0f, 0.38f, t), h = M.Lerp(0.95f, 0.75f, t);
                var pts = new List<Vector2> { V(0, y0 + h) };
                // a ragged hem: the branch tips droop
                const int teeth = 6;
                for (int k = 0; k <= teeth; k++)
                {
                    float u = k / (float)teeth * 2 - 1;
                    pts.Add(V(w * (1 - k / (float)teeth * 2) * 1, y0 - (k % 2 == 0 ? 0.08f : 0) + M.Abs(u) * 0.12f));
                }
                var poly = Poly(pts.ToArray());
                parts.Add(new Part("tier", (x, y) => Sdf.Polygon(x, y, poly)));
            }
        }

        /// <summary>A lumpy boulder; ore shows as veins and spots cut into it.</summary>
        static void Boulder(List<Part> parts, float size, int seed, bool ore)
        {
            var pts = new List<Vector2>();
            const int n = 11;
            for (int i = 0; i < n; i++)
            {
                float a = M.Pi * i / (n - 1);                       // over the top, from the right foot to the left
                float r = size * (0.85f + 0.3f * M.Hash(i, seed));
                pts.Add(V(M.Cos(a) * r * 1.25f, M.Sin(a) * r * 0.8f));
            }
            var poly = Poly(pts.ToArray());
            // cracks, and the ore's veins with a few nuggets on them
            var cracks = new List<(Vector2 a, Vector2 b)>
            {
                (V(-0.35f * size, 0.05f), V(-0.15f * size, 0.45f * size)), (V(-0.15f * size, 0.45f * size), V(0.05f * size, 0.55f * size)),
                (V(0.5f * size, 0.1f), V(0.62f * size, 0.38f * size)),
            };
            var veins = new List<Vector2[]>();
            var nuggets = new List<(Vector2 c, float r)>();
            if (ore)
            {
                for (int v = 0; v < 2; v++)
                {
                    var line = new Vector2[5];
                    for (int k = 0; k < 5; k++)
                        line[k] = V((-0.7f + k * 0.32f) * size, (0.25f + v * 0.28f + 0.08f * (M.Hash(k, seed + v * 7) - 0.5f) * 2) * size);
                    veins.Add(line);
                    for (int k = 1; k < 5; k += 2) nuggets.Add((line[k], (0.06f + 0.03f * M.Hash(k, seed + 3)) * size));
                }
            }
            parts.Add(new Part("rock", (x, y) => Sdf.Polygon(x, y, poly) - 0.03f, (x, y) =>
            {
                float d = float.MaxValue;
                foreach (var (a, b) in cracks) d = M.Min(d, Sdf.Segment(x, y, a.X, a.Y, b.X, b.Y));
                foreach (var line in veins)
                    for (int k = 0; k + 1 < line.Length; k++) d = M.Min(d, Sdf.Segment(x, y, line[k].X, line[k].Y, line[k + 1].X, line[k + 1].Y));
                foreach (var (c, r) in nuggets) d = M.Min(d, M.Abs(Sdf.Circle(x - c.X, y - c.Y, r)));
                return d;
            }));
        }

        /// <summary>A muddy scrap pile: a low mound with bits of old iron sticking out.</summary>
        static void ScrapPile(List<Part> parts)
        {
            var bits = new List<float[]>();
            for (int i = 0; i < 5; i++)
            {
                float x0 = -0.7f + i * 0.35f, a = (M.Hash(i, 11) - 0.5f) * 1.6f;
                var c = V(x0, 0.45f + 0.15f * M.Hash(i, 12));
                var u = V(M.Sin(a), M.Cos(a)); var p = u.Perp;
                bits.Add(Poly(c - p * 0.04f, c + u * 0.32f - p * 0.03f, c + u * 0.34f + p * 0.04f, c + p * 0.05f));
            }
            parts.Add(new Part("scrap", (x, y) => bits.Min(b => Sdf.Polygon(x, y, b))));
            parts.Add(new Part("mound", (x, y) => M.Max(M.Len(x / 1.25f, y / 0.6f) - 1, -y) * 0.6f,
                (x, y) => M.Min(M.Abs(M.Len(x / 0.9f, (y + 0.05f) / 0.4f) - 1) * 0.4f, M.Abs(M.Len((x + 0.3f) / 0.5f, (y + 0.02f) / 0.22f) - 1) * 0.22f)));
        }

        /// <summary>Obsidian: a cluster of sharp crystals.</summary>
        static void Crystals(List<Part> parts)
        {
            (float x, float h, float a)[] cs = { (-0.45f, 0.9f, 18), (0.4f, 1.0f, -15), (0, 1.5f, 3), (-0.2f, 0.7f, 30), (0.25f, 0.65f, -32) };
            foreach (var (cx, h, a) in cs)
            {
                var u = new Vector2(0, 1).Rotate(a); var p = u.Perp;
                var b = V(cx, 0);
                var poly = Poly(b - p * 0.16f, b + u * h * 0.8f - p * 0.15f, b + u * h, b + u * h * 0.8f + p * 0.15f, b + p * 0.16f);
                var ridge0 = b; var ridge1 = b + u * h;
                parts.Add(new Part("crystal", (x, y) => M.Max(Sdf.Polygon(x, y, poly), -y),
                    (x, y) => Sdf.Segment(x, y, ridge0.X, ridge0.Y, ridge1.X, ridge1.Y) + (y < 0.05f ? 1 : 0)));
            }
        }

        // ---------------------------------------------------------------- crafting stations

        static Part Block(string name, float cx, float cy, float hx, float hy, Func<float, float, float> lines = null) =>
            new Part(name, (x, y) => Sdf.Box(x - cx, y - cy, hx, hy) - 0.01f, lines);

        /// <summary>A plank table: top and two legs with a stretcher.</summary>
        static void Table(List<Part> parts, float halfW, float top)
        {
            parts.Add(new Part("legs", (x, y) => M.Min(M.Min(Sdf.Box(x + halfW * 0.82f, y - top / 2, 0.05f, top / 2), Sdf.Box(x - halfW * 0.82f, y - top / 2, 0.05f, top / 2)),
                                                       Sdf.Box(x, y - top * 0.3f, halfW * 0.82f, 0.035f))));
            parts.Add(Block("top", 0, top, halfW, 0.06f, (x, y) => M.Abs(y - top)));   // the join between two planks
        }

        static void Workbench(List<Part> parts)
        {
            // a tool board behind the bench, with a saw and an axe cut into it
            var saw = Geo.Poly(new[] { V(-0.45f, 1.2f), V(-0.05f, 1.32f), V(-0.05f, 1.22f), V(-0.45f, 1.12f) });
            parts.Add(Block("board", 0, 1.25f, 0.6f, 0.3f, (x, y) =>
            {
                float d = M.Abs(Sdf.Polygon(x, y, saw));
                d = M.Min(d, Sdf.Segment(x, y, 0.25f, 1.05f, 0.25f, 1.45f));
                d = M.Min(d, M.Abs(Sdf.Polygon(x, y, Geo.Poly(new[] { V(0.25f, 1.45f), V(0.42f, 1.47f), V(0.44f, 1.33f), V(0.25f, 1.36f) }))));
                return d;
            }));
            Table(parts, 0.75f, 0.85f);
            parts.Add(Block("vise", -0.55f, 0.98f, 0.1f, 0.08f));
        }

        static void Forge(List<Part> parts, bool black)
        {
            float w = black ? 0.62f : 0.55f, h = black ? 1.05f : 0.9f;
            // the chimney and hood behind, then the hearth with its stones and fire mouth, then the anvil
            float stackTop = black ? 2.7f : 2.2f;
            parts.Add(Block("chimney", 0, (h + stackTop) / 2, black ? 0.16f : 0.13f, (stackTop - h) / 2));
            var hood = Geo.Poly(new[] { V(-w, h), V(-0.18f, h + 0.55f), V(0.18f, h + 0.55f), V(w, h) });
            parts.Add(new Part("hood", (x, y) => Sdf.Polygon(x, y, hood)));
            if (black)
            {
                // a dvergr kiln: a rounded dome, ringed with bands
                parts.Add(new Part("kiln", (x, y) => M.Max(M.Len(x / w, (y - 0.0f) / h) - 1, -y) * M.Min(w, h),
                    (x, y) => M.Min(M.Abs(Sdf.Circle(x, y - 0.38f, 0.22f)), M.Min(M.Abs(y - 0.75f), M.Abs(y - 0.25f)))));
            }
            else
            {
                parts.Add(Block("hearth", 0, h / 2, w, h / 2, (x, y) =>
                {
                    // courses of stone, and the arched fire mouth
                    float row = M.Abs(M.Wrap(y, 0.22f) - 0.11f) - 0.105f;
                    float joint = M.Abs(M.Wrap(x + (M.Floor(y / 0.22f) % 2) * 0.15f, 0.3f) - 0.15f) - 0.145f;
                    float mouth = M.Abs(M.Min(Sdf.Box(x, y - 0.3f, 0.2f, 0.15f), Sdf.Circle(x, y - 0.45f, 0.2f)));
                    float stones = M.Max(M.Min(M.Abs(row), M.Abs(joint)), -(M.Min(Sdf.Box(x, y - 0.3f, 0.2f, 0.15f), Sdf.Circle(x, y - 0.45f, 0.2f)) - 0.03f));
                    return M.Min(stones, mouth);
                }));
            }
            var anvil = Geo.Poly(new[] { V(0.62f, 0), V(1.08f, 0), V(1.0f, 0.12f), V(0.92f, 0.12f), V(0.92f, 0.38f), V(1.12f, 0.42f),
                                         V(1.16f, 0.52f), V(0.72f, 0.52f), V(0.52f, 0.47f), V(0.72f, 0.40f), V(0.78f, 0.38f), V(0.78f, 0.12f), V(0.7f, 0.12f) });
            parts.Add(new Part("anvil", (x, y) => Sdf.Polygon(x, y, anvil)));
        }

        static void Cauldron(List<Part> parts)
        {
            parts.Add(new Part("legs", (x, y) => M.Min(M.Min(Sdf.Segment(x, y, -0.35f, 0, -0.25f, 0.3f), Sdf.Segment(x, y, 0.35f, 0, 0.25f, 0.3f)),
                                                       Sdf.Segment(x, y, 0, 0, 0, 0.2f)) - 0.035f));
            parts.Add(new Part("pot", (x, y) => M.Max(M.Len(x / 0.52f, (y - 0.5f) / 0.4f) - 1, y - 0.78f) * 0.4f,
                (x, y) => M.Abs(y - 0.5f) + (M.Abs(x) > 0.48f ? 1 : 0)));
            parts.Add(Block("rim", 0, 0.8f, 0.5f, 0.05f));
            parts.Add(new Part("handles", (x, y) => M.Min(M.Abs(Sdf.Circle(x + 0.55f, y - 0.62f, 0.08f)), M.Abs(Sdf.Circle(x - 0.55f, y - 0.62f, 0.08f))) - 0.022f));
        }

        static void Stonecutter(List<Part> parts)
        {
            parts.Add(Block("far post", -0.08f, 0.55f, 0.06f, 0.5f));
            // a big grinding wheel on its axle
            parts.Add(new Part("wheel", (x, y) => Sdf.Circle(x, y - 1.0f, 0.62f),
                (x, y) => M.Min(M.Abs(Sdf.Circle(x, y - 1.0f, 0.5f)), M.Abs(Sdf.Circle(x, y - 1.0f, 0.12f)))));
            parts.Add(Block("post", 0.08f, 0.55f, 0.06f, 0.5f));
            parts.Add(Block("base", 0, 0.1f, 0.7f, 0.1f));
            parts.Add(Block("block", 0.75f, 0.2f, 0.18f, 0.2f, (x, y) => Sdf.Segment(x, y, 0.62f, 0.25f, 0.85f, 0.12f)));
        }

        static void ArtisanTable(List<Part> parts)
        {
            // a spinning wheel on a post beside the bench
            parts.Add(Block("wheel post", 0.85f, 0.75f, 0.05f, 0.75f));
            parts.Add(new Part("wheel", (x, y) => Sdf.Circle(x - 0.85f, y - 1.45f, 0.48f), (x, y) =>
            {
                float lx = x - 0.85f, ly = y - 1.45f, d = M.Abs(Sdf.Circle(lx, ly, 0.38f));
                for (int k = 0; k < 4; k++) { float a = k * M.Pi / 4; d = M.Min(d, Sdf.Segment(lx, ly, -M.Cos(a) * 0.38f, -M.Sin(a) * 0.38f, M.Cos(a) * 0.38f, M.Sin(a) * 0.38f)); }
                return d;
            }));
            Table(parts, 0.7f, 0.85f);
            parts.Add(new Part("tools", (x, y) => M.Min(Sdf.Circle(x + 0.35f, y - 1.0f, 0.1f), Sdf.Box(x - 0.1f, y - 0.96f, 0.16f, 0.05f))));
        }

        static void GaldrTable(List<Part> parts)
        {
            Table(parts, 0.6f, 0.85f);
            // a floating rune ring with an orb inside
            parts.Add(new Part("ring", (x, y) => M.Abs(Sdf.Circle(x, y - 1.6f, 0.38f)) - 0.06f,
                (x, y) =>
                {
                    float a = (float)Math.Atan2(y - 1.6f, x);
                    float tick = M.Abs(M.Wrap(a + M.Pi, M.Pi / 6) - M.Pi / 12) * 0.38f;
                    return M.Max(tick, M.Abs(Sdf.Circle(x, y - 1.6f, 0.38f)) - 0.035f);
                }));
            parts.Add(new Part("orb", (x, y) => Sdf.Circle(x, y - 1.6f, 0.14f), (x, y) => M.Abs(Sdf.Circle(x, y - 1.6f, 0.07f))));
            parts.Add(Block("candle", -0.4f, 1.0f, 0.04f, 0.1f));
        }

        // ---------------------------------------------------------------- trophies and altars

        static readonly HashSet<string> HeadParts = new HashSet<string>
            { "head", "face", "ear", "antler", "tusk", "trunk", "horn", "far horn", "helmet", "cap", "hood", "beard", "mane" };

        /// <summary>A creature's trophy: its head, cut from the whole figure (the whole figure if it has no head).</summary>
        public static List<Part> Trophy(FigureType of)
        {
            var all = FigureShape.BuildParts(of, PoseLibrary.Resolve(of.Rig, "standing", null), null);
            var head = all.Where(p => HeadParts.Contains(p.Name)).ToList();
            return head.Count > 0 ? head : all;
        }

        /// <summary>The sacrificial stone a trophy is offered at: a standing slab on a plinth, the creature's head cut into its face.</summary>
        public static List<Part> Altar(FigureType of)
        {
            var emblem = new FigureShape(Trophy(of));
            const float W = 0.6f, Top = 1.75f;
            var slab = new List<Vector2> { V(W, 0.15f) };
            for (int i = 0; i <= 10; i++)
            {
                float a = M.Pi * i / 10;
                slab.Add(V(M.Cos(a) * W * 0.92f, Top - W * 0.5f + M.Sin(a) * W * 0.5f));
            }
            slab.Add(V(-W, 0.15f));
            var poly = Poly(slab.ToArray());
            // the emblem fills most of the face, above the middle
            float fit = M.Min(W * 1.4f / (2 * emblem.HalfWidth), 0.8f / (2 * emblem.HalfHeight));
            var ec = V(0, Top - 0.75f);
            var parts = new List<Part>
            {
                new Part("slab", (x, y) => Sdf.Polygon(x, y, poly), (x, y) =>
                {
                    float lx = (x - ec.X) / fit, ly = (y - ec.Y) / fit;
                    float d = M.Min(M.Abs(emblem.Distance(lx, ly)), emblem.CutDistance(lx, ly)) * fit;
                    // a rune line framing the face
                    float frame = M.Abs(Sdf.Polygon(x, y, poly) + 0.07f);
                    return M.Min(d, frame + (y < 0.3f ? 1 : 0));
                }),
                new Part("plinth", (x, y) => Sdf.Box(x, y - 0.1f, W + 0.2f, 0.1f) - 0.02f),
            };
            Geo.Ground(parts, 0, 1);
            return parts;
        }
    }
}
