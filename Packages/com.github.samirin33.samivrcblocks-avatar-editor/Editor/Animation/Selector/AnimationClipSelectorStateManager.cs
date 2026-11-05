#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Samirin33.AvatarEditor.Animation.Editor;

namespace Samirin33.SamirinVRCUtility.AvatarEditor
{
    /// <summary>
    /// AnimationClipSelector のフォールダウン状態と最後に選択したクリップを Animator(Controller) 毎に
    /// 永続化し、次回読み込み時に復元するマネージャー。
    /// <para>
    /// [InitializeOnLoad] により、AnimationClipSelector ウィンドウが開いていなくても
    /// Animator 選択時に最後のクリップを Animation ウィンドウに復元する。
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    internal static class AnimationClipSelectorStateManager
    {
        private static AnimationClipSelectorSettings _cachedSettings;
        private static bool _pendingSave;
        private static double _pendingSaveRequestTime;
        private const double SaveDebounceSeconds = 3.0;
        private static string _lastRestoredControllerPath;

        static AnimationClipSelectorStateManager()
        {
            EditorApplication.update += OnEditorUpdate;
            Selection.selectionChanged += OnSelectionChanged;
        }

        private static AnimationClipSelectorSettings GetSettings()
        {
            if (_cachedSettings == null)
                _cachedSettings = AssetDatabase.LoadAssetAtPath<AnimationClipSelectorSettings>(
                    AnimationClipSelector.SettingsAssetPath);
            return _cachedSettings;
        }

        /// <summary>Settings インスタンスを外部から設定する（AnimationClipSelector.OnEnable で呼ぶ）。</summary>
        internal static void SetSettingsInstance(AnimationClipSelectorSettings settings)
        {
            _cachedSettings = settings;
        }

        #region フォールダウン状態

        /// <summary>
        /// 指定コントローラーのフォールダウン状態を Settings から Dictionary に復元する。
        /// 保存済みの状態がなければ何もしない（デフォルトの全展開を維持）。
        /// 折りたたまれていたキーを false として設定する。
        /// </summary>
        public static void RestoreFoldoutState(string controllerPath, Dictionary<string, bool> foldoutState)
        {
            if (string.IsNullOrEmpty(controllerPath) || foldoutState == null) return;
            var settings = GetSettings();
            if (settings == null) return;
            if (!settings.HasFoldoutState(controllerPath)) return;

            var collapsedKeys = settings.GetCollapsedFoldoutKeys(controllerPath);
            foreach (var key in collapsedKeys)
            {
                foldoutState[key] = false;
            }
        }

        /// <summary>
        /// 現在のフォールダウン状態を Settings に保存する（Dirty フラグのみ）。
        /// 実際のディスク書き込みは <see cref="RequestSave"/> 経由のデバウンス処理か
        /// <see cref="FlushPendingSave"/> で行う。
        /// </summary>
        public static void SaveFoldoutState(string controllerPath, Dictionary<string, bool> foldoutState)
        {
            if (string.IsNullOrEmpty(controllerPath) || foldoutState == null) return;
            var settings = GetSettings();
            if (settings == null) return;

            var prefix = controllerPath + "|";
            var collapsedKeys = foldoutState
                .Where(kvp => kvp.Key.StartsWith(prefix) && !kvp.Value)
                .Select(kvp => kvp.Key)
                .ToList();

            settings.SetCollapsedFoldoutKeys(controllerPath, collapsedKeys);
        }

        #endregion

        #region 最後に選択したクリップ

        /// <summary>指定コントローラーの最後に選択したクリップを取得する。</summary>
        public static AnimationClip GetLastSelectedClip(string controllerPath)
        {
            var settings = GetSettings();
            return settings?.GetLastDisplayedClip(controllerPath);
        }

        /// <summary>
        /// クリップ選択を記録する。同じクリップが既に記録済みなら何もしない。
        /// Settings を Dirty にし、デバウンス保存をリクエストする。
        /// </summary>
        public static void NotifyClipSelected(string controllerPath, AnimationClip clip)
        {
            if (string.IsNullOrEmpty(controllerPath) || clip == null) return;
            var settings = GetSettings();
            if (settings == null) return;

            var current = settings.GetLastDisplayedClip(controllerPath);
            if (current == clip) return;

            settings.SetLastDisplayedClip(controllerPath, clip);
            RequestSave();
        }

        #endregion

        #region 保存管理

        /// <summary>デバウンスされた保存をリクエストする。</summary>
        public static void RequestSave()
        {
            _pendingSave = true;
            _pendingSaveRequestTime = EditorApplication.timeSinceStartup;
        }

        /// <summary>保留中の保存を即座に実行する（ウィンドウ閉鎖時など）。</summary>
        public static void FlushPendingSave()
        {
            if (!_pendingSave) return;
            _pendingSave = false;
            var settings = GetSettings();
            if (settings != null)
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }
        }

        private static void OnEditorUpdate()
        {
            if (!_pendingSave) return;
            if (EditorApplication.timeSinceStartup - _pendingSaveRequestTime < SaveDebounceSeconds) return;

            _pendingSave = false;
            var settings = GetSettings();
            if (settings != null)
                AssetDatabase.SaveAssets();
        }

        #endregion

        #region 自動クリップ復元（ウィンドウ不要）

        /// <summary>
        /// ヒエラルキーで Animator を持つオブジェクトが選択されたとき、
        /// そのコントローラーに対して最後に選択されていたクリップを Animation ウィンドウに復元する。
        /// </summary>
        private static void OnSelectionChanged()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                _lastRestoredControllerPath = null;
                return;
            }

            var animator = go.GetComponent<Animator>();
            if (animator == null) animator = go.GetComponentInParent<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                _lastRestoredControllerPath = null;
                return;
            }

            var controllerPath = GetControllerPath(animator.runtimeAnimatorController);
            if (string.IsNullOrEmpty(controllerPath)) return;

            if (controllerPath == _lastRestoredControllerPath) return;
            _lastRestoredControllerPath = controllerPath;

            var lastClip = GetLastSelectedClip(controllerPath);
            if (lastClip == null) return;

            EditorApplication.delayCall += () =>
            {
                var animWindow = AnimationWindowHelper.GetAnimationWindow();
                if (animWindow != null)
                    AnimationWindowHelper.SetAnimationWindowClip(lastClip);
            };
        }

        private static string GetControllerPath(RuntimeAnimatorController controller)
        {
            if (controller == null) return null;
            var animController = controller is AnimatorOverrideController ov
                ? ov.runtimeAnimatorController as AnimatorController
                : controller as AnimatorController;
            return animController != null ? AssetDatabase.GetAssetPath(animController) : null;
        }

        #endregion
    }
}
#endif
