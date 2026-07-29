using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.ShortcutManagement;
using UnityEditorInternal;
using UnityEngine;
using Samirin33.AvatarEditor.Animation.Editor;
using AnimatorController = UnityEditor.Animations.AnimatorController;

namespace Samirin33.AvatarEditor.Tools.Editor
{
    /// <summary>
    /// Animator 関連のショートカット（Unity のショートカット管理に登録）。
    /// キー割り当ては「Edit &gt; Shortcuts...」で変更できます（検索: Samirin）。
    /// </summary>
    public static class AnimatorBinding
    {
        private const string MenuPathConvergeToLast = "SamiVRCBlocks-AvatarEditor/Animator Binding/新しいトランジションを作成、最後に収束";
        private const string MenuPathDivergeFromFirst = "SamiVRCBlocks-AvatarEditor/Animator Binding/新しいトランジションを作成、最初から拡散";
        private const string MenuPathNewStateAtCenter = "SamiVRCBlocks-AvatarEditor/Animator Binding/新しいステートを作成";

        /// <summary>ショートカット ID（<see cref="ShortcutManager"/> / 設定画面と共通）。</summary>
        public static class ShortcutIds
        {
            public const string MergedCopy = "Samirin Animator Tools/Merged Copy";
            public const string MergedPasteOverwrite = "Samirin Animator Tools/Merged Paste Overwrite";
            public const string MergedPasteAdditive = "Samirin Animator Tools/Merged Paste Additive";
            /// <summary>複数: 最後に選んだステートへ収束 / 1件: Make Transition モード</summary>
            public const string NewTransitionConvergeToLast = "Samirin Animator Tools/New Transition Converge To Last";
            /// <summary>複数: 先頭から拡散（AnimatorState または Any State）/ 1件: Make Transition モード</summary>
            public const string NewTransitionDivergeFromFirst = "Samirin Animator Tools/New Transition Diverge From First";
            /// <summary>Animator グラフの表示中心に新規ステートを作成</summary>
            public const string NewStateAtCursor = "Samirin Animator Tools/New State At Screen Center";
        }

        [Shortcut(ShortcutIds.MergedCopy, KeyCode.C, ShortcutModifiers.Alt)]
        public static void ShortcutMergedCopy()
        {
            AnimatorTransitionMultiCopy.PerformMergedCopyFromSelection();
        }

        [Shortcut(ShortcutIds.MergedPasteOverwrite, KeyCode.V, ShortcutModifiers.Alt)]
        public static void ShortcutMergedPasteOverwrite()
        {
            AnimatorTransitionMultiCopy.PerformMergedPasteOverwriteFromSelection();
        }

        [Shortcut(ShortcutIds.MergedPasteAdditive, KeyCode.A, ShortcutModifiers.Alt)]
        public static void ShortcutMergedPasteAdditive()
        {
            AnimatorTransitionMultiCopy.PerformMergedPasteAdditiveFromSelection();
        }

        /// <summary>
        /// 現在の選択に含まれる他のすべての <see cref="AnimatorState"/> から
        /// <paramref name="destinationState"/> へ通常トランジションを追加する（収束）。
        /// 遷移先は選択集合に含まれていてもよい（その場合は遷移元から除外される）。
        /// </summary>
        public static bool TryAddConvergenceTransitionsFromSelectionToState(AnimatorState destinationState)
        {
            if (destinationState == null)
                return false;

            var destPath = AssetDatabase.GetAssetPath(destinationState);
            if (string.IsNullOrEmpty(destPath))
                return false;

            var states = CollectAnimatorStatesInSelectionOrder();
            var sources = new List<AnimatorState>();
            foreach (var s in states)
            {
                if (s == null || ReferenceEquals(s, destinationState))
                    continue;
                if (AssetDatabase.GetAssetPath(s) != destPath)
                    continue;
                sources.Add(s);
            }

            if (sources.Count == 0)
            {
                Debug.LogWarning(
                    "[AnimatorBinding] 収束元になる AnimatorState が選択にありません（遷移先と別のステートを1つ以上選択してください）。");
                return false;
            }

            if (!ValidateSameController(sources, out var controller))
                return false;

            if (AssetDatabase.GetAssetPath(sources[0]) != destPath)
            {
                Debug.LogWarning("[AnimatorBinding] 遷移先と遷移元は同一の Animator Controller 上である必要があります。");
                return false;
            }

            Undo.RegisterCompleteObjectUndo(controller, "Create Transitions (Converge To Picked State)");
            var added = 0;
            foreach (var from in sources)
            {
                if (ReferenceEquals(from, destinationState))
                    continue;
                try
                {
                    var tr = from.AddTransition(destinationState);
                    AnimatorDefaultSetting.ApplyDefaultsToTransitionFromScript(tr);
                    added++;
                }
                catch (System.Exception ex)
                {
                    Debug.LogException(ex);
                }
            }

            if (added > 0)
            {
                EditorUtility.SetDirty(controller);
                RepaintAnimatorControllerGraph(controller);
            }
            else
                Debug.Log("[AnimatorBinding] 収束用に追加したトランジションはありませんでした。");

            return true;
        }

