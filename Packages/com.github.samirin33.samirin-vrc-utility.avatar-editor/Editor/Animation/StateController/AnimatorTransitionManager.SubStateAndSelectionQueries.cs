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
        // サブステデフォ・選択判定
        private static string DrawFloatParameterPopup(string label, string current, List<string> parameterNames)
        {
            return DrawTextOrSelectField(label, current, parameterNames);
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

            if (HasAnimatorStateMachineInSelection())
                return false;

            return HasAnimatorTransitionInSelection();
        }

        private static bool HasAnimatorStateMachineInSelection()
        {
            foreach (var o in Selection.objects)
            {
                if (o is AnimatorStateMachine)
                    return true;
            }

            foreach (var id in Selection.instanceIDs)
            {
                var o = EditorUtility.InstanceIDToObject(id);
                if (o is AnimatorStateMachine)
                    return true;
            }

            return Selection.activeObject is AnimatorStateMachine;
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
    }
}
