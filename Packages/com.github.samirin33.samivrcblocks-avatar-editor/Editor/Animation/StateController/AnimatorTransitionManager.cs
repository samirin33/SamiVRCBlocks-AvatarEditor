using System;
using System.Collections.Generic;
using System.Linq;
using Samirin33.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;
using AnimatorController = UnityEditor.Animations.AnimatorController;
using AnimatorControllerParameter = UnityEngine.AnimatorControllerParameter;
using AnimatorControllerParameterType = UnityEngine.AnimatorControllerParameterType;
using BlendTree = UnityEditor.Animations.BlendTree;

namespace Samirin33.AvatarEditor.Tools.Editor
{
    /// <summary>
    /// 選択中トランジションの設定順序をドラッグ&amp;ドロップで入れ替えるウィンドウ。
    /// ステート選択時は Outgoing / Incoming を分けて表示する。
    /// 同じ親配列（State / Any State / Entry）内の選択要素だけを差し替える。
    /// 実装は <c>partial</c> により <c>AnimatorTransitionManager.*.cs</c> に分割。
    /// </summary>
    public sealed partial class AnimatorTransitionManager : EditorWindow
    {
        private enum GroupKind
        {
            State,
            AnyState,
            Entry,
            /// <summary>子サブステート（StateMachine ブロック）を起点にした遷移。</summary>
            StateMachineNode
        }

        private sealed class TransitionGroup
        {
            public AnimatorController controller;
            public AnimatorStateMachine stateMachine;
            public AnimatorState sourceState;
            public AnimatorStateMachine sourceStateMachine;
            public GroupKind kind;

            public string GroupId
            {
                get
                {
                    if (kind == GroupKind.StateMachineNode)
                    {
                        var c = controller != null ? controller.GetInstanceID() : 0;
                        var parent = stateMachine != null ? stateMachine.GetInstanceID() : 0;
                        var from = sourceStateMachine != null ? sourceStateMachine.GetInstanceID() : 0;
                        return $"{c}:{parent}:{from}:StateMachineNode";
                    }

                    var c2 = controller != null ? controller.GetInstanceID() : 0;
                    var sm = stateMachine != null ? stateMachine.GetInstanceID() : 0;
                    var st = sourceState != null ? sourceState.GetInstanceID() : 0;
                    return $"{c2}:{sm}:{st}:{kind}";
                }
            }

            public string Label
            {
                get
                {
                    return kind switch
                    {
                        GroupKind.State => $"State: {sourceState?.name ?? "(null)"}",
                        GroupKind.AnyState => $"Any State ({stateMachine?.name ?? "(null)"})",
                        GroupKind.Entry => $"Entry ({stateMachine?.name ?? "(null)"})",
                        GroupKind.StateMachineNode => $"StateMachine: {sourceStateMachine?.name ?? "(null)"} (from) ({stateMachine?.name ?? "(null)"})",
                        _ => "Unknown"
                    };
                }
            }
        }

        private sealed class TransitionRow
        {
            public AnimatorTransitionBase transition;
            public TransitionGroup group;
        }

        private sealed class SubStateDefaultActionContext
        {
            public AnimatorController controller;
            public AnimatorStateMachine parentStateMachine;
            public AnimatorState selectedState;
        }

        private readonly List<TransitionRow> _outgoing = new List<TransitionRow>();
        private readonly List<TransitionRow> _incoming = new List<TransitionRow>();
        private ReorderableList _reorderOutgoing;
        private ReorderableList _reorderIncoming;
        private Vector2 _scroll;

        [Flags]
        private enum PanelVisibility
        {
            None = 0,
            TransitionList = 1 << 0,
            SettingsAndConditions = 1 << 1,
            Clipboard = 1 << 2,
            All = TransitionList | SettingsAndConditions | Clipboard
        }

        private PanelVisibility _visiblePanels = PanelVisibility.All;

        private bool _foldoutClipboardPanel = false;
        private readonly List<bool> _clipboardSlotFold = new List<bool>();
        private readonly List<bool> _clipboardSlotFoldBlend = new List<bool>();
        private readonly List<bool> _clipboardSlotFoldConditions = new List<bool>();

        private enum FocusedListBucket
        {
            None,
            Outgoing,
            Incoming
        }

        private FocusedListBucket _selectionBucket = FocusedListBucket.None;
        private readonly HashSet<int> _selectedRowIndices = new HashSet<int>();

        private readonly List<ConditionEditRow> _conditionBuffer = new List<ConditionEditRow>();
        private string _lastConditionBufferSignature = "";
        private ReorderableList _reorderConditions;
        private List<AnimatorTransitionBase> _conditionEditTargetTransitions;
        private List<AnimatorController> _conditionEditMenuControllers;
        private readonly Dictionary<int, UnityEditor.Editor> _behaviourEditors = new Dictionary<int, UnityEditor.Editor>();

        private struct ConditionEditRow
        {
            public AnimatorConditionMode mode;
            public string parameter;
            public float threshold;
        }

        private static GUIContent _cachedBetweenIcon;
        private static GUIContent _cachedCopyIcon;
        private static GUIContent _cachedPasteOwIcon;
        private static GUIContent _cachedPasteAddIcon;
        private static GUIContent _cachedDeleteIcon;

        private static GUIStyle _foldoutStyleNormal;
        private static GUIStyle _centeredLabelStyle;

        private static GUIStyle FoldoutStyleNormal
        {
            get
            {
                if (_foldoutStyleNormal == null)
                {
                    _foldoutStyleNormal = new GUIStyle(EditorStyles.foldout)
                    {
                        fontStyle = FontStyle.Normal
                    };
                }

                return _foldoutStyleNormal;
            }
        }