        /// <summary>
        /// 選択中の各 <see cref="AnimatorState"/> から <paramref name="destinationStateMachine"/> へ遷移を追加する（収束）。
        /// </summary>
        public static bool TryAddConvergenceTransitionsFromSelectionToStateMachine(AnimatorStateMachine destinationStateMachine)
        {
            if (destinationStateMachine == null)
                return false;

            var destPath = AssetDatabase.GetAssetPath(destinationStateMachine);
            if (string.IsNullOrEmpty(destPath))
                return false;

            var states = CollectAnimatorStatesInSelectionOrder();
            var sources = new List<AnimatorState>();
            foreach (var s in states)
            {
                if (s == null)
                    continue;
                if (AssetDatabase.GetAssetPath(s) != destPath)
                    continue;
                sources.Add(s);
            }

            if (sources.Count == 0)
            {
                Debug.LogWarning(
                    "[AnimatorBinding] 収束元になる AnimatorState が選択にありません（遷移元のステートを1つ以上選択してください）。");
                return false;
            }

            if (!ValidateSameController(sources, out var controller))
                return false;

            if (AssetDatabase.GetAssetPath(sources[0]) != destPath)
            {
                Debug.LogWarning("[AnimatorBinding] 遷移先と遷移元は同一の Animator Controller 上である必要があります。");
                return false;
            }

            Undo.RegisterCompleteObjectUndo(controller, "Create Transitions (Converge To SubStateMachine)");
            var added = 0;
            foreach (var from in sources)
            {
                try
                {
                    var tr = from.AddTransition(destinationStateMachine);
                    AnimatorDefaultSetting.ApplyDefaultsToTransitionFromScript(tr);
                    added++;
                }
                catch (System.Exception ex)
                {
                    Debug.LogException(ex);
                }
            }

            if (added > 0)
            {
                EditorUtility.SetDirty(controller);
                RepaintAnimatorControllerGraph(controller);
            }
            else
                Debug.Log("[AnimatorBinding] 収束用に追加したトランジションはありませんでした。");

            return true;
        }

        /// <summary>
        /// 最後に選択したステートへ収束（複数）／1件なら Make Transition モード。
        /// </summary>
        [Shortcut(ShortcutIds.NewTransitionConvergeToLast, KeyCode.M, ShortcutModifiers.Alt)]
        public static void ShortcutNewTransitionConvergeToLast()
        {
            TryNewTransitionConvergeToLast();
        }

        /// <summary>
        /// 最初に選択したステートから拡散（複数）／1件なら Make Transition モード。
        /// </summary>
        [Shortcut(ShortcutIds.NewTransitionDivergeFromFirst, KeyCode.T)]
        public static void ShortcutNewTransitionDivergeFromFirst()
        {
            TryNewTransitionDivergeFromFirst();
        }

        /// <summary>
        /// Animator グラフ上の表示中心（取得不能時は選択付近）に新規ステートを作成。
        /// </summary>
        [Shortcut(ShortcutIds.NewStateAtCursor, KeyCode.N, ShortcutModifiers.Alt)]
        public static void ShortcutNewStateAtCursor()
        {
            TryCreateNewStateAtCursor();
        }

