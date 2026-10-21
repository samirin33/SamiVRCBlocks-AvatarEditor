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
                keywords = new HashSet<string>(new[]
                {
                    "Replace", "Constraint", "Animator", "Animation", "VRChat", "リバインド", "置換"
                }),
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
                    "Constraint置換時に、親階層のAnimatorに含まれるAnimationClipのバインディングを、置換先のConstraint型へ自動でリバインドするかどうかを設定します。",
                    MessageType.Info);
                EditorGUILayout.Space(4);

                bool autoFix = ReplaceConstraintPreferences.AutoFixAnimatorPath;
                bool newAutoFix = EditorGUILayout.ToggleLeft(
                    "Constraint置換時にAnimationClipのバインディングを置換先型へリバインドする",
                    autoFix);
                if (newAutoFix != autoFix)
                    ReplaceConstraintPreferences.AutoFixAnimatorPath = newAutoFix;

                EditorGUILayout.Space(4);
            });
        }
    }
}
