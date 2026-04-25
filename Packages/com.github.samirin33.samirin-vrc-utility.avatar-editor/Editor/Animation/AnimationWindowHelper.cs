#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    /// <summary>
    /// Animationウィンドウの取得・編集対象の切り替え・カーブ選択を共通で行うヘルパー。
    /// AnimationClipSelector と AnimationClipVariant の両方から利用する。
    /// </summary>
    public static class AnimationWindowHelper
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static EditorWindow GetAnimationWindow()
        {
            return AnimationWindowReflection.GetAnimationWindow();
        }

        /// <summary>Animationウィンドウの状態を取得（編集中のルートとクリップ）</summary>
        public static bool TryGetAnimationWindowState(out GameObject activeRoot, out AnimationClip activeAnimationClip)
        {
            activeRoot = null;
            activeAnimationClip = null;
            var window = GetAnimationWindow();
            if (window == null) return false;
            if (!window.hasFocus) return false;

            if (!AnimationWindowReflection.TryGetAnimationWindowState(out var state) || state == null)
                return false;

            var stateType = state.GetType();
            var rootProp = stateType.GetProperty("activeRootGameObject", Flags);
            var clipProp = stateType.GetProperty("activeAnimationClip", Flags);
            activeRoot = rootProp?.GetValue(state) as GameObject;
            activeAnimationClip = clipProp?.GetValue(state) as AnimationClip;
            return true;
        }

        /// <summary>
        /// Animation ウィンドウのフォーカス外でも、現在のルート・クリップ・再生ヘッドの時刻（取得できた場合）を得る。クリップが取れない場合は false。
        /// </summary>
        public static bool TryGetAnimationWindowStateUnfocused(out GameObject activeRoot, out AnimationClip activeAnimationClip, out float time)
        {
            activeRoot = null;
            activeAnimationClip = null;
            time = 0f;
            var window = GetAnimationWindow();
            if (window == null) return false;

            if (!AnimationWindowReflection.TryGetAnimationWindowState(out var state) || state == null)
                return false;

            var stateType = state.GetType();
            var rootProp = stateType.GetProperty("activeRootGameObject", Flags);
            var clipProp = stateType.GetProperty("activeAnimationClip", Flags);
            activeRoot = rootProp?.GetValue(state) as GameObject;
            activeAnimationClip = clipProp?.GetValue(state) as AnimationClip;
            if (activeAnimationClip == null) return false;
            if (!AnimationWindowReflection.TryGetStateTime(state, out time)) time = 0f;
            return true;
        }

        /// <summary>編集対象クリップを設定（previewing/recording/playing を維持）</summary>
        public static void SetAnimationWindowClip(AnimationClip clip)
        {
            if (clip == null) return;
            var window = GetAnimationWindow();
            if (window == null) return;
            var t = window.GetType();

            var wasPreviewing = GetAnimationWindowBool(window, t, "previewing");
            var wasRecording = GetAnimationWindowBool(window, t, "recording");
            var wasPlaying = GetAnimationWindowBool(window, t, "playing");

            var clipProp = t.GetProperty("animationClip", Flags);
            clipProp?.SetValue(window, clip);

            EditorApplication.delayCall += () =>
            {
                var w = GetAnimationWindow();
                if (w == null) return;
                var wt = w.GetType();
                if (wasPreviewing) SetAnimationWindowBool(w, wt, "previewing", true);
                if (wasRecording) SetAnimationWindowBool(w, wt, "recording", true);
                if (wasPlaying) SetAnimationWindowBool(w, wt, "playing", true);
            };
        }

        private static bool GetAnimationWindowBool(EditorWindow window, Type t, string propName)
        {
            var prop = t.GetProperty(propName, Flags);
            if (prop != null && prop.PropertyType == typeof(bool))
            {
                try { return (bool)prop.GetValue(window); }
                catch { }
            }
            var field = t.GetField(propName, Flags) ?? t.GetField("m_" + char.ToUpperInvariant(propName[0]) + propName.Substring(1), Flags);
            if (field != null && field.FieldType == typeof(bool))
            {
                try { return (bool)field.GetValue(window); }
                catch { }
            }
            return false;
        }

        private static void SetAnimationWindowBool(EditorWindow window, Type t, string propName, bool value)
        {
            var prop = t.GetProperty(propName, Flags);
            if (prop != null && prop.PropertyType == typeof(bool) && prop.CanWrite)
            {
                try { prop.SetValue(window, value); return; }
                catch { }
            }
            var field = t.GetField(propName, Flags) ?? t.GetField("m_" + char.ToUpperInvariant(propName[0]) + propName.Substring(1), Flags);
            if (field != null && field.FieldType == typeof(bool))
            {
                try { field.SetValue(window, value); }
                catch { }
            }
        }

        /// <summary>Animationウィンドウが閉じていれば開き、指定クリップを編集対象にする</summary>
        public static void EnsureAnimationWindowOpenAndSetClip(AnimationClip clip)
        {
            if (clip == null) return;
            var window = GetAnimationWindow();
            if (window == null)
            {
                EditorApplication.ExecuteMenuItem("Window/Animation/Animation");
                EditorApplication.delayCall += () => SetAnimationWindowClip(clip);
            }
            else
            {
                if (!window.hasFocus)
                    window.ShowTab();
                SetAnimationWindowClip(clip);
            }
        }

        /// <summary>Animationウィンドウの編集対象を指定クリップに切り替える（他ウィンドウから呼び出す用）</summary>
        public static void SetAnimationWindowToClip(AnimationClip clip)
        {
            if (clip == null) return;
            EnsureAnimationWindowOpenAndSetClip(clip);
        }

        /// <summary>指定クリップを表示し、必要ならウィンドウを開く（Variant用：Pingのみで選択は変えない）</summary>
        public static void OpenAnimationWindowWithClip(AnimationClip clip)
        {
            if (clip == null) return;
            var window = GetAnimationWindow();
            if (window == null)
            {
                EditorApplication.ExecuteMenuItem("Window/Animation/Animation");
                EditorApplication.delayCall += () =>
                {
                    SetAnimationWindowClip(clip);
                    EditorGUIUtility.PingObject(clip);
                    var w = GetAnimationWindow();
                    if (w != null) w.Repaint();
                };
            }
            else
            {
                if (!window.hasFocus)
                    window.ShowTab();
                EditorApplication.delayCall += () =>
                {
                    SetAnimationWindowClip(clip);
                    EditorGUIUtility.PingObject(clip);
                    var w = GetAnimationWindow();
                    if (w != null) w.Repaint();
                };
            }
        }

        /// <summary>Animationウィンドウで該当バインディングのカーブを選択する（可能な場合）</summary>
        public static void TrySelectCurveInAnimationWindow(AnimationClip clip, EditorCurveBinding binding)
        {
            if (clip == null || !AnimationWindowReflection.TryGetAnimationWindowState(out var state)) return;
            if (AnimationWindowReflection.GetActiveAnimationClip(state) != clip) return;
            var curves = AnimationWindowReflection.GetActiveCurves(state);
            if (curves == null) return;
            var list = new List<object>();
            foreach (var c in curves) list.Add(c);
            object match = null;
            foreach (var curve in list)
            {
                var path = AnimationWindowReflection.GetCurvePath(curve);
                var prop = AnimationWindowReflection.GetCurvePropertyName(curve);
                if (path == binding.path && prop == binding.propertyName) { match = curve; break; }
            }
            if (match == null) return;
            var stateType = state.GetType();
            var setProp = stateType.GetProperty("activeCurves", Flags);
            if (setProp != null && setProp.CanWrite)
            {
                try
                {
                    var single = Activator.CreateInstance(setProp.PropertyType.GenericTypeArguments?.Length > 0 ? setProp.PropertyType : typeof(List<object>));
                    if (single is System.Collections.IList singleList) { singleList.Add(match); setProp.SetValue(state, single); }
                }
                catch { }
            }
        }
    }
}
#endif