        /// <summary>
        /// 選択が1つのときは Make Transition（矢印がカーソルについて次のクリックで確定）。2つ以上なら最後のステートへ収束するトランジションを追加。
        /// </summary>
        public static bool TryNewTransitionConvergeToLast()
        {
            var objs = Selection.objects;
            if (objs != null && objs.Length >= 2 && TryAddTransitionsConvergeToLastFromSelectionObjects())
                return true;

            var states = CollectAnimatorStatesInSelectionOrder();
            if (states.Count == 0)
            {
                if (AnimatorAnyStateGraphSelectionHelper.TryGetAnyStateHostStateMachine(out var anySm))
                    return AnimatorTransitionPickSession.BeginFromAnyState(anySm);

                Debug.LogWarning("[AnimatorBinding] AnimatorState か Any State を選択してください。");
                return false;
            }

            if (TryPickDestinationModeIfSingleStateOnly(states))
                return true;

            if (states.Count >= 2)
                return TryAddTransitionsConvergeToLastForStatesOnly(states);

            return false;
        }

        /// <summary>
        /// 選択が1つのときは <see cref="TryNewTransitionConvergeToLast"/> と同じく遷移先選択モード。
        /// 2つ以上で先頭が <see cref="AnimatorState"/> なら先頭から他へ通常トランジションを拡散。
        /// 先頭が Any State グラフノードなら、そのホストの Any State から 2 番目以降へ <see cref="AnimatorStateMachine.AddAnyStateTransition"/> で拡散。
        /// </summary>
        public static bool TryNewTransitionDivergeFromFirst()
        {
            var objs = Selection.objects;
            if (objs != null && objs.Length >= 2 && TryAddTransitionsDivergeFromFirstFromSelectionObjects())
                return true;

            var states = CollectAnimatorStatesInSelectionOrder();
            if (states.Count == 0)
            {
                if (AnimatorAnyStateGraphSelectionHelper.TryGetAnyStateHostStateMachine(out var anySm))
                    return AnimatorTransitionPickSession.BeginFromAnyState(anySm);

                Debug.LogWarning("[AnimatorBinding] AnimatorState か Any State を選択してください。");
                return false;
            }

            if (TryPickDestinationModeIfSingleStateOnly(states))
                return true;

            if (states.Count >= 2)
                return TryAddTransitionsDivergeFromFirstForStatesOnly(states);

            return false;
        }

        /// <summary>収束・拡散のどちらのショートカットでも、単一ステート選択時は同じ「遷移先を次に選ぶ」モードに入る。</summary>
        private static bool TryPickDestinationModeIfSingleStateOnly(IReadOnlyList<AnimatorState> states)
        {
            if (states.Count != 1) return false;
            return AnimatorMakeTransitionModeInternal.TryEnterMakeTransitionPickMode(states[0]);
        }

        private static List<AnimatorState> CollectAnimatorStatesInSelectionOrder()
        {
            var list = new List<AnimatorState>();
            var seen = new HashSet<int>();

            void TryAdd(Object o)
            {
                if (o is not AnimatorState st) return;
                if (!seen.Add(st.GetInstanceID())) return;
                list.Add(st);
            }

            foreach (var o in Selection.objects)
                TryAdd(o);

            foreach (var id in Selection.instanceIDs)
                TryAdd(EditorUtility.InstanceIDToObject(id));

            if (list.Count == 0 && Selection.activeObject is AnimatorState active && seen.Add(active.GetInstanceID()))
                list.Add(active);

            return list;
        }

