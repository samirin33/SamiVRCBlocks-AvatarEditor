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
        // StateMachineBehaviour 一覧
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
    }
}
