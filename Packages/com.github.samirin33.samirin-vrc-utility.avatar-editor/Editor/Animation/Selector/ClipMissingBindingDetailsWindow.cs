#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Samirin33.Editor;
using Samirin33.AvatarEditor.Animation.Editor;

namespace Samirin33.SamirinVRCUtility.AvatarEditor
{
    /// <summary>
    /// Missing バインディングパスの詳細と、置換ツール連携・一括削除を行うポップアップ。
    /// </summary>
    internal class ClipMissingBindingDetailsWindow : EditorWindow
    {
        private AnimationClip _clip;
        private GameObject _root;
        private List<AnimationClip> _controllerClips = new List<AnimationClip>();
        private List<string> _missingPaths = new List<string>();
        private Vector2 _scroll;

        public static void Open(AnimationClip clip, GameObject root, IReadOnlyList<AnimationClip> controllerClips = null)
        {
            if (clip == null || root == null)
                return;

            var window = GetWindow<ClipMissingBindingDetailsWindow>("Clip Missing Bindings");
            window._clip = clip;
            window._root = root;
            window._controllerClips = controllerClips?
                .Where(c => c != null)
                .Distinct()
                .ToList() ?? new List<AnimationClip> { clip };
            if (!window._controllerClips.Contains(clip))
                window._controllerClips.Insert(0, clip);
            window.RefreshMissingPaths();
            window.minSize = new Vector2(460, 280);
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += RefreshMissingPaths;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= RefreshMissingPaths;
        }

        private void RefreshMissingPaths()
        {
            if (_clip == null || _root == null)
            {
                _missingPaths.Clear();
                return;
            }

            _missingPaths = AnimationClipBindingPathUtility
                .GetMissingBindingPaths(_root.transform, _clip)
                .ToList();
            Repaint();
            AnimationClipSelector.InvalidateAndRepaint();
        }

        private List<AnimationClip> GetClipsWithMissingPath(string path)
        {
            return AnimationClipBindingPathUtility.GetClipsWithMissingPath(
                _root.transform,
                _controllerClips,
                path);
        }

