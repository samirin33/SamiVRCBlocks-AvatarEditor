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
    public class AnimationClipBindingPathReplaceEditor : EditorWindow
    {
        private const string PrefsKeySourceMode = "SamirinEditorTools.AnimationClipBindingPathReplace.SourceMode";
        private const string PrefsKeyDirectoryPath = "SamirinEditorTools.AnimationClipBindingPathReplace.DirectoryPath";
        private const string PrefsKeyClipGuids = "SamirinEditorTools.AnimationClipBindingPathReplace.ClipGuids";
        private const string PrefsKeyPathFrom = "SamirinEditorTools.AnimationClipBindingPathReplace.PathFrom";
        private const string PrefsKeyPathTo = "SamirinEditorTools.AnimationClipBindingPathReplace.PathTo";
        private const string PrefsKeyPathToObject = "SamirinEditorTools.AnimationClipBindingPathReplace.PathToObject";

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
        private bool _loaded;
        private Transform _preferredRoot;

        [MenuItem("samirin33 Editor Tools/Animation/Animation Clip Binding Path Replace", false, 4)]
        public static void Open()
        {
            var w = GetWindow<AnimationClipBindingPathReplaceEditor>("Clip Binding Path Replace");
            w.minSize = new Vector2(400, 360);
        }

        /// <summary>
        /// Animation Clip Selector などから、Missing パス置換用にコンテキストを渡して開く。
        /// </summary>
        public static void OpenWithReplaceContext(IEnumerable<AnimationClip> clips, string pathFrom, Transform preferredRoot = null)
        {
            var w = GetWindow<AnimationClipBindingPathReplaceEditor>("Clip Binding Path Replace");
            w.minSize = new Vector2(400, 360);
            w.ApplyReplaceContext(clips, pathFrom, preferredRoot);
            w.Focus();
        }

        private void ApplyReplaceContext(IEnumerable<AnimationClip> clips, string pathFrom, Transform preferredRoot)
        {
            _loaded = true;
            _sourceMode = SourceMode.ClipArray;
            _preferredRoot = preferredRoot;
            _clipList.Clear();
            if (clips != null)
                _clipList.AddRange(clips.Where(c => c != null));
            _pathFrom = pathFrom ?? "";
            _pathTo = "";
            _pathToObject = null;
            SavePreferences();
            Repaint();
        }

        private void OnEnable()
        {
            LoadPreferences();
        }

        private void OnDisable()
        {
            SavePreferences();
        }

        private void OnGUI()
        {
            SamirinEditorStyleHelper.DrawWithBlueBackground(
                () =>
            {
                EditorGUILayout.Space(4);
                EditorGUI.BeginChangeCheck();
                _sourceMode = (SourceMode)EditorGUILayout.EnumPopup("対象の指定方法", _sourceMode);
                if (EditorGUI.EndChangeCheck())
                    SavePreferences();

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
            EditorGUI.BeginChangeCheck();
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
            if (EditorGUI.EndChangeCheck())
                SavePreferences();
        }

        private void DrawDirectory()
        {
            EditorGUILayout.LabelField("ディレクトリ指定", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            var newDir = (DefaultAsset)EditorGUILayout.ObjectField("フォルダ", _directoryAsset, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                _directoryAsset = newDir;
                _directoryScanned = false;
                SavePreferences();
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
                    {
                        ScanDirectory(path);
                        SavePreferences();
                    }
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

            EditorGUI.BeginChangeCheck();
            _pathFrom = EditorGUILayout.TextField("置換前のパス", _pathFrom);
            bool pathFromChanged = EditorGUI.EndChangeCheck();

            EditorGUI.BeginChangeCheck();
            _pathTo = EditorGUILayout.TextField("置換後のパス", _pathTo);
            bool pathToChanged = EditorGUI.EndChangeCheck();

            EditorGUI.BeginChangeCheck();
            _pathToObject = (Transform)EditorGUILayout.ObjectField("置換後のパス（オブジェクト指定）", _pathToObject, typeof(Transform), true);
            bool pathToObjectChanged = EditorGUI.EndChangeCheck();

            if (pathToObjectChanged)
                SyncPathToFromObject();
            else if (pathToChanged)
                SyncObjectFromPathTo();

            if (_pathToObject != null)
            {
                string calculatedPath = AnimationClipBindingPathUtility.GetPathFromAnimatorRoot(_pathToObject);
                if (string.IsNullOrEmpty(calculatedPath))
                    EditorGUILayout.HelpBox("親階層にAnimatorコンポーネントが見つかりませんでした。", MessageType.Warning);
            }

            if (pathFromChanged || pathToChanged || pathToObjectChanged)
                SavePreferences();
        }

        private void SyncPathToFromObject()
        {
            if (_pathToObject == null)
                return;

            string calculatedPath = AnimationClipBindingPathUtility.GetPathFromAnimatorRoot(_pathToObject);
            if (!string.IsNullOrEmpty(calculatedPath))
                _pathTo = calculatedPath;
        }

        private void SyncObjectFromPathTo()
        {
            if (string.IsNullOrEmpty(_pathTo))
            {
                _pathToObject = null;
                return;
            }

            _pathToObject = AnimationClipBindingPathUtility.TryResolveTransformFromPath(_pathTo, _preferredRoot);
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
            SavePreferences();
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

            SyncPathToFromObject();
            var replacedCount = AnimationClipBindingPathUtility.ReplacePathInClips(clips, _pathFrom, _pathTo);
            SavePreferences();
            Samirin33.SamirinVRCUtility.AvatarEditor.AnimationClipSelector.InvalidateAndRepaint();
            Debug.Log($"[AnimationClipBindingPathReplace] {clips.Count} クリップを処理しました。パス置換: {replacedCount} 件。");
        }

        private void LoadPreferences()
        {
            if (_loaded)
                return;

            _sourceMode = (SourceMode)EditorPrefs.GetInt(PrefsKeySourceMode, (int)SourceMode.Directory);
            _pathFrom = EditorPrefs.GetString(PrefsKeyPathFrom, "");
            _pathTo = EditorPrefs.GetString(PrefsKeyPathTo, "");

            RestoreClipList();
            RestoreDirectory();

            var pathToObjectId = EditorPrefs.GetString(PrefsKeyPathToObject, "");
            _pathToObject = LoadTransformFromGlobalObjectId(pathToObjectId);
            if (_pathToObject != null)
                SyncPathToFromObject();
            else if (!string.IsNullOrEmpty(_pathTo))
                SyncObjectFromPathTo();

            _loaded = true;
        }

        private void SavePreferences()
        {
            EditorPrefs.SetInt(PrefsKeySourceMode, (int)_sourceMode);
            EditorPrefs.SetString(PrefsKeyPathFrom, _pathFrom ?? "");
            EditorPrefs.SetString(PrefsKeyPathTo, _pathTo ?? "");
            EditorPrefs.SetString(PrefsKeyPathToObject, SaveTransformAsGlobalObjectId(_pathToObject));
            EditorPrefs.SetString(PrefsKeyClipGuids, SerializeClipGuids(_clipList));
            EditorPrefs.SetString(PrefsKeyDirectoryPath, GetDirectoryPath(_directoryAsset));
        }

        private void RestoreClipList()
        {
            _clipList.Clear();
            var guids = EditorPrefs.GetString(PrefsKeyClipGuids, "");
            if (string.IsNullOrEmpty(guids))
                return;

            foreach (var guid in guids.Split('|'))
            {
                if (string.IsNullOrEmpty(guid))
                    continue;

                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                    continue;

                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip != null)
                    _clipList.Add(clip);
            }
        }

        private void RestoreDirectory()
        {
            _directoryAsset = null;
            _directoryScanned = false;
            _directoryClips.Clear();

            var folderPath = EditorPrefs.GetString(PrefsKeyDirectoryPath, "");
            if (string.IsNullOrEmpty(folderPath))
                return;

            folderPath = folderPath.Replace("\\", "/").TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(folderPath))
                return;

            _directoryAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath);
            if (_directoryAsset != null)
                ScanDirectory(folderPath);
        }

        private static string SerializeClipGuids(IEnumerable<AnimationClip> clips)
        {
            var guids = clips
                .Where(c => c != null)
                .Select(c => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c)))
                .Where(guid => !string.IsNullOrEmpty(guid));
            return string.Join("|", guids);
        }

        private static string GetDirectoryPath(DefaultAsset directoryAsset)
        {
            if (directoryAsset == null)
                return "";

            var path = AssetDatabase.GetAssetPath(directoryAsset);
            return string.IsNullOrEmpty(path) ? "" : path.Replace("\\", "/").TrimEnd('/');
        }

        private static string SaveTransformAsGlobalObjectId(Transform transform)
        {
            if (transform == null)
                return "";

            try
            {
                return GlobalObjectId.GetGlobalObjectIdSlow(transform).ToString();
            }
            catch
            {
                return "";
            }
        }

        private static Transform LoadTransformFromGlobalObjectId(string globalObjectIdString)
        {
            if (string.IsNullOrEmpty(globalObjectIdString))
                return null;

            if (!GlobalObjectId.TryParse(globalObjectIdString, out var globalObjectId))
                return null;

            var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalObjectId);
            return obj as Transform;
        }
    }
}
