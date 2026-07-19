using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Samirin33.Editor;

namespace Samirin.VRCUtility.AvatarEditor.Editor
{
    public class PackageExporterWindow : EditorWindow
    {
        const string EditorPrefsKeyOutputDirectory = "Samirin.VRCUtility.AvatarEditor.PackageExporter.OutputDirectory";
        const string EditorPrefsKeyOverwrite = "Samirin.VRCUtility.AvatarEditor.PackageExporter.Overwrite";
        const string EditorPrefsKeyIncludeInstaller = "Samirin.VRCUtility.AvatarEditor.PackageExporter.IncludeInstaller";
        const string EditorPrefsKeyIncludeBoothManagerInstaller =
            "Samirin.VRCUtility.AvatarEditor.PackageExporter.IncludeBoothManagerInstaller";

        string _sourceFolderPath = "";
        DefaultAsset _sourceFolderAsset;
        string _packageName = "";
        int _versionMajor = 1, _versionMinor = 0, _versionPatch = 0;
        string _outputDirectory = "";
        bool _overwrite = true;
        bool _includeInstaller = true;
        bool _includeBoothManagerInstaller = true;

        PackageAssetInfo _assetInfo;
        Vector2 _scrollPosition;
        bool _urlsFoldout = true;
        bool _releasesFoldout = true;

        [MenuItem("samirin33 Editor Tools/Package Exporter", false, 100)]
        public static void Open()
        {
            var w = GetWindow<PackageExporterWindow>(false, "Package Exporter", true);
            w.minSize = new Vector2(50, 360);
        }

        void OnEnable()
        {
            _outputDirectory = EditorPrefs.GetString(EditorPrefsKeyOutputDirectory, "");
            _overwrite = EditorPrefs.GetBool(EditorPrefsKeyOverwrite, false);
            _includeInstaller = EditorPrefs.GetBool(EditorPrefsKeyIncludeInstaller, false);
            _includeBoothManagerInstaller = EditorPrefs.GetBool(EditorPrefsKeyIncludeBoothManagerInstaller, true);
            if (!string.IsNullOrEmpty(_sourceFolderPath))
                LoadAssetInfoFromFolder();
        }

        string GetVersionString() => $"{_versionMajor}.{_versionMinor}.{_versionPatch}";

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
                _assetInfo = new PackageAssetInfo { name = _packageName, version = GetVersionString(), urls = new PackageAssetInfo.UrlInfo[0], releases = new PackageAssetInfo.ReleaseInfo[0] };
            else
            {
                if (string.IsNullOrEmpty(_packageName)) _packageName = _assetInfo.name ?? "";
                if (!string.IsNullOrEmpty(_assetInfo.version))
                    ParseVersion(_assetInfo.version, out _versionMajor, out _versionMinor, out _versionPatch);
            }
        }

