using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Samirin33.Editor;
using Object = UnityEngine.Object;

namespace SamiVRCBlocksAvatar.Editor
{
    /// <summary>
    /// MA Menu Item と VRC Expressions Menu の表示名を、TMPro リッチテキストとして編集・プレビューします。
    /// 本文の編集は UI Toolkit の TextField で行い、選択範囲を常時追跡します。
    /// </summary>
    public class MenuRichTextEditorWindow : EditorWindow
    {
        const string MenuPath = "SBAvatarEditor/Menu/Rich Text Editor";
        const string PrefsAutoApply = "SamiVRCBlocksAvatar.MenuRichText.AutoApply";
        const string PrefsFollowSelection = "SamiVRCBlocksAvatar.MenuRichText.FollowSelection";
        const string PrefsBg = "SamiVRCBlocksAvatar.MenuRichText.PreviewBg";
        const string PrefsSize = "SamiVRCBlocksAvatar.MenuRichText.SizePercent";
        const string PrefsColor = "SamiVRCBlocksAvatar.MenuRichText.Color";
        const string PrefsMarkColor = "SamiVRCBlocksAvatar.MenuRichText.MarkColor";
        const string PrefsVOffset = "SamiVRCBlocksAvatar.MenuRichText.VOffset";
        const string PrefsSidebarWidth = "SamiVRCBlocksAvatar.MenuRichText.SidebarWidth";
        const string PrefsGradient = "SamiVRCBlocksAvatar.MenuRichText.Gradient";
        const string PrefsColorHistory = "SamiVRCBlocksAvatar.MenuRichText.ColorHistory";
        const int ColorHistoryMax = 10;
        const float PreviewHeightFixed = 100f;
        const float BodyHeightFixed = 100f;
        const float SidebarWidthMin = 180f;
        const float SidebarWidthDefault = 240f;
        const int SelectionPollIntervalMs = 33;

        static readonly Color[] Palette =
        {
            Color.white,
            new Color(1f, 0.32f, 0.32f),
            new Color(1f, 0.62f, 0.2f),
            new Color(1f, 0.86f, 0.2f),
            new Color(0.45f, 0.92f, 0.42f),
            new Color(0.3f, 0.85f, 0.95f),
            new Color(0.38f, 0.55f, 1f),
            new Color(0.78f, 0.45f, 1f),
            new Color(1f, 0.45f, 0.78f),
            new Color(1f, 0.84f, 0.2f),
            new Color(0.75f, 0.75f, 0.78f),
            new Color(0.2f, 0.2f, 0.22f)
        };

        static readonly Color VrcMenuBackground = new Color(0.10f, 0.12f, 0.18f, 1f);

        const string UndoName = "Menu Rich Text";

        [Serializable]
        class DraftUndoState : ScriptableObject
        {
            public string text = "";
            public int selStart;
            public int selEnd;
        }

        readonly List<MenuRichTextUtility.Target> _targets = new List<MenuRichTextUtility.Target>();
        readonly List<Color> _colorHistory = new List<Color>();
        MenuRichTextTmpPreview _preview;
        DraftUndoState _draftUndo;
        Vector2 _listScroll;
        int _selectedIndex;
        string _text = "";
        string _filter = "";
        string _status = "";
        MessageType _statusType = MessageType.Info;
        bool _autoApply = true;
        bool _followSelection = true;
        bool _dirty;
        bool _suppressUndo;
        bool _lockApplying;
        bool _cheatFoldout;
        int _selStart;
        int _selEnd;
        int _keptStart;
        int _keptEnd;
        Color _editColor = Color.white;
        string _hex = "FFFFFF";
        Color _markColor = new Color(1f, 0.878f, 0.4f, 1f);
        int _sizePercent = 100;
        float _vOffsetEm;
        Gradient _gradient;
        MenuRichTextUtility.SelectionFormat _selectionFormat;
        int _syncedSelStart = -1;
        int _syncedSelEnd = -1;
        string _syncedFormatText;
        Color _previewBg = VrcMenuBackground;
        float _sidebarWidth = SidebarWidthDefault;
        Object _sourceObject;
        GUIStyle _panelStyle;

        TextField _bodyField;
        VisualElement _leftPane;
        IMGUIContainer _listGui;
        IMGUIContainer _formatGui;
        IMGUIContainer _statusGui;
        bool _suppressFieldCallback;
        bool _bodyFocused;
        bool _draggingSidebar;
        int _pendingSelStart;
        int _pendingSelEnd;
        bool _pendingFocus;
        bool _pendingSizeApply;
        bool _pendingVOffsetApply;
        bool _pendingColorApply;
        bool _pendingMarkApply;

        [MenuItem(MenuPath, false, 8)]
        public static void Open()
        {
            var w = GetWindow<MenuRichTextEditorWindow>(false, "Menu Rich Text", true);
            w.minSize = new Vector2(720, 520);
            w.CollectFromCurrentSelection(true);
        }

        [MenuItem("CONTEXT/ModularAvatarMenuItem/SBAvatarEditor/Rich Text を編集")]
        static void OpenFromMaMenuItem(MenuCommand command)
        {
            OpenWith(command.context);
        }

        [MenuItem("CONTEXT/VRCExpressionsMenu/SBAvatarEditor/Rich Text を編集")]
        static void OpenFromVrcMenu(MenuCommand command)
        {
            OpenWith(command.context);
        }

        [MenuItem("Assets/SBAvatarEditor/Edit Menu Rich Text", true)]
        static bool OpenFromAssetsValidate()
        {
            return MenuRichTextUtility.IsVrcExpressionsMenu(Selection.activeObject);
        }

        [MenuItem("Assets/SBAvatarEditor/Edit Menu Rich Text", false, 220)]
        static void OpenFromAssets()
        {
            OpenWith(Selection.activeObject);
        }

        public static void OpenWith(Object obj)
        {
            Open();
            var w = GetWindow<MenuRichTextEditorWindow>();
            w._followSelection = false;
            EditorPrefs.SetBool(PrefsFollowSelection, false);
            w.LoadFromObject(obj);
        }

