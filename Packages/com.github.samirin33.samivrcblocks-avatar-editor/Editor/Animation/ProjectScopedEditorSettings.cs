using System;
using UnityEditor;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    /// <summary>
    /// プロジェクト単位のユーザー設定（UserSettings/EditorUserSettings.asset）を読み書きする。
    /// 以前 EditorPrefs に保存していたキーは、プロジェクトごとに初回読み込み時だけ値を引き継ぐ。
    /// </summary>
    internal static class ProjectScopedEditorSettings
    {
        private const string MigratedSuffix = ".MigratedFromEditorPrefs";

        public static string GetString(string key, string defaultValue = "")
        {
            MigrateString(key);
            return EditorUserSettings.GetConfigValue(key) ?? defaultValue;
        }

        public static void SetString(string key, string value)
        {
            MarkMigrated(key);
            EditorUserSettings.SetConfigValue(key, value ?? "");
        }

        public static bool GetBool(string key, bool defaultValue)
        {
            MigrateBool(key, defaultValue);
            var value = EditorUserSettings.GetConfigValue(key);
            if (string.IsNullOrEmpty(value))
                return defaultValue;
            return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public static void SetBool(string key, bool value)
        {
            MarkMigrated(key);
            EditorUserSettings.SetConfigValue(key, value ? "1" : "0");
        }

        private static bool IsMigrated(string key)
        {
            return EditorUserSettings.GetConfigValue(key + MigratedSuffix) == "1";
        }

        private static void MarkMigrated(string key)
        {
            if (!IsMigrated(key))
                EditorUserSettings.SetConfigValue(key + MigratedSuffix, "1");
        }

        // 旧 EditorPrefs はプロジェクト共通のため削除しない（他プロジェクトの初回移行元として残す）
        private static void MigrateString(string key)
        {
            if (IsMigrated(key))
                return;
            MarkMigrated(key);
            if (EditorPrefs.HasKey(key))
                EditorUserSettings.SetConfigValue(key, EditorPrefs.GetString(key, ""));
        }

        private static void MigrateBool(string key, bool defaultValue)
        {
            if (IsMigrated(key))
                return;
            MarkMigrated(key);
            if (EditorPrefs.HasKey(key))
                EditorUserSettings.SetConfigValue(key, EditorPrefs.GetBool(key, defaultValue) ? "1" : "0");
        }
    }
}
