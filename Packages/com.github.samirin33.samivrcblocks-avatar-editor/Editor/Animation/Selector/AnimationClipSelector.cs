#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Samirin33.Editor;
using Samirin33.AvatarEditor.Animation.Editor;

namespace Samirin33.SamirinVRCUtility.AvatarEditor
{
    /// <summary>レイヤー内のクリップグループ（サブステートマシン階層を保持）</summary>
    internal class ClipGroup
    {
        public string Name;
        public string FullPath;
        /// <summary>Animator 使用箇所検索用。サブステートマシン階層のみ（BlendTree 名は含めない）。</summary>
        public string AnimatorScopePath;
        public List<AnimationClip> Clips = new List<AnimationClip>();
        public List<ClipGroup> Children = new List<ClipGroup>();
        public int TotalClipCount => Clips.Count + Children.Sum(c => c.TotalClipCount);
    }

    /// <summary>
    /// Animationウィンドウで編集中のAnimatorを取得し、
    /// 編集可能なAnimationClip一覧を表示して編集対象を選択できるエディタ拡張。
    /// </summary>
    /// <remarks>競合キャッシュは AnimationClipSelector.ConflictCache.cs、Animator ウィンドウ連携とステート検索は AnimationClipSelector.AnimatorWindowLink.cs。</remarks>
    public partial class AnimationClipSelector : EditorWindow, ISerializationCallbackReceiver
    {
        [SerializeField] private Vector2 _scrollPosition;
        private List<AnimationClip> _clips = new List<AnimationClip>();
        [SerializeField] private string _selectedControllerPath;
        private RuntimeAnimatorController _clipListCacheController;
        private bool _clipListCacheBlendTreeNested;
        private List<KeyValuePair<string, ClipGroup>> _clipsByLayerCache;
        private static bool _clipListCacheInvalidated;
        /// <summary>Selector が最後に表示した Animator とController。詳細ウィンドウ等が同じ Controller を解決するために使う。</summary>
        private static GameObject _displayedAnimatorObject;
        private static RuntimeAnimatorController _displayedController;
        private Dictionary<string, bool> _layerFoldoutState = new Dictionary<string, bool>();
        [SerializeField] private List<string> _serializedFoldoutKeys = new List<string>();
        [SerializeField] private List<bool> _serializedFoldoutValues = new List<bool>();
        private bool _pendingSliderSave;
        [SerializeField] private string _searchFilter = "";
        [SerializeField] private GameObject _lastActiveRoot;
        [SerializeField] private string _currentControllerPath;
        [SerializeField] private AnimationClip _lastSelectedClip;
        private static AnimationClip _pendingClipSelection;
        private Dictionary<int, (bool hasConflict, int pathCount, ConflictEntry[] conflictEntries)> _pathConflictCache = new Dictionary<int, (bool, int, ConflictEntry[])>();
        private string _pathConflictCacheControllerPath;
        private bool _pathConflictCachePending;
        private static bool _pathConflictCacheInvalidated;

        private const string WindowTitle = "Animation Clip Selector";

        private static AnimationClipSelectorSettings Settings => AnimationClipSelectorSettings.instance;

        [MenuItem("SBAvatarEditor/Animation/Animation Clip Selector", false, 2)]
        public static void Open()
        {
            var window = GetWindow<AnimationClipSelector>(WindowTitle);
            window.minSize = new Vector2(280, 200);
        }

        public void OnBeforeSerialize()
        {
            _serializedFoldoutKeys.Clear();
            _serializedFoldoutValues.Clear();
            foreach (var kvp in _layerFoldoutState)
            {
                _serializedFoldoutKeys.Add(kvp.Key);
                _serializedFoldoutValues.Add(kvp.Value);
            }
        }

        public void OnAfterDeserialize()
        {
            _layerFoldoutState = new Dictionary<string, bool>();
            for (int i = 0; i < _serializedFoldoutKeys.Count && i < _serializedFoldoutValues.Count; i++)
            {
                _layerFoldoutState[_serializedFoldoutKeys[i]] = _serializedFoldoutValues[i];
            }
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            EditorApplication.projectChanged += InvalidateClipListCache;
#if UNITY_2022_2_OR_NEWER
            ObjectChangeEvents.changesPublished += OnObjectChangesPublished;
#endif
            _clipListCacheInvalidated = true;

            if (_lastSelectedClip != null)
            {
                var clip = _lastSelectedClip;
                EditorApplication.delayCall += () =>
                {
                    AnimationWindowHelper.SetAnimationWindowClip(clip);
                };
            }
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            EditorApplication.projectChanged -= InvalidateClipListCache;
#if UNITY_2022_2_OR_NEWER
            ObjectChangeEvents.changesPublished -= OnObjectChangesPublished;
#endif
            if (!string.IsNullOrEmpty(_currentControllerPath))
            {
                AnimationClipSelectorStateManager.SaveFoldoutState(_currentControllerPath, _layerFoldoutState);
            }
            AnimationClipSelectorStateManager.FlushPendingSave();
            SaveSettings();
            _pathConflictCache.Clear();
            _pathConflictCachePending = false;
            _clipsByLayerCache = null;
            _clipListCacheController = null;
        }

        private static void OnUndoRedoPerformed()
        {
            _pathConflictCacheInvalidated = true;
            _clipListCacheInvalidated = true;
            AnimationClipSelectorStateManager.RequestSave();
        }

        private static void InvalidateClipListCache()
        {
            _clipListCacheInvalidated = true;
        }

#if UNITY_2022_2_OR_NEWER
        private static void OnObjectChangesPublished(ref ObjectChangeEventStream stream)
        {
            for (int i = 0; i < stream.length; i++)
            {
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.CreateAssetObject:
                    case ObjectChangeKind.DestroyAssetObject:
                    case ObjectChangeKind.ChangeAssetObjectProperties:
                        _clipListCacheInvalidated = true;
                        return;
                }
            }
        }
#endif

        #region 設定・Animation ウィンドウ

        private static void SaveSettings()
        {
            Settings.Save();
        }

