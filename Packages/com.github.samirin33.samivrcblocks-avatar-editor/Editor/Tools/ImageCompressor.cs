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
    /// 画素はインポート済みテクスチャではなく元ファイル（PNG / JPG）から読む。インポート設定の最大サイズや圧縮、
    /// 色空間変換で劣化した画素を元ファイルへ書き戻さないため。ノーマルマップ・sRGB オフ・PNG/JPG 以外は対象外。
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
            public bool sourceHasAlpha;
            /// <summary>null 以外なら処理対象外。理由を表示する。</summary>
            public string skipReason;

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
            ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".bmp", ".exr", ".gif", ".psd", ".hdr"
        };

        /// <summary>Texture2D.LoadImage で元ファイルを劣化なく読める形式。</summary>
        static readonly string[] ProcessableExtensions = { ".png", ".jpg", ".jpeg" };

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
                foreach (var e in _entries) e.enabled = string.IsNullOrEmpty(e.skipReason);
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

            if (!string.IsNullOrEmpty(entry.skipReason))
                EditorGUILayout.HelpBox("対象外: " + entry.skipReason, MessageType.Warning);
            else if (WillDropAlpha(entry))
                EditorGUILayout.HelpBox(
                    "アルファ（透明部分）を含む可能性があります。JPG はアルファを保存できないため、透明部分がある場合は処理しません。PNG を選んでください。",
                    MessageType.Warning);

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
                // 確認ダイアログとアセット書き換えはレイアウト途中で行わない。
                EditorApplication.delayCall += ConfirmAndProcessAll;
            }
            GUI.enabled = true;

            if (!enabled)
                EditorGUILayout.HelpBox("有効な画像がありません。", MessageType.Warning);
        }

        void ConfirmAndProcessAll()
        {
            if (this == null)
                return;

            var targets = _entries.Where(e => e.enabled).ToList();
            var processable = targets.Where(e => string.IsNullOrEmpty(e.skipReason)).ToList();
            var skipped = targets.Count - processable.Count;
            if (processable.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Image Compressor",
                    "処理できる画像がありません。\n（PNG / JPG 以外、ノーマルマップ、sRGB オフのテクスチャは対象外です）",
                    "OK");
                return;
            }

            if (_saveMode == SaveMode.Overwrite)
            {
                var message = $"有効な {processable.Count} 件を上書きします。よろしいですか？\n（Undo はできません）";
                var renamed = processable.Count(e => !string.Equals(
                    ResolveOutputExtension(e), e.originalExtension, StringComparison.OrdinalIgnoreCase));
                if (renamed > 0)
                    message += $"\n\n{renamed} 件は拡張子が変わります（元の形式のファイルは残りません。参照は維持されます）。";
                var alpha = processable.Count(WillDropAlpha);
                if (alpha > 0)
                    message += $"\n\n{alpha} 件はアルファを含む可能性があります。透明部分があるものは JPG にせずスキップします。";
                if (skipped > 0)
                    message += $"\n\n対象外の {skipped} 件はスキップします。";

                if (!EditorUtility.DisplayDialog("Image Compressor", message, "実行", "キャンセル"))
                    return;
            }

            ProcessAll();
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
                originalExtension = Path.GetExtension(assetPath)?.ToLowerInvariant() ?? "",
            };
            RefreshSourceInfo(entry);
            ApplyBatchToEntry(entry);
            if (!string.IsNullOrEmpty(entry.skipReason))
                entry.enabled = false;
            _entries.Add(entry);
            _hasResultSummary = false;
        }

        /// <summary>
        /// 元ファイルの寸法・アルファ有無と、対象外かどうかを importer から取り直す。
        /// texture.width はインポート時の最大サイズで縮んでいることがあるため使わない。
        /// </summary>
        static void RefreshSourceInfo(ImageEntry entry)
        {
            var importer = AssetImporter.GetAtPath(entry.assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.GetSourceTextureWidthAndHeight(out var w, out var h);
                entry.originalWidth = w;
                entry.originalHeight = h;
                entry.sourceHasAlpha = importer.DoesSourceTextureHaveAlpha();
            }
            else if (entry.texture != null)
            {
                entry.originalWidth = entry.texture.width;
                entry.originalHeight = entry.texture.height;
                entry.sourceHasAlpha = true;
            }

            entry.skipReason = GetSkipReason(entry.assetPath, importer);
        }

        static string GetSkipReason(string assetPath, TextureImporter importer)
        {
            var ext = Path.GetExtension(assetPath)?.ToLowerInvariant() ?? "";
            if (!ProcessableExtensions.Contains(ext))
                return $"{ext} は元データを劣化なく読み書きできないため処理しません（PNG / JPG のみ対応）。";
            if (importer == null)
                return "TextureImporter を取得できません。";
            if (importer.textureType == TextureImporterType.NormalMap)
                return "ノーマルマップは縮小・再圧縮で法線が崩れるため処理しません。";
            if (!importer.sRGBTexture)
                return "sRGB がオフ（マスク・データ用）のテクスチャは値が変わると表示が崩れるため処理しません。";
            return null;
        }

        static bool WillDropAlpha(ImageEntry entry)
        {
            return entry.sourceHasAlpha &&
                   string.IsNullOrEmpty(entry.skipReason) &&
                   IsJpegExtension(ResolveOutputExtension(entry));
        }

        static string ResolveOutputExtension(ImageEntry entry)
        {
            switch (entry.format)
            {
                case OutputFormat.JPG: return ".jpg";
                case OutputFormat.PNG: return ".png";
                default:
                    if (IsJpegExtension(entry.originalExtension))
                        return entry.originalExtension == ".jpeg" ? ".jpeg" : ".jpg";
                    return ".png";
            }
        }

        void ProcessAll()
        {
            _lastTotalBefore = 0;
            _lastTotalAfter = 0;
            _lastSuccessCount = 0;
            _lastFailCount = 0;

            var targets = _entries.Where(e => e.enabled).ToList();
            if (targets.Count == 0) return;

            // 処理直前に importer 設定を取り直す（追加後に設定が変わっている可能性がある）。
            foreach (var entry in targets)
                RefreshSourceInfo(entry);

            AssetDatabase.StartAssetEditing();
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

                    if (!string.IsNullOrEmpty(entry.skipReason))
                        continue;

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
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            _lastTotalBefore = 0;
            _lastTotalAfter = 0;
            foreach (var entry in targets)
            {
                if (entry.resultBytes < 0 && string.IsNullOrEmpty(entry.resultPath))
                    continue;

                var path = string.IsNullOrEmpty(entry.resultPath) ? entry.assetPath : entry.resultPath;
                var resultTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (_saveMode == SaveMode.Overwrite)
                {
                    entry.texture = resultTexture;
                    entry.originalWidth = entry.resultWidth;
                    entry.originalHeight = entry.resultHeight;
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
            var skippedCount = targets.Count(e => !string.IsNullOrEmpty(e.skipReason));
            EditorUtility.DisplayDialog(
                "Image Compressor",
                $"完了しました。\n成功: {_lastSuccessCount} / 失敗: {_lastFailCount}" +
                (skippedCount > 0 ? $" / 対象外: {skippedCount}" : "") + "\n" +
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

            if (!TryLoadSourcePixels(absPath, out var pixels, out var srcW, out var srcH, out var loadError))
            {
                entry.lastError = loadError;
                return false;
            }

            entry.originalWidth = srcW;
            entry.originalHeight = srcH;
            var hasAlpha = HasTransparentPixel(pixels);
            var newExt = ResolveOutputExtension(entry);
            if (hasAlpha && IsJpegExtension(newExt))
            {
                entry.lastError = "透明部分があるため JPG にできません（アルファが失われます）。出力形式を PNG にしてください。";
                return false;
            }

            var (outW, outH) = PredictOutputSize(entry);
            if (outW != srcW || outH != srcH)
                pixels = ResizeArea(pixels, srcW, srcH, outW, outH);

            var bytes = Encode(pixels, outW, outH, hasAlpha, newExt, entry.jpgQuality);
            if (bytes == null || bytes.Length == 0)
            {
                entry.lastError = "エンコードに失敗しました。";
                return false;
            }

            string destAssetPath;
            var movedFrom = (string)null;
            if (_saveMode == SaveMode.SaveAsCopy)
            {
                var suffix = string.IsNullOrEmpty(_copySuffix) ? "_compressed" : _copySuffix;
                var dir = Path.GetDirectoryName(entry.assetPath)?.Replace("\\", "/") ?? "Assets";
                var name = Path.GetFileNameWithoutExtension(entry.assetPath);
                destAssetPath = GenerateUniqueDestPath(dir, name + suffix, newExt);
            }
            else
            {
                destAssetPath = entry.assetPath;
                var currentExt = Path.GetExtension(entry.assetPath);
                if (!string.Equals(currentExt, newExt, StringComparison.OrdinalIgnoreCase))
                {
                    // GUID と importer 設定を保つため MoveAsset で拡張子だけ変える（参照は切れない）。
                    var renamed = Path.ChangeExtension(entry.assetPath, newExt).Replace("\\", "/");
                    if (File.Exists(ToAbsolutePath(renamed)))
                    {
                        entry.lastError = "同名の " + newExt + " ファイルが既にあるため拡張子を変更できません: " + renamed;
                        return false;
                    }
                    var moveError = AssetDatabase.MoveAsset(entry.assetPath, renamed);
                    if (!string.IsNullOrEmpty(moveError))
                    {
                        entry.lastError = "拡張子変更に失敗: " + moveError;
                        return false;
                    }
                    movedFrom = entry.assetPath;
                    destAssetPath = renamed;
                    entry.assetPath = renamed;
                }
            }

            var destAbs = ToAbsolutePath(destAssetPath);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destAbs) ?? ".");
                File.WriteAllBytes(destAbs, bytes);
            }
            catch
            {
                if (movedFrom != null && string.IsNullOrEmpty(AssetDatabase.MoveAsset(destAssetPath, movedFrom)))
                    entry.assetPath = movedFrom;
                throw;
            }
            AssetDatabase.ImportAsset(destAssetPath, ImportAssetOptions.ForceUpdate);

            entry.resultPath = destAssetPath;
            entry.resultBytes = bytes.Length;
            entry.resultWidth = outW;
            entry.resultHeight = outH;
            entry.lastError = null;
            return true;
        }

        /// <summary>
        /// 元ファイルのバイト列を直接デコードする。16bit PNG はここで 8bit に落ちるため対象外にする。
        /// </summary>
        static bool TryLoadSourcePixels(string absPath, out Color32[] pixels, out int width, out int height, out string error)
        {
            pixels = null;
            width = height = 0;
            error = null;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!tex.LoadImage(File.ReadAllBytes(absPath), false))
                {
                    error = "画像ファイルをデコードできませんでした。";
                    return false;
                }

                if (tex.format == TextureFormat.RGBA64 || tex.format == TextureFormat.RGB48 ||
                    tex.format == TextureFormat.R16)
                {
                    error = "16bit 画像は 8bit に変換すると階調が失われるため処理しません。";
                    return false;
                }

                width = tex.width;
                height = tex.height;
                pixels = tex.GetPixels32();
                return true;
            }
            catch (Exception ex)
            {
                error = "画像の読み込みに失敗: " + ex.Message;
                return false;
            }
            finally
            {
                DestroyImmediate(tex);
            }
        }

        static bool HasTransparentPixel(Color32[] pixels)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a < 255)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 面積平均で縮小する。GPU の Blit と違い色空間変換が入らず、大きく縮めてもエイリアスが出にくい。
        /// </summary>
        static Color32[] ResizeArea(Color32[] src, int srcW, int srcH, int dstW, int dstH)
        {
            BuildAreaWeights(srcW, dstW, out var xStart, out var xWeights);
            BuildAreaWeights(srcH, dstH, out var yStart, out var yWeights);

            var dst = new Color32[dstW * dstH];
            for (int dy = 0; dy < dstH; dy++)
            {
                var wy = yWeights[dy];
                for (int dx = 0; dx < dstW; dx++)
                {
                    var wx = xWeights[dx];
                    float r = 0f, g = 0f, b = 0f, a = 0f, total = 0f;
                    for (int j = 0; j < wy.Length; j++)
                    {
                        var row = (yStart[dy] + j) * srcW + xStart[dx];
                        for (int i = 0; i < wx.Length; i++)
                        {
                            var w = wx[i] * wy[j];
                            var c = src[row + i];
                            r += c.r * w;
                            g += c.g * w;
                            b += c.b * w;
                            a += c.a * w;
                            total += w;
                        }
                    }

                    var inv = total > 0f ? 1f / total : 0f;
                    dst[dy * dstW + dx] = new Color32(
                        ToByte(r * inv), ToByte(g * inv), ToByte(b * inv), ToByte(a * inv));
                }
            }
            return dst;
        }

        static void BuildAreaWeights(int srcSize, int dstSize, out int[] starts, out float[][] weights)
        {
            starts = new int[dstSize];
            weights = new float[dstSize][];
            var ratio = (double)srcSize / dstSize;
            for (int d = 0; d < dstSize; d++)
            {
                var begin = d * ratio;
                var end = Math.Min(srcSize, (d + 1) * ratio);
                var first = Math.Min(srcSize - 1, (int)Math.Floor(begin));
                var last = Math.Max(first, Math.Min(srcSize - 1, (int)Math.Ceiling(end) - 1));
                var w = new float[last - first + 1];
                for (int s = first; s <= last; s++)
                {
                    var overlap = Math.Min(end, s + 1) - Math.Max(begin, s);
                    w[s - first] = (float)Math.Max(overlap, 0.0);
                }
                starts[d] = first;
                weights[d] = w;
            }
        }

        static byte ToByte(float v)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(v), 0, 255);
        }

        static byte[] Encode(Color32[] pixels, int width, int height, bool hasAlpha, string ext, int jpgQuality)
        {
            Texture2D tex;
            if (hasAlpha)
            {
                tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.SetPixels32(pixels);
            }
            else
            {
                // アルファ無しは RGB24 で書き、PNG に不要なアルファチャンネルを持たせない。
                var raw = new byte[width * height * 3];
                for (int i = 0, o = 0; i < pixels.Length; i++, o += 3)
                {
                    raw[o] = pixels[i].r;
                    raw[o + 1] = pixels[i].g;
                    raw[o + 2] = pixels[i].b;
                }
                tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.LoadRawTextureData(raw);
            }

            try
            {
                tex.Apply(false, false);
                return IsJpegExtension(ext)
                    ? tex.EncodeToJPG(Mathf.Clamp(jpgQuality, 1, 100))
                    : tex.EncodeToPNG();
            }
            finally
            {
                DestroyImmediate(tex);
            }
        }

        /// <summary>
        /// StartAssetEditing 中は書き出したばかりのファイルを AssetDatabase が知らないため、ディスクも見て重複を避ける。
        /// </summary>
        static string GenerateUniqueDestPath(string dir, string baseName, string ext)
        {
            var candidate = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{baseName}{ext}");
            for (int i = 1; File.Exists(ToAbsolutePath(candidate)); i++)
                candidate = $"{dir}/{baseName} {i}{ext}";
            return candidate;
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
