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

        /// <summary>Folder holding the heraldry mod's 'Charges' folder; Engravings sits beside it.</summary>
        public static ConfigEntry<string> HeraldryFolder;
        public static ConfigEntry<int> PixelsPerMetre;
        public static ConfigEntry<float> CarveDepth;
        public static ConfigEntry<string> CarvablePieces;
        public static ConfigEntry<float> ChiselDurability;
        public static ConfigEntry<float> ChiselWearPerCarving;

        public ChargeLibrary Charges { get; private set; }
        Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            HeraldryFolder = Config.Bind("Charges", "HeraldryFolder", "",
                "Folder that contains the heraldry mod's 'Charges' folder; engraving images go in an 'Engravings' folder beside it. " +
                "Empty = the Heraldry Mod's default, Documents\\ValheimHeraldryMod.");
            PixelsPerMetre = Config.Bind("Rendering", "PixelsPerMetre", 256,
                new ConfigDescription("Texture resolution of carvings (a 2 m wall at 256 is 512 x 512).", new AcceptableValueRange<int>(64, 1024)));
            CarveDepth = Config.Bind("Rendering", "CarveDepth", 0.025f,
                new ConfigDescription("How deep the deepest cut looks, in metres. Raises or lowers the relief of every carving.",
                    new AcceptableValueRange<float>(0.002f, 0.1f)));
            CarvablePieces = Config.Bind("Pieces", "CarvablePieces",
                "woodwall,wood_wall_half,wood_wall_quarter,stone_wall_1x1,stone_wall_2x1,stone_wall_4x2",
                "Build pieces that can be carved (prefab names, comma separated). Hover a piece and run 'engrave_inspect' to see its name.");
            ChiselDurability = Config.Bind("Chisel", "Durability", 200f, "Chisel durability at quality 1.");
            ChiselWearPerCarving = Config.Bind("Chisel", "WearPerCarving", 10f, "Durability a chisel loses each time you carve or recarve a piece.");

            CarvablePieces.SettingChanged += (_, __) => CarvedPiece.ReloadCarvable();
            PixelsPerMetre.SettingChanged += (_, __) => CarvedPiece.Generation++;
            CarveDepth.SettingChanged += (_, __) => CarvedPiece.Generation++;

            BuildChargeLibrary();
            Chisel.Register();
            _harmony = new Harmony(PluginGUID);
            _harmony.PatchAll(typeof(EngravingPlugin).Assembly);

            CommandManager.Instance.AddConsoleCommand(new EngraveFileCommand());
            CommandManager.Instance.AddConsoleCommand(new EngraveInspectCommand());
            CommandManager.Instance.AddConsoleCommand(new EngraveReloadCommand());
            Jotunn.Logger.LogInfo($"{PluginName} {PluginVersion} loaded; charge folders: {string.Join(", ", ChargeFolders())}");
        }

        private void Update() => CarvingJobs.Pump();

        private void OnDestroy() => _harmony?.UnpatchSelf();

        public static string[] ChargeFolders()
        {
            string root = string.IsNullOrEmpty(HeraldryFolder.Value)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ValheimHeraldryMod")
                : HeraldryFolder.Value;
            // order matters: engraving art wins over heraldry charges of the same name
            return new[] { Path.Combine(root, "Engravings"), Path.Combine(root, "Charges") };
        }

        public void BuildChargeLibrary()
        {
            Charges = new ChargeLibrary();
            var folders = ChargeFolders();
            try { Directory.CreateDirectory(folders[0]); }
            catch (Exception e) { Jotunn.Logger.LogWarning($"can't create {folders[0]}: {e.Message}"); }
            foreach (var dir in folders) Charges.AddSource(new FolderChargeSource(dir));
            CarvingJobs.SetLibrary(Charges);
            CarvedPiece.Generation++;
        }

        /// <summary>Renders a script to the heightmap the carving uses (main thread; for the dev command).</summary>
        public RenderResult Render(string script, float width, float height, out ParseResult parsed)
        {
            parsed = Parser.Parse(script);
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
                string text = carved.GetText();
                c.Print($"  carving: {(text.Length == 0 ? "none" : $"{text.Length} chars on side {(carved.GetSide() > 0 ? "+Z" : "-Z")}")}; " +
                        $"status: {carved.Status}; shown: {carved.Shown}");
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

    /// <summary>engrave_reload: re-reads charge images and redraws every carving.</summary>
    internal class EngraveReloadCommand : ConsoleCommand
    {
        public override string Name => "engrave_reload";
        public override string Help => "Reload charge images (Engravings and Charges folders) and redraw all carvings";

        public override void Run(string[] args)
        {
            EngravingPlugin.Instance.BuildChargeLibrary();
            global::Console.instance.Print("Charges reloaded; carvings will redraw.");
        }
    }
}