        /// <summary>
        /// Animator 自身と VRCAvatarDescriptor の Playable Layers から、表示する Controller を選ぶ。
        /// 保存済みの選択が候補に無ければ Animator の Controller、無ければ FX、それも無ければ先頭。
        /// </summary>
        private static RuntimeAnimatorController ChooseController(List<AvatarControllerEntry> candidates, string preferredPath)
        {
            if (candidates == null || candidates.Count == 0) return null;

            if (!string.IsNullOrEmpty(preferredPath))
            {
                foreach (var entry in candidates)
                {
                    if (AssetDatabase.GetAssetPath(entry.Controller) == preferredPath)
                        return entry.Controller;
                }
            }

            foreach (var entry in candidates)
            {
                if (entry.Label == "Animator") return entry.Controller;
            }
            foreach (var entry in candidates)
            {
                if (entry.Label == "FX") return entry.Controller;
            }
            return candidates[0].Controller;
        }

        /// <summary>root の Animator について、Selector で表示中の Controller（無ければ既定の候補）を返す。</summary>
        internal static RuntimeAnimatorController ResolveControllerForRoot(GameObject root)
        {
            var animator = GetAnimatorFromRoot(root);
            if (animator == null) return null;

            var candidates = AvatarAnimatorControllerCollector.GetControllers(animator);
            if (_displayedController != null && _displayedAnimatorObject == animator.gameObject)
            {
                foreach (var entry in candidates)
                {
                    if (entry.Controller == _displayedController)
                        return _displayedController;
                }
            }
            return ChooseController(candidates, null);
        }

        /// <summary>ResolveControllerForRoot の Controller が clip を含まなければ、clip を含む別の候補 Controller を返す。</summary>
        internal static RuntimeAnimatorController ResolveControllerForClip(GameObject root, AnimationClip clip)
        {
            var resolved = ResolveControllerForRoot(root);
            if (clip == null || resolved == null || resolved.animationClips.Contains(clip))
                return resolved;

            var animator = GetAnimatorFromRoot(root);
            foreach (var entry in AvatarAnimatorControllerCollector.GetControllers(animator))
            {
                if (entry.Controller.animationClips.Contains(clip))
                    return entry.Controller;
            }
            return resolved;
        }

        private void EnsureClipListCache(RuntimeAnimatorController controller, bool showBlendTreeNested)
        {
            if (!_clipListCacheInvalidated
                && _clipsByLayerCache != null
                && _clipListCacheController == controller
                && _clipListCacheBlendTreeNested == showBlendTreeNested)
                return;

            _clipListCacheInvalidated = false;
            _clipListCacheController = controller;
            _clipListCacheBlendTreeNested = showBlendTreeNested;
            _clips = GetAllAnimationClips(controller);
            _clipsByLayerCache = GetClipsByGroupedLayer(controller, showBlendTreeNested);
        }

