#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Samirin33.Editor;

namespace SamiVRCBlocksAvatar.Editor
{
    /// <summary>
    /// 指定ディレクトリ内のプレファブを解析し、lilAvatarUtils / MA Information 由来の統計を表示・書き出します。
    /// 依存パッケージが無い場合はコンパイルを壊さず、解析時に利用不可を通知します。
    /// </summary>
    public class ItemAnalyzer : EditorWindow
    {
        const string MenuPath = "SBAvatarEditor/Performance/Item Analyzer";
        const string EditorPrefsKeyDirectory = "SamiVRCBlocksAvatar.ItemAnalyzer.Directory";
        const string EditorPrefsKeyListHeight = "SamiVRCBlocksAvatar.ItemAnalyzer.ListHeight";
        const float ListHeightMin = 80f;
        const float ListHeightDefault = 180f;

        [Serializable]
        class PrefabEntry
        {
            public string assetPath;
            public string name;
            public bool selected = true;
            public PrefabAnalysisResult result;
        }

        DefaultAsset _directoryAsset;
        string _directoryPath = "";
        readonly List<PrefabEntry> _entries = new List<PrefabEntry>();
        Vector2 _listScroll;
        Vector2 _resultScroll;
        string _statusMessage = "";
        MessageType _statusType = MessageType.Info;
        bool _listFoldout = true;
        bool _resultFoldout = true;
        float _listHeight = ListHeightDefault;
        bool _draggingListSplitter;

        [MenuItem(MenuPath, false, 10)]
        public static void Open()
        {
            var w = GetWindow<ItemAnalyzer>(false, "Item Analyzer", true);
            w.minSize = new Vector2(420, 480);
        }

        /// <summary>
        /// Package Exporter などから配布フォルダを指定して開きます。
        /// </summary>
        public static void OpenWithDirectory(string directoryPath)
        {
            Open();
            var w = GetWindow<ItemAnalyzer>();
            w.SetDirectory(directoryPath);
        }

        /// <summary>
        /// 解析対象ディレクトリを設定し、プレファブを再スキャンします。
        /// </summary>
        public void SetDirectory(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath))
            {
                _directoryPath = "";
                _directoryAsset = null;
                _entries.Clear();
                SetStatus("ディレクトリが指定されていません。", MessageType.Warning);
                Repaint();
                return;
            }

