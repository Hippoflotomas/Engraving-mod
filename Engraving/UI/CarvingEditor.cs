using System.Collections.Generic;
using System.Linq;
using System.Text;
using Engraving.Carving;
using Engraving.Core.Language;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

using Engraving.Pictures;

namespace Engraving.UI
{
    /// <summary>
    /// The carving window: the script on the left, a live preview on the right, problems underneath.
    /// Two tabs, as in Heraldry Mod's blazon editor: Basic (dropdowns that write the script) and Advanced (the
    /// script as free text). Both make the same text, so saving, syncing and drawing don't care which was used.
    /// </summary>
    internal class CarvingEditor : MonoBehaviour
    {
        const float PanelW = 1040, PanelH = 640;
        const float LeftX = 30, FieldY = 70, FieldW = 540, FieldH = 330;
        const float PreviewX = 600, PreviewY = 70, PreviewBox = 410;
        const float PreviewPpm = 128;
        static readonly Color Beige = new Color(0.94f, 0.87f, 0.72f);

        // basic mode layout: a label column, then the controls
        const float RowY = 70, RowStep = 38, RowH = 32, ColLabel = 0, ColMain = 112, ColEnd = FieldW;
        const int DropdownFont = 14;
        const float BasicTextY = RowY + 8 * RowStep + 22, BasicTextH = 70;
        const float AdvancedProblemsY = FieldY + FieldH + 10, BasicProblemsY = BasicTextY + BasicTextH + 6;
        const string StaleNote = "Changing a choice replaces this carving.";

        static CarvingEditor _instance;

        GameObject _panel;
        Text _title, _problems, _status, _sizeLabel, _scriptLabel;
        InputField _input;
        RectTransform _fieldRect;
        RawImage _preview;
        Texture2D _previewTex;

        // basic mode
        GameObject _basicPanel;
        Text _basicTab, _sceneTab, _advancedTab;
        bool _basicMode, _syncing;
        BasicCarving _basic = new BasicCarving();
        // the charge menu shows one group at a time (shapes, people, a biome's creatures, your pictures)
        readonly List<BasicCarving.ChargeChoice> _pictures = new List<BasicCarving.ChargeChoice>();
        List<string> _groups = new List<string>();
        List<BasicCarving.ChargeChoice> _shownCharges = new List<BasicCarving.ChargeChoice>();
        string _shownGroup, _posesFor = "-";
        List<BasicCarving.Option> _poses = new List<BasicCarving.Option>();
        // the scene tab's menus, built from the player's progress
        List<BasicCarving.Option> _subjects = new List<BasicCarving.Option>(), _weapons = new List<BasicCarving.Option>();
        SceneKind? _subjectsFor;
        GameObject _chargeRows, _sceneRows;
        Dropdown _ddGroup, _ddPose, _ddHolding, _ddFacing, _ddSceneKind, _ddSceneStyle, _ddSubject, _ddHero, _ddWeapon;
        Toggle _tGround;
        Text _subjectLabel;
        Dropdown _ddBackground, _ddBorderStyle, _ddStrands, _ddLines, _ddWidth, _ddCharge, _ddCount, _ddArrangement, _ddPosition, _ddStyle;
        Toggle _tTop, _tBottom, _tLeft, _tRight;
        InputField _inscription;

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
            _panel.SetActive(false); // Jötunn makes it active; Show turns it on once there's a face to carve

            _title = Label("Carve", LeftX, 18, 560, 40, 26, gui.ValheimOrange);

            var field = gui.CreateInputField(_panel.transform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero,
                InputField.ContentType.Standard, "border all: knotwork\ncenter: a sun", 18, FieldW, FieldH);
            _fieldRect = field.GetComponent<RectTransform>();
            Place(_fieldRect, LeftX, FieldY, FieldW, FieldH);
            _input = field.GetComponent<InputField>();
            _input.lineType = InputField.LineType.MultiLineNewline;
            _input.characterLimit = CarvedPiece.MaxScriptLength;
            _input.textComponent.alignment = TextAnchor.UpperLeft;
            _input.textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            _input.textComponent.verticalOverflow = VerticalWrapMode.Overflow;
            if (_input.placeholder is Text ph) ph.alignment = TextAnchor.UpperLeft;
            _input.onValueChanged.AddListener(_ => OnTextChanged());

