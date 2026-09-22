using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Samirin33.Editor;

namespace SamiVRCBlocksAvatar.Editor
{
    public class PackageExporterWindow : EditorWindow
    {
        const string EditorPrefsKeyOutputDirectory = "SamiVRCBlocksAvatar.PackageExporter.OutputDirectory";
        const string EditorPrefsKeySourceFolder = "SamiVRCBlocksAvatar.PackageExporter.SourceFolder";
        const string EditorPrefsKeySourceFolderHistory = "SamiVRCBlocksAvatar.PackageExporter.SourceFolderHistory";
        const int MaxSourceFolderHistory = 5;
        const string EditorPrefsKeyOverwrite = "SamiVRCBlocksAvatar.PackageExporter.Overwrite";
        const string EditorPrefsKeyIncludeInstaller = "SamiVRCBlocksAvatar.PackageExporter.IncludeInstaller";
        const string EditorPrefsKeyIncludeBoothManagerInstaller =
            "SamiVRCBlocksAvatar.PackageExporter.IncludeBoothManagerInstaller";
        const string EditorPrefsKeyResetExistingAssetsOnImport =
            "SamiVRCBlocksAvatar.PackageExporter.ResetExistingAssetsOnImport";
        const string EditorPrefsKeyUseVersionFolder = "SamiVRCBlocksAvatar.PackageExporter.UseVersionFolder";
        const string EditorPrefsKeyCreateZip = "SamiVRCBlocksAvatar.PackageExporter.CreateZip";

        string _sourceFolderPath = "";
        DefaultAsset _sourceFolderAsset;
        readonly List<string> _sourceFolderHistory = new List<string>();
        string _packageName = "";
        int _versionMajor = 1, _versionMinor = 0, _versionPatch = 0;
        string _outputDirectory = "";
        bool _overwrite = true;
        bool _includeInstaller = true;
        bool _includeBoothManagerInstaller = true;
        bool _resetExistingAssetsOnImport;
        bool _useVersionFolder;
        bool _createZip;

        PackageAssetInfo _assetInfo;
        UnityEngine.Object _boothAssetInfo;
        Vector2 _scrollPosition;
        bool _urlsFoldout = true;
        bool _releasesFoldout = true;
        bool _relatedFoldersFoldout = true;
        GUIStyle _sectionHeaderStyle;

        static readonly Color SectionBarColor = new Color(0.40f, 0.44f, 0.47f, 0.38f);
        static readonly Color SectionLabelColor = new Color(0.82f, 0.84f, 0.86f, 0.70f);

        [MenuItem("SBAvatarEditor/File/Package Exporter", false, 0)]
        public static void Open()
        {
            var w = GetWindow<PackageExporterWindow>(false, "Package Exporter", true);
            w.minSize = new Vector2(820, 480);
        }

        void OnEnable()
        {
            _outputDirectory = EditorPrefs.GetString(EditorPrefsKeyOutputDirectory, "");
            _overwrite = EditorPrefs.GetBool(EditorPrefsKeyOverwrite, false);
            _includeInstaller = EditorPrefs.GetBool(EditorPrefsKeyIncludeInstaller, false);
            _includeBoothManagerInstaller = EditorPrefs.GetBool(EditorPrefsKeyIncludeBoothManagerInstaller, true);
            _resetExistingAssetsOnImport = EditorPrefs.GetBool(EditorPrefsKeyResetExistingAssetsOnImport, false);
            _useVersionFolder = EditorPrefs.GetBool(EditorPrefsKeyUseVersionFolder, false);
            _createZip = EditorPrefs.GetBool(EditorPrefsKeyCreateZip, false);

            LoadSourceFolderHistory();

            // 最後に編集していた配布フォルダを復元
            if (string.IsNullOrEmpty(_sourceFolderPath))
                _sourceFolderPath = EditorPrefs.GetString(EditorPrefsKeySourceFolder, "");

            RestoreSourceFolderSelection();
            if (!string.IsNullOrEmpty(_sourceFolderPath) && AssetDatabase.IsValidFolder(_sourceFolderPath))
                RememberSourceFolder(_sourceFolderPath);
        }

        void RestoreSourceFolderSelection()
        {
            if (string.IsNullOrEmpty(_sourceFolderPath))
                return;

            _sourceFolderPath = _sourceFolderPath.Replace("\\", "/").TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(_sourceFolderPath))
            {
                _sourceFolderAsset = null;
                return;
            }

