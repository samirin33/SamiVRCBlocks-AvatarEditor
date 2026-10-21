using UnityEditor;

namespace SamiVRCBlocksAvatar.Editor
{
    /// <summary>
    /// Package Exporter / VN3 License で共有する、プロジェクト固有のパス設定（配布フォルダ・出力先）。
    /// EditorPrefs は全プロジェクト共通なので EditorUserSettings（プロジェクトの UserSettings）に保存する。
    /// 旧 EditorPrefs の値は初回だけ引き継ぎ、他プロジェクトも移行できるよう旧キーは消さない。
    /// </summary>
    internal static class PackageExporterSettings
    {
        const string LegacyPrefsKeyOutputDirectory = "SamiVRCBlocksAvatar.PackageExporter.OutputDirectory";
        const string LegacyPrefsKeySourceFolder = "SamiVRCBlocksAvatar.PackageExporter.SourceFolder";

        const string ConfigKeyOutputDirectory = "SamiVRCBlocksAvatar.PackageExporter.OutputDirectory";
        const string ConfigKeySourceFolder = "SamiVRCBlocksAvatar.PackageExporter.SourceFolder";
        const string ConfigKeyMigrated = "SamiVRCBlocksAvatar.PackageExporter.PathsMigrated";

        public static string OutputDirectory
        {
            get => Get(ConfigKeyOutputDirectory);
            set => Set(ConfigKeyOutputDirectory, value);
        }

        public static string SourceFolder
        {
            get => Get(ConfigKeySourceFolder);
            set => Set(ConfigKeySourceFolder, value);
        }

        static string Get(string key)
        {
            MigrateOnce();
            return EditorUserSettings.GetConfigValue(key) ?? "";
        }

        static void Set(string key, string value)
        {
            MigrateOnce();
            EditorUserSettings.SetConfigValue(key, value ?? "");
        }

        static void MigrateOnce()
        {
            if (EditorUserSettings.GetConfigValue(ConfigKeyMigrated) == "1")
                return;

            if (string.IsNullOrEmpty(EditorUserSettings.GetConfigValue(ConfigKeyOutputDirectory)) &&
                EditorPrefs.HasKey(LegacyPrefsKeyOutputDirectory))
                EditorUserSettings.SetConfigValue(
                    ConfigKeyOutputDirectory,
                    EditorPrefs.GetString(LegacyPrefsKeyOutputDirectory, ""));

            if (string.IsNullOrEmpty(EditorUserSettings.GetConfigValue(ConfigKeySourceFolder)) &&
                EditorPrefs.HasKey(LegacyPrefsKeySourceFolder))
                EditorUserSettings.SetConfigValue(
                    ConfigKeySourceFolder,
                    EditorPrefs.GetString(LegacyPrefsKeySourceFolder, ""));

            EditorUserSettings.SetConfigValue(ConfigKeyMigrated, "1");
        }
    }
}
