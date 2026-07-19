#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Samirin.VRCUtility.AvatarEditor.Editor
{
    /// <summary>
    /// Installer 編集用ソース（Packages/.../Editor/...）を、エクスポート時だけ Assets へ一時移動する。
    /// </summary>
    public static class InstallerImport
    {
        public const string InstallerFolderName = "AvatarInstaller";
        public const string InstallerFolderAssetPath = "Assets/" + InstallerFolderName;
        public const string InstallerEditorScriptFileName = "SamirinVRCUtilityAvatarInstallerEditor.cs";

        /// <summary>編集用ソース（Packages/.../Editor/AvatarInstaller）</summary>
        public const string InstallerSourceFolderPackageRelative =
            "Packages/com.github.samirin33.samirin-vrc-utility.avatar-editor/Editor/" + InstallerFolderName;

        [MenuItem("Tools/SamirinVRCUtility/Avatar Installer/Move To Assets")]
        private static void MoveToAssetsMenu()
        {
            if (EnsureInstallerExtracted(force: true))
                Debug.Log($"[SamirinVRCUtility] Installer を {InstallerFolderAssetPath} へ移動しました。");
        }

        [MenuItem("Tools/SamirinVRCUtility/Avatar Installer/Restore To Package")]
        private static void RestoreToPackageMenu()
        {
            if (RestoreInstallerSourceToPackage())
                Debug.Log($"[SamirinVRCUtility] Installer を {InstallerSourceFolderPackageRelative} へ戻しました。");
        }

        /// <summary>
        /// force=true のとき、編集用ソースを Assets へ一時移動する（エクスポート用）。
        /// force=false のときは、ソースまたは Assets 上に揃っていれば true。
        /// </summary>
        public static bool EnsureInstallerExtracted(bool force = false)
        {
            if (!force)
                return IsSourceFolderComplete() || IsInstallerCompleteOnDisk();

            return MoveInstallerSourceToAssets();
        }

        public static bool IsInstallerCompleteOnDisk()
        {
            var editorScript = Path.Combine(Application.dataPath, InstallerFolderName, InstallerEditorScriptFileName);
            return File.Exists(editorScript);
        }

        public static string GetInstallerSourceFolderPath()
        {
            var projectPath = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectPath)) return null;
            return Path.Combine(projectPath, "Packages",
                "com.github.samirin33.samirin-vrc-utility.avatar-editor", "Editor", InstallerFolderName);
        }

        public static bool IsSourceFolderComplete()
        {
            var sourceFolder = GetInstallerSourceFolderPath();
            if (string.IsNullOrEmpty(sourceFolder) || !Directory.Exists(sourceFolder))
                return false;
            return File.Exists(Path.Combine(sourceFolder, InstallerEditorScriptFileName));
        }

        /// <summary>
        /// Packages 上の編集用ソースを Assets へ移動する。
        /// 既に Assets のみにある場合はそのまま成功扱い。
        /// </summary>
        public static bool MoveInstallerSourceToAssets()
        {
            try
            {
                // 既に移動済み
                if (IsInstallerCompleteOnDisk() && !IsSourceFolderComplete())
                {
                    Debug.Log($"[SamirinVRCUtility] Installer は既に {InstallerFolderAssetPath} にあります。");
                    return true;
                }

                if (!IsSourceFolderComplete())
                {
                    Debug.LogWarning($"[SamirinVRCUtility] 編集用ソースが見つかりません: {InstallerSourceFolderPackageRelative}");
                    return IsInstallerCompleteOnDisk();
                }

                // Assets 側に古いコピーがあれば削除
                if (AssetDatabase.IsValidFolder(InstallerFolderAssetPath) || IsInstallerCompleteOnDisk())
                    RemoveInstallerFolder();

                var err = AssetDatabase.MoveAsset(InstallerSourceFolderPackageRelative, InstallerFolderAssetPath);
                if (!string.IsNullOrEmpty(err))
                {
                    Debug.LogError($"[SamirinVRCUtility] Installer の Assets への移動に失敗: {err}");
                    return false;
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                Debug.Log($"[SamirinVRCUtility] Installer を {InstallerFolderAssetPath} へ一時移動しました。");
                return IsInstallerCompleteOnDisk();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SamirinVRCUtility] Installer の移動に失敗しました: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// エクスポート後など、Assets 上の Installer を Packages の編集用場所へ戻す。
        /// </summary>
        public static bool RestoreInstallerSourceToPackage()
        {
            try
            {
                if (IsSourceFolderComplete() && !IsInstallerCompleteOnDisk())
                    return true;

                if (!IsInstallerCompleteOnDisk() && !AssetDatabase.IsValidFolder(InstallerFolderAssetPath))
                    return true;

                // 戻り先に残骸があれば削除
                if (IsSourceFolderComplete() || AssetDatabase.IsValidFolder(InstallerSourceFolderPackageRelative))
                {
                    if (AssetDatabase.IsValidFolder(InstallerSourceFolderPackageRelative))
                        AssetDatabase.DeleteAsset(InstallerSourceFolderPackageRelative);
                    else
                    {
                        var src = GetInstallerSourceFolderPath();
                        if (!string.IsNullOrEmpty(src) && Directory.Exists(src))
                            Directory.Delete(src, true);
                    }
                }

                var sourceParent = "Packages/com.github.samirin33.samirin-vrc-utility.avatar-editor/Editor";
                if (!AssetDatabase.IsValidFolder(sourceParent))
                {
                    Debug.LogError($"[SamirinVRCUtility] 戻り先親フォルダがありません: {sourceParent}");
                    return false;
                }

                var fromPath = AssetDatabase.IsValidFolder(InstallerFolderAssetPath)
                    ? InstallerFolderAssetPath
                    : null;
                if (fromPath == null)
                {
                    Debug.LogWarning("[SamirinVRCUtility] Assets 上に戻す Installer がありません。");
                    return false;
                }

                var err = AssetDatabase.MoveAsset(fromPath, InstallerSourceFolderPackageRelative);
                if (!string.IsNullOrEmpty(err))
                {
                    Debug.LogError($"[SamirinVRCUtility] Installer の Packages への復帰に失敗: {err}");
                    return false;
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                Debug.Log($"[SamirinVRCUtility] Installer を {InstallerSourceFolderPackageRelative} へ戻しました。");
                return IsSourceFolderComplete();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SamirinVRCUtility] Installer の復帰に失敗しました: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Assets 上の Installer フォルダを削除する（復帰できない場合の後始末や、配布先での自己削除用）。
        /// </summary>
        public static void RemoveInstallerFolder()
        {
            try
            {
                if (AssetDatabase.IsValidFolder(InstallerFolderAssetPath))
                {
                    AssetDatabase.DeleteAsset(InstallerFolderAssetPath);
                    Debug.Log($"[SamirinVRCUtility] {InstallerFolderAssetPath} を削除しました。");
                    return;
                }

                var fullPath = Path.Combine(Application.dataPath, InstallerFolderName);
                if (Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, true);
                    var metaPath = fullPath + ".meta";
                    if (File.Exists(metaPath))
                        File.Delete(metaPath);
                    AssetDatabase.Refresh();
                    Debug.Log($"[SamirinVRCUtility] {InstallerFolderAssetPath} を削除しました。");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[SamirinVRCUtility] Installer フォルダの削除に失敗しました: {ex.Message}");
            }
        }
    }
}
#endif
