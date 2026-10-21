#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Samirin33.SamirinVRCUtility.AvatarEditor
{
    public partial class AnimationClipSelector
    {
        #region Animator ウィンドウ連携

        /// <summary>
        /// クリップを使用しているAnimatorStateを検索し、Animatorウィンドウで選択する。
        /// pathPrefix指定時: そのサブステート内のみで検索（例: "Base Layer|Locomotion" なら Locomotion 内のみ）。
        /// Auto Live Link 方式: GameObject を選択して Animator ウィンドウを同期させてからステートを選択。
        /// </summary>
        private static void SelectUsageInAnimator(GameObject animatorRoot, RuntimeAnimatorController controller, AnimationClip clip, string pathPrefix = null)
        {
            if (controller == null || clip == null) return;

            var statesInScope = FindAllStatesUsingClipInScope(controller, clip, pathPrefix);
            if (statesInScope == null || statesInScope.Count == 0)
            {
                Debug.LogWarning($"[AnimationClipSelector] クリップ「{clip.name}」を使用しているAnimatorStateが見つかりませんでした。");
                return;
            }

            var baseController = controller is AnimatorOverrideController ov
                ? ov.runtimeAnimatorController as AnimatorController
                : controller as AnimatorController;
            if (baseController == null) return;

            var first = statesInScope[0];
            var capturedRoot = animatorRoot != null ? animatorRoot : FindGameObjectWithController(controller);
            var capturedController = baseController;
            var capturedLayerIndex = first.layerIndex;
            var capturedState = first.state;
            var capturedStateMachine = first.stateMachine;
            var capturedPath = first.stateMachinePath;
            var capturedStates = statesInScope.Select(x => x.state).ToArray();

            if (capturedRoot != null)
                Selection.activeGameObject = capturedRoot;
            else
                Selection.activeObject = baseController;

            EditorApplication.ExecuteMenuItem("Window/Animation/Animator");

            EditorApplication.delayCall += () =>
            {
                EditorApplication.delayCall += () =>
                {
                    if (capturedRoot != null)
                        Selection.activeGameObject = capturedRoot;
                    else
                        Selection.activeObject = capturedController;

                    var context = capturedRoot != null ? (UnityEngine.Object)capturedRoot : capturedController;
                    if (capturedStates.Length == 1)
                    {
                        Selection.SetActiveObjectWithContext(capturedStates[0], context);
                    }
                    else
                    {
                        Selection.objects = capturedStates;
                    }
                    EditorGUIUtility.PingObject(capturedStates[0]);

                    var windowType = GetAnimatorControllerWindowType();
                    var animatorWindow = windowType != null ? GetAnimatorControllerWindow(windowType) : null;
                    if (animatorWindow != null)
                    {
                        EditorWindow.FocusWindowIfItsOpen(windowType);
                        SetAnimatorWindowState(animatorWindow, capturedController, capturedLayerIndex, capturedState, capturedStateMachine, capturedPath);
                        animatorWindow.Repaint();
                    }
                };
            };
        }

        private static System.Type GetAnimatorControllerWindowType()
        {
            var names = new[] { "UnityEditor.AnimatorControllerWindow", "UnityEditor.AnimatorWindow", "AnimatorControllerWindow", "AnimatorWindow" };
            foreach (var name in names)
            {
                var t = typeof(EditorWindow).Assembly.GetType(name);
                if (t != null) return t;
            }
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var name in names)
                {
                    var t = asm.GetType(name);
                    if (t != null && typeof(EditorWindow).IsAssignableFrom(t)) return t;
                }
            }
            // EditorWindow を継承し "Animator" を含む型を検索（AnimationWindow は除外）
            var editorWindowType = typeof(EditorWindow);
            System.Type fallback = null;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t == editorWindowType || !editorWindowType.IsAssignableFrom(t)) continue;
                        if (t.Name.IndexOf("Animation", System.StringComparison.OrdinalIgnoreCase) >= 0 && t.Name.IndexOf("Animator", System.StringComparison.OrdinalIgnoreCase) < 0)
                            continue; // AnimationWindow を除外
                        if (t.Name.IndexOf("Animator", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (t.Name.IndexOf("Controller", System.StringComparison.OrdinalIgnoreCase) >= 0) return t;
                            fallback = fallback ?? t;
                        }
                    }
                }
                catch { /* 読み込み不可アセンブリをスキップ */ }
            }
            return fallback;
        }

        private static EditorWindow GetAnimatorControllerWindow(System.Type windowType)
        {
            if (windowType == null) return null;
            var windows = (EditorWindow[])Resources.FindObjectsOfTypeAll(windowType);
            return windows.Length > 0 ? windows[0] : null;
        }

        private static void SetAnimatorWindowState(EditorWindow window, AnimatorController controller, int layerIndex, AnimatorState state, AnimatorStateMachine stateMachine, List<AnimatorStateMachine> stateMachinePath)
        {
            if (window == null || controller == null) return;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var t = window.GetType();

            // 1. コントローラーを設定
            SetFieldOrProperty(t, window, "animatorController", "m_AnimatorController", controller, flags);

            // 2. レイヤーを選択
            SetFieldOrProperty(t, window, "selectedLayerIndex", null, layerIndex, flags);

            // 3. パンくずリスト（サブステート侵入用）m_BreadCrumbs - List<BreadCrumbElement>
            if (stateMachinePath != null && stateMachinePath.Count > 0)
            {
                var breadCrumbsField = t.GetField("m_BreadCrumbs", flags);
                if (breadCrumbsField != null && breadCrumbsField.FieldType.IsGenericType)
                {
                    var listType = breadCrumbsField.FieldType;
                    var elemType = listType.GetGenericArguments()[0];
                    var list = System.Activator.CreateInstance(listType) as System.Collections.IList;
                    if (list != null)
                    {
                        if (elemType == typeof(AnimatorStateMachine))
                        {
                            foreach (var sm in stateMachinePath) list.Add(sm);
                            breadCrumbsField.SetValue(window, list);
                        }
                        else if (elemType.Name == "ChildAnimatorStateMachine")
                        {
                            foreach (var sm in stateMachinePath)
                            {
                                var child = CreateChildAnimatorStateMachine(elemType, sm);
                                if (child != null) list.Add(child);
                            }
                            breadCrumbsField.SetValue(window, list);
                        }
                        else if (elemType.Name == "BreadCrumbElement")
                        {
                            var added = 0;
                            foreach (var sm in stateMachinePath)
                            {
                                var elem = CreateBreadCrumbElement(elemType, sm);
                                if (elem != null) { list.Add(elem); added++; }
                            }
                            if (added > 0)
                            {
                                breadCrumbsField.SetValue(window, list);
                                InvokeRefreshBreadcrumbs(window.GetType(), window, stateMachine);
                            }
                        }
                    }
                }
            }

            // 4. ステート選択は呼び出し元で行う（複数選択対応のため）
        }

        private static object CreateChildAnimatorStateMachine(System.Type childType, AnimatorStateMachine stateMachine)
        {
            if (childType == null || stateMachine == null) return null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var obj = System.Activator.CreateInstance(childType);
            var smField = childType.GetField("stateMachine", flags) ?? childType.GetField("m_StateMachine", flags);
            var smProp = childType.GetProperty("stateMachine", flags);
            if (smField != null) smField.SetValue(obj, stateMachine);
            else if (smProp != null) smProp.SetValue(obj, stateMachine);
            return obj;
        }

        private static void InvokeRefreshBreadcrumbs(System.Type windowType, object window, AnimatorStateMachine stateMachine)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var name in new[] { "RefreshBreadcrumbs", "SyncBreadcrumbs", "OnBreadcrumbsChanged", "RebuildBreadcrumbs", "Refresh" })
            {
                var m = windowType.GetMethod(name, flags, null, System.Type.EmptyTypes, null);
                if (m != null) { try { m.Invoke(window, null); } catch { } break; }
            }
            if (stateMachine != null)
            {
                var editorProp = windowType.GetProperty("editor", flags);
                var editor = editorProp?.GetValue(window);
                if (editor != null)
                {
                    var et = editor.GetType();
                    var smProp = et.GetProperty("stateMachine", flags) ?? et.GetProperty("animatorStateMachine", flags);
                    if (smProp != null && smProp.CanWrite)
                    {
                        try { smProp.SetValue(editor, stateMachine); } catch { }
                    }
                    foreach (var name in new[] { "Refresh", "Sync", "OnBreadcrumbsChanged", "Repaint" })
                    {
                        var m = et.GetMethod(name, flags, null, System.Type.EmptyTypes, null);
                        if (m != null) { try { m.Invoke(editor, null); } catch { } break; }
                    }
                }
            }
        }

        private static object CreateBreadCrumbElement(System.Type elemType, AnimatorStateMachine stateMachine)
        {
            if (elemType == null || stateMachine == null) return null;
            const BindingFlags ctorFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var ctor in elemType.GetConstructors(ctorFlags))
            {
                var ps = ctor.GetParameters();
                if (ps.Length == 0) continue;
                if (!ps[0].ParameterType.IsAssignableFrom(typeof(AnimatorStateMachine))) continue;
                var args = new object[ps.Length];
                args[0] = stateMachine;
                var ok = true;
                for (int i = 1; i < ps.Length && ok; i++)
                {
                    var p = ps[i].ParameterType;
                    if (!p.IsValueType) { args[i] = null; continue; }
                    if (p == typeof(int)) { args[i] = 0; continue; }
                    if (p == typeof(float)) { args[i] = 0f; continue; }
                    if (p == typeof(bool)) { args[i] = false; continue; }
                    if (p == typeof(Vector2)) { args[i] = Vector2.zero; continue; }
                    if (p == typeof(Vector3)) { args[i] = Vector3.zero; continue; }
                    if (p == typeof(Rect)) { args[i] = default(Rect); continue; }
                    try { args[i] = System.Activator.CreateInstance(p); }
                    catch { ok = false; }
                }
                if (!ok) continue;
                try { return ctor.Invoke(args); }
                catch { }
            }
            return null;
        }

        private static void SetFieldOrProperty(System.Type t, object target, string propName, string fieldName, object value, BindingFlags flags)
        {
            var prop = t.GetProperty(propName, flags);
            var field = (!string.IsNullOrEmpty(fieldName) ? t.GetField(fieldName, flags) : null) ?? t.GetField(propName, flags);
            if (value == null) return;
            var valueType = value.GetType();
            if (prop != null && (prop.PropertyType.IsAssignableFrom(valueType) || (value is int && prop.PropertyType == typeof(int))))
            {
                prop.SetValue(target, value);
                return;
            }
            if (field != null && (field.FieldType.IsAssignableFrom(valueType) || (value is int && field.FieldType == typeof(int))))
                field.SetValue(target, value);
        }

        #endregion

        #region クリップ・ステート検索

        private struct StateUsageResult
        {
            public AnimatorState state;
            public int layerIndex;
            public AnimatorStateMachine stateMachine;
            public List<AnimatorStateMachine> stateMachinePath;
        }

        /// <summary>pathPrefixで指定したスコープ内でクリップを使用している全ステートを検索。サブステート内の場合はそのサブステート内のみ。</summary>
        private static List<StateUsageResult> FindAllStatesUsingClipInScope(RuntimeAnimatorController controller, AnimationClip targetClip, string pathPrefix)
        {
            var results = new List<StateUsageResult>();
            AnimatorController baseController = null;
            Dictionary<AnimationClip, AnimationClip> overrideMap = null;

            if (controller is AnimatorOverrideController overrideController)
            {
                baseController = overrideController.runtimeAnimatorController as AnimatorController;
                if (baseController == null) return results;
                overrideMap = new Dictionary<AnimationClip, AnimationClip>();
                var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(overrideController.overridesCount);
                overrideController.GetOverrides(overrides);
                foreach (var kv in overrides)
                {
                    if (kv.Value != null)
                        overrideMap[kv.Key] = kv.Value;
                }
            }
            else if (controller is AnimatorController animController)
            {
                baseController = animController;
            }

            if (baseController == null) return results;

            var pathParts = string.IsNullOrEmpty(pathPrefix) ? Array.Empty<string>() : pathPrefix.Split('|');
            var layerName = pathParts.Length > 0 ? pathParts[0] : null;

            for (int i = 0; i < baseController.layers.Length; i++)
            {
                var layer = baseController.layers[i];
                if (!string.IsNullOrEmpty(layerName) && layer.name != layerName)
                    continue;

                AnimatorStateMachine scopeStateMachine;
                if (pathParts.Length <= 1)
                {
                    scopeStateMachine = layer.stateMachine;
                }
                else
                {
                    scopeStateMachine = ResolveStateMachineAtPath(layer.stateMachine, pathParts, 1);
                    if (scopeStateMachine == null) continue;
                }

                CollectStatesUsingClipInStateMachine(scopeStateMachine, targetClip, overrideMap, i, layer.stateMachine, results);
            }
            return results;
        }

        private static AnimatorStateMachine ResolveStateMachineAtPath(AnimatorStateMachine root, string[] pathParts, int startIndex)
        {
            if (root == null || startIndex >= pathParts.Length) return root;
            var targetName = pathParts[startIndex];
            foreach (var child in root.stateMachines)
            {
                if (child.stateMachine == null) continue;
                if (child.stateMachine.name != targetName) continue;
                if (startIndex + 1 >= pathParts.Length)
                    return child.stateMachine;
                return ResolveStateMachineAtPath(child.stateMachine, pathParts, startIndex + 1);
            }
            return null;
        }

        private static void CollectStatesUsingClipInStateMachine(AnimatorStateMachine sm, AnimationClip targetClip, Dictionary<AnimationClip, AnimationClip> overrideMap, int layerIndex, AnimatorStateMachine rootStateMachine, List<StateUsageResult> results)
        {
            if (sm == null) return;

            foreach (var child in sm.states)
            {
                if (child.state == null) continue;
                var motion = child.state.motion;
                if (motion == null) continue;

                var effectiveClip = motion is AnimationClip c
                    ? (overrideMap != null && overrideMap.TryGetValue(c, out var ov) ? ov : c)
                    : null;
                if (effectiveClip == targetClip)
                {
                    results.Add(new StateUsageResult
                    {
                        state = child.state,
                        layerIndex = layerIndex,
                        stateMachine = sm,
                        stateMachinePath = BuildStateMachinePath(rootStateMachine, sm)
                    });
                    continue;
                }

                if (motion is BlendTree blendTree && FindStateUsingClipInBlendTree(blendTree, targetClip, overrideMap))
                {
                    results.Add(new StateUsageResult
                    {
                        state = child.state,
                        layerIndex = layerIndex,
                        stateMachine = sm,
                        stateMachinePath = BuildStateMachinePath(rootStateMachine, sm)
                    });
                }
            }
            foreach (var child in sm.stateMachines)
            {
                if (child.stateMachine == null) continue;
                CollectStatesUsingClipInStateMachine(child.stateMachine, targetClip, overrideMap, layerIndex, rootStateMachine, results);
            }
        }

        private static bool FindFirstStateUsingClip(RuntimeAnimatorController controller, AnimationClip targetClip, out AnimatorState state, out int layerIndex, out AnimatorStateMachine stateMachine, out List<AnimatorStateMachine> stateMachinePath)
        {
            state = null;
            layerIndex = 0;
            stateMachine = null;
            stateMachinePath = null;

            AnimatorController baseController = null;
            Dictionary<AnimationClip, AnimationClip> overrideMap = null;

            if (controller is AnimatorOverrideController overrideController)
            {
                baseController = overrideController.runtimeAnimatorController as AnimatorController;
                if (baseController == null) return false;
                overrideMap = new Dictionary<AnimationClip, AnimationClip>();
                var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(overrideController.overridesCount);
                overrideController.GetOverrides(overrides);
                foreach (var kv in overrides)
                {
                    if (kv.Value != null)
                        overrideMap[kv.Key] = kv.Value;
                }
            }
            else if (controller is AnimatorController animController)
            {
                baseController = animController;
            }

            if (baseController == null) return false;

            for (int i = 0; i < baseController.layers.Length; i++)
            {
                var layer = baseController.layers[i];
                if (FindStateUsingClipInStateMachine(layer.stateMachine, targetClip, overrideMap, out state, out stateMachine))
                {
                    layerIndex = i;
                    stateMachinePath = BuildStateMachinePath(layer.stateMachine, stateMachine);
                    return true;
                }
            }
            return false;
        }

        /// <summary>ルートから対象ステートマシンへのパスを構築（サブステート侵入用）</summary>
        private static List<AnimatorStateMachine> BuildStateMachinePath(AnimatorStateMachine root, AnimatorStateMachine target)
        {
            if (root == null || target == null || root == target)
                return new List<AnimatorStateMachine> { target ?? root };

            var path = new List<AnimatorStateMachine>();
            if (BuildStateMachinePathRecursive(root, target, path))
                return path;
            return new List<AnimatorStateMachine> { target };
        }

        private static bool BuildStateMachinePathRecursive(AnimatorStateMachine current, AnimatorStateMachine target, List<AnimatorStateMachine> path)
        {
            path.Add(current);
            if (current == target) return true;
            foreach (var child in current.stateMachines)
            {
                if (child.stateMachine == null) continue;
                if (BuildStateMachinePathRecursive(child.stateMachine, target, path))
                    return true;
            }
            path.RemoveAt(path.Count - 1);
            return false;
        }

        private static bool FindStateUsingClipInStateMachine(AnimatorStateMachine sm, AnimationClip targetClip, Dictionary<AnimationClip, AnimationClip> overrideMap, out AnimatorState state, out AnimatorStateMachine foundInStateMachine)
        {
            state = null;
            foundInStateMachine = null;
            if (sm == null) return false;

            foreach (var child in sm.states)
            {
                if (child.state == null) continue;
                var motion = child.state.motion;
                if (motion == null) continue;

                var effectiveClip = motion is AnimationClip c
                    ? (overrideMap != null && overrideMap.TryGetValue(c, out var ov) ? ov : c)
                    : null;
                if (effectiveClip == targetClip)
                {
                    state = child.state;
                    foundInStateMachine = sm;
                    return true;
                }

                if (motion is BlendTree blendTree)
                {
                    if (FindStateUsingClipInBlendTree(blendTree, targetClip, overrideMap))
                    {
                        state = child.state;
                        foundInStateMachine = sm;
                        return true;
                    }
                }
            }
            foreach (var child in sm.stateMachines)
            {
                if (child.stateMachine == null) continue;
                if (FindStateUsingClipInStateMachine(child.stateMachine, targetClip, overrideMap, out state, out foundInStateMachine))
                    return true;
            }
            return false;
        }

        private static bool FindStateUsingClipInBlendTree(BlendTree blendTree, AnimationClip targetClip, Dictionary<AnimationClip, AnimationClip> overrideMap)
        {
            if (blendTree == null) return false;
            foreach (var child in blendTree.children)
            {
                if (child.motion is AnimationClip c)
                {
                    var effective = overrideMap != null && overrideMap.TryGetValue(c, out var ov) ? ov : c;
                    if (effective == targetClip) return true;
                }
                else if (child.motion is BlendTree childTree)
                {
                    if (FindStateUsingClipInBlendTree(childTree, targetClip, overrideMap))
                        return true;
                }
            }
            return false;
        }

        #endregion
    }
}
#endif