            _scriptLabel = Label("The carving this makes:", LeftX, BasicTextY - 22, FieldW, 20, 14, Beige);

            _problems = Label("", LeftX, AdvancedProblemsY, FieldW, 120, 15, Color.white);
            _problems.supportRichText = true;
            _problems.alignment = TextAnchor.UpperLeft;
            _problems.verticalOverflow = VerticalWrapMode.Truncate;

            var help = Label("<color=#a0a0a0>Advanced: one line each, e.g.  background recessed  ·  border top and bottom: knotwork  ·  " +
                             "border all: runes \"text\"  ·  center: a sun  ·  top left and top right: a star, small.  " +
                             "Ctrl+Enter carves, Escape cancels.</color>",
                LeftX, PanelH - 110, PanelW - 60, 44, 14, Color.white);
            help.supportRichText = true;

            AddButton("Carve", LeftX, PanelH - 60, 150, 40, Apply);
            AddButton("Cancel", LeftX + 160, PanelH - 60, 150, 40, Hide);
            AddButton("Clear", LeftX + 320, PanelH - 60, 120, 40, Clear);
            _status = Label("", LeftX + 460, PanelH - 52, PanelW - LeftX - 490, 30, 15, Beige);
            _status.supportRichText = true;

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

            BuildBasic();
        }

        void Show(CarvedPiece piece, int side)
        {
            if (_panel == null) Build();
            _target = piece;
            _side = side;
            _model = piece.ModelFor(side);
            if (_model == null)
            {
                _target = null;
                if (_panel.activeSelf) Hide();
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "There's no flat face to carve on this side.");
                return;
            }

            _original = EngravingCharges.Visible(piece.GetText(side));
            string sideName = side > 0 ? "front" : "back";
            _title.text = $"Carve: {Localization.instance.Localize(piece.GetHoverName())} ({sideName})";
            _input.text = _original;
            _status.text = EngravingCharges.Visible(piece.GetText(-side)).Length > 0 ? $"<color=#a0a0a0>The {(side > 0 ? "back" : "front")} is carved too.</color>" : "";

            // preview box shaped like the face
            float aspect = _model.Width / _model.Height;
            float w = aspect >= 1 ? PreviewBox : PreviewBox * aspect;
            float h = aspect >= 1 ? PreviewBox / aspect : PreviewBox;
            Place(_preview.rectTransform, PreviewX + (PreviewBox - w) / 2, PreviewY + (PreviewBox - h) / 2, w, h);
            _sizeLabel.text = $"{_model.Width:0.0#} × {_model.Height:0.0#} m";

            // basic mode starts from its defaults each time (as Heraldry Mod's does); a carving already here dims it
            _basic = new BasicCarving();
            FillChargeList();
            _subjectsFor = null;   // the player may have earned more since last time
            SyncBasic();

            _panel.SetActive(true);
            if (_unblockAfterFrame >= 0) _unblockAfterFrame = -1;
            else GUIManager.BlockInput(true);
            string mode = EngravingPlugin.EditorMode.Value;
            SetMode(mode == "scene" || mode == "advanced" ? mode : "basic", false);
            if (_basicMode && _input.text.Trim().Length == 0) _input.text = _basic.Build(); // start from the default carving
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

        void Clear()
        {
            _input.text = "";
            if (_basicMode) ShowBasicStale();
        }