        private void OnGUI()
        {
            SamirinEditorStyleHelper.DrawWithBlueBackground(() =>
            {
                EditorGUILayout.LabelField("Missing バインディング", EditorStyles.boldLabel);

                if (_clip != null)
                {
                    var clipStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Italic };
                    EditorGUILayout.LabelField("選択 Clip: " + _clip.name, clipStyle);
                }

                if (_root != null)
                    EditorGUILayout.LabelField("Root: " + _root.name, EditorStyles.miniLabel);

                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(
                    "ルートオブジェクトから見つからない Transform パスにバインドされたキーです。\n" +
                    "同じ Missing パスを複数 Clip が持つ場合、全 Clip をまとめて置換・削除できます。",
                    MessageType.Info);

                EditorGUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    // if (GUILayout.Button("Binding Path Replace を開く（選択 Clip）", GUILayout.Height(24)))
                    // {
                    //     OpenPathReplace(null, new[] { _clip });
                    // }

                    EditorGUI.BeginDisabledGroup(_missingPaths.Count == 0);
                    if (GUILayout.Button("選択 Clip の Missing をすべて削除", GUILayout.Height(24)))
                    {
                        RemoveAllMissingBindings(new[] { _clip });
                    }
                    EditorGUI.EndDisabledGroup();
                }

                EditorGUILayout.Space(6);

                if (_missingPaths.Count == 0)
                {
                    SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont("Missing バインディングはありません。", MessageType.Info);
                    return;
                }

                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                var removedPaths = new List<string>();
                foreach (var path in _missingPaths)
                {
                    if (DrawMissingPathEntry(path))
                        removedPaths.Add(path);
                    EditorGUILayout.Space(4);
                }

                foreach (var path in removedPaths)
                    _missingPaths.Remove(path);

                EditorGUILayout.EndScrollView();
            }, new Rect(0, 0, position.width, position.height));
        }

        private bool DrawMissingPathEntry(string path)
        {
            var removed = false;
            var affectedClips = GetClipsWithMissingPath(path);
            var otherClipCount = affectedClips.Count(c => c != _clip);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            SamirinEditorStyleHelper.DrawWithDefaultFont(() =>
            {
                var lastName = string.IsNullOrEmpty(path) ? "(root)" : path.Split('/').LastOrDefault() ?? path;
                EditorGUILayout.LabelField(lastName, EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(path, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));

                if (affectedClips.Count > 1)
                {
                    var clipNames = string.Join(", ", affectedClips.Select(c => c.name));
                    EditorGUILayout.HelpBox(
                        $"同じ Missing パスを持つ Clip: {affectedClips.Count} 件\n{clipNames}",
                        MessageType.None);
                }

                EditorGUILayout.LabelField("一括操作（同パスを含む全 Clip）", EditorStyles.miniBoldLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button($"全 {affectedClips.Count} Clip で置換", GUILayout.MinWidth(130)))
                {
                    OpenPathReplace(path, affectedClips);
                }

                if (GUILayout.Button($"全 {affectedClips.Count} Clip から削除", GUILayout.MinWidth(150)))
                {
                    if (RemoveBindingsAtPathFromClips(affectedClips, path))
                        removed = true;
                }
                EditorGUILayout.EndHorizontal();

                if (otherClipCount > 0)
                {
                    EditorGUILayout.LabelField("選択 Clip のみ", EditorStyles.miniBoldLabel);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("選択 Clip で置換", GUILayout.MinWidth(130)))
                    {
                        OpenPathReplace(path, new[] { _clip });
                    }

                    if (GUILayout.Button("選択 Clip から削除", GUILayout.MinWidth(130)))
                    {
                        if (RemoveBindingsAtPath(_clip, path))
                            removed = true;
                    }
                    EditorGUILayout.EndHorizontal();
                }
            });

            EditorGUILayout.EndVertical();
            return removed;
        }

        private void OpenPathReplace(string pathFrom, IEnumerable<AnimationClip> clips)
        {
            var targetClips = clips?.Where(c => c != null).Distinct().ToList() ?? new List<AnimationClip>();
            if (targetClips.Count == 0 && _clip != null)
                targetClips.Add(_clip);

            AnimationClipBindingPathReplaceEditor.OpenWithReplaceContext(
                targetClips,
                pathFrom,
                _root != null ? _root.transform : null);
            AnimationClipSelector.SetAnimationWindowToClip(_clip);
        }

        private bool RemoveBindingsAtPath(AnimationClip clip, string path)
        {
            if (clip == null || string.IsNullOrEmpty(path))
                return false;

            var removed = AnimationClipBindingPathUtility.RemoveBindingsAtPath(clip, path);
            if (removed <= 0)
                return false;

            AssetDatabase.SaveAssets();
            AnimationClipSelector.InvalidateAndRepaint();
            return true;
        }

        private bool RemoveBindingsAtPathFromClips(IReadOnlyList<AnimationClip> clips, string path)
        {
            if (clips == null || clips.Count == 0 || string.IsNullOrEmpty(path))
                return false;

            var removed = AnimationClipBindingPathUtility.RemoveBindingsAtPathFromClips(clips, path);
            if (removed <= 0)
                return false;

            RefreshMissingPaths();
            Debug.Log($"[AnimationClipSelector] Missing パス「{path}」のバインディングを {clips.Count} Clip から {removed} 件削除しました。");
            return true;
        }

        private void RemoveAllMissingBindings(IEnumerable<AnimationClip> clips)
        {
            if (_root == null)
                return;

            var clipList = clips?.Where(c => c != null).Distinct().ToList();
            if (clipList == null || clipList.Count == 0)
                return;

            var removed = AnimationClipBindingPathUtility.RemoveMissingBindingsFromClips(_root.transform, clipList);
            if (removed <= 0)
                return;

            RefreshMissingPaths();
            Debug.Log($"[AnimationClipSelector] {clipList.Count} Clip から Missing バインディング {removed} 件を削除しました。");
        }
    }
}
#endif
