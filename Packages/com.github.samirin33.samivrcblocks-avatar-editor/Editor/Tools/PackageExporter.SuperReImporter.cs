using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SamiVRCBlocksAvatar.Editor
{
    /// <summary>
    /// SuperReImport 対象フォルダを Payload.unitypackage にまとめ、SuperReImporter と一緒に同梱する処理。
    /// </summary>
    public static partial class PackageExporter
    {
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

                AssetFolderUtility.EnsureAssetFolder("Assets/Editor");
                var destFull = Path.GetFullPath(Path.Combine(projectRoot, SuperReImporterAssetPath));
                Directory.CreateDirectory(destFull);
                // 商品ごとに GUID が変わると、複数商品を導入したとき同じパスに別アセットとして並んでクラスが重複する。
                StagedAssetMeta.WriteFolderMeta(destFull, SuperReImporterAssetPath);

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
                StagedAssetMeta.WriteFileMeta(
                    stagedScript,
                    SuperReImporterAssetPath + "/" + SuperReImporterScriptFileName,
                    sourceScript + ".meta");

                var stagedPayload = Path.Combine(destFull, SuperReImporterPayloadFileName);
                File.Copy(tempPayload, stagedPayload, true);
                StagedAssetMeta.WriteFileMeta(
                    stagedPayload,
                    SuperReImporterAssetPath + "/" + SuperReImporterPayloadFileName);

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
                AssetFolderUtility.DeleteAssetFolder(SuperReImporterAssetPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PackageExporter] SuperReImporter ステージの削除に失敗: " + ex.Message);
            }
        }
    }
}
