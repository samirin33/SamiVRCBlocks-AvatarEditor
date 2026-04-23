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

namespace Samirin33.AvatarEditor.Tools.Editor
{
    /// <summary>
    /// 選択中トランジションの設定順序をドラッグ&amp;ドロップで入れ替えるウィンドウ。
    /// ステート選択時は Outgoing / Incoming を分けて表示する。
    /// 同じ親配列（State / Any State / Entry）内の選択要素だけを差し替える。
    /// </summary>
    public sealed class AnimatorTransitionManager : EditorWindow
    {
        private enum GroupKind
        {
            State,
            AnyState,
            Entry
        }

        private sealed class TransitionGroup
        {
            public AnimatorController controller;
            public AnimatorStateMachine stateMachine;
            public AnimatorState sourceState;
            public GroupKind kind;

            public string GroupId
            {
                get
                {
                    var c = controller != null ? controller.GetInstanceID() : 0;
                    var sm = stateMachine != null ? stateMachine.GetInstanceID() : 0;
                    var st = sourceState != null ? sourceState.GetInstanceID() : 0;
                    return $"{c}:{sm}:{st}:{kind}";
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

        [MenuItem("samirin33 Editor Tools/AnimatorStateController", false, 110)]
        public static void Open()
        {
            var window = GetWindow<AnimatorTransitionManager>("AnimatorStateController");
            window.minSize = new Vector2(560f, 320f);
            window.RefreshSelection();
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
            });
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

            DrawSelectedStateBehaviourSection();

            if (showClip)
            {
                if (showList || showSettings)
                    EditorGUILayout.Space(12f);
                DrawClipboardSummary();
            }
        }

        private void DrawSelectedStateEditor()
        {
            if (Selection.activeObject is not AnimatorState state)
                return;

            var path = AssetDatabase.GetAssetPath(state);
            if (string.IsNullOrEmpty(path))
                return;

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField("ステート設定", EditorStyles.label);

            var changed = false;
            var newName = EditorGUILayout.TextField("ステート名", state.name);
            var newMotion = (Motion)EditorGUILayout.ObjectField("AnimationClip / Motion", state.motion, typeof(Motion), false);
            var useMotionTime = state.timeParameterActive;

            var newSpeed = state.speed;
            var newSpeedMultiplierEnabled = state.speedParameterActive;
            var newSpeedMultiplierParameter = state.speedParameter;
            var newMotionTimeParameter = state.timeParameter;

            if (!useMotionTime)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    newSpeed = EditorGUILayout.FloatField("Speed", state.speed);
                    newSpeedMultiplierEnabled = EditorGUILayout.ToggleLeft("Multiplier", state.speedParameterActive, GUILayout.Width(90f));
                }

                if (newSpeedMultiplierEnabled)
                {
                    var floatParams = GetFloatParameterNames(controller);
                    newSpeedMultiplierParameter = DrawFloatParameterPopup("Multiplier", state.speedParameter, floatParams);
                }
            }

            useMotionTime = EditorGUILayout.ToggleLeft("MotionTime を使用", useMotionTime);
            if (useMotionTime)
            {
                var floatParams = GetFloatParameterNames(controller);
                newMotionTimeParameter = DrawFloatParameterPopup("MotionTime", state.timeParameter, floatParams);
            }

            var newWriteDefaults = EditorGUILayout.Toggle("Write Default", state.writeDefaultValues);

            if (newName != state.name) changed = true;
            if (newMotion != state.motion) changed = true;
            if (useMotionTime != state.timeParameterActive) changed = true;
            if (!useMotionTime && !Mathf.Approximately(newSpeed, state.speed)) changed = true;
            if (!useMotionTime && newSpeedMultiplierEnabled != state.speedParameterActive) changed = true;
            if (!useMotionTime && newSpeedMultiplierEnabled && newSpeedMultiplierParameter != state.speedParameter) changed = true;
            if (useMotionTime && newMotionTimeParameter != state.timeParameter) changed = true;
            if (newWriteDefaults != state.writeDefaultValues) changed = true;

            if (changed)
            {
                Undo.RecordObject(state, "Edit Animator State");
                state.name = newName;
                state.motion = newMotion;
                state.timeParameterActive = useMotionTime;
                if (useMotionTime)
                {
                    state.timeParameter = newMotionTimeParameter;
                    state.speedParameterActive = false;
                }
                else
                {
                    state.speed = newSpeed;
                    state.speedParameterActive = newSpeedMultiplierEnabled;
                    state.speedParameter = newSpeedMultiplierEnabled ? newSpeedMultiplierParameter : string.Empty;
                }

                state.writeDefaultValues = newWriteDefaults;
                EditorUtility.SetDirty(state);
                EditorUtility.SetDirty(controller);
                InternalEditorUtility.RepaintAllViews();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8f);
        }

        private void DrawSelectedStateBehaviourSection()
        {
            var selected = CollectSelectedStatesWithController();
            if (selected.Count == 0)
                return;

            DrawStateBehaviourSection(selected);
        }

        private List<(AnimatorState state, AnimatorController controller)> CollectSelectedStatesWithController()
        {
            var result = new List<(AnimatorState state, AnimatorController controller)>();
            var seen = new HashSet<int>();
            void TryAdd(Object o)
            {
                if (o is not AnimatorState st) return;
                if (!seen.Add(st.GetInstanceID())) return;
                var path = AssetDatabase.GetAssetPath(st);
                if (string.IsNullOrEmpty(path)) return;
                var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (ctrl == null) return;
                result.Add((st, ctrl));
            }

            foreach (var o in Selection.objects)
                TryAdd(o);
            foreach (var id in Selection.instanceIDs)
                TryAdd(EditorUtility.InstanceIDToObject(id));
            TryAdd(Selection.activeObject);

            return result;
        }

        private void DrawStateBehaviourSection(List<(AnimatorState state, AnimatorController controller)> selectedStates)
        {
            if (selectedStates == null || selectedStates.Count == 0)
                return;

            var allStates = selectedStates.Select(s => s.state).Where(s => s != null).ToList();
            var controllers = selectedStates.Select(s => s.controller).Where(c => c != null).Distinct().ToList();
            if (allStates.Count == 0 || controllers.Count == 0)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField(
                selectedStates.Count > 1 ? $"Behaviour ({selectedStates.Count} ステート選択中)" : "Behaviour",
                EditorStyles.label);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("追加", GUILayout.Width(80f)))
                    ShowAddBehaviourMenu(selectedStates);

