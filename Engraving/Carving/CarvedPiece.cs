using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

using Engraving.Pictures;

namespace Engraving.Carving
{
    /// <summary>
    /// On every carvable build piece. Each side (piece-local +Z "front", -Z "back") can carry its own carving;
    /// the scripts live in the piece's ZDO, so every client draws the same carvings from the text.
    /// With a chisel equipped, E opens the editor on the side you're standing on; on pieces that use E
    /// themselves (doors, gates) Shift+E does, as Heraldry Mod does for its tagged pieces.
    /// </summary>
    public class CarvedPiece : MonoBehaviour, Hoverable, Interactable
    {
        public const string FrontKey = "engraving_text_front";
        public const string BackKey = "engraving_text_back";
        // before both sides could be carved (2026-10-29): one text and the side it was on
        const string LegacyTextKey = "engraving_text";
        const string LegacySideKey = "engraving_side";
        public const int MaxScriptLength = 2000;

        ZNetView _nview;
        Piece _piece;
        string _prefab;
        uint _lastRevision = uint.MaxValue;
        int _lastGeneration = -1;

        /// <summary>Bumped when charges or settings change, so every piece redraws.</summary>
        internal static int Generation;

        /// <summary>One side's carving: what's shown, and what's on its way.</summary>
        sealed class Layer
        {
            public int Side;
            public string Key;
            public Carving Carving;
            public CarveModel Model;
            /// <summary>Bumped on every change, so late texture callbacks for an old carving are ignored.</summary>
            public int Token;
            /// <summary>Faces whose renderer is switched off (worn/broken states); textured when they switch on.</summary>
            public readonly List<CarveFace> Waiting = new List<CarveFace>();
            public readonly HashSet<MeshRenderer> Started = new HashSet<MeshRenderer>();
            public readonly List<string> TextureKeys = new List<string>();
            /// <summary>Faces with their textures ready, and the material made for each.</summary>
            public readonly Dictionary<MeshRenderer, (CarveFace face, Material mat)> Ready = new Dictionary<MeshRenderer, (CarveFace, Material)>();
            public string Status = "not carved";
        }

        readonly Layer _front = new Layer { Side = 1 }, _back = new Layer { Side = -1 };
        /// <summary>Each touched renderer's own mesh and materials, to put back.</summary>
        readonly Dictionary<MeshRenderer, (Mesh mesh, Material[] mats)> _originals = new Dictionary<MeshRenderer, (Mesh, Material[])>();
        /// <summary>The carved mesh each renderer was given, to notice when something else swaps it.</summary>
        readonly Dictionary<MeshRenderer, Mesh> _shown = new Dictionary<MeshRenderer, Mesh>();
        static readonly HashSet<string> SwapReported = new HashSet<string>();

        Layer LayerFor(int side) => side > 0 ? _front : _back;

        void Start()
        {
            _nview = GetComponent<ZNetView>();
            _piece = GetComponent<Piece>();
            if (_nview == null || !_nview.IsValid()) { enabled = false; return; } // placement ghost
            // a dedicated server keeps the scripts and relays pictures, but draws nothing
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { enabled = false; return; }
            _prefab = Utils.GetPrefabName(gameObject);
            Refresh();
            InvokeRepeating(nameof(Refresh), 1f, 1f);
        }

        void OnDestroy()
        {
            Clear(_front);
            Clear(_back);
        }

        // ------------------------------------------------------------ the scripts

        ZDO Zdo => _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;

        /// <summary>The carving script on a side ("" if none).</summary>
        public string GetText(int side)
        {
            var zdo = Zdo;
            if (zdo == null) return "";
            string text = zdo.GetString(side > 0 ? FrontKey : BackKey, "");
            if (text.Length == 0)
            {
                string legacy = zdo.GetString(LegacyTextKey, "");
                if (legacy.Length > 0 && (zdo.GetInt(LegacySideKey, 1) > 0) == (side > 0)) text = legacy;
            }
            return text;
        }

        public bool IsCarved => GetText(1).Length > 0 || GetText(-1).Length > 0;

        /// <summary>Which side of the piece (piece-local ±Z) a world position is on.</summary>
        public int SideFacing(Vector3 worldPos) => transform.InverseTransformPoint(worldPos).z >= 0 ? 1 : -1;

