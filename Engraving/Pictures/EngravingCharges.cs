using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Engraving.Core.Charges;
using Engraving.Core.Geometry;
using Engraving.Core.Imaging;
using Engraving.Core.Language;

namespace Engraving.Pictures
{
    /// <summary>Where a picture charge came from. Decides which wins when two have the same name.</summary>
    internal enum PictureSource
    {
        /// <summary>The server's EngravingsServer folder (listed when you join; fetched without asking).</summary>
        Server = 0,
        /// <summary>Your Engravings folder.</summary>
        Own = 1,
        /// <summary>Heraldry Mod's Charges folder, read as plain files (no link to that mod).</summary>
        HeraldryFolder = 2,
        /// <summary>Pictures you accepted from other players (engrave_get).</summary>
        Downloaded = 3,
    }

    /// <summary>
    /// Every picture charge Engraving knows, by fingerprint (first 16 hex digits of the SHA-256 of the PNG file),
    /// and how names resolve to them. Engraving's own counterpart of Heraldry Mod's charge library (2026-10-30:
    /// Hippo wanted Engraving fully independent of Heraldry Mod).
    /// <list type="bullet">
    /// <item>When a carving is applied, each picture charge's name is resolved to a fingerprint and recorded in a
    /// hidden last line, <c>#charges: wolf=1a2b..., raven=...</c>, so every player carves the same picture.</item>
    /// <item>Before drawing (main thread), the pictures a carving needs are put where the background renderer
    /// finds them; missing ones go to <see cref="EngravingSync"/> (the server's own at once, other players' once
    /// approved with engrave_get).</item>
    /// </list>
    /// </summary>
    internal static class EngravingCharges
    {
        public const int FingerprintLength = 16;
        const string TailPrefix = "#charges:";

        internal sealed class Entry
        {
            public string Fingerprint;
            public string Name;            // as the file was called, for messages
            public string Key;             // normalised name, for lookups
            public PictureSource Source;
            public string Path;            // null for server pictures known only from the catalogue
            public long Size;
            public byte[] Bytes;           // loaded on first use
        }

        internal sealed class Waiting
        {
            public string Fingerprint;
            public string Name;
            public bool FromServer;
            public string Status = "waiting for engrave_get";
        }

        static readonly object Gate = new object();
        static readonly Dictionary<string, Entry> ByFingerprint = new Dictionary<string, Entry>();
        static readonly Dictionary<string, Waiting> Pending = new Dictionary<string, Waiting>();
        static readonly HashSet<string> Missing = new HashSet<string>();   // fingerprints already reported missing

        /// <summary>Changes whenever which pictures can be drawn changes, so carvings using them redraw.</summary>
        public static int Generation { get; private set; }

        // ------------------------------------------------------------------ folders

        public static string Root
        {
            get
            {
                string custom = EngravingPlugin.HeraldryFolder.Value;
                if (!string.IsNullOrWhiteSpace(custom)) return custom.Trim();
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrEmpty(docs)) docs = BepInEx.Paths.ConfigPath; // a server account without Documents
                return System.IO.Path.Combine(docs, "ValheimHeraldryMod");
            }
        }

        public static string OwnFolder => System.IO.Path.Combine(Root, "Engravings");
        public static string HeraldryChargesFolder => System.IO.Path.Combine(Root, "Charges");
        public static string DownloadFolder => System.IO.Path.Combine(Root, "EngravingsDownloaded");
        public static string ServerFolder
        {
            get
            {
                string custom = EngravingPlugin.ServerFolder.Value;
                return string.IsNullOrWhiteSpace(custom) ? System.IO.Path.Combine(Root, "EngravingsServer") : custom.Trim();
            }
        }

        public static string FingerprintOf(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder();
                for (int i = 0; i < FingerprintLength / 2; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        public static bool IsFingerprint(string s) =>
            s != null && s.Length == FingerprintLength && s.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));

