using System.IO;
using UnityEditor;
using UnityEngine;

namespace SamiVRCBlocksAvatar.Editor
{
    /// <summary>
    /// Assets 配下のフォルダ作成・削除の共通処理（PackageExporter / InstallerImport 用）。
    /// 導入先へ単体で配布される SuperReImporter / SamirinBoothManagerInstaller はこれに依存させない。
    /// </summary>
    internal static class AssetFolderUtility
    {
        public static void EnsureAssetFolder(string assetFolderPath)
        {
            var normalized = (assetFolderPath ?? "").Replace("\\", "/").TrimEnd('/');
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
        /// Assets 配下のフォルダを AssetDatabase 経由で削除し、未登録でディスクにだけ残っている場合は .meta ごと消す。
        /// </summary>
        /// <returns>何か削除したら true</returns>
        public static bool DeleteAssetFolder(string assetFolderPath)
        {
            var normalized = (assetFolderPath ?? "").Replace("\\", "/").TrimEnd('/');
            if (!normalized.StartsWith("Assets/"))
                return false;

            if (AssetDatabase.IsValidFolder(normalized))
                return AssetDatabase.DeleteAsset(normalized);

            var fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", normalized));
            if (!Directory.Exists(fullPath))
                return false;

            Directory.Delete(fullPath, true);
            var metaPath = fullPath + ".meta";
            if (File.Exists(metaPath))
                File.Delete(metaPath);
            AssetDatabase.Refresh();
            return true;
        }
    }
}
