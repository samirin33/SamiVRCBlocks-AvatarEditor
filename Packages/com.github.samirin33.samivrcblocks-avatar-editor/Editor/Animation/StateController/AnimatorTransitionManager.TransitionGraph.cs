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
        // RefreshSelection / グラフ解決 / FindGroup
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

                case GroupKind.StateMachineNode:
                    if (g.sourceStateMachine != null)
                    {
                        srcLabel = g.sourceStateMachine.name;
                        srcObject = g.sourceStateMachine;
                    }

                    if (t is AnimatorStateTransition smAst)
                    {
                        if (smAst.isExit)
                        {
                            dstLabel = "(Exit)";
                            dstObject = g.stateMachine;
                        }
                        else
                        {
                            ResolveStateTransitionDestination(smAst, g.stateMachine, out dstLabel, out dstObject);
                        }
                    }
                    else if (t is AnimatorTransition at2)
                    {
                        if (at2.destinationState != null)
                        {
                            dstLabel = at2.destinationState.name;
                            dstObject = at2.destinationState;
                        }
                        else if (at2.destinationStateMachine != null)
                        {
                            dstLabel = at2.destinationStateMachine.name;
                            dstObject = at2.destinationStateMachine;
                        }
                    }

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

            void TryExpandStateMachine(AnimatorStateMachine subRoot)
            {
                if (subRoot == null) return;
                var path = AssetDatabase.GetAssetPath(subRoot);
                if (string.IsNullOrEmpty(path)) return;
                var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (c == null) return;
                var states = new HashSet<AnimatorState>();
                var sms = new HashSet<AnimatorStateMachine>();
                BuildSubtreeStateAndStateMachines(subRoot, states, sms);
                CollectOutgoingFromStateMachineSubTree(subRoot, TryAddOutgoing);
                foreach (var layer in c.layers)
                    CollectIncomingToStateMachineSubtree(layer.stateMachine, states, sms, TryAddIncoming);
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
                    case AnimatorStateMachine asm:
                    {
                        // レイヤー直下のルートStateMachine（Animatorで「何もない」扱いの時も選ばれる）だと
                        // サブツリー全体＝全トランジション列挙になってしまうため、ネストしたサブステートのときだけ展開する。
                        var p = AssetDatabase.GetAssetPath(asm);
                        if (string.IsNullOrEmpty(p)) break;
                        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(p);
                        if (ctrl == null) break;
                        if (!IsNestedStateMachine(asm, ctrl)) break;
                        TryExpandStateMachine(asm);
                        break;
                    }
                    case AnimatorTransitionBase tr:
                        TryAddExplicit(tr);
                        break;
                }
            }

            ApplyDefaultRowSelectionWhenUnityHasMultipleTransitionsSelected();
        }

        /// <summary>
        /// Unity で <see cref="AnimatorTransitionBase"/> を複数選択しているとき、
        /// 外向き／内向き一覧のうち該当する行を選択する（未操作時の既定）。
        /// 同じ遷移元・遷移先ペアを持つ行が複数ある場合は、先頭の1行だけを選択する。
        /// </summary>
        private void ApplyDefaultRowSelectionWhenUnityHasMultipleTransitionsSelected()
        {
            var selectedTransitionIds = new HashSet<int>();
            foreach (var o in Selection.objects)
            {
                if (o is AnimatorTransitionBase tr)
                    selectedTransitionIds.Add(tr.GetInstanceID());
            }

            foreach (var id in Selection.instanceIDs)
            {
                if (EditorUtility.InstanceIDToObject(id) is AnimatorTransitionBase tr2)
                    selectedTransitionIds.Add(tr2.GetInstanceID());
            }

            if (selectedTransitionIds.Count < 2)
                return;

            var outgoingHits = FilterByUniqueEndpointPair(_outgoing, selectedTransitionIds);
            var incomingHits = FilterByUniqueEndpointPair(_incoming, selectedTransitionIds);

            if (outgoingHits.Count == 0 && incomingHits.Count == 0)
                return;

            _selectedRowIndices.Clear();
            _lastConditionBufferSignature = "";

            if (incomingHits.Count == 0 || outgoingHits.Count >= incomingHits.Count)
            {
                _selectionBucket = FocusedListBucket.Outgoing;
                foreach (var i in outgoingHits)
                    _selectedRowIndices.Add(i);
            }
            else
            {
                _selectionBucket = FocusedListBucket.Incoming;
                foreach (var i in incomingHits)
                    _selectedRowIndices.Add(i);
            }
        }

        /// <summary>
        /// rows のうち selectedIds に含まれる行を抽出し、遷移元・遷移先オブジェクトの組み合わせが
        /// 同じ行が複数あれば最初の1行だけを残したインデックスリストを返す。
        /// </summary>
        private static List<int> FilterByUniqueEndpointPair(List<TransitionRow> rows, HashSet<int> selectedIds)
        {
            var result = new List<int>();
            // キーは (srcInstanceID, dstInstanceID)
            var seenPairs = new HashSet<(int, int)>();

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.transition == null) continue;
                if (!selectedIds.Contains(row.transition.GetInstanceID())) continue;

                ResolveEndpoints(row.group, row.transition,
                    out _, out var srcObj,
                    out _, out var dstObj);

                var srcId = srcObj != null ? srcObj.GetInstanceID() : 0;
                var dstId = dstObj != null ? dstObj.GetInstanceID() : 0;
                var pair = (srcId, dstId);

                if (!seenPairs.Add(pair))
                    continue; // 同じペアがすでにあるためスキップ

                result.Add(i);
            }

            return result;
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

        private static void BuildSubtreeStateAndStateMachines(
            AnimatorStateMachine root,
            HashSet<AnimatorState> states,
            HashSet<AnimatorStateMachine> stateMachines)
        {
            if (root == null)
                return;
            stateMachines.Add(root);
            foreach (var c in root.states)
            {
                if (c.state != null)
                    states.Add(c.state);
            }

            foreach (var sub in root.stateMachines)
            {
                if (sub.stateMachine != null)
                    BuildSubtreeStateAndStateMachines(sub.stateMachine, states, stateMachines);
            }
        }

        private static bool IsTransitionDestinationInSubtree(
            AnimatorTransitionBase t,
            HashSet<AnimatorState> targetStates,
            HashSet<AnimatorStateMachine> targetStateMachines)
        {
            if (t is AnimatorStateTransition st)
            {
                if (st.isExit) return false;
                if (st.destinationState != null) return targetStates.Contains(st.destinationState);
                if (st.destinationStateMachine != null) return targetStateMachines.Contains(st.destinationStateMachine);
                return false;
            }

            if (t is AnimatorTransition at)
            {
                if (at.destinationState != null) return targetStates.Contains(at.destinationState);
                if (at.destinationStateMachine != null) return targetStateMachines.Contains(at.destinationStateMachine);
                return false;
            }

            return false;
        }

        private static void CollectIncomingToStateMachineSubtree(
            AnimatorStateMachine sm,
            HashSet<AnimatorState> targetStates,
            HashSet<AnimatorStateMachine> targetStateMachines,
            System.Action<AnimatorTransitionBase> add)
        {
            if (sm == null) return;
            foreach (var et in sm.entryTransitions)
            {
                if (et != null && IsTransitionDestinationInSubtree(et, targetStates, targetStateMachines))
                    add(et);
            }

            foreach (var t in sm.anyStateTransitions)
            {
                if (t != null && IsTransitionDestinationInSubtree(t, targetStates, targetStateMachines))
                    add(t);
            }

            foreach (var c in sm.states)
            {
                if (c.state == null) continue;
                foreach (var tr in c.state.transitions)
                {
                    if (tr == null) continue;
                    if (IsTransitionDestinationInSubtree(tr, targetStates, targetStateMachines))
                        add(tr);
                }
            }

            foreach (var sub in sm.stateMachines)
            {
                if (sub.stateMachine == null) continue;
                foreach (var t in sm.GetStateMachineTransitions(sub.stateMachine))
                {
                    if (t != null && IsTransitionDestinationInSubtree(t, targetStates, targetStateMachines))
                        add(t);
                }

                CollectIncomingToStateMachineSubtree(sub.stateMachine, targetStates, targetStateMachines, add);
            }
        }

        private static void CollectOutgoingFromStateMachineSubTree(AnimatorStateMachine sm,
            System.Action<AnimatorTransitionBase> add)
        {
            if (sm == null) return;
            foreach (var t in sm.anyStateTransitions)
            {
                if (t != null) add(t);
            }

            foreach (var c in sm.states)
            {
                if (c.state == null) continue;
                foreach (var tr in c.state.transitions)
                {
                    if (tr != null) add(tr);
                }
            }

            foreach (var sub in sm.stateMachines)
            {
                if (sub.stateMachine == null) continue;
                foreach (var t in sm.GetStateMachineTransitions(sub.stateMachine))
                {
                    if (t != null) add(t);
                }

                CollectOutgoingFromStateMachineSubTree(sub.stateMachine, add);
            }
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
                if (sub.stateMachine == null)
                    continue;
                foreach (var smt in stateMachine.GetStateMachineTransitions(sub.stateMachine))
                {
                    if (smt != target) continue;
                    group = new TransitionGroup
                    {
                        controller = controller,
                        stateMachine = stateMachine,
                        sourceState = null,
                        sourceStateMachine = sub.stateMachine,
                        kind = GroupKind.StateMachineNode
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
    }
}