        /// <summary>The face that would be carved on a side, for the editor's preview size.</summary>
        internal CarveModel ModelFor(int side) => CarveModel.For(gameObject, _prefab, side);

        internal bool CanEdit(bool flash) => PrivateArea.CheckAccess(transform.position, 0f, flash, false);

        public void SetCarving(string text, int side)
        {
            var zdo = Zdo;
            if (zdo == null || !CanEdit(true)) return;
            text = (text ?? "").Trim();
            if (text.Length > MaxScriptLength) text = text.Substring(0, MaxScriptLength);
            _nview.ClaimOwnership();
            // move an old single-side carving to its own key first, so carving the other side keeps it
            string legacy = zdo.GetString(LegacyTextKey, "");
            if (legacy.Length > 0)
            {
                zdo.Set(zdo.GetInt(LegacySideKey, 1) > 0 ? FrontKey : BackKey, legacy);
                zdo.Set(LegacyTextKey, "");
            }
            zdo.Set(side > 0 ? FrontKey : BackKey, text);
            Jotunn.Logger.LogInfo($"[Engraving] Carving set on '{_prefab}' {(side > 0 ? "front" : "back")}: {text.Length} characters.");
            Refresh();
        }

        // ------------------------------------------------------------ drawing

        void Refresh()
        {
            var zdo = Zdo;
            if (zdo == null) return;
            StartWaitingFaces(_front);
            StartWaitingFaces(_back);
            CheckShown();
            if (zdo.DataRevision == _lastRevision && _lastGeneration == Generation) return;
            _lastRevision = zdo.DataRevision;
            _lastGeneration = Generation;
            Refresh(_front);
            Refresh(_back);
        }

        void Refresh(Layer layer)
        {
            int side = layer.Side;
            string text = GetText(side);
            if (string.IsNullOrWhiteSpace(text)) { Clear(layer); layer.Status = "not carved"; return; }

            var model = CarveModel.For(gameObject, _prefab, side);
            if (model == null) { Clear(layer); layer.Status = "no face on this side"; return; }
            float ppm = EngravingPlugin.PixelsPerMetre.Value;
            var pictures = EngravingCharges.Prepare(text, out int waiting);
            string icons = ItemIconCharges.Prepare(text, out int iconsWaiting);
            string key = CarvingJobs.KeyFor(text, model.Width, model.Height, ppm) + "|" + side + "|" +
                         EngravingPlugin.CarveDepth.Value + "|" + EngravingPlugin.AlbedoMaxSize.Value + "|" + EngravingPlugin.KeepWallBumps.Value + "|" +
                         EngravingCharges.KeyOf(pictures) + "|" + icons;
            if (key == layer.Key) return;

            layer.Status = "drawing" + (waiting > 0 ? $" ({waiting} picture(s) not here yet (engrave_pending))" : "") +
                           (iconsWaiting > 0 ? $" ({iconsWaiting} item icon(s) reading)" : "");
            int token = ++layer.Token;
            CarvingJobs.Request(text, model.Width, model.Height, ppm, pictures, carving =>
            {
                if (this == null || token != layer.Token) return;
                if (carving.Map == null)
                {
                    layer.Status = "drawing failed: " + string.Join("; ", carving.Diagnostics.Select(d => d.Message));
                    Jotunn.Logger.LogWarning($"[Engraving] '{_prefab}': {layer.Status}");
                    return;
                }
                // the text may have changed again while this was drawing
                if (GetText(side) != text) return;
                try { Show(layer, carving, model, key); }
                catch (System.Exception e)
                {
                    layer.Status = "applying failed: " + e.Message;
                    Jotunn.Logger.LogWarning($"[Engraving] '{_prefab}': couldn't apply the carving: {e}");
                }
            });
        }

        void Show(Layer layer, Carving carving, CarveModel model, string key)
        {
            Clear(layer);
            layer.Model = model;
            layer.Carving = carving;
            layer.Key = key;
            // only what can be seen now; the worn and broken models wait until the wall switches to them
            foreach (var face in model.Faces)
            {
                if (face.Renderer == null) continue;
                if (face.Renderer.gameObject.activeInHierarchy) StartFace(layer, face);
                else layer.Waiting.Add(face);
            }
            UpdateStatus(layer);
        }