        void Apply()
        {
            if (_target == null) return;
            string text = (_input.text ?? "").Trim();
            var player = Player.m_localPlayer;
            bool changed = text != (_original ?? "").Trim();
            if (changed)
            {
                if (player == null || !Chisel.IsEquipped(player)) { _status.text = "Equip your chisel to carve."; return; }
                // a scene's open choices are made now, from what the carver has done, and written into the carving
                var sceneProblems = new List<string>();
                text = SceneProgressReader.Fill(text, sceneProblems);
                if (sceneProblems.Count > 0) { _status.text = $"<color=#ff8060>{sceneProblems[0]}</color>"; return; }
                var parsed = Parser.Parse(text);
                if (parsed.HasErrors) { _status.text = "<color=#ff8060>Fix the problems first.</color>"; return; }
                if (text.Length > 0 && !Chisel.Wear(player)) { _status.text = "Your chisel is too blunt."; return; }
                // pictures by fingerprint, and any pose that isn't built in, travel with the carving
                _target.SetCarving(Core.Figures.PoseLibrary.Record(EngravingCharges.WithFingerprints(text)), _side);
            }
            Hide();
        }

        void OnTextChanged()
        {
            var parsed = Parser.Parse(_input.text ?? "");
            ShowProblems(parsed.Diagnostics, null);
            if (_basicMode) ShowBasicStale();
            _renderAt = Time.unscaledTime + 0.3f;
        }

