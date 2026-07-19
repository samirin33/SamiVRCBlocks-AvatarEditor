#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Samirin.VRCUtility.AvatarEditor.Editor
{
    /// <summary>
    /// Packages/.../Editor/AvatarInstaller を、エクスポート時だけ Assets/AvatarInstaller へ一時配置する。
    /// パッケージに Assets パスで含めることで、インポート先でも Assets/AvatarInstaller として展開される。
    /// </summary>
    public static class InstallerImport
    {
        public const string InstallerFolderName = "AvatarInstaller";
        public const string InstallerFolderAssetPath = "Assets/" + InstallerFolderName;
        public const string InstallerEditorScriptFileName = "SamirinVRCUtilityAvatarInstallerEditor.cs";
        public const string InstallerUnityPackageFileName = "AvatarsInstaller.unitypackage";

        /// <summary>編集用ソース（Packages/.../Editor/AvatarInstaller）</summary>
        public const string InstallerSourceFolderPackageRelative =
            "Packages/com.github.samirin33.samirin-vrc-utility.avatar-editor/Editor/" + InstallerFolderName;

        /// <summary>配布用に同梱する Editor 専用 asmdef（ソース用 asmdef は defineConstraints 付きのため別物）。</summary>
        const string DistributionAsmdefFileName = "Samirin.VRCUtility.AvatarInstaller.asmdef";

        const string DistributionAsmdefJson =
            "{\n" +
            "    \"name\": \"Samirin.VRCUtility.AvatarInstaller\",\n" +
            "    \"rootNamespace\": \"\",\n" +
            "    \"references\": [],\n" +
            "    \"includePlatforms\": [\n" +
            "        \"Editor\"\n" +
            "    ],\n" +
            "    \"excludePlatforms\": [],\n" +
            "    \"allowUnsafeCode\": false,\n" +
            "    \"overrideReferences\": false,\n" +
            "    \"precompiledReferences\": [],\n" +
            "    \"autoReferenced\": true,\n" +
            "    \"defineConstraints\": [],\n" +
            "    \"versionDefines\": [],\n" +
            "    \"noEngineReferences\": false\n" +
            "}\n";

        [MenuItem("Tools/SamirinVRCUtility/Avatar Installer/Stage To Assets")]
        private static void StageToAssetsMenu()
        {
            if (StageInstallerToAssets())
                Debug.Log($"[SamirinVRCUtility] Installer を {InstallerFolderAssetPath} へ一時配置しました。");
        }

        [MenuItem("Tools/SamirinVRCUtility/Avatar Installer/Cleanup Staged Assets")]
        private static void CleanupStagedMenu()
        {
            CleanupStagedInstaller();
            Debug.Log($"[SamirinVRCUtility] {InstallerFolderAssetPath} を削除しました。");
        }

        /// <summary>
        /// force=true のとき、編集用ソースを Assets へ一時コピーする（エクスポート用）。
        /// force=false のときは、ソースまたは Assets 上に揃っていれば true。
        /// </summary>
        public static bool EnsureInstallerExtracted(bool force = false)
        {
            if (!force)
                return IsSourceFolderComplete() || IsInstallerCompleteOnDisk();

            return StageInstallerToAssets();
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
        /// Packages 上の編集用ソースを Assets/AvatarInstaller へコピーする（原本は Packages に残す）。
        /// 編集用 asmdef は配布に含めず、Editor 専用の配布用 asmdef を書き出す。
        /// </summary>
        public static bool StageInstallerToAssets()
        {
            try
            {
                var sourceFolder = GetInstallerSourceFolderPath();
                if (string.IsNullOrEmpty(sourceFolder) || !Directory.Exists(sourceFolder))
                {
                    Debug.LogWarning($"[SamirinVRCUtility] 編集用ソースが見つかりません: {InstallerSourceFolderPackageRelative}");
                    return IsInstallerCompleteOnDisk();
                }

                if (!File.Exists(Path.Combine(sourceFolder, InstallerEditorScriptFileName)))
                {
                    Debug.LogWarning($"[SamirinVRCUtility] インストーラスクリプトがありません: {InstallerEditorScriptFileName}");
                    return false;
                }

                CleanupStagedInstaller();
                EnsureAssetFolder(InstallerFolderAssetPath);

                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var destFull = Path.GetFullPath(Path.Combine(projectRoot, InstallerFolderAssetPath));

                foreach (var file in Directory.GetFiles(sourceFolder, "*", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(file);
                    // 編集用 asmdef（defineConstraints 付き）は配布しない
                    if (name.EndsWith(".asmdef", System.StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".asmdef.meta", System.StringComparison.OrdinalIgnoreCase))
                        continue;

                    File.Copy(file, Path.Combine(destFull, name), true);
                }

                // インポート先で UnityEditor 参照できるよう Editor 専用 asmdef を同梱
                File.WriteAllText(Path.Combine(destFull, DistributionAsmdefFileName), DistributionAsmdefJson);

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                if (!IsInstallerCompleteOnDisk())
                {
                    Debug.LogError($"[SamirinVRCUtility] ステージ先にスクリプトがありません: {InstallerFolderAssetPath}/{InstallerEditorScriptFileName}");
                    return false;
                }

                var unityPackage = Path.Combine(destFull, InstallerUnityPackageFileName);
                if (!File.Exists(unityPackage))
                    Debug.LogWarning($"[SamirinVRCUtility] {InstallerUnityPackageFileName} がステージに含まれていません。");

                Debug.Log($"[SamirinVRCUtility] Installer を {InstallerFolderAssetPath} へ一時配置しました。");
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SamirinVRCUtility] Installer のステージに失敗しました: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// エクスポート後など、Assets 上の一時配置だけを削除する（Packages 原本は触らない）。
        /// </summary>
        public static void CleanupStagedInstaller()
        {
            RemoveInstallerFolder();
        }

        /// <summary>
        /// 旧 API 互換: Assets 上のステージを消して Packages 原本があることを確認する。
        /// </summary>
        public static bool RestoreInstallerSourceToPackage()
        {
            CleanupStagedInstaller();
            if (IsSourceFolderComplete())
            {
                Debug.Log($"[SamirinVRCUtility] Packages 原本を確認: {InstallerSourceFolderPackageRelative}");
                return true;
            }

            Debug.LogWarning($"[SamirinVRCUtility] Packages 原本が見つかりません: {InstallerSourceFolderPackageRelative}");
            return false;
        }

        /// <summary>
        /// Assets 上の Installer フォルダを削除する。
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
#endif
