using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Samirin33.AvatarEditor.Tools.Editor
{
    /// <summary>
    /// Animator のトランジションに対するキャプチャ・再作成・設定適用。
    /// <see cref="AnimatorTransitionMultiCopy"/> や <see cref="AnimatorTransitionManager"/>、
    /// Animator Binding（ショートカット）経由のコピー／ペーストと処理を共通化する。
    /// </summary>
    public static class AnimatorTransitionEditOperations
    {
        [Serializable]
        public sealed class TransitionSettings
        {
            public bool hasBlendSettings;
            public float duration;
            public float offset;
            public bool hasExitTime;
            public float exitTime;
            public bool hasFixedDuration;
            public TransitionInterruptionSource interruptionSource;
            public bool orderedInterruption;
            public bool canTransitionToSelf;
            /// <summary>コピー元が Any State からの遷移か（UI 表示用。旧クリップボードは false 扱い）。</summary>
            public bool isFromAnyState;
            public ConditionData[] conditions = Array.Empty<ConditionData>();
        }

        [Serializable]
        public struct ConditionData
        {
            public AnimatorConditionMode mode;
            public string parameter;
            public float threshold;
        }

        /// <summary>遷移元・遷移先・種別（MultiCopy の TransitionLocation と同等）。</summary>
        public sealed class TransitionLocation
        {
            public AnimatorController Controller;
            public AnimatorStateMachine StateMachine;
            public AnimatorState SourceState;
            public AnimatorStateMachine SourceStateMachine;
            public bool IsStateMachineNode;
            public bool IsAnyState;
            public bool IsEntry;
            public AnimatorTransitionBase Template;
        }

        /// <summary>トランジション削除後に再作成するための遷移先情報。</summary>
        public sealed class TransitionTopology
        {
            public bool IsEntry;
            public bool IsAnyState;
            public bool IsStateMachineNode;
            public AnimatorState SourceState;
            public AnimatorStateMachine SourceStateMachine;
            public AnimatorStateMachine StateMachine;
            public AnimatorState DestState;
            public AnimatorStateMachine DestStateMachine;
            public bool IsExit;
        }

        public static TransitionSettings Capture(AnimatorTransitionBase t)
        {
            var conds = t.conditions;
            var cd = new ConditionData[conds.Length];
            for (var i = 0; i < conds.Length; i++)
            {
                cd[i] = new ConditionData
                {
                    mode = conds[i].mode,
                    parameter = conds[i].parameter,
                    threshold = conds[i].threshold
                };
            }

            if (t is AnimatorStateTransition st)
            {
                var fromAny = false;
                var controller = GetController(t);
                if (controller != null)
                {
                    var loc = FindTransitionLocation(t, controller);
                    fromAny = loc != null && loc.IsAnyState;
                }

                return new TransitionSettings
                {
                    hasBlendSettings = true,
                    duration = st.duration,
                    offset = st.offset,
                    hasExitTime = st.hasExitTime,
                    exitTime = st.exitTime,
                    hasFixedDuration = st.hasFixedDuration,
                    interruptionSource = st.interruptionSource,
                    orderedInterruption = st.orderedInterruption,
                    canTransitionToSelf = st.canTransitionToSelf,
                    isFromAnyState = fromAny,
                    conditions = cd
                };
            }

            return new TransitionSettings
            {
                hasBlendSettings = false,
                conditions = cd
            };
        }

        public static void ApplyOverwrite(AnimatorTransitionBase t, TransitionSettings s)
        {
            while (t.conditions.Length > 0)
                t.RemoveCondition(t.conditions[0]);

            for (var i = 0; i < s.conditions.Length; i++)
            {
                var c = s.conditions[i];
                t.AddCondition(c.mode, c.threshold, c.parameter);
            }

            if (!s.hasBlendSettings)
                return;

            if (t is AnimatorStateTransition st)
            {
                st.duration = s.duration;
                st.offset = s.offset;
                st.hasExitTime = s.hasExitTime;
                st.exitTime = s.exitTime;
                st.hasFixedDuration = s.hasFixedDuration;
                st.interruptionSource = s.interruptionSource;
                st.orderedInterruption = s.orderedInterruption;
                var c = GetController(t);
                var loc = c != null ? FindTransitionLocation(t, c) : null;
                if (loc != null && loc.IsAnyState)
                    st.canTransitionToSelf = s.canTransitionToSelf;
            }
        }

        /// <summary>この <see cref="AnimatorStateTransition"/> が Any State からの遷移かどうか。</summary>
        public static bool IsAnyStateTransition(AnimatorStateTransition transition)
        {
            if (transition == null) return false;
            var controller = GetController(transition);
            if (controller == null) return false;
            var loc = FindTransitionLocation(transition, controller);
            return loc != null && loc.IsAnyState;
        }

        public static void ApplyAdditiveConditionsOnly(AnimatorTransitionBase t, TransitionSettings s)
        {
            for (var i = 0; i < s.conditions.Length; i++)
            {
                var c = s.conditions[i];
                t.AddCondition(c.mode, c.threshold, c.parameter);
            }
        }

        public static AnimatorController GetController(AnimatorTransitionBase t)
        {
            var path = AssetDatabase.GetAssetPath(t);
            if (string.IsNullOrEmpty(path)) return null;
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        }

        public static void MarkControllerDirty(AnimatorTransitionBase t)
        {
            var c = GetController(t);
            if (c != null)
                EditorUtility.SetDirty(c);
        }

        /// <summary>
        /// トランジションを Animator から削除する（Entry / Any State / ステート間）。
        /// </summary>
        public static bool TryDeleteTransition(AnimatorTransitionBase t, string undoLabel = "Delete Transition")
        {
            if (t == null)
                return false;

            var controller = GetController(t);
            if (controller == null)
                return false;

            var loc = FindTransitionLocation(t, controller);
            if (loc == null)
                return false;

            Undo.RegisterCompleteObjectUndo(controller, undoLabel);

            if (loc.IsEntry)
            {
                if (t is AnimatorTransition et)
                {
                    loc.StateMachine.RemoveEntryTransition(et);
                    EditorUtility.SetDirty(controller);
                    return true;
                }

                return false;
            }

            if (loc.IsAnyState)
            {
                if (t is AnimatorStateTransition ast)
                {
                    loc.StateMachine.RemoveAnyStateTransition(ast);
                    EditorUtility.SetDirty(controller);
                    return true;
                }

                return false;
            }

            if (loc.IsStateMachineNode)
            {
                if (loc.StateMachine == null || loc.SourceStateMachine == null)
                    return false;
                if (t is not AnimatorTransition at)
                    return false;
                var list = new System.Collections.Generic.List<AnimatorTransition>(
                    loc.StateMachine.GetStateMachineTransitions(loc.SourceStateMachine));
                if (!list.Remove(at))
                    return false;
                loc.StateMachine.SetStateMachineTransitions(loc.SourceStateMachine, list.ToArray());
                EditorUtility.SetDirty(controller);
                return true;
            }

            if (loc.SourceState != null && t is AnimatorStateTransition st)
            {
                loc.SourceState.RemoveTransition(st);
                EditorUtility.SetDirty(controller);
                return true;
            }

            return false;
        }

        public static TransitionLocation FindTransitionLocation(AnimatorTransitionBase target, AnimatorController controller)
        {
            if (controller == null || target == null) return null;
            foreach (var layer in controller.layers)
            {
                if (TryFindInStateMachine(layer.stateMachine, target, controller, out var loc))
                    return loc;
            }

            return null;
        }

        private static bool TryFindInStateMachine(AnimatorStateMachine sm, AnimatorTransitionBase target,
            AnimatorController controller, out TransitionLocation loc)
        {
            loc = null;
            foreach (var et in sm.entryTransitions)
            {
                if (et != target) continue;
                loc = new TransitionLocation
                {
                    Controller = controller,
                    StateMachine = sm,
                    IsEntry = true,
                    IsAnyState = false,
                    SourceState = null,
                    Template = et
                };
                return true;
            }

            foreach (var t in sm.anyStateTransitions)
            {
                if (t != target) continue;
                loc = new TransitionLocation
                {
                    Controller = controller,
                    StateMachine = sm,
                    IsAnyState = true,
                    IsEntry = false,
                    SourceState = null,
                    Template = t
                };
                return true;
            }

            foreach (var child in sm.states)
            {
                foreach (var tr in child.state.transitions)
                {
                    if (tr != target) continue;
                    loc = new TransitionLocation
                    {
                        Controller = controller,
                        StateMachine = sm,
                        IsAnyState = false,
                        IsEntry = false,
                        SourceState = child.state,
                        Template = tr
                    };
                    return true;
                }
            }

            // 親が保持する子サブステート（StateMachine ブロック）を起点にした遷移
            foreach (var sub in sm.stateMachines)
            {
                if (sub.stateMachine == null)
                    continue;
                foreach (var smt in sm.GetStateMachineTransitions(sub.stateMachine))
                {
                    if (smt != target) continue;
                    loc = new TransitionLocation
                    {
                        Controller = controller,
                        StateMachine = sm,
                        IsStateMachineNode = true,
                        IsAnyState = false,
                        IsEntry = false,
                        SourceState = null,
                        SourceStateMachine = sub.stateMachine,
                        Template = smt
                    };
                    return true;
                }
            }

            foreach (var sub in sm.stateMachines)
            {
                if (TryFindInStateMachine(sub.stateMachine, target, controller, out loc))
                    return true;
            }

            return false;
        }

        public static TransitionTopology BuildTopology(AnimatorTransitionBase t, TransitionLocation loc)
        {
            if (loc == null || t == null) return null;
            if (loc.IsStateMachineNode)
            {
                var n = new TransitionTopology
                {
                    IsEntry = false,
                    IsAnyState = false,
                    IsStateMachineNode = true,
                    SourceState = null,
                    SourceStateMachine = loc.SourceStateMachine,
                    StateMachine = loc.StateMachine
                };

                if (t is AnimatorStateTransition smAst)
                {
                    n.IsExit = smAst.isExit;
                    if (!smAst.isExit)
                    {
                        n.DestState = smAst.destinationState;
                        n.DestStateMachine = smAst.destinationStateMachine;
                    }
                }
                else if (t is AnimatorTransition at)
                {
                    n.DestState = at.destinationState;
                    n.DestStateMachine = at.destinationStateMachine;
                }

                return n;
            }

            var topo = new TransitionTopology
            {
                IsEntry = loc.IsEntry,
                IsAnyState = loc.IsAnyState,
                IsStateMachineNode = false,
                SourceState = loc.SourceState,
                StateMachine = loc.StateMachine
            };

            if (t is AnimatorStateTransition ast)
            {
                topo.IsExit = ast.isExit;
                topo.DestState = ast.destinationState;
                topo.DestStateMachine = ast.destinationStateMachine;
            }
            else if (t is AnimatorTransition at)
            {
                topo.DestState = at.destinationState;
                topo.DestStateMachine = at.destinationStateMachine;
            }

            return topo;
        }

        /// <summary>既存の <see cref="TransitionLocation.Template"/> と同一トポロジでトランジションを追加する（MultiCopy と同じ）。</summary>
        public static AnimatorTransitionBase TryCreateParallelTransition(TransitionLocation loc)
        {
            if (loc == null) return null;

            var template = loc.Template;

            if (loc.IsEntry)
            {
                if (template.destinationState != null)
                    return loc.StateMachine.AddEntryTransition(template.destinationState);
                if (template.destinationStateMachine != null)
                    return loc.StateMachine.AddEntryTransition(template.destinationStateMachine);
                return null;
            }

            if (loc.IsAnyState)
            {
                if (template is not AnimatorStateTransition ast) return null;
                if (ast.destinationState != null)
                    return loc.StateMachine.AddAnyStateTransition(ast.destinationState);
                if (ast.destinationStateMachine != null)
                    return loc.StateMachine.AddAnyStateTransition(ast.destinationStateMachine);
                return null;
            }

            if (loc.IsStateMachineNode)
            {
                if (loc.StateMachine == null || loc.SourceStateMachine == null)
                    return null;
                if (template is AnimatorStateTransition smAst)
                {
                    if (smAst.isExit)
                        return loc.StateMachine.AddStateMachineExitTransition(loc.SourceStateMachine);
                    if (smAst.destinationState != null)
                        return loc.StateMachine.AddStateMachineTransition(loc.SourceStateMachine, smAst.destinationState);
                    if (smAst.destinationStateMachine != null)
                        return loc.StateMachine.AddStateMachineTransition(
                            loc.SourceStateMachine, smAst.destinationStateMachine);
                }
                else if (template is AnimatorTransition at2)
                {
                    if (at2.destinationState != null)
                        return loc.StateMachine.AddStateMachineTransition(loc.SourceStateMachine, at2.destinationState);
                    if (at2.destinationStateMachine != null)
                        return loc.StateMachine.AddStateMachineTransition(
                            loc.SourceStateMachine, at2.destinationStateMachine);
                }

                return null;
            }

            if (loc.SourceState == null || template is not AnimatorStateTransition st) return null;

            if (st.isExit)
                return loc.SourceState.AddExitTransition();

            if (st.destinationStateMachine != null)
                return loc.SourceState.AddTransition(st.destinationStateMachine);

            if (st.destinationState != null)
                return loc.SourceState.AddTransition(st.destinationState);

            return null;
        }

        /// <summary>キャプチャしたトポロジでトランジションを追加する（削除後の再作成用）。</summary>
        public static AnimatorTransitionBase CreateTransitionFromTopology(TransitionTopology topo)
        {
            if (topo == null || topo.StateMachine == null) return null;

            if (topo.IsEntry)
            {
                if (topo.DestState != null)
                    return topo.StateMachine.AddEntryTransition(topo.DestState);
                if (topo.DestStateMachine != null)
                    return topo.StateMachine.AddEntryTransition(topo.DestStateMachine);
                return null;
            }

            if (topo.IsAnyState)
            {
                if (topo.DestState != null)
                    return topo.StateMachine.AddAnyStateTransition(topo.DestState);
                if (topo.DestStateMachine != null)
                    return topo.StateMachine.AddAnyStateTransition(topo.DestStateMachine);
                return null;
            }

            if (topo.IsStateMachineNode && topo.SourceStateMachine != null)
            {
                if (topo.IsExit)
                    return topo.StateMachine.AddStateMachineExitTransition(topo.SourceStateMachine);
                if (topo.DestState != null)
                    return topo.StateMachine.AddStateMachineTransition(topo.SourceStateMachine, topo.DestState);
                if (topo.DestStateMachine != null)
                    return topo.StateMachine.AddStateMachineTransition(
                        topo.SourceStateMachine, topo.DestStateMachine);
                return null;
            }

            if (topo.SourceState == null) return null;

            if (topo.IsExit)
                return topo.SourceState.AddExitTransition();

            if (topo.DestStateMachine != null)
                return topo.SourceState.AddTransition(topo.DestStateMachine);

            if (topo.DestState != null)
                return topo.SourceState.AddTransition(topo.DestState);

            return null;
        }

        /// <summary>
        /// 同一ステート上の選択トランジションを、指定順で削除・再作成し設定を復元する（配列順を確実に反映する）。
        /// </summary>
        public static bool TryRebuildStateTransitionOrder(
            AnimatorState state,
            AnimatorController controller,
            IReadOnlyList<AnimatorStateTransition> userOrderedSelection,
            string undoLabel,
            out AnimatorTransitionBase[] recreatedInUserOrder)
        {
            recreatedInUserOrder = null;
            if (state == null || controller == null || userOrderedSelection == null || userOrderedSelection.Count < 2)
                return false;

            var original = state.transitions.ToArray().ToList();
            var selectedIds = new HashSet<int>(userOrderedSelection.Select(t => t.GetInstanceID()));
            var k = userOrderedSelection.Count;

            var snapshots = new (TransitionSettings settings, TransitionTopology topo)[k];
            for (var i = 0; i < k; i++)
            {
                var t = userOrderedSelection[i];
                var loc = FindTransitionLocation(t, controller);
                if (loc == null || loc.SourceState != state || loc.IsEntry || loc.IsAnyState)
                    return false;
                snapshots[i] = (Capture(t), BuildTopology(t, loc));
            }

            // Add* の呼び出し順を末尾→先頭にし、一時的に末尾へ積まれる順序を制御する（created[i] は UI 順のまま）
            var created = new AnimatorStateTransition[k];
            for (var i = k - 1; i >= 0; i--)
            {
                var neu = CreateTransitionFromTopology(snapshots[i].topo) as AnimatorStateTransition;
                if (neu == null) return false;
                ApplyOverwrite(neu, snapshots[i].settings);
                created[i] = neu;
            }

            var built = new List<AnimatorStateTransition>(original.Count);
            var ci = 0;
            foreach (var tr in original)
            {
                if (tr != null && selectedIds.Contains(tr.GetInstanceID()))
                    built.Add(created[ci++]);
                else
                    built.Add(tr);
            }

            if (ci != k) return false;

            Undo.RegisterCompleteObjectUndo(controller, undoLabel);
            state.transitions = built.ToArray();
            recreatedInUserOrder = Array.ConvertAll(created, x => (AnimatorTransitionBase)x);
            return true;
        }

        /// <summary>
        /// 同一ステートマシンの Any State 上の選択トランジションを再構築する。
        /// </summary>
        public static bool TryRebuildAnyStateTransitionOrder(
            AnimatorStateMachine stateMachine,
            AnimatorController controller,
            IReadOnlyList<AnimatorStateTransition> userOrderedSelection,
            string undoLabel,
            out AnimatorTransitionBase[] recreatedInUserOrder)
        {
            recreatedInUserOrder = null;
            if (stateMachine == null || controller == null || userOrderedSelection == null || userOrderedSelection.Count < 2)
                return false;

            var original = stateMachine.anyStateTransitions.ToArray().ToList();
            var selectedIds = new HashSet<int>(userOrderedSelection.Select(t => t.GetInstanceID()));
            var k = userOrderedSelection.Count;

            var snapshots = new (TransitionSettings settings, TransitionTopology topo)[k];
            for (var i = 0; i < k; i++)
            {
                var t = userOrderedSelection[i];
                var loc = FindTransitionLocation(t, controller);
                if (loc == null || loc.StateMachine != stateMachine || !loc.IsAnyState)
                    return false;
                snapshots[i] = (Capture(t), BuildTopology(t, loc));
            }

            var created = new AnimatorStateTransition[k];
            for (var i = k - 1; i >= 0; i--)
            {
                var neu = CreateTransitionFromTopology(snapshots[i].topo) as AnimatorStateTransition;
                if (neu == null) return false;
                ApplyOverwrite(neu, snapshots[i].settings);
                created[i] = neu;
            }

            var built = new List<AnimatorStateTransition>(original.Count);
            var ci = 0;
            foreach (var tr in original)
            {
                if (tr != null && selectedIds.Contains(tr.GetInstanceID()))
                    built.Add(created[ci++]);
                else
                    built.Add(tr);
            }

            if (ci != k) return false;

            Undo.RegisterCompleteObjectUndo(controller, undoLabel);
            stateMachine.anyStateTransitions = built.ToArray();
            recreatedInUserOrder = Array.ConvertAll(created, x => (AnimatorTransitionBase)x);
            return true;
        }

        /// <summary>
        /// 同一ステートマシンの Entry 上の選択トランジションを再構築する。
        /// </summary>
        public static bool TryRebuildEntryTransitionOrder(
            AnimatorStateMachine stateMachine,
            AnimatorController controller,
            IReadOnlyList<AnimatorTransition> userOrderedSelection,
            string undoLabel,
            out AnimatorTransitionBase[] recreatedInUserOrder)
        {
            recreatedInUserOrder = null;
            if (stateMachine == null || controller == null || userOrderedSelection == null || userOrderedSelection.Count < 2)
                return false;

            var original = stateMachine.entryTransitions.ToArray().ToList();
            var selectedIds = new HashSet<int>(userOrderedSelection.Select(t => t.GetInstanceID()));
            var k = userOrderedSelection.Count;

            var snapshots = new (TransitionSettings settings, TransitionTopology topo)[k];
            for (var i = 0; i < k; i++)
            {
                var t = userOrderedSelection[i];
                var loc = FindTransitionLocation(t, controller);
                if (loc == null || loc.StateMachine != stateMachine || !loc.IsEntry)
                    return false;
                snapshots[i] = (Capture(t), BuildTopology(t, loc));
            }

            var created = new AnimatorTransition[k];
            for (var i = k - 1; i >= 0; i--)
            {
                var neu = CreateTransitionFromTopology(snapshots[i].topo);
                if (neu is not AnimatorTransition at)
                    return false;
                ApplyOverwrite(at, snapshots[i].settings);
                created[i] = at;
            }

            var built = new List<AnimatorTransition>(original.Count);
            var ci = 0;
            foreach (var tr in original)
            {
                if (tr != null && selectedIds.Contains(tr.GetInstanceID()))
                    built.Add(created[ci++]);
                else
                    built.Add(tr);
            }

            if (ci != k) return false;

            Undo.RegisterCompleteObjectUndo(controller, undoLabel);
            stateMachine.entryTransitions = built.ToArray();
            recreatedInUserOrder = Array.ConvertAll(created, x => (AnimatorTransitionBase)x);
            return true;
        }

        /// <summary>
        /// 同一の子サブステート（StateMachine ブロック）を起点にした遷移配列を再構築する。
        /// </summary>
        public static bool TryRebuildStateMachineNodeTransitionOrder(
            AnimatorStateMachine parentStateMachine,
            AnimatorStateMachine sourceStateMachineNode,
            AnimatorController controller,
            IReadOnlyList<AnimatorTransition> userOrderedSelection,
            string undoLabel,
            out AnimatorTransitionBase[] recreatedInUserOrder)
        {
            recreatedInUserOrder = null;
            if (parentStateMachine == null || sourceStateMachineNode == null || controller == null ||
                userOrderedSelection == null || userOrderedSelection.Count < 2)
                return false;

            var original = new List<AnimatorTransition>(
                parentStateMachine.GetStateMachineTransitions(sourceStateMachineNode));
            var selectedIds = new HashSet<int>(userOrderedSelection.Select(t => t.GetInstanceID()));
            var k = userOrderedSelection.Count;

            var snapshots = new (TransitionSettings settings, TransitionTopology topo)[k];
            for (var i = 0; i < k; i++)
            {
                var t = userOrderedSelection[i];
                var loc = FindTransitionLocation(t, controller);
                if (loc == null || !loc.IsStateMachineNode || loc.StateMachine != parentStateMachine ||
                    loc.SourceStateMachine != sourceStateMachineNode)
                    return false;
                snapshots[i] = (Capture(t), BuildTopology(t, loc));
            }

            var created = new AnimatorTransition[k];
            for (var i = k - 1; i >= 0; i--)
            {
                var neu = CreateTransitionFromTopology(snapshots[i].topo);
                if (neu is not AnimatorTransition at)
                    return false;
                ApplyOverwrite(at, snapshots[i].settings);
                created[i] = at;
            }

            var built = new List<AnimatorTransition>(original.Count);
            var ci = 0;
            foreach (var tr in original)
            {
                if (tr != null && selectedIds.Contains(tr.GetInstanceID()))
                    built.Add(created[ci++]);
                else
                    built.Add(tr);
            }

            if (ci != k) return false;

            Undo.RegisterCompleteObjectUndo(controller, undoLabel);
            parentStateMachine.SetStateMachineTransitions(sourceStateMachineNode, built.ToArray());
            recreatedInUserOrder = Array.ConvertAll(created, x => (AnimatorTransitionBase)x);
            return true;
        }
    }
}
