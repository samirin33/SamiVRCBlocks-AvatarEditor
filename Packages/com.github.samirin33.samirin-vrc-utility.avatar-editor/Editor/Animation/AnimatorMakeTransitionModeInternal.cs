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

                    if (HasExistingAnyStateTransition(_anyStateHost, anyDestState, null))
                    {
                        Debug.Log("[AnimatorBinding] 既に Any State からこのステートへのトランジションがあります。", anyDestState);
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

                    if (HasExistingAnyStateTransition(_anyStateHost, null, anyDestSm))
                    {
                        Debug.Log("[AnimatorBinding] 既に Any State からこのサブステートマシンへのトランジションがあります。", anyDestSm);
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

            if (!toExit && destState != null && destState == _from)
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
                    if (HasExitTransitionFromState(_from))
                    {
                        Debug.Log("[AnimatorBinding] 既にこのステートから Exit へのトランジションがあります。", _from);
                        return;
                    }

                    tr = _from.AddExitTransition();
                }
                else if (destSm != null)
                {
                    if (HasTransitionToStateMachine(_from, destSm))
                    {
                        Debug.Log("[AnimatorBinding] 既にこのサブステートマシンへのトランジションがあります。", destSm);
                        return;
                    }

                    tr = _from.AddTransition(destSm);
                }
                else
                {
                    if (HasSimpleTransitionToState(_from, destState))
                    {
                        Debug.Log("[AnimatorBinding] 既にこのステートへのトランジションがあります。", destState);
                        return;
                    }

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

        private static bool HasSimpleTransitionToState(AnimatorState from, AnimatorState to)
        {
            if (from == null || to == null) return false;
            foreach (var tr in from.transitions)
            {
                if (tr.isExit) continue;
                if (tr.destinationStateMachine != null) continue;
                if (tr.destinationState == to) return true;
            }

            return false;
        }

        private static bool HasTransitionToStateMachine(AnimatorState from, AnimatorStateMachine sm)
        {
            if (from == null || sm == null) return false;
            foreach (var tr in from.transitions)
            {
                if (tr.isExit) continue;
                if (tr.destinationStateMachine == sm) return true;
            }

            return false;
        }

        private static bool TryShowStateMachineDestinationMenu(
            AnimatorStateMachine targetStateMachine,
            Action<AnimatorStateMachine> onSelectStateMachine,
            Action<AnimatorState> onSelectState)
        {
            if (targetStateMachine == null) return false;

            var nestedStates = new List<(string path, AnimatorState state)>();
            CollectNestedStates(targetStateMachine, "", nestedStates);
            if (nestedStates.Count == 0)
                return false;

            var entries = new List<AnimatorTransitionDestinationPickerWindow.Entry>
            {
                AnimatorTransitionDestinationPickerWindow.Entry.ForStateMachine(
                    $"{targetStateMachine.name} (サブステート本体)",
                    targetStateMachine,
                    onSelectStateMachine)
            };

            foreach (var item in nestedStates)
            {
                entries.Add(AnimatorTransitionDestinationPickerWindow.Entry.ForState(
                    $"{targetStateMachine.name}/{item.path}",
                    item.state,
                    onSelectState));
            }

            AnimatorTransitionDestinationPickerWindow.ShowWindow(
                $"遷移先を選択: {targetStateMachine.name}",
                entries);
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
                    if (HasExistingAnyStateTransition(sourceAnyStateMachine, destinationState, null))
                    {
                        Debug.Log("[AnimatorBinding] 既に Any State からこのステートへのトランジションがあります。", destinationState);
                        return;
                    }

                    tr = sourceAnyStateMachine.AddAnyStateTransition(destinationState);
                }
                else
                {
                    if (HasExistingAnyStateTransition(sourceAnyStateMachine, null, destinationStateMachine))
                    {
                        Debug.Log("[AnimatorBinding] 既に Any State からこのサブステートマシンへのトランジションがあります。", destinationStateMachine);
                        return;
                    }

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
                    if (HasExitTransitionFromState(sourceState))
                    {
                        Debug.Log("[AnimatorBinding] 既にこのステートから Exit へのトランジションがあります。", sourceState);
                        return;
                    }

                    tr = sourceState.AddExitTransition();
                }
                else if (destinationStateMachine != null)
                {
                    if (HasTransitionToStateMachine(sourceState, destinationStateMachine))
                    {
                        Debug.Log("[AnimatorBinding] 既にこのサブステートマシンへのトランジションがあります。", destinationStateMachine);
                        return;
                    }

                    tr = sourceState.AddTransition(destinationStateMachine);
                }
                else
                {
                    if (HasSimpleTransitionToState(sourceState, destinationState))
                    {
                        Debug.Log("[AnimatorBinding] 既にこのステートへのトランジションがあります。", destinationState);
                        return;
                    }

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

        private static bool HasExitTransitionFromState(AnimatorState from)
        {
            if (from == null) return false;
            foreach (var tr in from.transitions)
            {
                if (tr.isExit) return true;
            }

            return false;
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
        }

        private static bool HasExistingAnyStateTransition(
            AnimatorStateMachine host,
            AnimatorState destState,
            AnimatorStateMachine destSm)
        {
            if (host == null) return false;
            foreach (var tr in host.anyStateTransitions)
            {
                if (tr == null) continue;
                if (destState != null && tr.destinationState == destState && tr.destinationStateMachine == null)
                    return true;
                if (destSm != null && tr.destinationStateMachine == destSm)
                    return true;
            }

            return false;
        }

        private static void TryShowNotificationOnAnimatorWindow(string message)
        {
            var toolType = AnimatorMakeTransitionModeInternal.GetCachedAnimatorControllerToolType();
            if (toolType == null) return;
            try
            {
                var win = EditorWindow.GetWindow(toolType, false, null, false);
                if (win != null)
                    win.ShowNotification(new GUIContent(message));
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
                EditorUtility.SetDirty(stateMachine);

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
        }
    }

    /// <summary>
    /// Animator ウィンドウで「Any State」ノードが選択されたとき、
    /// 紐づく <see cref="AnimatorStateMachine"/> をグラフノード内部から取得する。
    /// </summary>
    internal static class AnimatorAnyStateGraphSelectionHelper
    {
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
                if (TryExtractAnimatorStateMachine(o, out var sm) && sm != null)
                {
                    stateMachine = sm;
                    return true;
                }
            }

            if (Selection.activeObject != null &&
                IsLikelyAnyStateGraphNode(Selection.activeObject) &&
                TryExtractAnimatorStateMachine(Selection.activeObject, out var sm2) &&
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
}

internal sealed class AnimatorTransitionDestinationPickerWindow : EditorWindow
{
    internal sealed class Entry
    {
        public string label;
        public Action onClick;

        public static Entry ForStateMachine(string label, AnimatorStateMachine sm, Action<AnimatorStateMachine> callback)
        {
            return new Entry
            {
                label = label,
                onClick = () => callback?.Invoke(sm)
            };
        }

        public static Entry ForState(string label, AnimatorState state, Action<AnimatorState> callback)
        {
            return new Entry
            {
                label = label,
                onClick = () => callback?.Invoke(state)
            };
        }
    }

    private string _titleText;
    private List<Entry> _entries;
    private Vector2 _scroll;

    internal static void ShowWindow(string titleText, List<Entry> entries)
    {
        var win = CreateInstance<AnimatorTransitionDestinationPickerWindow>();
        win.titleContent = new GUIContent("Transition Dest");
        win._titleText = titleText;
        win._entries = entries ?? new List<Entry>();
        win.minSize = new Vector2(420f, 300f);
        win.ShowUtility();
        win.Focus();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField(_titleText ?? "遷移先を選択", EditorStyles.boldLabel);
        EditorGUILayout.Space(6);

        if (_entries == null || _entries.Count == 0)
        {
            EditorGUILayout.HelpBox("選択候補がありません。", MessageType.Info);
            if (GUILayout.Button("閉じる"))
                Close();
            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (var entry in _entries)
        {
            if (entry == null) continue;
            if (GUILayout.Button(entry.label, GUILayout.Height(22)))
            {
                try
                {
                    entry.onClick?.Invoke();
                }
                finally
                {
                    Close();
                }
            }
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(6);
        if (GUILayout.Button("キャンセル"))
            Close();
    }
}