                var copiedType = Type.GetType(AnimatorBehivaourCopy.GetCopiedTypeName() ?? "", false);
                var canPasteAsNew = AnimatorBehivaourCopy.HasCopiedBehaviour &&
                                    copiedType != null &&
                                    typeof(StateMachineBehaviour).IsAssignableFrom(copiedType);
                EditorGUI.BeginDisabledGroup(!canPasteAsNew);
                if (GUILayout.Button("ペースト(新規)", GUILayout.Width(110f)) && canPasteAsNew)
                {
                    Undo.RegisterCompleteObjectUndo(controllers.ToArray(), "Paste StateMachineBehaviour As New");
                    foreach (var st in allStates)
                        AnimatorBehivaourCopy.PasteAsNew(st, copiedType);
                    foreach (var c in controllers)
                        EditorUtility.SetDirty(c);
                    RefreshSelection();
                    InternalEditorUtility.RepaintAllViews();
                }
                EditorGUI.EndDisabledGroup();
                GUILayout.FlexibleSpace();
            }

            var maxBehaviourCount = allStates.Max(s => s.behaviours?.Length ?? 0);
            if (maxBehaviourCount == 0)
            {
                HelpBoxFullWidth("このステートには Behaviour が登録されていません。", MessageType.Info);
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(8f);
                return;
            }

            var allExisting = new List<StateMachineBehaviour>();
            for (var i = 0; i < maxBehaviourCount; i++)
            {
                var perIndex = allStates
                    .Select(s =>
                    {
                        var arr = s.behaviours ?? Array.Empty<StateMachineBehaviour>();
                        return i < arr.Length ? arr[i] : null;
                    })
                    .ToList();
                var existing = perIndex.Where(b => b != null).ToList();
                if (existing.Count == 0)
                    continue;
                allExisting.AddRange(existing);
            }
            CleanupBehaviourEditors(allExisting.ToArray());

            for (var i = 0; i < maxBehaviourCount; i++)
            {
                var perIndex = allStates
                    .Select(s =>
                    {
                        var arr = s.behaviours ?? Array.Empty<StateMachineBehaviour>();
                        return i < arr.Length ? arr[i] : null;
                    })
                    .ToList();
                var existing = perIndex.Where(b => b != null).ToList();
                if (existing.Count == 0)
                    continue;
                var representative = existing[0];
                var allHave = existing.Count == allStates.Count;
                var sameType = existing.All(b => b.GetType() == representative.GetType()) && allHave;
                var deleted = false;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
                using (new EditorGUILayout.HorizontalScope())
                {
                    var rowIndex = i;
                    var typeLabel = sameType ? representative.GetType().Name : "Mixed / Missing";
                    EditorGUILayout.LabelField($"{i + 1}. {typeLabel}", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("↕", GUILayout.Width(30f)))
                    {
                        var menu = new GenericMenu();
                        var canMoveUp = CanMoveStateBehaviour(selectedStates, rowIndex, -1);
                        var canMoveDown = CanMoveStateBehaviour(selectedStates, rowIndex, 1);
                        if (canMoveUp)
                        {
                            menu.AddItem(new GUIContent("上へ"), false,
                                () => MoveStateBehaviourAt(selectedStates, rowIndex, -1));
                        }
                        else
                        {
                            menu.AddDisabledItem(new GUIContent("上へ"));
                        }

                        if (canMoveDown)
                        {
                            menu.AddItem(new GUIContent("下へ"), false,
                                () => MoveStateBehaviourAt(selectedStates, rowIndex, 1));
                        }
                        else
                        {
                            menu.AddDisabledItem(new GUIContent("下へ"));
                        }

                        menu.ShowAsContext();
                    }

                    if (GUILayout.Button("Copy", GUILayout.Width(52f)))
                        AnimatorBehivaourCopy.Copy(representative);

                    var canPasteValues = AnimatorBehivaourCopy.HasCopiedBehaviour &&
                                         sameType &&
                                         AnimatorBehivaourCopy.IsCopiedTypeMatch(representative.GetType());
                    EditorGUI.BeginDisabledGroup(!canPasteValues);
                    if (GUILayout.Button("Paste", GUILayout.Width(52f)) && canPasteValues)
                    {
                        Undo.RegisterCompleteObjectUndo(existing.ToArray(), "Paste StateMachineBehaviour Values");
                        foreach (var b in existing)
                            AnimatorBehivaourCopy.PasteValues(b);
                        foreach (var c in controllers)
                            EditorUtility.SetDirty(c);
                        InternalEditorUtility.RepaintAllViews();
                    }
                    EditorGUI.EndDisabledGroup();

                    if (GUILayout.Button("削除", GUILayout.Width(52f)))
                    {
                        RemoveStateBehaviourAt(selectedStates, i);
                        deleted = true;
                    }
                }

                if (deleted)
                {
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(8f);
                    return;
                }

                if (!sameType)
                {
                    HelpBoxFullWidth("同時編集するには、全選択ステートで同じ index に同じ型の Behaviour が必要です。", MessageType.Info);
                }
                else
                {
                    var editor = GetOrCreateBehaviourEditor(representative);
                    if (editor != null)
                    {
                        EditorGUI.BeginChangeCheck();
                        editor.OnInspectorGUI();
                        if (EditorGUI.EndChangeCheck())
                        {
                            // 複数選択時は代表1件のカスタムInspectorを表示し、変更後に同indexへ値を同期する。
                            if (existing.Count > 1)
                            {
                                var json = EditorJsonUtility.ToJson(representative);
                                var others = existing.Where(b => b != null && !ReferenceEquals(b, representative)).ToArray();
                                if (others.Length > 0)
                                    Undo.RegisterCompleteObjectUndo(others, "Sync StateMachineBehaviour Values");
                                foreach (var other in others)
                                    EditorJsonUtility.FromJsonOverwrite(json, other);
                            }

                            foreach (var b in existing)
                                EditorUtility.SetDirty(b);
                            foreach (var c in controllers)
                                EditorUtility.SetDirty(c);
                        }
                    }
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8f);
        }

        private void ShowAddBehaviourMenu(List<(AnimatorState state, AnimatorController controller)> selectedStates)
        {
            var menu = new GenericMenu();
            var controllers = selectedStates.Select(s => s.controller).Where(c => c != null).Distinct().ToArray();
            var states = selectedStates.Select(s => s.state).Where(s => s != null).ToArray();
            var types = TypeCache.GetTypesDerivedFrom<StateMachineBehaviour>()
                .Where(t => t != null && !t.IsAbstract && !t.IsGenericType && t.IsClass)
                .OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (types.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("(追加可能な Behaviour がありません)"));
            }
            else
            {
                foreach (var type in types)
                {
                    var captured = type;
                    var ns = captured.Namespace ?? string.Empty;
                    var isVrcSdkType =
                        ns.IndexOf("VRC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        captured.Name.IndexOf("VRC", StringComparison.OrdinalIgnoreCase) >= 0;
                    var label = isVrcSdkType
                        ? $"{captured.Name} ({ns})"
                        : (captured.FullName?.Replace('.', '/') ?? captured.Name);
                    menu.AddItem(new GUIContent(label), false, () =>
                    {
                        Undo.RegisterCompleteObjectUndo(controllers, "Add StateMachineBehaviour");
                        foreach (var st in states)
                            st.AddStateMachineBehaviour(captured);
                        foreach (var c in controllers)
                            EditorUtility.SetDirty(c);
                        RefreshSelection();
                        InternalEditorUtility.RepaintAllViews();
                    });
                }
            }

            menu.ShowAsContext();
        }

        private void RemoveStateBehaviourAt(List<(AnimatorState state, AnimatorController controller)> selectedStates, int index)
        {
            if (selectedStates == null || selectedStates.Count == 0)
                return;
            var controllers = selectedStates.Select(s => s.controller).Where(c => c != null).Distinct().ToArray();
            if (controllers.Length == 0)
                return;
            Undo.RegisterCompleteObjectUndo(controllers, "Remove StateMachineBehaviour");

            foreach (var pair in selectedStates)
            {
                var state = pair.state;
                if (state == null)
                    continue;
                var src = state.behaviours ?? Array.Empty<StateMachineBehaviour>();
                if (index < 0 || index >= src.Length)
                    continue;

                var target = src[index];
                state.behaviours = src.Where((_, i) => i != index).ToArray();
                if (target != null)
                {
                    var id = target.GetInstanceID();
                    if (_behaviourEditors.TryGetValue(id, out var ed) && ed != null)
                        DestroyImmediate(ed);
                    _behaviourEditors.Remove(id);
                    DestroyImmediate(target, true);
                }
            }

            foreach (var c in controllers)
                EditorUtility.SetDirty(c);
            RefreshSelection();
            InternalEditorUtility.RepaintAllViews();
        }

        private static bool CanMoveStateBehaviour(
            List<(AnimatorState state, AnimatorController controller)> selectedStates,
            int index,
            int direction)
        {
            if (selectedStates == null || selectedStates.Count == 0 || direction == 0)
                return false;

            foreach (var pair in selectedStates)
            {
                var state = pair.state;
                if (state == null)
                    continue;
                var arr = state.behaviours ?? Array.Empty<StateMachineBehaviour>();
                var targetIndex = index + direction;
                if (index < 0 || index >= arr.Length || targetIndex < 0 || targetIndex >= arr.Length)
                    return false;
            }

            return true;
        }

        private void MoveStateBehaviourAt(
            List<(AnimatorState state, AnimatorController controller)> selectedStates,
            int index,
            int direction)
        {
            if (!CanMoveStateBehaviour(selectedStates, index, direction))
                return;

            var controllers = selectedStates.Select(s => s.controller).Where(c => c != null).Distinct().ToArray();
            if (controllers.Length == 0)
                return;

            var targetIndex = index + direction;
            Undo.RegisterCompleteObjectUndo(controllers, "Reorder StateMachineBehaviour");

            foreach (var pair in selectedStates)
            {
                var state = pair.state;
                if (state == null)
                    continue;
                var arr = state.behaviours ?? Array.Empty<StateMachineBehaviour>();
                if (index < 0 || index >= arr.Length || targetIndex < 0 || targetIndex >= arr.Length)
                    continue;

                (arr[index], arr[targetIndex]) = (arr[targetIndex], arr[index]);
                state.behaviours = arr;
            }

            foreach (var c in controllers)
                EditorUtility.SetDirty(c);
            RefreshSelection();
            InternalEditorUtility.RepaintAllViews();
        }

        private UnityEditor.Editor GetOrCreateBehaviourEditor(StateMachineBehaviour behaviour)
        {
            if (behaviour == null)
                return null;
            var id = behaviour.GetInstanceID();
            if (_behaviourEditors.TryGetValue(id, out var existing) && existing != null && existing.target == behaviour)
                return existing;

            if (existing != null)
                DestroyImmediate(existing);
            var editor = UnityEditor.Editor.CreateEditor(behaviour);
            _behaviourEditors[id] = editor;
            return editor;
        }

        private void CleanupBehaviourEditors(StateMachineBehaviour[] currentBehaviours)
        {
            var validIds = new HashSet<int>(currentBehaviours.Where(b => b != null).Select(b => b.GetInstanceID()));
            var staleIds = _behaviourEditors.Keys.Where(id => !validIds.Contains(id)).ToList();
            foreach (var id in staleIds)
            {
                if (_behaviourEditors.TryGetValue(id, out var ed) && ed != null)
                    DestroyImmediate(ed);
                _behaviourEditors.Remove(id);
            }
        }

        private static string DrawFloatParameterPopup(string label, string current, List<string> parameterNames)
        {
            if (parameterNames.Count == 0)
            {
                EditorGUILayout.HelpBox("Float パラメータがありません。Animator Controller に追加してください。", MessageType.Warning, true);
                return string.Empty;
            }

            var index = parameterNames.IndexOf(current);
            if (index < 0) index = 0;
            var next = EditorGUILayout.Popup(label, index, parameterNames.ToArray());
            return parameterNames[Mathf.Clamp(next, 0, parameterNames.Count - 1)];
        }

        private static List<string> GetFloatParameterNames(AnimatorController controller)
        {
            var result = new List<string>();
            if (controller == null || controller.parameters == null)
                return result;

            foreach (var p in controller.parameters)
            {
                if (p != null && p.type == AnimatorControllerParameterType.Float && !string.IsNullOrEmpty(p.name))
                    result.Add(p.name);
            }

            return result;
        }

        private void DrawSetSubStateDefaultButtonIfNeeded()
        {
            if (!TryGetSubStateDefaultActionContext(out var ctx))
                return;

            if (ctx.parentStateMachine.defaultState == ctx.selectedState)
                return;

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("サブステート内のデフォルトステートにする", GUILayout.MinWidth(300f), GUILayout.Height(25f)))
                SetSubStateMachineDefaultStateAndMoveToOldest(ctx);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(8f);
        }

        private static bool TryGetSubStateDefaultActionContext(out SubStateDefaultActionContext ctx)
        {
            ctx = null;
            if (Selection.activeObject is not AnimatorState selectedState)
                return false;

            var path = AssetDatabase.GetAssetPath(selectedState);
            if (string.IsNullOrEmpty(path))
                return false;

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
                return false;

            foreach (var layer in controller.layers)
            {
                var root = layer.stateMachine;
                if (root == null)
                    continue;

                if (!TryFindContainingStateMachine(root, selectedState, out var parentStateMachine))
                    continue;

                // ルート直下は「サブステート内」ではないため対象外。
                if (ReferenceEquals(parentStateMachine, root))
                    return false;

                ctx = new SubStateDefaultActionContext
                {
                    controller = controller,
                    parentStateMachine = parentStateMachine,
                    selectedState = selectedState
                };
                return true;
            }

            return false;
        }

        private static bool TryFindContainingStateMachine(
            AnimatorStateMachine stateMachine,
            AnimatorState state,
            out AnimatorStateMachine parentStateMachine)
        {
            parentStateMachine = null;
            if (stateMachine == null || state == null)
                return false;

            foreach (var child in stateMachine.states)
            {
                if (child.state != state)
                    continue;
                parentStateMachine = stateMachine;
                return true;
            }

            foreach (var sub in stateMachine.stateMachines)
            {
                if (TryFindContainingStateMachine(sub.stateMachine, state, out parentStateMachine))
                    return true;
            }

            return false;
        }

        private void SetSubStateMachineDefaultStateAndMoveToOldest(SubStateDefaultActionContext ctx)
        {
            if (ctx == null || ctx.controller == null || ctx.parentStateMachine == null || ctx.selectedState == null)
                return;

            Undo.RegisterCompleteObjectUndo(ctx.controller, "Set SubStateMachine Default State");

            var states = ctx.parentStateMachine.states;
            var selectedIndex = -1;
            for (var i = 0; i < states.Length; i++)
            {
                if (states[i].state == ctx.selectedState)
                {
                    selectedIndex = i;
                    break;
                }
            }

            if (selectedIndex < 0)
                return;

            if (selectedIndex > 0)
            {
                var reordered = new ChildAnimatorState[states.Length];
                reordered[0] = states[selectedIndex];
                var w = 1;
                for (var i = 0; i < states.Length; i++)
                {
                    if (i == selectedIndex)
                        continue;
                    reordered[w++] = states[i];
                }

                ctx.parentStateMachine.states = reordered;
            }

            ctx.parentStateMachine.defaultState = ctx.selectedState;
            EditorUtility.SetDirty(ctx.controller);
            AssetDatabase.SaveAssets();
            RefreshSelection();
            Repaint();
            InternalEditorUtility.RepaintAllViews();
        }

        /// <summary>
        /// ステートが選ばれておらず、Animator のトランジションが選ばれているとき true（一覧見出しを「選択トランジション」にする）。
        /// </summary>
        private static bool IsSelectionTransitionOnly()
        {
            if (HasAnimatorStateInSelection())
                return false;

            return HasAnimatorTransitionInSelection();
        }

        private static bool HasAnimatorStateInSelection()
        {
            foreach (var o in Selection.objects)
            {
                if (o is AnimatorState)
                    return true;
            }

            foreach (var id in Selection.instanceIDs)
            {
                var o = EditorUtility.InstanceIDToObject(id);
                if (o is AnimatorState)
                    return true;
            }

            return Selection.activeObject is AnimatorState;
        }

        private static bool HasAnimatorTransitionInSelection()
        {
            foreach (var o in Selection.objects)
            {
                if (o is AnimatorTransitionBase)
                    return true;
            }

            foreach (var id in Selection.instanceIDs)
            {
                var o = EditorUtility.InstanceIDToObject(id);
                if (o is AnimatorTransitionBase)
                    return true;
            }

            return Selection.activeObject is AnimatorTransitionBase;
        }

        private void DrawEdgeSection(string title, List<TransitionRow> rows, ref ReorderableList reorderable, bool isOutgoingBucket)
        {
            if (rows.Count == 0)
                return;

            EditorGUILayout.LabelField(title, EditorStyles.label);

            EnsureReorderableList(rows, ref reorderable, isOutgoingBucket);
            reorderable.DoLayoutList();
            EditorGUILayout.Space(-EditorGUIUtility.standardVerticalSpacing);
            DrawAddParallelTransitionButtonRow(rows, isOutgoingBucket);
        }

        private void EnsureReorderableList(List<TransitionRow> rows, ref ReorderableList reorderable, bool isOutgoingBucket)
        {
            if (reorderable != null && reorderable.list == rows)
            {
                ApplyReorderableListCompactChrome(reorderable);
                return;
            }

            var bucket = isOutgoingBucket ? FocusedListBucket.Outgoing : FocusedListBucket.Incoming;

            reorderable = new ReorderableList(rows, typeof(TransitionRow), true, false, false, false)
            {
                drawElementCallback = (rect, index, _, _) =>
                {
                    if (index < 0 || index >= rows.Count) return;
                    var item = rows[index];
                    if (_selectionBucket == bucket && _selectedRowIndices.Contains(index))
                        EditorGUI.DrawRect(rect, new Color(0.25f, 0.45f, 0.85f, 0.18f));

                    ResolveEndpoints(item.group, item.transition,
                        out var srcLabel, out var srcObj,
                        out var dstLabel, out var dstObj);

                    var y = rect.y + 1f;
                    var h = EditorGUIUtility.singleLineHeight;
                    var x = rect.x + 12f;

                    var idxRect = new Rect(x, y, 28f, h);
                    x += 28f;
                    EditorGUI.LabelField(idxRect, $"{index + 1}.", EditorStyles.miniLabel);

                    var remaining = rect.xMax - x - 4f;
                    var iconW = 18f;
                    var gap = 4f;
                    const float actionBtnW = 22f;
                    var actionReserve = actionBtnW * 3f + gap * 2f;
                    var pairW = Mathf.Max(40f, (remaining - iconW - actionReserve - gap) * 0.5f);

                    var srcRect = new Rect(x, y, pairW, h);
                    x += pairW + gap * 0.5f;
                    var iconRect = new Rect(x, y, iconW, h);
                    x += iconW + gap * 0.5f;
                    var dstRect = new Rect(x, y, pairW, h);
                    x += pairW + gap * 0.5f;

                    var activeState = Selection.activeObject as AnimatorState;
                    var isSourceSelfState = activeState != null && ReferenceEquals(srcObj, activeState);
                    var isDestinationSelfState = activeState != null && ReferenceEquals(dstObj, activeState);

                    if (isSourceSelfState)
                        EditorGUI.LabelField(srcRect, srcLabel, CenteredLabelStyle);
                    else if (GUI.Button(srcRect, srcLabel, EditorStyles.miniButton))
                        SelectForInspector(srcObj);

                    GUI.Label(iconRect, BetweenIcon, EditorStyles.label);

                    if (isDestinationSelfState)
                        EditorGUI.LabelField(dstRect, dstLabel, CenteredLabelStyle);
                    else if (GUI.Button(dstRect, dstLabel, EditorStyles.miniButton))
                        SelectForInspector(dstObj);

                    var copyRect = new Rect(x, y, actionBtnW, h);
                    x += actionBtnW + gap;
                    var pasteOwRect = new Rect(x, y, actionBtnW, h);
                    x += actionBtnW + gap;
                    var deleteRect = new Rect(x, y, actionBtnW, h);

                    var e = Event.current;
                    var rowSelectableRect = new Rect(rect.x, rect.y, rect.width, rect.height);
                    var clickedOnInteractiveControl =
                        copyRect.Contains(e.mousePosition) ||
                        pasteOwRect.Contains(e.mousePosition) ||
                        deleteRect.Contains(e.mousePosition);

                    if (e.type == EventType.MouseDown && e.button == 0 && rowSelectableRect.Contains(e.mousePosition) &&
                        !clickedOnInteractiveControl)
                    {
                        var addToSelection = e.control || e.command;
                        if (addToSelection)
                        {
                            if (_selectionBucket != bucket)
                            {
                                _selectionBucket = bucket;
                                _selectedRowIndices.Clear();
                            }

                            if (!_selectedRowIndices.Add(index))
                                _selectedRowIndices.Remove(index);
                        }
                        else
                        {
                            _selectionBucket = bucket;
                            _selectedRowIndices.Clear();
                            _selectedRowIndices.Add(index);
                        }

                        if (isOutgoingBucket)
                        {
                            if (_reorderIncoming != null)
                                _reorderIncoming.index = -1;
                        }
                        else if (_reorderOutgoing != null)
                        {
                            _reorderOutgoing.index = -1;
                        }

                        _lastConditionBufferSignature = "";
                        e.Use();
                        Repaint();
                    }

                    if (GUI.Button(copyRect, CopyIcon))
                    {
                        AnimatorTransitionMultiCopy.CopyMergedSettings(item.transition);
                        Repaint();
                    }

                    if (GUI.Button(pasteOwRect, PasteOverwriteIcon))
                    {
                        var preservedBucket = _selectionBucket;
                        var preservedTransitionIds = GetSelectedRows()
                            .Select(r => r.transition)
                            .Where(t => t != null)
                            .Select(t => t.GetInstanceID())
                            .ToHashSet();
                        if (AnimatorTransitionMultiCopy.TryPasteMergedOverwrite(item.transition))
                        {
                            AssetDatabase.SaveAssets();
                            RefreshSelection();
                            RestoreRowSelection(preservedBucket, preservedTransitionIds);
                            InternalEditorUtility.RepaintAllViews();
                        }
                        Repaint();
                    }

                    if (GUI.Button(deleteRect, DeleteIcon))
                    {
                        var tr = item.transition;
                        EditorApplication.delayCall += () =>
                        {
                            if (tr == null)
                                return;
                            if (AnimatorTransitionEditOperations.TryDeleteTransition(tr, "Delete Transition"))
                            {
                                AssetDatabase.SaveAssets();
                                RefreshSelection();
                                Repaint();
                            }
                        };
                    }
                },
                elementHeight = EditorGUIUtility.singleLineHeight + 4f
            };

            ApplyReorderableListCompactChrome(reorderable);

            reorderable.onReorderCallbackWithDetails = (_, oldIndex, newIndex) =>
            {
                if (oldIndex == newIndex) return;
                _selectedRowIndices.Clear();
                _selectionBucket = FocusedListBucket.None;
                _lastConditionBufferSignature = "";
                ApplyOrder(rows);
                Repaint();
            };
        }

        /// <summary>
        /// 追加／削除ボタン非表示でもフッター高さが残ることがあるため、下のボタン行との隙間を詰める。
        /// </summary>
        private static void ApplyReorderableListCompactChrome(ReorderableList list)
        {
            if (list == null)
                return;
            list.footerHeight = 0f;
            list.headerHeight = 0f;
        }

        private void DrawAddParallelTransitionButtonRow(List<TransitionRow> rows, bool isOutgoingBucket)
        {
            if (rows == null || rows.Count == 0)
                return;

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button($"トランジションを追加", GUILayout.MinWidth(260f), GUILayout.Height(25f)))
                TryAddParallelTransition(rows, isOutgoingBucket);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void TryAddParallelTransition(List<TransitionRow> rows, bool isOutgoingBucket)
        {
            if (rows == null || rows.Count == 0)
                return;

            var bucket = isOutgoingBucket ? FocusedListBucket.Outgoing : FocusedListBucket.Incoming;
            TransitionRow template = null;

            if (_selectionBucket == bucket && _selectedRowIndices.Count > 0)
            {
                var ix = _selectedRowIndices.OrderBy(i => i).First();
                if (ix >= 0 && ix < rows.Count)
                    template = rows[ix];
            }

            template ??= rows[0];

            var t = template.transition;
            var c = template.group?.controller;
            if (c == null || t == null)
                return;

            var loc = AnimatorTransitionEditOperations.FindTransitionLocation(t, c);
            if (loc == null)
                return;

            Undo.RegisterCompleteObjectUndo(c, "Add Parallel Transition");
            var neu = AnimatorTransitionEditOperations.TryCreateParallelTransition(loc);
            if (neu == null)
            {
                EditorUtility.DisplayDialog("AnimatorStateController", "同じ経路のトランジションを追加できませんでした。", "OK");
                return;
            }

            EditorUtility.SetDirty(c);
            AssetDatabase.SaveAssets();
            // RefreshSelection() は Selection だけから再構築するため、未選択の新規トランジションが一覧に載らない。
            // 追加した行は現在のバケットのリストへ直接載せる（rows は _outgoing または _incoming と同一参照）。
            var g = FindGroup(neu);
            if (g != null)
            {
                rows.Add(new TransitionRow { transition = neu, group = g });
                if (isOutgoingBucket)
                    _reorderOutgoing = null;
                else
                    _reorderIncoming = null;
            }
            else
            {
                RefreshSelection();
            }

            Repaint();
            InternalEditorUtility.RepaintAllViews();
        }

        private static GUIContent BetweenIcon =>
            _cachedBetweenIcon ??= EditorGUIUtility.IconContent("d_preAudioPlayOn");

        private static GUIContent CopyIcon =>
            _cachedCopyIcon ??= EditorGUIUtility.IconContent("Grid.PickingTool", "コピー");

        private static GUIContent PasteOverwriteIcon =>
            _cachedPasteOwIcon ??= EditorGUIUtility.IconContent("Grid.FillTool", "ペースト");

        private static GUIContent DeleteIcon =>
            _cachedDeleteIcon ??= EditorGUIUtility.IconContent("winbtn_win_close", "削除");

        private void DrawTransitionSettingsEditor()
        {
            var rows = GetTransitionRowsForSettingsPanel();
            if (rows.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField("トランジション設定・条件", EditorStyles.label);

            // var usingUnselectedFallback = GetSelectedRows().Count == 0;
            // if (usingUnselectedFallback)
            //     HelpBoxFullWidth(
            //         "一覧未選択のため、先頭のトランジションの設定を表示しています（番号をクリックで選択・編集対象を切り替え）。",
            //         MessageType.Info);

            var sig = BuildSelectionSignature(rows);
            if (sig != _lastConditionBufferSignature)
            {
                _lastConditionBufferSignature = sig;
                SyncConditionBufferFromTransition(rows[0].transition);
            }

            // if (rows.Count > 1)
            //     HelpBoxFullWidth(
            //         "複数選択: 編集内容は選択されたすべてのトランジションに反映されます。値が異なる項目は「—」表示になります。",
            //         MessageType.Warning);

            var transitions = rows.Select(r => r.transition).Where(t => t != null).ToList();
            var stateTransitions = transitions.OfType<AnimatorStateTransition>().ToList();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            if (stateTransitions.Count > 0)
            {
                if (stateTransitions.Count < transitions.Count)
                    HelpBoxFullWidth(
                        "ブレンド／中断の各項目は AnimatorStateTransition にのみ適用されます（Entry 等は対象外）。",
                        MessageType.Info);
                DrawAnimatorStateTransitionBlendFields(stateTransitions);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField("条件", EditorStyles.label);
            DrawConditionListEditor(transitions);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndVertical();
        }

        private List<TransitionRow> GetSelectedRows()
        {
            if (_selectionBucket == FocusedListBucket.None)
                return new List<TransitionRow>();

            var list = _selectionBucket == FocusedListBucket.Outgoing ? _outgoing : _incoming;
            var result = new List<TransitionRow>();
            foreach (var idx in _selectedRowIndices.OrderBy(i => i))
            {
                if (idx >= 0 && idx < list.Count)
                    result.Add(list[idx]);
            }

            return result;
        }

        /// <summary>
        /// 番号で選択された行。未選択時は一覧の先頭（外向きを優先）を 1 件だけ返す。
        /// </summary>
        private List<TransitionRow> GetTransitionRowsForSettingsPanel()
        {
            var selected = GetSelectedRows();
            if (selected.Count > 0)
                return selected;

            var first = TryGetFirstListedTransitionRow();
            return first != null ? new List<TransitionRow> { first } : new List<TransitionRow>();
        }

        private TransitionRow TryGetFirstListedTransitionRow()
        {
            if (_outgoing.Count > 0)
                return _outgoing[0];
            if (_incoming.Count > 0)
                return _incoming[0];
            return null;
        }

        private static string BuildSelectionSignature(List<TransitionRow> rows)
        {
            if (rows == null || rows.Count == 0)
                return "";
            return string.Join("|", rows.Select(r => r.transition != null ? r.transition.GetInstanceID() : 0));
        }

        private void SyncConditionBufferFromTransition(AnimatorTransitionBase t)
        {
            _conditionBuffer.Clear();
            if (t?.conditions == null)
                return;
            foreach (var c in t.conditions)
            {
                _conditionBuffer.Add(new ConditionEditRow
                {
                    mode = c.mode,
                    parameter = c.parameter,
                    threshold = c.threshold
                });
            }
        }

        private static void RegisterUndoTransitions(IEnumerable<AnimatorTransitionBase> transitions, string name)
        {
            var controllers = new HashSet<AnimatorController>();
            foreach (var tr in transitions)
            {
                var c = AnimatorTransitionEditOperations.GetController(tr);
                if (c != null)
                    controllers.Add(c);
            }

            foreach (var c in controllers)
                Undo.RegisterCompleteObjectUndo(c, name);
        }

        private void DrawAnimatorStateTransitionBlendFields(List<AnimatorStateTransition> asts)
        {
            if (asts == null || asts.Count == 0)
                return;

            void ApplyFloat(System.Func<AnimatorStateTransition, float> read, System.Action<AnimatorStateTransition, float> write,
                string label)
            {
                var vals = asts.Select(read).Distinct().ToList();
                EditorGUI.showMixedValue = vals.Count > 1;
                EditorGUI.BeginChangeCheck();
                var v = EditorGUILayout.FloatField(label, vals.Count == 1 ? vals[0] : 0f);
                EditorGUI.showMixedValue = false;
                if (!EditorGUI.EndChangeCheck())
                    return;
                RegisterUndoTransitions(asts, label);
                foreach (var a in asts)
                    write(a, v);
                MarkControllersDirty(asts);
            }

            void ApplyBool(System.Func<AnimatorStateTransition, bool> read, System.Action<AnimatorStateTransition, bool> write,
                string label)
            {
                var vals = asts.Select(read).Distinct().ToList();
                EditorGUI.showMixedValue = vals.Count > 1;
                EditorGUI.BeginChangeCheck();
                var v = EditorGUILayout.Toggle(label, vals.Count == 1 && vals[0]);
                EditorGUI.showMixedValue = false;
                if (!EditorGUI.EndChangeCheck())
                    return;
                RegisterUndoTransitions(asts, label);
                foreach (var a in asts)
                    write(a, v);
                MarkControllersDirty(asts);
            }

            bool? GetCommonBool(System.Func<AnimatorStateTransition, bool> read)
            {
                var vals = asts.Select(read).Distinct().ToList();
                return vals.Count == 1 ? vals[0] : (bool?)null;
            }

            TransitionInterruptionSource? GetCommonInterruptionSource()
            {
                var vals = asts.Select(a => a.interruptionSource).Distinct().ToList();
                return vals.Count == 1 ? vals[0] : (TransitionInterruptionSource?)null;
            }

            void ApplyEnum(System.Func<AnimatorStateTransition, TransitionInterruptionSource> read,
                System.Action<AnimatorStateTransition, TransitionInterruptionSource> write, string label)
            {
                var vals = asts.Select(read).Distinct().ToList();
                EditorGUI.showMixedValue = vals.Count > 1;
                EditorGUI.BeginChangeCheck();
                var v = (TransitionInterruptionSource)EditorGUILayout.EnumPopup(label,
                    vals.Count == 1 ? vals[0] : TransitionInterruptionSource.None);
                EditorGUI.showMixedValue = false;
                if (!EditorGUI.EndChangeCheck())
                    return;
                RegisterUndoTransitions(asts, label);
                foreach (var a in asts)
                    write(a, v);
                MarkControllersDirty(asts);
            }

            ApplyFloat(a => a.duration, (a, v) => a.duration = v, "Duration");
            ApplyFloat(a => a.offset, (a, v) => a.offset = v, "Offset");

            using (new EditorGUILayout.HorizontalScope())
            {
                ApplyBool(a => a.hasExitTime, (a, v) => a.hasExitTime = v, "Has Exit Time");
                var hasExitTime = GetCommonBool(a => a.hasExitTime);
                if (hasExitTime == true)
                    ApplyBool(a => a.hasFixedDuration, (a, v) => a.hasFixedDuration = v, "Fixed Duration");
            }

            if (GetCommonBool(a => a.hasExitTime) == true)
                ApplyFloat(a => a.exitTime, (a, v) => a.exitTime = v, "Exit Time");

            using (new EditorGUILayout.HorizontalScope())
            {
                ApplyEnum(a => a.interruptionSource, (a, v) => a.interruptionSource = v, "Interruption Source");
                var interruptionSource = GetCommonInterruptionSource();
                if (interruptionSource.HasValue && interruptionSource.Value != TransitionInterruptionSource.None)
                    ApplyBool(a => a.orderedInterruption, (a, v) => a.orderedInterruption = v, "Ordered Interruption");
            }

            if (asts.All(AnimatorTransitionEditOperations.IsAnyStateTransition))
                ApplyBool(a => a.canTransitionToSelf, (a, v) => a.canTransitionToSelf = v, "Can Transition To Self");
        }

        private static void MarkControllersDirty(IEnumerable<AnimatorStateTransition> asts)
        {
            var controllers = new HashSet<AnimatorController>();
            foreach (var a in asts)
            {
                var c = AnimatorTransitionEditOperations.GetController(a);
                if (c != null)
                    controllers.Add(c);
            }

            foreach (var c in controllers)
                EditorUtility.SetDirty(c);
        }

        private void DrawConditionListEditor(List<AnimatorTransitionBase> transitions)
        {
            if (transitions == null || transitions.Count == 0)
                return;

            var menuControllers = GetControllersForParameterMenu();
            if (menuControllers.Count == 0)
                menuControllers = CollectAnimatorControllers(transitions);

            _conditionEditTargetTransitions = transitions;
            _conditionEditMenuControllers = menuControllers;

            EnsureConditionReorderableList();
            _reorderConditions.DoLayoutList();

            if (GUILayout.Button("条件を追加"))
            {
                RegisterUndoTransitions(transitions, "Add Animator Condition");
                var transitionCtrls = CollectAnimatorControllers(transitions);
                var newRow = CreateConditionRowOnAdd(menuControllers, transitionCtrls, _conditionBuffer);
                var ctrlsForType = menuControllers.Count > 0 ? menuControllers : transitionCtrls;
                var pType = ResolveParameterType(ctrlsForType, newRow.parameter);
                if (!string.IsNullOrEmpty(newRow.parameter))
                    newRow.threshold = SanitizeThresholdForMode(pType, newRow.mode, newRow.threshold);
                _conditionBuffer.Add(newRow);
                ApplyConditionsToTransitions(transitions, _conditionBuffer);
                MarkTransitionsDirty(transitions);
            }
        }

        private void EnsureConditionReorderableList()
        {
            if (_reorderConditions != null && ReferenceEquals(_reorderConditions.list, _conditionBuffer))
            {
                ApplyReorderableListCompactChrome(_reorderConditions);
                return;
            }

            _reorderConditions = new ReorderableList(_conditionBuffer, typeof(ConditionEditRow), true, false, false, false)
            {
                drawElementCallback = DrawConditionReorderElement,
                elementHeightCallback = _ => ConditionRowElementHeight
            };

            ApplyReorderableListCompactChrome(_reorderConditions);

            _reorderConditions.onReorderCallbackWithDetails = (_, oldIndex, newIndex) =>
            {
                if (oldIndex == newIndex)
                    return;
                var tr = _conditionEditTargetTransitions;
                if (tr == null || tr.Count == 0)
                    return;
                RegisterUndoTransitions(tr, "Reorder Animator Conditions");
                ApplyConditionsToTransitions(tr, _conditionBuffer);
                MarkTransitionsDirty(tr);
            };
        }

        private static float ConditionRowElementHeight =>
            EditorGUIUtility.singleLineHeight + 4f;

        private void DrawConditionReorderElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            var transitions = _conditionEditTargetTransitions;
            var menuControllers = _conditionEditMenuControllers;
            if (transitions == null || index < 0 || index >= _conditionBuffer.Count)
                return;

            if (menuControllers == null)
                menuControllers = new List<AnimatorController>();

            DrawConditionRowRectEditorGui(rect, index, transitions, menuControllers);
        }

        /// <summary>
        /// ReorderableList 要素は GUILayout ではなく EditorGUI（Rect 指定）のみで描画する（表示されない問題の回避）。
        /// </summary>
        private void DrawConditionRowRectEditorGui(Rect outer, int idx, List<AnimatorTransitionBase> transitions,
            List<AnimatorController> menuControllers)
        {
            var row = _conditionBuffer[idx];
            var newMode = row.mode;
            var newParam = row.parameter;
            var newTh = row.threshold;

            var pType = ResolveParameterType(menuControllers, newParam);
            var allowedModes = GetAllowedModes(pType);
            if (!allowedModes.Contains(newMode))
                newMode = allowedModes[0];

            var modeLabels = allowedModes.Select(m => new GUIContent(FormatConditionModeDisplay(m))).ToArray();
            var modeIdx = Mathf.Max(0, Array.IndexOf(allowedModes, newMode));

            var pad = 2f;
            var lineH = EditorGUIUtility.singleLineHeight;
            var inner = new Rect(outer.x + pad, outer.y + pad, outer.width - pad * 2f, outer.height - pad * 2f);
            if (inner.width < 10f || inner.height < lineH)
                return;

            EditorGUI.DrawRect(new Rect(outer.x + 1f, outer.y + 1f, outer.width - 2f, outer.height - 2f),
                EditorGUIUtility.isProSkin ? new Color(0.18f, 0.18f, 0.18f, 0.35f) : new Color(0.85f, 0.85f, 0.85f, 0.5f));

            const float modeW = 128f;
            const float pickW = 22f;
            const float delW = 44f;
            const float gap = 4f;
            var y = inner.y + (inner.height - lineH) * 0.5f;
            const float valueClusterW = 140f;

            var rDel = new Rect(inner.xMax - pickW, y, pickW, lineH);
            var rVal = new Rect(rDel.x - gap - valueClusterW, y, valueClusterW, lineH);
            var rMode = new Rect(rVal.x - gap - modeW, y, modeW, lineH);
            var rPick = new Rect(rMode.x - gap - pickW, y, pickW, lineH);
            var paramLeft = inner.x;
            var paramW = Mathf.Max(32f, rPick.x - gap - paramLeft);
            var rParam = new Rect(paramLeft, y, paramW, lineH);

            EditorGUI.BeginChangeCheck();
            newParam = EditorGUI.TextField(rParam, newParam);
            var paramChanged = EditorGUI.EndChangeCheck();
            if (paramChanged)
            {
                pType = ResolveParameterType(menuControllers, newParam);
                allowedModes = GetAllowedModes(pType);
                if (!allowedModes.Contains(newMode))
                    newMode = allowedModes[0];
            }

            if (GUI.Button(rPick, new GUIContent("▾")))
            {
                ShowParameterHierarchyMenu(rPick, menuControllers, pickedName =>
                {
                    RegisterUndoTransitions(transitions, "Set Animator Condition Parameter");
                    pType = ResolveParameterType(menuControllers, pickedName);
                    var modes = GetAllowedModes(pType);
                    var m = newMode;
                    if (!modes.Contains(m))
                        m = modes[0];
                    var th = SanitizeThresholdForMode(pType, m, newTh);
                    _conditionBuffer[idx] = new ConditionEditRow
                    {
                        mode = m,
                        parameter = pickedName,
                        threshold = th
                    };
                    ApplyConditionsToTransitions(transitions, _conditionBuffer);
                    MarkTransitionsDirty(transitions);
                });
            }

            EditorGUI.BeginChangeCheck();
            var picked = EditorGUI.Popup(rMode, modeIdx, modeLabels);
            var modePopupChanged = EditorGUI.EndChangeCheck();
            if (modePopupChanged && picked >= 0 && picked < allowedModes.Length)
                newMode = allowedModes[picked];

            var valueChanged = DrawConditionValueFieldRect(rVal, pType, ref newMode, ref newTh);

            if (GUI.Button(rDel, EditorGUIUtility.IconContent("winbtn_win_close")))
            {
                var captureIdx = idx;
                var captureTrans = transitions;
                EditorApplication.delayCall += () =>
                {
                    if (captureTrans == null || captureIdx < 0 || captureIdx >= _conditionBuffer.Count)
                        return;
                    RegisterUndoTransitions(captureTrans, "Remove Animator Condition");
                    _conditionBuffer.RemoveAt(captureIdx);
                    ApplyConditionsToTransitions(captureTrans, _conditionBuffer);
                    MarkTransitionsDirty(captureTrans);
                };
            }

            if (modePopupChanged || paramChanged || valueChanged || newMode != row.mode ||
                !string.Equals(newParam, row.parameter) || Mathf.Abs(newTh - row.threshold) > 0.0001f)
            {
                newTh = SanitizeThresholdForMode(pType, newMode, newTh);
                _conditionBuffer[idx] = new ConditionEditRow
                {
                    mode = newMode,
                    parameter = newParam,
                    threshold = newTh
                };
                RegisterUndoTransitions(transitions, "Edit Animator Condition");
                ApplyConditionsToTransitions(transitions, _conditionBuffer);
                MarkTransitionsDirty(transitions);
            }
        }

        private static bool DrawConditionValueFieldRect(Rect r, AnimatorControllerParameterType? pType,
            ref AnimatorConditionMode mode, ref float threshold)
        {
            var lineH = EditorGUIUtility.singleLineHeight;
            switch (pType)
            {
                case AnimatorControllerParameterType.Bool:
                    {
                        var on = mode == AnimatorConditionMode.If;
                        EditorGUI.BeginChangeCheck();
                        var t = EditorGUI.Toggle(new Rect(r.x, r.y, 56f, lineH), on);
                        var ch = EditorGUI.EndChangeCheck();
                        if (ch)
                        {
                            mode = t ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot;
                            threshold = t ? 1f : 0f;
                        }

                        GUI.Label(new Rect(r.x + 60f, r.y, 48f, lineH), "(Bool)", EditorStyles.miniLabel);
                        return ch;
                    }
                case AnimatorControllerParameterType.Trigger:
                    GUI.Label(new Rect(r.x, r.y, 40f, lineH), "—");
                    GUI.Label(new Rect(r.x + 44f, r.y, 80f, lineH), "(Trigger)", EditorStyles.miniLabel);
                    return false;
                case AnimatorControllerParameterType.Int:
                    {
                        EditorGUI.BeginChangeCheck();
                        var iv = EditorGUI.IntField(new Rect(r.x, r.y, 72f, lineH), Mathf.RoundToInt(threshold));
                        var ch = EditorGUI.EndChangeCheck();
                        if (ch)
                            threshold = iv;
                        GUI.Label(new Rect(r.x + 76f, r.y, 48f, lineH), "(Int)", EditorStyles.miniLabel);
                        return ch;
                    }
                case AnimatorControllerParameterType.Float:
                    {
                        EditorGUI.BeginChangeCheck();
                        var fv = EditorGUI.FloatField(new Rect(r.x, r.y, 72f, lineH), threshold);
                        var ch = EditorGUI.EndChangeCheck();
                        if (ch)
                            threshold = fv;
                        GUI.Label(new Rect(r.x + 76f, r.y, 56f, lineH), "(Float)", EditorStyles.miniLabel);
                        return ch;
                    }
                default:
                    {
                        EditorGUI.BeginChangeCheck();
                        var fv = EditorGUI.FloatField(new Rect(r.x, r.y, 72f, lineH), threshold);
                        var ch = EditorGUI.EndChangeCheck();
                        if (ch)
                            threshold = fv;
                        GUI.Label(new Rect(r.x + 76f, r.y, 40f, lineH), "(値)", EditorStyles.miniLabel);
                        return ch;
                    }
            }
        }

        /// <summary>
        /// 1 件目: コントローラー先頭パラメーターに「真」と 0 相当のしきい値（型に応じたモード）。
        /// 2 件目以降: 直前の行を複製。
        /// </summary>
        private static ConditionEditRow CreateConditionRowOnAdd(
            List<AnimatorController> menuControllers,
            List<AnimatorController> transitionControllers,
            List<ConditionEditRow> buffer)
        {
            if (buffer.Count > 0)
            {
                var prev = buffer[buffer.Count - 1];
                return new ConditionEditRow
                {
                    mode = prev.mode,
                    parameter = prev.parameter,
                    threshold = prev.threshold
                };
            }

            var ctrls = menuControllers != null && menuControllers.Count > 0 ? menuControllers : transitionControllers;
            if (ctrls == null)
                return new ConditionEditRow
                {
                    mode = AnimatorConditionMode.Equals,
                    parameter = "",
                    threshold = 0f
                };

            foreach (var c in ctrls)
            {
                if (c?.parameters == null || c.parameters.Length == 0)
                    continue;
                return BuildConditionFromAnimatorParameter(c.parameters[0]);
            }

            return new ConditionEditRow
            {
                mode = AnimatorConditionMode.Equals,
                parameter = "",
                threshold = 0f
            };
        }

        /// <summary>
        /// Bool: If・しきい値 0（適用時に Sanitize で真として整合）。Float: Greater・0。Int: Equals・0。Trigger: If・0。
        /// </summary>
        private static ConditionEditRow BuildConditionFromAnimatorParameter(AnimatorControllerParameter p)
        {
            switch (p.type)
            {
                case AnimatorControllerParameterType.Bool:
                    return new ConditionEditRow
                    {
                        mode = AnimatorConditionMode.If,
                        parameter = p.name,
                        threshold = 0f
                    };
                case AnimatorControllerParameterType.Trigger:
                    return new ConditionEditRow
                    {
                        mode = AnimatorConditionMode.If,
                        parameter = p.name,
                        threshold = 0f
                    };
                case AnimatorControllerParameterType.Float:
                    return new ConditionEditRow
                    {
                        mode = AnimatorConditionMode.Greater,
                        parameter = p.name,
                        threshold = 0f
                    };
                case AnimatorControllerParameterType.Int:
                    return new ConditionEditRow
                    {
                        mode = AnimatorConditionMode.Equals,
                        parameter = p.name,
                        threshold = 0f
                    };
                default:
                    return new ConditionEditRow
                    {
                        mode = AnimatorConditionMode.Equals,
                        parameter = p.name,
                        threshold = 0f
                    };
            }
        }

        private List<AnimatorController> GetControllersForParameterMenu()
        {
            var set = new HashSet<AnimatorController>();
            foreach (var row in _outgoing)
            {
                if (row.group?.controller != null)
                    set.Add(row.group.controller);
            }

            foreach (var row in _incoming)
            {
                if (row.group?.controller != null)
                    set.Add(row.group.controller);
            }

            return set.ToList();
        }

        private static List<AnimatorController> CollectAnimatorControllers(List<AnimatorTransitionBase> transitions)
        {
            var set = new HashSet<AnimatorController>();
            foreach (var t in transitions)
            {
                var c = AnimatorTransitionEditOperations.GetController(t);
                if (c != null)
                    set.Add(c);
            }

            return set.ToList();
        }

        private static AnimatorControllerParameterType? ResolveParameterType(
            IReadOnlyList<AnimatorController> controllers,
            string parameterName)
        {
            if (string.IsNullOrEmpty(parameterName) || controllers == null)
                return null;

            foreach (var c in controllers)
            {
                if (c == null)
                    continue;
                foreach (var p in c.parameters)
                {
                    if (p.name == parameterName)
                        return p.type;
                }
            }

            return null;
        }

        /// <summary>
        /// If / IfNot は UI 上「Equals」「Not Equals」。数値の Equals / NotEqual は「==」「!=」で If 系と区別する。
        /// </summary>
        private static string FormatConditionModeDisplay(AnimatorConditionMode m)
        {
            switch (m)
            {
                case AnimatorConditionMode.If:
                    return "True";
                case AnimatorConditionMode.IfNot:
                    return "False";
                case AnimatorConditionMode.Equals:
                    return "Equals";
                case AnimatorConditionMode.NotEqual:
                    return "Not Equals";
                default:
                    return m.ToString();
            }
        }

        private static AnimatorConditionMode[] GetAllowedModes(AnimatorControllerParameterType? pType)
        {
            switch (pType)
            {
                case AnimatorControllerParameterType.Bool:
                case AnimatorControllerParameterType.Trigger:
                    return new[] { AnimatorConditionMode.If, AnimatorConditionMode.IfNot };
                case AnimatorControllerParameterType.Float:
                    return new[]
                    {
                        AnimatorConditionMode.Greater,
                        AnimatorConditionMode.Less
                    };
                case AnimatorControllerParameterType.Int:
                    return new[]
                    {
                        AnimatorConditionMode.Greater,
                        AnimatorConditionMode.Less,
                        AnimatorConditionMode.Equals,
                        AnimatorConditionMode.NotEqual
                    };
                default:
                    return (AnimatorConditionMode[])Enum.GetValues(typeof(AnimatorConditionMode));
            }
        }

        private static float SanitizeThresholdForMode(AnimatorControllerParameterType? pType, AnimatorConditionMode mode,
            float threshold)
        {
            switch (pType)
            {
                case AnimatorControllerParameterType.Int:
                    return Mathf.Round(threshold);
                case AnimatorControllerParameterType.Bool:
                    return mode == AnimatorConditionMode.If ? 1f : 0f;
                default:
                    return threshold;
            }
        }

        private static void ShowParameterHierarchyMenu(Rect anchor, IReadOnlyList<AnimatorController> controllers,
            Action<string> onPick)
        {
            var menu = new GenericMenu();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in controllers)
            {
                if (c == null)
                    continue;
                foreach (var p in c.parameters)
                {
                    if (!string.IsNullOrEmpty(p.name))
                        names.Add(p.name);
                }
            }

            foreach (var name in names.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            {
                var path = name.Replace('\\', '/');
                var captured = name;
                menu.AddItem(new GUIContent(path), false, () => onPick(captured));
            }

            if (names.Count == 0)
                menu.AddDisabledItem(new GUIContent("(パラメーターがありません)"));

            menu.DropDown(anchor);
        }

        private static void ApplyConditionsToTransitions(List<AnimatorTransitionBase> transitions,
            List<ConditionEditRow> rows)
        {
            foreach (var t in transitions)
            {
                if (t == null) continue;
                while (t.conditions.Length > 0)
                    t.RemoveCondition(t.conditions[0]);

                foreach (var r in rows)
                    t.AddCondition(r.mode, r.threshold, r.parameter);
            }
        }

        private static void MarkTransitionsDirty(IEnumerable<AnimatorTransitionBase> transitions)
        {
            var controllers = new HashSet<AnimatorController>();
            foreach (var tr in transitions)
            {
                var c = AnimatorTransitionEditOperations.GetController(tr);
                if (c != null)
                    controllers.Add(c);
            }

            foreach (var c in controllers)
                EditorUtility.SetDirty(c);
        }

        private void DrawClipboardSummary()
        {
            var ctrls = GetControllersForParameterMenu();
            if (ctrls.Count == 0)
            {
                var rowsForCtrl = GetTransitionRowsForSettingsPanel();
                ctrls = CollectAnimatorControllers(rowsForCtrl.Select(r => r.transition).Where(t => t != null).ToList());
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            _foldoutClipboardPanel = EditorGUILayout.Foldout(_foldoutClipboardPanel, "クリップボード", true,
                FoldoutStyleNormal);
            if (!_foldoutClipboardPanel)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            var items = AnimatorTransitionMultiCopy.GetMergedClipboardItems();
            if (items.Count == 0)
            {
                HelpBoxFullWidth("コピーされた設定はありません。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            EnsureClipboardSlotFoldouts(items.Count);

            for (var i = 0; i < items.Count; i++)
            {
                var s = items[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
                _clipboardSlotFold[i] = EditorGUILayout.Foldout(_clipboardSlotFold[i], $"{i + 1}. ", true,
                    FoldoutStyleNormal);
                if (_clipboardSlotFold[i])
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
                    EditorGUILayout.LabelField("ブレンド / 中断", EditorStyles.label);
                    DrawClipboardBlendReadOnly(s);
                    EditorGUILayout.EndVertical();

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
                    EditorGUILayout.LabelField("条件", EditorStyles.label);
                    DrawClipboardConditionsReadOnly(s, ctrls);
                    EditorGUILayout.EndVertical();
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(4f);
            }

            EditorGUILayout.EndVertical();
        }

        private void EnsureClipboardSlotFoldouts(int count)
        {
            while (_clipboardSlotFold.Count < count)
            {
                _clipboardSlotFold.Add(false);
                _clipboardSlotFoldBlend.Add(false);
                _clipboardSlotFoldConditions.Add(false);
            }

            while (_clipboardSlotFold.Count > count)
            {
                var last = _clipboardSlotFold.Count - 1;
                _clipboardSlotFold.RemoveAt(last);
                _clipboardSlotFoldBlend.RemoveAt(last);
                _clipboardSlotFoldConditions.RemoveAt(last);
            }
        }

        private static void DrawClipboardBlendReadOnly(AnimatorTransitionEditOperations.TransitionSettings s)
        {
            if (s == null)
                return;

            if (!s.hasBlendSettings)
            {
                EditorGUILayout.LabelField("（ブレンド設定なし / Entry 等）", EditorStyles.wordWrappedLabel);
                return;
            }

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.FloatField("Duration", s.duration);
            EditorGUILayout.FloatField("Offset", s.offset);
            EditorGUILayout.Toggle("Has Exit Time", s.hasExitTime);
            EditorGUILayout.FloatField("Exit Time", s.exitTime);
            EditorGUILayout.Toggle("Fixed Duration", s.hasFixedDuration);
            EditorGUILayout.EnumPopup("Interruption Source", s.interruptionSource);
            EditorGUILayout.Toggle("Ordered Interruption", s.orderedInterruption);
            if (s.isFromAnyState)
                EditorGUILayout.Toggle("Can Transition To Self", s.canTransitionToSelf);
            EditorGUI.EndDisabledGroup();
        }

        private static void DrawClipboardConditionsReadOnly(AnimatorTransitionEditOperations.TransitionSettings s,
            List<AnimatorController> ctrls)
        {
            if (s == null)
                return;

            if (s.conditions == null || s.conditions.Length == 0)
            {
                EditorGUILayout.LabelField("（条件なし）", EditorStyles.wordWrappedLabel);
                return;
            }

            foreach (var c in s.conditions)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
                DrawClipboardConditionRow(c, ctrls);
                EditorGUILayout.EndVertical();
            }
        }

        private static void DrawClipboardConditionRow(AnimatorTransitionEditOperations.ConditionData c,
            List<AnimatorController> ctrls)
        {
            var pType = ResolveParameterType(ctrls, c.parameter);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(FormatConditionModeDisplay(c.mode), GUILayout.Width(132f));
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField(string.IsNullOrEmpty(c.parameter) ? "(未設定)" : c.parameter,
                EditorStyles.wordWrappedLabel, GUILayout.ExpandWidth(true));
            EditorGUI.BeginDisabledGroup(true);
            DrawClipboardConditionValuePreview(pType, c.mode, c.threshold);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawClipboardConditionValuePreview(AnimatorControllerParameterType? pType,
            AnimatorConditionMode mode, float threshold)
        {
            switch (pType)
            {
                case AnimatorControllerParameterType.Bool:
                    EditorGUILayout.Toggle(mode == AnimatorConditionMode.If, GUILayout.Width(64f));
                    GUILayout.Label("(Bool)", EditorStyles.miniLabel, GUILayout.Width(40f));
                    break;
                case AnimatorControllerParameterType.Trigger:
                    EditorGUILayout.LabelField("—", GUILayout.Width(72f));
                    GUILayout.Label("(Trigger)", EditorStyles.miniLabel, GUILayout.Width(56f));
                    break;
                case AnimatorControllerParameterType.Int:
                    EditorGUILayout.IntField(Mathf.RoundToInt(threshold), GUILayout.Width(88f));
                    GUILayout.Label("(Int)", EditorStyles.miniLabel, GUILayout.Width(36f));
                    break;
                case AnimatorControllerParameterType.Float:
                    EditorGUILayout.FloatField(threshold, GUILayout.Width(88f));
                    GUILayout.Label("(Float)", EditorStyles.miniLabel, GUILayout.Width(44f));
                    break;
                default:
                    EditorGUILayout.FloatField(threshold, GUILayout.Width(88f));
                    GUILayout.Label("(値)", EditorStyles.miniLabel, GUILayout.Width(36f));
                    break;
            }
        }

        private static void SelectForInspector(Object o)
        {
            if (o == null) return;
            Selection.activeObject = o;
            EditorGUIUtility.PingObject(o);
        }

        /// <summary>
        /// 行表示用の遷移元・遷移先ラベルと、クリック選択で使うオブジェクトを解決する。
        /// </summary>
        private static void ResolveEndpoints(
            TransitionGroup g,
            AnimatorTransitionBase t,
            out string srcLabel,
            out Object srcObject,
            out string dstLabel,
            out Object dstObject)
        {
            srcLabel = "?";
            srcObject = null;
            dstLabel = "?";
            dstObject = null;

            if (g == null || t == null) return;

            switch (g.kind)
            {
                case GroupKind.Entry:
                    srcLabel = "Entry";
                    srcObject = g.stateMachine;
                    if (t is AnimatorTransition et)
                    {
                        if (et.destinationState != null)
                        {
                            dstLabel = et.destinationState.name;
                            dstObject = et.destinationState;
                        }
                        else if (et.destinationStateMachine != null)
                        {
                            dstLabel = et.destinationStateMachine.name;
                            dstObject = et.destinationStateMachine;
                        }
                    }

                    break;

                case GroupKind.AnyState:
                    srcLabel = "Any State";
                    srcObject = g.stateMachine;
                    if (t is AnimatorStateTransition ast)
                        ResolveStateTransitionDestination(ast, g.stateMachine, out dstLabel, out dstObject);
                    break;

                case GroupKind.State:
                    if (g.sourceState != null)
                    {
                        srcLabel = g.sourceState.name;
                        srcObject = g.sourceState;
                    }

                    if (t is AnimatorStateTransition st)
                        ResolveStateTransitionDestination(st, g.stateMachine, out dstLabel, out dstObject);
                    break;
            }
        }

        private static void ResolveStateTransitionDestination(
            AnimatorStateTransition st,
            AnimatorStateMachine owningStateMachine,
            out string dstLabel,
            out Object dstObject)
        {
            dstLabel = "?";
            dstObject = null;
            if (st == null) return;

            if (st.isExit)
            {
                dstLabel = "(Exit)";
                dstObject = owningStateMachine;
                return;
            }

            if (st.destinationState != null)
            {
                dstLabel = st.destinationState.name;
                dstObject = st.destinationState;
                return;
            }

            if (st.destinationStateMachine != null)
            {
                dstLabel = st.destinationStateMachine.name;
                dstObject = st.destinationStateMachine;
            }
        }

        private void RefreshSelection()
        {
            _outgoing.Clear();
            _incoming.Clear();
            _reorderOutgoing = null;
            _reorderIncoming = null;
            _selectionBucket = FocusedListBucket.None;
            _selectedRowIndices.Clear();
            _lastConditionBufferSignature = "";
            var seen = new HashSet<int>();

            void TryAddOutgoing(AnimatorTransitionBase transition)
            {
                if (transition == null || !seen.Add(transition.GetInstanceID())) return;
                var group = FindGroup(transition);
                if (group == null) return;
                _outgoing.Add(new TransitionRow { transition = transition, group = group });
            }

            void TryAddIncoming(AnimatorTransitionBase transition)
            {
                if (transition == null || !seen.Add(transition.GetInstanceID())) return;
                var group = FindGroup(transition);
                if (group == null) return;
                _incoming.Add(new TransitionRow { transition = transition, group = group });
            }

            void TryExpandState(AnimatorState state)
            {
                if (state == null) return;
                var path = AssetDatabase.GetAssetPath(state);
                if (string.IsNullOrEmpty(path)) return;
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (controller == null) return;

                foreach (var tr in state.transitions)
                    TryAddOutgoing(tr);

                CollectIncomingToState(state, controller, TryAddIncoming);
            }

            void TryAddExplicit(AnimatorTransitionBase tr)
            {
                var group = FindGroup(tr);
                if (group == null) return;
                if (group.kind == GroupKind.Entry)
                    TryAddIncoming(tr);
                else
                    TryAddOutgoing(tr);
            }

            var objects = new List<Object>();
            foreach (var o in Selection.objects)
                if (o != null) objects.Add(o);
            foreach (var id in Selection.instanceIDs)
            {
                var o = EditorUtility.InstanceIDToObject(id);
                if (o != null) objects.Add(o);
            }

            if (objects.Count == 0 && Selection.activeObject != null)
                objects.Add(Selection.activeObject);

            var processedObjectIds = new HashSet<int>();
            foreach (var obj in objects)
            {
                if (!processedObjectIds.Add(obj.GetInstanceID())) continue;
                switch (obj)
                {
                    case AnimatorState st:
                        TryExpandState(st);
                        break;
                    case AnimatorTransitionBase tr:
                        TryAddExplicit(tr);
                        break;
                }
            }
        }

        private static void CollectIncomingToState(
            AnimatorState target,
            AnimatorController controller,
            System.Action<AnimatorTransitionBase> add)
        {
            if (target == null || controller == null) return;
            foreach (var layer in controller.layers)
                CollectIncomingInStateMachine(layer.stateMachine, target, add);
        }

        private static void CollectIncomingInStateMachine(
            AnimatorStateMachine sm,
            AnimatorState target,
            System.Action<AnimatorTransitionBase> add)
        {
            if (sm == null) return;

            foreach (var et in sm.entryTransitions)
            {
                if (et == null || et.destinationState != target) continue;
                add(et);
            }

            foreach (var t in sm.anyStateTransitions)
            {
                if (t == null || t.destinationState != target) continue;
                add(t);
            }

            foreach (var child in sm.states)
            {
                var s = child.state;
                if (s == null) continue;
                foreach (var tr in s.transitions)
                {
                    if (tr == null || tr.isExit) continue;
                    if (tr.destinationState != target) continue;
                    add(tr);
                }
            }

            foreach (var sub in sm.stateMachines)
                CollectIncomingInStateMachine(sub.stateMachine, target, add);
        }

        private static TransitionGroup FindGroup(AnimatorTransitionBase target)
        {
            if (target == null) return null;
            var path = AssetDatabase.GetAssetPath(target);
            if (string.IsNullOrEmpty(path)) return null;

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) return null;

            foreach (var layer in controller.layers)
            {
                if (TryFindInStateMachine(layer.stateMachine, controller, target, out var group))
                    return group;
            }

            return null;
        }

        private static bool TryFindInStateMachine(
            AnimatorStateMachine stateMachine,
            AnimatorController controller,
            AnimatorTransitionBase target,
            out TransitionGroup group)
        {
            group = null;
            if (stateMachine == null || target == null) return false;

            foreach (var entry in stateMachine.entryTransitions)
            {
                if (entry != target) continue;
                group = new TransitionGroup
                {
                    controller = controller,
                    stateMachine = stateMachine,
                    sourceState = null,
                    kind = GroupKind.Entry
                };
                return true;
            }

            foreach (var any in stateMachine.anyStateTransitions)
            {
                if (any != target) continue;
                group = new TransitionGroup
                {
                    controller = controller,
                    stateMachine = stateMachine,
                    sourceState = null,
                    kind = GroupKind.AnyState
                };
                return true;
            }

            foreach (var child in stateMachine.states)
            {
                foreach (var transition in child.state.transitions)
                {
                    if (transition != target) continue;
                    group = new TransitionGroup
                    {
                        controller = controller,
                        stateMachine = stateMachine,
                        sourceState = child.state,
                        kind = GroupKind.State
                    };
                    return true;
                }
            }

            foreach (var sub in stateMachine.stateMachines)
            {
                if (TryFindInStateMachine(sub.stateMachine, controller, target, out group))
                    return true;
            }

            return false;
        }

        private void RestoreRowSelection(FocusedListBucket bucket, HashSet<int> transitionIds)
        {
            if (transitionIds == null || transitionIds.Count == 0)
                return;

            var targetList = bucket switch
            {
                FocusedListBucket.Outgoing => _outgoing,
                FocusedListBucket.Incoming => _incoming,
                _ => null
            };
            if (targetList == null)
                return;

            _selectionBucket = bucket;
            _selectedRowIndices.Clear();
            for (var i = 0; i < targetList.Count; i++)
            {
                var tr = targetList[i].transition;
                if (tr != null && transitionIds.Contains(tr.GetInstanceID()))
                    _selectedRowIndices.Add(i);
            }
        }

        private void ApplyOrder(List<TransitionRow> bucket)
        {
            if (bucket == null || bucket.Count < 2) return;
            var preservedStates = Selection.objects.OfType<AnimatorState>().Cast<Object>().ToList();
            var preservedActiveState = Selection.activeObject as AnimatorState;

            var groupedEntries = bucket
                .Where(e => e.transition != null && e.group != null)
                .GroupBy(e => e.group.GroupId)
                .Select(g => g.ToList())
                .ToList();

            var touchedControllers = new HashSet<AnimatorController>();
            var selectionAfter = new List<Object>();

            foreach (var groupEntries in groupedEntries)
            {
                var group = groupEntries[0].group;
                if (group?.controller == null) continue;

                if (!ApplyGroupOrder(group, groupEntries.Select(e => e.transition).ToList(), out var recreated))
                    continue;

                touchedControllers.Add(group.controller);
                if (recreated != null && recreated.Length > 0)
                {
                    foreach (var t in recreated)
                        selectionAfter.Add(t);
                }
            }

            foreach (var controller in touchedControllers)
                EditorUtility.SetDirty(controller);

            if (touchedControllers.Count > 0)
                AssetDatabase.SaveAssets();

            if (selectionAfter.Count > 0)
            {
                var merged = new List<Object>();
                merged.AddRange(preservedStates);
                merged.AddRange(selectionAfter.Where(o => o != null));
                var arr = merged
                    .GroupBy(o => o.GetInstanceID())
                    .Select(g => g.First())
                    .ToArray();
                Selection.objects = arr;
                Selection.activeObject = preservedActiveState != null ? preservedActiveState : arr[0];
                EditorApplication.delayCall += () =>
                {
                    Selection.objects = arr;
                    if (preservedActiveState != null)
                        Selection.activeObject = preservedActiveState;
                    else if (arr.Length > 0)
                        Selection.activeObject = arr[0];
                    InternalEditorUtility.RepaintAllViews();
                };
                RefreshSelection();
                Repaint();
            }
        }

        private static bool ApplyGroupOrder(
            TransitionGroup group,
            List<AnimatorTransitionBase> orderedTransitions,
            out AnimatorTransitionBase[] recreated)
        {
            recreated = null;
            if (group == null || group.controller == null || orderedTransitions == null || orderedTransitions.Count < 2)
                return false;

            switch (group.kind)
            {
                case GroupKind.State:
                    return ApplyStateGroup(group, orderedTransitions, out recreated);
                case GroupKind.AnyState:
                    return ApplyAnyStateGroup(group, orderedTransitions, out recreated);
                case GroupKind.Entry:
                    return ApplyEntryGroup(group, orderedTransitions, out recreated);
                default:
                    return false;
            }
        }

        private static bool ApplyStateGroup(
            TransitionGroup group,
            List<AnimatorTransitionBase> orderedTransitions,
            out AnimatorTransitionBase[] recreated)
        {
            recreated = null;
            if (group.sourceState == null) return false;

            var ordered = orderedTransitions.OfType<AnimatorStateTransition>().ToList();
            if (ordered.Count < 2) return false;

            return AnimatorTransitionEditOperations.TryRebuildStateTransitionOrder(
                group.sourceState,
                group.controller,
                ordered,
                "Sort State Transitions",
                out recreated);
        }

        private static bool ApplyAnyStateGroup(
            TransitionGroup group,
            List<AnimatorTransitionBase> orderedTransitions,
            out AnimatorTransitionBase[] recreated)
        {
            recreated = null;
            if (group.stateMachine == null) return false;

            var ordered = orderedTransitions.OfType<AnimatorStateTransition>().ToList();
            if (ordered.Count < 2) return false;

            return AnimatorTransitionEditOperations.TryRebuildAnyStateTransitionOrder(
                group.stateMachine,
                group.controller,
                ordered,
                "Sort Any State Transitions",
                out recreated);
        }

        private static bool ApplyEntryGroup(
            TransitionGroup group,
            List<AnimatorTransitionBase> orderedTransitions,
            out AnimatorTransitionBase[] recreated)
        {
            recreated = null;
            if (group.stateMachine == null) return false;

            var ordered = new List<AnimatorTransition>();
            foreach (var t in orderedTransitions)
            {
                if (t is AnimatorStateTransition)
                    continue;
                if (t is AnimatorTransition at)
                    ordered.Add(at);
            }

            if (ordered.Count < 2) return false;

            return AnimatorTransitionEditOperations.TryRebuildEntryTransitionOrder(
                group.stateMachine,
                group.controller,
                ordered,
                "Sort Entry Transitions",
                out recreated);
        }
    }
}
