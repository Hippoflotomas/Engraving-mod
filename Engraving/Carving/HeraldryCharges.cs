using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Engraving.Core.Charges;
using Engraving.Core.Geometry;
using Engraving.Core.Imaging;
using Engraving.Core.Language;

namespace Engraving.Carving
{
    /// <summary>
    /// Picture charges through Heraldry Mod (API 3), so every player carves the same picture and pictures they
    /// don't have are fetched the way heraldry fetches charges for arms (server packs, other players).
    /// <list type="bullet">
    /// <item>When a carving is applied, each picture charge's name is resolved to a fingerprint and recorded in a
    /// last line <c>#charges: wolf=1a2b..., raven=...</c> (a comment to the parser; hidden in the editor).</item>
    /// <item>Before drawing, on the main thread, each recorded (or newly resolved) fingerprint's picture is fetched
    /// from heraldry into a store the background renderer reads; the render job gets a name → fingerprint map.</item>
    /// </list>
    /// Without Heraldry Mod (or with an older one) none of this happens and charges come from the folders.
    /// </summary>
    internal static class HeraldryCharges
    {
        const string TailPrefix = "#charges:";

        public static bool Available => HeraldryBridge.ApiVersion >= 3;

        static readonly object Gate = new object();
        /// <summary>Pictures by fingerprint (lower case), for the background renderer.</summary>
        static readonly Dictionary<string, byte[]> Pictures = new Dictionary<string, byte[]>();
        static int _lastGeneration = int.MinValue;

        // ------------------------------------------------------------------ the recorded fingerprints

        /// <summary>The script without the recorded fingerprints, as players write and read it.</summary>
        public static string Visible(string script)
        {
            if (string.IsNullOrEmpty(script)) return "";
            var lines = script.Replace("\r\n", "\n").Split('\n').Where(l => !l.TrimStart().StartsWith(TailPrefix, StringComparison.OrdinalIgnoreCase));
            return string.Join("\n", lines).TrimEnd();
        }

        static Dictionary<string, string> RecordedIn(string script)
        {
            var map = new Dictionary<string, string>();
            foreach (var line in (script ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string l = line.Trim();
                if (!l.StartsWith(TailPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var pair in l.Substring(TailPrefix.Length).Split(','))
                {
                    int eq = pair.IndexOf('=');
                    if (eq <= 0) continue;
                    string name = pair.Substring(0, eq).Trim().ToLowerInvariant(), fp = pair.Substring(eq + 1).Trim().ToLowerInvariant();
                    if (name.Length > 0 && fp.Length > 0) map[name] = fp;
                }
            }
            return map;
        }

        /// <summary>Picture charge names in a script (not built-in shapes), lower case, as written.</summary>
        static IEnumerable<string> PictureNames(string script) =>
            Parser.Parse(Visible(script)).Spec.Charges.Select(c => c.Name.ToLowerInvariant().Trim())
                .Where(n => n.Length > 0 && !ChargeLibrary.Candidates(n).Any(c => BuiltinShapes.TryGet(c, out _)))
                .Distinct();

        /// <summary>The name as written, then its singular forms (spaces kept, for heraldry's names).</summary>
        static IEnumerable<string> Spellings(string name)
        {
            yield return name;
            if (name.EndsWith("ves")) { yield return name.Substring(0, name.Length - 3) + "f"; yield return name.Substring(0, name.Length - 3) + "fe"; }
            if (name.EndsWith("ies")) yield return name.Substring(0, name.Length - 3) + "y";
            if (name.EndsWith("es")) yield return name.Substring(0, name.Length - 2);
            if (name.EndsWith("s")) yield return name.Substring(0, name.Length - 1);
            if (name.EndsWith("men")) yield return name.Substring(0, name.Length - 3) + "man";
        }

        static string Resolve(string name)
        {
            foreach (var spelling in Spellings(name))
            {
                string fp = HeraldryBridge.ResolveCharge(spelling);
                if (!string.IsNullOrEmpty(fp)) return fp.ToLowerInvariant();
            }
            return null;
        }

        /// <summary>
        /// The script to store when a carving is applied: what the player wrote, plus the fingerprint each picture
        /// charge resolves to here, so everyone draws the same pictures. Main thread.
        /// </summary>
        public static string WithFingerprints(string script)
        {
            string visible = Visible(script);
            if (!Available) return visible;
            var pairs = new List<string>();
            foreach (var name in PictureNames(visible))
            {
                string fp = Resolve(name);
                if (fp != null) pairs.Add(name + "=" + fp);
            }
            return pairs.Count == 0 ? visible : visible + "\n" + TailPrefix + " " + string.Join(", ", pairs);
        }

        // ------------------------------------------------------------------ before drawing

        /// <summary>
        /// Main thread, before a carving is drawn: which picture charges to draw from heraldry, as name → fingerprint,
        /// for the pictures that are here. Missing ones are asked for (they appear when they arrive; see <see cref="Poll"/>).
        /// <paramref name="waiting"/> counts pictures still on their way.
        /// </summary>
        public static Dictionary<string, string> Prepare(string script, out int waiting)
        {
            waiting = 0;
            var map = new Dictionary<string, string>();
            if (!Available) return map;
            var recorded = RecordedIn(script);
            foreach (var name in PictureNames(script))
            {
                string fp = recorded.TryGetValue(name, out var r) ? r : Resolve(name);
                if (fp == null) continue;
                bool have;
                lock (Gate) have = Pictures.ContainsKey(fp);
                if (!have)
                {
                    var bytes = HeraldryBridge.GetChargeImage(fp);
                    if (bytes == null) { waiting++; continue; }
                    lock (Gate) Pictures[fp] = bytes;
                }
                map[name] = fp;
            }
            return map;
        }

        /// <summary>Main thread, every frame: true when heraldry's pictures changed (one arrived, was blocked...).</summary>
        public static bool Poll()
        {
            // in the menus there's nothing to redraw, and the bridge would search the loaded assemblies every frame
            if (ZNetScene.instance == null || !Available) return false;
            int g = HeraldryBridge.ChargeGeneration();
            if (g == _lastGeneration) return false;
            bool first = _lastGeneration == int.MinValue;
            _lastGeneration = g;
            lock (Gate) Pictures.Clear();   // blocked ones must go; the rest are fetched again as needed
            return !first;
        }

        /// <summary>Text for render keys, so carvings drawn with different pictures don't share a cache entry.</summary>
        public static string KeyOf(IDictionary<string, string> map) =>
            map == null || map.Count == 0 ? "" : string.Join(",", map.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value));

        /// <summary>Background thread: points the charges named in the map at their fingerprints.</summary>
        public static void Apply(EngravingSpec spec, IDictionary<string, string> map)
        {
            if (map == null || map.Count == 0) return;
            foreach (var c in spec.Charges)
                if (map.TryGetValue(c.Name.ToLowerInvariant().Trim(), out var fp)) c.Name = FingerprintName(fp);
        }

        public static string FingerprintName(string fp) => "heraldryfp" + fp;

        /// <summary>The charge source the renderer reads heraldry's pictures from (by <see cref="FingerprintName"/>).</summary>
        public sealed class Source : IChargeSource
        {
            public bool TryGetMask(string normalisedName, out AlphaMask mask)
            {
                mask = null;
                if (!normalisedName.StartsWith("heraldryfp")) return false;
                byte[] bytes;
                lock (Gate) Pictures.TryGetValue(normalisedName.Substring("heraldryfp".Length), out bytes);
                if (bytes == null) return false;
                try { mask = Png.LoadMask(bytes); return true; }
                catch (Exception) { return false; }
            }
        }
    }
}
