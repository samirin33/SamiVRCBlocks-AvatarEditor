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
    public sealed partial class AnimatorTransitionManager : EditorWindow
    {
        // トランジション設定・条件
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
        /// 番号で選択された行。未選択時は、Unity 上でトランジションが選ばれていれば一覧の先頭（外向きを優先）を 1 件だけ返す。
        /// Animator ステートのみが選ばれている間は空（一覧で行を選ぶまでトランジション設定を出さない）。
        /// </summary>
        private List<TransitionRow> GetTransitionRowsForSettingsPanel()
        {
            var selected = GetSelectedRows();
            if (selected.Count > 0)
                return selected;

            if ((HasAnimatorStateInSelection() || HasAnimatorStateMachineInSelection()) &&
                !HasAnimatorTransitionInSelection())
                return new List<TransitionRow>();

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

            const float modeW = 64f;
            const float delW = 22f;
            const float gap = 4f;
            var y = inner.y + (inner.height - lineH) * 0.5f;
            const float valueClusterW = 70f;

            var rDel = new Rect(inner.xMax - delW, y, delW, lineH);
            var rVal = new Rect(rDel.x - gap - valueClusterW, y, valueClusterW, lineH);
            var rMode = new Rect(rVal.x - gap - modeW, y, modeW, lineH);
            var paramLeft = inner.x;
            var paramW = Mathf.Max(32f, rMode.x - gap - paramLeft);
            var rParam = new Rect(paramLeft, y, paramW, lineH);

            EditorGUI.BeginChangeCheck();
            var paramOptions = CollectConditionParameterNames(menuControllers);
            newParam = DrawTextOrSelectFieldInlineRect(rParam, newParam, paramOptions);
            var paramChanged = EditorGUI.EndChangeCheck();
            if (paramChanged)
            {
                pType = ResolveParameterType(menuControllers, newParam);
                allowedModes = GetAllowedModes(pType);
                if (!allowedModes.Contains(newMode))
                    newMode = allowedModes[0];
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

        private static List<string> CollectConditionParameterNames(IReadOnlyList<AnimatorController> controllers)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (controllers != null)
            {
                foreach (var c in controllers)
                {
                    if (c?.parameters == null)
                        continue;
                    foreach (var p in c.parameters)
                    {
                        if (!string.IsNullOrWhiteSpace(p?.name))
                            names.Add(p.name);
                    }
                }
            }

            return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
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
                        var t = EditorGUI.Toggle(new Rect(r.x, r.y, 28f, lineH), on);
                        var ch = EditorGUI.EndChangeCheck();
                        if (ch)
                        {
                            mode = t ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot;
                            threshold = t ? 1f : 0f;
                        }
                        return ch;
                    }
                case AnimatorControllerParameterType.Trigger:
                    GUI.Label(new Rect(r.x, r.y, 18f, lineH), "—");
                    return false;
                case AnimatorControllerParameterType.Int:
                    {
                        EditorGUI.BeginChangeCheck();
                        var iv = EditorGUI.IntField(new Rect(r.x, r.y, 36f, lineH), Mathf.RoundToInt(threshold));
                        var ch = EditorGUI.EndChangeCheck();
                        if (ch)
                            threshold = iv;
                        return ch;
                    }
                case AnimatorControllerParameterType.Float:
                    {
                        EditorGUI.BeginChangeCheck();
                        var fv = EditorGUI.FloatField(new Rect(r.x, r.y, 36f, lineH), threshold);
                        var ch = EditorGUI.EndChangeCheck();
                        if (ch)
                            threshold = fv;
                        return ch;
                    }
                default:
                    {
                        EditorGUI.BeginChangeCheck();
                        var fv = EditorGUI.FloatField(new Rect(r.x, r.y, 36f, lineH), threshold);
                        var ch = EditorGUI.EndChangeCheck();
                        if (ch)
                            threshold = fv;
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
    }
}
