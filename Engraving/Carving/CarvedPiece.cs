using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Engraving.Carving
{
    /// <summary>
    /// On every carvable build piece. The carving's script and which side it's on live in the piece's ZDO,
    /// so every client draws the same carving from the text. With a chisel equipped, E opens the editor.
    /// </summary>
    public class CarvedPiece : MonoBehaviour, Hoverable, Interactable
    {
        public const string TextKey = "engraving_text";
        public const string SideKey = "engraving_side";
        public const int MaxScriptLength = 2000;

        ZNetView _nview;
        Piece _piece;
        string _prefab;
        uint _lastRevision = uint.MaxValue;
        int _lastGeneration = -1;

        // what's currently shown
        string _shownKey;
        Carving _shown;
        CarveModel _model;
        /// <summary>Bumped on every Show/Clear, so late texture callbacks for an old carving are ignored.</summary>
        int _token;
        /// <summary>Faces whose renderer is switched off (worn/broken states); carved when they switch on.</summary>
        readonly List<CarveFace> _waiting = new List<CarveFace>();
        /// <summary>Faces with texture requests out, so they aren't asked for twice.</summary>
        readonly HashSet<CarveFace> _started = new HashSet<CarveFace>();
        readonly List<string> _textureKeys = new List<string>();
        readonly List<(CarveFace face, Material carved, Material[] before)> _applied =
            new List<(CarveFace, Material, Material[])>();

        /// <summary>What happened last, for engrave_inspect.</summary>
        internal string Status = "not carved";
        internal bool Shown => _shownKey != null;

        /// <summary>Bumped when charges or settings change, so every piece redraws.</summary>
        internal static int Generation;

        void Start()
        {
            _nview = GetComponent<ZNetView>();
            _piece = GetComponent<Piece>();
            if (_nview == null || !_nview.IsValid()) { enabled = false; return; } // placement ghost
            _prefab = Utils.GetPrefabName(gameObject);
            Refresh();
            InvokeRepeating(nameof(Refresh), 1f, 1f);
        }

        void OnDestroy() => Clear();

        public string GetText() => _nview != null && _nview.IsValid() ? _nview.GetZDO().GetString(TextKey, "") : "";
        public int GetSide() => _nview != null && _nview.IsValid() ? _nview.GetZDO().GetInt(SideKey, 1) : 1;

        /// <summary>Which side of the piece (piece-local ±Z) a world position is on.</summary>
        public int SideFacing(Vector3 worldPos) => transform.InverseTransformPoint(worldPos).z >= 0 ? 1 : -1;

        /// <summary>The face that would be carved from where this point is, for the editor's preview size.</summary>
        internal CarveModel ModelFor(int side) => CarveModel.For(gameObject, _prefab, side);

        internal bool CanEdit(bool flash) => PrivateArea.CheckAccess(transform.position, 0f, flash, false);

        public void SetCarving(string text, int side)
        {
            if (_nview == null || !_nview.IsValid() || !CanEdit(true)) return;
            text = (text ?? "").Trim();
            if (text.Length > MaxScriptLength) text = text.Substring(0, MaxScriptLength);
            _nview.ClaimOwnership();
            _nview.GetZDO().Set(TextKey, text);
            _nview.GetZDO().Set(SideKey, side);
            Jotunn.Logger.LogInfo($"[Engraving] Carving set on '{_prefab}' side {side}: {text.Length} characters.");
            Refresh();
        }

        void Refresh()
        {
            var zdo = _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;
            if (zdo == null) return;
            StartWaitingFaces();
            if (zdo.DataRevision == _lastRevision && _lastGeneration == Generation) return;
            _lastRevision = zdo.DataRevision;
            _lastGeneration = Generation;

            string text = GetText();
            int side = GetSide();
            if (string.IsNullOrWhiteSpace(text)) { Clear(); Status = "not carved"; return; }

            var model = CarveModel.For(gameObject, _prefab, side);
            if (model == null) { Clear(); Status = "no face on that side"; return; }
            float ppm = EngravingPlugin.PixelsPerMetre.Value;
            string key = CarvingJobs.KeyFor(text, model.Width, model.Height, ppm) + "|" + side + "|" + EngravingPlugin.CarveDepth.Value;
            if (key == _shownKey) return;

            Status = "drawing";
            CarvingJobs.Request(text, model.Width, model.Height, ppm, carving =>
            {
                if (this == null) return;
                if (carving.Map == null)
                {
                    Status = "drawing failed: " + string.Join("; ", carving.Diagnostics.Select(d => d.Message));
                    Jotunn.Logger.LogWarning($"[Engraving] '{_prefab}': {Status}");
                    return;
                }
                // the text may have changed again while this was drawing
                if (GetText() != text || GetSide() != side) return;
                try { Show(carving, model, key); }
                catch (System.Exception e)
                {
                    Status = "applying failed: " + e.Message;
                    Jotunn.Logger.LogWarning($"[Engraving] '{_prefab}': couldn't apply the carving: {e}");
                }
            });
        }

        void Show(Carving carving, CarveModel model, string key)
        {
            Clear();
            _model = model;
            _shown = carving;
            _shownKey = key;
            // only what can be seen now; the worn and broken models wait until the wall switches to them
            foreach (var face in model.Faces)
            {
                if (face.Renderer == null) continue;
                if (face.Renderer.gameObject.activeInHierarchy) StartFace(face);
                else _waiting.Add(face);
            }
            Status = $"texturing {_started.Count} renderer(s), {_waiting.Count} waiting (hidden); drawn in {carving.Millis} ms";
        }

        void StartWaitingFaces()
        {
            if (_waiting.Count == 0 || _shown == null) return;
            for (int i = _waiting.Count - 1; i >= 0; i--)
            {
                var face = _waiting[i];
                if (face.Renderer == null) { _waiting.RemoveAt(i); continue; }
                if (!face.Renderer.gameObject.activeInHierarchy) continue;
                _waiting.RemoveAt(i);
                StartFace(face);
            }
        }

        /// <summary>Asks for this face's textures; the mesh and material change together once both exist.</summary>
        void StartFace(CarveFace face)
        {
            if (!_started.Add(face)) return;
            var r = face.Renderer;
            var before = r.sharedMaterials;
            var source = face.SourceSlot < before.Length ? before[face.SourceSlot] : before.FirstOrDefault();
            int token = _token;
            var carving = _shown;
            Texture2D normal = null, albedo = null;

            void Ready()
            {
                if (this == null || token != _token || normal == null || albedo == null || r == null) return;
                var mat = MakeMaterial(source, albedo, normal);
                // the carved mesh has one more submesh: the face
                var now = r.sharedMaterials;
                var mats = new Material[face.FaceSlot + 1];
                for (int i = 0; i < mats.Length; i++) mats[i] = i < now.Length ? now[i] : source;
                mats[face.FaceSlot] = mat;
                r.GetComponent<MeshFilter>().sharedMesh = face.Carved;
                r.sharedMaterials = mats;
                _applied.Add((face, mat, before));
                Status = $"shown on {_applied.Count} renderer(s), {_waiting.Count} waiting (hidden); drawn in {carving.Millis} ms";
            }

            _textureKeys.Add(CarvingTextures.AcquireNormal(carving, EngravingPlugin.CarveDepth.Value, t => { normal = t; Ready(); }));
            _textureKeys.Add(CarvingTextures.AcquireAlbedo(carving, source, face, t => { albedo = t; Ready(); }));
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

        void Clear()
        {
            _token++;
            foreach (var (face, carved, before) in _applied)
            {
                if (face.Renderer != null)
                {
                    var filter = face.Renderer.GetComponent<MeshFilter>();
                    if (filter != null) filter.sharedMesh = face.Original;
                    face.Renderer.sharedMaterials = before;
                }
                if (carved != null) Destroy(carved);
            }
            foreach (var k in _textureKeys) CarvingTextures.Release(k);
            _textureKeys.Clear();
            _applied.Clear();
            _waiting.Clear();
            _started.Clear();
            _shown = null;
            _shownKey = null;
            _model = null;
        }

        // ------------------------------------------------------------ hover and E

        string DisplayName() => _piece != null ? Localization.instance.Localize(_piece.m_name) : "";

        public string GetHoverText()
        {
            if (_nview == null || !_nview.IsValid()) return "";
            var player = Player.m_localPlayer;
            if (player == null || !Chisel.IsEquipped(player)) return "";
            string state = string.IsNullOrWhiteSpace(GetText()) ? "" : "\n<i>carved</i>";
            if (!CanEdit(false)) return Localization.instance.Localize($"{DisplayName()}{state}\n$piece_noaccess");
            return Localization.instance.Localize($"{DisplayName()}{state}\n[<color=yellow><b>$KEY_Use</b></color>] Carve");
        }

        public string GetHoverName() => DisplayName();

        public float GetHoverOffset() => 0f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user != Player.m_localPlayer) return false;
            var player = (Player)user;
            if (!Chisel.IsEquipped(player)) return false;
            if (!CanEdit(true)) return true;
            UI.CarvingEditor.Open(this, SideFacing(player.transform.position));
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

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
    }
}