        /// <summary>
        /// <see cref="Selection.objects"/> の<strong>最後</strong>を遷移先（ステート / サブステートマシン / Exit）、
        /// それ以外の <see cref="AnimatorState"/> を遷移元として収束トランジションを追加。
        /// </summary>
        private static bool TryAddTransitionsConvergeToLastFromSelectionObjects()
        {
            var objs = Selection.objects;
            if (objs == null || objs.Length < 2) return false;

            var destObj = objs[objs.Length - 1];
            if (!AnimatorGraphDestinationResolver.TryResolve(destObj, out var destState, out var destSm, out var toExit))
            {
                Debug.LogWarning("[AnimatorBinding] 遷移先を認識できませんでした（ステート・サブステートマシン・Exit ノードを最後に選択してください）。");
                return true;
            }

            var sources = new List<AnimatorState>();
            for (var i = 0; i < objs.Length - 1; i++)
            {
                if (objs[i] is AnimatorState st)
                    sources.Add(st);
            }

            if (sources.Count == 0)
            {
                Debug.LogWarning("[AnimatorBinding] 収束では、最後以外に遷移元の AnimatorState を1つ以上選択してください。");
                return true;
            }

            if (!ValidateSameController(sources, out var controller))
                return true;

            var path = AssetDatabase.GetAssetPath(sources[0]);
            if (!ValidateDestinationSameControllerAsset(path, destState, destSm, toExit))
            {
                Debug.LogWarning("[AnimatorBinding] 遷移先は同じ Animator Controller アセット上である必要があります。");
                return true;
            }

            Undo.RegisterCompleteObjectUndo(controller, "Create Transitions (Converge To Last)");
            var added = 0;
            foreach (var from in sources)
            {
                try
                {
                    AnimatorStateTransition tr;
                    if (toExit)
                    {
                        tr = from.AddExitTransition();
                    }
                    else if (destSm != null)
                    {
                        tr = from.AddTransition(destSm);
                    }
                    else
                    {
                        if (from == destState) continue;
                        tr = from.AddTransition(destState);
                    }

                    AnimatorDefaultSetting.ApplyDefaultsToTransitionFromScript(tr);
                    added++;
                }
                catch (System.Exception ex)
                {
                    Debug.LogException(ex);
                }
            }

            if (added > 0)
            {
                EditorUtility.SetDirty(controller);
                RepaintAnimatorControllerGraph(controller);
            }
            else
                Debug.Log("[AnimatorBinding] 収束用に追加したトランジションはありません（条件を満たしませんでした）。");

            return true;
        }

        /// <summary>
        /// <see cref="Selection.objects"/> の<strong>先頭</strong>を遷移元（<see cref="AnimatorState"/> または Any State ノード）、2番目以降を遷移先として拡散。
        /// </summary>
        private static bool TryAddTransitionsDivergeFromFirstFromSelectionObjects()
        {
            var objs = Selection.objects;
            if (objs == null || objs.Length < 2) return false;

            if (AnimatorAnyStateGraphSelectionHelper.IsAnyStateGraphNode(objs[0]) &&
                AnimatorAnyStateGraphSelectionHelper.TryResolveBestHostStateMachineFromAnyStateGraphNode(objs[0], out var anyHost) &&
                anyHost != null)
                return TryAddAnyStateTransitionsDivergeFromSelection(anyHost, objs);

            if (!(objs[0] is AnimatorState first))
            {
                Debug.LogWarning("[AnimatorBinding] 拡散では、最初に遷移元の AnimatorState または Any State を選択してください。");
                return true;
            }

            var path = AssetDatabase.GetAssetPath(first);
            if (string.IsNullOrEmpty(path))
                return true;

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
                return true;

            Undo.RegisterCompleteObjectUndo(controller, "Create Transitions (Diverge From First)");
            var added = 0;
            for (var i = 1; i < objs.Length; i++)
            {
                if (!AnimatorGraphDestinationResolver.TryResolve(objs[i], out var destState, out var destSm, out var toExit))
                {
                    Debug.LogWarning($"[AnimatorBinding] 遷移先を認識できませんでした: {(objs[i] != null ? objs[i].name : "(null)")}");
                    continue;
                }

                if (!ValidateDestinationSameControllerAsset(path, destState, destSm, toExit))
                {
                    Debug.LogWarning("[AnimatorBinding] 遷移先は同じ Animator Controller アセット上である必要があります。");
                    continue;
                }

                try
                {
                    AnimatorStateTransition tr;
                    if (toExit)
                    {
                        tr = first.AddExitTransition();
                    }
                    else if (destSm != null)
                    {
                        tr = first.AddTransition(destSm);
                    }
                    else
                    {
                        if (destState == first) continue;
                        tr = first.AddTransition(destState);
                    }

                    AnimatorDefaultSetting.ApplyDefaultsToTransitionFromScript(tr);
                    added++;
                }
                catch (System.Exception ex)
                {
                    Debug.LogException(ex);
                }
            }

            if (added > 0)
            {
                EditorUtility.SetDirty(controller);
                RepaintAnimatorControllerGraph(controller);
            }
            else
                Debug.Log("[AnimatorBinding] 拡散用に追加したトランジションはありません（条件を満たしませんでした）。");

            return true;
        }