            _sourceFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_sourceFolderPath);
            LoadAssetInfoFromFolder();
            ResolveBoothAssetInfo();
        }

        void ResolveBoothAssetInfo()
        {
            if (!PackageExporter.IsUnderSamirin33Folder(_sourceFolderPath))
            {
                _boothAssetInfo = null;
                return;
            }
            _boothAssetInfo = PackageExporter.FindBoothAssetInfoForFolder(_sourceFolderPath);
        }

        void ApplyFromBoothAssetInfo()
        {
            if (!PackageExporter.IsUnderSamirin33Folder(_sourceFolderPath))
                return;

            if (_assetInfo == null)
                _assetInfo = new PackageAssetInfo();

            if (_boothAssetInfo == null)
                _boothAssetInfo = PackageExporter.FindBoothAssetInfoForFolder(_sourceFolderPath);

            if (!PackageExporter.TryApplyFromBoothAssetInfo(
                    _boothAssetInfo, _assetInfo, out var packageName, out var version))
            {
                EditorUtility.DisplayDialog(
                    "Package Exporter",
                    "SamirinBoothAssetInfo が見つからないか、読み取れませんでした。\n" +
                    "配布フォルダに対応する Asset Info を指定してください。",
                    "OK");
                return;
            }

            if (!string.IsNullOrEmpty(packageName))
                _packageName = packageName;
            if (!string.IsNullOrEmpty(version))
                ParseVersion(version, out _versionMajor, out _versionMinor, out _versionPatch);

            _urlsFoldout = true;
            _releasesFoldout = true;
        }

        void PersistSourceFolder(string path)
        {
            _sourceFolderPath = (path ?? "").Replace("\\", "/").TrimEnd('/');
            EditorPrefs.SetString(EditorPrefsKeySourceFolder, _sourceFolderPath);
            RememberSourceFolder(_sourceFolderPath);
        }

        void SelectSourceFolder(string path)
        {
            path = (path ?? "").Replace("\\", "/").TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(path))
                return;

            PersistSourceFolder(path);
            _sourceFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
            LoadAssetInfoFromFolder();
            ResolveBoothAssetInfo();
        }

        void LoadSourceFolderHistory()
        {
            _sourceFolderHistory.Clear();
            var raw = EditorPrefs.GetString(EditorPrefsKeySourceFolderHistory, "");
            if (string.IsNullOrEmpty(raw))
                return;

            var parts = raw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                var path = parts[i].Replace("\\", "/").Trim().TrimEnd('/');
                if (string.IsNullOrEmpty(path))
                    continue;
                if (ContainsSourceFolder(path))
                    continue;
                _sourceFolderHistory.Add(path);
                if (_sourceFolderHistory.Count >= MaxSourceFolderHistory)
                    break;
            }
        }

        void RememberSourceFolder(string path)
        {
            path = (path ?? "").Replace("\\", "/").TrimEnd('/');
            if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path))
                return;

            for (int i = _sourceFolderHistory.Count - 1; i >= 0; i--)
            {
                var existing = _sourceFolderHistory[i];
                if (string.Equals(existing, path, StringComparison.OrdinalIgnoreCase) ||
                    !AssetDatabase.IsValidFolder(existing))
                    _sourceFolderHistory.RemoveAt(i);
            }

            _sourceFolderHistory.Insert(0, path);
            if (_sourceFolderHistory.Count > MaxSourceFolderHistory)
                _sourceFolderHistory.RemoveRange(MaxSourceFolderHistory, _sourceFolderHistory.Count - MaxSourceFolderHistory);

            EditorPrefs.SetString(EditorPrefsKeySourceFolderHistory, string.Join("\n", _sourceFolderHistory));
        }

        bool ContainsSourceFolder(string path)
        {
            for (int i = 0; i < _sourceFolderHistory.Count; i++)
            {
                if (string.Equals(_sourceFolderHistory[i], path, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        void DrawSourceFolderHistory()
        {
            int visible = 0;
            for (int i = 0; i < _sourceFolderHistory.Count; i++)
            {
                if (AssetDatabase.IsValidFolder(_sourceFolderHistory[i]))
                    visible++;
            }
            if (visible == 0)
                return;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("履歴");
            string selected = null;
            for (int i = 0; i < _sourceFolderHistory.Count; i++)
            {
                var path = _sourceFolderHistory[i];
                if (!AssetDatabase.IsValidFolder(path))
                    continue;

                bool current = string.Equals(path, _sourceFolderPath, StringComparison.OrdinalIgnoreCase);
                var content = new GUIContent(GetSourceFolderHistoryLabel(path), path);
                using (new EditorGUI.DisabledScope(current))
                {
                    if (GUILayout.Button(content, EditorStyles.miniButton, GUILayout.Height(18f), GUILayout.MaxWidth(140f)))
                        selected = path;
                }
            }
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(selected))
                SelectSourceFolder(selected);
        }

        string GetSourceFolderHistoryLabel(string path)
        {
            var name = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(name))
                return path;

            int sameName = 0;
            for (int i = 0; i < _sourceFolderHistory.Count; i++)
            {
                var other = _sourceFolderHistory[i];
                if (!AssetDatabase.IsValidFolder(other))
                    continue;
                if (string.Equals(System.IO.Path.GetFileName(other), name, StringComparison.OrdinalIgnoreCase))
                    sameName++;
            }

            if (sameName <= 1)
                return name;

            var parent = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path));
            return string.IsNullOrEmpty(parent) ? name : parent + "/" + name;
        }

        string GetVersionString() => $"{_versionMajor}.{_versionMinor}.{_versionPatch}";

        string GetExportDirectory(string outputDirectory, string packageName, string version)
        {
            var dir = outputDirectory ?? "";
            if (!_useVersionFolder || string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(version))
                return dir;
            return System.IO.Path.Combine(dir, $"{packageName}_ver{version}");
        }

        static void ParseVersion(string version, out int major, out int minor, out int patch)
        {
            major = 1; minor = 0; patch = 0;
            if (string.IsNullOrEmpty(version)) return;
            var parts = version.Trim().Split('.');
            if (parts.Length > 0) int.TryParse(parts[0], out major);
            if (parts.Length > 1) int.TryParse(parts[1], out minor);
            if (parts.Length > 2) int.TryParse(parts[2], out patch);
            if (major < 0) major = 0;
            if (minor < 0) minor = 0;
            if (patch < 0) patch = 0;
        }

        static void DrawVersionIntField(string label, ref int value)
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(false));
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel, GUILayout.MinWidth(5));
            var newVal = EditorGUILayout.IntField(value);
            if (newVal != value) value = Mathf.Max(0, newVal);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("−"))
                value = Mathf.Max(0, value - 1);
            if (GUILayout.Button("+"))
                value++;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 指定フォルダ内の PackageAssetInfo.json をディスクから読み、releases の先頭（最新）を返す。編集内容ではなくファイルの内容を表示する用。
        /// </summary>
        PackageAssetInfo.ReleaseInfo GetLatestReleaseFromJsonFile()
        {
            if (string.IsNullOrEmpty(_sourceFolderPath)) return null;
            var fileInfo = PackageExporter.LoadAssetInfo(_sourceFolderPath);
            if (fileInfo?.releases == null || fileInfo.releases.Length == 0) return null;
            return fileInfo.releases[0];
        }

        void LoadAssetInfoFromFolder()
        {
            _assetInfo = PackageExporter.LoadAssetInfo(_sourceFolderPath);
            if (_assetInfo == null)
            {
                _assetInfo = new PackageAssetInfo
                {
                    name = _packageName,
                    version = GetVersionString(),
                    urls = new PackageAssetInfo.UrlInfo[0],
                    releases = new PackageAssetInfo.ReleaseInfo[0],
                    relatedFolders = new string[0],
                    superReimportFolders = new string[0]
                };
            }
            else
            {
                if (string.IsNullOrEmpty(_packageName)) _packageName = _assetInfo.name ?? "";
                if (!string.IsNullOrEmpty(_assetInfo.version))
                    ParseVersion(_assetInfo.version, out _versionMajor, out _versionMinor, out _versionPatch);
                if (_assetInfo.relatedFolders == null)
                    _assetInfo.relatedFolders = new string[0];
                if (_assetInfo.superReimportFolders == null)
                    _assetInfo.superReimportFolders = new string[0];
            }
        }

        void OnGUI()
        {
            SamirinEditorStyleHelper.DrawWithBlueBackground(() =>
            {
                EnsureAssetInfo();
                var versionStr = GetVersionString();
                var prevLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUILayout.BeginVertical();
                try
                {
                    EditorGUIUtility.labelWidth = 96f;
                    EditorGUILayout.Space(4);
                    SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont(
                        "配布向けUnityPackageを作成します。バージョンやアセット関連情報を管理、同梱できます。",
                        MessageType.Info);

                    float leftWidth = Mathf.Clamp((position.width - 40f) * 0.48f, 300f, 560f);
                    _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.BeginVertical(GUILayout.Width(leftWidth));
                    DrawSourceSection();
                    DrawBundleSection();
                    DrawOutputSection();
                    EditorGUILayout.EndVertical();
                    GUILayout.Space(8f);
                    EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
                    DrawPackageInfoSection();
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space(8);
                    EditorGUILayout.EndScrollView();

                    DrawExportFooter(versionStr);
                }
                finally
                {
                    EditorGUIUtility.labelWidth = prevLabelWidth;
                    EditorGUILayout.EndVertical();
                }
            }, new Rect(0, 0, position.width, position.height));
        }

        void EnsureAssetInfo()
        {
            if (_assetInfo == null && !string.IsNullOrEmpty(_sourceFolderPath))
                LoadAssetInfoFromFolder();
            if (_assetInfo != null)
                return;

            _assetInfo = new PackageAssetInfo
            {
                name = _packageName,
                version = GetVersionString(),
                urls = new PackageAssetInfo.UrlInfo[0],
                releases = new PackageAssetInfo.ReleaseInfo[0],
                relatedFolders = new string[0],
                superReimportFolders = new string[0]
            };
        }

        GUIStyle SectionHeaderStyle
        {
            get
            {
                if (_sectionHeaderStyle == null)
                {
                    _sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        fontSize = 12,
                        padding = new RectOffset(0, 0, 0, 0),
                        normal = { textColor = SectionLabelColor }
                    };
                }
                return _sectionHeaderStyle;
            }
        }

        void BeginSection(string title)
        {
            EditorGUILayout.Space(8);
            DrawSectionBar(title);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        }

        void EndSection()
        {
            EditorGUILayout.EndVertical();
        }

        void DrawSectionBar(string title)
        {
            var rect = EditorGUILayout.GetControlRect(false, 22f);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, SectionBarColor);
            GUI.Label(new Rect(rect.x + 8f, rect.y, rect.width - 12f, rect.height), title, SectionHeaderStyle);
        }

        void DrawSourceSection()
        {
            BeginSection("配布対象");
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            _sourceFolderAsset = (DefaultAsset)EditorGUILayout.ObjectField("配布フォルダ", _sourceFolderAsset, typeof(DefaultAsset), false);
            var sourceChanged = EditorGUI.EndChangeCheck();
            GUI.enabled = !string.IsNullOrEmpty(_sourceFolderPath) && AssetDatabase.IsValidFolder(_sourceFolderPath);
            if (GUILayout.Button("Item Analyzer", GUILayout.Width(110f), GUILayout.Height(18f)))
            {
                PersistSourceFolder(_sourceFolderPath);
                PackageExporter.OpenItemAnalyzer(_sourceFolderPath);
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            if (sourceChanged && _sourceFolderAsset != null)
            {
                var path = AssetDatabase.GetAssetPath(_sourceFolderAsset);
                if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                    SelectSourceFolder(path);
            }
            EditorGUI.BeginChangeCheck();
            _sourceFolderPath = EditorGUILayout.TextField("フォルダパス", _sourceFolderPath ?? "");
            if (EditorGUI.EndChangeCheck() && !string.IsNullOrEmpty(_sourceFolderPath))
            {
                var path = _sourceFolderPath.Replace("\\", "/").TrimEnd('/');
                if (AssetDatabase.IsValidFolder(path))
                    SelectSourceFolder(path);
            }
            if (!string.IsNullOrEmpty(_sourceFolderPath) && !AssetDatabase.IsValidFolder(_sourceFolderPath))
                EditorGUILayout.HelpBox("有効なプロジェクト内フォルダパスを指定してください（例: Assets/MyPackage）", MessageType.Warning);

            DrawSourceFolderHistory();

            _relatedFoldersFoldout = EditorGUILayout.Foldout(_relatedFoldersFoldout, "関連フォルダ", true);
            if (_relatedFoldersFoldout)
            {
                EditorGUI.indentLevel++;
                SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont(
                    "配布フォルダに加えて同梱するフォルダです。",
                    MessageType.Info);
                DrawRelatedFoldersList();
                EditorGUI.indentLevel--;
            }

            EndSection();
        }

        void DrawBundleSection()
        {
            BeginSection("同梱");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _includeInstaller = EditorGUILayout.ToggleLeft("SamiVRCBlocksAvatarInstaller を含める", _includeInstaller);
            SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont(
                "有効にすると Assets/SamiVRCBlocksAvatarInstaller として同梱されます。導入先でも Assets に展開され、SamiVRCBlocks-Avatar 関連スクリプトを使えるようになります。",
                MessageType.Info);
            EditorGUILayout.EndVertical();

            if (PackageExporter.IsUnderSamirin33Folder(_sourceFolderPath))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                _includeBoothManagerInstaller = EditorGUILayout.ToggleLeft(
                    "SamirinBoothManager インストーラを含める",
                    _includeBoothManagerInstaller);
                SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont(
                    "有効にすると、導入時に GitHub から SamirinBoothManager を自動取得するインストーラが同梱されます。",
                    MessageType.Info);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _resetExistingAssetsOnImport = EditorGUILayout.ToggleLeft(
                "インポート時に既存のアセットをリセットする。",
                _resetExistingAssetsOnImport);
            SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont(
                "指定したフォルダを導入先で一度削除してから再インポートします。",
                MessageType.Info);
            if (_resetExistingAssetsOnImport)
                DrawSuperReimportFoldersList();
            EditorGUILayout.EndVertical();
            EndSection();
        }

        void DrawPackageInfoSection()
        {
            BeginSection("パッケージ情報");
            EditorGUILayout.BeginHorizontal();
            _packageName = EditorGUILayout.TextField("パッケージ名", _packageName ?? "");
            _assetInfo.author = EditorGUILayout.TextField("作者", _assetInfo.author ?? "", GUILayout.MinWidth(120f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("バージョン");
            DrawVersionIntField("Major", ref _versionMajor);
            DrawVersionIntField("Minor", ref _versionMinor);
            DrawVersionIntField("Patch", ref _versionPatch);
            if (GUILayout.Button("現在のバージョン", GUILayout.ExpandWidth(false)))
            {
                var fromFile = PackageExporter.LoadAssetInfo(_sourceFolderPath);
                var ver = fromFile?.version;
                if (string.IsNullOrEmpty(ver) && _assetInfo != null) ver = _assetInfo.version;
                if (!string.IsNullOrEmpty(ver))
                    ParseVersion(ver, out _versionMajor, out _versionMinor, out _versionPatch);
            }
            EditorGUILayout.EndHorizontal();

            var latest = GetLatestReleaseFromJsonFile();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("保存済みの最終バージョン", EditorStyles.boldLabel);
            if (latest != null)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("バージョン", latest.version ?? "—");
                EditorGUILayout.LabelField("リリース日", latest.releaseDate ?? "—");
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.LabelField("フォルダ内に PackageAssetInfo.json がないか、releases がありません。");
            }
            EditorGUILayout.EndVertical();

            if (PackageExporter.IsUnderSamirin33Folder(_sourceFolderPath))
            {
                EditorGUILayout.Space(4);
                var boothType = PackageExporter.GetBoothAssetInfoType() ?? typeof(ScriptableObject);
                EditorGUI.BeginChangeCheck();
                _boothAssetInfo = EditorGUILayout.ObjectField(
                    "Booth Asset Info",
                    _boothAssetInfo,
                    boothType,
                    false);
                if (EditorGUI.EndChangeCheck() && _boothAssetInfo != null &&
                    !PackageExporter.IsBoothAssetInfo(_boothAssetInfo))
                {
                    _boothAssetInfo = null;
                }
                EditorGUILayout.BeginHorizontal();
                GUI.enabled = _boothAssetInfo != null || !string.IsNullOrEmpty(_sourceFolderPath);
                if (GUILayout.Button("Booth 情報を反映", GUILayout.Height(22)))
                    ApplyFromBoothAssetInfo();
                GUI.enabled = true;
                if (GUILayout.Button("再検索", GUILayout.Width(64), GUILayout.Height(22)))
                    ResolveBoothAssetInfo();
                EditorGUILayout.EndHorizontal();
                SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont(
                    "名前・説明・バージョン・作者(samirin33)・Booth/YouTube URL・更新履歴(updateInfos)を取り込みます。",
                    MessageType.Info);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("説明");
            _assetInfo.description = EditorGUILayout.TextArea(_assetInfo.description ?? "", GUILayout.MinHeight(44));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical();
            _releasesFoldout = EditorGUILayout.Foldout(_releasesFoldout, "Releases", true);
            if (_releasesFoldout)
            {
                EditorGUI.indentLevel++;
                DrawReleaseList();
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.BeginVertical();
            _urlsFoldout = EditorGUILayout.Foldout(_urlsFoldout, "関連URL", true);
            if (_urlsFoldout)
            {
                EditorGUI.indentLevel++;
                DrawUrlList();
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("vn3.org を開く", GUILayout.Height(22)))
                Application.OpenURL("https://www.vn3.org/");
            if (GUILayout.Button("VN3ライセンスを編集", GUILayout.Height(22)))
            {
                EditorPrefs.SetString(EditorPrefsKeyOutputDirectory, _outputDirectory);
                EditorPrefs.SetString(EditorPrefsKeySourceFolder, _sourceFolderPath ?? "");
                VN3LicenseEditorWindow.Open();
            }
            EditorGUILayout.EndHorizontal();
            EndSection();
        }

        void DrawOutputSection()
        {
            BeginSection("出力先");
            EditorGUILayout.BeginHorizontal();
            _outputDirectory = EditorGUILayout.TextField("出力ディレクトリ", _outputDirectory ?? "", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("参照", GUILayout.MinWidth(36)))
            {
                var selected = EditorUtility.OpenFolderPanel("出力先を選択", _outputDirectory, "");
                if (!string.IsNullOrEmpty(selected))
                    _outputDirectory = selected;
            }
            EditorGUILayout.EndHorizontal();
            _useVersionFolder = EditorGUILayout.ToggleLeft(
                "出力フォルダ名の末尾を x.x.x にする（商品名_verx.x.x）",
                _useVersionFolder);
            if (_useVersionFolder)
            {
                SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont(
                    "出力先に「商品名_verx.x.x」フォルダを作りその中へ書き出します。同じ場所にそれより前のバージョンがあれば、UnityPackage 以外をコピーします。すでに同じファイルがある場合は上書きしません。",
                    MessageType.Info);
            }
            EditorGUILayout.BeginHorizontal();
            _overwrite = EditorGUILayout.ToggleLeft("既存ファイルを上書きする", _overwrite);
            if (_useVersionFolder)
                _createZip = EditorGUILayout.ToggleLeft("zip も一緒に生成する", _createZip);
            EditorGUILayout.EndHorizontal();

            // if (PackageExporter.IsUnderSamirin33Folder(_sourceFolderPath) &&
            //     PackageExporter.TryGetBoothInformationOutputFolder(_sourceFolderPath, out var boothFolder))
            // {
            //     SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont(
            //         "samirin33 配下のため、ライセンス・解析情報は Booth Information にも出力できます:\n" + boothFolder,
            //         MessageType.Info);
            // }
            EndSection();
        }

        void DrawExportFooter(string versionStr)
        {
            EditorGUILayout.Space(4);
            DrawSectionBar("書き出し");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var outDir = GetExportDirectory(_outputDirectory, _packageName, versionStr);
            var fileName = !string.IsNullOrEmpty(_packageName) && !string.IsNullOrEmpty(versionStr)
                ? $"{_packageName}_ver{versionStr}.unitypackage"
                : "";
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                "ディレクトリ  " + (string.IsNullOrEmpty(outDir) ? "—" : outDir),
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                "ファイル  " + (string.IsNullOrEmpty(fileName) ? "—" : fileName),
                EditorStyles.miniLabel);
            if (_useVersionFolder && _createZip && !string.IsNullOrEmpty(_packageName) && !string.IsNullOrEmpty(versionStr))
                EditorGUILayout.LabelField("zip  " + $"{_packageName}_ver{versionStr}.zip", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);
            GUI.enabled = !string.IsNullOrEmpty(_sourceFolderPath) && !string.IsNullOrEmpty(_packageName) && !string.IsNullOrEmpty(_outputDirectory);
            if (GUILayout.Button("エクスポート !", GUILayout.Height(30), GUILayout.ExpandWidth(true)))
                RunExport(versionStr);
            GUI.enabled = true;
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        void RunExport(string versionStr)
        {
            EditorPrefs.SetString(EditorPrefsKeyOutputDirectory, _outputDirectory);
            EditorPrefs.SetString(EditorPrefsKeySourceFolder, _sourceFolderPath ?? "");
            EditorPrefs.SetBool(EditorPrefsKeyOverwrite, _overwrite);
            EditorPrefs.SetBool(EditorPrefsKeyIncludeInstaller, _includeInstaller);
            EditorPrefs.SetBool(EditorPrefsKeyIncludeBoothManagerInstaller, _includeBoothManagerInstaller);
            EditorPrefs.SetBool(EditorPrefsKeyResetExistingAssetsOnImport, _resetExistingAssetsOnImport);
            EditorPrefs.SetBool(EditorPrefsKeyUseVersionFolder, _useVersionFolder);
            EditorPrefs.SetBool(EditorPrefsKeyCreateZip, _createZip);

            _assetInfo.relatedFolders = PackageExporter.NormalizeRelatedFolders(
                _sourceFolderPath, _assetInfo.relatedFolders);

            var includeBooth = PackageExporter.IsUnderSamirin33Folder(_sourceFolderPath)
                && _includeBoothManagerInstaller;
            var result = PackageExporter.ExportPackage(
                _sourceFolderPath,
                _assetInfo,
                _packageName,
                versionStr,
                _outputDirectory,
                _overwrite,
                _includeInstaller,
                includeBooth,
                _resetExistingAssetsOnImport,
                _useVersionFolder,
                _useVersionFolder && _createZip);
            var packagePath = System.IO.Path.Combine(
                GetExportDirectory(_outputDirectory, _packageName, versionStr),
                $"{_packageName}_ver{versionStr}.unitypackage");
            if (result != null)
                EditorUtility.RevealInFinder(result);
            else if (System.IO.File.Exists(packagePath))
                EditorUtility.DisplayDialog("上書きしません", "同じファイルが既に存在します。上書きする場合は「既存ファイルを上書きする」にチェックを入れてください。", "OK");
        }

        void DrawRelatedFoldersList()
        {
            var list = _assetInfo.relatedFolders != null
                ? new List<string>(_assetInfo.relatedFolders)
                : new List<string>();

            for (int i = 0; i < list.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                var currentPath = (list[i] ?? "").Replace("\\", "/").TrimEnd('/');
                var currentAsset = string.IsNullOrEmpty(currentPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<DefaultAsset>(currentPath);

                EditorGUI.BeginChangeCheck();
                var nextAsset = (DefaultAsset)EditorGUILayout.ObjectField(
                    currentAsset, typeof(DefaultAsset), false, GUILayout.ExpandWidth(true));
                if (EditorGUI.EndChangeCheck())
                {
                    if (nextAsset == null)
                    {
                        list[i] = "";
                    }
                    else
                    {
                        var path = AssetDatabase.GetAssetPath(nextAsset).Replace("\\", "/");
                        if (AssetDatabase.IsValidFolder(path))
                            list[i] = path;
                        else
                            EditorUtility.DisplayDialog("関連フォルダ", "フォルダを選択してください。", "OK");
                    }
                }

                if (GUILayout.Button("−", GUILayout.MaxWidth(22)))
                {
                    list.RemoveAt(i);
                    i--;
                }
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(list[i]) && !AssetDatabase.IsValidFolder(list[i]))
                    EditorGUILayout.HelpBox("無効なフォルダパスです: " + list[i], MessageType.Warning);
            }

            if (GUILayout.Button("+ 関連フォルダを追加"))
                list.Add("");

            _assetInfo.relatedFolders = list.ToArray();
        }

        void DrawSuperReimportFoldersList()
        {
            var list = _assetInfo.superReimportFolders != null
                ? new List<string>(_assetInfo.superReimportFolders)
                : new List<string>();

            for (int i = 0; i < list.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                var currentPath = (list[i] ?? "").Replace("\\", "/").TrimEnd('/');
                var currentAsset = string.IsNullOrEmpty(currentPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<DefaultAsset>(currentPath);

                EditorGUI.BeginChangeCheck();
                var nextAsset = (DefaultAsset)EditorGUILayout.ObjectField(
                    currentAsset, typeof(DefaultAsset), false, GUILayout.ExpandWidth(true));
                if (EditorGUI.EndChangeCheck())
                {
                    if (nextAsset == null)
                    {
                        list[i] = "";
                    }
                    else
                    {
                        var path = AssetDatabase.GetAssetPath(nextAsset).Replace("\\", "/");
                        if (!AssetDatabase.IsValidFolder(path))
                            EditorUtility.DisplayDialog("SuperReImport", "フォルダを選択してください。", "OK");
                        else if (PackageExporter.IsSuperReimportExcludedFolder(path))
                            EditorUtility.DisplayDialog("SuperReImport", "インストーラのフォルダは削除対象にできません。", "OK");
                        else
                            list[i] = path;
                    }
                }

                if (GUILayout.Button("−", GUILayout.MaxWidth(22)))
                {
                    list.RemoveAt(i);
                    i--;
                }
                EditorGUILayout.EndHorizontal();

                if (i < 0 || i >= list.Count)
                    continue;
                if (!string.IsNullOrEmpty(list[i]) && PackageExporter.IsSuperReimportExcludedFolder(list[i]))
                    EditorGUILayout.HelpBox("このフォルダは削除しません: " + list[i], MessageType.Warning);
                else if (!string.IsNullOrEmpty(list[i]) && !AssetDatabase.IsValidFolder(list[i]))
                    EditorGUILayout.HelpBox("無効なフォルダパスです: " + list[i], MessageType.Warning);
            }

            if (GUILayout.Button("+ SuperReImport 対象フォルダを追加"))
                list.Add("");

            _assetInfo.superReimportFolders = list.ToArray();
        }

        void DrawUrlList()
        {
            var list = _assetInfo.urls != null ? new List<PackageAssetInfo.UrlInfo>(_assetInfo.urls) : new List<PackageAssetInfo.UrlInfo>();
            for (int i = 0; i < list.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(false));
                EditorGUILayout.LabelField("URLタイトル", EditorStyles.miniLabel);
                list[i].urlDescription = EditorGUILayout.TextField(list[i].urlDescription ?? "");
                EditorGUILayout.LabelField("URL", EditorStyles.miniLabel);
                list[i].url = EditorGUILayout.TextField(list[i].url ?? "");
                EditorGUILayout.EndVertical();
                if (GUILayout.Button("−", GUILayout.MaxWidth(22)))
                {
                    list.RemoveAt(i);
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ URL を追加"))
                list.Add(new PackageAssetInfo.UrlInfo());
            _assetInfo.urls = list.ToArray();
        }

        static string GetTodayDateString() => DateTime.Now.ToString("yyyy/M/d");

        void DrawReleaseList()
        {
            var list = _assetInfo.releases != null ? new List<PackageAssetInfo.ReleaseInfo>(_assetInfo.releases) : new List<PackageAssetInfo.ReleaseInfo>();
            if (GUILayout.Button("Release情報を追加"))
                list.Insert(0, new PackageAssetInfo.ReleaseInfo());
            for (int i = 0; i < list.Count; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                list[i].version = EditorGUILayout.TextField("Version", list[i].version ?? "");
                if (GUILayout.Button("現在のバージョン", GUILayout.ExpandWidth(false)))
                    list[i].version = GetVersionString();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                list[i].releaseDate = EditorGUILayout.TextField("Release Date", list[i].releaseDate ?? "", GUILayout.ExpandWidth(true));
                if (GUILayout.Button("今日", GUILayout.ExpandWidth(false)))
                    list[i].releaseDate = GetTodayDateString();
                EditorGUILayout.EndHorizontal();
                DrawStringArray("Release Notes", list[i].releaseNotes, out list[i].releaseNotes);
                if (GUILayout.Button("このリリース情報を削除"))
                {
                    list.RemoveAt(i);
                    i--;
                }
                EditorGUILayout.EndVertical();
            }
            _assetInfo.releases = list.ToArray();
        }

        static void DrawStringArray(string label, string[] array, out string[] result)
        {
            var list = array != null ? new List<string>(array) : new List<string>();
            EditorGUILayout.LabelField(label);
            if (GUILayout.Button("ノートを追加", GUILayout.ExpandWidth(false)))
                list.Add("");
            EditorGUI.indentLevel++;
            for (int i = 0; i < list.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                list[i] = EditorGUILayout.TextField(list[i] ?? "", GUILayout.ExpandWidth(true));
                if (GUILayout.Button("−", GUILayout.MaxWidth(22)))
                {
                    list.RemoveAt(i);
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;
            result = list.ToArray();
        }
    }
}
