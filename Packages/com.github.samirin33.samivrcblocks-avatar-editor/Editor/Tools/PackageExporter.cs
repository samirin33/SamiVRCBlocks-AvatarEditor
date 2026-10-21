using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SamiVRCBlocksAvatar.Editor
{
    /// <summary>
    /// 配布フォルダを UnityPackage に書き出す。
    /// Booth 情報は PackageExporter.BoothInfo.cs、バージョンフォルダ・zip は PackageExporter.VersionFolder.cs、
    /// BoothManagerInstaller の一時配置は PackageExporter.Staging.cs、SuperReImporter 同梱は PackageExporter.SuperReImporter.cs。
    /// </summary>
    public static partial class PackageExporter
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

        static string AssetPathToFullPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            var relative = assetPath.Replace("\\", "/");
            if (!relative.StartsWith("Assets/") && relative != "Assets") return null;
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));
        }

        /// <summary>
        /// Assets パスを絶対パスに変換します。
        /// </summary>
        public static string ToFullPath(string assetPath)
        {
            return AssetPathToFullPath(assetPath);
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
            var suppressSuperReImporter = false;

            // ステージしたスクリプトのコンパイル結果でドメインリロードが走ると、書き出しと後片付けが途中で切れる。
            EditorApplication.LockReloadAssemblies();
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
                    // ステージした SuperReImporter が書き出し中に対象フォルダを消さないようにする。
                    SessionState.SetBool(SuperReImporterSuppressSessionKey, true);
                    suppressSuperReImporter = true;
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
                try
                {
                    if (cleanupSuperReImporter)
                        CleanupStagedSuperReImporter();
                    if (cleanupBoothManagerInstaller)
                        CleanupStagedBoothManagerInstaller();
                    if (cleanupInstaller)
                        InstallerImport.CleanupStagedInstaller();
                }
                finally
                {
                    if (suppressSuperReImporter)
                        SessionState.SetBool(SuperReImporterSuppressSessionKey, false);
                    EditorApplication.UnlockReloadAssemblies();
                }
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
        /// Item Analyzer を開き、指定した配布フォルダを解析対象に設定します。
        /// </summary>
        public static void OpenItemAnalyzer(string sourceFolderPath)
        {
            ItemAnalyzer.OpenWithDirectory(sourceFolderPath);
        }
    }
}