        void StartWaitingFaces(Layer layer)
        {
            if (layer.Waiting.Count == 0 || layer.Carving == null) return;
            for (int i = layer.Waiting.Count - 1; i >= 0; i--)
            {
                var face = layer.Waiting[i];
                if (face.Renderer == null) { layer.Waiting.RemoveAt(i); continue; }
                if (!face.Renderer.gameObject.activeInHierarchy) continue;
                layer.Waiting.RemoveAt(i);
                StartFace(layer, face);
            }
        }

        /// <summary>Asks for this face's textures; the renderer is redone once both exist.</summary>
        void StartFace(Layer layer, CarveFace face)
        {
            var r = face.Renderer;
            if (!layer.Started.Add(r)) return;
            var own = Original(r).mats;
            var source = face.SourceSlot < own.Length ? own[face.SourceSlot] : own.FirstOrDefault();
            int token = layer.Token;
            var carving = layer.Carving;
            Texture2D normal = null, albedo = null;

            void Ready()
            {
                if (this == null || token != layer.Token || normal == null || albedo == null || r == null) return;
                layer.Ready[r] = (face, MakeMaterial(source, albedo, normal));
                Apply(r);
                UpdateStatus(layer);
            }

            layer.TextureKeys.Add(CarvingTextures.AcquireSurfaceNormal(carving, EngravingPlugin.CarveDepth.Value, source, face, t => { normal = t; Ready(); }));
            layer.TextureKeys.Add(CarvingTextures.AcquireAlbedo(carving, source, face, t => { albedo = t; Ready(); }));
        }

        (Mesh mesh, Material[] mats) Original(MeshRenderer r)
        {
            if (!_originals.TryGetValue(r, out var o))
            {
                o = (CarvedMeshes.OriginalOf(r.GetComponent<MeshFilter>().sharedMesh), r.sharedMaterials);
                _originals[r] = o;
            }
            return o;
        }

        /// <summary>Gives a renderer the mesh and materials for whichever sides are carved and ready on it.</summary>
        void Apply(MeshRenderer r)
        {
            if (r == null) return;
            var original = Original(r);
            var ready = new List<(CarveFace face, Material mat)>();
            if (_front.Ready.TryGetValue(r, out var f)) ready.Add(f);
            if (_back.Ready.TryGetValue(r, out var b)) ready.Add(b);
            var filter = r.GetComponent<MeshFilter>();
            if (ready.Count == 0)
            {
                filter.sharedMesh = original.mesh;
                r.sharedMaterials = original.mats;
                _originals.Remove(r);
                _shown.Remove(r);
                return;
            }
            var variant = CarvedMeshes.For(ready.Select(x => x.face).ToList());
            int count = variant.SlotOf.Values.Max() + 1;
            var mats = new Material[count];
            for (int i = 0; i < count; i++) mats[i] = i < original.mats.Length ? original.mats[i] : original.mats.LastOrDefault();
            foreach (var (face, mat) in ready) mats[variant.SlotOf[face.Side]] = mat;
            filter.sharedMesh = variant.Mesh;
            r.sharedMaterials = mats;
            _shown[r] = variant.Mesh;
        }

        /// <summary>
        /// Something other than this mod put a different mesh on a carved renderer. With our extra material still
        /// on it, Unity draws the whole mesh a second time in the carving's material at the mesh's own UVs: the
        /// carving smeared over both sides (2026-10-31, Grausten wall). Put the carved mesh back if it's the same
        /// model, otherwise drop the carving's materials; and say so in the log, once per piece type.
        /// </summary>
        void CheckShown()
        {
            if (_shown.Count == 0) return;
            foreach (var r in _shown.Keys.ToList())
            {
                if (r == null) { _shown.Remove(r); continue; }
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null) continue;
                var now = filter.sharedMesh;
                var want = _shown[r];
                if (now == want) continue;
                if (SwapReported.Add(_prefab + "/" + r.name))
                    Jotunn.Logger.LogWarning($"[Engraving] '{_prefab}': renderer '{PathTo(r.transform)}' was switched from our '{want?.name}' to " +
                                             $"'{now?.name}' (readable {now?.isReadable}, {now?.subMeshCount} submeshes) by something else; " +
                                             $"{r.sharedMaterials.Length} materials on it.");
                var original = Original(r);
                if (now != null && CarvedMeshes.OriginalOf(now) == original.mesh)
                {
                    Apply(r);   // the same model came back: show the carving on it again
                }
                else
                {
                    // a different model: carve nothing on it rather than smear the carving over it
                    _originals[r] = (now, original.mats);
                    r.sharedMaterials = original.mats;
                    _shown.Remove(r);
                }
            }
        }

