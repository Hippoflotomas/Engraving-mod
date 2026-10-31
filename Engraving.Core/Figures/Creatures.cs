using System;
using System.Collections.Generic;
using System.Linq;
using Engraving.Core.Geometry;

namespace Engraving.Core.Figures
{
    /// <summary>Bodies for one rig, read from built-in JSON ({ "bodies": [ ... ] }), looked up by name or word.</summary>
    public sealed class BodyLibrary
    {
        readonly Dictionary<string, Species> _byWord = new Dictionary<string, Species>(StringComparer.OrdinalIgnoreCase);
        public readonly List<Species> All = new List<Species>();

        public BodyLibrary(string json)
        {
            var root = (Dictionary<string, object>)MiniJson.Parse(json);
            foreach (Dictionary<string, object> d in (List<object>)root["bodies"])
            {
                var b = SpeciesLibrary.Read(d);
                All.Add(b);
                _byWord[b.Name] = b;
                foreach (var w in b.Words) _byWord[w] = b;
            }
        }

        public Species Find(string word) => word != null && _byWord.TryGetValue(word, out var s) ? s : null;
    }

    static class Geo
    {
        public static Vector2 Ahead(float deg) { float a = deg * M.Pi / 180f; return new Vector2(M.Cos(a), M.Sin(a)); }
        public static float[] Poly(IEnumerable<Vector2> pts) => pts.SelectMany(p => new[] { p.X, p.Y }).ToArray();

        /// <summary>Stands parts on y = 0 (or hovers them <paramref name="hover"/> above it), scaled by <paramref name="scale"/>.</summary>
        public static void Ground(List<Part> parts, float hover, float scale)
        {
            float low = float.MaxValue;
            for (float y = -4f; y < 4f && low == float.MaxValue; y += 0.005f)
                for (float x = -4f; x < 4f; x += 0.01f)
                    if (parts.Any(p => p.Distance(x, y) < 0)) { low = y; break; }
            var shift = new Vector2(0, (low == float.MaxValue ? 0 : -low) + hover);
            foreach (var p in parts) { p.Offset = shift; p.Scale = scale; }
        }

        /// <summary>A chain of tapered capsules through points.</summary>
        public static float Chain(float x, float y, IList<Vector2> pts, float r0, float r1)
        {
            float d = float.MaxValue;
            int n = pts.Count - 1;
            for (int i = 0; i < n; i++) d = M.Min(d, Sdf2.Capsule(x, y, pts[i], pts[i + 1], M.Lerp(r0, r1, i / (float)n), M.Lerp(r0, r1, (i + 1) / (float)n)));
            return d;
        }
    }