        private void OnGUI()
        {
            SamirinEditorStyleHelper.DrawWithBlueBackground(() =>
            {
                bool isAnimationWindowOpen = AnimationWindowHelper.TryGetAnimationWindowState(out var activeRoot, out var activeClip);
                if (!isAnimationWindowOpen && activeRoot == null)
                {
                    activeRoot = Selection.activeGameObject;
                }
                if (activeRoot == null && _lastActiveRoot != null)
                {
                    activeRoot = _lastActiveRoot;
                }

                if(activeRoot == null)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Animation Clip Selector", EditorStyles.boldLabel);
                    EditorGUILayout.Space(4);
                }

                if (activeRoot == null)
                {
                    EditorGUILayout.HelpBox(
                        "編集中のオブジェクトがありません。\nヒエラルキーでAnimatorを持つオブジェクトを選択してください。",
                        MessageType.Warning);
                    _clips.Clear();
                    _lastActiveRoot = null;
                    return;
                }

                var animator = GetAnimatorFromRoot(activeRoot);
                var controllerCandidates = AvatarAnimatorControllerCollector.GetControllers(animator);
                if (animator == null || controllerCandidates.Count == 0)
                {
                    if (activeRoot == _lastActiveRoot)
                        _lastActiveRoot = null;
                    EditorGUILayout.HelpBox(
                        "AnimatorまたはAnimator Controllerが見つかりません。\nAnimatorコンポーネントとControllerが設定されたオブジェクトを選択してください。",
                        MessageType.Warning);
                    _clips.Clear();
                    return;
                }

                // バインドパスは Animator の GameObject 基準のため、選択中の子/親オブジェクトではなく Animator 側に揃える
                activeRoot = animator.gameObject;
                _lastActiveRoot = activeRoot;

                var controller = ChooseController(controllerCandidates, _selectedControllerPath);
                if (controllerCandidates.Count > 1)
                {
                    var selectedIndex = controllerCandidates.FindIndex(c => c.Controller == controller);
                    var labels = controllerCandidates
                        .Select(c => new GUIContent($"{c.Label}: {c.Controller.name}", AssetDatabase.GetAssetPath(c.Controller)))
                        .ToArray();
                    var newIndex = EditorGUILayout.Popup(new GUIContent("Controller", "Animator と VRCAvatarDescriptor の Playable Layers から表示する Controller を選びます。"), selectedIndex, labels);
                    if (newIndex != selectedIndex && newIndex >= 0)
                        controller = controllerCandidates[newIndex].Controller;
                }
                _selectedControllerPath = AssetDatabase.GetAssetPath(controller);
                _displayedAnimatorObject = activeRoot;
                _displayedController = controller;

                EnsureClipListCache(controller, Settings.ShowBlendTreeNested);
                if (_clips.Count == 0)
                {
                    EditorGUILayout.HelpBox("Animator ControllerにAnimationClipが含まれていません。", MessageType.Info);
                    return;
                }

                EditorGUI.BeginChangeCheck();
                var controllerPath = GetControllerPath(controller);
                var clipsByLayer = _clipsByLayerCache;

                if (controllerPath != _currentControllerPath)
                {
                    if (!string.IsNullOrEmpty(_currentControllerPath))
                    {
                        AnimationClipSelectorStateManager.SaveFoldoutState(_currentControllerPath, _layerFoldoutState);
                        AnimationClipSelectorStateManager.RequestSave();
                    }
                    _layerFoldoutState.Clear();
                    AnimationClipSelectorStateManager.RestoreFoldoutState(controllerPath, _layerFoldoutState);
                    _currentControllerPath = controllerPath;
                }

                EditorGUILayout.LabelField($"AnimationClip一覧 ({_clips.Count}件)", EditorStyles.boldLabel);
                var isShowingCached = !isAnimationWindowOpen && Selection.activeGameObject == null && activeRoot != null;
                var statusLabel = isAnimationWindowOpen ? "編集中" : (isShowingCached ? "最後に選択" : "選択中");
                EditorGUILayout.LabelField($"{statusLabel}: {activeRoot.name}", EditorStyles.miniLabel);

                EditorGUILayout.BeginHorizontal();

                var collapseIcon = EditorGUIUtility.IconContent("d_IN_foldout_act");
                if (collapseIcon == null) collapseIcon = new GUIContent("−", "すべて折りたたむ");
                else collapseIcon.tooltip = "すべて折りたたむ";
                if (GUILayout.Button(collapseIcon, GUILayout.Width(30), GUILayout.Height(20)))
                {
                    SetAllFoldouts(clipsByLayer, controllerPath, false, _layerFoldoutState);
                    AnimationClipSelectorStateManager.SaveFoldoutState(controllerPath, _layerFoldoutState);
                    AnimationClipSelectorStateManager.RequestSave();
                }
                var expandIcon = EditorGUIUtility.IconContent("d_IN_foldout_act_on");
                if (expandIcon == null) expandIcon = new GUIContent("＋", "すべて開く");
                else expandIcon.tooltip = "すべて開く";
                if (GUILayout.Button(expandIcon, GUILayout.Width(30), GUILayout.Height(20)))
                {
                    SetAllFoldouts(clipsByLayer, controllerPath, true, _layerFoldoutState);
                    AnimationClipSelectorStateManager.SaveFoldoutState(controllerPath, _layerFoldoutState);
                    AnimationClipSelectorStateManager.RequestSave();
                }

                var reloadIcon = EditorGUIUtility.IconContent("Refresh");
                if (reloadIcon == null) reloadIcon = new GUIContent("↻", "リストを再読み込み");
                else reloadIcon.tooltip = "リストを再読み込み";
                if (GUILayout.Button(reloadIcon, GUILayout.Width(30), GUILayout.Height(20)))
                {
                    ReloadSelectorList(controller, controllerPath);
                    clipsByLayer = _clipsByLayerCache;
                }

                EditorGUILayout.LabelField("検索", GUILayout.Width(28));
                _searchFilter = EditorGUILayout.TextField(_searchFilter);
                var clearIcon = EditorGUIUtility.IconContent("winbtn_win_close");
                if (clearIcon == null) clearIcon = new GUIContent("×", "検索をクリア");
                else clearIcon.tooltip = "検索をクリア";
                if (GUILayout.Button(clearIcon, GUILayout.Width(22), GUILayout.Height(18)))
                {
                    _searchFilter = "";
                }

                EditorGUILayout.LabelField("間隔", GUILayout.Width(28));
                var spacing = EditorGUILayout.Slider(Settings.ItemSpacing, 0f, 16f, GUILayout.Width(120));
                if (EditorGUI.EndChangeCheck())
                {
                    Settings.ItemSpacing = spacing;
                    _pendingSliderSave = true;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                Settings.ShowBlendTreeNested = EditorGUILayout.ToggleLeft("BlendTree 入れ子表示", Settings.ShowBlendTreeNested);
                if (EditorGUI.EndChangeCheck())
                {
                    _pendingSliderSave = true;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(4);

                if (controllerPath != _pathConflictCacheControllerPath)
                {
                    _pathConflictCache.Clear();
                    _pathConflictCacheControllerPath = controllerPath;
                    _pathConflictCachePending = false;
                }
                if (_pathConflictCacheInvalidated)
                {
                    _pathConflictCache.Clear();
                    _pathConflictCachePending = false;
                    _pathConflictCacheInvalidated = false;
                }
                if (_pathConflictCache.Count == 0 && _clips.Count > 0 && !_pathConflictCachePending)
                {
                    _pathConflictCachePending = true;
                    var capturedController = controller;
                    var capturedPath = controllerPath;
                    EditorApplication.delayCall += () =>
                    {
                        if (capturedPath != _pathConflictCacheControllerPath)
                        {
                            _pathConflictCachePending = false;
                            return;
                        }
                        _pathConflictCache = BuildPathConflictCache(capturedController);
                        _pathConflictCachePending = false;
                        Repaint();
                    };
                }

                System.Func<AnimationClip, (bool hasConflict, int pathCount, ConflictEntry[] conflictEntries)?> getPathConflict = clip =>
                {
                    if (clip == null) return null;
                    return _pathConflictCache.TryGetValue(clip.GetInstanceID(), out var v) ? v : ((bool, int, ConflictEntry[])?)null;
                };

                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));

                float scrollContentY = 0f;
                foreach (var kvp in clipsByLayer)
                {
                    var layerName = kvp.Key;
                    var rootGroup = kvp.Value;
                    var filteredCount = GetMatchingClipCount(rootGroup, _searchFilter);
                    if (!string.IsNullOrEmpty(_searchFilter) && filteredCount == 0)
                        continue;

                    var foldoutKey = controllerPath + "|" + layerName;
                    if (!_layerFoldoutState.TryGetValue(foldoutKey, out var isExpanded))
                        isExpanded = true;

                    var layerBlockHeight = 13f + Settings.ItemSpacing;
                    var foldoutRect = EditorGUILayout.GetControlRect(false, layerBlockHeight);
                    scrollContentY += layerBlockHeight;
                    var hasActiveClipInLayer = !isExpanded && activeClip != null && rootGroup.TotalClipCount > 0 && ContainsClip(rootGroup, activeClip);
                    if (hasActiveClipInLayer)
                        EditorGUI.DrawRect(foldoutRect, new Color(0.3f, 0.5f, 0.8f, 0.1f));
                    var countLabel = string.IsNullOrEmpty(_searchFilter) ? $"{rootGroup.TotalClipCount}件" : $"{filteredCount}/{rootGroup.TotalClipCount}件";
                    _layerFoldoutState[foldoutKey] = EditorGUI.Foldout(new Rect(foldoutRect.x, foldoutRect.y, foldoutRect.width, 18f), isExpanded, $"{layerName} ({countLabel})", true);
                    if (!_layerFoldoutState[foldoutKey])
                        DrawFoldoutIssueIcons(rootGroup, foldoutRect, activeRoot, Settings, _searchFilter, getPathConflict);

                    if (_layerFoldoutState[foldoutKey])
                    {
                        var oldBg = GUI.backgroundColor;
                        GUI.backgroundColor = new Color(0.97f, 0.98f, 1f);
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        GUI.backgroundColor = oldBg;
                        GUILayout.Space(4);
                        scrollContentY += 4;
                        EditorGUI.indentLevel += 1;
                        GUILayout.Space(2);
                        scrollContentY += 2;
                        DrawClipGroup(rootGroup, controllerPath, layerName, activeRoot, animator, controller, activeClip, Settings, Settings.ItemSpacing, _layerFoldoutState, _searchFilter, !isAnimationWindowOpen, getPathConflict, ref scrollContentY);
                        EditorGUI.indentLevel -= 1;
                        GUILayout.Space(4);
                        scrollContentY += 4;
                        EditorGUILayout.EndVertical();
                    }
                }

                EditorGUILayout.EndScrollView();

                if (_pendingClipSelection != null)
                {
                    _lastSelectedClip = _pendingClipSelection;
                    _pendingClipSelection = null;
                }
            }, new Rect(0, 0, position.width, position.height));
        }

        private void ReloadSelectorList(RuntimeAnimatorController controller, string controllerPath)
        {
            if (controller == null)
                return;

            if (AnimationWindowHelper.TryGetAnimationWindowState(out var activeRoot, out _))
            {
                if (activeRoot != null)
                    _lastActiveRoot = activeRoot;
            }
            else if (Selection.activeGameObject != null && GetAnimatorFromRoot(Selection.activeGameObject) != null)
            {
                _lastActiveRoot = Selection.activeGameObject;
            }

            _clipListCacheInvalidated = true;
            EnsureClipListCache(controller, Settings.ShowBlendTreeNested);
            _pathConflictCache.Clear();
            _pathConflictCacheControllerPath = controllerPath;
            _pathConflictCachePending = false;
            _pathConflictCacheInvalidated = false;
            _pathConflictCache = BuildPathConflictCache(controller);
            Repaint();
        }

        private void OnInspectorUpdate()
        {
            if (_pendingSliderSave && GUIUtility.hotControl == 0)
            {
                _pendingSliderSave = false;
                SaveSettings();
            }
            Repaint();
        }

        /// <summary>Animationウィンドウの編集対象を指定クリップに切り替える。他ウィンドウ（例: ClipConflictDetailsWindow）から呼び出す用。</summary>
        internal static void SetAnimationWindowToClip(AnimationClip clip)
        {
            AnimationWindowHelper.SetAnimationWindowToClip(clip);
        }

        /// <summary>
        /// 多レイヤー競合している Transform パス＋属性の情報を、専用の詳細ウィンドウで表示する。
        /// Animation ウィンドウ側には対象クリップだけをセットし、競合情報は別ウィンドウで確認できるようにする。
        /// </summary>
        private static void ShowConflictPathsInAnimationWindow(AnimationClip clip, ConflictEntry[] conflictEntries, GameObject root)
        {
            if (clip == null || conflictEntries == null || conflictEntries.Length == 0) return;

            // Animation ウィンドウを開き、対象クリップをセット（視覚的な確認用）
            AnimationWindowHelper.EnsureAnimationWindowOpenAndSetClip(clip);

            // 構造化した競合詳細を専用ウィンドウで表示（クリックで選択可能）
            ClipConflictDetailsWindow.Open(clip, conflictEntries, root);
        }

        private static void ShowMissingBindingsPopup(AnimationClip clip, GameObject root)
        {
            if (clip == null || root == null) return;

            var controller = ResolveControllerForClip(root, clip);
            var controllerClips = controller != null
                ? GetAllAnimationClips(controller)
                : new List<AnimationClip> { clip };
            ClipMissingBindingDetailsWindow.Open(clip, root, controllerClips);
        }

        private static Animator GetAnimatorFromRoot(GameObject root)
        {
            if (root == null) return null;
            var animator = root.GetComponentInParent<Animator>();
            if (animator != null) return animator;
            return root.GetComponentInChildren<Animator>();
        }

        /// <summary>指定 Controller を使用している GameObject をシーンから検索（Auto Live Link 用）</summary>
        private static GameObject FindGameObjectWithController(RuntimeAnimatorController controller)
        {
            if (controller == null) return null;
            var animators = UnityEngine.Object.FindObjectsOfType<Animator>();
            foreach (var go in animators)
            {
                if (go.runtimeAnimatorController == controller) return go.gameObject;
                if (go.runtimeAnimatorController is AnimatorOverrideController ov && ov.runtimeAnimatorController == controller)
                    return go.gameObject;
            }
            return null;
        }

        private static string GetControllerPath(RuntimeAnimatorController controller)
        {
            if (controller == null) return null;
            var animController = controller is AnimatorOverrideController ov
                ? ov.runtimeAnimatorController as AnimatorController
                : controller as AnimatorController;
            if (animController == null) return null;
            return AssetDatabase.GetAssetPath(animController);
        }

        private static List<AnimationClip> GetAllAnimationClips(RuntimeAnimatorController controller)
        {
            var clips = new HashSet<AnimationClip>();

            if (controller is AnimatorOverrideController overrideController)
            {
                var baseController = overrideController.runtimeAnimatorController as AnimatorController;
                if (baseController != null)
                {
                    var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(overrideController.overridesCount);
                    overrideController.GetOverrides(overrides);
                    foreach (var kv in overrides)
                    {
                        clips.Add(kv.Value != null ? kv.Value : kv.Key);
                    }
                }
            }
            else if (controller is AnimatorController animController)
            {
                foreach (var clip in animController.animationClips)
                {
                    clips.Add(clip);
                }
            }

            return new List<AnimationClip>(clips);
        }

        /// <summary>
        /// レイヤー毎にクリップをグループ化（サブステートマシン階層を保持）。OverrideControllerのオーバーライドを適用。
        /// </summary>
        private static List<KeyValuePair<string, ClipGroup>> GetClipsByGroupedLayer(RuntimeAnimatorController controller, bool showBlendTreeNested)
        {
            var result = new List<KeyValuePair<string, ClipGroup>>();
            AnimatorController baseController = null;
            Dictionary<AnimationClip, AnimationClip> overrideMap = null;

            if (controller is AnimatorOverrideController overrideController)
            {
                baseController = overrideController.runtimeAnimatorController as AnimatorController;
                if (baseController == null) return result;
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

            if (baseController == null) return result;

            AnimationClip ResolveClip(AnimationClip baseClip)
            {
                if (baseClip == null) return null;
                if (overrideMap != null && overrideMap.TryGetValue(baseClip, out var ov))
                    return ov;
                return baseClip;
            }

            foreach (var layer in baseController.layers)
            {
                var root = BuildClipGroupFromStateMachine(layer.stateMachine, layer.name, layer.name, ResolveClip, showBlendTreeNested);
                result.Add(new KeyValuePair<string, ClipGroup>(layer.name, root));
            }

            return result;
        }

        /// <summary>レイヤー毎にクリップをグループ化（サブステートマシン階層を保持）。OverrideControllerのオーバーライドを適用。</summary>
        private static List<KeyValuePair<string, ClipGroup>> GetClipsByGroupedLayer(RuntimeAnimatorController controller)
        {
            return GetClipsByGroupedLayer(controller, true);
        }

        /// <summary>ステートマシンを再帰走査し、サブステート階層を保持したClipGroupを構築。共通のサブステート検知ロジック。</summary>
        private static ClipGroup BuildClipGroupFromStateMachine(AnimatorStateMachine stateMachine, string layerName, string pathPrefix, System.Func<AnimationClip, AnimationClip> resolveClip, bool showBlendTreeNested)
        {
            var group = new ClipGroup
            {
                Name = stateMachine != null ? stateMachine.name : "",
                FullPath = pathPrefix,
                AnimatorScopePath = pathPrefix
            };
            if (stateMachine == null) return group;

            var clipsSet = new HashSet<AnimationClip>();
            foreach (var child in stateMachine.states)
            {
                if (child.state == null || child.state.motion == null) continue;

                if (child.state.motion is AnimationClip motionClip)
                {
                    var resolved = resolveClip(motionClip);
                    if (resolved != null)
                        clipsSet.Add(resolved);
                }
                else if (child.state.motion is BlendTree motionBlendTree)
                {
                    if (showBlendTreeNested)
                    {
                        var blendTreePath = pathPrefix + "|" + child.state.name + ": " + motionBlendTree.name;
                        var btGroup = BuildClipGroupFromBlendTree(
                            motionBlendTree,
                            resolveClip,
                            child.state.name,
                            pathPrefix,
                            blendTreePath);
                        if (btGroup != null && (btGroup.Clips.Count > 0 || btGroup.Children.Count > 0))
                            group.Children.Add(btGroup);
                    }
                    else
                    {
                        CollectClipsFromMotion(child.state.motion, clipsSet, resolveClip);
                    }
                }
            }
            group.Clips = clipsSet.ToList();

            foreach (var child in stateMachine.stateMachines)
            {
                if (child.stateMachine == null) continue;
                var childPath = pathPrefix + "|" + child.stateMachine.name;
                var childGroup = BuildClipGroupFromStateMachine(child.stateMachine, layerName, childPath, resolveClip, showBlendTreeNested);
                if (childGroup.Clips.Count > 0 || childGroup.Children.Count > 0)
                    group.Children.Add(childGroup);
            }
            return group;
        }

        private static ClipGroup BuildClipGroupFromBlendTree(
            BlendTree blendTree,
            System.Func<AnimationClip, AnimationClip> resolveClip,
            string stateName,
            string animatorScopePath,
            string fullPath)
        {
            var group = new ClipGroup
            {
                Name = blendTree != null ? $"{stateName}: {blendTree.name}" : "",
                FullPath = fullPath,
                AnimatorScopePath = animatorScopePath
            };
            if (blendTree == null) return group;

            var clipsSet = new HashSet<AnimationClip>();
            for (int i = 0; i < blendTree.children.Length; i++)
            {
                var child = blendTree.children[i];
                if (child.motion == null) continue;

                if (child.motion is AnimationClip motionClip)
                {
                    var resolved = resolveClip(motionClip);
                    if (resolved != null)
                        clipsSet.Add(resolved);
                }
                else if (child.motion is BlendTree childBlendTree)
                {
                    var childFullPath = fullPath + "|" + stateName + ": " + childBlendTree.name;
                    var childGroup = BuildClipGroupFromBlendTree(
                        childBlendTree,
                        resolveClip,
                        stateName,
                        animatorScopePath,
                        childFullPath);
                    if (childGroup != null && (childGroup.Clips.Count > 0 || childGroup.Children.Count > 0))
                        group.Children.Add(childGroup);
                }
            }

            group.Clips = clipsSet.ToList();
            return group;
        }

        private static ClipGroup BuildClipGroupFromStateMachine(AnimatorStateMachine stateMachine, string layerName, string pathPrefix, System.Func<AnimationClip, AnimationClip> resolveClip)
        {
            return BuildClipGroupFromStateMachine(stateMachine, layerName, pathPrefix, resolveClip, true);
        }

        private static void ExpandFoldoutContainingClip(List<KeyValuePair<string, ClipGroup>> clipsByLayer, string controllerPath, AnimationClip clip, Dictionary<string, bool> foldoutState)
        {
            foreach (var kvp in clipsByLayer)
            {
                if (ContainsClip(kvp.Value, clip))
                {
                    ExpandGroupContainingClip(kvp.Value, controllerPath, kvp.Key, clip, foldoutState);
                    break;
                }
            }
        }

        private static void ExpandGroupContainingClip(ClipGroup group, string controllerPath, string pathPrefix, AnimationClip clip, Dictionary<string, bool> foldoutState)
        {
            foldoutState[controllerPath + "|" + pathPrefix] = true;
            if (group.Clips.Contains(clip)) return;
            foreach (var child in group.Children)
            {
                if (ContainsClip(child, clip))
                {
                    ExpandGroupContainingClip(child, controllerPath, pathPrefix + "|" + child.Name, clip, foldoutState);
                    break;
                }
            }
        }

        private static void SetAllFoldouts(List<KeyValuePair<string, ClipGroup>> clipsByLayer, string controllerPath, bool expanded, Dictionary<string, bool> foldoutState)
        {
            foreach (var kvp in clipsByLayer)
                SetGroupFoldouts(kvp.Value, controllerPath, kvp.Key, expanded, foldoutState);
        }

        private static void SetGroupFoldouts(ClipGroup group, string controllerPath, string pathPrefix, bool expanded, Dictionary<string, bool> foldoutState)
        {
            foldoutState[controllerPath + "|" + pathPrefix] = expanded;
            foreach (var child in group.Children)
                SetGroupFoldouts(child, controllerPath, pathPrefix + "|" + child.Name, expanded, foldoutState);
        }

        private static bool ContainsClip(ClipGroup group, AnimationClip clip)
        {
            if (group.Clips.Contains(clip)) return true;
            return group.Children.Any(c => ContainsClip(c, clip));
        }

        private static bool ClipMatchesSearch(AnimationClip clip, string searchFilter)
        {
            if (clip == null || string.IsNullOrEmpty(searchFilter)) return true;
            return clip.name.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int GetMatchingClipCount(ClipGroup group, string searchFilter)
        {
            if (string.IsNullOrEmpty(searchFilter)) return group.TotalClipCount;
            var count = group.Clips.Count(c => c != null && ClipMatchesSearch(c, searchFilter));
            foreach (var child in group.Children)
                count += GetMatchingClipCount(child, searchFilter);
            return count;
        }

        private static void DrawFoldoutIssueIcons(
            ClipGroup group,
            Rect foldoutRect,
            GameObject activeRoot,
            AnimationClipSelectorSettings settings,
            string searchFilter,
            System.Func<AnimationClip, (bool hasConflict, int pathCount, ConflictEntry[] conflictEntries)?> getPathConflict)
        {
            if (group == null || activeRoot == null)
                return;

            var hasConflict = GroupHasConflict(group, settings, searchFilter, getPathConflict);
            var hasMissing = GroupHasMissing(group, activeRoot, settings, searchFilter);
            if (!hasConflict && !hasMissing)
                return;

            var iconX = foldoutRect.xMax - 6f;
            if (hasConflict)
            {
                var warnRect = new Rect(iconX - 20f, foldoutRect.y, 20f, 18f);
                var warnIcon = EditorGUIUtility.IconContent("sv_icon_dot15_pix16_gizmo");
                if (warnIcon == null) warnIcon = new GUIContent("!", "この配下にコンフリクトがあります");
                else warnIcon.tooltip = "この配下にコンフリクトがあります";
                GUI.Label(warnRect, warnIcon);
                iconX -= 22f;
            }

            if (hasMissing)
            {
                var missingRect = new Rect(iconX - 20f, foldoutRect.y, 20f, 18f);
                var missingIcon = EditorGUIUtility.IconContent("sv_icon_dot12_pix16_gizmo");
                if (missingIcon == null) missingIcon = new GUIContent("M", "この配下に Missing があります");
                else missingIcon.tooltip = "この配下に Missing があります";
                GUI.Label(missingRect, missingIcon);
            }
        }

        private static bool GroupHasConflict(
            ClipGroup group,
            AnimationClipSelectorSettings settings,
            string searchFilter,
            System.Func<AnimationClip, (bool hasConflict, int pathCount, ConflictEntry[] conflictEntries)?> getPathConflict)
        {
            if (group == null)
                return false;

            foreach (var clip in group.Clips)
            {
                if (clip == null || !ClipMatchesSearch(clip, searchFilter) || settings.IsIgnoredClip(clip))
                    continue;

                var pathConflict = getPathConflict?.Invoke(clip);
                if (pathConflict.HasValue && pathConflict.Value.hasConflict)
                    return true;
            }

            foreach (var child in group.Children)
            {
                if (GroupHasConflict(child, settings, searchFilter, getPathConflict))
                    return true;
            }

            return false;
        }

        private static bool GroupHasMissing(
            ClipGroup group,
            GameObject activeRoot,
            AnimationClipSelectorSettings settings,
            string searchFilter)
        {
            if (group == null || activeRoot == null)
                return false;

            foreach (var clip in group.Clips)
            {
                if (clip == null || !ClipMatchesSearch(clip, searchFilter) || settings.IsIgnoredClip(clip))
                    continue;

                if (AnimationClipBindingPathUtility.GetMissingBindingPathCount(activeRoot.transform, clip) > 0)
                    return true;
            }

            foreach (var child in group.Children)
            {
                if (GroupHasMissing(child, activeRoot, settings, searchFilter))
                    return true;
            }

            return false;
        }

        private static void DrawClipGroup(ClipGroup group, string controllerPath, string pathPrefix, GameObject activeRoot, Animator animator, RuntimeAnimatorController controller, AnimationClip activeClip, AnimationClipSelectorSettings settings, float itemSpacing, Dictionary<string, bool> foldoutState, string searchFilter, bool openAnimationWindowOnClipSelect, System.Func<AnimationClip, (bool hasConflict, int pathCount, ConflictEntry[] conflictEntries)?> getPathConflict, ref float scrollContentY)
        {
            var animatorScopePath = string.IsNullOrEmpty(group.AnimatorScopePath) ? pathPrefix : group.AnimatorScopePath;
            var hasSubGroups = group.Children.Count > 0;
            var hasDirectClips = group.Clips.Count > 0;
            var rowHeight = 20f;

            if (hasSubGroups)
            {
                foreach (var child in group.Children)
                {
                    if (!string.IsNullOrEmpty(searchFilter) && GetMatchingClipCount(child, searchFilter) == 0)
                        continue;
                    var childPath = pathPrefix + "|" + child.Name;
                    var childKey = controllerPath + "|" + childPath;
                    if (!foldoutState.TryGetValue(childKey, out var childExpanded))
                        childExpanded = true;

                    var groupBlockHeight = rowHeight + itemSpacing;
                    var foldoutRect = EditorGUILayout.GetControlRect(false, groupBlockHeight);
                    scrollContentY += groupBlockHeight;
                    var hasActive = !childExpanded && activeClip != null && ContainsClip(child, activeClip);
                    if (hasActive)
                        EditorGUI.DrawRect(foldoutRect, new Color(0.3f, 0.5f, 0.8f, 0.15f));
                    var childCountLabel = string.IsNullOrEmpty(searchFilter) ? $"{child.TotalClipCount}件" : $"{GetMatchingClipCount(child, searchFilter)}/{child.TotalClipCount}件";
                    foldoutState[childKey] = EditorGUI.Foldout(new Rect(foldoutRect.x, foldoutRect.y, foldoutRect.width, rowHeight), childExpanded, $"{child.Name} ({childCountLabel})", true);
                    if (!foldoutState[childKey])
                        DrawFoldoutIssueIcons(child, foldoutRect, activeRoot, settings, searchFilter, getPathConflict);

                    if (foldoutState[childKey])
                    {
                        var oldBg = GUI.backgroundColor;
                        GUI.backgroundColor = new Color(0.92f, 0.94f, 1f);
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        GUI.backgroundColor = oldBg;
                        GUILayout.Space(4);
                        scrollContentY += 4;
                        EditorGUI.indentLevel += 2;
                        GUILayout.Space(2);
                        scrollContentY += 2;
                        DrawClipGroup(child, controllerPath, childPath, activeRoot, animator, controller, activeClip, settings, itemSpacing, foldoutState, searchFilter, openAnimationWindowOnClipSelect, getPathConflict, ref scrollContentY);
                        EditorGUI.indentLevel -= 2;
                        GUILayout.Space(4);
                        scrollContentY += 4;
                        EditorGUILayout.EndVertical();
                    }
                }
            }

            if (hasDirectClips)
            {
                var indentDepth = pathPrefix.Split('|').Length - 1;
                var indentPx = indentDepth * 20;
                var sortedClips = group.Clips.OrderBy(c => c != null ? c.name : "").ToList();
                for (int i = 0; i < sortedClips.Count; i++)
                {
                    var clip = sortedClips[i];
                    if (clip == null) continue;
                    if (!ClipMatchesSearch(clip, searchFilter)) continue;

                    var isActive = clip == activeClip;
                    var blockHeight = rowHeight + itemSpacing;
                    scrollContentY += blockHeight;

                    var bgColor = isActive
                        ? new Color(0.3f, 0.5f, 0.8f, 0.25f)
                        : (i % 2 == 0 ? new Color(1, 1, 1, 0.04f) : Color.clear);

                    EditorGUILayout.BeginVertical();
                    var rect = EditorGUILayout.GetControlRect(false, blockHeight);
                    if (indentPx > 0)
                    {
                        rect.x += indentPx;
                        rect.width -= indentPx;
                    }
                    EditorGUI.DrawRect(rect, bgColor);

                    var pathConflict = getPathConflict?.Invoke(clip);
                    var isIgnoredClip = settings.IsIgnoredClip(clip);
                    var hasMultiLayerConflict = !isIgnoredClip && pathConflict.HasValue && pathConflict.Value.hasConflict;
                    var missingBindingCount = isIgnoredClip ? 0 : AnimationClipBindingPathUtility.GetMissingBindingPathCount(activeRoot.transform, clip);
                    var hasMissingBinding = missingBindingCount > 0;
                    var pathCount = pathConflict?.pathCount ?? 0;
                    var conflictEntries = pathConflict?.conflictEntries ?? Array.Empty<ConflictEntry>();
                    var rightMargin = 54f + 6f;
                    if (hasMultiLayerConflict) rightMargin += 26f;
                    if (hasMissingBinding) rightMargin += 26f;

                    var btnRect = new Rect(rect.x + 8, rect.y, rect.width - rightMargin, rowHeight);
                    var animatorRect = new Rect(rect.xMax - 54, rect.y, 24, rowHeight);
                    var zoomRect = new Rect(rect.xMax - 26, rect.y, 24, rowHeight);

                    var labelStyle = isActive ? EditorStyles.boldLabel : EditorStyles.label;
                    if (GUI.Button(btnRect, new GUIContent((isActive ? "● " : "  ") + clip.name, clip.name), labelStyle))
                    {
                        var currentSelection = Selection.activeGameObject;
                        var animatorRootTransform = activeRoot != null ? activeRoot.transform : null;
                        var shouldChangeSelection =
                            animatorRootTransform != null &&
                            (currentSelection == null ||
                             (currentSelection != activeRoot &&
                              !currentSelection.transform.IsChildOf(animatorRootTransform)));

                        if (shouldChangeSelection)
                            Selection.activeGameObject = activeRoot;
                        if (openAnimationWindowOnClipSelect)
                            AnimationWindowHelper.EnsureAnimationWindowOpenAndSetClip(clip);
                        else
                            AnimationWindowHelper.SetAnimationWindowClip(clip);

                        _pendingClipSelection = clip;
                        AnimationClipSelectorStateManager.NotifyClipSelected(controllerPath, clip);
                    }

                    float warnX = rect.xMax - 54;
                    if (hasMultiLayerConflict)
                    {
                        // クリック判定をアイコン全体＋少し広めにとる
                        var warnRect = new Rect(warnX - 26, rect.y, 26, rowHeight);
                        var warnIcon = EditorGUIUtility.IconContent("sv_icon_dot15_pix16_gizmo");
                        if (warnIcon == null) warnIcon = new GUIContent("!", $"異なるレイヤーで同一パスを制御しようとしています。(警告パス数:{pathCount}個)\nクリックで対象パスを表示");
                        else warnIcon.tooltip = $"異なるレイヤーで同一パスを制御しようとしています。(警告パス数:{pathCount}個)\nクリックで対象パスを表示";

                        // ボタンは枠無しでフル矩形を当たり判定に使い、その上からアイコンを描画
                        if (GUI.Button(warnRect, GUIContent.none, GUIStyle.none))
                        {
                            ShowConflictPathsInAnimationWindow(clip, conflictEntries, activeRoot);
                        }
                        GUI.Label(warnRect, warnIcon);

                        warnX -= 26;
                    }

                    if (hasMissingBinding)
                    {
                        var missingRect = new Rect(warnX - 26, rect.y, 26, rowHeight);
                        var missingIcon = EditorGUIUtility.IconContent("sv_icon_dot12_pix16_gizmo");
                        if (missingIcon == null) missingIcon = new GUIContent("M", $"ルートオブジェクトから見て存在しない Transform パスにバインドされたキーがあります。(Missing パス数:{missingBindingCount}個)\nクリックで詳細と置換・削除");
                        else missingIcon.tooltip = $"ルートオブジェクトから見て存在しない Transform パスにバインドされたキーがあります。(Missing パス数:{missingBindingCount}個)\nクリックで詳細と置換・削除";

                        if (GUI.Button(missingRect, GUIContent.none, GUIStyle.none))
                        {
                            ShowMissingBindingsPopup(clip, activeRoot);
                            AnimationClipSelector.SetAnimationWindowToClip(clip);
                        }
                        GUI.Label(missingRect, missingIcon);

                        warnX -= 26;
                    }

                    var animatorContent = EditorGUIUtility.IconContent("AnimatorStateMachine Icon");
                    if (animatorContent == null) animatorContent = new GUIContent("A", "Animatorでの使用箇所を選択");
                    else animatorContent.tooltip = "Animatorでの使用箇所を選択";
                    if (GUI.Button(animatorRect, animatorContent))
                    {
                        // Auto Live Link は Animator.runtimeAnimatorController しか表示しないため、Playable Layer の Controller はアセット選択で開く
                        var usageRoot = controller == animator.runtimeAnimatorController ? animator.gameObject : null;
                        SelectUsageInAnimator(usageRoot, controller, clip, animatorScopePath);
                    }

                    if (GUI.Button(zoomRect, EditorGUIUtility.IconContent("AnimationClip Icon")))
                    {
                        EditorGUIUtility.PingObject(clip);
                        Selection.activeObject = clip;
                        EditorApplication.ExecuteMenuItem("Window/General/Project");
                    }

                    EditorGUILayout.EndVertical();
                }
            }
        }

        /// <summary>クリップが使用されているレイヤーインデックスの集合を取得（OverrideController対応）</summary>
        private static HashSet<int> GetLayerIndicesUsingClip(RuntimeAnimatorController controller, AnimationClip targetClip)
        {
            var layers = new HashSet<int>();
            AnimatorController baseController = null;
            Dictionary<AnimationClip, AnimationClip> overrideMap = null;

            if (controller is AnimatorOverrideController overrideController)
            {
                baseController = overrideController.runtimeAnimatorController as AnimatorController;
                if (baseController == null) return layers;
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
            if (baseController == null) return layers;

            AnimationClip ResolveClip(AnimationClip baseClip)
            {
                if (baseClip == null) return null;
                if (overrideMap != null && overrideMap.TryGetValue(baseClip, out var ov))
                    return ov;
                return baseClip;
            }

            for (int i = 0; i < baseController.layers.Length; i++)
            {
                if (StateMachineUsesClip(baseController.layers[i].stateMachine, targetClip, ResolveClip))
                    layers.Add(i);
            }
            return layers;
        }

        private static bool StateMachineUsesClip(AnimatorStateMachine sm, AnimationClip targetClip, System.Func<AnimationClip, AnimationClip> resolveClip)
        {
            if (sm == null) return false;
            foreach (var child in sm.states)
            {
                if (child.state?.motion == null) continue;
                var clips = new HashSet<AnimationClip>();
                CollectClipsFromMotion(child.state.motion, clips, resolveClip);
                if (clips.Contains(targetClip)) return true;
            }
            foreach (var child in sm.stateMachines)
            {
                if (child.stateMachine != null && StateMachineUsesClip(child.stateMachine, targetClip, resolveClip))
                    return true;
            }
            return false;
        }

        /// <summary>クリップが制御するバインディングパス数（同一パスは1としてカウント）</summary>
        private static int GetClipBindingPathCount(AnimationClip clip)
        {
            if (clip == null) return 0;
            var paths = new HashSet<string>();
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
                paths.Add(b.path);
            foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                paths.Add(b.path);
            return paths.Count;
        }

        private static void CollectClipsFromMotion(Motion motion, HashSet<AnimationClip> clips, System.Func<AnimationClip, AnimationClip> resolveClip)
        {
            if (motion == null) return;
            if (motion is AnimationClip clip)
            {
                var resolved = resolveClip(clip);
                if (resolved != null) clips.Add(resolved);
            }
            else if (motion is BlendTree blendTree)
            {
                foreach (var child in blendTree.children)
                {
                    CollectClipsFromMotion(child.motion, clips, resolveClip);
                }
            }
        }

        #endregion
    }
}
#endif
