using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    public static class VRCAvatarParamSetterCore
    {
        public static bool HasParameter(AnimatorController controller, string parameterName)
        {
            var parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == parameterName)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Animator ウィンドウで現在開いている Animator Controller を取得する。
        /// </summary>
        public static AnimatorController GetEditingAnimatorWindowController()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var typeNames = new[]
            {
                "UnityEditor.Graphs.AnimatorControllerTool, UnityEditor.Graphs",
                "UnityEditor.AnimatorControllerWindow, UnityEditor",
                "UnityEditor.AnimatorWindow, UnityEditor"
            };

            foreach (var n in typeNames)
            {
                var t = System.Type.GetType(n);
                if (t == null) continue;
                var windows = Resources.FindObjectsOfTypeAll(t);
                for (var i = 0; i < windows.Length; i++)
                {
                    var w = windows[i];
                    if (w == null) continue;
                    var wt = w.GetType();

                    var p = wt.GetProperty("animatorController", flags) ?? wt.GetProperty("m_AnimatorController", flags);
                    if (p != null)
                    {
                        var v = p.GetValue(w) as AnimatorController;
                        if (v != null) return v;
                    }

                    var f = wt.GetField("animatorController", flags) ?? wt.GetField("m_AnimatorController", flags);
                    if (f != null)
                    {
                        var v = f.GetValue(w) as AnimatorController;
                        if (v != null) return v;
                    }
                }
            }

            return null;
        }
    }
}