        /// <summary>
        /// 先頭が Any State ノード、2番目以降が遷移先のとき、ホストの Any State から各遷移先へ拡散。
        /// </summary>
        private static bool TryAddAnyStateTransitionsDivergeFromSelection(AnimatorStateMachine host, Object[] objs)
        {
            if (host == null || objs == null || objs.Length < 2) return true;

            var path = AssetDatabase.GetAssetPath(host);
            if (string.IsNullOrEmpty(path))
                return true;

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
                return true;

            Undo.RegisterCompleteObjectUndo(controller, "Create Any State Transitions (Diverge From First)");
            var added = 0;
            for (var i = 1; i < objs.Length; i++)
            {
                if (!AnimatorGraphDestinationResolver.TryResolve(objs[i], out var destState, out var destSm, out var toExit))
                {
                    Debug.LogWarning($"[AnimatorBinding] 遷移先を認識できませんでした: {(objs[i] != null ? objs[i].name : "(null)")}");
                    continue;
                }

                if (toExit)
                {
                    Debug.LogWarning("[AnimatorBinding] Any State から Exit ノードへのトランジションは Unity の Animator では作成できません。");
                    continue;
                }

                if (!ValidateDestinationSameControllerAsset(path, destState, destSm, false))
                {
                    Debug.LogWarning("[AnimatorBinding] 遷移先は同じ Animator Controller アセット上である必要があります。");
                    continue;
                }

                try
                {
                    AnimatorStateTransition tr;
                    if (destSm != null)
                    {
                        if (destSm == host) continue;
                        tr = host.AddAnyStateTransition(destSm);
                    }
                    else
                    {
                        tr = host.AddAnyStateTransition(destState);
                    }

                    AnimatorDefaultSetting.ApplyDefaultsToTransitionFromScript(tr);
                    added++;
                }
                catch (System.Exception ex)
                {
                    Debug.LogException(ex);
                }
            }

            if (added > 0)
            {
                EditorUtility.SetDirty(controller);
                AnimatorTransitionPickSession.RefreshAnimatorGraphAfterTransitionEdit(controller, host);
            }
            else
                Debug.Log("[AnimatorBinding] Any State 拡散で追加したトランジションはありません（条件を満たしませんでした）。");

            return true;
        }

        /// <summary>各ステート（最後以外）から最後に選択したステートへ遷移を追加（選択がすべて <see cref="AnimatorState"/> のみのとき）。</summary>
        private static bool TryAddTransitionsConvergeToLastForStatesOnly(IReadOnlyList<AnimatorState> states)
        {
            var last = states[states.Count - 1];
            if (!ValidateSameController(states, out var controller))
                return false;

            Undo.RegisterCompleteObjectUndo(controller, "Create Transitions (Converge To Last)");
            var added = 0;
            for (var i = 0; i < states.Count - 1; i++)
            {
                var from = states[i];
                if (from == last) continue;
                try
                {
                    var tr = from.AddTransition(last);
                    AnimatorDefaultSetting.ApplyDefaultsToTransitionFromScript(tr);
                    added++;
                }
                catch (System.Exception ex)
                {
                    Debug.LogException(ex);
                }
            }

            if (added > 0)
            {
                EditorUtility.SetDirty(controller);
                RepaintAnimatorControllerGraph(controller);
            }
            else
                Debug.Log("[AnimatorBinding] 収束用に追加したトランジションはありません（最後と遷移元がすべて同一、または追加に失敗しました）。");

            return true;
        }