        /// <summary>Reads your own folders (Engravings, Heraldry Mod's Charges, downloads). Keeps the server's.</summary>
        public static void LoadLocal()
        {
            try { Directory.CreateDirectory(OwnFolder); }
            catch (Exception e) { Jotunn.Logger.LogWarning($"[Engraving] Can't create {OwnFolder}: {e.Message}"); }
            lock (Gate)
            {
                foreach (var fp in ByFingerprint.Where(kv => kv.Value.Source != PictureSource.Server).Select(kv => kv.Key).ToList())
                    ByFingerprint.Remove(fp);
            }
            int own = LoadFolder(OwnFolder, PictureSource.Own);
            int heraldry = EngravingPlugin.UseHeraldryCharges.Value ? LoadFolder(HeraldryChargesFolder, PictureSource.HeraldryFolder) : 0;
            int downloaded = LoadFolder(DownloadFolder, PictureSource.Downloaded);
            Changed();
            Jotunn.Logger.LogInfo($"[Engraving] Pictures: {own} in Engravings, {heraldry} in Heraldry Mod's Charges, {downloaded} downloaded.");
        }

        /// <summary>When this computer is the server: its EngravingsServer folder, offered to everyone who joins.</summary>
        public static int LoadServerFolder()
        {
            lock (Gate)
                foreach (var fp in ByFingerprint.Where(kv => kv.Value.Source == PictureSource.Server).Select(kv => kv.Key).ToList())
                    ByFingerprint.Remove(fp);
            int n = LoadFolder(ServerFolder, PictureSource.Server);
            Changed();
            return n;
        }

