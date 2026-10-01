using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    public struct AnimationClipPathReplaceRule
    {
        public string from;
        public string to;
    }

    /// <summary>
    /// AnimationClip のバインドパス検出・置換・削除の共通処理。
    /// </summary>
    public static class AnimationClipBindingPathUtility
    {
        public static IReadOnlyList<string> GetMissingBindingPaths(Transform root, AnimationClip clip)
        {
            var missingPaths = new HashSet<string>();
            if (root == null || clip == null)
                return missingPaths.ToList();

            foreach (var path in CollectBindingPaths(clip))
            {
                if (string.IsNullOrEmpty(path))
                    continue;
                if (root.Find(path) == null)
                    missingPaths.Add(path);
            }

            return missingPaths.OrderBy(p => p).ToList();
        }

        public static int GetMissingBindingPathCount(Transform root, AnimationClip clip)
        {
            return GetMissingBindingPaths(root, clip).Count;
        }

        public static bool ClipHasBindingAtPath(AnimationClip clip, string path)
        {
            if (clip == null || path == null)
                return false;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path == path)
                    return true;
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (binding.path == path)
                    return true;
            }

            return false;
        }

        public static List<AnimationClip> GetClipsWithMissingPath(Transform root, IEnumerable<AnimationClip> scopeClips, string path)
        {
            var result = new List<AnimationClip>();
            if (root == null || string.IsNullOrEmpty(path) || scopeClips == null)
                return result;

            if (root.Find(path) != null)
                return result;

            foreach (var clip in scopeClips)
            {
                if (clip == null || result.Contains(clip))
                    continue;
                if (ClipHasBindingAtPath(clip, path))
                    result.Add(clip);
            }

            return result;
        }

        public static int RemoveBindingsAtPathFromClips(IEnumerable<AnimationClip> clips, string path)
        {
            if (string.IsNullOrEmpty(path))
                return 0;

            var clipArray = clips?.Where(c => c != null).Distinct().ToArray() ?? System.Array.Empty<AnimationClip>();
            if (clipArray.Length == 0)
                return 0;

            Undo.RegisterCompleteObjectUndo(clipArray, "Remove Binding Path");
            var removed = 0;
            foreach (var clip in clipArray)
            {
                var count = RemoveBindingsAtPathWithoutUndo(clip, path);
                if (count > 0)
                    EditorUtility.SetDirty(clip);
                removed += count;
            }

            if (removed > 0)
                AssetDatabase.SaveAssets();

            return removed;
        }

        public static int RemoveMissingBindingsFromClips(Transform root, IEnumerable<AnimationClip> clips, IEnumerable<string> paths = null)
        {
            if (root == null || clips == null)
                return 0;

            var clipList = clips.Where(c => c != null).Distinct().ToList();
            if (clipList.Count == 0)
                return 0;

            var removed = 0;
            if (paths != null)
            {
                var pathList = paths.ToList();
                Undo.RegisterCompleteObjectUndo(clipList.ToArray(), "Remove Missing Bindings");
                foreach (var clip in clipList)
                {
                    var clipRemoved = 0;
                    foreach (var path in pathList)
                        clipRemoved += RemoveBindingsAtPathWithoutUndo(clip, path);
                    if (clipRemoved > 0)
                        EditorUtility.SetDirty(clip);
                    removed += clipRemoved;
                }
            }
            else
            {
                foreach (var clip in clipList)
                    removed += RemoveMissingBindings(root, clip);
            }

            if (removed > 0)
                AssetDatabase.SaveAssets();

            return removed;
        }

        public static int RemoveBindingsAtPath(AnimationClip clip, string path)
        {
            if (clip == null || path == null)
                return 0;

            Undo.RecordObject(clip, "Remove Binding Path");
            var removed = 0;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path != path)
                    continue;
                AnimationUtility.SetEditorCurve(clip, binding, null);
                removed++;
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (binding.path != path)
                    continue;
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                removed++;
            }

            return removed;
        }

        public static int RemoveMissingBindings(Transform root, AnimationClip clip, IEnumerable<string> paths = null)
        {
            if (clip == null || root == null)
                return 0;

            var targetPaths = paths?.ToList() ?? GetMissingBindingPaths(root, clip).ToList();
            if (targetPaths.Count == 0)
                return 0;

            Undo.RecordObject(clip, "Remove Missing Bindings");
            var removed = 0;
            foreach (var path in targetPaths)
                removed += RemoveBindingsAtPathWithoutUndo(clip, path);

            if (removed > 0)
                EditorUtility.SetDirty(clip);

            return removed;
        }

        public static int ReplacePathInClip(AnimationClip clip, string from, string to)
        {
            if (clip == null || string.IsNullOrEmpty(from))
                return 0;

            return ReplacePathsInClip(clip, new List<AnimationClipPathReplaceRule>
            {
                new AnimationClipPathReplaceRule { from = from, to = to ?? "" }
            });
        }

        public static int ReplacePathInClips(IEnumerable<AnimationClip> clips, string from, string to)
        {
            if (string.IsNullOrEmpty(from))
                return 0;

            var clipArray = clips?.Where(c => c != null).ToArray() ?? System.Array.Empty<AnimationClip>();
            if (clipArray.Length == 0)
                return 0;

            Undo.RegisterCompleteObjectUndo(clipArray, "Animation Clip Binding Path Replace");
            var replacedCount = 0;
            foreach (var clip in clipArray)
            {
                replacedCount += ReplacePathInClipWithoutUndo(clip, from, to);
                EditorUtility.SetDirty(clip);
            }

            if (replacedCount > 0)
                AssetDatabase.SaveAssets();

            return replacedCount;
        }

        /// <summary>
        /// バインドパスを完全一致で置換する。ヒエラルキー変更の自動解決用。Undo と保存は呼び出し側が行う。
        /// </summary>
        public static int ReplaceExactPathsInClips(IEnumerable<AnimationClip> clips, IReadOnlyDictionary<string, string> exactPaths)
        {
            if (exactPaths == null || exactPaths.Count == 0)
                return 0;

            var clipArray = clips?.Where(c => c != null).Distinct().ToArray() ?? System.Array.Empty<AnimationClip>();
            if (clipArray.Length == 0)
                return 0;

            var replacedCount = 0;
            foreach (var clip in clipArray)
            {
                var count = ReplaceExactPathsInClipWithoutUndo(clip, exactPaths);
                if (count > 0)
                    EditorUtility.SetDirty(clip);
                replacedCount += count;
            }

            return replacedCount;
        }

        public static int ReplacePathsInClip(AnimationClip clip, IList<AnimationClipPathReplaceRule> rules)
        {
            if (clip == null || rules == null || rules.Count == 0)
                return 0;

            Undo.RecordObject(clip, "Animation Clip Binding Path Replace");
            var count = ReplacePathsInClipWithoutUndo(clip, rules);
            if (count > 0)
                EditorUtility.SetDirty(clip);
            return count;
        }

        public static string GetPathFromAnimatorRoot(Transform transform)
        {
            if (transform == null)
                return null;

            Transform current = transform;
            Transform animatorRoot = null;

            while (current != null)
            {
                if (current.GetComponent<Animator>() != null)
                {
                    animatorRoot = current;
                    break;
                }
                current = current.parent;
            }

            if (animatorRoot == null)
                return null;

            return GetTransformPath(transform, animatorRoot);
        }

        public static Transform TryResolveTransformFromPath(string path, Transform preferredRoot = null)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            if (preferredRoot != null)
            {
                var fromPreferred = FindChildByPath(preferredRoot, path);
                if (fromPreferred != null)
                    return fromPreferred;
            }

            foreach (var animator in Resources.FindObjectsOfTypeAll<Animator>())
            {
                if (animator == null)
                    continue;

                var found = FindChildByPath(animator.transform, path);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static IEnumerable<string> CollectBindingPaths(AnimationClip clip)
        {
            var paths = new HashSet<string>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                paths.Add(binding.path);
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                paths.Add(binding.path);
            return paths;
        }

        private static int RemoveBindingsAtPathWithoutUndo(AnimationClip clip, string path)
        {
            var removed = 0;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path != path)
                    continue;
                AnimationUtility.SetEditorCurve(clip, binding, null);
                removed++;
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (binding.path != path)
                    continue;
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                removed++;
            }

            return removed;
        }

        private static int ReplaceExactPathsInClipWithoutUndo(AnimationClip clip, IReadOnlyDictionary<string, string> exactPaths)
        {
            var count = 0;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (string.IsNullOrEmpty(binding.path) || !exactPaths.TryGetValue(binding.path, out var newPath))
                    continue;
                if (newPath == binding.path)
                    continue;

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                AnimationUtility.SetEditorCurve(clip, binding, null);
                var newBinding = binding;
                newBinding.path = newPath ?? "";
                AnimationUtility.SetEditorCurve(clip, newBinding, curve);
                count++;
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (string.IsNullOrEmpty(binding.path) || !exactPaths.TryGetValue(binding.path, out var newPath))
                    continue;
                if (newPath == binding.path)
                    continue;

                var keyframes = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                var newBinding = binding;
                newBinding.path = newPath ?? "";
                AnimationUtility.SetObjectReferenceCurve(clip, newBinding, keyframes);
                count++;
            }

            return count;
        }

        private static int ReplacePathInClipWithoutUndo(AnimationClip clip, string from, string to)
        {
            return ReplacePathsInClipWithoutUndo(clip, new List<AnimationClipPathReplaceRule>
            {
                new AnimationClipPathReplaceRule { from = from, to = to ?? "" }
            });
        }

        private static int ReplacePathsInClipWithoutUndo(AnimationClip clip, IList<AnimationClipPathReplaceRule> rules)
        {
            var count = 0;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var newPath = ApplyRules(binding.path, rules);
                if (newPath == binding.path)
                    continue;

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                AnimationUtility.SetEditorCurve(clip, binding, null);
                var newBinding = binding;
                newBinding.path = newPath;
                AnimationUtility.SetEditorCurve(clip, newBinding, curve);
                count++;
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                var newPath = ApplyRules(binding.path, rules);
                if (newPath == binding.path)
                    continue;

                var keyframes = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                var newBinding = binding;
                newBinding.path = newPath;
                AnimationUtility.SetObjectReferenceCurve(clip, newBinding, keyframes);
                count++;
            }

            return count;
        }

        private static string ApplyRules(string path, IList<AnimationClipPathReplaceRule> rules)
        {
            var result = path;
            foreach (var rule in rules)
            {
                if (!string.IsNullOrEmpty(rule.from))
                    result = result.Replace(rule.from, rule.to ?? "");
            }

            return result;
        }

        private static Transform FindChildByPath(Transform root, string path)
        {
            if (root == null)
                return null;

            if (string.IsNullOrEmpty(path))
                return root;

            var current = root;
            foreach (var part in path.Split('/'))
            {
                if (string.IsNullOrEmpty(part))
                    continue;

                Transform child = null;
                for (int i = 0; i < current.childCount; i++)
                {
                    var candidate = current.GetChild(i);
                    if (candidate.name == part)
                    {
                        child = candidate;
                        break;
                    }
                }

                if (child == null)
                    return null;

                current = child;
            }

            return current;
        }

        private static string GetTransformPath(Transform transform, Transform pathRoot = null)
        {
            if (transform == null)
                return null;

            var parts = new List<string>();
            var t = transform;
            if (pathRoot != null)
            {
                while (t != null && t != pathRoot)
                {
                    parts.Add(t.name);
                    t = t.parent;
                }

                if (t != pathRoot)
                    return null;

                parts.Reverse();
            }
            else
            {
                while (t != null)
                {
                    parts.Add(t.name);
                    t = t.parent;
                }

                parts.Reverse();
            }

            return parts.Count > 0 ? string.Join("/", parts) : "";
        }

        private const string PrefsKeyHierarchyWatch = "SamirinEditorTools.AnimationClipBindingPathReplace.HierarchyWatch";
        private const string PrefsKeyHierarchyWatchClips = "SamirinEditorTools.AnimationClipBindingPathReplace.HierarchyWatchClips";

        public static bool HierarchyWatchEnabled
        {
            get => EditorPrefs.GetBool(PrefsKeyHierarchyWatch, false);
            set
            {
                EditorPrefs.SetBool(PrefsKeyHierarchyWatch, value);
                HierarchyPathWatcher.NotifySettingsChanged();
            }
        }

        public static Transform WatchedAnimatorRoot => HierarchyPathWatcher.WatchedRoot;

        /// <summary>
        /// Missing パスについて、以前記録した Transform の GlobalObjectId から現在のパスを解決して置換する。
        /// </summary>
        public static int ReplaceMissingBindingsByRememberedObject(Transform root, IEnumerable<AnimationClip> clips)
        {
            return HierarchyPathWatcher.ResolveRememberedMissingPaths(root, clips);
        }

        /// <summary>
        /// ヒエラルキー監視の有効状態と、パスを書き換える Clip を設定する。
        /// 監視ルートは、その時点で選択中のオブジェクトの親 Animator。
        /// clips が null のときは、保存済みの Clip 一覧を維持する。
        /// </summary>
        public static void ConfigureHierarchyWatch(bool enabled, IEnumerable<AnimationClip> clips)
        {
            EditorPrefs.SetBool(PrefsKeyHierarchyWatch, enabled);
            if (clips != null)
                EditorPrefs.SetString(PrefsKeyHierarchyWatchClips, SerializeClips(clips));
            HierarchyPathWatcher.NotifySettingsChanged();
        }

        private static string SerializeClips(IEnumerable<AnimationClip> clips)
        {
            var ids = clips
                .Where(clip => clip != null)
                .Select(clip =>
                {
                    try
                    {
                        return GlobalObjectId.GetGlobalObjectIdSlow(clip).ToString();
                    }
                    catch
                    {
                        return "";
                    }
                })
                .Where(id => !string.IsNullOrEmpty(id));
            return string.Join("|", ids);
        }

        private static AnimationClip[] LoadWatchClips()
        {
            var stored = EditorPrefs.GetString(PrefsKeyHierarchyWatchClips, "");
            if (string.IsNullOrEmpty(stored))
                return System.Array.Empty<AnimationClip>();

            var clips = new List<AnimationClip>();
            foreach (var idString in stored.Split('|'))
            {
                if (!GlobalObjectId.TryParse(idString, out var globalObjectId))
                    continue;
                if (GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalObjectId) is AnimationClip clip && !clips.Contains(clip))
                    clips.Add(clip);
            }

            return clips.ToArray();
        }

        // 入れ子クラスの [InitializeOnLoad] は起動時に実行されないことがあるため、トップレベルから静的コンストラクタを起動する
        [InitializeOnLoadMethod]
        private static void InitializeHierarchyWatcher()
        {
            HierarchyPathWatcher.EnsureInitialized();
        }

        private static class HierarchyPathWatcher
        {
            private static bool _queued;
            private static bool _resolving;
            private static bool _skipNextResolve;
            private static int _snapshotRootId;
            private static Dictionary<int, string> _pathSnapshot;
            private static Dictionary<string, GlobalObjectId> _objectIdsByPath;

            internal static Transform WatchedRoot { get; private set; }

            static HierarchyPathWatcher()
            {
                EditorApplication.hierarchyChanged += OnHierarchyChanged;
                Selection.selectionChanged += OnSelectionChanged;
                Undo.undoRedoPerformed += OnUndoRedoPerformed;
                Undo.postprocessModifications += OnUndoPropertyModifications;
#if UNITY_2022_2_OR_NEWER
                ObjectChangeEvents.changesPublished += OnChangesPublished;
#endif
                EditorApplication.delayCall += CaptureSnapshot;
            }

            internal static void EnsureInitialized()
            {
            }

            internal static void NotifySettingsChanged()
            {
                _pathSnapshot = null;
                _objectIdsByPath = null;
                _snapshotRootId = 0;
                WatchedRoot = null;
                CaptureSnapshot();
            }

            private static void OnUndoRedoPerformed()
            {
                _skipNextResolve = true;
                _pathSnapshot = null;
                _objectIdsByPath = null;
                _snapshotRootId = 0;
                EditorApplication.delayCall += () =>
                {
                    CaptureSnapshot();
                    _skipNextResolve = false;
                };
            }

            private static UndoPropertyModification[] OnUndoPropertyModifications(UndoPropertyModification[] modifications)
            {
                if (modifications == null)
                    return modifications;

                for (int i = 0; i < modifications.Length; i++)
                {
                    var property = modifications[i].currentValue;
                    if (property.target == null || string.IsNullOrEmpty(property.propertyPath))
                        continue;

                    var isRename = property.propertyPath == "m_Name" && property.target is GameObject;
                    var isReparent = property.target is Transform
                        && (property.propertyPath == "m_Father" || property.propertyPath == "m_RootOrder");
                    if (!isRename && !isReparent)
                        continue;

                    QueueResolve();
                    break;
                }

                return modifications;
            }

            private static void OnHierarchyChanged()
            {
                QueueResolve();
            }

            private static void OnSelectionChanged()
            {
                var animatorRoot = ResolveAnimatorRootFromSelection();
                var rootId = animatorRoot != null ? animatorRoot.GetInstanceID() : 0;
                if (rootId != _snapshotRootId)
                    CaptureSnapshot();
                RepaintReplaceWindows();
            }

#if UNITY_2022_2_OR_NEWER
            private static void OnChangesPublished(ref ObjectChangeEventStream stream)
            {
                for (int i = 0; i < stream.length; i++)
                {
                    var kind = stream.GetEventType(i);
                    if (kind == ObjectChangeKind.ChangeGameObjectParent || kind == ObjectChangeKind.ChangeGameObjectStructure)
                    {
                        QueueResolve();
                        return;
                    }

                    if (kind != ObjectChangeKind.ChangeGameObjectOrComponentProperties)
                        continue;

                    stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var args);
                    var changed = EditorUtility.InstanceIDToObject(args.instanceId);
                    if (changed is GameObject || changed == null)
                    {
                        QueueResolve();
                        return;
                    }
                }
            }
