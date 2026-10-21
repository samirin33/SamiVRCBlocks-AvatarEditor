using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SamiVRCBlocksAvatar.Editor
{
    /// <summary>
    /// SamirinBoothManagerInstaller を Assets/Editor へ一時配置して配布物に含める処理。
    /// </summary>
    public static partial class PackageExporter
    {
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

                AssetFolderUtility.EnsureAssetFolder("Assets/Editor");

                // Packages 側の .meta をそのまま使うと同じ GUID のアセットが 2 つになるため、配置先パス由来の固定 GUID で書く。
                var destFull = Path.GetFullPath(Path.Combine(projectRoot, BoothManagerInstallerAssetPath));
                Directory.CreateDirectory(destFull);
                StagedAssetMeta.WriteFolderMeta(destFull, BoothManagerInstallerAssetPath);
                StagedAssetMeta.CopyTreeWithStableMeta(
                    sourceFull,
                    destFull,
                    BoothManagerInstallerAssetPath,
                    name => name.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase),
                    _ => true);

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
                AssetFolderUtility.DeleteAssetFolder(BoothManagerInstallerAssetPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[PackageExporter] SamirinBoothManagerInstaller ステージの削除に失敗: " + ex.Message);
            }
        }
    }
}
