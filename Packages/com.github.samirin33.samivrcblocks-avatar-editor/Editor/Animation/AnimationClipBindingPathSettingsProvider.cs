using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Samirin33.AvatarEditor.Animation.Editor;
using Samirin33.Editor;

namespace Samirin33.SamirinVRCUtility.AvatarEditor
{
    /// <summary>
    /// ヒエラルキー変更・リネームに合わせた AnimationClip バインドパス自動修正の Preferences。
    /// </summary>
    static class AnimationClipBindingPathSettingsProvider
    {
        const string SettingsPath = "Preferences/Samirin Editor Tools/Animation Binding Path";

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider(SettingsPath, SettingsScope.User)
            {
                label = "Animation Binding Path",
                keywords = new HashSet<string>(new[]
                {
                    "Animation", "Clip", "Binding", "Path", "Hierarchy", "Rename",
                    "パス", "自動修正", "ヒエラルキー", "リネーム", "監視"
                }),
                guiHandler = OnGUI
            };
        }

        static void OnGUI(string searchContext)
        {
            SamirinEditorStyleHelper.DrawWithBlueBackgroundForSettingsGui(() =>
            {
                EditorGUILayout.Space(4);
                var helpRect = EditorGUILayout.GetControlRect(false, 64);
                EditorGUI.HelpBox(helpRect,
                    "ヒエラルキーの変更やオブジェクトのリネームに合わせて、AnimationClip のバインドパスを自動修正するかどうかを設定します。\n" +
                    "Clip Binding Path Replace ウィンドウの「ヒエラルキーを監視」と同じ設定です。",
                    MessageType.Info);
                EditorGUILayout.Space(4);

                bool enabled = AnimationClipBindingPathUtility.HierarchyWatchEnabled;
                bool newEnabled = EditorGUILayout.ToggleLeft(
                    "ヒエラルキー変更・リネーム時にアニメーションパスを自動修正する",
                    enabled);
                if (newEnabled != enabled)
                    AnimationClipBindingPathUtility.HierarchyWatchEnabled = newEnabled;

                EditorGUILayout.Space(4);
            });
        }
    }
}
