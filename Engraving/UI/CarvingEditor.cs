using System.Linq;
using System.Text;
using Engraving.Carving;
using Engraving.Core.Language;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace Engraving.UI
{
    /// <summary>
    /// The carving window: the script on the left, a live preview on the right, problems underneath.
    /// Built with Jötunn's GUI helpers in the same way as Heraldry Mod's blazon editor.
    /// </summary>
    internal class CarvingEditor : MonoBehaviour
    {
        const float PanelW = 1040, PanelH = 640;
        const float LeftX = 30, FieldY = 70, FieldW = 540, FieldH = 330;
        const float PreviewX = 600, PreviewY = 70, PreviewBox = 410;
        const float PreviewPpm = 128;
        static readonly Color Beige = new Color(0.94f, 0.87f, 0.72f);

        static CarvingEditor _instance;

        GameObject _panel;
        Text _title, _problems, _status, _sizeLabel;
        InputField _input;
        RawImage _preview;
        Texture2D _previewTex;

        CarvedPiece _target;
        int _side;
        CarveModel _model;
        string _original;
        float _renderAt = -1;
        string _previewWanted;
        int _unblockAfterFrame = -1;

        public static bool IsOpen => _instance != null && _instance._panel != null && _instance._panel.activeSelf;

        public static void Open(CarvedPiece piece, int side)
        {
            if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null) return;
            if (_instance == null)
            {
                var go = new GameObject("EngravingCarvingEditor");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<CarvingEditor>();
            }
            _instance.Show(piece, side);
        }

        void Build()
        {
            var gui = GUIManager.Instance;
            _panel = gui.CreateWoodpanel(GUIManager.CustomGUIFront.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, PanelW, PanelH, true);
            _panel.name = "EngravingCarvingEditor";

            _title = Label("Carve", LeftX, 18, 600, 40, 26, gui.ValheimOrange);

            var field = gui.CreateInputField(_panel.transform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero,
                InputField.ContentType.Standard, "border all: knotwork\ncenter: a sun", 18, FieldW, FieldH);
            Place(field.GetComponent<RectTransform>(), LeftX, FieldY, FieldW, FieldH);
            _input = field.GetComponent<InputField>();
            _input.lineType = InputField.LineType.MultiLineNewline;
            _input.characterLimit = CarvedPiece.MaxScriptLength;
            _input.textComponent.alignment = TextAnchor.UpperLeft;
            _input.textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            _input.textComponent.verticalOverflow = VerticalWrapMode.Overflow;
            if (_input.placeholder is Text ph) ph.alignment = TextAnchor.UpperLeft;
            _input.onValueChanged.AddListener(_ => OnTextChanged());

            _problems = Label("", LeftX, FieldY + FieldH + 10, FieldW, 120, 15, Color.white);
            _problems.supportRichText = true;
            _problems.alignment = TextAnchor.UpperLeft;
            _problems.verticalOverflow = VerticalWrapMode.Truncate;

            var help = Label("<color=#a0a0a0>One line each:  background recessed  ·  border top and bottom: knotwork  ·  " +
                             "border all: runes \"text\"  ·  center: a sun  ·  top left and top right: a star, small.  " +
                             "Ctrl+Enter carves, Escape cancels.</color>",
                LeftX, PanelH - 110, PanelW - 60, 44, 14, Color.white);
            help.supportRichText = true;

            AddButton("Carve", LeftX, PanelH - 60, 150, 40, Apply);
            AddButton("Cancel", LeftX + 160, PanelH - 60, 150, 40, Hide);
            AddButton("Clear", LeftX + 320, PanelH - 60, 120, 40, () => _input.text = "");
            _status = Label("", LeftX + 460, PanelH - 52, PanelW - LeftX - 490, 30, 15, Beige);

            var frame = new GameObject("PreviewFrame", typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(_panel.transform, false);
            frame.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);
            frame.GetComponent<Image>().raycastTarget = false;
            Place(frame.GetComponent<RectTransform>(), PreviewX - 8, PreviewY - 8, PreviewBox + 16, PreviewBox + 16);
            var pv = new GameObject("Preview", typeof(RectTransform), typeof(RawImage));
            pv.transform.SetParent(_panel.transform, false);
            _preview = pv.GetComponent<RawImage>();
            _preview.raycastTarget = false;
            _previewTex = new Texture2D(16, 16, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            _preview.texture = _previewTex;
            _sizeLabel = Label("", PreviewX, PreviewY + PreviewBox + 14, PreviewBox, 24, 15, Beige);
            _sizeLabel.alignment = TextAnchor.UpperCenter;
        }

        void Show(CarvedPiece piece, int side)
        {
            if (_panel == null) Build();
            _target = piece;
            _side = side;
            _model = piece.ModelFor(side);
            if (_model == null)
            {
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "There's no flat face to carve on this side.");
                return;
            }

            // a carving on the other side is replaced when this side is carved
            bool sameSide = piece.GetSide() == side;
            _original = sameSide ? piece.GetText() : "";
            _title.text = $"Carve: {Localization.instance.Localize(piece.GetHoverName())}";
            _input.text = _original;
            if (!sameSide && piece.GetText().Length > 0)
                _status.text = "<color=#ffcc40>The other side is carved; carving this side smooths that one.</color>";
            else
                _status.text = "";
            _status.supportRichText = true;

            // preview box shaped like the face
            float aspect = _model.Width / _model.Height;
            float w = aspect >= 1 ? PreviewBox : PreviewBox * aspect;
            float h = aspect >= 1 ? PreviewBox / aspect : PreviewBox;
            Place(_preview.rectTransform, PreviewX + (PreviewBox - w) / 2, PreviewY + (PreviewBox - h) / 2, w, h);
            _sizeLabel.text = $"{_model.Width:0.0#} × {_model.Height:0.0#} m";

            _panel.SetActive(true);
            if (_unblockAfterFrame >= 0) _unblockAfterFrame = -1;
            else GUIManager.BlockInput(true);
            _input.ActivateInputField();
            _input.MoveTextEnd(false);
            OnTextChanged();
            _renderAt = 0;
        }

        void Hide()
        {
            if (_panel == null || !_panel.activeSelf) return;
            _panel.SetActive(false);
            _target = null;
            // release next frame so the Escape that closed us can't also open the menu
            _unblockAfterFrame = Time.frameCount;
        }

        void Apply()
        {
            if (_target == null) return;
            string text = (_input.text ?? "").Trim();
            var player = Player.m_localPlayer;
            bool changed = text != (_original ?? "").Trim() || _target.GetSide() != _side;
            if (changed)
            {
                if (player == null || !Chisel.IsEquipped(player)) { _status.text = "Equip your chisel to carve."; return; }
                var parsed = Parser.Parse(text);
                if (parsed.HasErrors) { _status.text = "<color=#ff8060>Fix the problems first.</color>"; return; }
                if (text.Length > 0 && !Chisel.Wear(player)) { _status.text = "Your chisel is too blunt."; return; }
                _target.SetCarving(text, _side);
            }
            Hide();
        }

        void OnTextChanged()
        {
            var parsed = Parser.Parse(_input.text ?? "");
            ShowProblems(parsed.Diagnostics, null);
            _renderAt = Time.unscaledTime + 0.3f;
        }

        void ShowProblems(System.Collections.Generic.IEnumerable<Diagnostic> parse, System.Collections.Generic.IEnumerable<Diagnostic> render)
        {
            var all = parse.Concat(render ?? Enumerable.Empty<Diagnostic>()).Take(6).ToList();
            var sb = new StringBuilder();
            foreach (var d in all)
                sb.AppendLine($"<color={(d.Severity == Severity.Error ? "#ff8060" : "#ffcc40")}>line {d.Line}: {d.Message}</color>");
            _problems.text = sb.ToString();
        }

        void Update()
        {
            if (_unblockAfterFrame >= 0 && Time.frameCount > _unblockAfterFrame)
            {
                _unblockAfterFrame = -1;
                GUIManager.BlockInput(false);
            }
            if (_panel == null || !_panel.activeSelf) return;
            if (_target == null) { Hide(); return; }   // destroyed while editing

            if (ZInput.GetKeyDown(KeyCode.Escape, false)) { Hide(); return; }
            bool ctrl = ZInput.GetKey(KeyCode.LeftControl, false) || ZInput.GetKey(KeyCode.RightControl, false);
            if (ctrl && (ZInput.GetKeyDown(KeyCode.Return, false) || ZInput.GetKeyDown(KeyCode.KeypadEnter, false))) { Apply(); return; }

            if (_renderAt >= 0 && Time.unscaledTime >= _renderAt)
            {
                _renderAt = -1;
                string text = _input.text ?? "";
                _previewWanted = text;
                CarvingJobs.Request(text, _model.Width, _model.Height, PreviewPpm, c =>
                {
                    if (!IsOpen || _previewWanted != text || c.Map == null) return;
                    CarvingTextures.Preview(c, _previewTex);
                    _preview.texture = _previewTex;
                    var parsed = Parser.Parse(text);
                    ShowProblems(parsed.Diagnostics, c.Diagnostics.Where(d => !parsed.Diagnostics.Any(p => p.Message == d.Message)));
                });
            }
        }

        // ------------------------------------------------------------ helpers (as in Heraldry Mod)

        Text Label(string text, float x, float y, float w, float h, int size, Color colour)
        {
            var gui = GUIManager.Instance;
            var go = gui.CreateText(text, _panel.transform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero,
                gui.AveriaSerifBold, size, colour, true, Color.black, w, h, false);
            Place(go.GetComponent<RectTransform>(), x, y, w, h);
            var label = go.GetComponent<Text>();
            label.raycastTarget = false;
            return label;
        }

        void AddButton(string text, float x, float y, float w, float h, UnityEngine.Events.UnityAction onClick)
        {
            var go = GUIManager.Instance.CreateButton(text, _panel.transform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, w, h);
            Place(go.GetComponent<RectTransform>(), x, y, w, h);
            go.GetComponent<Button>().onClick.AddListener(onClick);
        }

        static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -y);
        }
    }

    /// <summary>
    /// Keeps the player still while the carving window is open. Jötunn's GUIManager.BlockInput alone let movement
    /// through in testing (2026-10-29), so the window also tells the game a text box is open, as a sign's does:
    /// movement, hotkeys and mouse capture all check TextInput.IsVisible.
    /// </summary>
    [HarmonyPatch]
    internal static class CarvingEditorInputPatches
    {
        [HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
        [HarmonyPostfix]
        static void TextBoxOpen(ref bool __result)
        {
            if (CarvingEditor.IsOpen) __result = true;
        }

        [HarmonyPatch(typeof(PlayerController), "TakeInput")]
        [HarmonyPostfix]
        static void NoControllerInput(ref bool __result)
        {
            if (CarvingEditor.IsOpen) __result = false;
        }

        [HarmonyPatch(typeof(Player), "TakeInput")]
        [HarmonyPostfix]
        static void NoPlayerInput(ref bool __result)
        {
            if (CarvingEditor.IsOpen) __result = false;
        }
    }
}
