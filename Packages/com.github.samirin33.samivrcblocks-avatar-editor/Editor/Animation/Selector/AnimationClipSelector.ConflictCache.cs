#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Samirin33.AvatarEditor.Animation.Editor;

namespace Samirin33.SamirinVRCUtility.AvatarEditor
{
    public partial class AnimationClipSelector
    {
        /// <summary>指定コントローラーの全クリップについて競合キャッシュを構築する。無効化・Undo後の再計算や詳細ウィンドウの更新に利用。</summary>
        internal static Dictionary<int, (bool hasConflict, int pathCount, ConflictEntry[] conflictEntries)> BuildPathConflictCache(RuntimeAnimatorController controller)
        {
            var cache = new Dictionary<int, (bool, int, ConflictEntry[])>();
            if (controller == null) return cache;
            var capturedClips = GetAllAnimationClips(controller);
            try
            {
                AnimatorController baseController = null;
                Dictionary<AnimationClip, AnimationClip> overrideMap = null;

                if (controller is AnimatorOverrideController overrideController)
                {
                    baseController = overrideController.runtimeAnimatorController as AnimatorController;
                    if (baseController != null)
                    {
                        overrideMap = new Dictionary<AnimationClip, AnimationClip>();
                        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(overrideController.overridesCount);
                        overrideController.GetOverrides(overrides);
                        foreach (var kv in overrides)
                        {
                            if (kv.Value != null)
                                overrideMap[kv.Key] = kv.Value;
                        }
                    }
                }
                else if (controller is AnimatorController animController)
                {
                    baseController = animController;
                }

                if (baseController == null) return cache;

                var controllerAssetPath = AssetDatabase.GetAssetPath(baseController);
                var selectorSettings = Settings;

                var clipBindingKeyMap = new Dictionary<AnimationClip, HashSet<string>>();
                foreach (var c in capturedClips)
                {
                    if (c == null) continue;
                    var keys = new HashSet<string>();
                    foreach (var b in AnimationUtility.GetCurveBindings(c))
                    {
                        var typeName = b.type != null ? b.type.Name : "Unknown";
                        keys.Add($"{b.path}|{b.propertyName}|{typeName}");
                    }
                    foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(c))
                    {
                        var typeName = b.type != null ? b.type.Name : "Unknown";
                        keys.Add($"{b.path}|{b.propertyName}|{typeName}");
                    }
                    clipBindingKeyMap[c] = keys;
                }

                var bindingKeyUsage = new Dictionary<string, Dictionary<int, HashSet<AnimationClip>>>();
                AnimationClip ResolveClip(AnimationClip baseClip)
                {
                    if (baseClip == null) return null;
                    if (overrideMap != null && overrideMap.TryGetValue(baseClip, out var ov))
                        return ov;
                    return baseClip;
                }

                for (int i = 0; i < baseController.layers.Length; i++)
                {
                    CollectPathLayerUsage(baseController.layers[i].stateMachine, i, ResolveClip, clipBindingKeyMap, bindingKeyUsage);
                }

                foreach (var kv in clipBindingKeyMap)
                {
                    var clip = kv.Key;
                    var keys = kv.Value;
                    var conflictCount = 0;
                    var conflictPathList = new List<ConflictEntry>();

                    foreach (var key in keys)
                    {
                        if (string.IsNullOrEmpty(key)) continue;
                        if (!bindingKeyUsage.TryGetValue(key, out var perLayer) || perLayer == null)
                            continue;

                        var selfLayers = new HashSet<int>();
                        foreach (var kvLayer in perLayer)
                        {
                            if (kvLayer.Value != null && kvLayer.Value.Contains(clip))
                                selfLayers.Add(kvLayer.Key);
                        }

                        var otherLayerEntries = perLayer
                            .Where(p => !selfLayers.Contains(p.Key) && p.Value != null && p.Value.Count > 0)
                            .ToList();
                        if (otherLayerEntries.Count == 0) continue;

                        var parts = key.Split('|');
                        var path = parts.Length > 0 ? parts[0] : "";
                        var property = parts.Length > 1 ? parts[1] : "";
                        var typeName = parts.Length > 2 ? parts[2] : "";
                        if (selectorSettings != null && selectorSettings.IsIgnoredConflict(controllerAssetPath, path, property, typeName))
                            continue;

                        conflictCount++;
                        var lastObjectName = string.IsNullOrEmpty(path) ? "" : path.Split('/').LastOrDefault() ?? path;

                        var entry = new ConflictEntry
                        {
                            Path = path,
                            LastObjectName = lastObjectName,
                            PropertyName = property,
                            TypeName = typeName
                        };
                        foreach (var layerIndex in selfLayers)
                        {
                            if (!perLayer.TryGetValue(layerIndex, out var selfClipsInLayer) || selfClipsInLayer == null || selfClipsInLayer.Count == 0)
                                continue;
                            var selfLayerName = (layerIndex >= 0 && layerIndex < baseController.layers.Length)
                                ? baseController.layers[layerIndex].name
                                : $"Layer {layerIndex}";
                            entry.SelfTargets.Add(new ConflictTargetInfo
                            {
                                LayerName = selfLayerName,
                                Clips = selfClipsInLayer.Where(c => c != null).ToList()
                            });
                        }
                        foreach (var kvLayer in otherLayerEntries)
                        {
                            var layerIndex = kvLayer.Key;
                            var clipsInLayer = kvLayer.Value;
                            var layerName = (layerIndex >= 0 && layerIndex < baseController.layers.Length)
                                ? baseController.layers[layerIndex].name
                                : $"Layer {layerIndex}";
                            entry.Targets.Add(new ConflictTargetInfo
                            {
                                LayerName = layerName,
                                Clips = clipsInLayer?.Where(c => c != null).ToList() ?? new List<AnimationClip>()
                            });
                        }
                        conflictPathList.Add(entry);
                    }

                    cache[clip.GetInstanceID()] = (conflictCount > 0, conflictCount, conflictPathList.ToArray());
                }
            }
            catch { /* 例外時は空キャッシュのまま */ }

