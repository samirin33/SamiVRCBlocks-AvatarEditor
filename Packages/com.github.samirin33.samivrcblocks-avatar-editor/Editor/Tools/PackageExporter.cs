using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

namespace SamiVRCBlocksAvatar.Editor
{
    public static class PackageExporter
    {
        public const string AssetInfoFileName = "PackageAssetInfo.json";

        public const string BoothManagerInstallerFolderName = "SamirinBoothManagerInstaller";
        public const string BoothManagerInstallerScriptFileName = "SamirinBoothManagerInstaller.cs";

        /// <summary>編集用ソース（Packages/.../Editor/SamirinBoothManagerInstaller）</summary>
        public const string BoothManagerInstallerSourcePackageRelative =
            "Packages/com.github.samirin33.samivrcblocks-avatar-editor/Editor/" + BoothManagerInstallerFolderName;

        /// <summary>配布用に一時配置する Assets パス（Editor 配下＝導入先で Editor スクリプトとしてコンパイルされる）</summary>
        public const string BoothManagerInstallerAssetPath =
            "Assets/Editor/" + BoothManagerInstallerFolderName;

        public const string SuperReImporterFolderName = "SuperReImporter";
        public const string SuperReImporterScriptFileName = "SuperReImporter.cs";
        public const string SuperReImporterPayloadFileName = "Payload.unitypackage";
        public const string SuperReImporterSuppressFileName = "SuperReImporter.suppress";
        public const string SuperReImporterGuardAsmdefFileName = "SuperReImporter.ExportGuard.asmdef";

        /// <summary>配布用に一時配置する Assets パス。</summary>
        public const string SuperReImporterAssetPath = "Assets/Editor/" + SuperReImporterFolderName;

        /// <summary>編集用ソース（Packages/.../Editor/SuperReImporter）</summary>
        public const string SuperReImporterSourcePackageRelative =
            "Packages/com.github.samirin33.samivrcblocks-avatar-editor/Editor/" + SuperReImporterFolderName;

        /// <summary>SuperReImporter.SuppressSessionKey と同じ。ステージ中の誤実行を止める。</summary>
        public const string SuperReImporterSuppressSessionKey = "SuperReImporter.Suppress";

        const string SuperReImporterFoldersPlaceholder = "__SUPER_REIMPORT_FOLDERS__";
        const string SuperReImporterTokenPlaceholder = "__SUPER_REIMPORT_TOKEN__";

        const string SuperReImporterGuardAsmdefJson =
            "{\n" +
            "    \"name\": \"SuperReImporter.ExportGuard\",\n" +
            "    \"rootNamespace\": \"\",\n" +
            "    \"references\": [],\n" +
            "    \"includePlatforms\": [\n" +
            "        \"Editor\"\n" +
            "    ],\n" +
            "    \"excludePlatforms\": [],\n" +
            "    \"allowUnsafeCode\": false,\n" +
            "    \"overrideReferences\": false,\n" +
            "    \"precompiledReferences\": [],\n" +
            "    \"autoReferenced\": false,\n" +
            "    \"defineConstraints\": [\n" +
            "        \"SAMIRIN_SUPER_REIMPORTER_EXPORT_GUARD\"\n" +
            "    ],\n" +
            "    \"versionDefines\": [],\n" +
            "    \"noEngineReferences\": false\n" +
            "}\n";

        /// <summary>Assets/samirin33 直下またはその配下か。</summary>
        public static bool IsUnderSamirin33Folder(string assetFolderPath)
        {
            if (string.IsNullOrEmpty(assetFolderPath))
                return false;
            var normalized = assetFolderPath.Replace("\\", "/").TrimEnd('/');
            return normalized == "Assets/samirin33"
                || normalized.StartsWith("Assets/samirin33/");
        }

