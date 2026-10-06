using System;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SamiVRCBlocksAvatar.Editor
{
    /// <summary>
    /// VRChat は &lt;size=N%&gt; をメニュー文字の N% として描きます。
    /// Gesture Manager と MA Menu Item のプレビューは同じタグを過大に描くため、
    /// 保存文字列は変えずに表示だけ合わせます。
    /// </summary>
    [InitializeOnLoad]
    public static class MenuRichTextPreviewSizeFix
    {
        const string HarmonyId = "com.github.samirin33.samivrcblocks-avatar-editor.menu-richtext-size";
        const float GestureManagerFallbackFont = 12f;
        const int InstallRetryLimit = 8;

        static readonly Regex SizePercentTag = new Regex(
            @"<size\s*=\s*([-+]?\d+(?:\.\d+)?)\s*%>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        static int _retries;
        static bool _installed;
        static bool _watching;

        static MenuRichTextPreviewSizeFix()
        {
            EditorApplication.delayCall += TryInstall;
        }

        static void TryInstall()
        {
            if (_installed)
                return;

            var harmonyType = MenuRichTextUtility.FindType("HarmonyLib.Harmony");
            if (harmonyType == null)
            {
                Retry();
                return;
            }

            try
            {
                Install(harmonyType);
                _installed = true;
            }
            catch (Exception)
            {
                EnsureGestureManagerWatch();
                Retry();
            }
        }

        static void Retry()
        {
            if (++_retries > InstallRetryLimit)
            {
                EnsureGestureManagerWatch();
                return;
            }

            EditorApplication.delayCall += TryInstall;
        }

        static void Install(Type harmonyType)
        {
            var harmony = Activator.CreateInstance(harmonyType, HarmonyId);
            var patch = FindPatchMethod(harmonyType);
            var harmonyMethodCtor = FindHarmonyMethodCtor(harmonyType);
            if (patch == null || harmonyMethodCtor == null)
                throw new MissingMethodException("Harmony.Patch");

            TryPatch(
                harmony,
                patch,
                harmonyMethodCtor,
                FindLabelField(),
                typeof(MenuRichTextPreviewSizeFix).GetMethod(nameof(RewriteImguiPercentSize), BindingFlags.Public | BindingFlags.Static),
                null);

            var richText = MenuRichTextUtility.FindType(
                "BlackStartX.GestureManager.Library.VisualElements.GmgTmpRichTextElement");
            if (richText == null)
                return;

            var parsed = TryPatch(
                harmony,
                patch,
                harmonyMethodCtor,
                FindTmpNumberAttribute(richText),
                null,
                typeof(MenuRichTextPreviewSizeFix).GetMethod(nameof(FixGestureManagerPercent), BindingFlags.Public | BindingFlags.Static));
            if (!parsed)
                EnsureGestureManagerWatch();
        }

        static bool TryPatch(
            object harmony,
            MethodInfo patch,
            ConstructorInfo harmonyMethodCtor,
            MethodBase original,
            MethodInfo prefix,
            MethodInfo postfix)
        {
            if (original == null || (prefix == null && postfix == null))
                return false;

            try
            {
                var pre = prefix == null ? null : harmonyMethodCtor.Invoke(new object[] { prefix });
                var post = postfix == null ? null : harmonyMethodCtor.Invoke(new object[] { postfix });
                patch.Invoke(harmony, new[] { original, pre, post, null, null });
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static MethodInfo FindPatchMethod(Type harmonyType)
        {
            foreach (var method in harmonyType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.Name != "Patch")
                    continue;
                var parameters = method.GetParameters();
                if (parameters.Length == 5
                    && parameters[0].ParameterType == typeof(MethodBase)
                    && parameters[1].ParameterType.Name == "HarmonyMethod")
                    return method;
            }

            return null;
        }

        static ConstructorInfo FindHarmonyMethodCtor(Type harmonyType)
        {
            var harmonyMethod = harmonyType.Assembly.GetType("HarmonyLib.HarmonyMethod");
            if (harmonyMethod == null)
                return null;

            foreach (var ctor in harmonyMethod.GetConstructors())
            {
                var parameters = ctor.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(MethodInfo))
                    return ctor;
            }

            return null;
        }

        static MethodInfo FindLabelField()
        {
            foreach (var method in typeof(EditorGUILayout).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != "LabelField")
                    continue;
                var parameters = method.GetParameters();
                if (parameters.Length == 4
                    && parameters[0].ParameterType == typeof(string)
                    && parameters[1].ParameterType == typeof(string)
                    && parameters[2].ParameterType == typeof(GUIStyle)
                    && parameters[3].ParameterType == typeof(GUILayoutOption[]))
                    return method;
            }

            return null;
        }

        static MethodInfo FindTmpNumberAttribute(Type richTextElement)
        {
            var data = richTextElement.GetNestedType("Data", BindingFlags.Public | BindingFlags.NonPublic);
            if (data == null)
                return null;

            foreach (var method in data.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (method.Name == "TmpNumberAttribute")
                    return method;
            }

            return null;
        }

        /// <summary>
        /// IMGUI は &lt;size=80%&gt; を 80px として読む。描画引数だけを基準フォントの割合へ換算する。
        /// </summary>
        public static void RewriteImguiPercentSize(ref string label2, GUIStyle style)
        {
            if (string.IsNullOrEmpty(label2) || label2.IndexOf('%') < 0)
                return;
            if (label2.IndexOf("<size", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            try
            {
                var baseFont = style != null && style.fontSize > 0 ? style.fontSize : 12;
                label2 = SizePercentTag.Replace(label2, match =>
                {
                    if (!float.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
                        return match.Value;
                    var px = Mathf.Max(1, Mathf.RoundToInt(baseFont * percent / 100f));
                    return "<size=" + px.ToString(CultureInfo.InvariantCulture) + ">";
                });
            }
            catch (Exception)
            {
                // プレビュー変換の失敗でインスペクタを止めない
            }
        }

        /// <summary>
        /// Gesture Manager が UI Toolkit の % 指定にしたサイズを、要素のフォントサイズに対する割合へ戻す。
        /// </summary>
        public static void FixGestureManagerPercent(object element, ref StyleLength size)
        {
            var length = size.value;
            if (length.unit != LengthUnit.Percent)
                return;

            var font = GestureManagerFallbackFont;
            if (element is VisualElement visual)
            {
                var own = visual.style.fontSize.value;
                if (own.unit == LengthUnit.Pixel && own.value > 0.01f)
                    font = own.value;
            }

            size = new StyleLength(font * length.value / 100f);
        }

        static void EnsureGestureManagerWatch()
        {
            if (MenuRichTextUtility.FindType(
                    "BlackStartX.GestureManager.Library.VisualElements.GmgTmpRichTextElement") == null)
                return;
            EnsureWatch();
        }

        static void EnsureWatch()
        {
            if (_watching)
                return;
            _watching = true;
            EditorApplication.update += WatchGestureManagerFonts;
        }

        static void WatchGestureManagerFonts()
        {
            if (!EditorApplication.isPlaying)
                return;

            try
            {
                var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
                for (var i = 0; i < windows.Length; i++)
                {
                    var window = windows[i];
                    if (window == null)
                        continue;
                    var name = window.GetType().Name;
                    if (name.IndexOf("Inspector", StringComparison.Ordinal) < 0
                        && name.IndexOf("Gesture", StringComparison.Ordinal) < 0
                        && name.IndexOf("Floating", StringComparison.Ordinal) < 0)
                        continue;
                    var root = window.rootVisualElement;
                    if (root != null)
                        FixTree(root);
                }
            }
            catch (Exception)
            {
                // プレビュー補正でエディタ更新を止めない
            }
        }

        static void FixTree(VisualElement element)
        {
            if (element.GetType().Name == "GmgTmpRichTextElement")
                FixRichTextChildren(element, BaseFontOf(element));

            var count = element.childCount;
            for (var i = 0; i < count; i++)
                FixTree(element[i]);
        }

        static void FixRichTextChildren(VisualElement element, float baseFont)
        {
            var count = element.childCount;
            for (var i = 0; i < count; i++)
            {
                var child = element[i];
                var font = child.style.fontSize.value;
                if (font.unit == LengthUnit.Percent)
                    child.style.fontSize = baseFont * font.value / 100f;
                FixRichTextChildren(child, baseFont);
            }
        }

        static float BaseFontOf(VisualElement element)
        {
            var length = element.style.fontSize.value;
            if (length.unit == LengthUnit.Pixel && length.value > 0.01f)
                return length.value;
            if (element.resolvedStyle.fontSize > 0.01f)
                return element.resolvedStyle.fontSize;
            return GestureManagerFallbackFont;
        }
    }
}