        void ShowProblems(IEnumerable<Diagnostic> parse, IEnumerable<Diagnostic> render)
        {
            var all = parse.Concat(render ?? Enumerable.Empty<Diagnostic>()).Take(6).ToList();
            var sb = new StringBuilder();
            foreach (var d in all)
                sb.AppendLine($"<color={(d.Severity == Severity.Error ? "#ff8060" : "#ffcc40")}>line {d.Line}: {d.Message}</color>");
            _problems.text = sb.ToString();
        }

        // ================================================================== basic mode

        void BuildBasic()
        {
            _basicTab = AddButton("Basic", PreviewX, 16, 120, 36, () => SetMode("basic", true));
            _sceneTab = AddButton("Scene", PreviewX + 128, 16, 120, 36, () => SetMode("scene", true));
            _advancedTab = AddButton("Advanced", PreviewX + 256, 16, 120, 36, () => SetMode("advanced", true));

            _basicPanel = new GameObject("Basic", typeof(RectTransform), typeof(CanvasGroup));
            _basicPanel.transform.SetParent(_panel.transform, false);
            Place(_basicPanel.GetComponent<RectTransform>(), 0, 0, PanelW, PanelH);
            var t = _basicPanel.transform;

            float Row(int r) => RowY + r * RowStep;
            Text RowLabel(Transform parent, int r, string text) => Label(text, LeftX + ColLabel, Row(r) + 5, ColMain - 6, RowH, 17, Beige, parent);

            // ---- shared by Basic and Scene: background and border
            RowLabel(t, 0, "Background");
            _ddBackground = MakeDropdown(t, ColMain, Row(0), 220, BasicCarving.Backgrounds, i => _basic.Background = BasicCarving.Backgrounds[i].Value);

            RowLabel(t, 1, "Border on");
            _tTop = MakeToggle(t, ColMain, Row(1), "Top", on => _basic.Top = on);
            _tBottom = MakeToggle(t, ColMain + 105, Row(1), "Bottom", on => _basic.Bottom = on);
            _tLeft = MakeToggle(t, ColMain + 225, Row(1), "Left", on => _basic.Left = on);
            _tRight = MakeToggle(t, ColMain + 320, Row(1), "Right", on => _basic.Right = on);

            RowLabel(t, 2, "Border");
            _ddBorderStyle = MakeDropdown(t, ColMain, Row(2), 150, BasicCarving.BorderStyles, i => _basic.BorderStyle = BasicCarving.BorderStyles[i].Value);
            _ddStrands = MakeDropdown(t, ColMain + 156, Row(2), 130, BasicCarving.Strands, i => _basic.StrandCount = int.Parse(BasicCarving.Strands[i].Value));
            _ddLines = MakeDropdown(t, ColMain + 156, Row(2), 130, BasicCarving.LineCounts, i => _basic.LineCount = int.Parse(BasicCarving.LineCounts[i].Value));
            _ddWidth = MakeDropdown(t, ColMain + 292, Row(2), ColEnd - ColMain - 292, BasicCarving.Widths, i => _basic.Width = BasicCarving.Widths[i].Value);

            RowLabel(t, 3, "Runes say");
            var ins = GUIManager.Instance.CreateInputField(t, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero,
                InputField.ContentType.Standard, "carved by my hand", 16, ColEnd - ColMain, RowH);
            Place(ins.GetComponent<RectTransform>(), LeftX + ColMain, Row(3), ColEnd - ColMain, RowH);
            ins.AddComponent<CanvasGroup>();
            _inscription = ins.GetComponent<InputField>();
            _inscription.characterLimit = 80;
            _inscription.lineType = InputField.LineType.SingleLine;
            _inscription.onValueChanged.AddListener(v =>
            {
                if (_syncing) return;
                _basic.Inscription = v;
                Changed();
            });

            // ---- Basic: one charge group (a shape, a figure or a picture)
            _chargeRows = Group(t, "Charge");
            var c = _chargeRows.transform;
            RowLabel(c, 4, "Carve");
            _ddGroup = MakeDropdown(c, ColMain, Row(4), 150, new BasicCarving.Option[0], i =>
            {
                if (i < 0 || i >= _groups.Count) return false;
                // a new group starts from its first entry
                if (_groups[i] != BasicCarving.GroupOf(_basic.Charge)) _basic.Charge = ChargesIn(_groups[i]).FirstOrDefault(x => !x.IsDivider) ?? BasicCarving.NoCharge;
                return true;
            });
            _ddCharge = MakeDropdown(c, ColMain + 156, Row(4), ColEnd - ColMain - 156, new BasicCarving.Option[0], i =>
            {
                if (i < 0 || i >= _shownCharges.Count || _shownCharges[i].IsDivider) return false;
                _basic.Charge = _shownCharges[i];
                return true;
            });

            RowLabel(c, 5, "How many");
            _ddCount = MakeDropdown(c, ColMain, Row(5), 120, BasicCarving.Counts, i => _basic.Count = i + 1);
            _ddArrangement = MakeDropdown(c, ColMain + 126, Row(5), 170, BasicCarving.Arrangements, i => _basic.Arrangement = BasicCarving.Arrangements[i].Value);

            RowLabel(c, 6, "Where");
            _ddPosition = MakeDropdown(c, ColMain, Row(6), 200, BasicCarving.Positions, i => _basic.Position = BasicCarving.Positions[i].Value);
            _ddStyle = MakeDropdown(c, ColMain + 206, Row(6), ColEnd - ColMain - 206, BasicCarving.Styles, i => _basic.Style = BasicCarving.Styles[i].Value);

            RowLabel(c, 7, "Figure");
            _ddPose = MakeDropdown(c, ColMain, Row(7), 130, new BasicCarving.Option[0], i =>
            {
                if (i < 0 || i >= _poses.Count) return false;
                _basic.Pose = i == 0 ? null : _poses[i].Value;
                return true;
            });
            _ddHolding = MakeDropdown(c, ColMain + 136, Row(7), 160, BasicCarving.Holdings, i => _basic.Holding = BasicCarving.Holdings[i].Value);
            _ddFacing = MakeDropdown(c, ColMain + 302, Row(7), ColEnd - ColMain - 302, BasicCarving.Facings, i => _basic.Facing = BasicCarving.Facings[i].Value);

            // ---- Scene: a Viking and a counterpart from the player's journey
            _sceneRows = Group(t, "Scene");
            var sc = _sceneRows.transform;
            RowLabel(sc, 4, "Scene of");
            _ddSceneKind = MakeDropdown(sc, ColMain, Row(4), 200, BasicCarving.SceneKinds, i =>
            {
                var kind = (SceneKind)i;
                if (kind != _basic.Scene) _basic.SceneSubject = null;   // another kind's foe doesn't carry over
                _basic.Scene = kind;
            });
            _ddSceneStyle = MakeDropdown(sc, ColMain + 206, Row(4), ColEnd - ColMain - 206, BasicCarving.Styles, i => _basic.Style = BasicCarving.Styles[i].Value);

            _subjectLabel = RowLabel(sc, 5, "Against");
            _ddSubject = MakeDropdown(sc, ColMain, Row(5), ColEnd - ColMain, new BasicCarving.Option[0], i =>
            {
                if (i < 0 || i >= _subjects.Count) return false;
                _basic.SceneSubject = _subjects[i].Value.Length == 0 ? null : _subjects[i].Value;
                return true;
            });

            RowLabel(sc, 6, "Carved by");
            _ddHero = MakeDropdown(sc, ColMain, Row(6), 200, BasicCarving.Heroes, i => _basic.Hero = BasicCarving.Heroes[i].Value);
            _ddWeapon = MakeDropdown(sc, ColMain + 206, Row(6), ColEnd - ColMain - 206, new BasicCarving.Option[0], i =>
            {
                if (i < 0 || i >= _weapons.Count) return false;
                _basic.SceneWeapon = _weapons[i].Value.Length == 0 ? null : _weapons[i].Value;
                return true;
            });

            RowLabel(sc, 7, "Ground");
            _tGround = MakeToggle(sc, ColMain, Row(7), "A line to stand on", on => _basic.Ground = on, 220);
        }

        /// <summary>A set of rows shown on one tab only.</summary>
        static GameObject Group(Transform parent, string name)
        {
            var g = new GameObject(name, typeof(RectTransform));
            g.transform.SetParent(parent, false);
            Place(g.GetComponent<RectTransform>(), 0, 0, PanelW, PanelH);
            return g;
        }

        /// <summary>A dropdown in the basic panel. <paramref name="set"/> stores the choice (dividers are refused).</summary>
        Dropdown MakeDropdown(Transform parent, float x, float y, float w, BasicCarving.Option[] options, System.Action<int> set) =>
            MakeDropdown(parent, x, y, w, options, i =>
            {
                if (options[i].IsDivider) return false;
                set(i);
                return true;
            });

        Dropdown MakeDropdown(Transform parent, float x, float y, float w, BasicCarving.Option[] options, System.Func<int, bool> set)
        {
            var go = GUIManager.Instance.CreateDropDown(parent, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, DropdownFont, w, RowH);
            Place(go.GetComponent<RectTransform>(), LeftX + x, y, w, RowH);
            go.AddComponent<CanvasGroup>();
            var dd = go.GetComponent<Dropdown>();
            dd.ClearOptions();
            dd.AddOptions(options.Select(o => o.Label).ToList());
            if (dd.captionText != null)
            {
                dd.captionText.horizontalOverflow = HorizontalWrapMode.Overflow;
                dd.captionText.resizeTextForBestFit = false;
            }
            dd.onValueChanged.AddListener(i =>
            {
                if (_syncing) return;
                if (set(i)) Changed();
                else SyncBasic(); // a refused choice (a divider) goes back
            });
            return dd;
        }

        Toggle MakeToggle(Transform parent, float x, float y, string text, System.Action<bool> set, float labelW = 70)
        {
            var go = GUIManager.Instance.CreateToggle(parent, 28, 28);
            Place(go.GetComponent<RectTransform>(), LeftX + x, y + 2, 28, 28);
            var toggle = go.GetComponent<Toggle>();
            // Jötunn's toggle may bring its own (empty) label: ours sits beside the box
            Label(text, LeftX + x + 32, y + 5, labelW, RowH, 16, Color.white, parent);
            toggle.onValueChanged.AddListener(on =>
            {
                if (_syncing) return;
                set(on);
                Changed();
            });
            return toggle;
        }

        /// <summary>A choice was made: the carving text is rebuilt from the dropdowns.</summary>
        void Changed()
        {
            _basic.Tidy();
            _input.text = _basic.Build(); // parsing, preview and Carve follow from the text
            SyncBasic();
        }

        /// <summary>Built-in shapes, the figures by group, then your pictures (they change with the server).</summary>
        void FillChargeList()
        {
            _pictures.Clear();
            foreach (var name in EngravingCharges.AvailableNames()) _pictures.Add(BasicCarving.Named(name, name));
            // item icons stay out: hundreds of them made the list unusable (Hippo, 2026-10-31); Advanced can name them
            _groups = BasicCarving.Groups(_pictures.Count > 0);
            _ddGroup.ClearOptions();
            _ddGroup.AddOptions(_groups);
            _shownGroup = null;
            if (BasicCarving.GroupOf(_basic.Charge) == BasicCarving.PicturesGroup && !_pictures.Contains(_basic.Charge))
                _basic.Charge = BasicCarving.BuiltInCharges[2];
        }

        List<BasicCarving.ChargeChoice> ChargesIn(string group)
        {
            if (group == BasicCarving.ShapesGroup) return BasicCarving.BuiltInCharges.ToList();
            if (group == BasicCarving.PicturesGroup) return _pictures;
            return BasicCarving.FigureGroups.First(g => g.group == group).figures.ToList();
        }

        /// <summary>Shows the model's choices and greys out what doesn't apply.</summary>
        void SyncBasic()
        {
            _syncing = true;
            try
            {
                Set(_ddBackground, System.Array.FindIndex(BasicCarving.Backgrounds, o => o.Value == _basic.Background), true);
                _tTop.SetIsOnWithoutNotify(_basic.Top);
                _tBottom.SetIsOnWithoutNotify(_basic.Bottom);
                _tLeft.SetIsOnWithoutNotify(_basic.Left);
                _tRight.SetIsOnWithoutNotify(_basic.Right);
                Set(_ddBorderStyle, System.Array.FindIndex(BasicCarving.BorderStyles, o => o.Value == _basic.BorderStyle), _basic.HasBorder);
                // strands and line counts share a place: show whichever applies
                bool lines = _basic.BorderStyle == "lines";
                _ddStrands.gameObject.SetActive(!lines);
                _ddLines.gameObject.SetActive(lines);
                Set(_ddStrands, _basic.StrandCount - 2, _basic.BorderHasStrands);
                Set(_ddLines, _basic.LineCount - 1, _basic.BorderHasLines);
                Set(_ddWidth, System.Array.FindIndex(BasicCarving.Widths, o => o.Value == _basic.Width), _basic.HasBorder);
                _inscription.SetTextWithoutNotify(_basic.Inscription);
                _inscription.interactable = _basic.BorderHasInscription;
                _inscription.GetComponent<CanvasGroup>().alpha = _basic.BorderHasInscription ? 1f : 0.35f;

                _chargeRows.SetActive(!_basic.SceneMode);
                _sceneRows.SetActive(_basic.SceneMode);
                if (_basic.SceneMode) SyncScene();
                else SyncCharge();
            }
            finally
            {
                _syncing = false;
            }
        }

        void SyncCharge()
        {
            // the group's list, rebuilt when the group changes
            string group = BasicCarving.GroupOf(_basic.Charge);
            if (_shownGroup != group)
            {
                _shownCharges = ChargesIn(group);
                _ddCharge.ClearOptions();
                _ddCharge.AddOptions(_shownCharges.Select(x => x.Label).ToList());
                _shownGroup = group;
            }
            Set(_ddGroup, _groups.IndexOf(group), true);
            Set(_ddCharge, Mathf.Max(0, _shownCharges.IndexOf(_basic.Charge)), true);
            Set(_ddCount, _basic.Count - 1, _basic.HasCharge);
            Set(_ddArrangement, System.Array.FindIndex(BasicCarving.Arrangements, o => o.Value == _basic.Arrangement), _basic.HasArrangement);
            Set(_ddPosition, System.Array.FindIndex(BasicCarving.Positions, o => o.Value == _basic.Position), _basic.HasCharge);
            Set(_ddStyle, System.Array.FindIndex(BasicCarving.Styles, o => o.Value == _basic.Style), _basic.HasCharge);

            // a figure's pose, what a person holds, and which way it faces
            string rig = _basic.IsFigure ? _basic.Charge.Rig : null;
            if (_posesFor != rig)
            {
                _poses = rig != null ? BasicCarving.PosesFor(rig) : new List<BasicCarving.Option> { new BasicCarving.Option("Pose", "") };
                _ddPose.ClearOptions();
                _ddPose.AddOptions(_poses.Select(o => o.Label).ToList());
                _posesFor = rig;
            }
            Set(_ddPose, Mathf.Max(0, _poses.FindIndex(o => o.Value == _basic.Pose)), rig != null);
            Set(_ddHolding, Mathf.Max(0, System.Array.FindIndex(BasicCarving.Holdings, o => o.Value == _basic.Holding)), _basic.CanHold);
            Set(_ddFacing, _basic.Facing == "left" ? 1 : 0, rig != null);
        }

        void SyncScene()
        {
            var kind = _basic.Scene;
            if (_subjectsFor != kind)
            {
                var progress = SceneProgressReader.Read(Player.m_localPlayer);
                _subjects = BasicCarving.SceneSubjects(kind, progress);
                _ddSubject.ClearOptions();
                _ddSubject.AddOptions(_subjects.Select(o => o.Label).ToList());
                _weapons = BasicCarving.SceneWeapons(progress);
                _ddWeapon.ClearOptions();
                _ddWeapon.AddOptions(_weapons.Select(o => o.Label).ToList());
                _subjectsFor = kind;
            }
            int at = _subjects.FindIndex(o => o.Value == (_basic.SceneSubject ?? ""));
            if (at < 0) { _basic.SceneSubject = null; at = 0; }
            int w = _weapons.FindIndex(o => o.Value == (_basic.SceneWeapon ?? ""));
            if (w < 0) { _basic.SceneWeapon = null; w = 0; }
            _subjectLabel.text = kind == SceneKind.Gathering ? "Gathering" : kind == SceneKind.Victory ? "Over" : kind == SceneKind.Hunt ? "Hunting" : "Against";
            Set(_ddSceneKind, (int)kind, true);
            Set(_ddSceneStyle, System.Array.FindIndex(BasicCarving.Styles, o => o.Value == _basic.Style), true);
            Set(_ddSubject, at, true);
            Set(_ddHero, _basic.Hero == "shieldmaiden" ? 1 : 0, true);
            // the victor holds nothing up but the trophy
            Set(_ddWeapon, w, kind != SceneKind.Victory);
            _tGround.SetIsOnWithoutNotify(_basic.Ground);
        }

        /// <summary>
        /// When the carving in the box didn't come from the dropdowns (written in Advanced, or already on the wall),
        /// the dropdowns are dimmed: they don't describe it, and touching one replaces it (as in Heraldry Mod).
        /// </summary>
        void ShowBasicStale()
        {
            string current = (_input.text ?? "").Trim();
            bool stale = current.Length > 0 && current != _basic.Build();
            _basicPanel.GetComponent<CanvasGroup>().alpha = stale ? 0.45f : 1f;
            if (stale && _status.text.Length == 0) _status.text = StaleNote;
            else if (!stale && _status.text == StaleNote) _status.text = "";
        }

        static void Set(Dropdown dd, int index, bool enabled)
        {
            dd.SetValueWithoutNotify(Mathf.Max(0, index));
            dd.RefreshShownValue();
            dd.interactable = enabled;
            dd.GetComponent<CanvasGroup>().alpha = enabled ? 1f : 0.35f;
        }

        /// <summary>
        /// Basic and Scene: the dropdowns, with the carving text they make shown read-only underneath (so it teaches the
        /// words). Advanced: the text box, editable. All three make the same kind of text.
        /// </summary>
        void SetMode(string mode, bool remember)
        {
            bool dropdowns = mode != "advanced";
            if (remember) EngravingPlugin.EditorMode.Value = mode;
            // between Basic and Scene, a carving the dropdowns made is remade for the other tab
            string current = (_input.text ?? "").Trim();
            bool fromDropdowns = current.Length == 0 || current == _basic.Build();
            _basic.SceneMode = mode == "scene";
            _basicMode = dropdowns;
            _basicPanel.SetActive(dropdowns);
            _scriptLabel.gameObject.SetActive(dropdowns);
            _input.readOnly = dropdowns;
            if (dropdowns)
            {
                SyncBasic();
                if (fromDropdowns) _input.text = _basic.Build();
                Place(_fieldRect, LeftX, BasicTextY, FieldW, BasicTextH);
                Place(_problems.rectTransform, LeftX, BasicProblemsY, FieldW, PanelH - 112 - BasicProblemsY);
                _input.DeactivateInputField();
                ShowBasicStale();
            }
            else
            {
                Place(_fieldRect, LeftX, FieldY, FieldW, FieldH);
                Place(_problems.rectTransform, LeftX, AdvancedProblemsY, FieldW, 120);
                if (_status.text == StaleNote) _status.text = "";
                _input.ActivateInputField();
                _input.MoveTextEnd(false);
            }
            var on = GUIManager.Instance.ValheimOrange;
            _basicTab.color = mode == "basic" ? on : Color.white;
            _sceneTab.color = mode == "scene" ? on : Color.white;
            _advancedTab.color = mode == "advanced" ? on : Color.white;
        }

        // ================================================================== per frame

        void Update()
        {
            if (_unblockAfterFrame >= 0 && Time.frameCount > _unblockAfterFrame)
            {
                _unblockAfterFrame = -1;
                GUIManager.BlockInput(false);
            }
            if (_panel == null || !_panel.activeSelf) return;
            if (_target == null || _model == null) { Hide(); return; }   // destroyed while editing

            if (ZInput.GetKeyDown(KeyCode.Escape, false)) { Hide(); return; }
            bool ctrl = ZInput.GetKey(KeyCode.LeftControl, false) || ZInput.GetKey(KeyCode.RightControl, false);
            if (ctrl && (ZInput.GetKeyDown(KeyCode.Return, false) || ZInput.GetKeyDown(KeyCode.KeypadEnter, false))) { Apply(); return; }

            if (_renderAt >= 0 && Time.unscaledTime >= _renderAt)
            {
                _renderAt = -1;
                // drawn as it will be stored: with the player's own poses recorded
                var sceneProblems = new List<string>();
                string text = Core.Figures.PoseLibrary.Record(SceneProgressReader.Fill(_input.text ?? "", sceneProblems));
                _previewWanted = text;
                var pictures = EngravingCharges.Prepare(text, out int waiting);
                ItemIconCharges.Prepare(text, out int iconsWaiting);
                // item icons read back in a frame or two: look again then
                if (iconsWaiting > 0) _renderAt = Time.unscaledTime + 0.3f;
                CarvingJobs.Request(text, _model.Width, _model.Height, PreviewPpm, pictures, c =>
                {
                    if (!IsOpen || _previewWanted != text || c.Map == null) return;
                    CarvingTextures.Preview(c, _previewTex);
                    _preview.texture = _previewTex;
                    var parsed = Parser.Parse(text);
                    var render = c.Diagnostics.Where(d => !parsed.Diagnostics.Any(p => p.Message == d.Message)).ToList();
                    foreach (var sp in sceneProblems) render.Insert(0, new Diagnostic(Severity.Error, 0, sp));
                    if (waiting > 0)
                        render.Insert(0, new Diagnostic(Severity.Warning, 0, $"{waiting} picture(s) not on this computer yet (see engrave_pending)"));
                    ShowProblems(parsed.Diagnostics, render);
                });
            }
        }

        // ------------------------------------------------------------ helpers (as in Heraldry Mod)

        Text Label(string text, float x, float y, float w, float h, int size, Color colour, Transform parent = null)
        {
            var gui = GUIManager.Instance;
            var go = gui.CreateText(text, parent ?? _panel.transform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero,
                gui.AveriaSerifBold, size, colour, true, Color.black, w, h, false);
            Place(go.GetComponent<RectTransform>(), x, y, w, h);
            var label = go.GetComponent<Text>();
            label.raycastTarget = false;
            return label;
        }

        /// <summary>A button; returns its label, so tabs can be coloured.</summary>
        Text AddButton(string text, float x, float y, float w, float h, UnityEngine.Events.UnityAction onClick)
        {
            var go = GUIManager.Instance.CreateButton(text, _panel.transform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, w, h);
            Place(go.GetComponent<RectTransform>(), x, y, w, h);
            go.GetComponent<Button>().onClick.AddListener(onClick);
            return go.GetComponentInChildren<Text>();
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
