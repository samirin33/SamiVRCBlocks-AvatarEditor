using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Samirin33.Editor;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    /// <summary>
    /// アニメーションクリップ内のバインドパスを一括でリネームするエディタウィンドウ
    /// </summary>
    public class AnimationClipPathReplaceEditor : EditorWindow
    {
        private enum SourceMode
        {
            ClipArray,
            Directory
        }

        private SourceMode _sourceMode = SourceMode.Directory;
        private readonly List<AnimationClip> _clipList = new List<AnimationClip>();
        private DefaultAsset _directoryAsset;
        private readonly List<AnimationClip> _directoryClips = new List<AnimationClip>();
        private string _pathFrom = "";
        private string _pathTo = "";
        private Transform _pathToObject;
        private Vector2 _clipScroll;
        private bool _directoryScanned;

        private struct PathReplaceRule
        {
            public string from;
            public string to;
        }

        [MenuItem("samirin33 Editor Tools/Animation Clip Path Replace")]
        public static void Open()
        {
            var w = GetWindow<AnimationClipPathReplaceEditor>("Anim Path Replace");
            w.minSize = new Vector2(400, 360);
        }

        private void OnGUI()
        {
            SamirinEditorStyleHelper.DrawWithBlueBackground(
                () =>
            {
                EditorGUILayout.Space(4);
                _sourceMode = (SourceMode)EditorGUILayout.EnumPopup("対象の指定方法", _sourceMode);
                EditorGUILayout.Space(4);

                if (_sourceMode == SourceMode.ClipArray)
                {
                    DrawClipArray();
                }
                else
                {
                    DrawDirectory();
                }

                EditorGUILayout.Space(8);
                DrawRules();
                EditorGUILayout.Space(8);
                DrawExecute();
            }, new Rect(0, 0, position.width, position.height));
        }

        private void DrawClipArray()
        {
            EditorGUILayout.LabelField("アニメーションクリップ配列", EditorStyles.boldLabel);
            _clipScroll = EditorGUILayout.BeginScrollView(_clipScroll, GUILayout.Height(120));
            for (int i = 0; i < _clipList.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                _clipList[i] = (AnimationClip)EditorGUILayout.ObjectField(_clipList[i], typeof(AnimationClip), false);
                if (GUILayout.Button("−", GUILayout.Width(24)))
                {
                    _clipList.RemoveAt(i);
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("+ クリップを追加"))
                _clipList.Add(null);
        }

        private void DrawDirectory()
        {
            EditorGUILayout.LabelField("ディレクトリ指定", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            var newDir = (DefaultAsset)EditorGUILayout.ObjectField("フォルダ", _directoryAsset, typeof(DefaultAsset), false);
            if (newDir != _directoryAsset)
            {
                _directoryAsset = newDir;
                _directoryScanned = false;
            }
            EditorGUILayout.EndHorizontal();

            if (_directoryAsset != null)
            {
                string path = AssetDatabase.GetAssetPath(_directoryAsset);
                if (!AssetDatabase.IsValidFolder(path) && !System.IO.Directory.Exists(path))
                {
                    EditorGUILayout.HelpBox("有効なフォルダを指定してください。", MessageType.Warning);
                }
                else
                {
                    if (GUILayout.Button("このフォルダ以下の全Clipを取得"))
                        ScanDirectory(path);
                    if (_directoryScanned)
                        EditorGUILayout.LabelField($"見つかったClip: {_directoryClips.Count} 件");
                }
            }
        }

        private void ScanDirectory(string folderPath)
        {
            _directoryClips.Clear();
            string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { folderPath });
            foreach (string guid in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
                if (clip != null)
                    _directoryClips.Add(clip);
            }
            _directoryScanned = true;
        }

        private void DrawRules()
        {
            EditorGUILayout.LabelField("パス置換", EditorStyles.boldLabel);
            _pathFrom = EditorGUILayout.TextField("置換前のパス", _pathFrom);

            EditorGUILayout.BeginHorizontal();
            _pathTo = EditorGUILayout.TextField("置換後のパス", _pathTo);
            EditorGUILayout.EndHorizontal();

            _pathToObject = (Transform)EditorGUILayout.ObjectField("置換後のパス（オブジェクト指定）", _pathToObject, typeof(Transform), true);
            if (_pathToObject != null)
            {
                string calculatedPath = GetPathFromAnimatorRoot(_pathToObject);
                if (!string.IsNullOrEmpty(calculatedPath))
                {
                    EditorGUILayout.HelpBox($"計算されたパス: {calculatedPath}", MessageType.Info);
                    if (GUILayout.Button("計算されたパスを適用"))
                    {
                        _pathTo = calculatedPath;
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("親階層にAnimatorコンポーネントが見つかりませんでした。", MessageType.Warning);
                }
            }
        }

        private void DrawExecute()
        {
            var clips = GetTargetClips();
            bool hasClips = clips != null && clips.Count > 0;
            bool hasPathRule = !string.IsNullOrEmpty(_pathFrom);
            bool hasRules = hasPathRule;

            if (!hasClips)
                EditorGUILayout.HelpBox("対象のアニメーションクリップを指定してください。", MessageType.Info);
            if (!hasRules)
                EditorGUILayout.HelpBox("パス置換を指定してください。", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(!hasClips || !hasRules);
            if (GUILayout.Button("パスを置換して保存", GUILayout.Height(32)))
            {
                ExecuteReplace(clips);
            }
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button("ルールをクリア", GUILayout.Height(32), GUILayout.Width(120)))
            {
                ClearAllRules();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void ClearAllRules()
        {
            _pathFrom = "";
            _pathTo = "";
            _pathToObject = null;
        }

        private List<AnimationClip> GetTargetClips()
        {
            if (_sourceMode == SourceMode.ClipArray)
                return _clipList.Where(c => c != null).ToList();
            return _directoryScanned ? new List<AnimationClip>(_directoryClips) : null;
        }

        private void ExecuteReplace(List<AnimationClip> clips)
        {
            if (clips == null || clips.Count == 0) return;

            // オブジェクトが指定されている場合は、そこから計算したパスを使用
            string pathToValue = _pathTo;
            if (_pathToObject != null)
            {
                string calculatedPath = GetPathFromAnimatorRoot(_pathToObject);
                if (!string.IsNullOrEmpty(calculatedPath))
                {
                    pathToValue = calculatedPath;
                }
            }

            var pathRules = !string.IsNullOrEmpty(_pathFrom)
                ? new List<PathReplaceRule> { new PathReplaceRule { from = _pathFrom, to = pathToValue ?? "" } }
                : new List<PathReplaceRule>();

            int replacedCount = 0;
            var clipArray = clips.Where(c => c != null).ToArray();
            if (clipArray.Length > 0)
            {
                Undo.RegisterCompleteObjectUndo(clipArray, "Animation Clip Path Replace");
            }

            foreach (var clip in clips)
            {
                if (clip == null) continue;
                if (pathRules.Count > 0)
                    replacedCount += ReplacePathsInClip(clip, pathRules);
            }

            foreach (var clip in clips)
            {
                if (clip != null)
                    EditorUtility.SetDirty(clip);
            }
            AssetDatabase.SaveAssets();

            Debug.Log($"[AnimationClipPathReplace] {clips.Count} クリップを処理しました。パス置換: {replacedCount} 件。");
        }

        private static int ReplacePathsInClip(AnimationClip clip, List<PathReplaceRule> rules)
        {
            int count = 0;

            // Float/Int curves
            var curveBindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var binding in curveBindings)
            {
                string newPath = ApplyRules(binding.path, rules);
                if (newPath == binding.path) continue;

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                AnimationUtility.SetEditorCurve(clip, binding, null);
                var newBinding = binding;
                newBinding.path = newPath;
                AnimationUtility.SetEditorCurve(clip, newBinding, curve);
                count++;
            }

            // Object reference curves (e.g. Sprite, Material)
            var objBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            foreach (var binding in objBindings)
            {
                string newPath = ApplyRules(binding.path, rules);
                if (newPath == binding.path) continue;

                var keyframes = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                var newBinding = binding;
                newBinding.path = newPath;
                AnimationUtility.SetObjectReferenceCurve(clip, newBinding, keyframes);
                count++;
            }

            return count;
        }

        private static string ApplyRules(string path, List<PathReplaceRule> rules)
        {
            string result = path;
            foreach (var rule in rules)
                if (!string.IsNullOrEmpty(rule.from))
                    result = result.Replace(rule.from, rule.to ?? "");
            return result;
        }

        /// <summary>
        /// 指定されたTransformから親方向に検索して、最初に見つかったAnimatorコンポーネントを持つオブジェクトからのパスを取得する。
        /// </summary>
        private static string GetPathFromAnimatorRoot(Transform transform)
        {
            if (transform == null) return null;

            // 親方向に検索してAnimatorを見つける
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

            if (animatorRoot == null) return null;

            // animatorRootからtransformまでのパスを計算
            return GetTransformPath(transform, animatorRoot);
        }

        /// <summary> 階層パスを取得。pathRoot を指定するとそのオブジェクトからの相対パスになる（クリップのパスが Animator 基準の場合に使用）。 </summary>
        private static string GetTransformPath(Transform transform, Transform pathRoot = null)
        {
            if (transform == null) return null;
            var parts = new List<string>();
            var t = transform;
            if (pathRoot != null)
            {
                while (t != null && t != pathRoot)
                {
                    parts.Add(t.name);
                    t = t.parent;
                }
                if (t != pathRoot) return null; // pathRoot の子孫でない
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
    }
}
