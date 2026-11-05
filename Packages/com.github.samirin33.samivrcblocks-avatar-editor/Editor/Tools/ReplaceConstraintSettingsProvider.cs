using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Samirin33.Editor;

namespace Samirin33.SamirinVRCUtility.AvatarEditor
{
    static class ReplaceConstraintSettingsProvider
    {
        const string SettingsPath = "Preferences/Samirin Editor Tools/Replace Constraint";

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider(SettingsPath, SettingsScope.User)
            {
                label = "Replace Constraint",
                keywords = new HashSet<string>(new[] { "Replace", "Constraint", "Animator", "Animation", "VRChat" }),
                guiHandler = OnGUI
            };
        }

        static void OnGUI(string searchContext)
        {
            SamirinEditorStyleHelper.DrawWithBlueBackgroundForSettingsGui(() =>
            {
                EditorGUILayout.Space(4);
                var helpRect = EditorGUILayout.GetControlRect(false, 52);
                EditorGUI.HelpBox(helpRect,
                    "Constraint置換時に、親階層のAnimatorに含まれるAnimationClipのバインディングを自動で置換先にリバインドするかどうかを設定します。",
                    MessageType.Info);
                EditorGUILayout.Space(4);

                bool autoFix = ReplaceConstraintPreferences.AutoFixAnimatorPath;
                bool newAutoFix = EditorGUILayout.ToggleLeft(
                    "Animatorのパスを自動修正する（置換時にAnimationClipのバインディングをリバインド）",
                    autoFix);
                if (newAutoFix != autoFix)
                    ReplaceConstraintPreferences.AutoFixAnimatorPath = newAutoFix;

                EditorGUILayout.Space(4);
            });
        }
    }
}
