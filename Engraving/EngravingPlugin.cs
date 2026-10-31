using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using Engraving.Carving;
using Engraving.Core.Charges;
using Engraving.Core.Imaging;
using Engraving.Core.Language;
using Engraving.Core.Render;
using Engraving.Pictures;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Engraving
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    //[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class EngravingPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.hippoflotomas.engraving";
        public const string PluginName = "Engraving";
        public const string PluginVersion = "0.0.1";

        public static EngravingPlugin Instance { get; private set; }

        // Use this class to add your own localization to the game
        // https://valheim-modding.github.io/Jotunn/tutorials/localization.html
        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();

        /// <summary>Folder holding the Engravings folder (and Heraldry Mod's Charges folder, if you use it).</summary>
        public static ConfigEntry<string> HeraldryFolder;
        public static ConfigEntry<bool> UseHeraldryCharges;
        public static ConfigEntry<string> ServerFolder;
        public static ConfigEntry<bool> SharePictures;
        public static ConfigEntry<int> MaxPictureKB, MaxPictureDownloadMB, MaxPictureUploadMB, RelayCacheMB;
        public static long MaxPictureBytes => (long)MaxPictureKB.Value << 10;
        public static long MaxDownloadBytes => (long)MaxPictureDownloadMB.Value << 20;
        public static long MaxUploadBytes => (long)MaxPictureUploadMB.Value << 20;
        public static long RelayCacheBytes => (long)RelayCacheMB.Value << 20;
        public static ConfigEntry<int> PixelsPerMetre;
        public static ConfigEntry<float> CarveDepth;
        public static ConfigEntry<int> AlbedoMaxSize;
        public static ConfigEntry<bool> KeepWallBumps;
        public static ConfigEntry<string> EditorMode;
        public static ConfigEntry<string> CarvablePieces;
        public static ConfigEntry<float> ChiselDurability;
        public static ConfigEntry<float> ChiselWearPerCarving;

        public ChargeLibrary Charges { get; private set; }
        Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            HeraldryFolder = Config.Bind("Pictures", "PictureRoot", "",
                "Folder holding your 'Engravings' pictures, downloads ('EngravingsDownloaded') and, if UseHeraldryCharges is on, " +
                "Heraldry Mod's 'Charges'. Empty = Documents\\ValheimHeraldryMod (beside Heraldry Mod's own folders).");
            UseHeraldryCharges = Config.Bind("Pictures", "UseHeraldryCharges", true,
                "Also use the PNGs in Heraldry Mod's Charges folder as pictures (plain files; Engraving doesn't need Heraldry Mod).");
            SharePictures = Config.Bind("Pictures", "SharePictures", true,
                "Send your own pictures when other players approve fetching them for carvings they see.");
            MaxPictureKB = Config.Bind("Pictures", "MaxPictureKB", 1024,
                new ConfigDescription("Largest picture (KB) this computer accepts, sends or serves.", new AcceptableValueRange<int>(16, 8192)));
            MaxPictureDownloadMB = Config.Bind("Pictures", "MaxPictureDownloadMB", 64,
                new ConfigDescription("Most picture data (MB) this computer downloads per session.", new AcceptableValueRange<int>(1, 1024)));
            MaxPictureUploadMB = Config.Bind("Pictures", "MaxPictureUploadMB", 32,
                new ConfigDescription("Most picture data (MB) this computer sends to other players per session.", new AcceptableValueRange<int>(1, 1024)));
            ServerFolder = Config.Bind("Server", "ServerPictureFolder", "",
                "Pictures this computer offers to everyone when it's the server (hosting or dedicated); players get them without asking. " +
                "Empty = <PictureRoot>\\EngravingsServer.");
            RelayCacheMB = Config.Bind("Server", "RelayCacheMB", 32,
                new ConfigDescription("Memory (MB) the server keeps players' pictures in, so they aren't fetched twice. Never written to disk.",
                    new AcceptableValueRange<int>(0, 512)));
            PixelsPerMetre = Config.Bind("Rendering", "PixelsPerMetre", 256,
                new ConfigDescription("Texture resolution of carvings (a 2 m wall at 256 is 512 x 512).", new AcceptableValueRange<int>(64, 1024)));
            CarveDepth = Config.Bind("Rendering", "CarveDepth", 0.025f,
                new ConfigDescription("How deep the deepest cut looks, in metres. Raises or lowers the relief of every carving.",
                    new AcceptableValueRange<float>(0.002f, 0.1f)));
            AlbedoMaxSize = Config.Bind("Rendering", "ColourTextureMaxSize", 1024,
                new ConfigDescription("Largest size of a carving's colour texture. It follows the wall's own texture detail up to this; " +
                                      "higher is sharper but uses more video memory (1024 x 1024 is about 5 MB per distinct carving).",
                    new AcceptableValueList<int>(512, 1024, 2048)));
            KeepWallBumps = Config.Bind("Rendering", "KeepWallBumps", true,
                "Blend the wall's own normal map (stone bumps, wood grain) into carvings, so carved stone stays rough. " +
                "Off: carvings are smooth, and walls of different kinds share one normal map per carving (less video memory).");
            CarvablePieces = Config.Bind("Pieces", "CarvablePieces",
                "woodwall,wood_wall_half,wood_wall_quarter,stone_wall_1x1,stone_wall_2x1,stone_wall_4x2,wood_door,wood_gate",
                "Build pieces that can be carved (prefab names, comma separated). Hover a piece and run 'engrave_inspect' to see its name.");
            EditorMode = Config.Bind("Editor", "Mode", "basic",
                new ConfigDescription("Which tab the carving window opens on (remembered when you switch).", new AcceptableValueList<string>("basic", "advanced")));
            ChiselDurability = Config.Bind("Chisel", "Durability", 200f, "Chisel durability at quality 1.");
            ChiselWearPerCarving = Config.Bind("Chisel", "WearPerCarving", 10f, "Durability a chisel loses each time you carve or recarve a piece.");

            CarvablePieces.SettingChanged += (_, __) => CarvedPiece.ReloadCarvable();
            PixelsPerMetre.SettingChanged += (_, __) => CarvedPiece.Generation++;
            CarveDepth.SettingChanged += (_, __) => CarvedPiece.Generation++;
            AlbedoMaxSize.SettingChanged += (_, __) => CarvedPiece.Generation++;
            KeepWallBumps.SettingChanged += (_, __) => CarvedPiece.Generation++;

            UseHeraldryCharges.SettingChanged += (_, __) => EngravingCharges.LoadLocal();

            BuildChargeLibrary();
            EngravingCharges.LoadLocal();
            EngravingSync.Init();
            Chisel.Register();
            _harmony = new Harmony(PluginGUID);
            _harmony.PatchAll(typeof(EngravingPlugin).Assembly);

            CommandManager.Instance.AddConsoleCommand(new EngraveFileCommand());
            CommandManager.Instance.AddConsoleCommand(new EngraveInspectCommand());
            CommandManager.Instance.AddConsoleCommand(new EngraveDumpCommand());
            CommandManager.Instance.AddConsoleCommand(new EngraveReloadCommand());
            CommandManager.Instance.AddConsoleCommand(new EngravePendingCommand());
            CommandManager.Instance.AddConsoleCommand(new EngraveGetCommand());
            Jotunn.Logger.LogInfo($"{PluginName} {PluginVersion} loaded; pictures in {EngravingCharges.OwnFolder}" +
                                  (UseHeraldryCharges.Value ? $" and {EngravingCharges.HeraldryChargesFolder}" : ""));
        }

        private void Update()
        {
            CarvingJobs.Pump();
            EngravingSync.Update();
            // an item icon was read back: redraw what uses it
            if (ItemIconCharges.Changed)
            {
                ItemIconCharges.Changed = false;
                lock (Charges) Charges.ClearCache();
                CarvingJobs.SetLibrary(Charges);
                CarvedPiece.Generation++;
            }
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();

        public void BuildChargeLibrary()
        {
            Charges = new ChargeLibrary();
            // pictures by fingerprint (yours, the server's, downloaded, Heraldry Mod's Charges folder), then item icons
            Charges.AddSource(new EngravingCharges.Source());
            Charges.AddSource(new ItemIconCharges.Source());
            CarvingJobs.SetLibrary(Charges);
            CarvedPiece.Generation++;
        }

        /// <summary>Renders a script to the heightmap the carving uses (main thread; for the dev command).</summary>
        public RenderResult Render(string script, float width, float height, out ParseResult parsed)
        {
            parsed = Parser.Parse(script);
            EngravingCharges.Apply(parsed.Spec, EngravingCharges.Prepare(script, out _));
            var opt = new RenderOptions { Width = width, Height = height, PixelsPerMetre = PixelsPerMetre.Value };
            lock (Charges) return EngravingRenderer.Render(parsed.Spec, Charges, opt);
        }
    }

    /// <summary>
    /// Dev check that the engine runs inside the game: engrave_file path/to/panel.eng [width height]
    /// writes panel.height.png next to the script and reports any problems with the script.
    /// </summary>
    internal class EngraveFileCommand : ConsoleCommand
    {
        public override string Name => "engrave_file";
        public override string Help => "Render an engraving script to a heightmap PNG: engrave_file <path.eng> [width height]";

        public override void Run(string[] args)
        {
            if (args.Length < 1) { global::Console.instance.Print("usage: " + Help); return; }
            string path = args[0];
            if (!File.Exists(path)) { global::Console.instance.Print($"no such file: {path}"); return; }
            float w = 2, h = 2;
            if (args.Length >= 3)
            {
                float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out w);
                float.TryParse(args[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out h);
            }

            var plugin = EngravingPlugin.Instance;
            lock (plugin.Charges) plugin.Charges.ClearCache();
            var sw = Stopwatch.StartNew();
            var result = plugin.Render(File.ReadAllText(path), w, h, out var parsed);
            sw.Stop();
            foreach (var d in parsed.Diagnostics) global::Console.instance.Print(d.ToString());
            foreach (var d in result.Diagnostics) global::Console.instance.Print(d.ToString());

            string outPath = Path.ChangeExtension(path, ".height.png");
            File.WriteAllBytes(outPath, Png.EncodeGray16(result.Map.H, result.Map.Width, result.Map.Height));
            global::Console.instance.Print($"{result.Map.Width}x{result.Map.Height} in {sw.ElapsedMilliseconds} ms -> {outPath}");
        }
    }

    /// <summary>engrave_inspect: what the carver sees on the hovered piece (prefab name, faces found, material).</summary>
    internal class EngraveInspectCommand : ConsoleCommand
    {
        public override string Name => "engrave_inspect";
        public override string Help => "Show the hovered build piece's prefab name and whether (and where) it can be carved";

        public override void Run(string[] args)
        {
            var c = global::Console.instance;
            var player = Player.m_localPlayer;
            var hover = player != null ? player.GetHoverObject() : null;
            var piece = hover != null ? hover.GetComponentInParent<Piece>() : null;
            if (piece == null) { c.Print("Look at a build piece first."); return; }
            string prefab = Utils.GetPrefabName(piece.gameObject);
            bool carvable = CarvedPiece.IsCarvable(prefab);
            c.Print($"'{prefab}' ({Localization.instance.Localize(piece.m_name)}): " +
                    (carvable ? "carvable" : "not in CarvablePieces"));
            foreach (int side in new[] { 1, -1 })
            {
                var model = CarveModel.For(piece.gameObject, prefab, side);
                c.Print($"  side {(side > 0 ? "+Z" : "-Z")}: " + (model == null ? "no flat face" : model.Note));
                if (model == null) continue;
                foreach (var f in model.Faces)
                {
                    var mats = f.Renderer.sharedMaterials;
                    var m = f.SourceSlot < mats.Length ? mats[f.SourceSlot] : null;
                    c.Print($"    {f.Renderer.name}: {f.Triangles} tris, material '{m?.name}' shader '{m?.shader?.name}'" +
                            $" _MainTex:{m != null && m.HasProperty("_MainTex")} _BumpMap:{m != null && m.HasProperty("_BumpMap")}");
                }
            }
            var carved = piece.GetComponent<CarvedPiece>();
            if (carved != null)
            {
                c.Print($"  front: {carved.Describe(1)}");
                c.Print($"  back: {carved.Describe(-1)}");
                foreach (var r in piece.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                    c.Print($"    renderer '{r.name}' (active {r.gameObject.activeInHierarchy}, enabled {r.enabled}): mesh '{mesh?.name}', " +
                            $"{r.sharedMaterials.Length} materials [{string.Join(", ", r.sharedMaterials.Select(m => m != null ? m.name : "null"))}]");
                }
            }
            if (player != null)
                c.Print($"  you're on side {(piece.transform.InverseTransformPoint(player.transform.position).z >= 0 ? "+Z" : "-Z")}");
        }
    }

    /// <summary>engrave_dump: writes the hovered carved piece's textures and face layout to disk, for debugging.</summary>
    internal class EngraveDumpCommand : ConsoleCommand
    {
        public override string Name => "engrave_dump";
        public override string Help => "Save the hovered carved piece's textures and a report to Documents\\ValheimHeraldryMod\\EngravingDebug";

        public override void Run(string[] args)
        {
            var c = global::Console.instance;
            var player = Player.m_localPlayer;
            var hover = player != null ? player.GetHoverObject() : null;
            var carved = hover != null ? hover.GetComponentInParent<CarvedPiece>() : null;
            if (carved == null) { c.Print("Look at a carvable piece first."); return; }
            string dir = System.IO.Path.Combine(EngravingCharges.Root, "EngravingDebug");
            try { c.Print($"Saved {carved.Dump(dir)} file(s) to {dir}"); }
            catch (System.Exception e) { c.Print("Dump failed: " + e.Message); Jotunn.Logger.LogWarning("[Engraving] engrave_dump: " + e); }
        }
    }

    /// <summary>engrave_reload: re-reads charge images and redraws every carving.</summary>
    internal class EngraveReloadCommand : ConsoleCommand
    {
        public override string Name => "engrave_reload";
        public override string Help => "Reload charge images (Engravings and Charges folders) and redraw all carvings";

        public override void Run(string[] args)
        {
            EngravingCharges.LoadLocal();
            if (ZNet.instance != null && ZNet.instance.IsServer()) EngravingCharges.LoadServerFolder();
            EngravingPlugin.Instance.BuildChargeLibrary();
            global::Console.instance.Print("Pictures reloaded; carvings will redraw.");
        }
    }

    /// <summary>engrave_pending: pictures carvings here use that you haven't got.</summary>
    internal class EngravePendingCommand : ConsoleCommand
    {
        public override string Name => "engrave_pending";
        public override string Help => "List pictures that carvings here use and you haven't got (fetch them with engrave_get)";

        public override void Run(string[] args)
        {
            var c = global::Console.instance;
            var list = EngravingCharges.PendingList().Where(w => EngravingCharges.Bytes(w.Fingerprint) == null).ToList();
            if (list.Count == 0) { c.Print("No pictures waiting."); return; }
            foreach (var w in list)
                c.Print($"  {w.Name}  ({w.Fingerprint}){(w.FromServer ? ", the server's" : ", another player's")}: {w.Status}");
            c.Print("Fetch them with 'engrave_get all', or one by name or fingerprint.");
        }
    }

    /// <summary>engrave_get all | name | fingerprint: fetches pictures from the server or other players.</summary>
    internal class EngraveGetCommand : ConsoleCommand
    {
        public override string Name => "engrave_get";
        public override string Help => "Fetch pictures that carvings here use: engrave_get all | <name> | <fingerprint>";

        public override void Run(string[] args)
        {
            var c = global::Console.instance;
            var list = EngravingCharges.PendingList().Where(w => EngravingCharges.Bytes(w.Fingerprint) == null).ToList();
            string what = args.Length > 0 ? string.Join(" ", args).Trim().ToLowerInvariant() : "";
            if (what.Length == 0) { c.Print("usage: " + Help); return; }
            var pick = what == "all" ? list : list.Where(w => w.Fingerprint == what || (w.Name ?? "").ToLowerInvariant() == what).ToList();
            if (pick.Count == 0) { c.Print("Nothing like that is waiting (see engrave_pending)."); return; }
            int n = EngravingSync.Request(pick.Select(w => w.Fingerprint));
            c.Print(n > 0 ? $"Asking for {n} picture(s)..." : "Already asked; they'll arrive when someone online has them.");
        }
    }
}
