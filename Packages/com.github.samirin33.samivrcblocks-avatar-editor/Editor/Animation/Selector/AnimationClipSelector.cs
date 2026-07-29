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
    public class AnimationClipSelector : EditorWindow, ISerializationCallbackReceiver
    {
        [SerializeField] private Vector2 _scrollPosition;
        private List<AnimationClip> _clips = new List<AnimationClip>();
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
        internal const string SettingsAssetPath = "Assets/SamiVRCBlocksAvatar/Editor/AnimationClipSelectorSettings.asset";

        private AnimationClipSelectorSettings _settings;

        private AnimationClipSelectorSettings Settings
        {
            get
            {
                if (_settings == null)
                    _settings = LoadOrCreateSettings();
                return _settings;
            }
        }

        [MenuItem("SamiVRCBlocks-AvatarEditor/Animation/Animation Clip Selector", false, 2)]
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
            _settings = LoadOrCreateSettings();
            AnimationClipSelectorStateManager.SetSettingsInstance(_settings);
            Undo.undoRedoPerformed += OnUndoRedoPerformed;

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
            if (!string.IsNullOrEmpty(_currentControllerPath))
            {
                AnimationClipSelectorStateManager.SaveFoldoutState(_currentControllerPath, _layerFoldoutState);
            }
            AnimationClipSelectorStateManager.FlushPendingSave();
            SaveSettings();
            _pathConflictCache.Clear();
            _pathConflictCachePending = false;
        }

        private static void OnUndoRedoPerformed()
        {
            _pathConflictCacheInvalidated = true;
        }

        #region 設定・Animation ウィンドウ

        private static AnimationClipSelectorSettings LoadOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<AnimationClipSelectorSettings>(SettingsAssetPath);
            if (settings != null) return settings;

            var dir = Path.GetDirectoryName(SettingsAssetPath)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder("Assets/SamiVRCBlocksAvatar"))
            {
                AssetDatabase.CreateFolder("Assets", "SamiVRCBlocksAvatar");
            }
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder("Assets/SamiVRCBlocksAvatar/Editor"))
            {
                AssetDatabase.CreateFolder("Assets/SamiVRCBlocksAvatar", "Editor");
            }

            settings = CreateInstance<AnimationClipSelectorSettings>();
            settings.ItemSpacing = 2f;
            settings.ShowBlendTreeNested = false;
            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        private void SaveSettings()
        {
            if (_settings != null)
            {
                EditorUtility.SetDirty(_settings);
                AssetDatabase.SaveAssets();
            }
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
                if (animator == null || animator.runtimeAnimatorController == null)
                {
                    if (activeRoot == _lastActiveRoot)
                        _lastActiveRoot = null;
                    EditorGUILayout.HelpBox(
                        "AnimatorまたはAnimator Controllerが見つかりません。\nAnimatorコンポーネントとControllerが設定されたオブジェクトを選択してください。",
                        MessageType.Warning);
                    _clips.Clear();
                    return;
                }

                _lastActiveRoot = activeRoot;

                _clips = GetAllAnimationClips(animator.runtimeAnimatorController);
                if (_clips.Count == 0)
                {
                    EditorGUILayout.HelpBox("Animator ControllerにAnimationClipが含まれていません。", MessageType.Info);
                    return;
                }

                EditorGUI.BeginChangeCheck();
                var controllerPath = GetControllerPath(animator.runtimeAnimatorController);
                var clipsByLayer = GetClipsByGroupedLayer(animator.runtimeAnimatorController, Settings.ShowBlendTreeNested);

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
                    ReloadSelectorList(animator, controllerPath);
                    clipsByLayer = GetClipsByGroupedLayer(animator.runtimeAnimatorController, Settings.ShowBlendTreeNested);
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
                    var capturedController = animator.runtimeAnimatorController;
                    var capturedPath = controllerPath;
                    var capturedClips = new List<AnimationClip>(_clips);
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
                        DrawClipGroup(rootGroup, controllerPath, layerName, activeRoot, animator, activeClip, Settings, Settings.ItemSpacing, _layerFoldoutState, _searchFilter, !isAnimationWindowOpen, getPathConflict, ref scrollContentY);
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

        private void ReloadSelectorList(Animator animator, string controllerPath)
        {
            if (animator == null || animator.runtimeAnimatorController == null)
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

            _clips = GetAllAnimationClips(animator.runtimeAnimatorController);
            _pathConflictCache.Clear();
            _pathConflictCacheControllerPath = controllerPath;
            _pathConflictCachePending = false;
            _pathConflictCacheInvalidated = false;
            _pathConflictCache = BuildPathConflictCache(animator.runtimeAnimatorController);
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

            var animator = GetAnimatorFromRoot(root);
            var controllerClips = animator?.runtimeAnimatorController != null
                ? GetAllAnimationClips(animator.runtimeAnimatorController)
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

                        conflictCount++;
                        var parts = key.Split('|');
                        var path = parts.Length > 0 ? parts[0] : "";
                        var property = parts.Length > 1 ? parts[1] : "";
                        var typeName = parts.Length > 2 ? parts[2] : "";
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
            var w = GetWindow<AnimationClipSelector>(false);
            if (w != null) w.Repaint();
        }

        /// <summary>Selector の表示を更新する。Missing バインディング操作後などに呼ぶ。</summary>
        internal static void InvalidateAndRepaint()
        {
            InvalidatePathConflictCache();
        }

        /// <summary>指定クリップの競合エントリ一覧を取得。root から Controller を解決してキャッシュを構築する。Undo 後の詳細ウィンドウ更新用。</summary>
        internal static ConflictEntry[] GetConflictEntriesForClip(AnimationClip clip, GameObject root)
        {
            if (clip == null || root == null) return Array.Empty<ConflictEntry>();
            var animator = root.GetComponentInParent<Animator>();
            if (animator == null) animator = root.GetComponentInChildren<Animator>();
            var controller = animator?.runtimeAnimatorController;
            if (controller == null) return Array.Empty<ConflictEntry>();
            var cache = BuildPathConflictCache(controller);
            return cache.TryGetValue(clip.GetInstanceID(), out var v) ? v.conflictEntries : Array.Empty<ConflictEntry>();
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

        private static void DrawClipGroup(ClipGroup group, string controllerPath, string pathPrefix, GameObject activeRoot, Animator animator, AnimationClip activeClip, AnimationClipSelectorSettings settings, float itemSpacing, Dictionary<string, bool> foldoutState, string searchFilter, bool openAnimationWindowOnClipSelect, System.Func<AnimationClip, (bool hasConflict, int pathCount, ConflictEntry[] conflictEntries)?> getPathConflict, ref float scrollContentY)
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
                        DrawClipGroup(child, controllerPath, childPath, activeRoot, animator, activeClip, settings, itemSpacing, foldoutState, searchFilter, openAnimationWindowOnClipSelect, getPathConflict, ref scrollContentY);
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
                        SelectUsageInAnimator(animator.gameObject, animator.runtimeAnimatorController, clip, animatorScopePath);

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

        #endregion

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
