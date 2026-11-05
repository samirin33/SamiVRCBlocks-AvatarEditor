#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Samirin33.Editor;
using Samirin33.SamirinVRCUtility.AvatarEditor;

namespace Samirin33.SamirinVRCUtility.AvatarEditor
{
    static class AnimationClipSelectorSettingsProvider
    {
        const string SettingsPath = "Preferences/Samirin Editor Tools/Animation Clip Selector";

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider(SettingsPath, SettingsScope.User)
            {
                label = "Animation Clip Selector",
                keywords = new HashSet<string>(new[] { "Animation", "Clip", "Selector", "競合", "GUID", "ignore", "無視" }),
                guiHandler = OnGUI
            };
        }

        static void OnGUI(string searchContext)
        {
            SamirinEditorStyleHelper.DrawWithBlueBackgroundForSettingsGui(() =>
            {
                var settings = LoadOrCreateSettings();
                if (settings == null)
                {
                    EditorGUILayout.HelpBox("設定アセットを読み込めませんでした。", MessageType.Warning);
                    return;
                }

                var so = new SerializedObject(settings);
                var ignoreClipsProp = so.FindProperty("_ignoreClips");
                if (ignoreClipsProp == null)
                {
                    EditorGUILayout.HelpBox("IgnoreClips プロパティが見つかりません。", MessageType.Warning);
                    return;
                }

                EditorGUILayout.Space(4);
                const float helpBoxHeight = 52f;
                var helpRect = EditorGUILayout.GetControlRect(false, helpBoxHeight);
                EditorGUI.HelpBox(helpRect,
                    "ここに登録した AnimationClip は、レイヤー間の競合警告の対象外になります。\n" +
                    "（例: 意図的に複数レイヤーで使うクリップを追加）",
                    MessageType.Info);
                EditorGUILayout.Space(4);

                so.Update();
                EditorGUILayout.PropertyField(ignoreClipsProp, new GUIContent("競合警告を無視する AnimationClip 一覧"), true);
                so.ApplyModifiedProperties();

                if (GUI.changed)
                {
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssets();
                    AnimationClipSelector.InvalidatePathConflictCache();
                }

                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("無視しているコンフリクト", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "競合詳細ウィンドウの「このコンフリクトを無視する」で追加したパス＋属性です。戻すと警告が再表示されます。",
                    MessageType.Info);

                var ignored = settings.IgnoredConflicts;
                if (ignored == null || ignored.Count == 0)
                {
                    EditorGUILayout.LabelField("なし", EditorStyles.miniLabel);
                }
                else
                {
                    var removeIndex = -1;
                    for (int i = 0; i < ignored.Count; i++)
                    {
                        var entry = ignored[i];
                        if (entry == null) continue;

                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(new GUIContent(FormatIgnoredConflict(entry), entry.controllerPath + "\n" + entry.path), GUILayout.MinWidth(80));
                        if (GUILayout.Button("戻す", GUILayout.Width(48)))
                            removeIndex = i;
                        EditorGUILayout.EndHorizontal();
                    }

                    if (removeIndex >= 0)
                    {
                        Undo.RecordObject(settings, "無視したコンフリクトを戻す");
                        if (settings.RemoveIgnoredConflictAt(removeIndex))
                        {
                            AnimationClipSelectorStateManager.SetSettingsInstance(settings);
                            AnimationClipSelectorStateManager.RequestSave();
                            AnimationClipSelector.InvalidatePathConflictCache();
                            AnimationClipSelector.RefreshOpenConflictDetailsWindows();
                        }
                    }
                }

                EditorGUILayout.Space(4);
                if (GUILayout.Button("Animation Clip Selector を開く", GUILayout.Height(22)))
                    AnimationClipSelector.Open();
            });
        }

        static AnimationClipSelectorSettings LoadOrCreateSettings()
        {
            var path = AnimationClipSelector.SettingsAssetPath;
            var settings = AssetDatabase.LoadAssetAtPath<AnimationClipSelectorSettings>(path);
            if (settings != null) return settings;

            var dir = Path.GetDirectoryName(path)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder("Assets/SamiVRCBlocksAvatar"))
                AssetDatabase.CreateFolder("Assets", "SamiVRCBlocksAvatar");
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder("Assets/SamiVRCBlocksAvatar/Editor"))
                AssetDatabase.CreateFolder("Assets/SamiVRCBlocksAvatar", "Editor");

            settings = ScriptableObject.CreateInstance<AnimationClipSelectorSettings>();
            AssetDatabase.CreateAsset(settings, path);
            AssetDatabase.SaveAssets();
            return settings;
        }

        static string FormatIgnoredConflict(IgnoredConflictEntry entry)
        {
            var controllerName = string.IsNullOrEmpty(entry.controllerPath)
                ? "(Controller 不明)"
                : Path.GetFileNameWithoutExtension(entry.controllerPath);
            var target = string.IsNullOrEmpty(entry.path) ? "(ルート)" : entry.path;
            if (!string.IsNullOrEmpty(entry.propertyName))
                target += "." + entry.propertyName;
            if (!string.IsNullOrEmpty(entry.typeName))
                target += " [" + entry.typeName + "]";
            return controllerName + ": " + target;
        }
    }
}
#endif
