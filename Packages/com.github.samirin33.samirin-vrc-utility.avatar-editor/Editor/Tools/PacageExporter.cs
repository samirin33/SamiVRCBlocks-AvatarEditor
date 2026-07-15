using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Samirin.VRCUtility.AvatarEditor.Editor
{
    public static class PacageExporter
    {
        public const string AssetInfoFileName = "PackageAssetInfo.json";

        static string AssetPathToFullPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            var relative = assetPath.Replace("\\", "/");
            if (!relative.StartsWith("Assets/") && relative != "Assets") return null;
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));
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
        /// <param name="includeInstallerFolder">Assets/SamirinVRCUtility Avatar Installer をパッケージに含めるか</param>
        /// <returns>成功した場合の出力ファイルパス。失敗時は null。</returns>
        public static string ExportPackage(
            string sourceAssetFolder,
            PackageAssetInfo assetInfo,
            string packageName,
            string version,
            string outputDirectory,
            bool overwrite,
            bool includeInstallerFolder = false)
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
                SaveAssetInfo(sourceAssetFolder, assetInfo);
            }

            var paths = GetAssetPathsInFolder(sourceAssetFolder);
            if (includeInstallerFolder)
            {
                // Assets 上の Installer は不足しがち（初回展開後にスクリプトだけ消える等）なので、
                // エクスポート直前に Packages 内 ZIP から強制再展開して中身を揃える。
                if (!InstollerImport.EnsureInstallerExtracted(force: true))
                {
                    Debug.LogError(
                        "[PackageExporter] SamirinVRCUtility Avatar Installer の展開に失敗しました。" +
                        $" Packages 内の {InstollerImport.ZipFileName} を確認してください。");
                    return null;
                }

                var installerFolder = InstollerImport.InstallerFolderAssetPath;
                var installerPaths = GetAssetPathsInFolder(installerFolder);
                // FindAssets が取りこぼす場合に備え、ディスク上のファイルもアセットパスとして追加する
                foreach (var diskPath in GetDiskAssetPathsInFolder(installerFolder))
                {
                    if (!installerPaths.Contains(diskPath))
                        installerPaths.Add(diskPath);
                }

                if (installerPaths.Count == 0)
                {
                    Debug.LogError("[PackageExporter] Installer フォルダに含めるアセットが見つかりません: " + installerFolder);
                    return null;
                }

                var editorScriptPath = (installerFolder + "/" + InstollerImport.InstallerEditorScriptFileName).Replace("\\", "/");
                if (!installerPaths.Contains(editorScriptPath))
                {
                    Debug.LogError(
                        "[PackageExporter] Installer 内のスクリプトが含まれていません: " + editorScriptPath +
                        " — ZIP の内容を確認してください。");
                    return null;
                }

                var set = new HashSet<string>(paths);
                foreach (var p in installerPaths)
                {
                    if (!set.Contains(p)) { set.Add(p); paths.Add(p); }
                }

                Debug.Log($"[PackageExporter] Installer を含めます ({installerPaths.Count} assets): " + string.Join(", ", installerPaths));
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
    }
}