        /// <summary>先頭のステートから他の各ステートへ遷移を追加（選択がすべて <see cref="AnimatorState"/> のみのとき）。</summary>
        private static bool TryAddTransitionsDivergeFromFirstForStatesOnly(IReadOnlyList<AnimatorState> states)
        {
            var first = states[0];
            if (!ValidateSameController(states, out var controller))
                return false;

            Undo.RegisterCompleteObjectUndo(controller, "Create Transitions (Diverge From First)");
            var added = 0;
            for (var i = 1; i < states.Count; i++)
            {
                var to = states[i];
                if (to == first) continue;
                try
                {
                    var tr = first.AddTransition(to);
                    AnimatorDefaultSetting.ApplyDefaultsToTransitionFromScript(tr);
                    added++;
                }
                catch (System.Exception ex)
                {
                    Debug.LogException(ex);
                }
            }

            if (added > 0)
            {
                EditorUtility.SetDirty(controller);
                RepaintAnimatorControllerGraph(controller);
            }
            else
                Debug.Log("[AnimatorBinding] 拡散用に追加したトランジションはありません（先頭と遷移先がすべて同一、または追加に失敗しました）。");

            return true;
        }

        private static bool ValidateDestinationSameControllerAsset(string controllerAssetPath, AnimatorState destState, AnimatorStateMachine destSm, bool toExit)
        {
            if (toExit) return !string.IsNullOrEmpty(controllerAssetPath);
            if (destState != null)
                return AssetDatabase.GetAssetPath(destState) == controllerAssetPath;
            if (destSm != null)
                return AssetDatabase.GetAssetPath(destSm) == controllerAssetPath;
            return false;
        }

        private static void RepaintAnimatorControllerGraph(AnimatorController controller)
        {
            if (controller != null)
                EditorUtility.SetDirty(controller);
            EditorApplication.delayCall += () => InternalEditorUtility.RepaintAllViews();
        }

        private static bool ValidateSameController(IReadOnlyList<AnimatorState> states, out AnimatorController controller)
        {
            controller = null;
            if (states == null || states.Count == 0) return false;
            var path = AssetDatabase.GetAssetPath(states[0]);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("[AnimatorBinding] AnimatorController のパスを取得できませんでした。");
                return false;
            }

            for (var i = 1; i < states.Count; i++)
            {
                if (AssetDatabase.GetAssetPath(states[i]) != path)
                {
                    Debug.LogWarning("[AnimatorBinding] 選択したステートは同一の Animator Controller アセット上である必要があります。");
                    return false;
                }
            }

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
            {
                Debug.LogWarning("[AnimatorBinding] AnimatorController を読み込めませんでした。");
                return false;
            }

            return true;
        }

        public static bool TryCreateNewStateAtCursor()
        {
            if (!TryResolveTargetStateMachine(out var stateMachine, out var controller))
            {
                Debug.LogWarning("[AnimatorBinding] AnimatorState か AnimatorStateMachine を選択した状態で実行してください。");
                return false;
            }

            var hasCenterPos = TryGetAnimatorGraphCenterPosition(out var position);
            if (!hasCenterPos)
                position = GetFallbackPosition(stateMachine);
            position = ResolveNonOverlappingStatePosition(stateMachine, position);

            var name = GetUniqueStateName(stateMachine, "New State");

            Undo.RegisterCompleteObjectUndo(controller, "Create State At Cursor");
            var created = stateMachine.AddState(name, position);
            AnimatorDefaultSetting.ApplyDefaultsToStateFromScript(created);
            EditorUtility.SetDirty(controller);

            Selection.activeObject = created;
            Debug.Log(
                hasCenterPos
                    ? $"[AnimatorBinding] Animator エディタの中心位置にステートを作成しました: {created.name}"
                    : $"[AnimatorBinding] Animator エディタ中心を取得できなかったため、フォールバック位置にステートを作成しました: {created.name}",
                created);
            return true;
        }

        private static bool TryResolveTargetStateMachine(out AnimatorStateMachine stateMachine, out AnimatorController controller)
        {
            stateMachine = null;
            controller = null;

            if (Selection.activeObject is AnimatorStateMachine sm)
                stateMachine = sm;
            else if (Selection.activeObject is AnimatorState st)
                stateMachine = GetParentStateMachine(st);
            else
            {
                foreach (var o in Selection.objects)
                {
                    if (o is AnimatorStateMachine s)
                    {
                        stateMachine = s;
                        break;
                    }

                    if (o is AnimatorState state)
                    {
                        stateMachine = GetParentStateMachine(state);
                        if (stateMachine != null)
                            break;
                    }
                }
            }

            if (stateMachine == null)
                return false;

            var path = AssetDatabase.GetAssetPath(stateMachine);
            if (string.IsNullOrEmpty(path))
                return false;

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            return controller != null;
        }