        static string AssetPathToFullPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            var relative = assetPath.Replace("\\", "/");
            if (!relative.StartsWith("Assets/") && relative != "Assets") return null;
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));
        }

        public const string BoothInformationFolder = "Assets/samirin33/SamirinBoothInformation";
        public const string ItemAnalysisFileName = "ItemAnalysis.txt";
        const string BoothAssetInfoTypeName = "SamirinBoothAssetInfo";
        const string Samirin33Root = "Assets/samirin33";
        static readonly HashSet<string> ExcludedSamirin33ProductNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SamirinBoothInformation",
            "SamirinBoothManager",
            "Editor",
            "Script"
        };

        /// <summary>
        /// Assets/samirin33 配下の配布物キー（例: Assets/samirin33/DeskPen/... → DeskPen）を取得します。
        /// </summary>
        public static bool TryGetSamirin33ProductKey(string sourceAssetFolder, out string productKey)
        {
            productKey = null;
            if (string.IsNullOrEmpty(sourceAssetFolder))
                return false;

            var normalized = sourceAssetFolder.Replace("\\", "/").TrimEnd('/');
            if (normalized == Samirin33Root)
                return false;

            if (normalized.StartsWith(BoothInformationFolder + "/", StringComparison.OrdinalIgnoreCase))
            {
                var relative = normalized.Substring(BoothInformationFolder.Length).TrimStart('/');
                if (string.IsNullOrEmpty(relative))
                    return false;
                productKey = relative.Split('/')[0];
            }
            else if (normalized.StartsWith(Samirin33Root + "/", StringComparison.OrdinalIgnoreCase))
            {
                var relative = normalized.Substring(Samirin33Root.Length).TrimStart('/');
                if (string.IsNullOrEmpty(relative))
                    return false;
                productKey = relative.Split('/')[0];
            }
            else
            {
                return false;
            }

            if (string.IsNullOrEmpty(productKey) || ExcludedSamirin33ProductNames.Contains(productKey))
            {
                productKey = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// 配布フォルダに対応する SamirinBoothInformation 配下フォルダを解決します。
        /// 例: Assets/samirin33/DeskPen → Assets/samirin33/SamirinBoothInformation/DeskPen
        /// </summary>
        public static bool TryGetBoothInformationOutputFolder(
            string sourceAssetFolder,
            out string boothInfoFolderAssetPath,
            bool createIfMissing = false)
        {
            boothInfoFolderAssetPath = null;
            if (!TryGetSamirin33ProductKey(sourceAssetFolder, out var productKey))
                return false;

            boothInfoFolderAssetPath = (BoothInformationFolder + "/" + productKey).Replace("\\", "/");

            // BoothAssetInfo があれば、その実ファイルの親フォルダを優先
            var boothAsset = FindBoothAssetInfoForFolder(Samirin33Root + "/" + productKey)
                            ?? FindBoothAssetInfoForFolder(sourceAssetFolder);
            if (boothAsset != null)
            {
                var assetPath = AssetDatabase.GetAssetPath(boothAsset).Replace("\\", "/");
                var parent = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
                if (!string.IsNullOrEmpty(parent) &&
                    parent.StartsWith(BoothInformationFolder + "/", StringComparison.OrdinalIgnoreCase))
                {
                    boothInfoFolderAssetPath = parent;
                }
            }

            if (createIfMissing)
                EnsureAssetFolder(boothInfoFolderAssetPath);

            return createIfMissing || AssetDatabase.IsValidFolder(boothInfoFolderAssetPath);
        }

        /// <summary>
        /// Assets パスを絶対パスに変換します。
        /// </summary>
        public static string ToFullPath(string assetPath)
        {
            return AssetPathToFullPath(assetPath);
        }

        /// <summary>
        /// SamirinBoothInformation 配下フォルダにテキストファイルを書き出します。
        /// </summary>
        public static string WriteTextToBoothInformationFolder(
            string sourceAssetFolder,
            string fileName,
            string contents)
        {
            if (string.IsNullOrEmpty(fileName))
                throw new ArgumentException("fileName is required.", nameof(fileName));

            if (!TryGetBoothInformationOutputFolder(sourceAssetFolder, out var assetFolder, createIfMissing: true))
            {
                Debug.LogWarning("[PackageExporter] Booth Information 出力先を解決できません: " + sourceAssetFolder);
                return null;
            }

            var fullFolder = AssetPathToFullPath(assetFolder);
            if (string.IsNullOrEmpty(fullFolder))
                return null;

            if (!Directory.Exists(fullFolder))
                Directory.CreateDirectory(fullFolder);

            var fullPath = Path.Combine(fullFolder, fileName).Replace("\\", "/");
            File.WriteAllText(fullPath, contents ?? "", new System.Text.UTF8Encoding(true));
            AssetDatabase.Refresh();
            return fullPath;
        }

        /// <summary>
        /// Assembly-CSharp 上の SamirinBoothAssetInfo 型を取得（パッケージから直接参照できないため）。
        /// </summary>
        public static Type GetBoothAssetInfoType()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try
                {
                    type = assembly.GetType(BoothAssetInfoTypeName);
                }
                catch
                {
                    continue;
                }
                if (type != null)
                    return type;
            }
            return null;
        }

        public static bool IsBoothAssetInfo(UnityEngine.Object obj)
        {
            return obj != null && obj.GetType().Name == BoothAssetInfoTypeName;
        }

        /// <summary>
        /// 配布フォルダに対応する SamirinBoothAssetInfo を探す（folderName / フォルダ名 / name）。
        /// </summary>
        public static UnityEngine.Object FindBoothAssetInfoForFolder(string sourceAssetFolder)
        {
            if (string.IsNullOrEmpty(sourceAssetFolder))
                return null;

            var folderKey = Path.GetFileName(sourceAssetFolder.Replace("\\", "/").TrimEnd('/'));
            if (string.IsNullOrEmpty(folderKey))
                return null;

            if (!AssetDatabase.IsValidFolder(BoothInformationFolder))
                return null;

            var guids = AssetDatabase.FindAssets("t:" + BoothAssetInfoTypeName, new[] { BoothInformationFolder });
            UnityEngine.Object fallbackByName = null;

            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]).Replace('\\', '/');
                if (path.EndsWith("/Manger.asset", StringComparison.OrdinalIgnoreCase))
                    continue;

                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (!IsBoothAssetInfo(asset))
                    continue;

                var so = new SerializedObject(asset);
                var folderName = so.FindProperty("folderName")?.stringValue ?? "";
                var displayName = so.FindProperty("name")?.stringValue ?? "";

                if (!string.IsNullOrEmpty(folderName) &&
                    string.Equals(folderName, folderKey, StringComparison.OrdinalIgnoreCase))
                    return asset;

                if (fallbackByName == null &&
                    string.Equals(displayName, folderKey, StringComparison.OrdinalIgnoreCase))
                    fallbackByName = asset;
            }

            return fallbackByName;
        }

        /// <summary>
        /// SamirinBoothAssetInfo から PackageAssetInfo の基本情報・関連URL・更新履歴を反映する。
        /// </summary>
        /// <returns>反映に成功したら true</returns>
        public static bool TryApplyFromBoothAssetInfo(
            UnityEngine.Object boothAsset,
            PackageAssetInfo target,
            out string packageName,
            out string version)
        {
            packageName = null;
            version = null;
            if (target == null || !IsBoothAssetInfo(boothAsset))
                return false;

            var so = new SerializedObject(boothAsset);
            var folderName = so.FindProperty("folderName")?.stringValue?.Trim() ?? "";
            var displayName = so.FindProperty("name")?.stringValue?.Trim() ?? "";
            var description = so.FindProperty("description")?.stringValue ?? "";
            var major = so.FindProperty("majorVertion")?.intValue ?? 0;
            var minor = so.FindProperty("minorVertion")?.intValue ?? 0;
            var patch = so.FindProperty("patchVertion")?.intValue ?? 0;
            var boothUrl = so.FindProperty("url")?.stringValue?.Trim() ?? "";
            var youtubeUrl = so.FindProperty("youtubeUrl")?.stringValue?.Trim() ?? "";

            packageName = !string.IsNullOrEmpty(folderName) ? folderName : displayName;
            version = $"{major}.{minor}.{patch}";

            if (!string.IsNullOrEmpty(packageName))
                target.name = packageName;
            target.version = version;
            target.author = "samirin33";
            target.description = description;

            var urls = new List<PackageAssetInfo.UrlInfo>();
            if (!string.IsNullOrEmpty(boothUrl))
            {
                urls.Add(new PackageAssetInfo.UrlInfo
                {
                    urlDescription = "Booth",
                    url = boothUrl
                });
            }
            if (!string.IsNullOrEmpty(youtubeUrl))
            {
                urls.Add(new PackageAssetInfo.UrlInfo
                {
                    urlDescription = "YouTube",
                    url = youtubeUrl
                });
            }
            target.urls = urls.ToArray();
            target.releases = BuildReleasesFromUpdateInfos(so.FindProperty("updateInfos"));

            return true;
        }

        /// <summary>
        /// updateInfos（古い→新しい順想定）を releases（最新が先頭）へ変換する。
        /// </summary>
        static PackageAssetInfo.ReleaseInfo[] BuildReleasesFromUpdateInfos(SerializedProperty updateInfosProp)
        {
            var list = new List<PackageAssetInfo.ReleaseInfo>();
            if (updateInfosProp == null || !updateInfosProp.isArray)
                return list.ToArray();

            for (var i = 0; i < updateInfosProp.arraySize; i++)
            {
                var item = updateInfosProp.GetArrayElementAtIndex(i);
                var updateName = item.FindPropertyRelative("updateName")?.stringValue?.Trim() ?? "";
                var updateDescription = item.FindPropertyRelative("updateDescription")?.stringValue?.Trim() ?? "";
                var dateProp = item.FindPropertyRelative("updateDate");
                var year = dateProp?.FindPropertyRelative("year")?.intValue ?? 0;
                var month = dateProp?.FindPropertyRelative("month")?.intValue ?? 0;
                var day = dateProp?.FindPropertyRelative("day")?.intValue ?? 0;

                var notes = string.IsNullOrEmpty(updateDescription)
                    ? new string[0]
                    : new[] { updateDescription };

                list.Add(new PackageAssetInfo.ReleaseInfo
                {
                    version = string.IsNullOrEmpty(updateName) ? null : updateName,
                    releaseDate = FormatBoothDate(year, month, day),
                    releaseNotes = notes
                });
            }

            // 最新を先頭に
            list.Reverse();
            return list.ToArray();
        }

        static string FormatBoothDate(int year, int month, int day)
        {
            if (year <= 0 || month <= 0 || day <= 0)
                return null;
            return $"{year}/{month}/{day}";
        }

        /// <summary>
        /// 指定フォルダ直下の PackageAssetInfo.json を読み込む。存在しない場合は null。
        /// </summary>
        public static PackageAssetInfo LoadAssetInfo(string assetFolderPath)
        {
            if (string.IsNullOrEmpty(assetFolderPath)) return null;
            var fullPath = AssetPathToFullPath(Path.Combine(assetFolderPath, AssetInfoFileName).Replace("\\", "/"));
            if (fullPath == null || !File.Exists(fullPath)) return null;
            var path = fullPath;
            try
            {
                var json = File.ReadAllText(path);
                return JsonUtility.FromJson<PackageAssetInfo>(json);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PackageExporter] Failed to load PackageAssetInfo: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 指定フォルダ直下に PackageAssetInfo.json を書き込む。
        /// </summary>
        public static void SaveAssetInfo(string assetFolderPath, PackageAssetInfo info)
        {
            if (string.IsNullOrEmpty(assetFolderPath) || info == null) return;
            var path = AssetPathToFullPath(Path.Combine(assetFolderPath, AssetInfoFileName).Replace("\\", "/"));
            if (path == null) return;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = JsonUtility.ToJson(info, true);
            File.WriteAllText(path, json);
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// フォルダ以下（直下含む）の全アセットパスを取得。Assets/ から始まるパスのみ。
        /// </summary>
        public static List<string> GetAssetPathsInFolder(string assetFolderPath)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(assetFolderPath)) return list;

            var normalized = assetFolderPath.Replace("\\", "/").TrimEnd('/');
            if (!normalized.StartsWith("Assets/") && normalized != "Assets")
                return list;

            var fullPath = Path.Combine(Application.dataPath, "..", normalized).Replace("\\", "/");
            if (!Directory.Exists(fullPath)) return list;

            foreach (var guid in AssetDatabase.FindAssets("", new[] { normalized }))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(assetPath) && (assetPath + "/").StartsWith(normalized + "/"))
                    list.Add(assetPath);
            }

            return list;
        }

        /// <summary>
        /// ディスク上のファイルからアセットパスを列挙する（.meta は除く）。
        /// AssetDatabase への未登録直後でも ExportPackage 用パスを集められる。
        /// </summary>
        static List<string> GetDiskAssetPathsInFolder(string assetFolderPath)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(assetFolderPath)) return list;

            var normalized = assetFolderPath.Replace("\\", "/").TrimEnd('/');
            if (!normalized.StartsWith("Assets/") && normalized != "Assets")
                return list;

            var fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", normalized));
            if (!Directory.Exists(fullPath)) return list;

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            foreach (var file in Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                var relative = file.Substring(projectRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                list.Add(relative.Replace("\\", "/"));
            }

            return list;
        }

        /// <summary>
        /// UnityPackage をエクスポートする。
        /// 出力前に PackageAssetInfo をフォルダ直下に保存し、そのフォルダごとパッケージに含める。
        /// </summary>
        /// <param name="sourceAssetFolder">Assets/ 以下のフォルダパス</param>
        /// <param name="packageName">パッケージ表示名（ファイル名の {名前} 部分）</param>
        /// <param name="version">x.x.x 形式</param>
        /// <param name="outputDirectory">出力先ディレクトリ（フルパス）</param>
        /// <param name="overwrite">既存ファイルを上書きするか</param>
        /// <param name="includeInstallerFolder">SamiVRCBlocksAvatarInstaller を Assets へ一時配置してパッケージに含めるか</param>
        /// <param name="includeBoothManagerInstaller">SamirinBoothManagerInstaller をパッケージに同梱するか（samirin33 配下向け）</param>
        /// <param name="resetExistingAssetsOnImport">有効なら SuperReImporter を同梱し、導入先で対象フォルダを一度削除してから再インポートする</param>
        /// <param name="useVersionFolder">有効なら出力先を「商品名_verx.x.x」フォルダにする</param>
        /// <param name="createZip">有効ならバージョンフォルダの zip も一緒に生成する</param>
        /// <returns>成功した場合の出力ファイルパス。失敗時は null。</returns>
        public static string ExportPackage(
            string sourceAssetFolder,
            PackageAssetInfo assetInfo,
            string packageName,
            string version,
            string outputDirectory,
            bool overwrite,
            bool includeInstallerFolder = false,
            bool includeBoothManagerInstaller = false,
            bool resetExistingAssetsOnImport = false,
            bool useVersionFolder = false,
            bool createZip = false)
        {
            if (string.IsNullOrEmpty(sourceAssetFolder) || string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(version))
            {
                Debug.LogError("[PackageExporter] sourceFolder, packageName, version are required.");
                return null;
            }

            if (assetInfo != null)
            {
                assetInfo.name = packageName;
                assetInfo.version = version;
                assetInfo.relatedFolders = NormalizeRelatedFolders(sourceAssetFolder, assetInfo.relatedFolders);
                assetInfo.superReimportFolders = NormalizeSuperReimportFolders(assetInfo.superReimportFolders);
                SaveAssetInfo(sourceAssetFolder, assetInfo);
            }

            var paths = GetAssetPathsInFolder(sourceAssetFolder);
            AppendRelatedFolderPaths(paths, sourceAssetFolder, assetInfo?.relatedFolders);
            // Installer 同梱時は Packages→Assets へ一時移動し、終了後に戻す
            var cleanupInstaller = false;
            var cleanupBoothManagerInstaller = false;
            var cleanupSuperReImporter = false;
            var lockReload = false;
            try
            {
                if (includeInstallerFolder)
                {
                    cleanupInstaller = true;

                    // Packages/.../SamiVRCBlocksAvatarInstaller 原本は残し、Assets/SamiVRCBlocksAvatarInstaller へ一時コピーして同梱する
                    // → インポート時も Assets/SamiVRCBlocksAvatarInstaller として展開される
                    if (!InstallerImport.StageInstallerToAssets())
                    {
                        Debug.LogError(
                            "[PackageExporter] SamiVRCBlocksAvatarInstaller の配置に失敗しました。" +
                            $" {InstallerImport.InstallerSourceFolderPackageRelative} を確認してください。");
                        return null;
                    }

                    var installerFolder = InstallerImport.InstallerFolderAssetPath;
                    var installerPaths = GetAssetPathsInFolder(installerFolder);
                    foreach (var diskPath in GetDiskAssetPathsInFolder(installerFolder))
                    {
                        if (!installerPaths.Contains(diskPath))
                            installerPaths.Add(diskPath);
                    }

                    // 編集用（defineConstraints 付き）asmdef が混入していたら除外
                    installerPaths.RemoveAll(p =>
                        p.EndsWith("SamiVRCBlocksAvatar.Installer.Source.asmdef", System.StringComparison.OrdinalIgnoreCase) ||
                        p.EndsWith("SamiVRCBlocksAvatar.Installer.Source.asmdef.meta", System.StringComparison.OrdinalIgnoreCase) ||
                        p.IndexOf(".Source.asmdef", System.StringComparison.OrdinalIgnoreCase) >= 0);

                    if (installerPaths.Count == 0)
                    {
                        Debug.LogError("[PackageExporter] Installer フォルダに含めるアセットが見つかりません: " + installerFolder);
                        return null;
                    }

                    var editorScriptPath = (installerFolder + "/" + InstallerImport.InstallerEditorScriptFileName).Replace("\\", "/");
                    if (!installerPaths.Contains(editorScriptPath))
                    {
                        Debug.LogError(
                            "[PackageExporter] Installer 内のスクリプトが含まれていません: " + editorScriptPath +
                            " — 編集用ソースの内容を確認してください。");
                        return null;
                    }

                    var set = new HashSet<string>(paths);
                    foreach (var p in installerPaths)
                    {
                        if (!set.Contains(p)) { set.Add(p); paths.Add(p); }
                    }

                    Debug.Log($"[PackageExporter] SamiVRCBlocksAvatarInstaller を含めます ({installerPaths.Count} assets): " + string.Join(", ", installerPaths));
                }

                if (includeBoothManagerInstaller)
                {
                    if (!IsUnderSamirin33Folder(sourceAssetFolder))
                    {
                    }
                    else if (!StageBoothManagerInstallerToAssets())
                    {
                        Debug.LogError(
                            "[PackageExporter] SamirinBoothManagerInstaller の配置に失敗しました。" +
                            $" {BoothManagerInstallerSourcePackageRelative} を確認してください。");
                        return null;
                    }
                    else
                    {
                        cleanupBoothManagerInstaller = true;

                        var boothPaths = GetAssetPathsInFolder(BoothManagerInstallerAssetPath);
                        foreach (var diskPath in GetDiskAssetPathsInFolder(BoothManagerInstallerAssetPath))
                        {
                            if (!boothPaths.Contains(diskPath))
                                boothPaths.Add(diskPath);
                        }

                        boothPaths.RemoveAll(p =>
                            p.EndsWith(".asmdef", System.StringComparison.OrdinalIgnoreCase) ||
                            p.EndsWith(".asmdef.meta", System.StringComparison.OrdinalIgnoreCase));

                        var scriptPath = (BoothManagerInstallerAssetPath + "/" + BoothManagerInstallerScriptFileName).Replace("\\", "/");
                        if (!boothPaths.Contains(scriptPath))
                        {
                            Debug.LogError(
                                "[PackageExporter] SamirinBoothManagerInstaller スクリプトが含まれていません: " + scriptPath);
                            return null;
                        }

                        var set = new HashSet<string>(paths);
                        foreach (var p in boothPaths)
                        {
                            if (!set.Contains(p)) { set.Add(p); paths.Add(p); }
                        }

                        Debug.Log(
                            $"[PackageExporter] SamirinBoothManagerInstaller を含めます ({boothPaths.Count} assets): " +
                            string.Join(", ", boothPaths));
                    }
                }

                if (paths.Count == 0)
                {
                    Debug.LogError("[PackageExporter] No assets found in folder: " + sourceAssetFolder);
                    return null;
                }

                var fileName = $"{packageName}_ver{version}.unitypackage";
                var versionFolderName = $"{packageName}_ver{version}";
                var exportDirectory = outputDirectory;
                if (useVersionFolder)
                    exportDirectory = Path.Combine(outputDirectory, versionFolderName);
                var outputPath = Path.Combine(exportDirectory, fileName).Replace("\\", "/");

                if (File.Exists(outputPath) && !overwrite)
                {
                    Debug.LogWarning("[PackageExporter] Output file already exists and overwrite is false: " + outputPath);
                    return null;
                }

                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var exportOptions = ExportPackageOptions.Recurse;
                if (resetExistingAssetsOnImport)
                {
                    // ステージしたスクリプトがコンパイルされて、書き出し中に対象フォルダを消さないようにする。
                    EditorApplication.LockReloadAssemblies();
                    lockReload = true;
                    SessionState.SetBool(SuperReImporterSuppressSessionKey, true);
                    cleanupSuperReImporter = true;

                    if (!TryWrapWithSuperReImporter(
                            assetInfo != null ? assetInfo.superReimportFolders : null,
                            paths,
                            out var wrappedPaths))
                    {
                        Debug.LogError("[PackageExporter] SuperReImporter の同梱に失敗したためエクスポートを中止しました。");
                        return null;
                    }

                    paths = wrappedPaths;
                }

                AssetDatabase.ExportPackage(paths.ToArray(), outputPath, exportOptions);
                Debug.Log($"[PackageExporter] Exported: {outputPath}");

                if (useVersionFolder)
                {
                    CopyExtrasFromPreviousVersionFolder(
                        outputDirectory, packageName, version, exportDirectory);
                    if (createZip)
                    {
                        var zipPath = Path.Combine(outputDirectory, versionFolderName + ".zip");
                        TryCreateDirectoryZip(exportDirectory, zipPath, overwrite);
                    }
                }

                return outputPath;
            }
            finally
            {
                if (cleanupSuperReImporter)
                    CleanupStagedSuperReImporter();
                if (cleanupBoothManagerInstaller)
                    CleanupStagedBoothManagerInstaller();
                if (cleanupInstaller)
                    InstallerImport.CleanupStagedInstaller();
                if (lockReload)
                {
                    SessionState.SetBool(SuperReImporterSuppressSessionKey, false);
                    EditorApplication.UnlockReloadAssemblies();
                }
            }
        }

        /// <summary>
        /// 同じ出力先にある一つ前のバージョンフォルダから、UnityPackage 以外を新しいフォルダへコピーする。
        /// コピー先に同名のファイルが既にある場合は上書きしない。
        /// </summary>
        static void CopyExtrasFromPreviousVersionFolder(
            string parentDirectory,
            string packageName,
            string version,
            string destinationFolder)
        {
            var previous = FindPreviousVersionFolder(parentDirectory, packageName, version);
            if (string.IsNullOrEmpty(previous))
            {
                Debug.Log("[PackageExporter] 一つ前のバージョンフォルダはありません。");
                return;
            }

            Debug.Log("[PackageExporter] 一つ前のバージョンフォルダ: " + previous);
            var copied = 0;
            try
            {
                foreach (var entry in Directory.GetFileSystemEntries(previous))
                {
                    var name = Path.GetFileName(entry);
                    if (string.IsNullOrEmpty(name))
                        continue;
                    if (File.Exists(entry) &&
                        name.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var dest = Path.Combine(destinationFolder, name);
                    if (Directory.Exists(entry))
                    {
                        copied += CopyDirectoryWithoutOverwrite(entry, dest);
                        continue;
                    }

                    if (!File.Exists(entry) || File.Exists(dest))
                        continue;
                    var destDir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);
                    File.Copy(entry, dest, false);
                    copied++;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PackageExporter] 前バージョンの付属ファイルのコピーに失敗: " + ex.Message);
                return;
            }

            if (copied == 0)
                Debug.Log("[PackageExporter] 前バージョンから新しくコピーした付属ファイルはありません。既存のファイルは残しています。");
            else
                Debug.Log("[PackageExporter] 前バージョンから付属ファイルをコピーしました: " + copied);
        }

        static string FindPreviousVersionFolder(string parentDirectory, string packageName, string version)
        {
            if (string.IsNullOrEmpty(parentDirectory) || !Directory.Exists(parentDirectory))
                return null;
            if (!TryParseVersion(version, out var currentMajor, out var currentMinor, out var currentPatch))
                return null;

            string bestPath = null;
            var bestMajor = -1;
            var bestMinor = -1;
            var bestPatch = -1;
            foreach (var dir in Directory.GetDirectories(parentDirectory))
            {
                var folderName = Path.GetFileName(dir);
                if (!TryParsePackageVersionFolder(folderName, packageName, out var major, out var minor, out var patch))
                    continue;
                if (!IsOlderVersion(major, minor, patch, currentMajor, currentMinor, currentPatch))
                    continue;
                if (bestPath != null && !IsOlderVersion(bestMajor, bestMinor, bestPatch, major, minor, patch))
                    continue;

                bestPath = dir;
                bestMajor = major;
                bestMinor = minor;
                bestPatch = patch;
            }

            return bestPath;
        }

        static bool TryParsePackageVersionFolder(
            string folderName,
            string packageName,
            out int major,
            out int minor,
            out int patch)
        {
            major = minor = patch = 0;
            if (string.IsNullOrEmpty(folderName) || string.IsNullOrEmpty(packageName))
                return false;

            var prefix = packageName + "_ver";
            if (!folderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
            return TryParseVersion(folderName.Substring(prefix.Length), out major, out minor, out patch);
        }

        static bool TryParseVersion(string version, out int major, out int minor, out int patch)
        {
            major = minor = patch = 0;
            if (string.IsNullOrEmpty(version))
                return false;
            var parts = version.Split('.');
            if (parts.Length != 3)
                return false;
            return int.TryParse(parts[0], out major)
                && int.TryParse(parts[1], out minor)
                && int.TryParse(parts[2], out patch)
                && major >= 0 && minor >= 0 && patch >= 0;
        }

        static bool IsOlderVersion(int major, int minor, int patch, int otherMajor, int otherMinor, int otherPatch)
        {
            if (major != otherMajor)
                return major < otherMajor;
            if (minor != otherMinor)
                return minor < otherMinor;
            return patch < otherPatch;
        }

        static int CopyDirectoryWithoutOverwrite(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);
            var copied = 0;
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var dest = Path.Combine(destinationDir, Path.GetFileName(file));
                if (File.Exists(dest))
                    continue;
                File.Copy(file, dest, false);
                copied++;
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var dest = Path.Combine(destinationDir, Path.GetFileName(dir));
                copied += CopyDirectoryWithoutOverwrite(dir, dest);
            }

            return copied;
        }

        static void TryCreateDirectoryZip(string sourceDirectory, string zipPath, bool overwrite)
        {
            try
            {
                if (File.Exists(zipPath))
                {
                    if (!overwrite)
                    {
                        Debug.LogWarning("[PackageExporter] zip が既にあるためスキップします: " + zipPath);
                        return;
                    }
                    File.Delete(zipPath);
                }

                ZipFile.CreateFromDirectory(
                    sourceDirectory,
                    zipPath,
                    System.IO.Compression.CompressionLevel.Optimal,
                    true);
                Debug.Log("[PackageExporter] zip を生成しました: " + zipPath);
            }
            catch (Exception ex)
            {
                Debug.LogError("[PackageExporter] zip の生成に失敗: " + ex.Message);
            }
        }

        /// <summary>
        /// 関連フォルダを正規化する（空・重複・配布フォルダ自身・無効パスを除く）。
        /// </summary>
        public static string[] NormalizeRelatedFolders(string sourceAssetFolder, string[] relatedFolders)
        {
            var result = new List<string>();
            if (relatedFolders == null || relatedFolders.Length == 0)
                return result.ToArray();

            var source = (sourceAssetFolder ?? "").Replace("\\", "/").TrimEnd('/');
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < relatedFolders.Length; i++)
            {
                var folder = (relatedFolders[i] ?? "").Replace("\\", "/").TrimEnd('/');
                if (string.IsNullOrEmpty(folder))
                    continue;
                if (!folder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) && folder != "Assets")
                    continue;
                if (!string.IsNullOrEmpty(source) &&
                    string.Equals(folder, source, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    Debug.LogWarning("[PackageExporter] 関連フォルダが無効のためスキップ: " + folder);
                    continue;
                }
                if (!seen.Add(folder))
                    continue;
                result.Add(folder);
            }

            return result.ToArray();
        }

        static void AppendRelatedFolderPaths(List<string> paths, string sourceAssetFolder, string[] relatedFolders)
        {
            if (paths == null)
                return;

            var normalized = NormalizeRelatedFolders(sourceAssetFolder, relatedFolders);
            if (normalized.Length == 0)
                return;

            var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < normalized.Length; i++)
            {
                var folder = normalized[i];
                if (set.Add(folder))
                    paths.Add(folder);

                var folderPaths = GetAssetPathsInFolder(folder);
                for (int j = 0; j < folderPaths.Count; j++)
                {
                    if (set.Add(folderPaths[j]))
                        paths.Add(folderPaths[j]);
                }

                Debug.Log($"[PackageExporter] 関連フォルダを含めます: {folder} ({folderPaths.Count} assets)");
            }
        }

        /// <summary>
        /// Packages 上の SamirinBoothManagerInstaller を Assets/Editor/... へコピーして配布用に揃える。
        /// （Packages 側の編集用ソースは残す）
        /// </summary>
        public static bool StageBoothManagerInstallerToAssets()
        {
            try
            {
                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var sourceFull = Path.GetFullPath(Path.Combine(projectRoot, BoothManagerInstallerSourcePackageRelative));
                if (!Directory.Exists(sourceFull))
                {
                    Debug.LogWarning("[PackageExporter] BoothManagerInstaller ソースがありません: " + sourceFull);
                    return false;
                }

                var scriptFull = Path.Combine(sourceFull, BoothManagerInstallerScriptFileName);
                if (!File.Exists(scriptFull))
                {
                    Debug.LogWarning("[PackageExporter] BoothManagerInstaller スクリプトがありません: " + scriptFull);
                    return false;
                }

                // 古いステージを削除
                CleanupStagedBoothManagerInstaller();

                EnsureAssetFolder("Assets/Editor");
                EnsureAssetFolder(BoothManagerInstallerAssetPath);

                var destFull = Path.GetFullPath(Path.Combine(projectRoot, BoothManagerInstallerAssetPath));
                foreach (var file in Directory.GetFiles(sourceFull, "*", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(file);
                    // 編集用 asmdef があれば配布コピーから除外
                    if (name.EndsWith(".asmdef", System.StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".asmdef.meta", System.StringComparison.OrdinalIgnoreCase))
                        continue;
                    File.Copy(file, Path.Combine(destFull, name), true);
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                var stagedScript = BoothManagerInstallerAssetPath + "/" + BoothManagerInstallerScriptFileName;
                if (!File.Exists(Path.Combine(destFull, BoothManagerInstallerScriptFileName)))
                {
                    Debug.LogError("[PackageExporter] ステージ先にスクリプトがありません: " + stagedScript);
                    return false;
                }

                Debug.Log("[PackageExporter] SamirinBoothManagerInstaller を一時配置: " + BoothManagerInstallerAssetPath);
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[PackageExporter] SamirinBoothManagerInstaller のステージに失敗: " + ex.Message);
                return false;
            }
        }

        public static void CleanupStagedBoothManagerInstaller()
        {
            try
            {
                if (AssetDatabase.IsValidFolder(BoothManagerInstallerAssetPath))
                {
                    AssetDatabase.DeleteAsset(BoothManagerInstallerAssetPath);
                    return;
                }

                var fullPath = Path.Combine(Application.dataPath, "Editor", BoothManagerInstallerFolderName);
                if (Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, true);
                    var metaPath = fullPath + ".meta";
                    if (File.Exists(metaPath))
                        File.Delete(metaPath);
                    AssetDatabase.Refresh();
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[PackageExporter] SamirinBoothManagerInstaller ステージの削除に失敗: " + ex.Message);
            }
        }

        static void EnsureAssetFolder(string assetFolderPath)
        {
            var normalized = assetFolderPath.Replace("\\", "/").TrimEnd('/');
            if (AssetDatabase.IsValidFolder(normalized))
                return;

            var parts = normalized.Split('/');
            if (parts.Length < 2 || parts[0] != "Assets")
                return;

            var current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        /// <summary>
        /// SuperReImport 対象は Payload にまとめ、対象外はインポート画面のトグルに残す。
        /// </summary>
        static bool TryWrapWithSuperReImporter(
            string[] superReimportFolders,
            List<string> assetPaths,
            out List<string> reimporterAssetPaths)
        {
            reimporterAssetPaths = null;
            string tempPayload = null;
            try
            {
                var resetFolders = BuildResetTargetFolders(superReimportFolders);
                if (resetFolders.Count == 0)
                    Debug.Log("[PackageExporter] SuperReImport の対象フォルダはありません。既存フォルダは削除しません。");

                for (int i = 0; i < resetFolders.Count; i++)
                {
                    var folder = resetFolders[i];
                    if (folder.IndexOf('"') >= 0 || folder.IndexOf('|') >= 0 ||
                        folder.IndexOf('\n') >= 0 || folder.IndexOf('\r') >= 0)
                    {
                        Debug.LogError("[PackageExporter] リセット対象パスに使えない文字があります: " + folder);
                        return false;
                    }
                }

                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var sourceScript = Path.Combine(
                    projectRoot,
                    SuperReImporterSourcePackageRelative,
                    SuperReImporterScriptFileName);
                if (!File.Exists(sourceScript))
                {
                    Debug.LogError("[PackageExporter] SuperReImporter のソースがありません: " + sourceScript);
                    return false;
                }

                // 以前のステージが配布フォルダ内にあると Payload に混ざるので、先に消す。
                CleanupStagedSuperReImporter();

                SplitSuperReimportPaths(assetPaths, resetFolders, out var payloadPaths, out var selectablePaths);
                if (payloadPaths.Count == 0)
                {
                    reimporterAssetPaths = selectablePaths;
                    Debug.Log("[PackageExporter] 再インポート対象がないため、すべてインポート画面のトグルに含めます。");
                    return true;
                }

                tempPayload = Path.Combine(
                    Path.GetTempPath(),
                    "SuperReImporterBuild_" + Guid.NewGuid().ToString("N") + ".unitypackage");
                AssetDatabase.ExportPackage(payloadPaths.ToArray(), tempPayload, ExportPackageOptions.Recurse);
                if (!File.Exists(tempPayload) || new FileInfo(tempPayload).Length == 0)
                {
                    Debug.LogError("[PackageExporter] 再インポート用 Payload の作成に失敗しました。");
                    return false;
                }

                EnsureAssetFolder("Assets/Editor");
                EnsureAssetFolder(SuperReImporterAssetPath);
                var destFull = Path.GetFullPath(Path.Combine(projectRoot, SuperReImporterAssetPath));

                // この asmdef は書き出し中のコンパイルを止める。配布物には含めない。
                File.WriteAllText(
                    Path.Combine(destFull, SuperReImporterGuardAsmdefFileName),
                    SuperReImporterGuardAsmdefJson);
                File.WriteAllText(
                    Path.Combine(destFull, SuperReImporterSuppressFileName),
                    "export-staging\n");

                var stagedScript = Path.Combine(destFull, SuperReImporterScriptFileName);
                File.Copy(sourceScript, stagedScript, true);
                var token = Guid.NewGuid().ToString("N");
                if (!TryPatchSuperReImporterScript(stagedScript, string.Join("|", resetFolders.ToArray()), token))
                    return false;

                File.Copy(
                    tempPayload,
                    Path.Combine(destFull, SuperReImporterPayloadFileName),
                    true);

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                var scriptMeta = Path.Combine(destFull, SuperReImporterScriptFileName + ".meta");
                var payloadMeta = Path.Combine(destFull, SuperReImporterPayloadFileName + ".meta");
                if (!File.Exists(scriptMeta) || !File.Exists(payloadMeta))
                {
                    Debug.LogError("[PackageExporter] SuperReImporter の .meta が生成されませんでした。");
                    return false;
                }

                reimporterAssetPaths = GetAssetPathsInFolder(SuperReImporterAssetPath);
                foreach (var diskPath in GetDiskAssetPathsInFolder(SuperReImporterAssetPath))
                {
                    if (!reimporterAssetPaths.Contains(diskPath))
                        reimporterAssetPaths.Add(diskPath);
                }

                reimporterAssetPaths.RemoveAll(p =>
                    IsSuperReImporterFolderPath(p) || IsSuperReImporterExportExcluded(p));

                for (int i = 0; i < selectablePaths.Count; i++)
                {
                    if (!ContainsAssetPath(reimporterAssetPaths, selectablePaths[i]))
                        reimporterAssetPaths.Add(selectablePaths[i]);
                }

                var scriptAssetPath = (SuperReImporterAssetPath + "/" + SuperReImporterScriptFileName).Replace("\\", "/");
                var payloadAssetPath = (SuperReImporterAssetPath + "/" + SuperReImporterPayloadFileName).Replace("\\", "/");
                EnsureListed(reimporterAssetPaths, scriptAssetPath);
                EnsureListed(reimporterAssetPaths, payloadAssetPath);
                if (!ContainsAssetPath(reimporterAssetPaths, scriptAssetPath) ||
                    !ContainsAssetPath(reimporterAssetPaths, payloadAssetPath))
                {
                    Debug.LogError(
                        "[PackageExporter] SuperReImporter の同梱ファイルが揃っていません: " +
                        string.Join(", ", reimporterAssetPaths.ToArray()));
                    return false;
                }

                Debug.Log(
                    "[PackageExporter] SuperReImporter を同梱します。リセット対象: " +
                    string.Join(", ", resetFolders.ToArray()) +
                    " / インポート画面で選択できるアセット: " + selectablePaths.Count);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[PackageExporter] SuperReImporter の同梱に失敗: " + ex);
                return false;
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempPayload) && File.Exists(tempPayload))
                {
                    try
                    {
                        File.Delete(tempPayload);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[PackageExporter] 一時 Payload の削除に失敗: " + ex.Message);
                    }
                }
            }
        }

        /// <summary>
        /// SuperReImport で削除してよいフォルダか。同梱インストーラと SuperReImporter 自身は対象外。
        /// </summary>
        public static bool IsSuperReimportExcludedFolder(string assetFolderPath)
        {
            var folder = (assetFolderPath ?? "").Replace("\\", "/").TrimEnd('/');
            if (string.IsNullOrEmpty(folder) || string.Equals(folder, "Assets", StringComparison.OrdinalIgnoreCase))
                return true;
            if (!folder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return true;
            if (IsSameOrUnderFolder(folder, SuperReImporterAssetPath))
                return true;
            if (IsSameOrUnderFolder(folder, InstallerImport.InstallerFolderAssetPath))
                return true;
            if (IsSameOrUnderFolder(folder, BoothManagerInstallerAssetPath))
                return true;
            return false;
        }

        static bool IsSameOrUnderFolder(string folder, string parent)
        {
            if (string.Equals(folder, parent, StringComparison.OrdinalIgnoreCase))
                return true;
            return folder.StartsWith(parent + "/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 指定された SuperReImport 対象だけを残す。未指定・インストーラ・無効パスは除く。
        /// </summary>
        public static string[] NormalizeSuperReimportFolders(string[] folders)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (folders == null)
                return result.ToArray();

            for (int i = 0; i < folders.Length; i++)
            {
                var folder = (folders[i] ?? "").Replace("\\", "/").TrimEnd('/');
                if (string.IsNullOrEmpty(folder))
                    continue;
                if (IsSuperReimportExcludedFolder(folder))
                {
                    Debug.LogWarning("[PackageExporter] SuperReImport の対象外のためスキップ: " + folder);
                    continue;
                }
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    Debug.LogWarning("[PackageExporter] SuperReImport 対象フォルダが無効のためスキップ: " + folder);
                    continue;
                }
                if (!seen.Add(folder))
                    continue;
                result.Add(folder);
            }

            result.RemoveAll(folder =>
            {
                for (int i = 0; i < result.Count; i++)
                {
                    var other = result[i];
                    if (string.Equals(folder, other, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (folder.StartsWith(other + "/", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            });
            return result.ToArray();
        }

        static List<string> BuildResetTargetFolders(string[] superReimportFolders)
        {
            var normalized = NormalizeSuperReimportFolders(superReimportFolders);
            return new List<string>(normalized);
        }

        static void SplitSuperReimportPaths(
            List<string> assetPaths,
            List<string> resetFolders,
            out List<string> payloadPaths,
            out List<string> selectablePaths)
        {
            payloadPaths = new List<string>();
            selectablePaths = new List<string>();
            if (assetPaths == null)
                return;

            var payloadSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var selectableSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < assetPaths.Count; i++)
            {
                var path = (assetPaths[i] ?? "").Replace("\\", "/").TrimEnd('/');
                if (string.IsNullOrEmpty(path))
                    continue;
                if (IsSuperReImporterFolderPath(path) ||
                    path.StartsWith(SuperReImporterAssetPath + "/", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (IsUnderResetFolder(path, resetFolders))
                {
                    if (payloadSeen.Add(path))
                        payloadPaths.Add(path);
                    continue;
                }

                // フォルダパスを再帰すると、その中のリセット対象までインポート画面に出てしまう。
                if (IsAncestorOfResetFolder(path, resetFolders))
                    continue;

                if (selectableSeen.Add(path))
                    selectablePaths.Add(path);
            }
        }

        static bool IsUnderResetFolder(string assetPath, List<string> resetFolders)
        {
            if (resetFolders == null)
                return false;
            for (int i = 0; i < resetFolders.Count; i++)
            {
                var folder = resetFolders[i];
                if (string.Equals(assetPath, folder, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (assetPath.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        static bool IsAncestorOfResetFolder(string assetPath, List<string> resetFolders)
        {
            if (resetFolders == null || string.IsNullOrEmpty(assetPath))
                return false;
            for (int i = 0; i < resetFolders.Count; i++)
            {
                if (resetFolders[i].StartsWith(assetPath + "/", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        static bool IsSuperReImporterFolderPath(string assetPath)
        {
            return string.Equals(
                (assetPath ?? "").Replace("\\", "/").TrimEnd('/'),
                SuperReImporterAssetPath,
                StringComparison.OrdinalIgnoreCase);
        }

        static void EnsureListed(List<string> paths, string assetPath)
        {
            if (!ContainsAssetPath(paths, assetPath))
                paths.Add(assetPath);
        }

        static bool ContainsAssetPath(List<string> paths, string assetPath)
        {
            var target = (assetPath ?? "").Replace("\\", "/");
            for (int i = 0; i < paths.Count; i++)
            {
                if (string.Equals((paths[i] ?? "").Replace("\\", "/"), target, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        static bool IsSuperReImporterExportExcluded(string assetPath)
        {
            var name = Path.GetFileName((assetPath ?? "").Replace("\\", "/"));
            if (string.IsNullOrEmpty(name))
                return true;
            if (name.Equals(SuperReImporterGuardAsmdefFileName, StringComparison.OrdinalIgnoreCase))
                return true;
            if (name.Equals(SuperReImporterGuardAsmdefFileName + ".meta", StringComparison.OrdinalIgnoreCase))
                return true;
            if (name.Equals(SuperReImporterSuppressFileName, StringComparison.OrdinalIgnoreCase))
                return true;
            if (name.Equals(SuperReImporterSuppressFileName + ".meta", StringComparison.OrdinalIgnoreCase))
                return true;
            if (name.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase))
                return true;
            if (name.EndsWith(".asmdef.meta", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        static bool TryPatchSuperReImporterScript(string scriptFullPath, string foldersJoined, string token)
        {
            if (string.IsNullOrEmpty(scriptFullPath) || !File.Exists(scriptFullPath))
                return false;

            var text = File.ReadAllText(scriptFullPath);
            if (text.IndexOf(SuperReImporterFoldersPlaceholder, StringComparison.Ordinal) < 0 ||
                text.IndexOf(SuperReImporterTokenPlaceholder, StringComparison.Ordinal) < 0)
            {
                Debug.LogError("[PackageExporter] SuperReImporter のプレースホルダが見つかりません。");
                return false;
            }

            text = text.Replace(SuperReImporterFoldersPlaceholder, foldersJoined);
            text = text.Replace(SuperReImporterTokenPlaceholder, token);
            if (text.IndexOf(SuperReImporterFoldersPlaceholder, StringComparison.Ordinal) >= 0 ||
                text.IndexOf(SuperReImporterTokenPlaceholder, StringComparison.Ordinal) >= 0)
            {
                Debug.LogError("[PackageExporter] SuperReImporter のプレースホルダを置換しきれませんでした。");
                return false;
            }

            File.WriteAllText(scriptFullPath, text, new System.Text.UTF8Encoding(true));
            return true;
        }

        public static void CleanupStagedSuperReImporter()
        {
            try
            {
                if (AssetDatabase.IsValidFolder(SuperReImporterAssetPath))
                {
                    AssetDatabase.DeleteAsset(SuperReImporterAssetPath);
                    return;
                }

                var fullPath = Path.Combine(Application.dataPath, "Editor", SuperReImporterFolderName);
                if (Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, true);
                    var metaPath = fullPath + ".meta";
                    if (File.Exists(metaPath))
                        File.Delete(metaPath);
                    AssetDatabase.Refresh();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PackageExporter] SuperReImporter ステージの削除に失敗: " + ex.Message);
            }
        }

        /// <summary>
        /// Item Analyzer を開き、指定した配布フォルダを解析対象に設定します。
        /// </summary>
        public static void OpenItemAnalyzer(string sourceFolderPath)
        {
            ItemAnalyzer.OpenWithDirectory(sourceFolderPath);
        }
    }
}
