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
        readonly List<(CarveFace face, Material source, Material carved, Material[] before, float depth)> _applied =
            new List<(CarveFace, Material, Material, Material[], float)>();

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
            Refresh();
        }

        void Refresh()
        {
            var zdo = _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;
            if (zdo == null) return;
            if (zdo.DataRevision == _lastRevision && _lastGeneration == Generation) return;
            _lastRevision = zdo.DataRevision;
            _lastGeneration = Generation;

            string text = GetText();
            int side = GetSide();
            if (string.IsNullOrWhiteSpace(text)) { Clear(); return; }

            var model = CarveModel.For(gameObject, _prefab, side);
            if (model == null) { Clear(); return; }
            float ppm = EngravingPlugin.PixelsPerMetre.Value;
            string key = CarvingJobs.KeyFor(text, model.Width, model.Height, ppm) + "|" + side + "|" + EngravingPlugin.CarveDepth.Value;
            if (key == _shownKey) return;

            CarvingJobs.Request(text, model.Width, model.Height, ppm, carving =>
            {
                if (this == null || carving.Map == null) return;
                // the text may have changed again while this was drawing
                if (GetText() != text || GetSide() != side) return;
                Show(carving, model, key);
            });
        }

        void Show(Carving carving, CarveModel model, string key)
        {
            Clear();
            _model = model;
            _shown = carving;
            _shownKey = key;
            foreach (var face in model.Faces)
            {
                var r = face.Renderer;
                if (r == null) continue;
                var filter = r.GetComponent<MeshFilter>();
                var before = r.sharedMaterials;
                var source = face.SourceSlot < before.Length ? before[face.SourceSlot] : before.FirstOrDefault();
                float depth = EngravingPlugin.CarveDepth.Value;
                var set = CarvingTextures.Acquire(carving, source, face, depth);
                var mat = MakeMaterial(source, set);

                // the carved mesh has one more submesh: the face
                var mats = new Material[face.FaceSlot + 1];
                for (int i = 0; i < mats.Length; i++) mats[i] = i < before.Length ? before[i] : source;
                mats[face.FaceSlot] = mat;
                filter.sharedMesh = face.Carved;
                r.sharedMaterials = mats;
                _applied.Add((face, source, mat, before, depth));
            }
        }

        static Material MakeMaterial(Material source, CarvingTextures.Set set)
        {
            var m = source != null ? new Material(source) : new Material(Shader.Find("Standard"));
            m.name = "Engraving_Carved";
            if (m.HasProperty("_MainTex"))
            {
                m.mainTexture = set.Albedo;
                m.mainTextureScale = Vector2.one;
                m.mainTextureOffset = Vector2.zero;
            }
            if (m.HasProperty("_BumpMap"))
            {
                m.SetTexture("_BumpMap", set.Normal);
                m.SetTextureScale("_BumpMap", Vector2.one);
                m.SetTextureOffset("_BumpMap", Vector2.zero);
                m.EnableKeyword("_NORMALMAP");
            }
            if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", 1f);
            return m;
        }

        void Clear()
        {
            foreach (var (face, source, carved, before, depth) in _applied)
            {
                if (face.Renderer != null)
                {
                    var filter = face.Renderer.GetComponent<MeshFilter>();
                    if (filter != null) filter.sharedMesh = face.Original;
                    face.Renderer.sharedMaterials = before;
                }
                if (_shown != null) CarvingTextures.Release(_shown, source, face, depth);
                if (carved != null) Destroy(carved);
            }
            _applied.Clear();
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