        static int LoadFolder(string dir, PictureSource source)
        {
            if (!Directory.Exists(dir)) return 0;
            int n = 0;
            foreach (var path in Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories))
            {
                try
                {
                    var info = new FileInfo(path);
                    if (info.Length > EngravingPlugin.MaxPictureBytes) { Jotunn.Logger.LogWarning($"[Engraving] {path} is over MaxPictureKB; skipped."); continue; }
                    var bytes = File.ReadAllBytes(path);
                    string name = System.IO.Path.GetFileNameWithoutExtension(path);
                    // downloaded files are saved as <fingerprint>.<name>.png
                    if (source == PictureSource.Downloaded && name.Length > FingerprintLength + 1 && name[FingerprintLength] == '.')
                        name = name.Substring(FingerprintLength + 1);
                    Add(new Entry
                    {
                        Fingerprint = FingerprintOf(bytes), Name = name, Key = ChargeLibrary.Normalise(name),
                        Source = source, Path = path, Size = bytes.Length, Bytes = bytes,
                    });
                    n++;
                }
                catch (Exception e) { Jotunn.Logger.LogWarning($"[Engraving] Couldn't read {path}: {e.Message}"); }
            }
            return n;
        }

        static void Add(Entry e)
        {
            lock (Gate)
            {
                // the same picture from several places keeps the strongest source
                if (ByFingerprint.TryGetValue(e.Fingerprint, out var old) && old.Source <= e.Source)
                {
                    if (old.Bytes == null && e.Bytes != null) { old.Bytes = e.Bytes; old.Path = e.Path; }
                    return;
                }
                ByFingerprint[e.Fingerprint] = e;
            }
        }

        static void Changed()
        {
            Generation++;
            CarvedPieceGeneration();
        }

        static void CarvedPieceGeneration() => Carving.CarvedPiece.Generation++;

        // ------------------------------------------------------------------ the server's list

        /// <summary>The server's pictures, as it listed them on joining (pictures arrive when needed).</summary>
        public static void SetCatalogue(IEnumerable<(string fp, string name, long size)> entries)
        {
            lock (Gate)
            {
                foreach (var fp in ByFingerprint.Where(kv => kv.Value.Source == PictureSource.Server && kv.Value.Bytes == null).Select(kv => kv.Key).ToList())
                    ByFingerprint.Remove(fp);
                foreach (var (fp, name, size) in entries)
                {
                    if (ByFingerprint.TryGetValue(fp, out var have))
                    {
                        have.Source = PictureSource.Server;   // the server's word wins for name lookups
                        continue;
                    }
                    ByFingerprint[fp] = new Entry { Fingerprint = fp, Name = name, Key = ChargeLibrary.Normalise(name), Source = PictureSource.Server, Size = size };
                }
            }
            Changed();
        }

        /// <summary>Leaving a server: forget its list (pictures you downloaded stay on disk).</summary>
        public static void LeaveServer()
        {
            lock (Gate)
            {
                foreach (var fp in ByFingerprint.Where(kv => kv.Value.Source == PictureSource.Server && kv.Value.Path == null).Select(kv => kv.Key).ToList())
                    ByFingerprint.Remove(fp);
                Pending.Clear();
                Missing.Clear();
            }
            Changed();
        }

        public static IEnumerable<Entry> ServerPictures()
        {
            lock (Gate) return ByFingerprint.Values.Where(e => e.Source == PictureSource.Server && e.Bytes != null).ToList();
        }

        public static bool IsFromServer(string fp)
        {
            lock (Gate) return ByFingerprint.TryGetValue(fp, out var e) && e.Source == PictureSource.Server;
        }

        // ------------------------------------------------------------------ pictures

        public static byte[] Bytes(string fp)
        {
            lock (Gate) return fp != null && ByFingerprint.TryGetValue(fp, out var e) ? e.Bytes : null;
        }

        public static string NameOf(string fp)
        {
            lock (Gate) return fp != null && ByFingerprint.TryGetValue(fp, out var e) ? e.Name : null;
        }

        /// <summary>A picture that arrived from the server (checked by the caller): saved and usable at once. Main thread.</summary>
        public static void AddDownloaded(string fp, string name, byte[] bytes)
        {
            bool fromServer = IsFromServer(fp);
            string safe = new string((name ?? "picture").Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_').ToArray()).Trim();
            if (safe.Length == 0) safe = "picture";
            string path = null;
            try
            {
                Directory.CreateDirectory(DownloadFolder);
                path = System.IO.Path.Combine(DownloadFolder, fp + "." + safe + ".png");
                File.WriteAllBytes(path, bytes);
            }
            catch (Exception e) { Jotunn.Logger.LogWarning($"[Engraving] Couldn't save a downloaded picture: {e.Message}"); }
            lock (Gate)
            {
                if (ByFingerprint.TryGetValue(fp, out var e)) { e.Bytes = bytes; e.Path = path ?? e.Path; e.Size = bytes.Length; }
                else ByFingerprint[fp] = new Entry { Fingerprint = fp, Name = safe, Key = ChargeLibrary.Normalise(safe), Source = fromServer ? PictureSource.Server : PictureSource.Downloaded, Path = path, Size = bytes.Length, Bytes = bytes };
                Pending.Remove(fp);
                Missing.Remove(fp);
            }
            Changed();
        }

        /// <summary>Checks a picture from the network: right fingerprint, within the size limit, a PNG we can read.</summary>
        public static string Problem(string fp, byte[] bytes, long maxBytes)
        {
            if (bytes == null || bytes.Length == 0) return "empty";
            if (bytes.Length > maxBytes) return $"too large ({bytes.Length / 1024} KB)";
            if (FingerprintOf(bytes) != fp) return "arrived different from what was asked for";
            try
            {
                Png.DecodeRgba(bytes, out int w, out int h);
                if (w > 2048 || h > 2048) return $"too big ({w} x {h}; at most 2048 x 2048)";
            }
            catch (Exception e) { return "not a readable PNG: " + e.Message; }
            return null;
        }

        // ------------------------------------------------------------------ names

        /// <summary>
        /// The fingerprint a name resolves to here: the server's pictures first, then yours, then Heraldry Mod's
        /// Charges folder, then downloaded ones. Plurals fold ("wolves" finds "wolf"). Null if none.
        /// </summary>
        public static string Resolve(string name)
        {
            lock (Gate)
            {
                foreach (var cand in ChargeLibrary.Candidates(name))
                {
                    var best = ByFingerprint.Values.Where(e => e.Key == cand).OrderBy(e => e.Source).ThenBy(e => e.Name).FirstOrDefault();
                    if (best != null) return best.Fingerprint;
                }
            }
            return null;
        }

        /// <summary>Names of the pictures that can be carved here, for the basic editor's charge list.</summary>
        public static List<string> AvailableNames()
        {
            lock (Gate)
                return ByFingerprint.Values.Where(e => e.Path != null || e.Bytes != null)
                    .GroupBy(e => e.Key).Select(g => g.OrderBy(e => e.Source).First().Name)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ------------------------------------------------------------------ the recorded fingerprints

        /// <summary>The script without the recorded fingerprints, as players write and read it.</summary>
        public static string Visible(string script)
        {
            if (string.IsNullOrEmpty(script)) return "";
            // recorded poses ("#pose:") are hidden too
            var lines = Core.Figures.PoseLibrary.WithoutTail(script).Replace("\r\n", "\n").Split('\n')
                .Where(l => !l.TrimStart().StartsWith(TailPrefix, StringComparison.OrdinalIgnoreCase));
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
                    if (name.Length > 0 && IsFingerprint(fp)) map[name] = fp;
                }
            }
            return map;
        }

        /// <summary>Picture charge names in a script (not built-in shapes), lower case, as written.</summary>
        static IEnumerable<string> PictureNames(string script) =>
            Parser.Parse(Visible(script)).Spec.Charges.Select(c => c.Name.ToLowerInvariant().Trim())
                .Where(n => n.Length > 0 && !ChargeLibrary.Candidates(n).Any(c => BuiltinShapes.TryGet(c, out _)) && !Core.Figures.Catalog.IsFigure(n))
                .Distinct();

        /// <summary>
        /// The script to store when a carving is applied: what the player wrote, plus the fingerprint each picture
        /// charge resolves to here, so everyone draws the same pictures.
        /// </summary>
        public static string WithFingerprints(string script)
        {
            string visible = Visible(script);
            var pairs = new List<string>();
            foreach (var name in PictureNames(visible))
            {
                string fp = Resolve(name);
                if (fp != null) pairs.Add(name + "=" + fp);
            }
            return pairs.Count == 0 ? visible : visible + "\n" + TailPrefix + " " + string.Join(", ", pairs);
        }

        /// <summary>
        /// Main thread, before a carving is drawn: the picture charges to draw, as name → fingerprint, for pictures
        /// that are here. Missing ones are handed to the sync (fetched at once if they're the server's, else listed
        /// for engrave_get); <paramref name="waiting"/> counts them.
        /// </summary>
        public static Dictionary<string, string> Prepare(string script, out int waiting)
        {
            waiting = 0;
            var map = new Dictionary<string, string>();
            var recorded = RecordedIn(script);
            foreach (var name in PictureNames(script))
            {
                string fp = recorded.TryGetValue(name, out var r) ? r : Resolve(name);
                if (fp == null) continue;
                if (Bytes(fp) == null)
                {
                    waiting++;
                    NoteMissing(fp, name);
                    continue;
                }
                map[name] = fp;
            }
            return map;
        }

        static void NoteMissing(string fp, string name)
        {
            bool fromServer = IsFromServer(fp);
            bool first;
            lock (Gate)
            {
                first = Missing.Add(fp);
                if (!Pending.ContainsKey(fp))
                    Pending[fp] = new Waiting { Fingerprint = fp, Name = NameOf(fp) ?? name, FromServer = fromServer };
            }
            if (!first) return;
            if (fromServer) EngravingSync.Request(new[] { fp });
            else EngravingSync.RemindEncounter(NameOf(fp) ?? name);
        }

        public static List<Waiting> PendingList()
        {
            lock (Gate) return Pending.Values.ToList();
        }

        public static void SetStatus(string fp, string status)
        {
            lock (Gate) if (Pending.TryGetValue(fp, out var w)) w.Status = status;
        }

        public static string KeyOf(IDictionary<string, string> map) =>
            map == null || map.Count == 0 ? "" : string.Join(",", map.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value));

        /// <summary>Background thread: points the charges named in the map at their fingerprints.</summary>
        public static void Apply(EngravingSpec spec, IDictionary<string, string> map)
        {
            if (map == null || map.Count == 0) return;
            foreach (var c in spec.Charges)
                if (map.TryGetValue(c.Name.ToLowerInvariant().Trim(), out var fp)) c.Name = "picturefp" + fp;
        }

        /// <summary>The charge source the renderer reads pictures from (by fingerprint names from <see cref="Apply"/>).</summary>
        public sealed class Source : IChargeSource
        {
            public bool TryGetMask(string normalisedName, out AlphaMask mask)
            {
                mask = null;
                if (!normalisedName.StartsWith("picturefp")) return false;
                var bytes = Bytes(normalisedName.Substring("picturefp".Length));
                if (bytes == null) return false;
                try { mask = Png.LoadMask(bytes); return true; }
                catch (Exception) { return false; }
            }
        }
    }
}
