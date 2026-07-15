#if UNITY_EDITOR
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

namespace Samirin.VRCUtility.AvatarEditor.Editor
{
    [InitializeOnLoad]
    public static class InstollerImport
    {
        public const string ZipFileName = "SamirinVRCUtility Avatar Installer.zip";
        public const string InstallerFolderAssetPath = "Assets/SamirinVRCUtility Avatar Installer";
        public const string InstallerEditorScriptFileName = "SamirinVRCUtilityAvatarInstallerEditor.cs";

        private static string EditorPrefsKey => "SamirinVRCUtility.InstallerImported." + Application.dataPath;

        static InstollerImport()
        {
            EditorApplication.delayCall += OnDelayCall;
        }

        private static void OnDelayCall()
        {
            EnsureInstallerExtracted(force: false);
        }

        /// <summary>
        /// Packages 内の ZIP を Assets に展開する。
        /// force=false でも、必須スクリプトが欠ける場合は再展開する。
        /// </summary>
        /// <returns>展開に成功した、または既に揃っている場合は true。</returns>
        public static bool EnsureInstallerExtracted(bool force = false)
        {
            if (!force && EditorPrefs.GetBool(EditorPrefsKey, false) && IsInstallerCompleteOnDisk())
                return true;

            return ExtractInstaller(force || !IsInstallerCompleteOnDisk());
        }

        [MenuItem("Tools/SamirinVRCUtility Avatar Installer Reimport")]
        private static void ReimportInstaller()
        {
            ExtractInstaller(force: true);
        }

        private static bool IsInstallerCompleteOnDisk()
        {
            var editorScript = Path.Combine(Application.dataPath, "SamirinVRCUtility Avatar Installer", InstallerEditorScriptFileName);
            return File.Exists(editorScript);
        }

        private static string GetInstallerZipPath()
        {
            string projectPath = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectPath)) return null;
            return Path.Combine(projectPath, "Packages",
                "com.github.samirin33.samirin-vrc-utility.avatar-editor", ZipFileName);
        }

        private static bool ExtractInstaller(bool force = false)
        {
            string zipPath = GetInstallerZipPath();
            if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
            {
                if (force)
                    Debug.LogWarning($"[SamirinVRCUtility] {ZipFileName} が見つかりません: {zipPath}");
                return false;
            }

            try
            {
                ExtractZipToDirectory(zipPath, Application.dataPath);
                EditorPrefs.SetBool(EditorPrefsKey, true);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                AssetDatabase.ImportAsset(InstallerFolderAssetPath, ImportAssetOptions.ImportRecursive);
                Debug.Log($"[SamirinVRCUtility] {ZipFileName} を Assets に解凍して配置しました。");
                return IsInstallerCompleteOnDisk();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SamirinVRCUtility] ZIP の解凍に失敗しました: {ex.Message}");
                return false;
            }
        }

        private static void ExtractZipToDirectory(string zipPath, string destinationPath)
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    string destPath = Path.Combine(destinationPath, entry.FullName);
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        if (!Directory.Exists(destPath))
                            Directory.CreateDirectory(destPath);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                        entry.ExtractToFile(destPath, overwrite: true);
                    }
                }
            }
        }
    }
}
#endif
