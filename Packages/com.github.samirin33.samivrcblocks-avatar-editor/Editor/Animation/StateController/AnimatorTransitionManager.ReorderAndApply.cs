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
        // 並べ替え・Undo
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
                case GroupKind.StateMachineNode:
                    return ApplyStateMachineNodeGroup(group, orderedTransitions, out recreated);
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

        private static bool ApplyStateMachineNodeGroup(
            TransitionGroup group,
            List<AnimatorTransitionBase> orderedTransitions,
            out AnimatorTransitionBase[] recreated)
        {
            recreated = null;
            if (group.stateMachine == null || group.sourceStateMachine == null) return false;

            var ordered = new List<AnimatorTransition>();
            foreach (var t in orderedTransitions)
            {
                if (t is AnimatorTransition at)
                    ordered.Add(at);
            }

            if (ordered.Count < 2) return false;

            return AnimatorTransitionEditOperations.TryRebuildStateMachineNodeTransitionOrder(
                group.stateMachine,
                group.sourceStateMachine,
                group.controller,
                ordered,
                "Sort StateMachine Node Transitions",
                out recreated);
        }
    }
}