        void OnGUI()
        {
            SamirinEditorStyleHelper.DrawWithBlueBackground(() =>
            {
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));

                var prevLabelWidth = EditorGUIUtility.labelWidth;
                try
                {
                    EditorGUIUtility.labelWidth = Mathf.Max(prevLabelWidth, 10f);
                    EditorGUILayout.Space(4);
                    SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont(
                        "プロジェクト内フォルダを選択し、パッケージ名・バージョンを指定して UnityPackage をエクスポートします。PackageAssetInfo はフォルダ直下に保存され、パッケージに含まれます。",
                        MessageType.Info);
                    EditorGUILayout.Space(4);

                    // ソースフォルダ
                    EditorGUILayout.LabelField("ソースフォルダ (Assets/...)");
                    EditorGUI.BeginChangeCheck();
                    _sourceFolderAsset = (DefaultAsset)EditorGUILayout.ObjectField("配布フォルダ", _sourceFolderAsset, typeof(DefaultAsset), false);
                    if (EditorGUI.EndChangeCheck() && _sourceFolderAsset != null)
                    {
                        var path = AssetDatabase.GetAssetPath(_sourceFolderAsset);
                        if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                        {
                            _sourceFolderPath = path;
                            LoadAssetInfoFromFolder();
                        }
                    }
                    EditorGUI.BeginChangeCheck();
                    _sourceFolderPath = EditorGUILayout.TextField("フォルダパス", _sourceFolderPath ?? "");
                    if (EditorGUI.EndChangeCheck() && !string.IsNullOrEmpty(_sourceFolderPath))
                    {
                        var path = _sourceFolderPath.Replace("\\", "/").TrimEnd('/');
                        if (AssetDatabase.IsValidFolder(path))
                        {
                            _sourceFolderPath = path;
                            LoadAssetInfoFromFolder();
                        }
                    }
                    if (!string.IsNullOrEmpty(_sourceFolderPath) && !AssetDatabase.IsValidFolder(_sourceFolderPath))
                    {
                        EditorGUILayout.HelpBox("有効なプロジェクト内フォルダパスを指定してください（例: Assets/MyPackage）", MessageType.Warning);
                    }
                    EditorGUILayout.Space(6);

                    // PackageAssetInfo 編集のため先にロード（最終リリース表示で参照する）
                    if (_assetInfo == null && !string.IsNullOrEmpty(_sourceFolderPath))
                        LoadAssetInfoFromFolder();
                    if (_assetInfo == null)
                        _assetInfo = new PackageAssetInfo { name = _packageName, version = GetVersionString(), urls = new PackageAssetInfo.UrlInfo[0], releases = new PackageAssetInfo.ReleaseInfo[0] };

                    // パッケージ名・バージョン
                    EditorGUILayout.LabelField("パッケージ情報");
                    _packageName = EditorGUILayout.TextField("パッケージ名", _packageName ?? "");

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

                    // 最終リリースバージョン（指定フォルダの JSON ファイルの内容を表示。編集状況は反映しない）
                    var latest = GetLatestReleaseFromJsonFile();
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField("最終バージョン");
                    if (latest != null)
                    {
                        EditorGUILayout.LabelField("バージョン", latest.version ?? "—");
                        EditorGUILayout.LabelField("リリース日", latest.releaseDate ?? "—");
                    }
                    else
                    {
                        EditorGUILayout.LabelField("フォルダ内に PackageAssetInfo.json がないか、releases がありません。");
                    }
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(6);

                    // Installer フォルダを含めるか（EditorPrefs）
                    _includeInstaller = EditorGUILayout.ToggleLeft("パッケージに「AvatarInstaller」を含める", _includeInstaller);
                    SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont(
                        "有効にすると Assets/AvatarInstaller として同梱されます。導入先でも Assets に展開され、samirin33 VRC Utility 関連スクリプトを使えるようになります。",
                        MessageType.Info);

                    // Assets/samirin33 配下のときだけ BoothManager インストーラ同梱オプションを表示
                    if (PackageExporter.IsUnderSamirin33Folder(_sourceFolderPath))
                    {
                        EditorGUILayout.Space(4);
                        _includeBoothManagerInstaller = EditorGUILayout.ToggleLeft(
                            "パッケージに「SamirinBoothManager」インストーラを含める",
                            _includeBoothManagerInstaller);
                        SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont(
                            "有効にすると、導入時に GitHub から SamirinBoothManager を自動取得するインストーラが同梱されます。",
                            MessageType.Info);
                    }

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("vn3.org を開く", GUILayout.ExpandWidth(false)))
                        Application.OpenURL("https://www.vn3.org/");
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space(6);

                    EditorGUILayout.LabelField("パッケージ情報");
                    _assetInfo.author = EditorGUILayout.TextField("作者", _assetInfo.author ?? "");
                    _assetInfo.description = EditorGUILayout.TextArea(_assetInfo.description ?? "", GUILayout.MinHeight(44));

                    _releasesFoldout = EditorGUILayout.Foldout(_releasesFoldout, "Releases", true);
                    if (_releasesFoldout)
                    {
                        EditorGUI.indentLevel++;
                        DrawReleaseList();
                        EditorGUI.indentLevel--;
                    }

                    _urlsFoldout = EditorGUILayout.Foldout(_urlsFoldout, "関連URL", true);
                    if (_urlsFoldout)
                    {
                        EditorGUI.indentLevel++;
                        DrawUrlList();
                        EditorGUI.indentLevel--;
                    }

                    EditorGUILayout.Space(8);

                    // 出力先（EditorPrefs）
                    EditorGUILayout.LabelField("出力先（個人的設定・パッケージに含まれません）");
                    EditorGUILayout.BeginHorizontal();
                    _outputDirectory = EditorGUILayout.TextField("出力ディレクトリ", _outputDirectory ?? "", GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("参照", GUILayout.MinWidth(36)))
                    {
                        var selected = EditorUtility.OpenFolderPanel("出力先を選択", _outputDirectory, "");
                        if (!string.IsNullOrEmpty(selected))
                            _outputDirectory = selected;
                    }
                    EditorGUILayout.EndHorizontal();
                    _overwrite = EditorGUILayout.ToggleLeft("既存ファイルを上書きする", _overwrite);
                    EditorGUILayout.Space(4);
                    if (GUILayout.Button("VN3ライセンスを編集／指定フォルダに生成", GUILayout.Height(22)))
                    {
                        EditorPrefs.SetString(EditorPrefsKeyOutputDirectory, _outputDirectory);
                        VN3LicenseEditorWindow.Open();
                    }
                    EditorGUILayout.Space(6);

                    // エクスポート
                    var versionStr = GetVersionString();
                    GUI.enabled = !string.IsNullOrEmpty(_sourceFolderPath) && !string.IsNullOrEmpty(_packageName) && !string.IsNullOrEmpty(_outputDirectory);
                    if (GUILayout.Button("エクスポート !", GUILayout.Height(28), GUILayout.ExpandWidth(true)))
                    {
                        EditorPrefs.SetString(EditorPrefsKeyOutputDirectory, _outputDirectory);
                        EditorPrefs.SetBool(EditorPrefsKeyOverwrite, _overwrite);
                        EditorPrefs.SetBool(EditorPrefsKeyIncludeInstaller, _includeInstaller);
                        EditorPrefs.SetBool(EditorPrefsKeyIncludeBoothManagerInstaller, _includeBoothManagerInstaller);

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
                            includeBooth);
                        if (result != null)
                            EditorUtility.RevealInFinder(result);
                        else if (System.IO.File.Exists(System.IO.Path.Combine(_outputDirectory, $"{_packageName}_ver{versionStr}.unitypackage")))
                            EditorUtility.DisplayDialog("上書きしません", "同じファイルが既に存在します。上書きする場合は「既存ファイルを上書きする」にチェックを入れてください。", "OK");
                    }
                    GUI.enabled = true;

                    var outDir = _outputDirectory ?? "";
                    var fileName = !string.IsNullOrEmpty(_packageName) && !string.IsNullOrEmpty(versionStr)
                        ? $"{_packageName}_ver{versionStr}.unitypackage"
                        : "";
                    EditorGUILayout.LabelField("最終出力ディレクトリ", outDir);
                    EditorGUILayout.LabelField("ファイル名", string.IsNullOrEmpty(fileName) ? "—" : fileName);
                    EditorGUILayout.Space(4);
                }
            finally
            {
                EditorGUIUtility.labelWidth = prevLabelWidth;
            }
            EditorGUILayout.EndScrollView();
            }, new Rect(0, 0, position.width, position.height));
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