            var path = directoryPath.Replace("\\", "/").TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(path))
            {
                SetStatus($"有効なフォルダではありません: {path}", MessageType.Warning);
                Repaint();
                return;
            }

            _directoryPath = path;
            _directoryAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_directoryPath);
            EditorPrefs.SetString(EditorPrefsKeyDirectory, _directoryPath);
            ScanPrefabs();
            Repaint();
        }

        void OnEnable()
        {
            _directoryPath = EditorPrefs.GetString(EditorPrefsKeyDirectory, "");
            _listHeight = EditorPrefs.GetFloat(EditorPrefsKeyListHeight, ListHeightDefault);
            RestoreDirectorySelection();
        }

        void OnGUI()
        {
            SamirinEditorStyleHelper.DrawWithBlueBackground(() =>
            {
                EditorGUILayout.Space(4);
                SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont(
                    "ディレクトリ内のプレファブを解析します。メッシュ・マテリアル・テクスチャ・PhysBone 等は lilAvatarUtils、同期パラメーターは MA Information（NDMF ParameterInfo）から取得します。",
                    MessageType.Info);

                DrawDependencyStatus();
                EditorGUILayout.Space(4);
                DrawDirectorySection();
                EditorGUILayout.Space(6);
                DrawPrefabList();
                EditorGUILayout.Space(6);
                DrawActions();
                EditorGUILayout.Space(6);
                DrawResults();

                if (!string.IsNullOrEmpty(_statusMessage))
                {
                    EditorGUILayout.Space(4);
                    SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont(_statusMessage, _statusType);
                }

                EditorGUILayout.Space(4);
            });
        }

        void DrawDependencyStatus()
        {
            var lilOk = LilAvatarUtilsBridge.IsAvailable;
            var maOk = MaInformationBridge.IsAvailable;
            var msg =
                $"lilAvatarUtils: {(lilOk ? "利用可能" : "未検出")}  /  MA Information (NDMF): {(maOk ? "利用可能" : "未検出")}";
            SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont(
                msg,
                lilOk && maOk ? MessageType.Info : MessageType.Warning);
        }

        void DrawDirectorySection()
        {
            EditorGUILayout.LabelField("対象ディレクトリ", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var next = (DefaultAsset)EditorGUILayout.ObjectField("フォルダ", _directoryAsset, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                _directoryAsset = next;
                if (_directoryAsset != null)
                {
                    var path = AssetDatabase.GetAssetPath(_directoryAsset);
                    if (AssetDatabase.IsValidFolder(path))
                    {
                        _directoryPath = path.Replace("\\", "/");
                        EditorPrefs.SetString(EditorPrefsKeyDirectory, _directoryPath);
                        ScanPrefabs();
                    }
                    else
                    {
                        _directoryAsset = null;
                        SetStatus("フォルダアセットを指定してください。", MessageType.Warning);
                    }
                }
                else
                {
                    _directoryPath = "";
                    _entries.Clear();
                }
            }

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("パス", _directoryPath ?? "");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("再スキャン", GUILayout.Width(100)))
                ScanPrefabs();
            if (GUILayout.Button("全選択", GUILayout.Width(80)))
                SetAllSelected(true);
            if (GUILayout.Button("全解除", GUILayout.Width(80)))
                SetAllSelected(false);
            if (GUILayout.Button("Exporterの配布フォルダ", GUILayout.Width(140)))
                LoadDirectoryFromPackageExporter();
            EditorGUILayout.EndHorizontal();
        }

        void LoadDirectoryFromPackageExporter()
        {
            const string exporterSourceFolderKey = "SamiVRCBlocksAvatar.PackageExporter.SourceFolder";
            var path = EditorPrefs.GetString(exporterSourceFolderKey, "");
            if (string.IsNullOrEmpty(path))
            {
                SetStatus("Package Exporter に配布フォルダが設定されていません。", MessageType.Warning);
                return;
            }

            SetDirectory(path);
            SetStatus($"Package Exporter の配布フォルダを読み込みました: {path}", MessageType.Info);
        }

        void DrawPrefabList()
        {
            _listFoldout = EditorGUILayout.Foldout(_listFoldout, $"プレファブ一覧 ({_entries.Count})", true);
            if (!_listFoldout)
                return;

            var maxListHeight = Mathf.Max(ListHeightMin, position.height - 280f);
            _listHeight = Mathf.Clamp(_listHeight, ListHeightMin, maxListHeight);

            _listScroll = EditorGUILayout.BeginScrollView(
                _listScroll,
                GUILayout.Height(_listHeight),
                GUILayout.ExpandWidth(true));
            if (_entries.Count == 0)
            {
                EditorGUILayout.LabelField("プレファブがありません。");
            }
            else
            {
                foreach (var e in _entries)
                {
                    EditorGUILayout.BeginHorizontal();
                    e.selected = EditorGUILayout.Toggle(e.selected, GUILayout.Width(18));
                    if (GUILayout.Button(e.name, EditorStyles.linkLabel))
                    {
                        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(e.assetPath);
                        if (obj != null)
                        {
                            Selection.activeObject = obj;
                            EditorGUIUtility.PingObject(obj);
                        }
                    }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(e.assetPath, EditorStyles.miniLabel);
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndScrollView();

            DrawListHeightSplitter(maxListHeight);
        }

        void DrawListHeightSplitter(float maxListHeight)
        {
            var rect = EditorGUILayout.GetControlRect(false, 8f);
            rect.y += 2f;
            rect.height = 4f;
            EditorGUI.DrawRect(rect, new Color(0.35f, 0.35f, 0.35f, 0.55f));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeVertical);

            var id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (rect.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        _draggingListSplitter = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id && _draggingListSplitter)
                    {
                        _listHeight = Mathf.Clamp(_listHeight + e.delta.y, ListHeightMin, maxListHeight);
                        EditorPrefs.SetFloat(EditorPrefsKeyListHeight, _listHeight);
                        Repaint();
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        _draggingListSplitter = false;
                        EditorPrefs.SetFloat(EditorPrefsKeyListHeight, _listHeight);
                        e.Use();
                    }
                    break;
            }
        }

        void DrawActions()
        {
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_entries.Count(e => e.selected) == 0))
            {
                if (GUILayout.Button("選択プレファブを解析", GUILayout.Height(28)))
                    AnalyzeSelected();
            }

            var hasResults = _entries.Any(e => e.result != null && e.result.Success);
            using (new EditorGUI.DisabledScope(!hasResults))
            {
                if (GUILayout.Button("結果をコピー", GUILayout.Height(28)))
                    CopyAllResults();
                if (GUILayout.Button("結果をファイルに書き出し", GUILayout.Height(28)))
                    ExportResults();
            }
            EditorGUILayout.EndHorizontal();

            string boothFolder = null;
            var canResolveBooth = PackageExporter.TryGetBoothInformationOutputFolder(_directoryPath, out boothFolder);
            if (PackageExporter.IsUnderSamirin33Folder(_directoryPath))
            {
                EditorGUILayout.Space(2);
                using (new EditorGUI.DisabledScope(!(hasResults && canResolveBooth)))
                {
                    var label = canResolveBooth
                        ? $"Booth Information に解析結果を書き出し ({boothFolder})"
                        : "Booth Information に解析結果を書き出し（出力先を解決できません）";
                    if (GUILayout.Button(label, GUILayout.Height(24)))
                        ExportResultsToBoothInformation();
                }
            }
        }

        void DrawResults()
        {
            var analyzed = _entries.Where(e => e.result != null).ToList();
            _resultFoldout = EditorGUILayout.Foldout(_resultFoldout, $"解析結果 ({analyzed.Count})", true);
            if (!_resultFoldout)
                return;

            _resultScroll = EditorGUILayout.BeginScrollView(_resultScroll, GUILayout.ExpandHeight(true));
            if (analyzed.Count == 0)
            {
                EditorGUILayout.LabelField("まだ解析結果がありません。");
            }
            else
            {
                foreach (var e in analyzed)
                {
                    DrawResultBlock(e);
                    EditorGUILayout.Space(8);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawResultBlock(PrefabEntry e)
        {
            var r = e.result;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(e.name, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(r == null || !r.Success))
            {
                if (GUILayout.Button("コピー", GUILayout.Width(60)))
                    CopyResult(e);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(e.assetPath, EditorStyles.miniLabel);

            if (!r.Success)
            {
                EditorGUILayout.HelpBox(r.ErrorMessage ?? "解析に失敗しました。", MessageType.Error);
                EditorGUILayout.EndVertical();
                return;
            }

            DrawMetricRow("ポリゴン数", FormatNullable(r.PolygonCount));
            DrawMetricRow("頂点数", FormatNullable(r.VertexCount));
            DrawMetricRow("マテリアル数", FormatNullable(r.MaterialCount));
            DrawMetricRow("マテリアルスロット数", FormatNullable(r.MaterialSlotCount));
            DrawMetricRow("テクスチャサイズ", r.TextureSizeBytes.HasValue ? FormatBytes(r.TextureSizeBytes.Value) : "-");
            DrawMetricRow("PhysBone数", FormatNullable(r.PhysBoneCount));
            DrawMetricRow("PhysBoneコライダー数", FormatNullable(r.PhysBoneColliderCount));
            DrawMetricRow("ライト数", FormatNullable(r.LightCount));
            DrawMetricRow("カメラ数", FormatNullable(r.CameraCount));
            DrawMetricRow("同期パラメーター", FormatNullable(r.SyncParameterBits) + " ビット");

            if (!string.IsNullOrEmpty(r.WarningMessage))
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.HelpBox(r.WarningMessage, MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        static void DrawMetricRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(180));
            EditorGUILayout.LabelField(value ?? "-");
            EditorGUILayout.EndHorizontal();
        }

        void CopyAllResults()
        {
            var analyzed = _entries.Where(e => e.result != null && e.result.Success).ToList();
            if (analyzed.Count == 0)
            {
                SetStatus("コピーする解析結果がありません。", MessageType.Warning);
                return;
            }

            var sb = new StringBuilder();
            for (var i = 0; i < analyzed.Count; i++)
            {
                if (i > 0)
                    sb.AppendLine().AppendLine("---").AppendLine();
                sb.Append(FormatResultText(analyzed[i]));
            }

            EditorGUIUtility.systemCopyBuffer = sb.ToString();
            SetStatus($"{analyzed.Count} 件の解析結果をクリップボードにコピーしました。", MessageType.Info);
        }

        void CopyResult(PrefabEntry entry)
        {
            if (entry?.result == null || !entry.result.Success)
            {
                SetStatus("コピーする解析結果がありません。", MessageType.Warning);
                return;
            }

            EditorGUIUtility.systemCopyBuffer = FormatResultText(entry);
            SetStatus($"{entry.name} の解析結果をクリップボードにコピーしました。", MessageType.Info);
        }

        static string FormatResultText(PrefabEntry entry)
        {
            var r = entry.result;
            var sb = new StringBuilder();
            sb.AppendLine(entry.name);
            sb.AppendLine(entry.assetPath);
            sb.AppendLine($"ポリゴン数: {FormatNullable(r.PolygonCount)}");
            sb.AppendLine($"頂点数: {FormatNullable(r.VertexCount)}");
            sb.AppendLine($"マテリアル数: {FormatNullable(r.MaterialCount)}");
            sb.AppendLine($"マテリアルスロット数: {FormatNullable(r.MaterialSlotCount)}");
            sb.AppendLine($"テクスチャサイズ: {(r.TextureSizeBytes.HasValue ? FormatBytes(r.TextureSizeBytes.Value) : "-")}");
            sb.AppendLine($"PhysBone数: {FormatNullable(r.PhysBoneCount)}");
            sb.AppendLine($"PhysBoneコライダー数: {FormatNullable(r.PhysBoneColliderCount)}");
            sb.AppendLine($"ライト数: {FormatNullable(r.LightCount)}");
            sb.AppendLine($"カメラ数: {FormatNullable(r.CameraCount)}");
            sb.Append($"同期パラメーター: {FormatNullable(r.SyncParameterBits)} ビット");
            if (!string.IsNullOrEmpty(r.WarningMessage))
            {
                sb.AppendLine();
                sb.Append($"警告: {r.WarningMessage}");
            }
            return sb.ToString();
        }

        void RestoreDirectorySelection()
        {
            if (string.IsNullOrEmpty(_directoryPath))
                return;

            _directoryPath = _directoryPath.Replace("\\", "/").TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(_directoryPath))
            {
                _directoryAsset = null;
                return;
            }

            _directoryAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_directoryPath);
            ScanPrefabs();
        }

        void ScanPrefabs()
        {
            _entries.Clear();
            if (string.IsNullOrEmpty(_directoryPath) || !AssetDatabase.IsValidFolder(_directoryPath))
            {
                SetStatus("有効なディレクトリを指定してください。", MessageType.Warning);
                return;
            }

            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { _directoryPath });
            foreach (var guid in guids.OrderBy(g => AssetDatabase.GUIDToAssetPath(g), StringComparer.OrdinalIgnoreCase))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    continue;

                _entries.Add(new PrefabEntry
                {
                    assetPath = path,
                    name = Path.GetFileNameWithoutExtension(path),
                    selected = true
                });
            }

            SetStatus($"{_entries.Count} 件のプレファブを検出しました。", MessageType.Info);
        }

        void SetAllSelected(bool selected)
        {
            foreach (var e in _entries)
                e.selected = selected;
        }

        void AnalyzeSelected()
        {
            var targets = _entries.Where(e => e.selected).ToList();
            if (targets.Count == 0)
            {
                SetStatus("解析対象のプレファブが選択されていません。", MessageType.Warning);
                return;
            }

            var lilOk = LilAvatarUtilsBridge.IsAvailable;
            var maOk = MaInformationBridge.IsAvailable;

            if (!lilOk && !maOk)
            {
                EditorUtility.DisplayDialog(
                    "Item Analyzer",
                    "lilAvatarUtils と MA Information (nadena.dev.ndmf / Modular Avatar) の両方が利用できません。\n必要なパッケージを導入してください。",
                    "OK");
                SetStatus("依存パッケージが利用できないため解析できません。", MessageType.Error);
                return;
            }

            if (!lilOk)
            {
                EditorUtility.DisplayDialog(
                    "Item Analyzer",
                    "lilAvatarUtils がプロジェクトに見つかりません。\nポリゴン数・頂点数・マテリアル・テクスチャ・PhysBone・ライト・カメラの情報は取得できません。",
                    "OK");
            }

            if (!maOk)
            {
                EditorUtility.DisplayDialog(
                    "Item Analyzer",
                    "MA Information (NDMF ParameterInfo) が利用できません。\n同期パラメーター数は取得できません。\nModular Avatar / NDMF を導入してください。",
                    "OK");
            }

            try
            {
                for (var i = 0; i < targets.Count; i++)
                {
                    var e = targets[i];
                    EditorUtility.DisplayProgressBar(
                        "Item Analyzer",
                        $"解析中: {e.name} ({i + 1}/{targets.Count})",
                        (float)i / targets.Count);

                    e.result = AnalyzePrefab(e.assetPath, lilOk, maOk);
                }

                SetStatus($"{targets.Count} 件のプレファブを解析しました。", MessageType.Info);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Repaint();
        }

        static PrefabAnalysisResult AnalyzePrefab(string assetPath, bool useLil, bool useMa)
        {
            var result = new PrefabAnalysisResult { AssetPath = assetPath, PrefabName = Path.GetFileNameWithoutExtension(assetPath) };
            var warnings = new List<string>();

            GameObject root = null;
            var loadedContents = false;
            try
            {
                root = PrefabUtility.LoadPrefabContents(assetPath);
                loadedContents = root != null;
                if (root == null)
                    root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

                if (root == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "プレファブを読み込めませんでした。";
                    return result;
                }

                if (useLil)
                {
                    try
                    {
                        LilAvatarUtilsBridge.Fill(root, result);
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"lilAvatarUtils 解析エラー: {ex.Message}");
                    }
                }
                else
                {
                    warnings.Add("lilAvatarUtils 未導入のためメッシュ/マテリアル/テクスチャ/PhysBone/ライト/カメラは未取得");
                }

                if (useMa)
                {
                    try
                    {
                        MaInformationBridge.Fill(root, result);
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"MA Information 解析エラー: {ex.Message}");
                    }
                }
                else
                {
                    warnings.Add("MA Information 未導入のため同期パラメーターは未取得");
                }

                result.Success = true;
                if (warnings.Count > 0)
                    result.WarningMessage = string.Join("\n", warnings);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            finally
            {
                if (loadedContents && root != null)
                    PrefabUtility.UnloadPrefabContents(root);
            }

            return result;
        }

        void ExportResults()
        {
            var rows = _entries.Where(e => e.result != null && e.result.Success).ToList();
            if (rows.Count == 0)
            {
                SetStatus("書き出す解析結果がありません。", MessageType.Warning);
                return;
            }

            var defaultName = $"ItemAnalyzer_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            var path = EditorUtility.SaveFilePanel("解析結果の書き出し", Application.dataPath, defaultName, "csv");
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                File.WriteAllText(path, BuildCsvText(rows), new UTF8Encoding(true));
                EditorUtility.RevealInFinder(path);
                SetStatus($"書き出しました: {path}", MessageType.Info);
            }
            catch (Exception ex)
            {
                SetStatus($"書き出しに失敗しました: {ex.Message}", MessageType.Error);
            }
        }

        void ExportResultsToBoothInformation()
        {
            var rows = _entries.Where(e => e.result != null && e.result.Success).ToList();
            if (rows.Count == 0)
            {
                SetStatus("書き出す解析結果がありません。", MessageType.Warning);
                return;
            }

            if (!PackageExporter.TryGetBoothInformationOutputFolder(_directoryPath, out var boothFolder, createIfMissing: true))
            {
                SetStatus("Booth Information の出力先を解決できません。ディレクトリが Assets/samirin33 配下か確認してください。", MessageType.Error);
                return;
            }

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"# Item Analysis");
                sb.AppendLine($"Source: {_directoryPath}");
                sb.AppendLine($"Output: {boothFolder}");
                sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine();

                for (var i = 0; i < rows.Count; i++)
                {
                    if (i > 0)
                        sb.AppendLine().AppendLine("---").AppendLine();
                    sb.Append(FormatResultText(rows[i]));
                }

                var path = PackageExporter.WriteTextToBoothInformationFolder(
                    _directoryPath,
                    PackageExporter.ItemAnalysisFileName,
                    sb.ToString());

                if (string.IsNullOrEmpty(path))
                {
                    SetStatus("Booth Information への書き出しに失敗しました。", MessageType.Error);
                    return;
                }

                EditorUtility.RevealInFinder(path);
                SetStatus($"Booth Information に書き出しました: {boothFolder}/{PackageExporter.ItemAnalysisFileName}", MessageType.Info);
            }
            catch (Exception ex)
            {
                SetStatus($"Booth Information への書き出しに失敗しました: {ex.Message}", MessageType.Error);
            }
        }

        static string BuildCsvText(List<PrefabEntry> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", new[]
            {
                "Name",
                "Path",
                "Polygons",
                "Vertices",
                "Materials",
                "MaterialSlots",
                "TextureSizeBytes",
                "TextureSize",
                "PhysBones",
                "PhysBoneColliders",
                "Lights",
                "Cameras",
                "SyncParameterBits",
                "Warnings"
            }));

            foreach (var e in rows)
            {
                var r = e.result;
                sb.AppendLine(string.Join(",", new[]
                {
                    Csv(r.PrefabName),
                    Csv(r.AssetPath),
                    Csv(FormatNullable(r.PolygonCount)),
                    Csv(FormatNullable(r.VertexCount)),
                    Csv(FormatNullable(r.MaterialCount)),
                    Csv(FormatNullable(r.MaterialSlotCount)),
                    Csv(r.TextureSizeBytes.HasValue ? r.TextureSizeBytes.Value.ToString() : ""),
                    Csv(r.TextureSizeBytes.HasValue ? FormatBytes(r.TextureSizeBytes.Value) : ""),
                    Csv(FormatNullable(r.PhysBoneCount)),
                    Csv(FormatNullable(r.PhysBoneColliderCount)),
                    Csv(FormatNullable(r.LightCount)),
                    Csv(FormatNullable(r.CameraCount)),
                    Csv(FormatNullable(r.SyncParameterBits)),
                    Csv(r.WarningMessage ?? "")
                }));
            }

            return sb.ToString();
        }

        void SetStatus(string message, MessageType type)
        {
            _statusMessage = message;
            _statusType = type;
        }

        static string FormatNullable(int? value) => value.HasValue ? value.Value.ToString() : "-";

        static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            if (bytes < 1024L * 1024)
                return $"{bytes / 1024.0:0.##} KB";
            return $"{bytes / (1024.0 * 1024.0):0.##} MB";
        }

        static string Csv(string value)
        {
            if (value == null)
                return "\"\"";
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }
    }

    sealed class PrefabAnalysisResult
    {
        public string PrefabName;
        public string AssetPath;
        public bool Success;
        public string ErrorMessage;
        public string WarningMessage;

        public int? PolygonCount;
        public int? VertexCount;
        public int? MaterialCount;
        public int? MaterialSlotCount;
        public long? TextureSizeBytes;
        public int? PhysBoneCount;
        public int? PhysBoneColliderCount;
        public int? LightCount;
        public int? CameraCount;
        public int? SyncParameterBits;
    }

    /// <summary>
    /// lilAvatarUtils の解析ロジック（AvatarUtils.Analyze 相当）をリフレクション経由で利用します。
    /// </summary>
    static class LilAvatarUtilsBridge
    {
        static bool? _available;
        static Type _avatarUtilsType;
        static MethodInfo _analyzeMethod;
        static FieldInfo _gameObjectField;
        static FieldInfo _texturesGuiField;
        static FieldInfo _materialsGuiField;
        static FieldInfo _renderersGuiField;
        static FieldInfo _physBonesGuiField;
        static bool _resolved;

        public static bool IsAvailable
        {
            get
            {
                EnsureResolved();
                return _available == true;
            }
        }

        static void EnsureResolved()
        {
            if (_resolved)
                return;
            _resolved = true;

            _avatarUtilsType = FindType("jp.lilxyzw.avatarutils.AvatarUtils");
            if (_avatarUtilsType == null)
            {
                _available = false;
                return;
            }

            _analyzeMethod = _avatarUtilsType.GetMethod("Analyze", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _gameObjectField = _avatarUtilsType.GetField("gameObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _texturesGuiField = _avatarUtilsType.GetField("texturesGUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _materialsGuiField = _avatarUtilsType.GetField("materialsGUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _renderersGuiField = _avatarUtilsType.GetField("renderersGUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _physBonesGuiField = _avatarUtilsType.GetField("physBonesGUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            _available = _analyzeMethod != null && _gameObjectField != null;
        }

        public static void Fill(GameObject root, PrefabAnalysisResult result)
        {
            EnsureResolved();
            if (_available != true)
                throw new InvalidOperationException("lilAvatarUtils が利用できません。");

            // AvatarUtils.Analyze が内部 API のため失敗する場合に備え、同等ロジックでも集計する。
            try
            {
                if (TryFillViaAvatarUtils(root, result))
                {
                    FillLightsAndCameras(root, result);
                    return;
                }
            }
            catch
            {
                // fall through to equivalent analysis
            }

            FillEquivalentToLilAvatarUtils(root, result);
            FillLightsAndCameras(root, result);
        }

        static bool TryFillViaAvatarUtils(GameObject root, PrefabAnalysisResult result)
        {
            var window = ScriptableObject.CreateInstance(_avatarUtilsType);
            try
            {
                _gameObjectField.SetValue(window, root);
                _analyzeMethod.Invoke(window, null);

                // Materials
                if (_materialsGuiField != null)
                {
                    var materialsGui = _materialsGuiField.GetValue(window);
                    var mds = GetMemberValue(materialsGui, "mds") as IEnumerable;
                    result.MaterialCount = mds?.Cast<object>().Count(o => o != null) ?? 0;
                }

                // Textures + VRAM
                if (_texturesGuiField != null)
                {
                    var texturesGui = _texturesGuiField.GetValue(window);
                    if (GetMemberValue(texturesGui, "tds") is IDictionary tds)
                    {
                        long total = 0;
                        foreach (DictionaryEntry entry in tds)
                        {
                            var td = entry.Value;
                            if (td == null)
                                continue;
                            var vram = GetMemberValue(td, "vramSize");
                            if (vram is long l)
                                total += l;
                            else if (vram is int i)
                                total += i;
                        }
                        result.TextureSizeBytes = total;
                    }
                }

                // Renderers
                if (_renderersGuiField != null)
                {
                    var renderersGui = _renderersGuiField.GetValue(window);
                    int polys = 0, verts = 0, slots = 0;

                    if (GetMemberValue(renderersGui, "smrs") is IEnumerable smrs)
                    {
                        foreach (var item in smrs)
                        {
                            if (!(item is SkinnedMeshRenderer smr) || smr == null)
                                continue;
                            slots += smr.sharedMaterials?.Length ?? 0;
                            if (smr.sharedMesh != null)
                            {
                                polys += smr.sharedMesh.triangles.Length / 3;
                                verts += smr.sharedMesh.vertexCount;
                            }
                        }
                    }

                    if (GetMemberValue(renderersGui, "mrs") is IEnumerable mrs)
                    {
                        foreach (var item in mrs)
                        {
                            MeshRenderer mr = null;
                            MeshFilter mf = null;
                            if (item is ValueTuple<MeshRenderer, MeshFilter> tuple)
                            {
                                mr = tuple.Item1;
                                mf = tuple.Item2;
                            }
                            else
                            {
                                var itemType = item.GetType();
                                if (itemType.IsGenericType && itemType.Name.StartsWith("ValueTuple", StringComparison.Ordinal))
                                {
                                    mr = itemType.GetField("Item1")?.GetValue(item) as MeshRenderer;
                                    mf = itemType.GetField("Item2")?.GetValue(item) as MeshFilter;
                                }
                            }

                            if (mr != null)
                                slots += mr.sharedMaterials?.Length ?? 0;
                            if (mf != null && mf.sharedMesh != null)
                            {
                                polys += mf.sharedMesh.triangles.Length / 3;
                                verts += mf.sharedMesh.vertexCount;
                            }
                        }
                    }

                    if (GetMemberValue(renderersGui, "psrs") is IEnumerable psrs)
                    {
                        foreach (var item in psrs)
                        {
                            if (item is ParticleSystemRenderer psr && psr != null)
                                slots += psr.sharedMaterials?.Length ?? 0;
                        }
                    }

                    result.PolygonCount = polys;
                    result.VertexCount = verts;
                    result.MaterialSlotCount = slots;
                }

                // PhysBones
                if (_physBonesGuiField != null)
                {
                    var physBonesGui = _physBonesGuiField.GetValue(window);
                    if (GetMemberValue(physBonesGui, "pbs") is IEnumerable pbs)
                        result.PhysBoneCount = pbs.Cast<object>().Count(o => o != null);
                    if (GetMemberValue(physBonesGui, "pbcs") is IEnumerable pbcs)
                        result.PhysBoneColliderCount = pbcs.Cast<object>().Count(o => o != null);
                }
                else
                {
                    FillPhysBonesByType(root, result);
                }

                return result.PolygonCount.HasValue || result.MaterialCount.HasValue || result.TextureSizeBytes.HasValue;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        /// <summary>
        /// lilAvatarUtils.AvatarUtils.Analyze と同等の集計（パッケージ導入済み時のフォールバック）。
        /// </summary>
        static void FillEquivalentToLilAvatarUtils(GameObject root, PrefabAnalysisResult result)
        {
            var refs = CollectObjectReferences(root);

            var materials = new HashSet<Material>();
            var textures = new HashSet<Texture>();
            foreach (var obj in refs)
            {
                if (obj is Material mat)
                    materials.Add(mat);
                else if (obj is Texture tex)
                    textures.Add(tex);
            }

            result.MaterialCount = materials.Count;
            result.TextureSizeBytes = textures.Sum(EstimateVramSize);

            int polys = 0, verts = 0, slots = 0;
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(c => !IsEditorOnly(c)))
            {
                slots += smr.sharedMaterials?.Length ?? 0;
                if (smr.sharedMesh == null)
                    continue;
                polys += smr.sharedMesh.triangles.Length / 3;
                verts += smr.sharedMesh.vertexCount;
            }

            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true).Where(c => !IsEditorOnly(c)))
            {
                slots += mr.sharedMaterials?.Length ?? 0;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null)
                    continue;
                polys += mf.sharedMesh.triangles.Length / 3;
                verts += mf.sharedMesh.vertexCount;
            }

            foreach (var psr in root.GetComponentsInChildren<ParticleSystemRenderer>(true).Where(c => !IsEditorOnly(c)))
                slots += psr.sharedMaterials?.Length ?? 0;

            result.PolygonCount = polys;
            result.VertexCount = verts;
            result.MaterialSlotCount = slots;

            FillPhysBonesByType(root, result);
        }

        static void FillPhysBonesByType(GameObject root, PrefabAnalysisResult result)
        {
            var pbType = FindType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone")
                         ?? FindType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneBase");
            var pbcType = FindType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider")
                          ?? FindType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneColliderBase");

            if (pbType != null)
                result.PhysBoneCount = root.GetComponentsInChildren(pbType, true).Count(c => c != null && !IsEditorOnly(c));
            else
                result.PhysBoneCount = 0;

            if (pbcType != null)
                result.PhysBoneColliderCount = root.GetComponentsInChildren(pbcType, true).Count(c => c != null && !IsEditorOnly(c));
            else
                result.PhysBoneColliderCount = 0;
        }

        static void FillLightsAndCameras(GameObject root, PrefabAnalysisResult result)
        {
            result.LightCount = root.GetComponentsInChildren<Light>(true).Count(c => !IsEditorOnly(c));
            result.CameraCount = root.GetComponentsInChildren<Camera>(true).Count(c => !IsEditorOnly(c));
        }

        static HashSet<UnityEngine.Object> CollectObjectReferences(GameObject gameObject)
        {
            var refs = new HashSet<UnityEngine.Object>();
            foreach (var c in gameObject.GetComponentsInChildren<Component>(true))
                CollectReferenceFromObject(refs, null, c, null);
            return refs;
        }

        static void CollectReferenceFromObject(
            HashSet<UnityEngine.Object> refs,
            HashSet<string> scannedMaterialProperties,
            UnityEngine.Object obj,
            UnityEngine.Object parent)
        {
            if (!obj)
                return;
            if (obj is GameObject go && IsEditorOnly(go))
                return;
            if (obj is Component c && IsEditorOnly(c))
                return;

            if (!refs.Add(obj))
                return;

            if (obj is GameObject ||
                obj is Transform ||
                obj is Mesh ||
                obj is Texture ||
                obj is Shader ||
                obj is TextAsset ||
                obj.GetType() == typeof(UnityEngine.Object))
                return;

            using var so = new SerializedObject(obj);
            if (obj is Material m)
            {
                scannedMaterialProperties ??= new HashSet<string>();
                var texs = so.FindProperty("m_SavedProperties.m_TexEnvs");
                if (texs != null)
                {
                    var size = texs.arraySize;
                    for (var i = 0; i < size; i++)
                    {
                        var data = texs.GetArrayElementAtIndex(i);
                        var name = data.FindPropertyRelative("first").stringValue;
                        if (!scannedMaterialProperties.Add(name))
                            continue;
                        var tex = data.FindPropertyRelative("second.m_Texture");
                        CollectReferenceFromObject(refs, scannedMaterialProperties, tex.objectReferenceValue, obj);
                    }
                }

                if (m.parent != null)
                    CollectReferenceFromObject(refs, scannedMaterialProperties, m.parent, obj);
                return;
            }

            var iter = so.GetIterator();
            var enterChildren = true;
            while (iter.Next(enterChildren))
            {
                enterChildren = iter.propertyType != SerializedPropertyType.String;
                if (iter.propertyType == SerializedPropertyType.ObjectReference && iter.name != "m_CorrespondingSourceObject")
                    CollectReferenceFromObject(refs, scannedMaterialProperties, iter.objectReferenceValue, obj);
            }
        }

        static long EstimateVramSize(Texture t)
        {
            if (t == null)
                return 0;

            try
            {
                var mathHelper = FindType("jp.lilxyzw.avatarutils.MathHelper");
                if (mathHelper != null)
                {
                    MethodInfo method = null;
                    object formatArg = null;
                    switch (t)
                    {
                        case Texture2D o:
                            method = mathHelper.GetMethod("ComputeVRAMSize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Texture), typeof(TextureFormat) }, null);
                            formatArg = o.format;
                            break;
                        case Cubemap o:
                            method = mathHelper.GetMethod("ComputeVRAMSize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Texture), typeof(TextureFormat) }, null);
                            formatArg = o.format;
                            break;
                        case Texture3D o:
                            method = mathHelper.GetMethod("ComputeVRAMSize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Texture), typeof(TextureFormat) }, null);
                            formatArg = o.format;
                            break;
                        case Texture2DArray o:
                            method = mathHelper.GetMethod("ComputeVRAMSize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Texture), typeof(TextureFormat) }, null);
                            formatArg = o.format;
                            break;
                        case CubemapArray o:
                            method = mathHelper.GetMethod("ComputeVRAMSize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Texture), typeof(TextureFormat) }, null);
                            formatArg = o.format;
                            break;
                        case RenderTexture o:
                            method = mathHelper.GetMethod("ComputeVRAMSize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(RenderTexture), typeof(RenderTextureFormat) }, null);
                            formatArg = o.format;
                            break;
                    }

                    if (method != null && formatArg != null)
                        return (long)method.Invoke(null, new object[] { t, formatArg });
                }
            }
            catch
            {
                // fall through
            }

            // 簡易推定（bpp=32 相当）
            double pixels = (double)t.width * t.height;
            switch (t)
            {
                case Texture3D o: pixels *= o.depth; break;
                case Texture2DArray o: pixels *= o.depth; break;
                case Cubemap _: pixels *= 6; break;
                case CubemapArray o: pixels *= 6 * o.cubemapCount; break;
            }

            double mipPixels = 0;
            for (var i = 0; i < t.mipmapCount; i++)
                mipPixels += pixels / Math.Pow(Math.Pow(2, i), 2);
            return (long)(mipPixels * 4);
        }

        static bool IsEditorOnly(Component c) => c != null && IsEditorOnly(c.transform);
        static bool IsEditorOnly(GameObject go) => go != null && IsEditorOnly(go.transform);

        static bool IsEditorOnly(Transform t)
        {
            while (t != null)
            {
                if (t.CompareTag("EditorOnly"))
                    return true;
                t = t.parent;
            }
            return false;
        }

        static object GetMemberValue(object target, string name)
        {
            if (target == null)
                return null;
            var type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var field = type.GetField(name, flags);
            if (field != null)
                return field.GetValue(target);
            var prop = type.GetProperty(name, flags);
            return prop?.GetValue(target);
        }

        static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = null;
                try
                {
                    type = assembly.GetType(fullName, false);
                }
                catch
                {
                    // ignored
                }

                if (type != null)
                    return type;
            }
            return null;
        }
    }

    /// <summary>
    /// MA Information が利用する NDMF ParameterInfo をリフレクション経由で呼び出します。
    /// </summary>
    static class MaInformationBridge
    {
        static bool? _available;
        static bool _resolved;
        static object _forUi;
        static MethodInfo _getParametersForObject;

        public static bool IsAvailable
        {
            get
            {
                EnsureResolved();
                return _available == true;
            }
        }

        static void EnsureResolved()
        {
            if (_resolved)
                return;
            _resolved = true;

            // MA Information ウィンドウと、それが利用する NDMF ParameterInfo の両方が必要
            var maInfoWindowType = FindType("nadena.dev.modular_avatar.core.editor.ParamsUsageWindow");
            var parameterInfoType = FindType("nadena.dev.ndmf.ParameterInfo");
            if (maInfoWindowType == null || parameterInfoType == null)
            {
                _available = false;
                return;
            }

            var forUiField = parameterInfoType.GetField("ForUI", BindingFlags.Public | BindingFlags.Static);
            _forUi = forUiField?.GetValue(null);
            _getParametersForObject = parameterInfoType.GetMethod(
                "GetParametersForObject",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(GameObject), FindType("nadena.dev.ndmf.ParameterInfo+ConflictHandler") ?? typeof(Delegate) },
                null);

            // ConflictHandler は optional（null 可）。シグネチャ不一致時は引数1つのオーバーロードを探す。
            if (_getParametersForObject == null)
            {
                foreach (var m in parameterInfoType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name != "GetParametersForObject")
                        continue;
                    var ps = m.GetParameters();
                    if (ps.Length >= 1 && ps[0].ParameterType == typeof(GameObject))
                    {
                        _getParametersForObject = m;
                        break;
                    }
                }
            }

            _available = _forUi != null && _getParametersForObject != null;
        }

        public static void Fill(GameObject root, PrefabAnalysisResult result)
        {
            EnsureResolved();
            if (_available != true)
                throw new InvalidOperationException("MA Information (NDMF ParameterInfo) が利用できません。");

            object raw;
            var parametersCount = _getParametersForObject.GetParameters().Length;
            if (parametersCount == 1)
                raw = _getParametersForObject.Invoke(_forUi, new object[] { root });
            else
                raw = _getParametersForObject.Invoke(_forUi, new object[] { root, null });

            if (!(raw is IEnumerable enumerable))
            {
                result.SyncParameterBits = 0;
                return;
            }

            // MA Information (ParamsUsageWindow) と同じく BitUsage のみ集計する
            var bits = 0;
            foreach (var p in enumerable)
            {
                if (p == null)
                    continue;

                var bitUsageObj = GetMemberValue(p, "BitUsage");
                var bitUsage = bitUsageObj is int i ? i : 0;
                if (bitUsage > 0)
                    bits += bitUsage;
            }

            result.SyncParameterBits = bits;
        }

        static object GetMemberValue(object target, string name)
        {
            if (target == null)
                return null;
            var type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var prop = type.GetProperty(name, flags);
            if (prop != null)
                return prop.GetValue(target);
            var field = type.GetField(name, flags);
            return field?.GetValue(target);
        }

        static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = null;
                try
                {
                    type = assembly.GetType(fullName, false);
                }
                catch
                {
                    // ignored
                }

                if (type != null)
                    return type;
            }
            return null;
        }
    }
}
#endif