        void OnEnable()
        {
            _autoApply = EditorPrefs.GetBool(PrefsAutoApply, true);
            _followSelection = EditorPrefs.GetBool(PrefsFollowSelection, true);
            _sidebarWidth = EditorPrefs.GetFloat(PrefsSidebarWidth, SidebarWidthDefault);
            _sizePercent = EditorPrefs.GetInt(PrefsSize, 100);
            _vOffsetEm = EditorPrefs.GetFloat(PrefsVOffset, 0f);
            _editColor = MenuRichTextUtility.ParseColorOr(EditorPrefs.GetString(PrefsColor, "FFFFFF"), Color.white);
            _hex = MenuRichTextUtility.ToHtmlColor(_editColor);
            _markColor = MenuRichTextUtility.ParseMarkColorOr(
                EditorPrefs.GetString(PrefsMarkColor, "FFE066"),
                new Color(1f, 0.878f, 0.4f, 1f));
            LoadGradientPrefs();
            LoadColorHistory();
            var bgHtml = EditorPrefs.GetString(PrefsBg, "");
            _previewBg = string.IsNullOrEmpty(bgHtml) ? VrcMenuBackground : MenuRichTextUtility.ParseColorOr(bgHtml, VrcMenuBackground);
            _preview = new MenuRichTextTmpPreview();
            EnsureDraftUndoState();
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            CollectFromCurrentSelection(false);
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            _preview?.Dispose();
            _preview = null;
            DestroyDraftUndoState();
        }

        void OnDestroy()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            _preview?.Dispose();
            _preview = null;
            DestroyDraftUndoState();
        }

        // ---------------------------------------------------------------- UI 構築

        void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.flexDirection = FlexDirection.Column;
            root.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
            SamirinEditorStyleHelper.ApplyCustomFont(root);

            root.Add(CreateImguiPanel(() => SamirinEditorStyleHelper.DrawWithBlueBackground(DrawTopBar)));

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexGrow = 1f;
            row.style.minHeight = 0f;
            root.Add(row);

            _leftPane = new VisualElement();
            _leftPane.style.width = _sidebarWidth;
            _leftPane.style.flexShrink = 0f;
            _leftPane.style.flexDirection = FlexDirection.Column;
            row.Add(_leftPane);

            _listGui = CreateImguiPanel(DrawObjectListGui);
            _listGui.style.flexGrow = 1f;
            _listGui.style.minHeight = 0f;
            _leftPane.Add(_listGui);

            row.Add(CreateSidebarHandle());

            var rightScroll = new ScrollView(ScrollViewMode.Vertical);
            rightScroll.style.flexGrow = 1f;
            rightScroll.style.minWidth = 320f;
            row.Add(rightScroll);

            rightScroll.Add(CreateImguiPanel(DrawPreviewPanel));
            rightScroll.Add(CreateBodyPanel());

            _formatGui = CreateImguiPanel(DrawFormatPanel);
            rightScroll.Add(_formatGui);

