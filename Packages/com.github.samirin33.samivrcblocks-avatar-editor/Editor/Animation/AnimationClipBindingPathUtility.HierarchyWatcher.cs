using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    public static partial class AnimationClipBindingPathUtility
    {
        private const string PrefsKeyHierarchyWatch = "SamirinEditorTools.AnimationClipBindingPathReplace.HierarchyWatch";
        private const string PrefsKeyHierarchyWatchClips = "SamirinEditorTools.AnimationClipBindingPathReplace.HierarchyWatchClips";

        private static bool? _hierarchyWatchEnabledCache;

        /// <summary>ヒエラルキー監視の有効状態。プロジェクトごとに保存する。</summary>
        public static bool HierarchyWatchEnabled
        {
            get
            {
                if (!_hierarchyWatchEnabledCache.HasValue)
                    _hierarchyWatchEnabledCache = ProjectScopedEditorSettings.GetBool(PrefsKeyHierarchyWatch, false);
                return _hierarchyWatchEnabledCache.Value;
            }
            set
            {
                SetHierarchyWatchEnabled(value);
                HierarchyPathWatcher.NotifySettingsChanged();
            }
        }

        public static Transform WatchedAnimatorRoot => HierarchyPathWatcher.WatchedRoot;

        /// <summary>
        /// Missing パスについて、以前記録した Transform の GlobalObjectId から現在のパスを解決して置換する（Undo 可）。
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
            SetHierarchyWatchEnabled(enabled);
            if (clips != null)
                ProjectScopedEditorSettings.SetString(PrefsKeyHierarchyWatchClips, SerializeClips(clips));
            HierarchyPathWatcher.NotifySettingsChanged();
        }

        private static void SetHierarchyWatchEnabled(bool enabled)
        {
            _hierarchyWatchEnabledCache = enabled;
            ProjectScopedEditorSettings.SetBool(PrefsKeyHierarchyWatch, enabled);
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
            var stored = ProjectScopedEditorSettings.GetString(PrefsKeyHierarchyWatchClips, "");
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

        /// <summary>
        /// 選択中オブジェクトの親 Animator 配下のリネーム・親子変更を検出し、そのアバターの Clip のバインドパスを追従させる。
        /// パスはすべて監視ルート（Animator）基準。
        /// </summary>
        private static class HierarchyPathWatcher
        {
            private static bool _queued;
            private static bool _resolving;
            private static bool _skipNextResolve;
            private static int _snapshotRootId;
            private static Dictionary<int, string> _pathSnapshot;
            private static Dictionary<string, GlobalObjectId> _objectIdsByPath;
            private static readonly Dictionary<int, GlobalObjectId> GlobalObjectIdCache = new Dictionary<int, GlobalObjectId>();

            internal static Transform WatchedRoot { get; private set; }

            static HierarchyPathWatcher()
            {
                Selection.selectionChanged += OnSelectionChanged;
                Undo.undoRedoPerformed += OnUndoRedoPerformed;
                Undo.postprocessModifications += OnUndoPropertyModifications;
#if UNITY_2022_2_OR_NEWER
                ObjectChangeEvents.changesPublished += OnChangesPublished;
#else
                EditorApplication.hierarchyChanged += QueueResolve;
#endif
                EditorApplication.delayCall += CaptureSnapshot;
            }

            internal static void EnsureInitialized()
            {
            }

            internal static void NotifySettingsChanged()
            {
                ClearSnapshot();
                GlobalObjectIdCache.Clear();
                WatchedRoot = null;
                CaptureSnapshot();
            }

            private static void ClearSnapshot()
            {
                _pathSnapshot = null;
                _objectIdsByPath = null;
                _snapshotRootId = 0;
            }

            private static void OnUndoRedoPerformed()
            {
                _skipNextResolve = true;
                ClearSnapshot();
                GlobalObjectIdCache.Clear();
                EditorApplication.delayCall += () =>
                {
                    CaptureSnapshot();
                    _skipNextResolve = false;
                };
            }

            private static UndoPropertyModification[] OnUndoPropertyModifications(UndoPropertyModification[] modifications)
            {
                if (modifications == null || !HierarchyWatchEnabled)
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
                if (!HierarchyWatchEnabled)
                    return;

                for (int i = 0; i < stream.length; i++)
                {
                    switch (stream.GetEventType(i))
                    {
                        case ObjectChangeKind.ChangeGameObjectParent:
                        case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                        case ObjectChangeKind.ChangeGameObjectStructure:
                        case ObjectChangeKind.CreateGameObjectHierarchy:
                        case ObjectChangeKind.DestroyGameObjectHierarchy:
                        case ObjectChangeKind.UpdatePrefabInstances:
                            QueueResolve();
                            return;

                        case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                            stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var args);
                            if (EditorUtility.InstanceIDToObject(args.instanceId) is GameObject go && IsRenamedSinceSnapshot(go))
                            {
                                QueueResolve();
                                return;
                            }
                            break;
                    }
                }
            }
#endif

            /// <summary>プロパティ変更イベントのうち、スナップショット時から名前が変わったものだけを拾う。</summary>
            private static bool IsRenamedSinceSnapshot(GameObject go)
            {
                if (_pathSnapshot == null || go == null)
                    return false;
                if (!_pathSnapshot.TryGetValue(go.transform.GetInstanceID(), out var oldPath) || string.IsNullOrEmpty(oldPath))
                    return false;

                var slash = oldPath.LastIndexOf('/');
                var oldName = slash >= 0 ? oldPath.Substring(slash + 1) : oldPath;
                return oldName != go.name;
            }

            private static void QueueResolve()
            {
                if (!HierarchyWatchEnabled || _resolving || _queued)
                    return;
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    ClearSnapshot();
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
                    ClearSnapshot();
                    return;
                }

                var animatorRoot = ResolveAnimatorRootFromSelection();
                WatchedRoot = animatorRoot;
                if (animatorRoot == null)
                {
                    ClearSnapshot();
                    return;
                }

                var rootId = animatorRoot.GetInstanceID();
                if (rootId != _snapshotRootId)
                    GlobalObjectIdCache.Clear();

                _snapshotRootId = rootId;
                _pathSnapshot = CapturePaths(animatorRoot, out var transforms);
                _objectIdsByPath = CaptureObjectIds(animatorRoot, transforms, _pathSnapshot);
            }

            private static int ResolvePaths()
            {
                if (_skipNextResolve || _resolving || !HierarchyWatchEnabled)
                    return 0;

                var animatorRoot = ResolveAnimatorRootFromSelection();
                WatchedRoot = animatorRoot;
                if (animatorRoot == null)
                {
                    ClearSnapshot();
                    return 0;
                }

                var rootId = animatorRoot.GetInstanceID();
                var current = CapturePaths(animatorRoot, out var transforms);
                if (_pathSnapshot == null || _snapshotRootId != rootId)
                {
                    if (_snapshotRootId != rootId)
                        GlobalObjectIdCache.Clear();
                    _snapshotRootId = rootId;
                    _pathSnapshot = current;
                    _objectIdsByPath = CaptureObjectIds(animatorRoot, transforms, current);
                    return 0;
                }

                var rules = BuildPathRules(_pathSnapshot, current);
                AddRulesForMissingObjectIds(rules, _objectIdsByPath, animatorRoot);
                _snapshotRootId = rootId;
                _pathSnapshot = current;
                _objectIdsByPath = CaptureObjectIds(animatorRoot, transforms, current);
                if (rules.Count == 0)
                    return 0;

                var clips = CollectTargetClips(animatorRoot, null, rules.Keys);
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

                var targets = CollectTargetClips(animatorRoot, clips, rules.Keys);
                if (targets.Length == 0)
                    return 0;

                _resolving = true;
                try
                {
                    Undo.RegisterCompleteObjectUndo(targets, "Resolve Missing Binding Paths");
                    var replaced = ReplaceExactPathsInClips(targets, rules);
                    if (replaced > 0)
                        SaveClipsIfDirty(targets);
                    return replaced;
                }
                finally
                {
                    _resolving = false;
                }
            }

            private static Transform ResolveAnimatorRoot(Transform root)
            {
                if (root == null)
                    return null;

                var animator = root.GetComponentInParent<Animator>();
                return animator != null ? animator.transform : null;
            }

            /// <summary>
            /// 書き換え対象の Clip を集める。他アバターや配布アセットの Clip を巻き込まないよう、
            /// 置換ツールで指定した Clip・ルートの Animator・VRCAvatarDescriptor・ルート基準の MA Merge Animator に限定し、
            /// その中で古いパスを実際に持つものだけを返す。
            /// </summary>
            private static AnimationClip[] CollectTargetClips(Transform animatorRoot, IEnumerable<AnimationClip> extraClips, IEnumerable<string> oldPaths)
            {
                var candidates = new HashSet<AnimationClip>();
                foreach (var clip in LoadWatchClips())
                    AddEditableClip(candidates, clip);

                if (extraClips != null)
                {
                    foreach (var clip in extraClips)
                        AddEditableClip(candidates, clip);
                }

                var animator = animatorRoot != null ? animatorRoot.GetComponent<Animator>() : null;
                foreach (var entry in AvatarAnimatorControllerCollector.GetControllers(animator))
                    AddControllerClips(candidates, entry.Controller);
                foreach (var controller in AvatarAnimatorControllerCollector.GetRootRelativeMergeAnimatorControllers(animatorRoot))
                    AddControllerClips(candidates, controller);

                var pathSet = oldPaths != null ? new HashSet<string>(oldPaths) : null;
                if (pathSet == null || pathSet.Count == 0)
                    return candidates.ToArray();

                return candidates.Where(clip => ClipContainsAnyPath(clip, pathSet)).ToArray();
            }

            private static void AddControllerClips(HashSet<AnimationClip> clips, RuntimeAnimatorController controller)
            {
                if (controller == null)
                    return;

                var controllerClips = controller.animationClips;
                for (int i = 0; i < controllerClips.Length; i++)
                    AddEditableClip(clips, controllerClips[i]);
            }

            private static void AddEditableClip(HashSet<AnimationClip> clips, AnimationClip clip)
            {
                if (IsEditableClip(clip))
                    clips.Add(clip);
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

            /// <summary>
            /// パス → GlobalObjectId。GetGlobalObjectIdSlow は重いため InstanceID ごとにキャッシュし、未取得分だけまとめて取得する。
            /// 未保存オブジェクトは ID が確定していない（targetObjectId が 0）ためキャッシュしない。
            /// </summary>
            private static Dictionary<string, GlobalObjectId> CaptureObjectIds(Transform root, List<Transform> transforms, Dictionary<int, string> pathsById)
            {
                var map = new Dictionary<string, GlobalObjectId>();
                if (root == null || transforms == null || pathsById == null)
                    return map;

                var uncached = new List<Object>();
                for (int i = 0; i < transforms.Count; i++)
                {
                    var transform = transforms[i];
                    if (transform != null && transform != root && !GlobalObjectIdCache.ContainsKey(transform.GetInstanceID()))
                        uncached.Add(transform);
                }

                var fetched = new Dictionary<int, GlobalObjectId>();
                if (uncached.Count > 0)
                {
                    var ids = new GlobalObjectId[uncached.Count];
                    GlobalObjectId.GetGlobalObjectIdsSlow(uncached.ToArray(), ids);
                    for (int i = 0; i < uncached.Count; i++)
                    {
                        var instanceId = uncached[i].GetInstanceID();
                        fetched[instanceId] = ids[i];
                        if (ids[i].targetObjectId != 0)
                            GlobalObjectIdCache[instanceId] = ids[i];
                    }
                }

                for (int i = 0; i < transforms.Count; i++)
                {
                    var transform = transforms[i];
                    if (transform == null || transform == root)
                        continue;

                    var instanceId = transform.GetInstanceID();
                    if (!GlobalObjectIdCache.TryGetValue(instanceId, out var id) && !fetched.TryGetValue(instanceId, out id))
                        continue;

                    if (pathsById.TryGetValue(instanceId, out var path) && !string.IsNullOrEmpty(path))
                        map[path] = id;
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

                var missingPaths = new List<string>();
                var missingIds = new List<GlobalObjectId>();
                foreach (var pair in objectIdsByPath)
                {
                    if (string.IsNullOrEmpty(pair.Key) || rules.ContainsKey(pair.Key))
                        continue;
                    if (animatorRoot.Find(pair.Key) != null)
                        continue;

                    missingPaths.Add(pair.Key);
                    missingIds.Add(pair.Value);
                }

                if (missingIds.Count == 0)
                    return;

                var objects = new Object[missingIds.Count];
                GlobalObjectId.GlobalObjectIdentifiersToObjectsSlow(missingIds.ToArray(), objects);
                for (int i = 0; i < objects.Length; i++)
                {
                    var transform = objects[i] as Transform;
                    if (transform == null)
                        continue;
                    if (transform != animatorRoot && !transform.IsChildOf(animatorRoot))
                        continue;

                    var newPath = GetTransformPath(transform, animatorRoot);
                    if (string.IsNullOrEmpty(newPath) || newPath == missingPaths[i])
                        continue;

                    rules[missingPaths[i]] = newPath;
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

            /// <summary>InstanceID → ルート基準パス。1 回の走査で全子孫のパスを作る。</summary>
            private static Dictionary<int, string> CapturePaths(Transform root, out List<Transform> transforms)
            {
                var map = new Dictionary<int, string>();
                transforms = new List<Transform>();
                if (root == null)
                    return map;

                CapturePathsRecursive(root, "", map, transforms);
                return map;
            }

            private static void CapturePathsRecursive(Transform transform, string path, Dictionary<int, string> map, List<Transform> transforms)
            {
                map[transform.GetInstanceID()] = path;
                transforms.Add(transform);
                for (int i = 0; i < transform.childCount; i++)
                {
                    var child = transform.GetChild(i);
                    var childPath = string.IsNullOrEmpty(path) ? child.name : path + "/" + child.name;
                    CapturePathsRecursive(child, childPath, map, transforms);
                }
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
