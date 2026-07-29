using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Samirin33.Editor;

namespace Samirin33.AvatarEditor.Tools.Editor
{
    /// <summary>
    /// 画像アセットのリサイズ・形式変換・圧縮を行うエディタウィンドウ。
    /// 複数選択時は一括設定しつつ、必要に応じて個別調整できます。
    /// </summary>
    public class ImageCompressor : EditorWindow
    {
        public enum OutputFormat
        {
            KeepOriginal = 0,
            PNG = 1,
            JPG = 2,
        }

        public enum SaveMode
        {
            Overwrite = 0,
            SaveAsCopy = 1,
        }

        [Serializable]
        class ImageEntry
        {
            public string assetPath;
            public Texture2D texture;
            public bool enabled = true;
            public bool foldout;
            public bool useOverride;

            public float scale = 1f;
            public OutputFormat format = OutputFormat.KeepOriginal;
            public int jpgQuality = 80;
            public bool useMaxSize;
            public int maxSize = 1024;

            public long originalBytes;
            public int originalWidth;
            public int originalHeight;
            public string originalExtension;

            public long resultBytes = -1;
            public long bytesBeforeProcess;
            public int resultWidth;
            public int resultHeight;
            public string resultPath;
            public string lastError;
        }

        const string MenuPath = "SBAvatarEditor/Performance/Image Compressor";
        const string AssetsMenuPath = "Assets/SamiVRCBlocks/Compress Images";

        static readonly string[] ImageExtensions =
        {
            ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".bmp", ".exr", ".gif"
        };

        static readonly string[] FormatLabels = { "元の形式を維持", "PNG", "JPG" };
        static readonly string[] SaveModeLabels = { "上書き", "コピーとして保存" };

        readonly List<ImageEntry> _entries = new List<ImageEntry>();
        Vector2 _scroll;
        Vector2 _listScroll;

        float _batchScale = 0.5f;
        OutputFormat _batchFormat = OutputFormat.JPG;
        int _batchJpgQuality = 80;
        bool _batchUseMaxSize;
        int _batchMaxSize = 1024;
        SaveMode _saveMode = SaveMode.Overwrite;
        string _copySuffix = "_compressed";

        long _lastTotalBefore;
        long _lastTotalAfter;
        int _lastSuccessCount;
        int _lastFailCount;
        bool _hasResultSummary;
        bool _listFoldout = true;
        bool _batchFoldout = true;
        bool _resultFoldout = true;

        DefaultAsset _folderToAdd;
        Texture2D _textureToAdd;

        [MenuItem(MenuPath, false, 9)]
        public static void Open()
        {
            var w = GetWindow<ImageCompressor>(false, "Image Compressor", true);
            w.minSize = new Vector2(420, 480);
        }

        [MenuItem(AssetsMenuPath, false, 220)]
        public static void OpenFromAssets()
        {
            Open();
            var w = GetWindow<ImageCompressor>();
            w.AddFromSelection();
        }

        [MenuItem(AssetsMenuPath, true)]
        public static bool OpenFromAssetsValidate()
        {
            return Selection.assetGUIDs != null && Selection.assetGUIDs.Length > 0;
        }

        void OnEnable()
        {
            wantsMouseMove = true;
        }

        void OnGUI()
        {
            SamirinEditorStyleHelper.DrawWithBlueBackground(() =>
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));

                var prevLabelWidth = EditorGUIUtility.labelWidth;
                try
                {
                    EditorGUIUtility.labelWidth = Mathf.Max(prevLabelWidth, 140f);
                    EditorGUILayout.Space(4);
                    SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont(
                        "画像またはフォルダを追加し、倍率・出力形式でピクセル数とファイルサイズを削減します。複数ある場合は一括設定し、必要なら個別に上書きできます。",
                        MessageType.Info);
                    EditorGUILayout.Space(4);

                    DrawAddSection();
                    EditorGUILayout.Space(6);
                    DrawBatchSettings();
                    EditorGUILayout.Space(6);
                    DrawEntryList();
                    EditorGUILayout.Space(6);
                    DrawActions();
                    EditorGUILayout.Space(6);
                    DrawResultSummary();
                    EditorGUILayout.Space(4);
                }
                finally
                {
                    EditorGUIUtility.labelWidth = prevLabelWidth;
                }

