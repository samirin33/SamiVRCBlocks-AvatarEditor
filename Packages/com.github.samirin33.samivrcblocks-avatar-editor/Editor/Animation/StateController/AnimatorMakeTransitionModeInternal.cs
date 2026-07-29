using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditorInternal;
using UnityEngine;
using Samirin33.AvatarEditor.Animation.Editor;
using Object = UnityEngine.Object;
using AnimatorController = UnityEditor.Animations.AnimatorController;

namespace Samirin33.AvatarEditor.Tools.Editor
{
    /// <summary>
    /// グラフ上で選ばれた遷移先（通常ステート / サブステートマシン / Exit ノード）を解決する。
    /// </summary>
    internal static class AnimatorGraphDestinationResolver
    {
        /// <summary>
        /// 遷移先オブジェクトを <see cref="AnimatorState"/> / <see cref="AnimatorStateMachine"/> / Exit に分類する。
        /// </summary>
        public static bool TryResolve(Object selected, out AnimatorState destState, out AnimatorStateMachine destStateMachine, out bool toExit)
        {
            destState = null;
            destStateMachine = null;
            toExit = false;
            if (selected == null) return false;

            if (selected is AnimatorState st)
            {
                destState = st;
                return true;
            }

            if (selected is AnimatorStateMachine sm)
            {
                destStateMachine = sm;
                return true;
            }

            if (IsAnimatorExitGraphNode(selected))
            {
                toExit = true;
                return true;
            }

            return false;
        }