        static Material MakeMaterial(Material source, Texture2D albedo, Texture2D normal)
        {
            var m = source != null ? new Material(source) : new Material(Shader.Find("Standard"));
            m.name = "Engraving_Carved";
            if (m.HasProperty("_MainTex"))
            {
                m.mainTexture = albedo;
                m.mainTextureScale = Vector2.one;
                m.mainTextureOffset = Vector2.zero;
            }
            if (m.HasProperty("_BumpMap"))
            {
                m.SetTexture("_BumpMap", normal);
                m.SetTextureScale("_BumpMap", Vector2.one);
                m.SetTextureOffset("_BumpMap", Vector2.zero);
                m.EnableKeyword("_NORMALMAP");
            }
            if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", 1f);
            return m;
        }

        void Clear(Layer layer)
        {
            layer.Token++;
            var touched = layer.Ready.Keys.ToList();
            foreach (var (_, mat) in layer.Ready.Values) if (mat != null) Destroy(mat);
            layer.Ready.Clear();
            foreach (var r in touched) Apply(r);   // back to the other side's carving, or the original
            foreach (var k in layer.TextureKeys) CarvingTextures.Release(k);
            layer.TextureKeys.Clear();
            layer.Waiting.Clear();
            layer.Started.Clear();
            layer.Carving = null;
            layer.Key = null;
            layer.Model = null;
        }

        void UpdateStatus(Layer layer)
        {
            layer.Status = $"shown on {layer.Ready.Count} renderer(s), {layer.Started.Count - layer.Ready.Count} texturing, " +
                           $"{layer.Waiting.Count} waiting (hidden); drawn in {layer.Carving?.Millis ?? 0} ms";
        }

        /// <summary>For engrave_inspect.</summary>
        internal string Describe(int side)
        {
            var layer = LayerFor(side);
            string text = GetText(side);
            return (text.Length == 0 ? "none" : $"{text.Length} chars") + $"; {layer.Status}";
        }