    // =====================================================================================================
    /// <summary>
    /// The winged rig: birds, bats, drakes, the dragon, and winged insects. A body with a neck and head (beak, snout
    /// or proboscis), a tail (feather fan, long and spiked, or none), two or four legs, and two wings (feathered,
    /// membrane on finger bones, or thin insect wings), raised or folded by the pose.
    /// </summary>
    public static class Flyer
    {
        public const string BuiltinBodies = @"{ ""bodies"": [
  { ""name"": ""raven"", ""words"": [""ravens"", ""hugin"", ""munin"", ""crow"", ""crows"", ""bird"", ""birds""],
    ""body"": 0.28, ""bodyR"": 0.085, ""neck"": 0.06, ""head"": 0.06, ""beak"": 0.09, ""head.style"": ""beak"",
    ""wing"": 0.55, ""wing.style"": ""feather"", ""tail"": 0.18, ""tail.style"": ""fan"", ""legs"": 2, ""leg"": 0.12 },
  { ""name"": ""vulture"", ""words"": [""vultures"", ""volture"", ""voltures""], ""scale"": 1.4,
    ""body"": 0.4, ""bodyR"": 0.12, ""neck"": 0.18, ""head"": 0.06, ""beak"": 0.1, ""head.style"": ""beak"", ""ruff"": 1,
    ""wing"": 0.8, ""wing.style"": ""feather"", ""tail"": 0.2, ""tail.style"": ""fan"", ""legs"": 2, ""leg"": 0.18 },
  { ""name"": ""chicken"", ""words"": [""chickens"", ""hen"", ""hens"", ""rooster"", ""roosters""],
    ""body"": 0.3, ""bodyR"": 0.13, ""neck"": 0.12, ""head"": 0.06, ""beak"": 0.05, ""head.style"": ""beak"", ""comb"": 1,
    ""wing"": 0.22, ""wing.style"": ""feather"", ""tail"": 0.16, ""tail.style"": ""fan"", ""legs"": 2, ""leg"": 0.16, ""grounded"": 1 },
  { ""name"": ""bat"", ""words"": [""bats""],
    ""body"": 0.16, ""bodyR"": 0.06, ""neck"": 0.02, ""head"": 0.055, ""beak"": 0.03, ""head.style"": ""bat"",
    ""wing"": 0.42, ""wing.style"": ""membrane"", ""tail"": 0, ""legs"": 2, ""leg"": 0.06 },
  { ""name"": ""drake"", ""words"": [""drakes"", ""hatchling"", ""hatchlings""], ""scale"": 1.2,
    ""body"": 0.55, ""bodyR"": 0.12, ""neck"": 0.35, ""head"": 0.08, ""beak"": 0.16, ""head.style"": ""dragon"",
    ""wing"": 0.85, ""wing.style"": ""membrane"", ""tail"": 0.9, ""tail.style"": ""dragon"", ""legs"": 2, ""leg"": 0.22 },
  { ""name"": ""dragon"", ""words"": [""dragons"", ""moder"", ""fader""], ""scale"": 4,
    ""body"": 0.7, ""bodyR"": 0.17, ""neck"": 0.55, ""head"": 0.1, ""beak"": 0.22, ""head.style"": ""dragon"", ""spikes"": 1,
    ""wing"": 1.2, ""wing.style"": ""membrane"", ""tail"": 1.3, ""tail.style"": ""dragon"", ""legs"": 4, ""leg"": 0.32 },
  { ""name"": ""deathsquito"", ""words"": [""deathsquitos"", ""deathsquitoes"", ""mosquito"", ""mosquitos"", ""mosquitoes""],
    ""body"": 0.3, ""bodyR"": 0.06, ""neck"": 0.0, ""head"": 0.045, ""beak"": 0.22, ""head.style"": ""proboscis"",
    ""wing"": 0.32, ""wing.style"": ""insect"", ""tail"": 0, ""legs"": 2, ""leg"": 0.22, ""abdomen"": 1 },
  { ""name"": ""seeker"", ""words"": [""seekers""], ""scale"": 1.3,
    ""body"": 0.4, ""bodyR"": 0.1, ""neck"": 0.0, ""head"": 0.08, ""beak"": 0.1, ""head.style"": ""mandibles"",
    ""wing"": 0.6, ""wing.style"": ""insect"", ""tail"": 0, ""legs"": 2, ""leg"": 0.35, ""abdomen"": 1 }
] }";

        public static readonly BodyLibrary Bodies = new BodyLibrary(BuiltinBodies);

        public static List<Part> Build(Species s, Pose pose)
        {
            float G(string k, float f) => s.Get(k, f);
            float P(string k, float f) => pose.Get(k, f);
            float len = G("body", 0.3f), r = G("bodyR", 0.08f);
            float tilt = P("body", 0);
            if (G("grounded", 0) > 0.5f) tilt = M.Clamp(tilt, -15, 25);
            var spine = Geo.Ahead(tilt);
            var up = spine.Perp;
            var tailEnd = new Vector2(0, 0);
            var chest = tailEnd + spine * len;
            var parts = new List<Part>();

            // wings: far one first, then legs and tail, body, head, near wing on top
            float wingLen = G("wing", 0.5f);
            string wingStyle = s.Str("wing.style", "feather");
            float wingUp = P("wing", 30), fold = M.Clamp(P("wingFold", 0), 0, 1);
            var wingRoot = chest - spine * (len * 0.25f) + up * (r * 0.6f);
            parts.Add(Wing("far wing", wingRoot + new Vector2(-0.02f, 0.015f), tilt, wingUp + 12, fold, wingLen * 0.95f, wingStyle));

            // legs: hanging or standing
            int legs = (int)G("legs", 2);
            float legLen = G("leg", 0.15f), legDeg = P("legs", 10);
            for (int i = 0; i < legs; i += 2)
            {
                var hip = i == 0 ? tailEnd + spine * (len * 0.3f) - up * (r * 0.7f) : chest - spine * (len * 0.1f) - up * (r * 0.7f);
                parts.Add(BirdLeg("far leg", hip + new Vector2(-0.015f, 0.01f), legDeg - 8, legLen, r));
            }

            // tail
            float tail = G("tail", 0);
            string tailStyle = s.Str("tail.style", "fan");
            if (tail > 0)
            {
                if (tailStyle == "fan")
                {
                    var a = tailEnd + up * (r * 0.3f); var t1 = a + Geo.Ahead(180 + tilt + 15) * tail; var t2 = a + Geo.Ahead(180 + tilt - 10) * tail;
                    var poly = Geo.Poly(new[] { a + up * (r * 0.4f), t1, t2, a - up * (r * 0.4f) });
                    parts.Add(new Part("tail", (x, y) => Sdf.Polygon(x, y, poly) - 0.008f,
                        (x, y) => Sdf.Segment(x, y, a.X, a.Y, (t1 + t2).X * 0.5f, (t1 + t2).Y * 0.5f)));
                }
                else
                {
                    var pts = new List<Vector2> { tailEnd };
                    float a = 180 + tilt + P("tail", 0);
                    for (int i = 0; i < 6; i++) { pts.Add(pts[pts.Count - 1] + Geo.Ahead(a) * (tail / 6)); a += P("tailCurl", 8); }
                    var tip = pts[pts.Count - 1];
                    var spade = Geo.Poly(new[] { tip + Geo.Ahead(a + 90) * (r * 0.5f), tip + Geo.Ahead(a) * (r * 1.4f), tip + Geo.Ahead(a - 90) * (r * 0.5f) });
                    parts.Add(new Part("tail", (x, y) => M.Min(Geo.Chain(x, y, pts, r * 0.7f, r * 0.12f), Sdf.Polygon(x, y, spade))));
                }
            }

            // body (an abdomen behind for insects)
            bool abdomen = G("abdomen", 0) > 0.5f;
            var spikes = new List<(Vector2, Vector2)>();
            if (G("spikes", 0) > 0.5f)
                for (int i = 0; i < 6; i++) { var b = Vector2.Lerp(tailEnd, chest, i / 6f) + up * (r * 0.8f); spikes.Add((b, b + up * (r * 0.6f) - spine * (r * 0.3f))); }
            parts.Add(new Part("body", (x, y) =>
            {
                float d = abdomen ? M.Min(Sdf2.Capsule(x, y, tailEnd - spine * (len * 0.35f), chest - spine * (len * 0.4f), r * 0.55f, r * 1.05f),
                                          Sdf.Circle(x - chest.X, y - chest.Y, r * 0.9f))
                                  : Sdf2.Capsule(x, y, tailEnd, chest, r * 0.75f, r);
                foreach (var (a, b) in spikes) d = M.Min(d, Sdf2.Capsule(x, y, a, b, r * 0.18f, 0.004f));
                return d;
            }, abdomen ? (Func<float, float, float>)((x, y) =>
            {
                float best = float.MaxValue;
                for (int i = 1; i <= 4; i++)
                {
                    var c = Vector2.Lerp(tailEnd - spine * (len * 0.3f), chest - spine * (len * 0.45f), i / 5f);
                    float rr = M.Lerp(r * 0.55f, r * 1.05f, i / 5f);
                    best = M.Min(best, Sdf.Segment(x, y, (c + up * rr).X, (c + up * rr).Y, (c - up * rr).X, (c - up * rr).Y));
                }
                return best;
            }) : null));

            // neck and head
            float neck = G("neck", 0.1f), headR = G("head", 0.06f), beak = G("beak", 0.08f);
            var headC = chest + Geo.Ahead(tilt + P("neck", 40)) * (neck + r * 0.6f);
            var face = Geo.Ahead(tilt + P("head", 0));
            var faceUp = face.Perp;
            string headStyle = s.Str("head.style", "beak");
            if (neck > 0.01f) parts.Add(new Part("neck", (x, y) => Sdf2.Capsule(x, y, chest, headC, r * 0.6f, headR * 0.8f)));
            parts.Add(Head(headC, face, faceUp, headR, beak, headStyle, G("comb", 0) > 0.5f, G("ruff", 0) > 0.5f));

            // near legs and wing
            for (int i = 0; i < legs; i += 2)
            {
                var hip = i == 0 ? tailEnd + spine * (len * 0.3f) - up * (r * 0.7f) : chest - spine * (len * 0.1f) - up * (r * 0.7f);
                parts.Add(BirdLeg("leg", hip, legDeg + 8, legLen, r));
            }
            parts.Add(Wing("wing", wingRoot, tilt, wingUp, fold, wingLen, wingStyle));

            Geo.Ground(parts, G("grounded", 0) > 0.5f || P("perched", 0) > 0.5f ? 0 : 0.15f, G("scale", 1));
            return parts;
        }

        static Part BirdLeg(string name, Vector2 hip, float deg, float len, float r)
        {
            var knee = hip + new Vector2(M.Sin((deg) * M.Pi / 180), -M.Cos(deg * M.Pi / 180)) * (len * 0.5f);
            var foot = knee + new Vector2(M.Sin((deg - 30) * M.Pi / 180), -M.Cos((deg - 30) * M.Pi / 180)) * (len * 0.5f);
            var toeA = foot + new Vector2(len * 0.22f, -0.005f); var toeB = foot + new Vector2(-len * 0.12f, -0.005f);
            float w = M.Max(0.008f, r * 0.14f);
            return new Part(name, (x, y) => M.Min(M.Min(Sdf2.Capsule(x, y, hip, knee, w * 1.6f, w), Sdf2.Capsule(x, y, knee, foot, w, w * 0.8f)),
                                                  M.Min(Sdf2.Capsule(x, y, foot, toeA, w * 0.8f, w * 0.5f), Sdf2.Capsule(x, y, foot, toeB, w * 0.8f, w * 0.5f))));
        }

        /// <summary>A wing from the shoulder: raised by <paramref name="upDeg"/> above straight back, folded towards the body by <paramref name="fold"/>.</summary>
        static Part Wing(string name, Vector2 root, float tilt, float upDeg, float fold, float len, string style)
        {
            float spread = M.Lerp(1f, 0.45f, fold);
            float a = 180 + tilt - upDeg;   // pointing back and up
            var elbow = root + Geo.Ahead(a + 25 * (1 - fold)) * (len * 0.4f * spread);
            var tip = elbow + Geo.Ahead(a - 10 + 20 * fold) * (len * 0.6f * spread);
            var trailing = new List<Vector2>();
            if (style == "insect")
            {
                var c = Vector2.Lerp(root, tip, 0.5f);
                var along = (tip - root).Normalized;
                float l = (tip - root).Length / 2, w = l * 0.32f;
                return new Part(name, (x, y) =>
                {
                    float px = (x - c.X) * along.X + (y - c.Y) * along.Y, py = -(x - c.X) * along.Y + (y - c.Y) * along.X;
                    return (M.Len(px / l, py / w) - 1) * w;
                }, (x, y) => Sdf.Segment(x, y, root.X, root.Y, tip.X, tip.Y));
            }
            if (style == "membrane")
            {
                // finger bones from the wrist, the skin scalloped between them back to the body
                var fingers = new List<Vector2>();
                // the leading finger along the top edge, the rest fanning back towards the tail, and the skin
                // joining the flank behind the shoulder (2026-10-31: fanned forward, the wings looked backwards)
                for (int i = 0; i < 4; i++) fingers.Add(elbow + Geo.Ahead(a - 10 + 20 * fold + i * 38 * spread) * (len * (0.62f - i * 0.07f) * spread));
                var body = root + Geo.Ahead(a + 95) * (len * 0.25f);
                var outline = new List<Vector2> { root, elbow };
                foreach (var f in fingers)
                {
                    outline.Add(f);
                }
                outline.Add(body);
                var poly = Geo.Poly(outline);
                return new Part(name, (x, y) =>
                {
                    float d = Sdf.Polygon(x, y, poly);
                    // scallops: bite circles out between finger tips
                    for (int i = 0; i + 1 < fingers.Count; i++)
                    {
                        var mid = Vector2.Lerp(fingers[i], fingers[i + 1], 0.5f);
                        var inward = (Vector2.Lerp(elbow, body, 0.3f) - mid).Normalized;
                        var c = mid - inward * ((fingers[i] - fingers[i + 1]).Length * 0.4f);
                        d = M.Max(d, -(Sdf.Circle(x - c.X, y - c.Y, (fingers[i] - fingers[i + 1]).Length * 0.55f)));
                    }
                    return d;
                }, (x, y) =>
                {
                    float d = Sdf.Segment(x, y, root.X, root.Y, elbow.X, elbow.Y);
                    foreach (var f in fingers) d = M.Min(d, Sdf.Segment(x, y, elbow.X, elbow.Y, f.X, f.Y));
                    return d;
                });
            }
            // feathered: a broad wing with long flight feathers cut along its trailing edge
            var back = root + Geo.Ahead(a - 95) * (len * 0.22f);
            var feathers = new List<(Vector2, Vector2)>();
            var featherTips = new List<Vector2>();
            for (int i = 0; i <= 6; i++)
            {
                var b = Vector2.Lerp(tip, back, i / 6f);
                var tipF = b + Geo.Ahead(a - 70 - i * 10) * (len * 0.22f * (1 - i / 9f));
                featherTips.Add(tipF);
                feathers.Add((Vector2.Lerp(elbow, b, 0.7f), tipF));
            }
            var outline2 = new List<Vector2> { root, elbow, tip };
            outline2.AddRange(featherTips);
            outline2.Add(back);
            var poly2 = Geo.Poly(outline2);
            return new Part(name, (x, y) => Sdf.Polygon(x, y, poly2) - 0.006f,
                (x, y) => feathers.Min(f => Sdf.Segment(x, y, f.Item1.X, f.Item1.Y, f.Item2.X, f.Item2.Y)));
        }

        static Part Head(Vector2 c, Vector2 face, Vector2 faceUp, float r, float beak, string style, bool comb, bool ruff)
        {
            var eye = c + face * (r * 0.3f) + faceUp * (r * 0.25f);
            Func<float, float, float> eyeCut = (x, y) => M.Abs(Sdf.Circle(x - eye.X, y - eye.Y, r * 0.18f));
            switch (style)
            {
                case "bat":
                {
                    var ear = Geo.Poly(new[] { c + faceUp * (r * 0.6f), c + faceUp * (r * 2.0f) - face * (r * 0.4f), c - face * (r * 0.6f) + faceUp * (r * 0.4f) });
                    var nose = c + face * (r + beak);
                    return new Part("head", (x, y) => M.Min(M.Min(Sdf.Circle(x - c.X, y - c.Y, r), Sdf2.Capsule(x, y, c, nose, r * 0.7f, r * 0.35f)), Sdf.Polygon(x, y, ear)), eyeCut);
                }
                case "dragon":
                {
                    var nose = c + face * (r + beak);
                    var jawA = c + face * (r * 0.2f) - faceUp * (r * 0.45f); var jawB = nose - faceUp * (r * 0.3f);
                    var horn1 = new[] { c - face * (r * 0.3f) + faceUp * (r * 0.5f), c - face * (r * 2.4f) + faceUp * (r * 1.4f), c - face * (r * 0.9f) + faceUp * (r * 0.1f) };
                    var hp = Geo.Poly(horn1);
                    return new Part("head", (x, y) => M.Min(M.Min(Sdf.Circle(x - c.X, y - c.Y, r), Sdf2.Capsule(x, y, c, nose, r * 0.8f, r * 0.45f)), Sdf.Polygon(x, y, hp)),
                        (x, y) => M.Min(eyeCut(x, y), Sdf.Segment(x, y, jawA.X, jawA.Y, jawB.X, jawB.Y)));
                }
                case "proboscis":
                {
                    var tip = c + face * (r + beak) - faceUp * (beak * 0.25f);
                    return new Part("head", (x, y) => M.Min(Sdf.Circle(x - c.X, y - c.Y, r), Sdf2.Capsule(x, y, c, tip, r * 0.25f, 0.004f)), eyeCut);
                }
                case "mandibles":
                {
                    var m1 = c + face * (r + beak) + faceUp * (r * 0.25f); var m2 = c + face * (r + beak) - faceUp * (r * 0.45f);
                    var ant = c + faceUp * (r * 2.2f) + face * (r * 1.2f);
                    return new Part("head", (x, y) => M.Min(M.Min(Sdf.Circle(x - c.X, y - c.Y, r), Sdf2.Capsule(x, y, c, m1, r * 0.3f, r * 0.12f)),
                                                         M.Min(Sdf2.Capsule(x, y, c, m2, r * 0.3f, r * 0.12f), Sdf2.Capsule(x, y, c, ant, r * 0.1f, 0.004f))), eyeCut);
                }
                default:
                {
                    // a bird: round head, a pointed beak, maybe a comb (chicken) or a bare head on a ruffed neck (vulture)
                    var tip = c + face * (r + beak) - faceUp * (beak * 0.15f);
                    var bk = Geo.Poly(new[] { c + face * (r * 0.6f) + faceUp * (r * 0.35f), tip, c + face * (r * 0.6f) - faceUp * (r * 0.4f) });
                    var combPts = comb ? Geo.Poly(new[] { c + faceUp * (r * 0.7f) - face * (r * 0.5f), c + faceUp * (r * 1.5f) - face * (r * 0.2f), c + faceUp * (r * 1.2f) + face * (r * 0.2f),
                                                          c + faceUp * (r * 1.4f) + face * (r * 0.5f), c + faceUp * (r * 0.7f) + face * (r * 0.5f) }) : null;
                    var ruffC = c - face * (r * 1.4f) - faceUp * (r * 1.3f);
                    return new Part("head", (x, y) =>
                    {
                        float d = M.Min(Sdf.Circle(x - c.X, y - c.Y, r), Sdf.Polygon(x, y, bk));
                        if (combPts != null) d = M.Min(d, Sdf.Polygon(x, y, combPts));
                        if (ruff) d = M.Min(d, Sdf.Circle(x - ruffC.X, y - ruffC.Y, r * 1.3f));
                        return d;
                    }, eyeCut);
                }
            }
        }
    }

    // =====================================================================================================
    /// <summary>
    /// The serpent rig: a long body following a wave (or rearing up to strike), tapering from the head to the tail,
    /// with a jawed head and an optional fin or spine ridge along the back.
    /// </summary>
    public static class Serpent
    {
        public const string BuiltinBodies = @"{ ""bodies"": [
  { ""name"": ""serpent"", ""words"": [""serpents"", ""seaserpent"", ""seaserpents"", ""snake"", ""snakes""], ""scale"": 2.5,
    ""length"": 2.2, ""r"": 0.12, ""tail"": 0.02, ""head"": 0.15, ""jaw"": 0.22, ""fin"": 1, ""eye"": 1 },
  { ""name"": ""bonemaw"", ""words"": [""bonemaws"", ""bonemawserpent"", ""bonemawserpents""], ""scale"": 3,
    ""length"": 2.4, ""r"": 0.11, ""tail"": 0.03, ""head"": 0.16, ""jaw"": 0.26, ""ribs"": 1, ""eye"": 1 },
  { ""name"": ""leech"", ""words"": [""leeches""], ""scale"": 1,
    ""length"": 1.0, ""r"": 0.07, ""tail"": 0.03, ""head"": 0.05, ""jaw"": 0.02, ""rings"": 1 }
] }";

        public static readonly BodyLibrary Bodies = new BodyLibrary(BuiltinBodies);

        public static List<Part> Build(Species s, Pose pose)
        {
            float L = s.Get("length", 2f), r = s.Get("r", 0.1f), rt = s.Get("tail", 0.02f), headR = s.Get("head", 0.12f), jaw = s.Get("jaw", 0.2f);
            float amp = pose.Get("wave", 0.12f), waves = pose.Get("waves", 1.5f), rear = pose.Get("rear", 0), mouth = pose.Get("mouth", 10);
            // the spine: from the tail (left) to the head (right), a sine wave; rearing lifts the front third up
            var pts = new List<Vector2>();
            int n = 40;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                float x = t * L, y = amp * M.Sin(t * waves * 2 * M.Pi);
                if (rear > 0 && t > 0.6f) { float u = (t - 0.6f) / 0.4f; y += rear * L * 0.45f * u * u; x -= rear * L * 0.15f * u * u; }
                pts.Add(new Vector2(x, y));
            }
            var head = pts[n];
            var dir = (pts[n] - pts[n - 2]).Normalized;
            float headDeg = (float)(Math.Atan2(dir.Y, dir.X) * 180 / Math.PI) - (rear > 0 ? 25 : 0);
            var face = Geo.Ahead(headDeg); var faceUp = face.Perp;
            var parts = new List<Part>();
            bool fin = s.Get("fin", 0) > 0.5f, ribs = s.Get("ribs", 0) > 0.5f, rings = s.Get("rings", 0) > 0.5f;

            // a fin along the back, behind the body
            if (fin)
            {
                var top = new List<Vector2>();
                for (int i = 4; i <= n - 4; i++)
                {
                    var p = pts[i]; var d = (pts[i + 1] - pts[i - 1]).Normalized.Perp;
                    float rr = M.Lerp(rt, r, i / (float)n);
                    top.Add(p + d * (rr + (i % 3 == 0 ? r * 0.9f : r * 0.45f)));
                }
                var basePts = new List<Vector2>();
                for (int i = n - 4; i >= 4; i--) basePts.Add(pts[i]);
                var poly = Geo.Poly(top.Concat(basePts));
                parts.Add(new Part("fin", (x, y) => Sdf.Polygon(x, y, poly)));
            }
            parts.Add(new Part("body", (x, y) =>
            {
                float d = float.MaxValue;
                for (int i = 0; i < n; i++)
                    d = M.Min(d, Sdf2.Capsule(x, y, pts[i], pts[i + 1], M.Lerp(rt, r, i / (float)n), M.Lerp(rt, r, (i + 1) / (float)n)));
                return d;
            }, (ribs || rings) ? (Func<float, float, float>)((x, y) =>
            {
                float best = float.MaxValue;
                for (int i = 3; i < n - 2; i += ribs ? 2 : 3)
                {
                    var p = pts[i]; var d = (pts[i + 1] - pts[i - 1]).Normalized.Perp * (M.Lerp(rt, r, i / (float)n) * 0.85f);
                    best = M.Min(best, Sdf.Segment(x, y, p.X - d.X, p.Y - d.Y, p.X + d.X, p.Y + d.Y));
                }
                return best;
            }) : null));
            // the head: an upper jaw and a lower jaw opened by the pose
            var upperTip = head + Geo.Ahead(headDeg + mouth * 0.4f) * jaw;
            var lowerTip = head + Geo.Ahead(headDeg - mouth) * (jaw * 0.85f);
            var eye = head + face * (headR * 0.3f) + faceUp * (headR * 0.5f);
            bool hasEye = s.Get("eye", 0) > 0.5f;
            parts.Add(new Part("head", (x, y) => M.Min(M.Min(Sdf.Circle(x - head.X, y - head.Y, headR), Sdf2.Capsule(x, y, head, upperTip, headR * 0.8f, headR * 0.3f)),
                                                    Sdf2.Capsule(x, y, head - faceUp * (headR * 0.3f), lowerTip, headR * 0.55f, headR * 0.2f)),
                hasEye ? (Func<float, float, float>)((x, y) => M.Abs(Sdf.Circle(x - eye.X, y - eye.Y, headR * 0.16f))) : null));
            Geo.Ground(parts, 0, s.Get("scale", 1));
            return parts;
        }
    }

    // =====================================================================================================
    /// <summary>
    /// Oozes and growths: a soft mound with eyes, drips and bumps; Bonemass with a skull in its bulk; the Gjall, a
    /// floating sac with tentacles; the abomination, a heap with roots.
    /// </summary>
    public static class Blob
    {
        public const string BuiltinBodies = @"{ ""bodies"": [
  { ""name"": ""blob"", ""words"": [""blobs"", ""ooze"", ""oozes""], ""w"": 0.55, ""h"": 0.45, ""eyes"": 2 },
  { ""name"": ""oozer"", ""words"": [""oozers""], ""scale"": 1.5, ""w"": 0.6, ""h"": 0.55, ""eyes"": 3, ""bumps"": 1 },
  { ""name"": ""growth"", ""words"": [""growths"", ""tar"", ""tarblob""], ""w"": 0.6, ""h"": 0.35, ""eyes"": 1, ""drips"": 1 },
  { ""name"": ""bonemass"", ""words"": [], ""scale"": 4, ""w"": 0.5, ""h"": 0.7, ""eyes"": 0, ""skull"": 1, ""bumps"": 1, ""drips"": 1 },
  { ""name"": ""gjall"", ""words"": [""gjalls""], ""scale"": 3, ""w"": 0.5, ""h"": 0.5, ""eyes"": 0, ""float"": 1, ""tentacles"": 5 },
  { ""name"": ""abomination"", ""words"": [""abominations""], ""scale"": 3.2, ""w"": 0.55, ""h"": 0.4, ""eyes"": 1, ""roots"": 7 }
] }";

        public static readonly BodyLibrary Bodies = new BodyLibrary(BuiltinBodies);

        public static List<Part> Build(Species s, Pose pose)
        {
            float w = s.Get("w", 0.5f), h = s.Get("h", 0.4f);
            float squash = pose.Get("squash", 1), lift = pose.Get("lift", 0);
            h *= squash; w /= M.Sqrt(squash);
            bool floating = s.Get("float", 0) > 0.5f;
            var c = new Vector2(0, floating ? h : h * 0.45f);
            var parts = new List<Part>();
            int roots = (int)s.Get("roots", 0), tentacles = (int)s.Get("tentacles", 0);
            var limbs = new List<List<Vector2>>();
            for (int i = 0; i < roots; i++)
            {
                // roots splaying out and down from the heap, some rising
                float a = 200 + i * (140f / M.Max(1, roots - 1));
                var pts = new List<Vector2> { c };
                var d = Geo.Ahead(a);
                for (int k = 0; k < 4; k++) { pts.Add(pts[pts.Count - 1] + d * (w * 0.45f)); d = Geo.Ahead(a + (i % 2 == 0 ? 25 : -20) * (k + 1)); }
                limbs.Add(pts);
            }
            for (int i = 0; i < tentacles; i++)
            {
                float x0 = M.Lerp(-w * 0.7f, w * 0.7f, i / M.Max(1f, tentacles - 1f));
                var pts = new List<Vector2> { new Vector2(x0, c.Y - h * 0.6f) };
                for (int k = 1; k <= 5; k++) pts.Add(new Vector2(x0 + 0.08f * M.Sin(k * 1.3f + i), c.Y - h * 0.6f - k * h * 0.28f));
                limbs.Add(pts);
            }
            if (limbs.Count > 0)
                parts.Add(new Part("limbs", (x, y) => limbs.Min(l => Geo.Chain(x, y, l, w * 0.09f, w * 0.025f))));

            bool bumps = s.Get("bumps", 0) > 0.5f, drips = s.Get("drips", 0) > 0.5f;
            var lumps = new List<(Vector2, float)>();
            if (bumps) for (int i = 0; i < 5; i++) lumps.Add((c + Geo.Ahead(30 + i * 30) * (h * 0.8f), w * 0.22f));
            var dripPts = new List<(Vector2, Vector2)>();
            if (drips) for (int i = 0; i < 4; i++) { var a = c + new Vector2(M.Lerp(-w * 0.7f, w * 0.7f, i / 3f), -h * 0.2f); dripPts.Add((a, a + new Vector2(0, -h * (0.35f + 0.15f * (i % 2))))); }
            parts.Add(new Part("body", (x, y) =>
            {
                float d = (M.Len((x - c.X) / w, (y - c.Y) / h) - 1) * M.Min(w, h);
                if (!floating) d = M.Max(d, -(y - 0f));   // flat underneath, sitting on the ground
                foreach (var (p, rr) in lumps) d = M.Min(d, Sdf.Circle(x - p.X, y - p.Y, rr));
                foreach (var (a, b) in dripPts) d = M.Min(d, Sdf2.Capsule(x, y, a, b, w * 0.07f, w * 0.04f));
                return d;
            }));
            int eyes = (int)s.Get("eyes", 2);
            var eyePts = new List<Vector2>();
            for (int i = 0; i < eyes; i++) eyePts.Add(c + new Vector2(w * (0.25f + 0.2f * i), h * (0.25f - 0.12f * (i % 2))));
            bool skull = s.Get("skull", 0) > 0.5f;
            var sk = c + new Vector2(w * 0.35f, h * 0.35f);
            if (skull || eyes > 0)
                parts.Add(new Part("face", (x, y) => skull ? M.Min(Sdf.Circle(x - sk.X, y - sk.Y, w * 0.22f), Sdf2.Capsule(x, y, sk + new Vector2(w * 0.05f, -w * 0.2f), sk + new Vector2(w * 0.2f, -w * 0.25f), w * 0.08f, w * 0.07f))
                                                          : eyePts.Min(e => Sdf.Circle(x - e.X, y - e.Y, w * 0.09f)),
                    skull ? (Func<float, float, float>)((x, y) => M.Abs(Sdf.Circle(x - sk.X - w * 0.07f, y - sk.Y, w * 0.06f))) : null));
            Geo.Ground(parts, (floating ? 0.4f : 0) + lift, s.Get("scale", 1));
            return parts;
        }
    }

    // =====================================================================================================
    /// <summary>
    /// Insects and ticks on the ground: a head with mandibles and feelers, a thorax and a ringed abdomen, three
    /// jointed legs a side; the seeker soldier raises its front pair as claws.
    /// </summary>
    public static class Insect
    {
        public const string BuiltinBodies = @"{ ""bodies"": [
  { ""name"": ""tick"", ""words"": [""ticks""], ""scale"": 0.6, ""head"": 0.06, ""thorax"": 0.08, ""abdomen"": 0.22, ""leg"": 0.22, ""rings"": 0, ""claws"": 0 },
  { ""name"": ""seekersoldier"", ""words"": [""seekersoldiers"", ""seekerbrute"", ""soldier"", ""soldiers""], ""scale"": 2.4,
    ""head"": 0.12, ""thorax"": 0.18, ""abdomen"": 0.26, ""leg"": 0.5, ""rings"": 1, ""claws"": 1 },
  { ""name"": ""queen"", ""words"": [""seekerqueen"", ""thequeen""], ""scale"": 5,
    ""head"": 0.12, ""thorax"": 0.16, ""abdomen"": 0.34, ""leg"": 0.45, ""rings"": 1, ""claws"": 1 }
] }";

        public static readonly BodyLibrary Bodies = new BodyLibrary(BuiltinBodies);

        public static List<Part> Build(Species s, Pose pose)
        {
            float hr = s.Get("head", 0.08f), tr = s.Get("thorax", 0.12f), ar = s.Get("abdomen", 0.2f), leg = s.Get("leg", 0.3f);
            float raise = pose.Get("raise", 0), step = pose.Get("step", 0);
            var thorax = new Vector2(0, leg * 0.6f);
            var abdomen = thorax + new Vector2(-(tr + ar * 0.9f), ar * 0.25f);
            var head = thorax + new Vector2(tr + hr * 0.8f, tr * 0.2f);
            var parts = new List<Part>();
            bool claws = s.Get("claws", 0) > 0.5f;

            List<Vector2> LegPts(float x0, float outDeg, float len)
            {
                var hip = thorax + new Vector2(x0, -tr * 0.3f);
                var knee = hip + Geo.Ahead(outDeg) * (len * 0.45f) + new Vector2(0, len * 0.25f);
                var foot = new Vector2(knee.X + Geo.Ahead(outDeg).X * len * 0.25f, 0);
                return new List<Vector2> { hip, knee, foot };
            }
            var far = new List<List<Vector2>> { LegPts(tr * 0.4f, 35 + step, leg), LegPts(0, 80 - step, leg * 0.9f), LegPts(-tr * 0.4f, 140 + step, leg) };
            var near = new List<List<Vector2>> { LegPts(tr * 0.5f, 25 - step, leg), LegPts(0.02f, 95 + step, leg * 0.9f), LegPts(-tr * 0.3f, 150 - step, leg) };
            if (claws)
            {
                // the front pair raised as claws (more with "raise")
                var shoulder = thorax + new Vector2(tr * 0.6f, tr * 0.2f);
                var elbow = shoulder + Geo.Ahead(40 + raise) * (leg * 0.5f);
                var hook = elbow + Geo.Ahead(-30 + raise) * (leg * 0.45f);
                near[0] = new List<Vector2> { shoulder, elbow, hook };
                far[0] = new List<Vector2> { shoulder + new Vector2(-0.02f, 0.02f), elbow + new Vector2(-0.04f, 0.04f), hook + new Vector2(-0.04f, 0.05f) };
            }
            float lw = M.Max(0.012f, tr * 0.18f);
            parts.Add(new Part("far legs", (x, y) => far.Min(l => Geo.Chain(x, y, l, lw * 1.3f, lw * 0.6f))));
            parts.Add(new Part("abdomen", (x, y) => (M.Len((x - abdomen.X) / (ar * 1.2f), (y - abdomen.Y) / ar) - 1) * ar,
                s.Get("rings", 0) > 0.5f ? (Func<float, float, float>)((x, y) =>
                {
                    float best = float.MaxValue;
                    for (int i = 1; i <= 4; i++)
                    {
                        float px = abdomen.X - ar * 1.2f + i * ar * 2.4f / 5;
                        best = M.Min(best, M.Abs(x - px) + M.Max(0, M.Abs(y - abdomen.Y) - ar * 0.9f));
                    }
                    return best;
                }) : null));
            parts.Add(new Part("thorax", (x, y) => Sdf.Circle(x - thorax.X, y - thorax.Y, tr)));
            var ant = head + new Vector2(hr * 1.5f, hr * 2.2f);
            var mand = head + new Vector2(hr * 1.6f, -hr * 0.6f);
            var eye = head + new Vector2(hr * 0.3f, hr * 0.3f);
            parts.Add(new Part("head", (x, y) => M.Min(M.Min(Sdf.Circle(x - head.X, y - head.Y, hr), Sdf2.Capsule(x, y, head, ant, hr * 0.12f, 0.004f)),
                                                    Sdf2.Capsule(x, y, head, mand, hr * 0.35f, hr * 0.1f)),
                (x, y) => M.Abs(Sdf.Circle(x - eye.X, y - eye.Y, hr * 0.25f))));
            parts.Add(new Part("legs", (x, y) => near.Min(l => Geo.Chain(x, y, l, lw * 1.4f, lw * 0.6f))));
            Geo.Ground(parts, 0, s.Get("scale", 1));
            return parts;
        }
    }
}