        /// <summary>Animator グラフの Exit ノード（通常の <see cref="AnimatorState"/> ではない）。</summary>
        public static bool IsAnimatorExitGraphNode(Object o)
        {
            if (o == null) return false;
            if (o is AnimatorState) return false;
            if (o is AnimatorController) return false;
            if (o is AnimatorTransition) return false;

            var typeName = o.GetType().Name;
            if (typeName.IndexOf("Exit", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (!string.IsNullOrEmpty(o.name))
            {
                var nn = o.name.Trim();
                if (nn.Equals("Exit", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Unity 標準の「Make Transition」（ドラフト矢印）に相当する操作。
    /// Animator グラフ上では内部 API が使えないことが多いため、
    /// 確実に動く「遷移先ステートを次に選択する」方式を主に用いる。
    /// </summary>
    internal static class AnimatorMakeTransitionModeInternal
    {
        private static Type _animatorControllerToolTypeCache;

        private static readonly string[] LinkMethodNameCandidates =
        {
            "StartLink", "BeginLink", "BeginMakeTransition", "StartMakeTransition", "EnterLinkMode",
            "StartLinking", "BeginLinking", "PrepareTransition", "DoMakeTransition", "MakeTransition",
            "BeginNewTransition", "StartNewTransition", "StartCreateTransition", "BeginCreateTransition"
        };

        /// <summary>
        /// 選択が1つの <see cref="AnimatorState"/> のとき、Make Transition 相当を開始する。
        /// </summary>
        public static bool TryEnterMakeTransitionPickMode(AnimatorState sourceState)
        {
            if (sourceState == null) return false;

            Selection.activeObject = sourceState;

            // 旧 Unity の CONTEXT/AnimatorState/Make Transition は Unity 6 以降で登録が無く、
            // EditorApplication.ExecuteMenuItem が失敗のたびにコンソールへエラーを出すため使わない。

            if (TryInvokeAnimatorWindowLinkMethod(sourceState))
                return true;

            // Animator ウィンドウでは上記がほぼ失敗するため、2クリック方式へ
            return AnimatorTransitionPickSession.Begin(sourceState);
        }

        private static bool TryInvokeAnimatorWindowLinkMethod(AnimatorState sourceState)
        {
            var toolType = ResolveAnimatorControllerToolType();
            if (toolType == null)
                return false;

            EditorWindow win = null;
            try
            {
                var windows = Resources.FindObjectsOfTypeAll(toolType);
                foreach (var w in windows)
                {
                    if (w is EditorWindow ew)
                    {
                        win = ew;
                        break;
                    }
                }

                if (win == null)
                    win = EditorWindow.GetWindow(toolType, false, null, false);

                if (win == null)
                    return false;

                win.Focus();

                foreach (var name in LinkMethodNameCandidates)
                {
                    var m = toolType.GetMethod(name,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(AnimatorState) },
                        null);
                    if (m == null) continue;
                    try
                    {
                        m.Invoke(win, new object[] { sourceState });
                        return true;
                    }
                    catch
                    {
                        // try next
                    }
                }

                foreach (var m in toolType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var ps = m.GetParameters();
                    if (ps.Length != 1 || ps[0].ParameterType != typeof(AnimatorState)) continue;
                    var n = m.Name;
                    if (n.IndexOf("Link", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("Transition", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("Edge", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    try
                    {
                        m.Invoke(win, new object[] { sourceState });
                        return true;
                    }
                    catch
                    {
                        // try next
                    }
                }

                // ネスト型（グラフビュー本体）にメソッドがある場合
                foreach (var nested in toolType.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
                {
                    foreach (var name in LinkMethodNameCandidates)
                    {
                        var m = nested.GetMethod(name,
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                            null,
                            new[] { typeof(AnimatorState) },
                            null);
                        if (m == null) continue;
                        foreach (var field in toolType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            if (field.FieldType != nested && !nested.IsAssignableFrom(field.FieldType)) continue;
                            var host = field.GetValue(win);
                            if (host == null) continue;
                            try
                            {
                                m.Invoke(host, new object[] { sourceState });
                                return true;
                            }
                            catch
                            {
                                // try next host
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AnimatorMakeTransitionModeInternal] " + ex.Message);
            }

            return false;
        }

        private static Type ResolveAnimatorControllerToolType()
        {
            if (_animatorControllerToolTypeCache != null)
                return _animatorControllerToolTypeCache;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.FullName == null ||
                    (!assembly.FullName.Contains("UnityEditor") && !assembly.FullName.Contains("Graphs")))
                    continue;
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (var t in types)
                {
                    if (t.Name != "AnimatorControllerTool") continue;
                    _animatorControllerToolTypeCache = t;
                    return _animatorControllerToolTypeCache;
                }
            }

            return null;
        }

        /// <summary><see cref="AnimatorTransitionPickSession"/> から Animator ウィンドウ型を取得するため。</summary>
        internal static Type GetCachedAnimatorControllerToolType() => ResolveAnimatorControllerToolType();
    }

    /// <summary>
    /// 遷移元を決めたあと、次の選択で遷移先 <see cref="AnimatorState"/> を選ぶと <see cref="AnimatorState.AddTransition"/> する。
    /// Unity がドラフト矢印 API を公開していないための実用的な代替。
    /// </summary>
    internal static class AnimatorTransitionPickSession
    {
        private static AnimatorState _from;
        private static AnimatorStateMachine _anyStateHost;
        private static bool _fromAnyState;
        private static bool _active;
        private static double _startedAt;
        private static double _notificationHideAt;
        private static EditorWindow _notificationWindow;

        public static bool IsActive => _active;

        public static bool Begin(AnimatorState sourceState)
        {
            if (sourceState == null) return false;

            End();

            _from = sourceState;
            _fromAnyState = false;
            _anyStateHost = null;
            _active = true;
            _startedAt = EditorApplication.timeSinceStartup;

            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.update += OnEditorUpdate;

            // 単一ステート起点のショートカット開始時は、次のクリックを明確にするため選択を解除する。
            Selection.objects = Array.Empty<Object>();
            Selection.activeObject = null;

            // Debug.Log(
            //     "[AnimatorBinding] 遷移先のステートを Animator ウィンドウで選択してください。" +
            //     "（Esc でキャンセル）\n" +
            //     "※ Unity のドラフト矢印は非公開 API のため、2 ステップ選択でトランジションを作成します。",
            //     sourceState);

            TryShowNotificationOnAnimatorWindow(
                "遷移先のステートを選択（Escでキャンセル）");

            return true;
        }

        /// <summary>
        /// Any State ノードを選択した状態で、次に選ぶ遷移先へ <see cref="AnimatorStateMachine.AddAnyStateTransition"/> する。
        /// </summary>
        public static bool BeginFromAnyState(AnimatorStateMachine hostStateMachine)
        {
            if (hostStateMachine == null) return false;

            End();

            _from = null;
            _fromAnyState = true;
            _anyStateHost = hostStateMachine;
            _active = true;
            _startedAt = EditorApplication.timeSinceStartup;

            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.update += OnEditorUpdate;

            // Debug.Log(
            //     "[AnimatorBinding] Any State からの遷移先を Animator ウィンドウで選択してください。" +
            //     "（Esc でキャンセル）\n" +
            //     "※ サブステートマシンへ接続する場合は、そのノードを選択してください。",
            //     hostStateMachine);

            TryShowNotificationOnAnimatorWindow(
                "Any State の遷移先を選択（Escでキャンセル）");

            return true;
        }

        private static void OnEditorUpdate()
        {
            if (!_active) return;

            if (_notificationWindow != null && EditorApplication.timeSinceStartup >= _notificationHideAt)
            {
                try
                {
                    _notificationWindow.RemoveNotification();
                }
                catch
                {
                    // ignored
                }
                _notificationWindow = null;
                _notificationHideAt = 0;
            }

            if (EditorApplication.timeSinceStartup - _startedAt > 120.0)
            {
                Debug.Log("[AnimatorBinding] トランジション選択モードをタイムアウトで終了しました。");
                End();
                return;
            }

            try
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                    Cancel();
            }
            catch
            {
                // Input が使えない環境では無視
            }
        }

        private static void OnSelectionChanged()
        {
            if (!_active) return;

            if (_fromAnyState)
            {
                if (_anyStateHost == null)
                {
                    End();
                    return;
                }

                if (AnimatorGraphDestinationResolver.IsAnimatorExitGraphNode(Selection.activeObject))
                {
                    Debug.LogWarning("[AnimatorBinding] Any State から Exit ノードへのトランジションは Unity の Animator では作成できません。");
                    return;
                }

                var anyDestState = Selection.activeObject as AnimatorState;
                var anyDestSm = Selection.activeObject as AnimatorStateMachine;

                if (anyDestState == null && anyDestSm == null)
                    return;

                if (anyDestSm != null && anyDestSm == _anyStateHost)
                    return;

                var pathHost = AssetDatabase.GetAssetPath(_anyStateHost);
                if (string.IsNullOrEmpty(pathHost))
                {
                    End();
                    return;
                }

                if (anyDestState != null)
                {
                    var pathDest = AssetDatabase.GetAssetPath(anyDestState);
                    if (string.IsNullOrEmpty(pathDest) || pathDest != pathHost)
                    {
                        Debug.LogWarning("[AnimatorBinding] Any State の遷移先は同一 Animator Controller 上である必要があります。");
                        return;
                    }
                }
                else if (anyDestSm != null)
                {
                    var pathDestSm = AssetDatabase.GetAssetPath(anyDestSm);
                    if (string.IsNullOrEmpty(pathDestSm) || pathDestSm != pathHost)
                    {
                        Debug.LogWarning("[AnimatorBinding] Any State の遷移先は同一 Animator Controller 上である必要があります。");
                        return;
                    }

                    if (TryShowStateMachineDestinationMenu(
                            anyDestSm,
                            onSelectStateMachine: selectedSm =>
                            {
                                CreateAnyStateTransition(_anyStateHost, selectedSm, null);
                            },
                            onSelectState: selectedState =>
                            {
                                CreateAnyStateTransition(_anyStateHost, null, selectedState);
                            }))
                    {
                        return;
                    }
                }

                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(pathHost);
                if (controller == null)
                {
                    End();
                    return;
                }

                try
                {
                    Undo.RegisterCompleteObjectUndo(controller, "Create Any State Transition (Pick Destination)");
                    AnimatorStateTransition tr;
                    if (anyDestState != null)
                        tr = _anyStateHost.AddAnyStateTransition(anyDestState);
                    else
                        tr = _anyStateHost.AddAnyStateTransition(anyDestSm);

                    AnimatorDefaultSetting.ApplyDefaultsToTransitionFromScript(tr);
                    RefreshAnimatorGraphAfterChange(controller, _anyStateHost);
                    var destLabel = anyDestState != null ? anyDestState.name : anyDestSm.name;
                    Debug.Log($"[AnimatorBinding] Any State からトランジションを作成しました → {destLabel}", Selection.activeObject);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
                finally
                {
                    End();
                }

                return;
            }

            if (_from == null) return;

            if (!AnimatorGraphDestinationResolver.TryResolve(Selection.activeObject, out var destState, out var destSm, out var toExit))
                return;

            var pathA = AssetDatabase.GetAssetPath(_from);
            if (string.IsNullOrEmpty(pathA))
            {
                End();
                return;
            }

            if (!toExit)
            {
                if (destState != null)
                {
                    var pathB = AssetDatabase.GetAssetPath(destState);
                    if (string.IsNullOrEmpty(pathB) || pathA != pathB)
                    {
                        Debug.LogWarning("[AnimatorBinding] 遷移元と遷移先は同一 Animator Controller アセット上である必要があります。");
                        return;
                    }
                }
                else if (destSm != null)
                {
                    var pathSm = AssetDatabase.GetAssetPath(destSm);
                    if (string.IsNullOrEmpty(pathSm) || pathA != pathSm)
                    {
                        Debug.LogWarning("[AnimatorBinding] 遷移元と遷移先は同一 Animator Controller アセット上である必要があります。");
                        return;
                    }

                    // グラフの空白クリックでは、現在表示中の親ステートマシンが
                    // Selection.activeObject になることがある。その場合は遷移先選択として扱わない。
                    var parentSm = AnimatorAnyStateGraphSelectionHelper.FindParentStateMachineForState(_from);
                    if (parentSm == destSm)
                        return;

                    if (TryShowStateMachineDestinationMenu(
                            destSm,
                            onSelectStateMachine: selectedSm =>
                            {
                                CreateStateTransition(_from, selectedSm, null, false);
                            },
                            onSelectState: selectedState =>
                            {
                                CreateStateTransition(_from, null, selectedState, false);
                            }))
                    {
                        return;
                    }
                }
            }

            var controller2 = AssetDatabase.LoadAssetAtPath<AnimatorController>(pathA);
            if (controller2 == null)
            {
                End();
                return;
            }

            try
            {
                Undo.RegisterCompleteObjectUndo(controller2, "Create Transition (Pick Destination)");
                AnimatorStateTransition tr;
                if (toExit)
                {
                    tr = _from.AddExitTransition();
                }
                else if (destSm != null)
                {
                    tr = _from.AddTransition(destSm);
                }
                else
                {
                    tr = _from.AddTransition(destState);
                }

                AnimatorDefaultSetting.ApplyDefaultsToTransitionFromScript(tr);
                RefreshAnimatorGraphAfterChange(controller2, null);
                var label = toExit ? "Exit" : (destSm != null ? destSm.name : destState.name);
                Debug.Log($"[AnimatorBinding] トランジションを作成しました: {_from.name} → {label}", Selection.activeObject);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                End();
            }
        }

        private static bool TryShowStateMachineDestinationMenu(
            AnimatorStateMachine targetStateMachine,
            Action<AnimatorStateMachine> onSelectStateMachine,
            Action<AnimatorState> onSelectState)
        {
            if (targetStateMachine == null)
                return false;

            if (targetStateMachine.states.Length == 0 && targetStateMachine.stateMachines.Length == 0)
                return false;

            AnimatorTransitionDestinationPickerWindow.ShowWindow(
                $"遷移先を選択: {targetStateMachine.name}",
                targetStateMachine,
                onSelectStateMachine,
                onSelectState);
            return true;
        }

        private static void CollectNestedStates(
            AnimatorStateMachine stateMachine,
            string prefix,
            List<(string path, AnimatorState state)> results)
        {
            if (stateMachine == null || results == null) return;

            foreach (var child in stateMachine.states)
            {
                if (child.state == null) continue;
                var label = string.IsNullOrEmpty(prefix) ? child.state.name : $"{prefix}/{child.state.name}";
                results.Add((label, child.state));
            }

            foreach (var childSm in stateMachine.stateMachines)
            {
                var sm = childSm.stateMachine;
                if (sm == null) continue;
                var nextPrefix = string.IsNullOrEmpty(prefix) ? sm.name : $"{prefix}/{sm.name}";
                CollectNestedStates(sm, nextPrefix, results);
            }
        }

        private static void CreateAnyStateTransition(
            AnimatorStateMachine sourceAnyStateMachine,
            AnimatorStateMachine destinationStateMachine,
            AnimatorState destinationState)
        {
            if (sourceAnyStateMachine == null) return;

            var path = AssetDatabase.GetAssetPath(sourceAnyStateMachine);
            if (string.IsNullOrEmpty(path))
            {
                End();
                return;
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
            {
                End();
                return;
            }

            try
            {
                Undo.RegisterCompleteObjectUndo(controller, "Create Any State Transition (Pick Destination)");
                AnimatorStateTransition tr;
                if (destinationState != null)
                {
                    tr = sourceAnyStateMachine.AddAnyStateTransition(destinationState);
                }
                else
                {
                    tr = sourceAnyStateMachine.AddAnyStateTransition(destinationStateMachine);
                }

                AnimatorDefaultSetting.ApplyDefaultsToTransitionFromScript(tr);
                RefreshAnimatorGraphAfterChange(controller, sourceAnyStateMachine);
                var destLabel = destinationState != null ? destinationState.name : destinationStateMachine.name;
                Debug.Log($"[AnimatorBinding] Any State からトランジションを作成しました → {destLabel}", tr);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                End();
            }
        }

        private static void CreateStateTransition(
            AnimatorState sourceState,
            AnimatorStateMachine destinationStateMachine,
            AnimatorState destinationState,
            bool toExit)
        {
            if (sourceState == null) return;

            var path = AssetDatabase.GetAssetPath(sourceState);
            if (string.IsNullOrEmpty(path))
            {
                End();
                return;
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
            {
                End();
                return;
            }

            try
            {
                Undo.RegisterCompleteObjectUndo(controller, "Create Transition (Pick Destination)");
                AnimatorStateTransition tr;
                if (toExit)
                {
                    tr = sourceState.AddExitTransition();
                }
                else if (destinationStateMachine != null)
                {
                    tr = sourceState.AddTransition(destinationStateMachine);
                }
                else
                {
                    tr = sourceState.AddTransition(destinationState);
                }

                AnimatorDefaultSetting.ApplyDefaultsToTransitionFromScript(tr);
                RefreshAnimatorGraphAfterChange(controller, null);
                var label = toExit ? "Exit" : (destinationStateMachine != null ? destinationStateMachine.name : destinationState.name);
                Debug.Log($"[AnimatorBinding] トランジションを作成しました: {sourceState.name} → {label}", tr);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                End();
            }
        }

        public static void Cancel()
        {
            if (!_active) return;
            Debug.Log("[AnimatorBinding] トランジション選択モードをキャンセルしました。");
            End();
        }

        private static void End()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.update -= OnEditorUpdate;
            _active = false;
            _from = null;
            _fromAnyState = false;
            _anyStateHost = null;
            _notificationWindow = null;
            _notificationHideAt = 0;
        }

        private static void TryShowNotificationOnAnimatorWindow(string message)
        {
            var toolType = AnimatorMakeTransitionModeInternal.GetCachedAnimatorControllerToolType();
            if (toolType == null) return;
            try
            {
                var win = EditorWindow.GetWindow(toolType, false, null, false);
                if (win != null)
                {
                    win.ShowNotification(new GUIContent(message));
                    _notificationWindow = win;
                    _notificationHideAt = EditorApplication.timeSinceStartup + 0.2d;
                }
            }
            catch
            {
                // ignored
            }
        }

        /// <summary>
        /// スクリプトで Any State 遷移などを追加した直後、Animator グラフが更新されないことがあるため再描画を促す。
        /// </summary>
        private static void RefreshAnimatorGraphAfterChange(AnimatorController controller, AnimatorStateMachine stateMachine)
        {
            if (controller != null)
                EditorUtility.SetDirty(controller);
            if (stateMachine != null)
            {
                EditorUtility.SetDirty(stateMachine);
                MarkStateMachineAncestorsDirty(controller, stateMachine);
            }

            void RepaintAnimatorTools()
            {
                var toolType = AnimatorMakeTransitionModeInternal.GetCachedAnimatorControllerToolType();
                if (toolType != null)
                {
                    foreach (var w in Resources.FindObjectsOfTypeAll(toolType))
                    {
                        if (w is EditorWindow ew)
                            ew.Repaint();
                    }
                }

                InternalEditorUtility.RepaintAllViews();
            }

            RepaintAnimatorTools();
            EditorApplication.delayCall += RepaintAnimatorTools;
            EditorApplication.delayCall += () =>
            {
                EditorApplication.delayCall += RepaintAnimatorTools;
            };
        }

        /// <summary>ネストしたステートマシンを変更したとき、親チェーンも Dirty にしてグラフの再評価を確実にする。</summary>
        private static void MarkStateMachineAncestorsDirty(AnimatorController controller, AnimatorStateMachine target)
        {
            if (controller == null || target == null) return;
            foreach (var layer in controller.layers)
            {
                if (MarkDirtyIfContainsDescendant(layer.stateMachine, target))
                    EditorUtility.SetDirty(layer.stateMachine);
            }
        }

        private static bool MarkDirtyIfContainsDescendant(AnimatorStateMachine root, AnimatorStateMachine target)
        {
            if (root == null) return false;
            if (root == target)
            {
                EditorUtility.SetDirty(root);
                return true;
            }

            foreach (var child in root.stateMachines)
            {
                var sm = child.stateMachine;
                if (sm == null) continue;
                if (MarkDirtyIfContainsDescendant(sm, target))
                {
                    EditorUtility.SetDirty(root);
                    return true;
                }
            }

            return false;
        }

        /// <summary>他クラスから一括 Any State 追加後などにグラフ更新だけ行いたい場合。</summary>
        internal static void RefreshAnimatorGraphAfterTransitionEdit(AnimatorController controller, AnimatorStateMachine anyStateHost)
        {
            RefreshAnimatorGraphAfterChange(controller, anyStateHost);
        }
    }

    /// <summary>
    /// Animator ウィンドウで「Any State」ノードが選択されたとき、
    /// 紐づく <see cref="AnimatorStateMachine"/> をグラフノード内部から取得する。
    /// </summary>
    internal static class AnimatorAnyStateGraphSelectionHelper
    {
        /// <summary>Animator グラフ上の Any State ノードか（<see cref="AnimatorState"/> 等は除外）。</summary>
        internal static bool IsAnyStateGraphNode(Object o) => IsLikelyAnyStateGraphNode(o);

        /// <summary>
        /// Any State グラフノードから、<see cref="AnimatorStateMachine.AddAnyStateTransition"/> を呼ぶべきホストを推定する。
        /// 反射で複数の <see cref="AnimatorStateMachine"/> が取れる場合はコントローラー階層で最も深いものを採用（サブステート内 Any State 対策）。
        /// </summary>
        internal static bool TryResolveBestHostStateMachineFromAnyStateGraphNode(Object graphNode, out AnimatorStateMachine host)
        {
            host = null;
            if (!IsLikelyAnyStateGraphNode(graphNode)) return false;

            var candidates = new List<AnimatorStateMachine>();
            var seenSm = new HashSet<int>();
            var visited = new HashSet<object>();
            CollectAnimatorStateMachinesFromGraphObject(graphNode, 0, visited, candidates, seenSm);

            if (candidates.Count == 0)
                return TryExtractAnimatorStateMachine(graphNode, out host);

            var path = AssetDatabase.GetAssetPath(candidates[0]);
            if (string.IsNullOrEmpty(path))
                return TryExtractAnimatorStateMachine(graphNode, out host);

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
                return TryExtractAnimatorStateMachine(graphNode, out host);

            AnimatorStateMachine best = null;
            var bestDepth = -1;
            foreach (var sm in candidates)
            {
                if (sm == null) continue;
                if (!IsStateMachineUnderController(controller, sm)) continue;
                var d = GetStateMachineDepthInController(controller, sm);
                if (d > bestDepth)
                {
                    bestDepth = d;
                    best = sm;
                }
            }

            if (best != null)
            {
                host = best;
                return true;
            }

            return TryExtractAnimatorStateMachine(graphNode, out host);
        }

        private static bool IsStateMachineUnderController(AnimatorController controller, AnimatorStateMachine sm)
        {
            if (controller == null || sm == null) return false;
            foreach (var layer in controller.layers)
            {
                if (StateMachineContainsRecursive(layer.stateMachine, sm))
                    return true;
            }

            return false;
        }

        private static bool StateMachineContainsRecursive(AnimatorStateMachine root, AnimatorStateMachine target)
        {
            if (root == null || target == null) return false;
            if (root == target) return true;
            foreach (var child in root.stateMachines)
            {
                if (child.stateMachine != null && StateMachineContainsRecursive(child.stateMachine, target))
                    return true;
            }

            return false;
        }

        private static int GetStateMachineDepthInController(AnimatorController controller, AnimatorStateMachine sm)
        {
            if (controller == null || sm == null) return -1;
            var best = -1;
            foreach (var layer in controller.layers)
            {
                var d = GetStateMachineDepthRecursive(layer.stateMachine, sm, 0);
                if (d > best)
                    best = d;
            }

            return best;
        }

        private static int GetStateMachineDepthRecursive(AnimatorStateMachine root, AnimatorStateMachine target, int depth)
        {
            if (root == null) return -1;
            if (root == target) return depth;
            foreach (var child in root.stateMachines)
            {
                if (child.stateMachine == null) continue;
                var d = GetStateMachineDepthRecursive(child.stateMachine, target, depth + 1);
                if (d >= 0) return d;
            }

            return -1;
        }

        private static void CollectAnimatorStateMachinesFromGraphObject(
            object obj,
            int depth,
            HashSet<object> visited,
            List<AnimatorStateMachine> results,
            HashSet<int> seenSmIds)
        {
            if (obj == null || depth > 14) return;
            if (!visited.Add(obj)) return;

            var t = obj.GetType();
            if (t.IsPrimitive || t == typeof(string)) return;

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var f in t.GetFields(flags))
            {
                if (f.FieldType == typeof(AnimatorStateMachine))
                {
                    var v = f.GetValue(obj) as AnimatorStateMachine;
                    if (v != null && seenSmIds.Add(v.GetInstanceID()))
                        results.Add(v);
                    continue;
                }

                object fv;
                try
                {
                    fv = f.GetValue(obj);
                }
                catch
                {
                    continue;
                }

                if (fv == null) continue;
                if (f.FieldType.IsPrimitive || f.FieldType == typeof(string)) continue;
                CollectAnimatorStateMachinesFromGraphObject(fv, depth + 1, visited, results, seenSmIds);
            }

            foreach (var p in t.GetProperties(flags))
            {
                if (p.PropertyType != typeof(AnimatorStateMachine) || !p.CanRead) continue;
                AnimatorStateMachine v;
                try
                {
                    v = p.GetValue(obj) as AnimatorStateMachine;
                }
                catch
                {
                    continue;
                }

                if (v != null && seenSmIds.Add(v.GetInstanceID()))
                    results.Add(v);
            }
        }

        public static AnimatorStateMachine FindParentStateMachineForState(AnimatorState state)
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

        public static bool TryGetAnyStateHostStateMachine(out AnimatorStateMachine stateMachine)
        {
            stateMachine = null;
            foreach (var o in Selection.objects)
            {
                if (!IsLikelyAnyStateGraphNode(o)) continue;
                if (TryResolveBestHostStateMachineFromAnyStateGraphNode(o, out var sm) && sm != null)
                {
                    stateMachine = sm;
                    return true;
                }
            }

            if (Selection.activeObject != null &&
                IsLikelyAnyStateGraphNode(Selection.activeObject) &&
                TryResolveBestHostStateMachineFromAnyStateGraphNode(Selection.activeObject, out var sm2) &&
                sm2 != null)
            {
                stateMachine = sm2;
                return true;
            }

            return false;
        }

        private static AnimatorStateMachine FindStateMachineContainingState(AnimatorStateMachine sm, AnimatorState target)
        {
            if (sm == null || target == null) return null;

            foreach (var s in sm.states)
            {
                if (s.state == target) return sm;
            }

            foreach (var sub in sm.stateMachines)
            {
                var found = FindStateMachineContainingState(sub.stateMachine, target);
                if (found != null) return found;
            }

            return null;
        }

        private static bool IsLikelyAnyStateGraphNode(Object o)
        {
            if (o == null) return false;
            if (o is AnimatorState) return false;
            if (o is AnimatorController) return false;
            if (o is AnimatorTransition) return false;

            var t = o.GetType();
            var typeName = t.Name;
            if (typeName.IndexOf("AnyState", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (!string.IsNullOrEmpty(o.name))
            {
                var nn = o.name.Trim();
                if (nn.Equals("Entry", StringComparison.OrdinalIgnoreCase)) return false;
                if (nn.Equals("Any State", StringComparison.OrdinalIgnoreCase)) return true;
                if (nn.Equals("AnyState", StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        private static bool TryExtractAnimatorStateMachine(Object root, out AnimatorStateMachine found)
        {
            found = null;
            if (root is AnimatorStateMachine asm)
            {
                found = asm;
                return true;
            }

            if (root == null) return false;

            var visited = new HashSet<object>();
            return TryExtractAnimatorStateMachineDepth(root, 0, visited, out found);
        }

        private static bool TryExtractAnimatorStateMachineDepth(object obj, int depth, HashSet<object> visited, out AnimatorStateMachine found)
        {
            found = null;
            if (obj == null || depth > 12) return false;
            if (!visited.Add(obj)) return false;

            var t = obj.GetType();
            if (t.IsPrimitive || t == typeof(string)) return false;

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var f in t.GetFields(flags))
            {
                if (f.FieldType != typeof(AnimatorStateMachine)) continue;
                var v = f.GetValue(obj) as AnimatorStateMachine;
                if (v != null)
                {
                    found = v;
                    return true;
                }
            }

            foreach (var p in t.GetProperties(flags))
            {
                if (p.PropertyType != typeof(AnimatorStateMachine) || !p.CanRead) continue;
                AnimatorStateMachine v;
                try
                {
                    v = p.GetValue(obj) as AnimatorStateMachine;
                }
                catch
                {
                    continue;
                }

                if (v != null)
                {
                    found = v;
                    return true;
                }
            }

            foreach (var f in t.GetFields(flags))
            {
                if (f.FieldType == typeof(AnimatorStateMachine)) continue;
                object fv;
                try
                {
                    fv = f.GetValue(obj);
                }
                catch
                {
                    continue;
                }

                if (fv == null) continue;
                if (f.FieldType.IsPrimitive || f.FieldType == typeof(string)) continue;
                if (TryExtractAnimatorStateMachineDepth(fv, depth + 1, visited, out found))
                    return true;
            }

            return false;
        }
    }

    internal sealed class AnimatorTransitionDestinationPickerWindow : EditorWindow
    {
        private string _titleText;
        private AnimatorStateMachine _rootSm;
        private AnimatorStateMachine _viewSm;
        private readonly List<AnimatorStateMachine> _drillStack = new List<AnimatorStateMachine>();
        private Action<AnimatorStateMachine> _onSelectStateMachine;
        private Action<AnimatorState> _onSelectState;
        private Vector2 _scroll;
        private float _zoom = 1f;
        private Object _unusedHighlight;

        internal static void ShowWindow(
            string titleText,
            AnimatorStateMachine rootSm,
            Action<AnimatorStateMachine> onSelectStateMachine,
            Action<AnimatorState> onSelectState)
        {
            var win = CreateInstance<AnimatorTransitionDestinationPickerWindow>();
            win.titleContent = new GUIContent("Transition Dest");
            win._titleText = titleText;
            win._rootSm = rootSm;
            win._viewSm = rootSm;
            win._drillStack.Clear();
            win._onSelectStateMachine = onSelectStateMachine;
            win._onSelectState = onSelectState;
            win._zoom = 0f;
            win._scroll = Vector2.zero;
            win.minSize = new Vector2(480f, 400f);
            win.ShowUtility();
            win.Focus();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(_titleText ?? "遷移先を選択", EditorStyles.boldLabel);
            EditorGUILayout.Space(6);

            if (_rootSm == null || _viewSm == null)
            {
                EditorGUILayout.HelpBox("対象のサブステートがありません。", MessageType.Info);
                if (GUILayout.Button("閉じる"))
                    Close();
                return;
            }

            EditorGUILayout.HelpBox(
                    "ノード名は末尾のみ表示します。サブステート（青）をクリックすると内側のグラフへ。ステート（緑）で確定して閉じます。"
                    + "ホイールでズーム、中ボタンドラッグでグラフをパン、スクロールバーでも移動できます。",
                MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_drillStack.Count == 0))
                {
                    if (GUILayout.Button("← 上の階層へ", GUILayout.Width(120f)))
                    {
                        _viewSm = _drillStack[_drillStack.Count - 1];
                        _drillStack.RemoveAt(_drillStack.Count - 1);
                    }
                }

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("-", GUILayout.Width(28f)))
                    _zoom = Mathf.Max(0.12f, _zoom - 0.12f);
                var zoomLabel = _zoom <= 0f ? "自動" : $"{Mathf.RoundToInt(_zoom * 100f)}%";
                GUILayout.Label(zoomLabel, GUILayout.Width(44f));
                if (GUILayout.Button("+", GUILayout.Width(28f)))
                    _zoom = Mathf.Min(5f, _zoom + 0.12f);
            }

            EditorGUILayout.LabelField(
                TransitionDestGraphCanvas.BuildBreadcrumbPath(_rootSm, _viewSm),
                EditorStyles.miniLabel);

            if (GUILayout.Button("このサブステート本体で確定", GUILayout.Height(26)))
            {
                try
                {
                    _onSelectStateMachine?.Invoke(_viewSm);
                }
                finally
                {
                    Close();
                }
            }

            EditorGUILayout.Space(4);

            var nodes = new List<TransitionDestGraphNode>();
            CollectDirectChildNodes(_viewSm, nodes);
            if (nodes.Count == 0)
                EditorGUILayout.HelpBox("この階層にノードがありません。", MessageType.Info);
            else
            {
                _unusedHighlight = null;
                TransitionDestGraphCanvas.Draw(
                    ref _scroll,
                    ref _zoom,
                    nodes,
                    ref _unusedHighlight,
                    sm =>
                    {
                        if (sm == null)
                            return;
                        _drillStack.Add(_viewSm);
                        _viewSm = sm;
                        _zoom = 0f;
                        _scroll = Vector2.zero;
                    },
                    st =>
                    {
                        try
                        {
                            _onSelectState?.Invoke(st);
                        }
                        finally
                        {
                            Close();
                        }
                    },
                    new Vector2(Mathf.Max(400f, position.width - 32f), 300f),
                    GUILayout.MinHeight(300f),
                    GUILayout.MinWidth(Mathf.Max(400f, position.width - 24f)));
            }

            EditorGUILayout.Space(6);
            if (GUILayout.Button("キャンセル"))
                Close();
        }

        private static void CollectDirectChildNodes(AnimatorStateMachine sm, List<TransitionDestGraphNode> nodes)
        {
            if (sm == null || nodes == null)
                return;

            foreach (var cs in sm.states)
            {
                if (cs.state == null)
                    continue;
                nodes.Add(new TransitionDestGraphNode
                {
                    shortLabel = cs.state.name,
                    targetState = cs.state,
                    graphPosition = cs.position,
                    hasGraphPosition = true
                });
            }

            foreach (var c in sm.stateMachines)
            {
                var inner = c.stateMachine;
                if (inner == null)
                    continue;
                nodes.Add(new TransitionDestGraphNode
                {
                    shortLabel = inner.name,
                    targetStateMachine = inner,
                    graphPosition = c.position,
                    hasGraphPosition = true
                });
            }
        }
    }
}