            return cache;
        }

        /// <summary>キャッシュを無効化し、次回描画時に再計算させる。コンフリクト解消ボタン押下時などに呼ぶ。</summary>
        internal static void InvalidatePathConflictCache()
        {
            _pathConflictCacheInvalidated = true;
            // GetWindow は閉じているウィンドウを新しく開いてしまうため、開いているものだけ再描画する
            if (!HasOpenInstances<AnimationClipSelector>()) return;
            foreach (var w in Resources.FindObjectsOfTypeAll<AnimationClipSelector>())
            {
                if (w != null) w.Repaint();
            }
        }

        /// <summary>
        /// 指定ルートの Controller について、パス＋属性の競合警告を出さないようにする。
        /// 同じ Controller 上のほかの Clip でも、そのパス＋属性は警告対象外になる。
        /// </summary>
        internal static bool IgnoreConflict(GameObject root, string path, string propertyName, string typeName)
        {
            var controllerPath = GetControllerPath(ResolveControllerForRoot(root));
            if (string.IsNullOrEmpty(controllerPath)) return false;

            var settings = Settings;
            if (settings == null) return false;
            if (settings.IsIgnoredConflict(controllerPath, path, propertyName, typeName))
                return false;

            Undo.RecordObject(settings, "このコンフリクトを無視");
            if (!settings.IgnoreConflict(controllerPath, path, propertyName, typeName))
                return false;

            AnimationClipSelectorStateManager.RequestSave();
            InvalidatePathConflictCache();
            return true;
        }

        /// <summary>開いている競合詳細ウィンドウの一覧を、現在の無視設定で作り直す。</summary>
        internal static void RefreshOpenConflictDetailsWindows()
        {
            var windows = Resources.FindObjectsOfTypeAll<ClipConflictDetailsWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] == null) continue;
                windows[i].RefreshConflictEntriesFromOutside();
            }
        }

        /// <summary>Selector の表示を更新する。Missing バインディング操作後などに呼ぶ。</summary>
        internal static void InvalidateAndRepaint()
        {
            _clipListCacheInvalidated = true;
            InvalidatePathConflictCache();
            RefreshOpenConflictDetailsWindows();
            RefreshOpenMissingBindingWindows();
        }

        /// <summary>開いている Missing 詳細ウィンドウの一覧を、現在のバインドパスで作り直す。</summary>
        internal static void RefreshOpenMissingBindingWindows()
        {
            var windows = Resources.FindObjectsOfTypeAll<ClipMissingBindingDetailsWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] == null) continue;
                windows[i].RefreshMissingPathsFromOutside();
            }
        }

        /// <summary>指定クリップの競合エントリ一覧を取得。root から Controller を解決してキャッシュを構築する。Undo 後の詳細ウィンドウ更新用。</summary>
        internal static ConflictEntry[] GetConflictEntriesForClip(AnimationClip clip, GameObject root)
        {
            if (clip == null || root == null) return Array.Empty<ConflictEntry>();
            var controller = ResolveControllerForClip(root, clip);
            if (controller == null) return Array.Empty<ConflictEntry>();
            var cache = BuildPathConflictCache(controller);
            return cache.TryGetValue(clip.GetInstanceID(), out var v) ? v.conflictEntries : Array.Empty<ConflictEntry>();
        }

        /// <summary>
        /// ステートマシン全体を走査し、「Transformパス＋属性キー」ごとに
        /// どのレイヤーのどのクリップがそのキーを制御しているかを収集する。
        /// </summary>
        private static void CollectPathLayerUsage(
            AnimatorStateMachine sm,
            int layerIndex,
            System.Func<AnimationClip, AnimationClip> resolveClip,
            Dictionary<AnimationClip, HashSet<string>> clipBindingKeyMap,
            Dictionary<string, Dictionary<int, HashSet<AnimationClip>>> bindingKeyUsage)
        {
            if (sm == null) return;

            foreach (var child in sm.states)
            {
                if (child.state?.motion == null) continue;

                var clips = new HashSet<AnimationClip>();
                CollectClipsFromMotion(child.state.motion, clips, resolveClip);

                foreach (var clip in clips)
                {
                    if (clip == null) continue;
                    if (!clipBindingKeyMap.TryGetValue(clip, out var keys) || keys == null) continue;

                    foreach (var key in keys)
                    {
                        if (string.IsNullOrEmpty(key)) continue;

                        if (!bindingKeyUsage.TryGetValue(key, out var perLayer))
                        {
                            perLayer = new Dictionary<int, HashSet<AnimationClip>>();
                            bindingKeyUsage[key] = perLayer;
                        }

                        if (!perLayer.TryGetValue(layerIndex, out var clipsInLayer))
                        {
                            clipsInLayer = new HashSet<AnimationClip>();
                            perLayer[layerIndex] = clipsInLayer;
                        }

                        clipsInLayer.Add(clip);
                    }
                }
            }

            foreach (var child in sm.stateMachines)
            {
                if (child.stateMachine == null) continue;
                CollectPathLayerUsage(child.stateMachine, layerIndex, resolveClip, clipBindingKeyMap, bindingKeyUsage);
            }
        }
    }
}
#endif
