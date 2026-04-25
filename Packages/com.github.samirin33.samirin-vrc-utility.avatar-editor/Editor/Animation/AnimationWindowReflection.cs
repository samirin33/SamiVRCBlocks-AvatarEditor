#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    /// <summary>
    /// UnityのAnimationWindow内部APIへリフレクションでアクセスするヘルパー。
    /// </summary>
    internal static class AnimationWindowReflection
    {
        private static readonly Assembly UnityEditorAssembly = typeof(EditorWindow).Assembly;

        public static EditorWindow GetAnimationWindow()
        {
            var windowType = UnityEditorAssembly.GetType("UnityEditor.AnimationWindow");
            if (windowType == null) return null;

            var windows = (EditorWindow[])Resources.FindObjectsOfTypeAll(windowType);
            return windows.Length > 0 ? windows[0] : null;
        }

        public static bool TryGetAnimationWindowState(out object state)
        {
            state = null;
            var window = GetAnimationWindow();
            if (window == null) return false;

            var animEditorField = window.GetType().GetField("m_AnimEditor", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var animEditor = animEditorField?.GetValue(window);
            if (animEditor == null) return false;

            var stateField = animEditor.GetType().GetField("m_State", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            state = stateField?.GetValue(animEditor);
            return state != null;
        }

        public static AnimationClip GetActiveAnimationClip(object state)
        {
            if (state == null) return null;
            var prop = state.GetType().GetProperty("activeAnimationClip", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return prop?.GetValue(state) as AnimationClip;
        }

        public static bool TryGetStateTime(object state, out float time)
        {
            time = 0f;
            if (state == null) return false;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = state.GetType();
            foreach (var name in new[] { "time", "currentTime" })
            {
                var prop = t.GetProperty(name, flags);
                if (prop == null) continue;
                if (prop.PropertyType == typeof(float))
                {
                    try
                    {
                        time = (float)prop.GetValue(state);
                        return true;
                    }
                    catch
                    {
                        // ignore
                    }
                }
                if (prop.PropertyType == typeof(double))
                {
                    try
                    {
                        time = (float)(double)prop.GetValue(state);
                        return true;
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            return false;
        }

        public static IEnumerable GetActiveCurves(object state)
        {
            if (state == null) return null;
            var prop = state.GetType().GetProperty("activeCurves", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return prop?.GetValue(state) as IEnumerable;
        }

        public static string GetCurvePath(object curve)
        {
            if (curve == null) return null;
            var prop = curve.GetType().GetProperty("path", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return prop?.GetValue(curve) as string;
        }

        public static string GetCurvePropertyName(object curve)
        {
            if (curve == null) return null;
            var prop = curve.GetType().GetProperty("propertyName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return prop?.GetValue(curve) as string;
        }

        public static IEnumerable GetCurveKeyframes(object curve)
        {
            if (curve == null) return null;
            var field = curve.GetType().GetField("m_Keyframes", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return field?.GetValue(curve) as IEnumerable;
        }

        public static void SetCurveKeyframesOrder(object curve, IList keyframeObjects)
        {
            if (curve == null || keyframeObjects == null) return;

            var field = curve.GetType().GetField("m_Keyframes", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var keyframeList = field?.GetValue(curve);
            if (keyframeList == null) return;

            var clearMethod = keyframeList.GetType().GetMethod("Clear");
            var addMethod = keyframeList.GetType().GetMethod("Add");
            if (clearMethod == null || addMethod == null) return;

            clearMethod.Invoke(keyframeList, null);
            foreach (var kf in keyframeObjects)
            {
                addMethod.Invoke(keyframeList, new[] { kf });
            }
        }

        public static float GetKeyframeTime(object keyframe)
        {
            if (keyframe == null) return 0;
            var prop = keyframe.GetType().GetProperty("time", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return prop != null ? (float)prop.GetValue(keyframe) : 0;
        }

        public static float GetKeyframeValue(object keyframe)
        {
            if (keyframe == null) return 0;
            var prop = keyframe.GetType().GetProperty("value", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return prop != null ? (float)prop.GetValue(keyframe) : 0;
        }

        public static void SetKeyframeValue(object keyframe, float value)
        {
            if (keyframe == null) return;
            var prop = keyframe.GetType().GetProperty("value", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            prop?.SetValue(keyframe, value);
        }

        public static float GetKeyframeInTangent(object keyframe)
        {
            if (keyframe == null) return 0;
            var prop = keyframe.GetType().GetProperty("inTangent", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return prop != null ? (float)prop.GetValue(keyframe) : 0;
        }

        public static void SetKeyframeInTangent(object keyframe, float value)
        {
            if (keyframe == null) return;
            var prop = keyframe.GetType().GetProperty("inTangent", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            prop?.SetValue(keyframe, value);
        }

        public static float GetKeyframeOutTangent(object keyframe)
        {
            if (keyframe == null) return 0;
            var prop = keyframe.GetType().GetProperty("outTangent", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return prop != null ? (float)prop.GetValue(keyframe) : 0;
        }

        public static void SetKeyframeOutTangent(object keyframe, float value)
        {
            if (keyframe == null) return;
            var prop = keyframe.GetType().GetProperty("outTangent", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            prop?.SetValue(keyframe, value);
        }

        public static float GetKeyframeInWeight(object keyframe)
        {
            if (keyframe == null) return 0;
            var prop = keyframe.GetType().GetProperty("inWeight", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return prop != null ? (float)prop.GetValue(keyframe) : 0;
        }

        public static void SetKeyframeInWeight(object keyframe, float value)
        {
            if (keyframe == null) return;
            var prop = keyframe.GetType().GetProperty("inWeight", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            prop?.SetValue(keyframe, value);
        }

        public static float GetKeyframeOutWeight(object keyframe)
        {
            if (keyframe == null) return 0;
            var prop = keyframe.GetType().GetProperty("outWeight", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return prop != null ? (float)prop.GetValue(keyframe) : 0;
        }

        public static void SetKeyframeOutWeight(object keyframe, float value)
        {
            if (keyframe == null) return;
            var prop = keyframe.GetType().GetProperty("outWeight", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            prop?.SetValue(keyframe, value);
        }

        public static void SetKeyframeWeightedMode(object keyframe, WeightedMode mode)
        {
            if (keyframe == null) return;
            var prop = keyframe.GetType().GetProperty("weightedMode", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            prop?.SetValue(keyframe, mode);
        }

        public static void SaveKeySelection(object state, string undoLabel)
        {
            if (state == null) return;
            var method = state.GetType().GetMethod("SaveKeySelection", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            method?.Invoke(state, new object[] { undoLabel });
        }

        public static void SaveCurve(object state, AnimationClip clip, object curve, string undoLabel)
        {
            if (state == null || clip == null || curve == null) return;
            var methods = state.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            foreach (var m in methods)
            {
                if (m.Name == "SaveCurve" && m.GetParameters().Length == 3)
                {
                    m.Invoke(state, new object[] { clip, curve, undoLabel });
                    return;
                }
            }
        }

        public static void ResampleAnimation(object state)
        {
            if (state == null) return;
            var method = state.GetType().GetMethod("ResampleAnimation", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            method?.Invoke(state, Array.Empty<object>());
        }

        public static void ClearSelectedKeysCache(object state)
        {
            if (state == null) return;
            var field = state.GetType().GetField("m_SelectedKeysCache", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            field?.SetValue(state, null);
        }
    }
}
#endif