        private static GUIStyle CenteredLabelStyle
        {
            get
            {
                if (_centeredLabelStyle == null)
                {
                    _centeredLabelStyle = new GUIStyle(EditorStyles.label)
                    {
                        alignment = TextAnchor.MiddleCenter
                    };
                }

                return _centeredLabelStyle;
            }
        }

        /// <summary>
        /// スクロール内でも折り返し全文が計算されるよう wide を付与（複数行で見切れないようにする）。
        /// </summary>
        private static void HelpBoxFullWidth(string message, MessageType messageType)
        {
            EditorGUILayout.HelpBox(message, messageType, true);
        }

        [MenuItem("SBAvatarEditor/Animation/AnimatorStateController", false, 3)]
        public static void Open()
        {
            var window = GetWindow<AnimatorTransitionManager>("AnimatorStateController");
            window.minSize = new Vector2(560f, 320f);
            window.RefreshSelection();
            // ウィンドウ表示直後は Selection / Animator の同期が次フレームまで遅れることがあるため再読み込みする
            var w = window;
            EditorApplication.delayCall += () =>
            {
                if (w == null)
                    return;
                w.RefreshSelection();
                w.Repaint();
            };
        }

        private void OnEnable()
        {
            // 新規オープン・ドッキング復帰・スクリプトリロード後に、現在の選択に合わせて一覧とステート表示を更新する
            _stateNameMultilineLastSyncedState = null;
            RefreshSelection();
            Repaint();
        }

        private void OnFocus()
        {
            Repaint();
        }

        private void OnSelectionChange()
        {
            RefreshSelection();
            Repaint();
        }

        private void OnDisable()
        {
            foreach (var ed in _behaviourEditors.Values)
            {
                if (ed != null)
                    DestroyImmediate(ed);
            }
            _behaviourEditors.Clear();
        }

        private void OnGUI()
        {
            SamirinEditorStyleHelper.DrawWithBlueBackground(() =>
            {
                EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
                DrawToolbar();

                _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
                DrawMainContent();
                EditorGUILayout.EndScrollView();

                EditorGUILayout.EndVertical();
            }, new Rect(0, 0, position.width, position.height));
        }

        private void DrawToolbar()
        {
            // using (new EditorGUILayout.HorizontalScope())
            // {
            //     if (GUILayout.Button("選択を再読み込み", GUILayout.Width(160f)))
            //         RefreshSelection();

            //     GUILayout.Space(8f);
            //     EditorGUILayout.LabelField("表示", GUILayout.Width(32f));
            //     TogglePanel(ref _visiblePanels, PanelVisibility.TransitionList, "一覧", 52f);
            //     TogglePanel(ref _visiblePanels, PanelVisibility.SettingsAndConditions, "設定・条件", 88f);
            //     TogglePanel(ref _visiblePanels, PanelVisibility.Clipboard, "クリップボード", 100f);

            //     GUILayout.FlexibleSpace();
            // }

            // EditorGUILayout.Space(6f);
        }

        private static void TogglePanel(ref PanelVisibility flags, PanelVisibility bit, string label, float width)
        {
            var on = (flags & bit) != 0;
            var n = EditorGUILayout.ToggleLeft(label, on, GUILayout.Width(width));
            if (n == on)
                return;
            if (n)
                flags |= bit;
            else
                flags &= ~bit;
        }

        private void DrawMainContent()
        {
            // var showList = (_visiblePanels & PanelVisibility.TransitionList) != 0;
            // var showSettings = (_visiblePanels & PanelVisibility.SettingsAndConditions) != 0;
            // var showClip = (_visiblePanels & PanelVisibility.Clipboard) != 0;

            var showList = true;
            var showSettings = true;
            var showClip = false;

            DrawSetSubStateDefaultButtonIfNeeded();
            DrawSelectedStateEditor();
            DrawSelectedSubStateMachineNameEditor();
            DrawSelectedBlendTreeEditor();

            var hideTransitionListPanel = ShouldHideTransitionListPanelForSelection();

            if (!hideTransitionListPanel)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
                if (showList)
                {
                    if (_outgoing.Count == 0 && _incoming.Count == 0)
                    {
                        HelpBoxFullWidth(
                            "トランジション、またはステートを選択してください。",
                            MessageType.Info);
                    }
                    else
                    {
                        var outgoingTitle = IsSelectionTransitionOnly() ? "選択トランジション" : "外向きトランジション";
                        DrawEdgeSection(outgoingTitle, _outgoing, ref _reorderOutgoing, true);
                        DrawEdgeSection("内向きトランジション", _incoming, ref _reorderIncoming, false);
                    }
                }

                if (showSettings)
                {
                    DrawTransitionSettingsEditor();
                    if (showClip)
                        EditorGUILayout.Space(10f);
                }
                EditorGUILayout.EndVertical();
            }

            DrawSelectedStateBehaviourSection();

            if (showClip)
            {
                if (showList || showSettings)
                    EditorGUILayout.Space(12f);
                DrawClipboardSummary();
            }
        }

        private static bool ShouldHideTransitionListPanelForSelection()
        {
            if (Selection.activeObject is BlendTree)
                return true;

            if (Selection.activeObject is not AnimatorStateMachine sm)
                return false;

            var path = AssetDatabase.GetAssetPath(sm);
            if (string.IsNullOrEmpty(path))
                return false;

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
                return false;

            return IsNestedStateMachine(sm, controller);
        }
    }
}
