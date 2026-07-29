#if UNITY_EDITOR
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace SamiVRCBlocksAvatar.Editor
{
    /// <summary>
    /// Packages/.../Editor/SamiVRCBlocksAvatarInstaller を、エクスポート時だけ Assets/SamiVRCBlocksAvatarInstaller へ一時配置する。
    /// パッケージに Assets パスで含めることで、インポート先でも Assets/SamiVRCBlocksAvatarInstaller として展開される。
    /// </summary>
    public static class InstallerImport
    {
        /// <summary>配布・ステージ先フォルダ名（Assets 配下）。</summary>
        public const string InstallerFolderName = "SamiVRCBlocksAvatarInstaller";
        /// <summary>Packages 上の編集用ソースフォルダ名。</summary>
        public const string InstallerSourceFolderName = "SamiVRCBlocksAvatarInstaller";
        public const string InstallerFolderAssetPath = "Assets/" + InstallerFolderName;
        public const string InstallerEditorScriptFileName = "SamiVRCBlocksAvatarInstallerEditor.cs";
        public const string InstallerUnityPackageFileName = "SamiVRCBlocksInstaller.unitypackage";

        /// <summary>編集用ソース（Packages/.../Editor/SamiVRCBlocksAvatarInstaller）</summary>
        public const string InstallerSourceFolderPackageRelative =
            "Packages/com.github.samirin33.samivrcblocks-avatar-editor/Editor/" + InstallerSourceFolderName;

        /// <summary>配布用に同梱する Editor 専用 asmdef（ソース用 asmdef は defineConstraints 付きのため別物）。</summary>
        const string DistributionAsmdefFileName = "SamiVRCBlocksAvatar.Installer.asmdef";

        const string DistributionAsmdefJson =
            "{\n" +
            "    \"name\": \"SamiVRCBlocksAvatar.Installer\",\n" +
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

        static readonly Regex TargetAssetGuidRegex = new Regex(
            @"(private\s+const\s+string\s+TargetAssetGUID\s*=\s*"")([0-9a-fA-F]{32})("")",
            RegexOptions.Compiled);

        [MenuItem("Tools/SamiVRCBlocksAvatar/Avatar Installer/Stage To Assets")]
        private static void StageToAssetsMenu()
        {
            if (StageInstallerToAssets())
                Debug.Log($"[SamiVRCBlocksAvatar] Installer を {InstallerFolderAssetPath} へ一時配置しました。");
        }

        [MenuItem("Tools/SamiVRCBlocksAvatar/Avatar Installer/Cleanup Staged Assets")]
        private static void CleanupStagedMenu()
        {
            CleanupStagedInstaller();
            Debug.Log($"[SamiVRCBlocksAvatar] {InstallerFolderAssetPath} を削除しました。");
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
                "com.github.samirin33.samivrcblocks-avatar-editor", "Editor", InstallerSourceFolderName);
        }

        public static bool IsSourceFolderComplete()
        {
            var sourceFolder = GetInstallerSourceFolderPath();
            if (string.IsNullOrEmpty(sourceFolder) || !Directory.Exists(sourceFolder))
                return false;
            return File.Exists(Path.Combine(sourceFolder, InstallerEditorScriptFileName));
        }

        /// <summary>
        /// Packages 上の編集用ソースを Assets/SamiVRCBlocksAvatarInstaller へコピーする（原本は Packages に残す）。
        /// 編集用 asmdef は配布に含めず、Editor 専用の配布用 asmdef を書き出す。
        /// ステージ先 unitypackage には固有 GUID を割り当て、Installer スクリプトの TargetAssetGUID に反映する。
        /// </summary>
        public static bool StageInstallerToAssets()
        {
            try
            {
                var sourceFolder = GetInstallerSourceFolderPath();
                if (string.IsNullOrEmpty(sourceFolder) || !Directory.Exists(sourceFolder))
                {
                    Debug.LogWarning($"[SamiVRCBlocksAvatar] 編集用ソースが見つかりません: {InstallerSourceFolderPackageRelative}");
                    return IsInstallerCompleteOnDisk();
                }

                if (!File.Exists(Path.Combine(sourceFolder, InstallerEditorScriptFileName)))
                {
                    Debug.LogWarning($"[SamiVRCBlocksAvatar] インストーラスクリプトがありません: {InstallerEditorScriptFileName}");
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

                    // Packages 側 GUID 衝突を避けるため .meta はコピーしない（後で固有 GUID を割り当てる）
                    if (name.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase))
                        continue;

                    File.Copy(file, Path.Combine(destFull, name), true);
                }

                var unityPackageFullPath = Path.Combine(destFull, InstallerUnityPackageFileName);
                if (!File.Exists(unityPackageFullPath))
                {
                    Debug.LogError($"[SamiVRCBlocksAvatar] {InstallerUnityPackageFileName} がステージに含まれていません。");
                    return false;
                }

                // 仮展開先 unitypackage 専用 GUID を発行し、スクリプトの TargetAssetGUID に埋め込む
                var stagedUnityPackageGuid = GUID.Generate().ToString();
                WriteDefaultImporterMeta(unityPackageFullPath + ".meta", stagedUnityPackageGuid);

                var stagedScriptPath = Path.Combine(destFull, InstallerEditorScriptFileName);
                if (!PatchTargetAssetGuid(stagedScriptPath, stagedUnityPackageGuid))
                {
                    Debug.LogError(
                        "[SamiVRCBlocksAvatar] ステージ先スクリプトの TargetAssetGUID 書き換えに失敗しました: " +
                        stagedScriptPath);
                    return false;
                }

                // インポート先で UnityEditor 参照できるよう Editor 専用 asmdef を同梱
                File.WriteAllText(Path.Combine(destFull, DistributionAsmdefFileName), DistributionAsmdefJson);

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                if (!IsInstallerCompleteOnDisk())
                {
                    Debug.LogError($"[SamiVRCBlocksAvatar] ステージ先にスクリプトがありません: {InstallerFolderAssetPath}/{InstallerEditorScriptFileName}");
                    return false;
                }

                var resolved = AssetDatabase.GUIDToAssetPath(stagedUnityPackageGuid);
                Debug.Log(
                    $"[SamiVRCBlocksAvatar] Installer を {InstallerFolderAssetPath} へ一時配置しました。" +
                    $" TargetAssetGUID={stagedUnityPackageGuid} → '{resolved}'");
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SamiVRCBlocksAvatar] Installer のステージに失敗しました: {ex.Message}");
                return false;
            }
        }

        static bool PatchTargetAssetGuid(string scriptFullPath, string guid)
        {
            if (string.IsNullOrEmpty(scriptFullPath) || !File.Exists(scriptFullPath))
                return false;
            if (string.IsNullOrEmpty(guid) || guid.Length != 32)
                return false;

            var source = File.ReadAllText(scriptFullPath);
            if (!TargetAssetGuidRegex.IsMatch(source))
                return false;

            var patched = TargetAssetGuidRegex.Replace(source, "${1}" + guid + "${3}", 1);
            File.WriteAllText(scriptFullPath, patched);
            return true;
        }

        static void WriteDefaultImporterMeta(string metaFullPath, string guid)
        {
            var meta =
                "fileFormatVersion: 2\n" +
                "guid: " + guid + "\n" +
                "DefaultImporter:\n" +
                "  externalObjects: {}\n" +
                "  userData: \n" +
                "  assetBundleName: \n" +
                "  assetBundleVariant: \n";
            File.WriteAllText(metaFullPath, meta);
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
                Debug.Log($"[SamiVRCBlocksAvatar] Packages 原本を確認: {InstallerSourceFolderPackageRelative}");
                return true;
            }

            Debug.LogWarning($"[SamiVRCBlocksAvatar] Packages 原本が見つかりません: {InstallerSourceFolderPackageRelative}");
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
                    Debug.Log($"[SamiVRCBlocksAvatar] {InstallerFolderAssetPath} を削除しました。");
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
                    Debug.Log($"[SamiVRCBlocksAvatar] {InstallerFolderAssetPath} を削除しました。");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[SamiVRCBlocksAvatar] Installer フォルダの削除に失敗しました: {ex.Message}");
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
