using UnityEditor;
using UnityEngine;

namespace Samirin33.SamirinVRCUtility.AvatarEditor
{
    /// <summary>
    /// Replace Constraint ツールの設定（EditorPrefs）
    /// </summary>
    public static class ReplaceConstraintPreferences
    {
        const string PrefsKeyAutoFixAnimatorPath = "SamirinEditorTools.ReplaceConstraint.AutoFixAnimatorPath";
        const bool DefaultAutoFixAnimatorPath = true;

        /// <summary>
        /// Constraint置換時に、親階層のAnimatorに含まれるAnimationClipのバインディングを自動で置換先にリバインドするか。
        /// </summary>
        public static bool AutoFixAnimatorPath
        {
            get => EditorPrefs.GetBool(PrefsKeyAutoFixAnimatorPath, DefaultAutoFixAnimatorPath);
            set => EditorPrefs.SetBool(PrefsKeyAutoFixAnimatorPath, value);
        }
    }
}