        /// <summary>
        /// For engrave_dump: writes what each carved renderer shows (colour and normal textures, the face's
        /// triangles drawn over the colour, and a report) to <paramref name="dir"/>. Returns the number of files.
        /// </summary>
        internal int Dump(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            int files = 0;
            var report = new System.Text.StringBuilder();
            report.AppendLine($"piece '{_prefab}' at {transform.position}");
            foreach (var layer in new[] { _front, _back })
            {
                string side = layer.Side > 0 ? "front" : "back";
                report.AppendLine($"== {side} (+Z is front): {layer.Status}");
                report.AppendLine("script:\n" + GetText(layer.Side));
                var model = layer.Model;
                if (model == null) continue;
                report.AppendLine($"model: {model.Note}; bounds x {model.Bounds.xMin:0.000}..{model.Bounds.xMax:0.000}, y {model.Bounds.yMin:0.000}..{model.Bounds.yMax:0.000}");
                if (layer.Carving?.Map != null) report.AppendLine($"heightmap {layer.Carving.Map.Width} x {layer.Carving.Map.Height}");
                int n = 0;
                foreach (var face in model.Faces)
                {
                    n++;
                    var r = face.Renderer;
                    if (r == null) continue;
                    var filter = r.GetComponent<MeshFilter>();
                    var shown = filter != null ? filter.sharedMesh : null;
                    report.AppendLine($"-- face {n}: renderer '{PathTo(r.transform)}' active {r.gameObject.activeInHierarchy} enabled {r.enabled}");
                    report.AppendLine($"   original mesh '{face.Original?.name}' readable {face.Original?.isReadable} verts {face.Original?.vertexCount}; showing '{shown?.name}'; slot {face.SourceSlot}; {face.Triangles} tris");
                    report.AppendLine($"   renderer local scale {r.transform.lossyScale}, LOD group {(r.GetComponentInParent<LODGroup>() != null)}");
                    var own = Original(r).mats;
                    var src = face.SourceSlot < own.Length ? own[face.SourceSlot] : own.FirstOrDefault();
                    if (src != null)
                    {
                        var mt = src.HasProperty("_MainTex") ? src.mainTexture : null;
                        report.AppendLine($"   material '{src.name}' shader '{src.shader?.name}' keywords [{string.Join(" ", src.shaderKeywords)}]");
                        if (mt != null) report.AppendLine($"   _MainTex '{mt.name}' {mt.width}x{mt.height} filter {mt.filterMode} wrap {mt.wrapMode} scale {src.mainTextureScale} offset {src.mainTextureOffset}");
                    }
                    for (int t = 0; t < face.Triangles; t++)
                    {
                        int b = t * 6;
                        var f = face.TriFaceUV; var o = face.TriOrigUV;
                        report.AppendLine($"   tri {t}: facing {(face.TriFacing != null ? face.TriFacing[t] : 1):0.00} depth {face.TriDepth[t]:0.000} " +
                                          $"face ({f[b]:0.000},{f[b + 1]:0.000}) ({f[b + 2]:0.000},{f[b + 3]:0.000}) ({f[b + 4]:0.000},{f[b + 5]:0.000}) " +
                                          $"orig ({o[b]:0.000},{o[b + 1]:0.000}) ({o[b + 2]:0.000},{o[b + 3]:0.000}) ({o[b + 4]:0.000},{o[b + 5]:0.000})");
                    }
                    if (!layer.Ready.TryGetValue(r, out var ready) || ready.mat == null) { report.AppendLine("   (no textures shown)"); continue; }
                    string stem = System.IO.Path.Combine(dir, $"{_prefab}_{side}_{n}_{r.name}");
                    var albedo = ready.mat.HasProperty("_MainTex") ? ready.mat.mainTexture : null;
                    var normal = ready.mat.HasProperty("_BumpMap") ? ready.mat.GetTexture("_BumpMap") : null;
                    var col = ReadBack(albedo, false);
                    if (col != null)
                    {
                        System.IO.File.WriteAllBytes(stem + "_colour.png", col.EncodeToPNG()); files++;
                        DrawTriangles(col, face);
                        System.IO.File.WriteAllBytes(stem + "_triangles.png", col.EncodeToPNG()); files++;
                        Destroy(col);
                    }
                    var nor = ReadBack(normal, true);
                    if (nor != null) { System.IO.File.WriteAllBytes(stem + "_normal.png", nor.EncodeToPNG()); files++; Destroy(nor); }
                }
            }
            report.AppendLine("== every renderer now");
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
            {
                var m = r.GetComponent<MeshFilter>()?.sharedMesh;
                report.AppendLine($"   '{PathTo(r.transform)}' active {r.gameObject.activeInHierarchy} enabled {r.enabled}: mesh '{m?.name}' " +
                                  $"({m?.subMeshCount} submeshes, readable {m?.isReadable}), materials [{string.Join(", ", r.sharedMaterials.Select(x => x != null ? x.name : "null"))}]" +
                                  (_shown.TryGetValue(r, out var want) ? $"; we gave it '{want?.name}'" : ""));
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"{_prefab}_report.txt"), report.ToString());
            return files + 1;
        }

        string PathTo(Transform t)
        {
            var parts = new List<string>();
            for (var x = t; x != null && x != transform; x = x.parent) parts.Add(x.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        static Texture2D ReadBack(Texture tex, bool linear)
        {
            if (tex == null) return null;
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32,
                linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false, linear);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        /// <summary>Outlines the face triangles: square-on ones yellow, bevels and joints red.</summary>
        static void DrawTriangles(Texture2D tex, CarveFace face)
        {
            int w = tex.width, h = tex.height;
            for (int t = 0; t < face.Triangles; t++)
            {
                var f = face.TriFaceUV;
                var c = face.TriFacing == null || face.TriFacing[t] >= 0.87f ? Color.yellow : Color.red;
                for (int k = 0; k < 3; k++)
                {
                    int a = t * 6 + k * 2, b = t * 6 + (k + 1) % 3 * 2;
                    float x0 = f[a] * w, y0 = f[a + 1] * h, x1 = f[b] * w, y1 = f[b + 1] * h;
                    int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0))) + 1;
                    for (int i = 0; i <= steps; i++)
                    {
                        int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, i / (float)steps)), y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, i / (float)steps));
                        if (x >= 0 && x < w && y >= 0 && y < h) tex.SetPixel(x, y, c);
                    }
                }
            }
            tex.Apply();
        }

        // ------------------------------------------------------------ hover and E

        string DisplayName() => _piece != null ? Localization.instance.Localize(_piece.m_name) : "";

        public string GetHoverText()
        {
            if (_nview == null || !_nview.IsValid()) return "";
            var player = Player.m_localPlayer;
            if (player == null || !Chisel.IsEquipped(player)) return "";
            string state = IsCarved ? "\n<i>carved</i>" : "";
            if (!CanEdit(false)) return Localization.instance.Localize($"{DisplayName()}{state}\n$piece_noaccess");
            return Localization.instance.Localize($"{DisplayName()}{state}\n[<color=yellow><b>$KEY_Use</b></color>] Carve this side");
        }

        public string GetHoverName() => DisplayName();

        public float GetHoverOffset() => 0f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user != Player.m_localPlayer) return false;
            return OpenEditor((Player)user);
        }

        /// <summary>Opens the carving window on the side the player stands on, if they hold a chisel.</summary>
        internal bool OpenEditor(Player player)
        {
            if (!Chisel.IsEquipped(player)) return false;
            if (!CanEdit(true)) return true;
            UI.CarvingEditor.Open(this, SideFacing(player.transform.position));
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        /// <summary>True if the piece answers E itself (a door, a gate): then Shift+E carves.</summary>
        internal bool HasOwnInteraction()
        {
            if (_ownInteraction.HasValue) return _ownInteraction.Value;
            bool own = GetComponentsInParent<Interactable>(true).Concat(GetComponentsInChildren<Interactable>(true))
                .Any(i => !ReferenceEquals(i, this));
            _ownInteraction = own;
            return own;
        }

        bool? _ownInteraction;

        // ------------------------------------------------------------ which pieces

        static HashSet<string> _carvable;

        internal static void ReloadCarvable()
        {
            _carvable = new HashSet<string>(
                EngravingPlugin.CarvablePieces.Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0),
                System.StringComparer.OrdinalIgnoreCase);
        }

        internal static bool IsCarvable(string prefab)
        {
            if (_carvable == null) ReloadCarvable();
            return _carvable.Contains(prefab);
        }
    }

    [HarmonyPatch]
    internal static class CarvedPiecePatches
    {
        [HarmonyPatch(typeof(Piece), "Awake")]
        [HarmonyPostfix]
        static void Discover(Piece __instance)
        {
            var go = __instance.gameObject;
            if (go.GetComponent<CarvedPiece>() != null) return;
            if (CarvedPiece.IsCarvable(Utils.GetPrefabName(go))) go.AddComponent<CarvedPiece>();
        }

        /// <summary>Shift+E (the game's alternative use) with a chisel equipped carves, even on pieces that use E themselves.</summary>
        [HarmonyPatch(typeof(Player), "Interact", typeof(GameObject), typeof(bool), typeof(bool))]
        [HarmonyPrefix]
        static bool AltUse(Player __instance, GameObject __0, bool __1, bool __2) // (go, hold, alt), by position
        {
            if (!__2 || __1 || __0 == null || __instance != Player.m_localPlayer) return true;
            var piece = __0.GetComponentInParent<CarvedPiece>();
            if (piece == null || !Chisel.IsEquipped(__instance)) return true;
            piece.OpenEditor(__instance);
            return false;
        }

        /// <summary>The carving hint under a door's or gate's own hover text, when E belongs to the piece.</summary>
        [HarmonyPatch(typeof(Hud), "UpdateCrosshair")]
        [HarmonyPostfix]
        static void Hint(Hud __instance, Player __0) // the hovering player, by position
        {
            var player = __0;
            if (player == null || !Chisel.IsEquipped(player)) return;
            var piece = player.GetHoverObject()?.GetComponentInParent<CarvedPiece>();
            if (piece == null || !piece.HasOwnInteraction()) return;
            var label = __instance.m_hoverName;
            if (label == null || string.IsNullOrEmpty(label.text) || label.text.Contains("Carve")) return;
            label.text += "\n[<color=yellow><b>Shift+$KEY_Use</b></color>] Carve this side";
            label.text = Localization.instance.Localize(label.text);
        }
    }
}
