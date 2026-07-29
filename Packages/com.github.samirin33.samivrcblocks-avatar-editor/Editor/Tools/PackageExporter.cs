using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Samirin.VRCUtility.AvatarEditor.Editor
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
        const string BoothAssetInfoTypeName = "SamirinBoothAssetInfo";

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
        /// <param name="includeInstallerFolder">AvatarInstaller を Assets へ一時配置してパッケージに含めるか</param>
        /// <param name="includeBoothManagerInstaller">SamirinBoothManagerInstaller をパッケージに同梱するか（samirin33 配下向け）</param>
        /// <returns>成功した場合の出力ファイルパス。失敗時は null。</returns>
        public static string ExportPackage(
            string sourceAssetFolder,
            PackageAssetInfo assetInfo,
            string packageName,
            string version,
            string outputDirectory,
            bool overwrite,
            bool includeInstallerFolder = false,
            bool includeBoothManagerInstaller = false)
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
                SaveAssetInfo(sourceAssetFolder, assetInfo);
            }

            var paths = GetAssetPathsInFolder(sourceAssetFolder);
            AppendRelatedFolderPaths(paths, sourceAssetFolder, assetInfo?.relatedFolders);
            // Installer 同梱時は Packages→Assets へ一時移動し、終了後に戻す
            var cleanupInstaller = false;
            var cleanupBoothManagerInstaller = false;
            try
            {
                if (includeInstallerFolder)
                {
                    cleanupInstaller = true;

                    // Packages 原本は残し、Assets/AvatarInstaller へ一時コピーして同梱する
                    // → インポート時も Assets/AvatarInstaller として展開される
                    if (!InstallerImport.StageInstallerToAssets())
                    {
                        Debug.LogError(
                            "[PackageExporter] AvatarInstaller の配置に失敗しました。" +
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
                        p.EndsWith("AvatarInstaller.Source.asmdef", System.StringComparison.OrdinalIgnoreCase) ||
                        p.EndsWith("AvatarInstaller.Source.asmdef.meta", System.StringComparison.OrdinalIgnoreCase) ||
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

                    Debug.Log($"[PackageExporter] AvatarInstaller を含めます ({installerPaths.Count} assets): " + string.Join(", ", installerPaths));
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
                var outputPath = Path.Combine(outputDirectory, fileName).Replace("\\", "/");

                if (File.Exists(outputPath) && !overwrite)
                {
                    Debug.LogWarning("[PackageExporter] Output file already exists and overwrite is false: " + outputPath);
                    return null;
                }

                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                AssetDatabase.ExportPackage(paths.ToArray(), outputPath, ExportPackageOptions.Recurse);
                Debug.Log($"[PackageExporter] Exported: {outputPath}");
                return outputPath;
            }
            finally
            {
                if (cleanupBoothManagerInstaller)
                    CleanupStagedBoothManagerInstaller();
                if (cleanupInstaller)
                    InstallerImport.CleanupStagedInstaller();
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
    }
}