        private static AnimatorStateMachine GetParentStateMachine(AnimatorState state)
        {
            if (state == null) return null;
            var path = AssetDatabase.GetAssetPath(state);
            if (string.IsNullOrEmpty(path)) return null;

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) return null;

            foreach (var layer in controller.layers)
            {
                var found = FindStateMachineContainingState(layer.stateMachine, state);
                if (found != null) return found;
            }

            return null;
        }

        private static AnimatorStateMachine FindStateMachineContainingState(AnimatorStateMachine stateMachine, AnimatorState target)
        {
            foreach (var child in stateMachine.states)
            {
                if (child.state == target)
                    return stateMachine;
            }

            foreach (var sub in stateMachine.stateMachines)
            {
                var found = FindStateMachineContainingState(sub.stateMachine, target);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static string GetUniqueStateName(AnimatorStateMachine stateMachine, string baseName)
        {
            var names = new HashSet<string>();
            foreach (var child in stateMachine.states)
            {
                if (child.state != null && !string.IsNullOrEmpty(child.state.name))
                    names.Add(child.state.name);
            }

            if (!names.Contains(baseName))
                return baseName;

            var index = 1;
            while (names.Contains($"{baseName} {index}"))
                index++;
            return $"{baseName} {index}";
        }

        private static Vector2 GetFallbackPosition(AnimatorStateMachine stateMachine)
        {
            var states = stateMachine.states;
            if (states == null || states.Length == 0)
                return Vector2.zero;

            var anchor = states[states.Length - 1].position;
            return anchor + new Vector3(240f, 0f, 0f);
        }

        private static Vector2 ResolveNonOverlappingStatePosition(AnimatorStateMachine stateMachine, Vector2 desiredPosition)
        {
            if (stateMachine == null) return desiredPosition;

            var position = desiredPosition;
            const float epsilon = 0.01f;
            const float offsetX = 40f;
            const float offsetY = 30f;
            const int maxAttempts = 500;

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                var overlapped = false;
                foreach (var child in stateMachine.states)
                {
                    if (child.state == null) continue;
                    var p = child.position;
                    if (Mathf.Abs(p.x - position.x) <= epsilon && Mathf.Abs(p.y - position.y) <= epsilon)
                    {
                        overlapped = true;
                        break;
                    }
                }

                if (!overlapped)
                    return position;

                position += new Vector2(offsetX, offsetY);
            }

            return position;
        }

        private static bool TryGetAnimatorGraphCenterPosition(out Vector2 position)
        {
            position = default;

            var toolType = AnimatorMakeTransitionModeInternal.GetCachedAnimatorControllerToolType();
            if (toolType == null)
                return false;

            EditorWindow window = null;
            foreach (var obj in Resources.FindObjectsOfTypeAll(toolType))
            {
                if (obj is EditorWindow ew)
                {
                    window = ew;
                    break;
                }
            }

            if (window == null)
                return false;

            return TryGetVector2WithLikelyNames(window, out position, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
        }

        private static bool TryGetVector2WithLikelyNames(object root, out Vector2 value, int depth, HashSet<object> visited)
        {
            value = default;
            if (root == null) return false;
            if (depth > 4) return false;
            if (!visited.Add(root)) return false;

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var candidateNames = new[]
            {
                "m_ViewCenter",
                "viewCenter",
                "m_GraphCenter",
                "graphCenter",
                "m_Center",
                "center",
                "m_ContentCenter",
                "contentCenter"
            };

            foreach (var name in candidateNames)
            {
                var field = root.GetType().GetField(name, flags);
                if (field != null && field.FieldType == typeof(Vector2))
                {
                    value = (Vector2)field.GetValue(root);
                    return true;
                }

                var prop = root.GetType().GetProperty(name, flags);
                if (prop != null && prop.PropertyType == typeof(Vector2) && prop.CanRead)
                {
                    value = (Vector2)prop.GetValue(root);
                    return true;
                }
            }

            foreach (var field in root.GetType().GetFields(flags))
            {
                var fieldValue = field.GetValue(root);
                if (fieldValue == null) continue;
                if (field.FieldType.IsPrimitive || field.FieldType == typeof(string)) continue;

                if (TryGetVector2WithLikelyNames(fieldValue, out value, depth + 1, visited))
                    return true;
            }

            return false;
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new();

            public new bool Equals(object x, object y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }

        [InitializeOnLoadMethod]
        private static void InitializeMenuHotkeys()
        {
            SyncMenuHotkeysFromCurrentShortcutSettings();
        }

        // [MenuItem(MenuPathConvergeToLast, true)]
        private static bool ValidateMenuConvergeToLast()
        {
            SyncMenuHotkeysFromCurrentShortcutSettings();
            return true;
        }

        // [MenuItem(MenuPathConvergeToLast, false, 110)]
        public static void MenuConvergeToLast()
        {
            TryNewTransitionConvergeToLast();
        }

        // [MenuItem(MenuPathDivergeFromFirst, true)]
        private static bool ValidateMenuDivergeFromFirst()
        {
            SyncMenuHotkeysFromCurrentShortcutSettings();
            return true;
        }

        // [MenuItem(MenuPathDivergeFromFirst, false, 111)]
        public static void MenuDivergeFromFirst()
        {
            TryNewTransitionDivergeFromFirst();
        }

        // [MenuItem(MenuPathNewStateAtCenter, true)]
        private static bool ValidateMenuNewStateAtCursor()
        {
            SyncMenuHotkeysFromCurrentShortcutSettings();
            return true;
        }

        // [MenuItem(MenuPathNewStateAtCenter, false, 112)]
        public static void MenuNewStateAtCursor()
        {
            TryCreateNewStateAtCursor();
        }

        private static void SyncMenuHotkeysFromCurrentShortcutSettings()
        {
            AnimatorMenuHotkeyDisplay.TrySetFromShortcutId(MenuPathConvergeToLast, ShortcutIds.NewTransitionConvergeToLast);
            AnimatorMenuHotkeyDisplay.TrySetFromShortcutId(MenuPathDivergeFromFirst, ShortcutIds.NewTransitionDivergeFromFirst);
            AnimatorMenuHotkeyDisplay.TrySetFromShortcutId(MenuPathNewStateAtCenter, ShortcutIds.NewStateAtCursor);
        }

        [MenuItem("SamiVRCBlocks-AvatarEditor/Settings/Animator Binding", false, 10)]
        public static void OpenAnimatorBindingPreferences()
        {
#if UNITY_2022_2_OR_NEWER
            SettingsService.OpenUserPreferences("Preferences/Samirin Editor Tools/Animator Binding");
#else
            EditorUtility.DisplayDialog(
                "Animator Binding",
                "ショートカット設定は Unity 2022.2 以降の Preferences で利用できます。",
                "OK");
#endif
        }
    }

    /// <summary>
    /// <see cref="ShortcutManager"/> の現在の割当を <c>Menu.SetHotkey</c> へ反映し、メニュー上は <c>(表記)</c> 形式にする。
    /// </summary>
    internal static class AnimatorMenuHotkeyDisplay
    {
        public static void TrySetFromShortcutId(string menuPath, string shortcutId)
        {
            if (string.IsNullOrEmpty(menuPath) || string.IsNullOrEmpty(shortcutId))
                return;

            var binding = ShortcutManager.instance.GetShortcutBinding(shortcutId);
            var hotkeyText = binding.ToString();
            if (string.IsNullOrWhiteSpace(hotkeyText))
            {
                TryInvokeSetHotkey(menuPath, "");
                return;
            }

            TryInvokeSetHotkey(menuPath, $"({hotkeyText})");
        }

        private static void TryInvokeSetHotkey(string menuPath, string hotkeyDisplay)
        {
            var menuType = typeof(MenuItem).Assembly.GetType("UnityEditor.Menu");
            if (menuType == null) return;

            var setHotkeyMethod = menuType.GetMethod(
                "SetHotkey",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(string) },
                null);
            if (setHotkeyMethod == null) return;

            setHotkeyMethod.Invoke(null, new object[] { menuPath, hotkeyDisplay });
        }
    }
}