            _statusGui = CreateImguiPanel(DrawStatus);
            root.Add(_statusGui);
        }

        static IMGUIContainer CreateImguiPanel(Action onGui)
        {
            // IMGUI のテキスト入力やスライダーを操作できるようにフォーカスを許可する
            return new IMGUIContainer(onGui) { focusable = true };
        }

        VisualElement CreateSidebarHandle()
        {
            var handle = new VisualElement();
            handle.style.width = 5f;
            handle.style.flexShrink = 0f;
            handle.style.backgroundColor = new Color(0f, 0f, 0f, 0.25f);
            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                _draggingSidebar = true;
                handle.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_draggingSidebar || _leftPane == null)
                    return;
                var max = Mathf.Max(SidebarWidthMin, position.width - 360f);
                _sidebarWidth = Mathf.Clamp(e.position.x - _leftPane.worldBound.xMin, SidebarWidthMin, max);
                _leftPane.style.width = _sidebarWidth;
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!_draggingSidebar)
                    return;
                _draggingSidebar = false;
                handle.ReleasePointer(e.pointerId);
                EditorPrefs.SetFloat(PrefsSidebarWidth, _sidebarWidth);
                e.StopPropagation();
            });
            return handle;
        }

        VisualElement CreateBodyPanel()
        {
            var panel = new VisualElement();
            panel.style.flexDirection = FlexDirection.Column;
            panel.style.marginTop = 4f;
            panel.style.marginBottom = 4f;

            panel.Add(CreateImguiPanel(DrawBodyHeader));

            _bodyField = new TextField
            {
                multiline = true,
                isDelayed = false
            };
            _bodyField.style.height = BodyHeightFixed;
            _bodyField.style.flexShrink = 0f;
            _bodyField.style.marginLeft = 4f;
            _bodyField.style.marginRight = 4f;
            _bodyField.style.fontSize = 13f;
            _bodyField.SetValueWithoutNotify(_text ?? "");
            MenuRichTextFieldCompat.SetupPlainMultiline(_bodyField);
            ApplyBodyFieldFont();
            _bodyField.RegisterCallback<AttachToPanelEvent>(_ => ApplyBodyFieldFont());
            _bodyField.RegisterValueChangedCallback(OnBodyFieldChanged);
            _bodyField.RegisterCallback<FocusInEvent>(_ => _bodyFocused = true);
            _bodyField.RegisterCallback<FocusOutEvent>(_ => _bodyFocused = false);
            // 本文をクリックしたら保持選択をいったん捨て、ドラッグで付け直す
            _bodyField.RegisterCallback<PointerDownEvent>(_ =>
            {
                _keptStart = 0;
                _keptEnd = 0;
            }, TrickleDown.TrickleDown);
            _bodyField.schedule.Execute(PollFieldSelection).Every(SelectionPollIntervalMs);
            _bodyField.SetEnabled(CurrentTarget() != null);
            panel.Add(_bodyField);

            panel.Add(CreateImguiPanel(DrawBodyFooter));

            return panel;
        }

        void ApplyBodyFieldFont()
        {
            if (_bodyField == null)
                return;

            SamirinEditorStyleHelper.ApplyCustomFont(_bodyField);
            var input = _bodyField.Q(TextField.textInputUssName);
            if (input != null)
                SamirinEditorStyleHelper.ApplyCustomFont(input);

            var textElement = _bodyField.Q<TextElement>();
            if (textElement != null)
                SamirinEditorStyleHelper.ApplyCustomFont(textElement);
        }

        void OnRootKeyDown(KeyDownEvent e)
        {
            if (!e.ctrlKey && !e.commandKey)
                return;

            // TextField 内蔵の Undo より、Unity の Undo スタックを優先する
            if (e.keyCode == KeyCode.Z && !e.shiftKey)
            {
                Undo.PerformUndo();
            }
            else if (e.keyCode == KeyCode.Y || (e.keyCode == KeyCode.Z && e.shiftKey))
            {
                Undo.PerformRedo();
            }
            else
            {
                return;
            }

            e.StopImmediatePropagation();
            e.PreventDefault();
        }

        // ---------------------------------------------------------------- 選択範囲

        /// <summary>
        /// TextField の選択位置を定期的に読み取り、書式 UI に反映する。
        /// 書式パネル操作中に本文の選択表示が消えても、直前の範囲は _kept に残す。
        /// </summary>
        void PollFieldSelection()
        {
            if (_bodyField == null)
                return;

            var text = _bodyField.value ?? "";
            MenuRichTextFieldCompat.GetSelection(_bodyField, out var cursor, out var select);
            MenuRichTextUtility.GetSelectionRange(text, cursor, select, out var start, out var end);
            MenuRichTextUtility.SnapSelectionToContent(text, ref start, ref end);

            // 範囲があるときだけ保持を更新する。空選択で消さない（書式欄入力中のため）
            if (start < end)
            {
                _keptStart = start;
                _keptEnd = end;
            }

            if (start == _selStart && end == _selEnd)
                return;

            _selStart = start;
            _selEnd = end;
            _syncedSelStart = -1;
            _formatGui?.MarkDirtyRepaint();
        }

        /// <summary>
        /// 書式適用に使う選択範囲。TextField の選択を優先し、
        /// 書式ボタンへフォーカスが移って選択表示が消えた場合は直前の範囲を使う。
        /// </summary>
        bool TryGetSelection(out int start, out int end)
        {
            var text = _text ?? "";
            if (_bodyField != null)
            {
                MenuRichTextFieldCompat.GetSelection(_bodyField, out var cursor, out var select);
                MenuRichTextUtility.GetSelectionRange(text, cursor, select, out var a, out var b);
                MenuRichTextUtility.SnapSelectionToContent(text, ref a, ref b);
                if (a < b)
                {
                    _selStart = a;
                    _selEnd = b;
                    _keptStart = a;
                    _keptEnd = b;
                    start = a;
                    end = b;
                    return true;
                }
            }

            MenuRichTextUtility.GetSelectionRange(text, _keptStart, _keptEnd, out start, out end);
            MenuRichTextUtility.SnapSelectionToContent(text, ref start, ref end);
            return start < end;
        }

        bool HasTextSelection()
        {
            return TryGetSelection(out _, out _);
        }

        void OnBodyFieldChanged(ChangeEvent<string> e)
        {
            if (_suppressFieldCallback)
                return;

            // 入力で選択は置き換わるので、保持していた範囲は破棄する
            _keptStart = 0;
            _keptEnd = 0;
            MenuRichTextFieldCompat.GetSelection(_bodyField, out var cursor, out var select);
            SetText(e.newValue, cursor, select, fromField: true);
        }

        /// <summary>
        /// 本文と選択範囲を TextField に書き戻す。タグ付け外しの後でも選択を保つ。
        /// </summary>
        void ApplyTextToField(string text, int selStart, int selEnd, bool focus)
        {
            if (_bodyField == null)
                return;

            text = text ?? "";
            if ((_bodyField.value ?? "") != text)
            {
                _suppressFieldCallback = true;
                try
                {
                    _bodyField.SetValueWithoutNotify(text);
                }
                finally
                {
                    _suppressFieldCallback = false;
                }
            }

            _pendingSelStart = selStart;
            _pendingSelEnd = selEnd;
            _pendingFocus = focus;
            RestorePendingSelection(false);
            // IMGUI の描画中にフォーカスを移さないため、フォーカス戻しは次フレームに回す
            _bodyField.schedule.Execute(() => RestorePendingSelection(_pendingFocus));
        }

        void RestorePendingSelection(bool allowFocus)
        {
            if (_bodyField == null)
                return;

            var text = _bodyField.value ?? "";
            var len = text.Length;
            var start = Mathf.Clamp(Mathf.Min(_pendingSelStart, _pendingSelEnd), 0, len);
            var end = Mathf.Clamp(Mathf.Max(_pendingSelStart, _pendingSelEnd), 0, len);
            MenuRichTextUtility.SnapSelectionToContent(text, ref start, ref end);
            if (allowFocus && _bodyField.enabledInHierarchy)
                _bodyField.Focus();

            // cursorIndex / selectIndex はキャレット位置（end は排他的）
            MenuRichTextFieldCompat.SelectRange(_bodyField, start, end);
            _selStart = start;
            _selEnd = end;
            _keptStart = start;
            _keptEnd = end;
            _syncedSelStart = -1;
        }

        // ---------------------------------------------------------------- 対象の収集

        void OnSelectionChange()
        {
            if (!_followSelection)
                return;
            if (_dirty && !_autoApply)
                return;
            CollectFromCurrentSelection(true);
            Repaint();
        }

        void CollectFromCurrentSelection(bool selectFirst)
        {
            _sourceObject = Selection.activeObject;
            LoadFromObject(_sourceObject, selectFirst);
        }

        void LoadFromObject(Object obj, bool selectFirst = true)
        {
            _sourceObject = obj;
            _targets.Clear();
            _targets.AddRange(MenuRichTextUtility.CollectFromObject(obj));
            if (_targets.Count == 0 && obj != null)
                _targets.AddRange(MenuRichTextUtility.CollectFromSelection());

            if (selectFirst || _selectedIndex >= _targets.Count)
                _selectedIndex = 0;
            LoadSelected(false);
        }

        void SelectIndex(int index)
        {
            if (_dirty && !_autoApply)
                ApplyToTarget(false);
            _selectedIndex = index;
            LoadSelected(false);
        }

        MenuRichTextUtility.Target CurrentTarget()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _targets.Count)
                return null;
            return _targets[_selectedIndex];
        }

        void LoadSelected(bool applyPrevious)
        {
            if (applyPrevious && _dirty)
                ApplyToTarget(false);

            var t = CurrentTarget();
            _lockApplying = true;
            _suppressUndo = true;
            _text = t != null ? MenuRichTextUtility.Read(t) : "";
            _selStart = 0;
            _selEnd = 0;
            _keptStart = 0;
            _keptEnd = 0;
            _syncedSelStart = -1;
            SyncDraftUndoStateWithoutRecord();
            _dirty = false;
            _suppressUndo = false;
            _lockApplying = false;

            _bodyField?.SetEnabled(t != null);
            ApplyTextToField(_text, 0, 0, focus: false);

            if (t == null)
                SetStatus("編集対象がありません。", MessageType.Warning);
            else
                SetStatus($"編集中: {t.KindLabel}  {t.OwnerPath}", MessageType.Info);
        }

        // ---------------------------------------------------------------- Undo

        void EnsureDraftUndoState()
        {
            if (_draftUndo != null)
                return;
            _draftUndo = CreateInstance<DraftUndoState>();
            _draftUndo.hideFlags = HideFlags.HideAndDontSave;
            _draftUndo.name = "MenuRichTextDraftUndo";
            _draftUndo.text = _text ?? "";
            _draftUndo.selStart = _selStart;
            _draftUndo.selEnd = _selEnd;
        }

        void DestroyDraftUndoState()
        {
            if (_draftUndo == null)
                return;
            DestroyImmediate(_draftUndo);
            _draftUndo = null;
        }

        void SyncDraftUndoStateWithoutRecord()
        {
            EnsureDraftUndoState();
            if (_draftUndo == null)
                return;
            _draftUndo.text = _text ?? "";
            _draftUndo.selStart = _selStart;
            _draftUndo.selEnd = _selEnd;
        }

        void OnUndoRedoPerformed()
        {
            EnsureDraftUndoState();
            var selStart = _selStart;
            var selEnd = _selEnd;
            if (_draftUndo != null)
            {
                _text = _draftUndo.text ?? "";
                selStart = Mathf.Clamp(_draftUndo.selStart, 0, _text.Length);
                selEnd = Mathf.Clamp(_draftUndo.selEnd, 0, _text.Length);
            }

            var t = CurrentTarget();
            if (t != null && t.IsValid)
            {
                var fromTarget = MenuRichTextUtility.Read(t) ?? "";
                if (_autoApply)
                {
                    _text = fromTarget;
                    _suppressUndo = true;
                    SyncDraftUndoStateWithoutRecord();
                    _suppressUndo = false;
                    _dirty = false;
                }
                else
                {
                    _dirty = (_text ?? "") != fromTarget;
                }

                t.RawText = fromTarget;
                t.PlainPreview = MenuRichTextUtility.StripTags(fromTarget);
            }
            else
            {
                _dirty = false;
            }

            ApplyTextToField(_text, selStart, selEnd, focus: false);
            SetStatus("Undo / Redo を反映しました。", MessageType.Info);
            Repaint();
        }

        // ---------------------------------------------------------------- IMGUI 描画

        void EnsureStyles()
        {
            if (_panelStyle == null)
            {
                _panelStyle = new GUIStyle("HelpBox")
                {
                    padding = new RectOffset(8, 8, 8, 8)
                };
            }
        }

        void DrawTopBar()
        {
            EnsureStyles();
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            var follow = EditorGUILayout.ToggleLeft("選択を追従", _followSelection, GUILayout.Width(100));
            if (EditorGUI.EndChangeCheck())
            {
                _followSelection = follow;
                EditorPrefs.SetBool(PrefsFollowSelection, _followSelection);
                if (_followSelection)
                    CollectFromCurrentSelection(true);
            }

            EditorGUI.BeginChangeCheck();
            var autoApply = EditorGUILayout.ToggleLeft("自動書き込み", _autoApply, GUILayout.Width(100));
            if (EditorGUI.EndChangeCheck())
            {
                _autoApply = autoApply;
                EditorPrefs.SetBool(PrefsAutoApply, _autoApply);
            }

            EditorGUI.BeginChangeCheck();
            var next = EditorGUILayout.ObjectField(_sourceObject, typeof(Object), true);
            if (EditorGUI.EndChangeCheck())
                LoadFromObject(next);

            if (GUILayout.Button("取込", GUILayout.Width(44)))
                CollectFromCurrentSelection(true);
            using (new EditorGUI.DisabledScope(_selectedIndex < 0 || _selectedIndex >= _targets.Count))
            {
                if (GUILayout.Button("Ping", GUILayout.Width(44)))
                    MenuRichTextUtility.Ping(CurrentTarget());
            }

            EditorGUILayout.EndHorizontal();
            // DrawDependencyStatus();
        }

        void DrawDependencyStatus()
        {
            var ma = MenuRichTextUtility.IsMaAvailable ? "利用可能" : "未検出";
            var vrc = MenuRichTextUtility.IsVrcMenuAvailable ? "利用可能" : "未検出";
            var tmp = MenuRichTextTmpPreview.IsAvailable ? "利用可能" : "未検出";
            var ok = MenuRichTextUtility.IsMaAvailable || MenuRichTextUtility.IsVrcMenuAvailable;
            SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont(
                $"MA: {ma}  /  VRC Menu: {vrc}  /  TMP: {tmp}",
                ok ? MessageType.Info : MessageType.Warning);
        }

        void DrawStatus()
        {
            if (string.IsNullOrEmpty(_status))
                return;
            SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont(_status, _statusType);
        }

        void DrawObjectListGui()
        {
            EnsureStyles();
            var rect = _listGui != null ? _listGui.contentRect : Rect.zero;
            if (rect.width < 1f || rect.height < 1f)
                return;

            GUILayout.BeginArea(new Rect(0f, 0f, rect.width, rect.height));
            try
            {
                DrawObjectListPanel();
            }
            finally
            {
                GUILayout.EndArea();
            }
        }

        void DrawObjectListPanel()
        {
            EditorGUILayout.BeginVertical(_panelStyle, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("オブジェクトリスト", EditorStyles.boldLabel);
            _filter = EditorGUILayout.TextField(_filter ?? "", EditorStyles.toolbarSearchField);

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.ExpandHeight(true));
            if (_targets.Count == 0)
            {
                EditorGUILayout.HelpBox("MA Menu Item または VRC Expressions Menu を選択してください。", MessageType.None);
            }
            else
            {
                for (var i = 0; i < _targets.Count; i++)
                {
                    var t = _targets[i];
                    if (!PassFilter(t))
                        continue;
                    var selected = i == _selectedIndex;
                    var pressed = GUILayout.Toggle(selected, t.ListLabel, "Button");
                    if (pressed && !selected)
                        SelectIndex(i);
                    // if (selected && !string.IsNullOrEmpty(t.OwnerPath))
                    //     EditorGUILayout.LabelField(t.OwnerPath, EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        bool PassFilter(MenuRichTextUtility.Target t)
        {
            if (string.IsNullOrEmpty(_filter))
                return true;
            var f = _filter;
            return (t.ListLabel != null && t.ListLabel.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                   || (t.OwnerPath != null && t.OwnerPath.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                   || (t.RawText != null && t.RawText.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        void DrawPreviewPanel()
        {
            EnsureStyles();
            EditorGUILayout.BeginVertical(_panelStyle, GUILayout.ExpandWidth(true));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("プレビュー", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUI.BeginChangeCheck();
            _previewBg = EditorGUILayout.ColorField(GUIContent.none, _previewBg, false, false, false, GUILayout.Width(40));
            if (EditorGUI.EndChangeCheck())
            {
                _previewBg.a = 1f;
                EditorPrefs.SetString(PrefsBg, MenuRichTextUtility.ToHtmlRgb(_previewBg));
            }

            if (GUILayout.Button("背景色リセット", GUILayout.Width(88)))
            {
                _previewBg = VrcMenuBackground;
                EditorPrefs.SetString(PrefsBg, MenuRichTextUtility.ToHtmlRgb(_previewBg));
            }

            EditorGUILayout.EndHorizontal();

            var visualRect = GUILayoutUtility.GetRect(16f, PreviewHeightFixed, GUILayout.ExpandWidth(true), GUILayout.Height(PreviewHeightFixed));
            if (Event.current.type == EventType.Repaint)
            {
                if (_preview != null)
                    _preview.Draw(visualRect, _text, _previewBg);
                else
                    DrawImguiRichFallback(visualRect, _text, _previewBg);
            }

            if (_preview != null && !string.IsNullOrEmpty(_preview.StatusMessage))
                EditorGUILayout.LabelField(_preview.StatusMessage, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        void DrawBodyHeader()
        {
            EditorGUILayout.LabelField("本文", EditorStyles.boldLabel);
        }

        void DrawBodyFooter()
        {
            var target = CurrentTarget();
            EditorGUILayout.BeginHorizontal();
            var plain = MenuRichTextUtility.StripTags(_text);
            EditorGUILayout.LabelField($"文字数 {(_text ?? "").Length}（表示 {plain.Length}）", EditorStyles.miniLabel);
            if (GUILayout.Button("コピー", GUILayout.Width(56)))
            {
                EditorGUIUtility.systemCopyBuffer = _text ?? "";
                SetStatus("ソースをコピーしました。", MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(target == null || (_autoApply && !_dirty)))
            {
                if (GUILayout.Button("書き込む", GUILayout.Width(64)))
                    ApplyToTarget(true);
            }

            if (target != null && target.Kind == MenuRichTextUtility.TargetKind.MaItemName &&
                GUILayout.Button("label空", GUILayout.Width(56)))
            {
                SetText("", 0, 0);
                ApplyToTarget(true);
                SetStatus("label を空にしました。GameObject 名が表示に使われます。", MessageType.Info);
            }

            EditorGUILayout.EndHorizontal();
        }

        void DrawFormatPanel()
        {
            EnsureStyles();
            EditorGUILayout.BeginVertical(_panelStyle, GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField("書式", EditorStyles.boldLabel);
            DrawToolbar();
            DrawCheatSheet();
            EditorGUILayout.EndVertical();
        }

        void DrawToolbar()
        {
            SyncFormatUiFromSelection();

            EditorGUILayout.BeginHorizontal();
            ToolbarToggle("B", "太字 <b>", _selectionFormat.Bold, () => ApplyWrap("<b>", "</b>"));
            ToolbarToggle("I", "斜体 <i>", _selectionFormat.Italic, () => ApplyWrap("<i>", "</i>"));
            ToolbarToggle("U", "下線 <u>", _selectionFormat.Underline, () => ApplyWrap("<u>", "</u>"));
            ToolbarToggle("S", "取り消し線 <s>", _selectionFormat.Strikethrough, () => ApplyWrap("<s>", "</s>"));
            ToolbarToggle("x₂", "下付き <sub>", _selectionFormat.Subscript, () => ApplyWrap("<sub>", "</sub>"));
            ToolbarToggle("x²", "上付き <sup>", _selectionFormat.Superscript, () => ApplyWrap("<sup>", "</sup>"));
            ToolbarToggle("A", "スモールキャップス", _selectionFormat.SmallCaps, () => ApplyWrap("<smallcaps>", "</smallcaps>"));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("マーク", GUILayout.Width(40));
            EditorGUI.BeginChangeCheck();
            _markColor = EditorGUILayout.ColorField(GUIContent.none, _markColor, true, true, false, GUILayout.Width(52));
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(PrefsMarkColor, MenuRichTextUtility.ToHtmlColor(_markColor));
                _pendingMarkApply = true;
            }

            if (GUILayout.Button("適用", GUILayout.Width(48)))
            {
                _pendingMarkApply = false;
                ApplyMark(_markColor);
            }

            using (new EditorGUI.DisabledScope(!_selectionFormat.Mark))
            {
                if (GUILayout.Button("解除", GUILayout.Width(48)))
                {
                    _pendingMarkApply = false;
                    RemoveTag("mark");
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全削除", GUILayout.Width(56)))
            {
                var stripped = MenuRichTextUtility.StripTags(_text);
                SetText(stripped, 0, stripped.Length);
            }

            if (GUILayout.Button("選択範囲削除", GUILayout.Width(96)))
            {
                if (!TryGetSelection(out var selA, out var selB))
                {
                    SetStatus("本文で文字を選択してからタグを削除してください。", MessageType.Warning);
                }
                else
                {
                    var next = MenuRichTextUtility.StripTagsInRange(_text, selA, selB, out var a, out var b);
                    SetText(next, a, b);
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("色", GUILayout.Width(24));
            EditorGUI.BeginChangeCheck();
            _editColor = EditorGUILayout.ColorField(GUIContent.none, _editColor, true, true, false, GUILayout.Width(52));
            if (EditorGUI.EndChangeCheck())
            {
                _hex = MenuRichTextUtility.ToHtmlColor(_editColor);
                EditorPrefs.SetString(PrefsColor, _hex);
                // カラーピッカー操作中は毎フレーム適用しない（本文フォーカス奪取を防ぐ）
                _pendingColorApply = true;
            }

            _hex = EditorGUILayout.TextField(_hex, GUILayout.Width(80));
            if (GUILayout.Button("HEX", GUILayout.Width(40)))
            {
                _pendingColorApply = false;
                _editColor = MenuRichTextUtility.ParseColorOr(_hex, _editColor);
                _hex = MenuRichTextUtility.ToHtmlColor(_editColor);
                EditorPrefs.SetString(PrefsColor, _hex);
                ApplyColor(_editColor);
            }

            if (GUILayout.Button("色を適用", GUILayout.Width(72)))
            {
                _pendingColorApply = false;
                ApplyColor(_editColor);
            }

            using (new EditorGUI.DisabledScope(!_selectionFormat.HasColor))
            {
                if (GUILayout.Button("色解除", GUILayout.Width(56)))
                    RemoveTag("color");
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            foreach (var c in Palette)
            {
                if (DrawColorSwatch(c, 18f, 18f))
                {
                    _editColor = c;
                    _editColor.a = 1f;
                    _hex = MenuRichTextUtility.ToHtmlColor(_editColor);
                    EditorPrefs.SetString(PrefsColor, _hex);
                    ApplyColor(_editColor);
                }
            }

            var sep = GUILayoutUtility.GetRect(8f, 18f, GUILayout.Width(8f));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(sep.x + 3f, sep.y + 2f, 1f, sep.height - 4f), new Color(1f, 1f, 1f, 0.25f));

            for (var i = 0; i < _colorHistory.Count; i++)
            {
                var hist = _colorHistory[i];
                if (DrawColorSwatch(hist, 18f, 18f))
                {
                    _editColor = hist;
                    _hex = MenuRichTextUtility.ToHtmlColor(hist);
                    EditorPrefs.SetString(PrefsColor, _hex);
                    ApplyColor(hist);
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("グラデ", GUILayout.Width(48));
            EnsureGradient();
            EditorGUI.BeginChangeCheck();
            _gradient = EditorGUILayout.GradientField(_gradient);
            if (EditorGUI.EndChangeCheck())
                SaveGradientPrefs();
            if (GUILayout.Button("適用", GUILayout.Width(48)))
                ApplyGradient();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            _sizePercent = EditorGUILayout.IntSlider("サイズ %", _sizePercent, 20, 200);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetInt(PrefsSize, _sizePercent);
                // ドラッグ中・数値入力中は値だけ更新し、操作が終わってから一度だけ適用する
                _pendingSizeApply = true;
            }
            if (GUILayout.Button("適用", GUILayout.Width(48)))
            {
                _pendingSizeApply = false;
                ApplySize(_sizePercent);
            }

            using (new EditorGUI.DisabledScope(!_selectionFormat.HasSize))
            {
                if (GUILayout.Button("解除", GUILayout.Width(48)))
                {
                    _pendingSizeApply = false;
                    RemoveTag("size");
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("50%"))
                ApplySizePreset(50);
            if (GUILayout.Button("75%"))
                ApplySizePreset(75);
            if (GUILayout.Button("100%"))
                ApplySizePreset(100);
            if (GUILayout.Button("125%"))
                ApplySizePreset(125);
            if (GUILayout.Button("150%"))
                ApplySizePreset(150);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            _vOffsetEm = EditorGUILayout.Slider("voffset (em)", _vOffsetEm, -1.5f, 1.5f);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetFloat(PrefsVOffset, _vOffsetEm);
                _pendingVOffsetApply = true;
            }
            if (GUILayout.Button("適用", GUILayout.Width(48)))
            {
                _pendingVOffsetApply = false;
                ApplyVOffset(_vOffsetEm);
            }

            using (new EditorGUI.DisabledScope(!_selectionFormat.HasVOffset))
            {
                if (GUILayout.Button("解除", GUILayout.Width(48)))
                {
                    _pendingVOffsetApply = false;
                    RemoveTag("voffset");
                }
            }

            EditorGUILayout.EndHorizontal();

            FlushPendingFormatApplies();
        }

        /// <summary>
        /// スライダー／数値入力／カラーピッカーの操作が終わったタイミングで、まとめてタグを適用する。
        /// </summary>
        void FlushPendingFormatApplies()
        {
            if (IsFormatControlBusy())
                return;

            if (_pendingColorApply)
            {
                _pendingColorApply = false;
                if (HasTextSelection())
                    ApplyColor(_editColor, focusBody: false);
            }

            if (_pendingMarkApply)
            {
                _pendingMarkApply = false;
                if (HasTextSelection())
                    ApplyMark(_markColor, focusBody: false);
            }

            if (_pendingSizeApply)
            {
                _pendingSizeApply = false;
                if (HasTextSelection())
                    ApplySize(_sizePercent, focusBody: false);
            }

            if (_pendingVOffsetApply)
            {
                _pendingVOffsetApply = false;
                if (HasTextSelection())
                    ApplyVOffset(_vOffsetEm, focusBody: false);
            }
        }

        static bool IsFormatControlBusy()
        {
            return GUIUtility.hotControl != 0 || EditorGUIUtility.editingTextField;
        }

        void ApplySizePreset(int percent)
        {
            _pendingSizeApply = false;
            ApplySize(percent);
        }

        void SyncFormatUiFromSelection()
        {
            if (!TryGetSelection(out var selA, out var selB))
            {
                _selectionFormat = default;
                _syncedSelStart = -1;
                _syncedSelEnd = -1;
                _syncedFormatText = null;
                return;
            }

            var text = _text ?? "";
            var selectionChanged = selA != _syncedSelStart || selB != _syncedSelEnd || text != (_syncedFormatText ?? "");
            if (selectionChanged)
            {
                _syncedSelStart = selA;
                _syncedSelEnd = selB;
                _syncedFormatText = text;
                _selectionFormat = MenuRichTextUtility.InspectSelection(text, selA, selB);
            }

            // スライダー／数値入力中はフィールド値を上書きしない（操作が跳ねる原因になる）
            if (IsFormatControlBusy() || _pendingSizeApply || _pendingVOffsetApply || _pendingColorApply || _pendingMarkApply)
                return;

            if (!_selectionFormat.HasColor && !_selectionFormat.HasMarkColor &&
                !_selectionFormat.HasSize && !_selectionFormat.HasVOffset &&
                !_selectionFormat.HasGradient)
                return;

            if (_selectionFormat.HasGradient && _selectionFormat.Gradient != null)
                _gradient = _selectionFormat.Gradient;

            if (_selectionFormat.HasColor)
            {
                _editColor = _selectionFormat.Color;
                _hex = MenuRichTextUtility.ToHtmlColor(_editColor);
            }

            if (_selectionFormat.HasMarkColor)
                _markColor = _selectionFormat.MarkColor;

            if (_selectionFormat.HasSize)
                _sizePercent = _selectionFormat.SizePercent;

            if (_selectionFormat.HasVOffset)
                _vOffsetEm = _selectionFormat.VOffsetEm;
        }

        static bool DrawColorSwatch(Color color, float width, float height)
        {
            var rect = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
            var opaque = color;
            opaque.a = 1f;
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.65f));
                var inner = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
                EditorGUI.DrawRect(inner, opaque);
            }

            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        void ToolbarToggle(string label, string tooltip, bool active, Action onClick, float width = 32f)
        {
            var prev = GUI.backgroundColor;
            if (active)
                GUI.backgroundColor = new Color(0.35f, 0.65f, 1f, 1f);
            if (GUILayout.Button(new GUIContent(label, tooltip), GUILayout.Width(width), GUILayout.Height(22)))
                onClick();
            GUI.backgroundColor = prev;
        }

        void DrawImguiRichFallback(Rect rect, string text, Color background)
        {
            EditorGUI.DrawRect(rect, background);
            var style = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                wordWrap = true,
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                richText = true,
                padding = new RectOffset(12, 12, 18, 18),
                normal = { textColor = Color.white }
            };
            GUI.Label(rect, string.IsNullOrEmpty(text) ? " " : text, style);
        }

        void DrawCheatSheet()
        {
            _cheatFoldout = EditorGUILayout.Foldout(_cheatFoldout, "よく使う TMPro タグ", true);
            if (!_cheatFoldout)
                return;

            const string cheatText =
                "<b>太字</b>  <i>斜体</i>  <u>下線</u>  <s>取り消し</s>\n" +
                "<color=#FF88AA>色</color>  <size=80%>サイズ</size>  <voffset=0.3em>位置</voffset>\n" +
                "<sub>下付き</sub>  <sup>上付き</sup>  <mark=#FFE066>ハイライト</mark>  <smallcaps>ABC</smallcaps>\n" +
                "MA Menu Item では GameObject 名ではなく label に書き込みます（リッチテキスト用）。";

            var style = new GUIStyle(EditorStyles.helpBox)
            {
                wordWrap = true,
                richText = false,
                padding = new RectOffset(10, 10, 10, 10)
            };
            var available = _formatGui != null ? _formatGui.contentRect.width : 0f;
            var width = Mathf.Max(160f, available - 40f);
            var height = style.CalcHeight(new GUIContent(cheatText), width) + 4f;
            EditorGUILayout.LabelField(cheatText, style, GUILayout.Height(height), GUILayout.ExpandWidth(true));
        }

        // ---------------------------------------------------------------- 書式の適用

        void ApplyWrap(string open, string close)
        {
            if (!TryGetSelection(out var selA, out var selB))
            {
                SetStatus("本文で文字を選択してから書式を適用してください。", MessageType.Warning);
                return;
            }

            var next = MenuRichTextUtility.WrapOrInsert(_text, selA, selB, open, close, out var a, out var b);
            SetText(next, a, b);
        }

        void ApplyColor(Color color, bool focusBody = true)
        {
            if (!TryGetSelection(out var selA, out var selB))
            {
                SetStatus("本文で文字を選択してから色を適用してください。", MessageType.Warning);
                return;
            }

            var open = "<color=#" + MenuRichTextUtility.ToHtmlColor(color) + ">";
            var next = MenuRichTextUtility.WrapColor(_text, selA, selB, color, out var a, out var b);
            if (next.IndexOf(open, StringComparison.OrdinalIgnoreCase) >= 0)
                PushColorHistory(color);
            _editColor = color;
            _hex = MenuRichTextUtility.ToHtmlColor(color);
            EditorPrefs.SetString(PrefsColor, _hex);
            SetText(next, a, b, focusBody: focusBody);
        }

        void ApplyMark(Color color, bool focusBody = true)
        {
            if (!TryGetSelection(out var selA, out var selB))
            {
                SetStatus("本文で文字を選択してからマークを適用してください。", MessageType.Warning);
                return;
            }

            _markColor = color;
            EditorPrefs.SetString(PrefsMarkColor, MenuRichTextUtility.ToHtmlColor(color));
            var next = MenuRichTextUtility.WrapMark(_text, selA, selB, color, out var a, out var b);
            SetText(next, a, b, focusBody: focusBody);
        }

        void ApplySize(int percent, bool focusBody = true)
        {
            if (!TryGetSelection(out var selA, out var selB))
            {
                SetStatus("本文で文字を選択してからサイズを適用してください。", MessageType.Warning);
                return;
            }

            _sizePercent = percent;
            EditorPrefs.SetInt(PrefsSize, percent);
            var next = MenuRichTextUtility.WrapSizePercent(_text, selA, selB, percent, out var a, out var b);
            SetText(next, a, b, focusBody: focusBody);
        }

        void ApplyVOffset(float em, bool focusBody = true)
        {
            if (!TryGetSelection(out var selA, out var selB))
            {
                SetStatus("本文で文字を選択してから voffset を適用してください。", MessageType.Warning);
                return;
            }

            var next = MenuRichTextUtility.WrapVOffset(_text, selA, selB, em, out var a, out var b);
            SetText(next, a, b, focusBody: focusBody);
        }

        void ApplyGradient()
        {
            if (!TryGetSelection(out var selA, out var selB))
            {
                SetStatus("本文で文字を選択してから適用してください。", MessageType.Warning);
                return;
            }

            EnsureGradient();
            var next = MenuRichTextUtility.ApplyGradient(_text, selA, selB, _gradient, out var a, out var b);
            SetText(next, a, b);
            SaveGradientPrefs();
        }

        void RemoveTag(string tagName)
        {
            if (!TryGetSelection(out var selA, out var selB))
            {
                SetStatus("本文で文字を選択してから解除してください。", MessageType.Warning);
                return;
            }

            var next = MenuRichTextUtility.UnwrapTag(_text, selA, selB, tagName, out var a, out var b);
            if (next == (_text ?? ""))
            {
                SetStatus($"選択範囲に {tagName} タグがありません。", MessageType.Warning);
                return;
            }

            SetText(next, a, b);
        }

        // ---------------------------------------------------------------- 設定の保存

        void EnsureGradient()
        {
            if (_gradient != null)
                return;
            _gradient = new Gradient();
            _gradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.32f, 0.32f), 0f), new GradientColorKey(new Color(1f, 0.86f, 0.2f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        }

        void LoadGradientPrefs()
        {
            EnsureGradient();
            var raw = EditorPrefs.GetString(PrefsGradient, "");
            if (string.IsNullOrEmpty(raw))
                return;

            var parts = raw.Split('|');
            if (parts.Length < 2)
                return;

            var from = MenuRichTextUtility.ParseColorOr(parts[0], new Color(1f, 0.32f, 0.32f));
            var to = MenuRichTextUtility.ParseColorOr(parts[1], new Color(1f, 0.86f, 0.2f));
            var keys = new List<GradientColorKey> { new GradientColorKey(from, 0f) };
            for (var i = 2; i < parts.Length; i++)
            {
                var at = parts[i].Split('@');
                if (at.Length != 2)
                    continue;
                if (!float.TryParse(at[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var t))
                    continue;
                keys.Add(new GradientColorKey(MenuRichTextUtility.ParseColorOr(at[0], Color.white), Mathf.Clamp01(t)));
            }

            keys.Add(new GradientColorKey(to, 1f));
            keys.Sort((a, b) => a.time.CompareTo(b.time));
            _gradient.SetKeys(keys.ToArray(), new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        }

        void SaveGradientPrefs()
        {
            EnsureGradient();
            var keys = _gradient.colorKeys;
            if (keys == null || keys.Length == 0)
                return;

            var from = MenuRichTextUtility.ToHtmlRgb(keys[0].color);
            var to = MenuRichTextUtility.ToHtmlRgb(keys[keys.Length - 1].color);
            var sb = new System.Text.StringBuilder();
            sb.Append(from).Append('|').Append(to);
            for (var i = 1; i < keys.Length - 1; i++)
            {
                sb.Append('|')
                    .Append(MenuRichTextUtility.ToHtmlRgb(keys[i].color))
                    .Append('@')
                    .Append(keys[i].time.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }

            EditorPrefs.SetString(PrefsGradient, sb.ToString());
        }

        void LoadColorHistory()
        {
            _colorHistory.Clear();
            var raw = EditorPrefs.GetString(PrefsColorHistory, "");
            if (string.IsNullOrEmpty(raw))
                return;

            foreach (var part in raw.Split(','))
            {
                var token = (part ?? "").Trim().TrimStart('#');
                if (string.IsNullOrEmpty(token))
                    continue;
                if (!ColorUtility.TryParseHtmlString("#" + token, out var c))
                    continue;
                PushColorHistory(c, persist: false);
            }
        }

        void SaveColorHistory()
        {
            var parts = new List<string>(_colorHistory.Count);
            foreach (var c in _colorHistory)
                parts.Add(MenuRichTextUtility.ToHtmlColor(c));
            EditorPrefs.SetString(PrefsColorHistory, string.Join(",", parts));
        }

        void PushColorHistory(Color color, bool persist = true)
        {
            var hex = MenuRichTextUtility.ToHtmlColor(color);
            for (var i = _colorHistory.Count - 1; i >= 0; i--)
            {
                if (MenuRichTextUtility.ToHtmlColor(_colorHistory[i]) == hex)
                    _colorHistory.RemoveAt(i);
            }

            _colorHistory.Insert(0, color);
            while (_colorHistory.Count > ColorHistoryMax)
                _colorHistory.RemoveAt(_colorHistory.Count - 1);

            if (persist)
                SaveColorHistory();
        }

        // ---------------------------------------------------------------- 本文の更新

        void SetText(string next, int selStart, int selEnd, bool fromField = false, bool focusBody = true)
        {
            next = next ?? "";
            MenuRichTextUtility.GetSelectionRange(next, selStart, selEnd, out selStart, out selEnd);
            if (next == (_text ?? "") && selStart == _selStart && selEnd == _selEnd)
                return;

            EnsureDraftUndoState();
            var group = Undo.GetCurrentGroup();
            if (!_suppressUndo && _draftUndo != null)
            {
                Undo.SetCurrentGroupName(UndoName);
                Undo.RecordObject(_draftUndo, UndoName);
                _draftUndo.text = next;
                _draftUndo.selStart = selStart;
                _draftUndo.selEnd = selEnd;
            }

            _text = next;
            _selStart = selStart;
            _selEnd = selEnd;
            _keptStart = selStart;
            _keptEnd = selEnd;
            _syncedSelStart = -1;

            // 入力中はキャレットを動かさない。書式適用時は選択を復元する（必要なら本文へフォーカス）
            if (!fromField)
                ApplyTextToField(next, selStart, selEnd, focusBody);

            _dirty = true;
            if (_autoApply && !_lockApplying)
                ApplyToTarget(false);

            if (!_suppressUndo)
                Undo.CollapseUndoOperations(group);

            Repaint();
        }

        void ApplyToTarget(bool notify)
        {
            var t = CurrentTarget();
            if (t == null || !t.IsValid)
            {
                if (notify)
                    SetStatus("書き込み先がありません。", MessageType.Warning);
                return;
            }

            EnsureDraftUndoState();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            if (_draftUndo != null && !_suppressUndo)
            {
                Undo.RecordObject(_draftUndo, UndoName);
                _draftUndo.text = _text ?? "";
                _draftUndo.selStart = _selStart;
                _draftUndo.selEnd = _selEnd;
            }

            if (!MenuRichTextUtility.Write(t, _text, UndoName))
            {
                SetStatus("書き込みに失敗しました。プロパティが見つかりません。", MessageType.Error);
                return;
            }

            if (!_suppressUndo)
                Undo.CollapseUndoOperations(group);

            _dirty = false;
            if (notify)
                SetStatus("書き込みました。", MessageType.Info);
        }

        void SetStatus(string message, MessageType type)
        {
            _status = message;
            _statusType = type;
            _statusGui?.MarkDirtyRepaint();
        }
    }

    /// <summary>
    /// UI Toolkit の TextField 選択 API のバージョン差を吸収します。
    /// </summary>
    static class MenuRichTextFieldCompat
    {
        /// <summary>
        /// タグをそのまま表示する複数行入力にします。
        /// </summary>
        public static void SetupPlainMultiline(TextField field)
        {
            if (field == null)
                return;

            // white-space は継承されるため、内部の TextElement にも折り返しが伝わる
            field.style.whiteSpace = WhiteSpace.Normal;
            field.style.unityTextAlign = TextAnchor.UpperLeft;

            // 書式適用後にフォーカスを戻すため、全選択されると選択範囲が壊れる
            field.selectAllOnFocus = false;
            field.selectAllOnMouseUp = false;

            var textElement = field.Q<TextElement>();
            if (textElement != null)
            {
                textElement.enableRichText = false;
                textElement.parseEscapeSequences = false;
                SamirinEditorStyleHelper.ApplyCustomFont(textElement);
            }

            // 入力領域（USS でフォントが上書きされることがある）にも適用
            var input = field.Q(TextField.textInputUssName);
            if (input != null)
                SamirinEditorStyleHelper.ApplyCustomFont(input);

#if UNITY_2022_2_OR_NEWER
            field.SetVerticalScrollerVisibility(ScrollerVisibility.Auto);
#endif
        }

        public static void GetSelection(TextField field, out int cursorIndex, out int selectIndex)
        {
            cursorIndex = 0;
            selectIndex = 0;
            if (field == null)
                return;

#if UNITY_2022_2_OR_NEWER
            var selection = field.textSelection;
            if (selection == null)
                return;
            cursorIndex = selection.cursorIndex;
            selectIndex = selection.selectIndex;
#else
            cursorIndex = field.cursorIndex;
            selectIndex = field.selectIndex;
#endif
        }

        public static void SelectRange(TextField field, int cursorIndex, int selectIndex)
        {
            if (field == null)
                return;

#if UNITY_2022_2_OR_NEWER
            field.textSelection?.SelectRange(cursorIndex, selectIndex);
#else
            field.SelectRange(cursorIndex, selectIndex);
#endif
        }
    }
}