                EditorGUILayout.EndScrollView();
            }, new Rect(0, 0, position.width, position.height));

            HandleDragAndDrop();
        }

        void DrawAddSection()
        {
            EditorGUILayout.LabelField("追加", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _textureToAdd = (Texture2D)EditorGUILayout.ObjectField("画像", _textureToAdd, typeof(Texture2D), false);
            GUI.enabled = _textureToAdd != null;
            if (GUILayout.Button("追加", GUILayout.Width(56)))
            {
                TryAddTexture(_textureToAdd);
                _textureToAdd = null;
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _folderToAdd = (DefaultAsset)EditorGUILayout.ObjectField("フォルダ", _folderToAdd, typeof(DefaultAsset), false);
            GUI.enabled = _folderToAdd != null;
            if (GUILayout.Button("追加", GUILayout.Width(56)))
            {
                var path = AssetDatabase.GetAssetPath(_folderToAdd);
                if (AssetDatabase.IsValidFolder(path))
                    AddFromFolder(path);
                else
                    EditorUtility.DisplayDialog("Image Compressor", "フォルダを選択してください。", "OK");
                _folderToAdd = null;
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("選択中のアセットを追加", GUILayout.Height(22)))
                AddFromSelection();
            if (GUILayout.Button("リストをクリア", GUILayout.Height(22), GUILayout.Width(100)))
            {
                _entries.Clear();
                _hasResultSummary = false;
            }
            EditorGUILayout.EndHorizontal();

            SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont(
                "Project ビューから画像やフォルダをこのウィンドウへドラッグ＆ドロップでも追加できます。",
                MessageType.None);
        }

        void DrawBatchSettings()
        {
            _batchFoldout = EditorGUILayout.Foldout(_batchFoldout, "一括設定（デフォルト）", true);
            if (!_batchFoldout) return;

            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            _batchScale = EditorGUILayout.Slider("縮小倍率", _batchScale, 0.05f, 1f);
            _batchFormat = (OutputFormat)EditorGUILayout.Popup("出力形式", (int)_batchFormat, FormatLabels);
            if (_batchFormat == OutputFormat.JPG || _batchFormat == OutputFormat.KeepOriginal)
                _batchJpgQuality = EditorGUILayout.IntSlider("JPG 品質", _batchJpgQuality, 1, 100);

            _batchUseMaxSize = EditorGUILayout.ToggleLeft("最大辺サイズを制限する", _batchUseMaxSize);
            if (_batchUseMaxSize)
                _batchMaxSize = EditorGUILayout.IntPopup("最大辺 (px)", _batchMaxSize,
                    new[] { "128", "256", "512", "1024", "2048", "4096" },
                    new[] { 128, 256, 512, 1024, 2048, 4096 });

            _saveMode = (SaveMode)EditorGUILayout.Popup("保存方法", (int)_saveMode, SaveModeLabels);
            if (_saveMode == SaveMode.SaveAsCopy)
                _copySuffix = EditorGUILayout.TextField("コピー用サフィックス", _copySuffix ?? "_compressed");

            if (EditorGUI.EndChangeCheck())
                ApplyBatchToNonOverrides();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("一括設定を全件に適用", GUILayout.Height(22)))
                ApplyBatchToAll(forceOverrideReset: true);
            if (GUILayout.Button("50%", GUILayout.Width(44)))
            {
                _batchScale = 0.5f;
                ApplyBatchToNonOverrides();
            }
            if (GUILayout.Button("25%", GUILayout.Width(44)))
            {
                _batchScale = 0.25f;
                ApplyBatchToNonOverrides();
            }
            EditorGUILayout.EndHorizontal();

            SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont(
                "個別調整にチェックが入っていない項目へ、この一括設定が反映されます。",
                MessageType.Info);
            EditorGUI.indentLevel--;
        }

        void DrawEntryList()
        {
            var enabledCount = _entries.Count(e => e.enabled);
            _listFoldout = EditorGUILayout.Foldout(
                _listFoldout,
                $"対象画像 ({enabledCount}/{_entries.Count})",
                true);
            if (!_listFoldout) return;

            if (_entries.Count == 0)
            {
                EditorGUILayout.HelpBox("画像が追加されていません。", MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("すべて有効", GUILayout.Height(20)))
                foreach (var e in _entries) e.enabled = true;
            if (GUILayout.Button("すべて無効", GUILayout.Height(20)))
                foreach (var e in _entries) e.enabled = false;
            if (GUILayout.Button("無効を削除", GUILayout.Height(20)))
                _entries.RemoveAll(e => !e.enabled);
            EditorGUILayout.EndHorizontal();

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.MinHeight(160), GUILayout.MaxHeight(360));
            for (int i = 0; i < _entries.Count; i++)
            {
                DrawEntry(_entries[i], i);
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawEntry(ImageEntry entry, int index)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            entry.enabled = EditorGUILayout.Toggle(entry.enabled, GUILayout.Width(18));
            var preview = entry.texture;
            if (preview != null)
                GUILayout.Label(preview, GUILayout.Width(36), GUILayout.Height(36));
            else
                GUILayout.Box("?", GUILayout.Width(36), GUILayout.Height(36));

            EditorGUILayout.BeginVertical();
            var title = string.IsNullOrEmpty(entry.assetPath)
                ? "(missing)"
                : Path.GetFileName(entry.assetPath);
            entry.foldout = EditorGUILayout.Foldout(entry.foldout, title, true);
            EditorGUILayout.LabelField(
                $"{entry.originalWidth}×{entry.originalHeight}  /  {FormatBytes(entry.originalBytes)}",
                EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            if (GUILayout.Button("−", GUILayout.Width(22), GUILayout.Height(22)))
            {
                _entries.RemoveAt(index);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(entry.assetPath))
                EditorGUILayout.LabelField(entry.assetPath, EditorStyles.miniLabel);

            if (entry.foldout)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginDisabledGroup(!entry.enabled);

                EditorGUI.BeginChangeCheck();
                entry.useOverride = EditorGUILayout.ToggleLeft("個別調整する", entry.useOverride);
                if (EditorGUI.EndChangeCheck() && !entry.useOverride)
                    ApplyBatchToEntry(entry);

                EditorGUI.BeginDisabledGroup(!entry.useOverride);
                entry.scale = EditorGUILayout.Slider("縮小倍率", entry.scale, 0.05f, 1f);
                entry.format = (OutputFormat)EditorGUILayout.Popup("出力形式", (int)entry.format, FormatLabels);
                if (entry.format == OutputFormat.JPG ||
                    (entry.format == OutputFormat.KeepOriginal && IsJpegExtension(entry.originalExtension)))
                {
                    entry.jpgQuality = EditorGUILayout.IntSlider("JPG 品質", entry.jpgQuality, 1, 100);
                }

                entry.useMaxSize = EditorGUILayout.ToggleLeft("最大辺サイズを制限する", entry.useMaxSize);
                if (entry.useMaxSize)
                {
                    entry.maxSize = EditorGUILayout.IntPopup("最大辺 (px)", entry.maxSize,
                        new[] { "128", "256", "512", "1024", "2048", "4096" },
                        new[] { 128, 256, 512, 1024, 2048, 4096 });
                }
                EditorGUI.EndDisabledGroup();

                var (outW, outH) = PredictOutputSize(entry);
                var formatLabel = ResolveFormatLabel(entry);
                EditorGUILayout.LabelField("予測出力", $"{outW}×{outH}  ({formatLabel})", EditorStyles.miniLabel);

                if (entry.resultBytes >= 0)
                {
                    var before = entry.bytesBeforeProcess > 0 ? entry.bytesBeforeProcess : entry.originalBytes;
                    var delta = before - entry.resultBytes;
                    var pct = before > 0 ? (delta * 100.0 / before) : 0;
                    var color = delta >= 0 ? new Color(0.2f, 0.55f, 0.25f) : new Color(0.7f, 0.25f, 0.2f);
                    var prev = GUI.contentColor;
                    GUI.contentColor = color;
                    EditorGUILayout.LabelField(
                        "削減結果",
                        $"{FormatBytes(before)} → {FormatBytes(entry.resultBytes)}  ({FormatSignedBytes(-delta)}, {pct:F1}%)");
                    GUI.contentColor = prev;
                    if (!string.IsNullOrEmpty(entry.resultPath) &&
                        !string.Equals(entry.resultPath, entry.assetPath, StringComparison.OrdinalIgnoreCase))
                        EditorGUILayout.LabelField("出力先", entry.resultPath, EditorStyles.miniLabel);
                }

                if (!string.IsNullOrEmpty(entry.lastError))
                    EditorGUILayout.HelpBox(entry.lastError, MessageType.Error);

                EditorGUI.EndDisabledGroup();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        void DrawActions()
        {
            var enabled = _entries.Count(e => e.enabled) > 0;
            GUI.enabled = enabled;
            if (GUILayout.Button("圧縮・変換を実行", GUILayout.Height(30), GUILayout.ExpandWidth(true)))
            {
                if (_saveMode == SaveMode.Overwrite)
                {
                    if (!EditorUtility.DisplayDialog(
                            "Image Compressor",
                            $"有効な {_entries.Count(e => e.enabled)} 件を上書きします。よろしいですか？\n（Undo はできません）",
                            "実行",
                            "キャンセル"))
                    {
                        GUI.enabled = true;
                        return;
                    }
                }

                ProcessAll();
            }
            GUI.enabled = true;

            if (!enabled)
                EditorGUILayout.HelpBox("有効な画像がありません。", MessageType.Warning);
        }

        void DrawResultSummary()
        {
            _resultFoldout = EditorGUILayout.Foldout(_resultFoldout, "容量削減サマリー", true);
            if (!_resultFoldout || !_hasResultSummary) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("成功", $"{_lastSuccessCount} 件");
            EditorGUILayout.LabelField("失敗", $"{_lastFailCount} 件");
            EditorGUILayout.LabelField("変換前合計", FormatBytes(_lastTotalBefore));
            EditorGUILayout.LabelField("変換後合計", FormatBytes(_lastTotalAfter));

            var saved = _lastTotalBefore - _lastTotalAfter;
            var pct = _lastTotalBefore > 0 ? (saved * 100.0 / _lastTotalBefore) : 0;
            var prev = GUI.contentColor;
            GUI.contentColor = saved >= 0 ? new Color(0.2f, 0.55f, 0.25f) : new Color(0.7f, 0.25f, 0.2f);
            EditorGUILayout.LabelField("削減量", $"{FormatSignedBytes(-saved)}  ({pct:F1}%)");
            GUI.contentColor = prev;
            EditorGUILayout.EndVertical();
            EditorGUI.indentLevel--;
        }

        void HandleDragAndDrop()
        {
            var evt = Event.current;
            if (evt == null) return;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;

            var paths = DragAndDrop.paths;
            if (paths == null || paths.Length == 0) return;

            var hasValid = paths.Any(p =>
            {
                var assetPath = ToAssetPath(p);
                return !string.IsNullOrEmpty(assetPath) &&
                       (AssetDatabase.IsValidFolder(assetPath) || IsImageAssetPath(assetPath));
            });
            if (!hasValid && (DragAndDrop.objectReferences == null || DragAndDrop.objectReferences.Length == 0))
                return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj is Texture2D tex)
                        TryAddTexture(tex);
                    else if (obj != null)
                    {
                        var path = AssetDatabase.GetAssetPath(obj);
                        if (AssetDatabase.IsValidFolder(path))
                            AddFromFolder(path);
                        else if (IsImageAssetPath(path))
                            TryAddPath(path);
                    }
                }

                foreach (var p in paths)
                {
                    var assetPath = ToAssetPath(p);
                    if (string.IsNullOrEmpty(assetPath)) continue;
                    if (AssetDatabase.IsValidFolder(assetPath))
                        AddFromFolder(assetPath);
                    else if (IsImageAssetPath(assetPath))
                        TryAddPath(assetPath);
                }

                evt.Use();
                Repaint();
            }
        }

        void ApplyBatchToNonOverrides()
        {
            foreach (var e in _entries)
            {
                if (!e.useOverride)
                    ApplyBatchToEntry(e);
            }
        }

        void ApplyBatchToAll(bool forceOverrideReset)
        {
            foreach (var e in _entries)
            {
                if (forceOverrideReset)
                    e.useOverride = false;
                ApplyBatchToEntry(e);
            }
        }

        void ApplyBatchToEntry(ImageEntry entry)
        {
            entry.scale = _batchScale;
            entry.format = _batchFormat;
            entry.jpgQuality = _batchJpgQuality;
            entry.useMaxSize = _batchUseMaxSize;
            entry.maxSize = _batchMaxSize;
        }

        public void AddFromSelection()
        {
            var guids = Selection.assetGUIDs;
            if (guids == null) return;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                if (AssetDatabase.IsValidFolder(path))
                    AddFromFolder(path);
                else if (IsImageAssetPath(path))
                    TryAddPath(path);
            }
        }

        void AddFromFolder(string folderPath)
        {
            folderPath = (folderPath ?? "").Replace("\\", "/").TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(folderPath)) return;

            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsImageAssetPath(path))
                    TryAddPath(path);
            }
        }

        void TryAddTexture(Texture2D texture)
        {
            if (texture == null) return;
            var path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path) || !IsImageAssetPath(path))
            {
                EditorUtility.DisplayDialog("Image Compressor", "プロジェクト内の画像アセットのみ追加できます。", "OK");
                return;
            }
            TryAddPath(path);
        }

        void TryAddPath(string assetPath)
        {
            assetPath = (assetPath ?? "").Replace("\\", "/");
            if (string.IsNullOrEmpty(assetPath) || !IsImageAssetPath(assetPath)) return;
            if (_entries.Any(e => string.Equals(e.assetPath, assetPath, StringComparison.OrdinalIgnoreCase)))
                return;

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (tex == null) return;

            var abs = ToAbsolutePath(assetPath);
            long bytes = 0;
            if (File.Exists(abs))
                bytes = new FileInfo(abs).Length;

            var entry = new ImageEntry
            {
                assetPath = assetPath,
                texture = tex,
                originalBytes = bytes,
                originalWidth = tex.width,
                originalHeight = tex.height,
                originalExtension = Path.GetExtension(assetPath)?.ToLowerInvariant() ?? "",
            };
            ApplyBatchToEntry(entry);
            _entries.Add(entry);
            _hasResultSummary = false;
        }

        void ProcessAll()
        {
            _lastTotalBefore = 0;
            _lastTotalAfter = 0;
            _lastSuccessCount = 0;
            _lastFailCount = 0;

            var targets = _entries.Where(e => e.enabled).ToList();
            if (targets.Count == 0) return;

            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    var entry = targets[i];
                    EditorUtility.DisplayProgressBar(
                        "Image Compressor",
                        Path.GetFileName(entry.assetPath),
                        (float)i / targets.Count);

                    entry.lastError = null;
                    entry.resultBytes = -1;
                    entry.resultPath = null;
                    entry.bytesBeforeProcess = 0;

                    try
                    {
                        if (ProcessEntry(entry))
                            _lastSuccessCount++;
                        else
                            _lastFailCount++;
                    }
                    catch (Exception ex)
                    {
                        entry.lastError = ex.Message;
                        _lastFailCount++;
                        Debug.LogException(ex);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }

            _lastTotalBefore = 0;
            _lastTotalAfter = 0;
            foreach (var entry in targets)
            {
                if (entry.resultBytes < 0 && string.IsNullOrEmpty(entry.resultPath))
                    continue;

                var path = string.IsNullOrEmpty(entry.resultPath) ? entry.assetPath : entry.resultPath;
                entry.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (entry.texture != null)
                {
                    entry.resultWidth = entry.texture.width;
                    entry.resultHeight = entry.texture.height;
                    if (_saveMode == SaveMode.Overwrite)
                    {
                        entry.originalWidth = entry.texture.width;
                        entry.originalHeight = entry.texture.height;
                    }
                }

                var abs = ToAbsolutePath(path);
                if (File.Exists(abs))
                    entry.resultBytes = new FileInfo(abs).Length;

                _lastTotalBefore += entry.bytesBeforeProcess;
                _lastTotalAfter += Math.Max(0, entry.resultBytes);

                if (_saveMode == SaveMode.Overwrite)
                {
                    entry.assetPath = path;
                    entry.originalExtension = Path.GetExtension(path)?.ToLowerInvariant() ?? "";
                    entry.originalBytes = Math.Max(0, entry.resultBytes);
                }
            }

            _hasResultSummary = true;
            Repaint();

            var saved = _lastTotalBefore - _lastTotalAfter;
            EditorUtility.DisplayDialog(
                "Image Compressor",
                $"完了しました。\n成功: {_lastSuccessCount} / 失敗: {_lastFailCount}\n" +
                $"変換前: {FormatBytes(_lastTotalBefore)}\n変換後: {FormatBytes(_lastTotalAfter)}\n" +
                $"削減: {FormatSignedBytes(-saved)} ({(_lastTotalBefore > 0 ? saved * 100.0 / _lastTotalBefore : 0):F1}%)",
                "OK");
        }

        bool ProcessEntry(ImageEntry entry)
        {
            if (string.IsNullOrEmpty(entry.assetPath))
            {
                entry.lastError = "パスが無効です。";
                return false;
            }

            var absPath = ToAbsolutePath(entry.assetPath);
            if (!File.Exists(absPath))
            {
                entry.lastError = "ファイルが見つかりません。";
                return false;
            }

            entry.originalBytes = new FileInfo(absPath).Length;
            entry.bytesBeforeProcess = entry.originalBytes;

            var source = LoadReadableTexture(entry.assetPath, out var loadError);
            if (source == null)
            {
                entry.lastError = loadError ?? "テクスチャを読み込めませんでした。";
                return false;
            }

            Texture2D working = source;
            Texture2D scaled = null;
            try
            {
                var (outW, outH) = PredictOutputSize(entry);
                if (outW != source.width || outH != source.height)
                {
                    scaled = ResizeTexture(source, outW, outH);
                    working = scaled;
                }

                byte[] bytes;
                string newExt;
                EncodeImage(working, entry, out bytes, out newExt);

                if (bytes == null || bytes.Length == 0)
                {
                    entry.lastError = "エンコードに失敗しました。";
                    return false;
                }

                string destAssetPath;
                if (_saveMode == SaveMode.SaveAsCopy)
                {
                    var suffix = string.IsNullOrEmpty(_copySuffix) ? "_compressed" : _copySuffix;
                    var dir = Path.GetDirectoryName(entry.assetPath)?.Replace("\\", "/") ?? "Assets";
                    var name = Path.GetFileNameWithoutExtension(entry.assetPath);
                    destAssetPath = $"{dir}/{name}{suffix}{newExt}";
                    destAssetPath = AssetDatabase.GenerateUniqueAssetPath(destAssetPath);
                }
                else
                {
                    destAssetPath = entry.assetPath;
                    var currentExt = Path.GetExtension(entry.assetPath);
                    if (!string.Equals(currentExt, newExt, StringComparison.OrdinalIgnoreCase))
                    {
                        var renamed = Path.ChangeExtension(entry.assetPath, newExt).Replace("\\", "/");
                        var moveError = AssetDatabase.MoveAsset(entry.assetPath, renamed);
                        if (!string.IsNullOrEmpty(moveError))
                        {
                            entry.lastError = "拡張子変更に失敗: " + moveError;
                            return false;
                        }
                        destAssetPath = renamed;
                        entry.assetPath = renamed;
                    }
                }

                var destAbs = ToAbsolutePath(destAssetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(destAbs) ?? ".");
                File.WriteAllBytes(destAbs, bytes);
                AssetDatabase.ImportAsset(destAssetPath, ImportAssetOptions.ForceUpdate);

                entry.resultPath = destAssetPath;
                entry.resultBytes = bytes.Length;
                entry.resultWidth = working.width;
                entry.resultHeight = working.height;
                entry.lastError = null;
                return true;
            }
            finally
            {
                if (scaled != null)
                    DestroyImmediate(scaled);
                if (source != null)
                    DestroyImmediate(source);
            }
        }

        static (int w, int h) PredictOutputSize(ImageEntry entry)
        {
            var w = Mathf.Max(1, Mathf.RoundToInt(entry.originalWidth * entry.scale));
            var h = Mathf.Max(1, Mathf.RoundToInt(entry.originalHeight * entry.scale));
            if (entry.useMaxSize && entry.maxSize > 0)
            {
                var maxEdge = Mathf.Max(w, h);
                if (maxEdge > entry.maxSize)
                {
                    var s = (float)entry.maxSize / maxEdge;
                    w = Mathf.Max(1, Mathf.RoundToInt(w * s));
                    h = Mathf.Max(1, Mathf.RoundToInt(h * s));
                }
            }
            return (w, h);
        }

        static void EncodeImage(Texture2D working, ImageEntry entry, out byte[] bytes, out string newExt)
        {
            switch (entry.format)
            {
                case OutputFormat.JPG:
                    bytes = working.EncodeToJPG(Mathf.Clamp(entry.jpgQuality, 1, 100));
                    newExt = ".jpg";
                    return;
                case OutputFormat.PNG:
                    bytes = working.EncodeToPNG();
                    newExt = ".png";
                    return;
                default:
                    if (IsJpegExtension(entry.originalExtension))
                    {
                        bytes = working.EncodeToJPG(Mathf.Clamp(entry.jpgQuality, 1, 100));
                        newExt = entry.originalExtension == ".jpeg" ? ".jpeg" : ".jpg";
                    }
                    else
                    {
                        // Unity の Encode API で安全に扱えるよう PNG へ寄せる
                        bytes = working.EncodeToPNG();
                        newExt = ".png";
                    }
                    return;
            }
        }

        static string ResolveFormatLabel(ImageEntry entry)
        {
            switch (entry.format)
            {
                case OutputFormat.PNG: return "PNG";
                case OutputFormat.JPG: return $"JPG q{entry.jpgQuality}";
                default:
                    if (IsJpegExtension(entry.originalExtension))
                        return $"元形式(JPG) q{entry.jpgQuality}";
                    return entry.originalExtension == ".png"
                        ? "元形式(PNG)"
                        : $"PNGへ変換 (元{entry.originalExtension})";
            }
        }

        static Texture2D LoadReadableTexture(string assetPath, out string error)
        {
            error = null;
            try
            {
                var loaded = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (loaded == null)
                {
                    error = "Texture2D として読み込めません。";
                    return null;
                }

                return CopyViaBlit(loaded, loaded.width, loaded.height);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        static Texture2D CopyViaBlit(Texture source, int width, int height)
        {
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply(false, false);
                return tex;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static Texture2D ResizeTexture(Texture2D source, int width, int height)
        {
            return CopyViaBlit(source, width, height);
        }

        static bool IsImageAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var ext = Path.GetExtension(path)?.ToLowerInvariant() ?? "";
            return ImageExtensions.Contains(ext);
        }

        static bool IsJpegExtension(string ext)
        {
            ext = (ext ?? "").ToLowerInvariant();
            return ext == ".jpg" || ext == ".jpeg";
        }

        static string ToAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            path = path.Replace("\\", "/");
            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return path;
            var dataPath = Application.dataPath.Replace("\\", "/");
            if (path.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
                return "Assets" + path.Substring(dataPath.Length);
            return null;
        }

        static string ToAbsolutePath(string assetPath)
        {
            assetPath = (assetPath ?? "").Replace("\\", "/");
            if (assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            return Path.GetFullPath(assetPath);
        }

        static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return $"{kb:F1} KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return $"{mb:F2} MB";
            return $"{mb / 1024.0:F2} GB";
        }

        static string FormatSignedBytes(long bytes)
        {
            // bytes は「増減」(after-before)。表示は削減をマイナス表記にしたいので呼び出し側で符号を渡す。
            var sign = bytes > 0 ? "+" : "";
            return sign + FormatBytes(Math.Abs(bytes));
        }
    }
}
