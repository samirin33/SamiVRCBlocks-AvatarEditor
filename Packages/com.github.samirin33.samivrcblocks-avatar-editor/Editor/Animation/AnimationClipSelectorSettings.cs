#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Samirin33.SamirinVRCUtility.AvatarEditor
{
    [Serializable]
    public class ControllerClipEntry
    {
        public string controllerPath;
        public AnimationClip clip;
    }

    [Serializable]
    public class ControllerFoldoutStateEntry
    {
        public string controllerPath;
        public List<string> collapsedKeys = new List<string>();
    }

    /// <summary>
    /// Animation Clip Selector の表示設定とAnimator毎の最後に表示したクリップを保存するアセット。
    /// </summary>
    public class AnimationClipSelectorSettings : ScriptableObject
    {
        [SerializeField] private float _itemSpacing = 2f;
        [SerializeField] private bool _showBlendTreeNested = true;
        [SerializeField] private List<ControllerClipEntry> _lastDisplayedClipPerController = new List<ControllerClipEntry>();
        [SerializeField] private List<AnimationClip> _ignoreClips = new List<AnimationClip>();
        [SerializeField] private List<string> _defaultIgnoreGUIDs = new List<string> { "4de039275b65be24c8f0a641d7a44924" };
        [SerializeField] private List<ControllerFoldoutStateEntry> _foldoutStates = new List<ControllerFoldoutStateEntry>();

        public float ItemSpacing
        {
            get => _itemSpacing;
            set => _itemSpacing = Mathf.Clamp(value, 0f, 16f);
        }

        /// <summary>BlendTree を入れ子の折りたたみグループとして表示するか。</summary>
        public bool ShowBlendTreeNested
        {
            get => _showBlendTreeNested;
            set => _showBlendTreeNested = value;
        }

        /// <summary>競合警告を出さない AnimationClip 一覧。</summary>
        public IReadOnlyList<AnimationClip> IgnoreClips => _ignoreClips;

        /// <summary>指定クリップが競合警告の対象外かどうか。</summary>
        public bool IsIgnoredClip(AnimationClip clip)
        {
            if (clip == null) return false;
            if (_ignoreClips.Contains(clip)) return true;
            var path = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(path)) return false;
            var guid = AssetDatabase.AssetPathToGUID(path);
            return !string.IsNullOrEmpty(guid) && _defaultIgnoreGUIDs.Contains(guid);
        }

        public AnimationClip GetLastDisplayedClip(string controllerPath)
        {
            if (string.IsNullOrEmpty(controllerPath)) return null;
            var entry = _lastDisplayedClipPerController.Find(e => e.controllerPath == controllerPath);
            return entry?.clip;
        }

        public void SetLastDisplayedClip(string controllerPath, AnimationClip clip)
        {
            if (string.IsNullOrEmpty(controllerPath)) return;
            var entry = _lastDisplayedClipPerController.Find(e => e.controllerPath == controllerPath);
            if (entry != null)
            {
                entry.clip = clip;
            }
            else
            {
                _lastDisplayedClipPerController.Add(new ControllerClipEntry { controllerPath = controllerPath, clip = clip });
            }

            // 毎フレームの SaveAssets 呼び出しは避け、Dirty フラグのみ立てる
            EditorUtility.SetDirty(this);
        }

        /// <summary>指定コントローラーのフォールダウン状態が保存済みかどうか。</summary>
        public bool HasFoldoutState(string controllerPath)
        {
            if (string.IsNullOrEmpty(controllerPath)) return false;
            return _foldoutStates.Exists(e => e.controllerPath == controllerPath);
        }

        /// <summary>指定コントローラーに対して、折りたたまれているフォルダキー一覧を取得。</summary>
        public IReadOnlyList<string> GetCollapsedFoldoutKeys(string controllerPath)
        {
            if (string.IsNullOrEmpty(controllerPath)) return Array.Empty<string>();
            var entry = _foldoutStates.Find(e => e.controllerPath == controllerPath);
            return entry?.collapsedKeys ?? (IReadOnlyList<string>)Array.Empty<string>();
        }

        /// <summary>指定コントローラーに対して、折りたたまれているフォルダキー一覧を保存。</summary>
        public void SetCollapsedFoldoutKeys(string controllerPath, IEnumerable<string> collapsedKeys)
        {
            if (string.IsNullOrEmpty(controllerPath)) return;
            var entry = _foldoutStates.Find(e => e.controllerPath == controllerPath);
            if (entry == null)
            {
                entry = new ControllerFoldoutStateEntry { controllerPath = controllerPath };
                _foldoutStates.Add(entry);
            }

            entry.collapsedKeys.Clear();
            if (collapsedKeys != null)
            {
                entry.collapsedKeys.AddRange(collapsedKeys);
            }

            EditorUtility.SetDirty(this);
        }
    }
}
#endif