#endif

            private static void QueueResolve()
            {
                if (!HierarchyWatchEnabled || _resolving || _queued)
                    return;
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    _pathSnapshot = null;
                    _objectIdsByPath = null;
                    _snapshotRootId = 0;
                    return;
                }

                _queued = true;
                EditorApplication.delayCall += () =>
                {
                    _queued = false;
                    var replaced = ResolvePaths();
                    if (replaced > 0)
                        Samirin33.SamirinVRCUtility.AvatarEditor.AnimationClipSelector.InvalidateAndRepaint();

                    RepaintReplaceWindows();
                };
            }

            private static void RepaintReplaceWindows()
            {
                var windows = Resources.FindObjectsOfTypeAll<AnimationClipBindingPathReplaceEditor>();
                for (int i = 0; i < windows.Length; i++)
                {
                    if (windows[i] != null)
                        windows[i].Repaint();
                }
            }

            private static void CaptureSnapshot()
            {
                if (!HierarchyWatchEnabled)
                {
                    WatchedRoot = null;
                    _pathSnapshot = null;
                    _objectIdsByPath = null;
                    _snapshotRootId = 0;
                    return;
                }

                var animatorRoot = ResolveAnimatorRootFromSelection();
                WatchedRoot = animatorRoot;
                if (animatorRoot == null)
                {
                    _pathSnapshot = null;
                    _objectIdsByPath = null;
                    _snapshotRootId = 0;
                    return;
                }

                _snapshotRootId = animatorRoot.GetInstanceID();
                _pathSnapshot = CapturePaths(animatorRoot);
                _objectIdsByPath = CaptureObjectIds(animatorRoot);
            }

            private static int ResolvePaths()
            {
                if (_skipNextResolve || _resolving || !HierarchyWatchEnabled)
                    return 0;

                var animatorRoot = ResolveAnimatorRootFromSelection();
                WatchedRoot = animatorRoot;
                if (animatorRoot == null)
                {
                    _pathSnapshot = null;
                    _objectIdsByPath = null;
                    _snapshotRootId = 0;
                    return 0;
                }

                var rootId = animatorRoot.GetInstanceID();
                var current = CapturePaths(animatorRoot);
                if (_pathSnapshot == null || _snapshotRootId != rootId)
                {
                    _snapshotRootId = rootId;
                    _pathSnapshot = current;
                    _objectIdsByPath = CaptureObjectIds(animatorRoot);
                    return 0;
                }

                var rules = BuildPathRules(_pathSnapshot, current);
                AddRulesForMissingObjectIds(rules, _objectIdsByPath, animatorRoot);
                _snapshotRootId = rootId;
                _pathSnapshot = current;
                _objectIdsByPath = CaptureObjectIds(animatorRoot);
                if (rules.Count == 0)
                    return 0;

                var clips = CollectTargetClips(animatorRoot, rules.Keys);
                if (clips.Length == 0)
                    return 0;

                var group = Undo.GetCurrentGroup();
                _resolving = true;
                try
                {
                    Undo.RegisterCompleteObjectUndo(clips, "Resolve Animation Binding Paths");
                    var replaced = ReplaceExactPathsInClips(clips, rules);
                    if (replaced > 0)
                    {
                        Undo.CollapseUndoOperations(group);
                        Debug.Log($"[AnimationClipBindingPathReplace] ヒエラルキー変更に合わせてバインドパスを {replaced} 件更新しました。");
                    }

                    return replaced;
                }
                finally
                {
                    _resolving = false;
                }
            }

            internal static int ResolveRememberedMissingPaths(Transform root, IEnumerable<AnimationClip> clips)
            {
                if (!HierarchyWatchEnabled || root == null || _objectIdsByPath == null || _objectIdsByPath.Count == 0)
                    return 0;

                var animatorRoot = ResolveAnimatorRoot(root);
                if (animatorRoot == null)
                    return 0;

                var rules = new Dictionary<string, string>();
                AddRulesForMissingObjectIds(rules, _objectIdsByPath, animatorRoot);
                if (rules.Count == 0)
                    return 0;

                var targets = new HashSet<AnimationClip>();
                if (clips != null)
                {
                    foreach (var clip in clips)
                    {
                        if (IsEditableClip(clip))
                            targets.Add(clip);
                    }
                }

                foreach (var clip in CollectTargetClips(animatorRoot, rules.Keys))
                    targets.Add(clip);

                if (targets.Count == 0)
                    return 0;

                return ReplaceExactPathsInClips(targets, rules);
            }

            private static Transform ResolveAnimatorRoot(Transform root)
            {
                if (root == null)
                    return null;

                var animator = root.GetComponentInParent<Animator>();
                return animator != null ? animator.transform : null;
            }

            private static AnimationClip[] CollectTargetClips(Transform animatorRoot, IEnumerable<string> oldPaths)
            {
                var clips = new HashSet<AnimationClip>();
                foreach (var clip in LoadWatchClips())
                {
                    if (IsEditableClip(clip))
                        clips.Add(clip);
                }

                var animator = animatorRoot != null ? animatorRoot.GetComponent<Animator>() : null;
                var controller = animator != null ? animator.runtimeAnimatorController : null;
                if (controller != null)
                {
                    var controllerClips = controller.animationClips;
                    for (int i = 0; i < controllerClips.Length; i++)
                    {
                        if (IsEditableClip(controllerClips[i]))
                            clips.Add(controllerClips[i]);
                    }
                }

                var pathList = oldPaths != null ? new HashSet<string>(oldPaths) : null;
                if (pathList != null && pathList.Count > 0)
                {
                    var loaded = Resources.FindObjectsOfTypeAll<AnimationClip>();
                    for (int i = 0; i < loaded.Length; i++)
                    {
                        var clip = loaded[i];
                        if (!IsEditableClip(clip) || clips.Contains(clip))
                            continue;
                        if (ClipContainsAnyPath(clip, pathList))
                            clips.Add(clip);
                    }
                }

                return clips.ToArray();
            }

            private static bool IsEditableClip(AnimationClip clip)
            {
                return clip != null && AssetDatabase.Contains(clip);
            }

            private static bool ClipContainsAnyPath(AnimationClip clip, HashSet<string> paths)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (paths.Contains(binding.path))
                        return true;
                }

                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    if (paths.Contains(binding.path))
                        return true;
                }

                return false;
            }

            private static Dictionary<string, GlobalObjectId> CaptureObjectIds(Transform root)
            {
                var map = new Dictionary<string, GlobalObjectId>();
                if (root == null)
                    return map;

                var transforms = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    var transform = transforms[i];
                    if (transform == null)
                        continue;

                    var path = GetPathFromAnimatorRoot(transform);
                    if (string.IsNullOrEmpty(path))
                        continue;

                    map[path] = GlobalObjectId.GetGlobalObjectIdSlow(transform);
                }

                return map;
            }

            private static void AddRulesForMissingObjectIds(
                Dictionary<string, string> rules,
                Dictionary<string, GlobalObjectId> objectIdsByPath,
                Transform animatorRoot)
            {
                if (rules == null || objectIdsByPath == null || animatorRoot == null)
                    return;

                foreach (var pair in objectIdsByPath)
                {
                    if (string.IsNullOrEmpty(pair.Key) || rules.ContainsKey(pair.Key))
                        continue;
                    if (animatorRoot.Find(pair.Key) != null)
                        continue;

                    var transform = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(pair.Value) as Transform;
                    if (transform == null)
                        continue;
                    if (transform != animatorRoot && !transform.IsChildOf(animatorRoot))
                        continue;

                    var newPath = GetPathFromAnimatorRoot(transform);
                    if (string.IsNullOrEmpty(newPath) || newPath == pair.Key)
                        continue;

                    rules[pair.Key] = newPath;
                }
            }

            private static Transform ResolveAnimatorRootFromSelection()
            {
                var selected = Selection.activeGameObject;
                if (selected == null)
                    return null;

                var animator = selected.GetComponentInParent<Animator>();
                return animator != null ? animator.transform : null;
            }

            private static Dictionary<int, string> CapturePaths(Transform root)
            {
                var map = new Dictionary<int, string>();
                if (root == null)
                    return map;

                var transforms = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    var transform = transforms[i];
                    if (transform == null)
                        continue;

                    var path = GetPathFromAnimatorRoot(transform);
                    if (path == null)
                        continue;

                    map[transform.GetInstanceID()] = path;
                }

                return map;
            }

            private static Dictionary<string, string> BuildPathRules(
                Dictionary<int, string> previous,
                Dictionary<int, string> current)
            {
                var changes = new List<(string oldPath, string newPath)>();
                foreach (var pair in previous)
                {
                    if (string.IsNullOrEmpty(pair.Value))
                        continue;
                    if (!current.TryGetValue(pair.Key, out var newPath))
                        continue;
                    if (newPath == pair.Value)
                        continue;

                    changes.Add((pair.Value, newPath ?? ""));
                }

                var rules = new Dictionary<string, string>();
                foreach (var group in changes.GroupBy(change => change.oldPath))
                {
                    var newPaths = group.Select(change => change.newPath).Distinct().ToList();
                    if (newPaths.Count != 1)
                        continue;
                    if (newPaths[0] == group.Key)
                        continue;

                    rules[group.Key] = newPaths[0];
                }

                return rules;
            }
        }
    }
}
