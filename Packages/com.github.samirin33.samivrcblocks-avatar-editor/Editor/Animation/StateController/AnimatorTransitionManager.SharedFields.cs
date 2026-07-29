using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditorInternal;
using UnityEngine;

namespace Samirin33.AvatarEditor.Tools.Editor
{
    public sealed partial class AnimatorTransitionManager : EditorWindow
    {
        // 共通: TextField + 候補選択のハイブリッド入力
        private static string DrawTextOrSelectField(string label, string current, IReadOnlyList<string> options)
        {
            current ??= string.Empty;
            using (new EditorGUILayout.HorizontalScope())
            {
                var nextText = EditorGUILayout.TextField(label, current);
                var popupChosen = DrawSelectPopupOnly(options, nextText, GUILayout.Width(120f));
                return popupChosen ?? nextText;
            }
        }

        private static string DrawTextOrSelectFieldInlineRect(Rect rect, string current, IReadOnlyList<string> options)
        {
            current ??= string.Empty;
            var popupWidth = 98f;
            var textRect = new Rect(rect.x, rect.y, Mathf.Max(10f, rect.width - popupWidth - 4f), rect.height);
            var popupRect = new Rect(textRect.xMax + 4f, rect.y, popupWidth, rect.height);
            var nextText = EditorGUI.TextField(textRect, current);
            var popupChosen = DrawSelectPopupOnlyRect(popupRect, options, nextText);
            return popupChosen ?? nextText;
        }

        private static string DrawSelectPopupOnly(IReadOnlyList<string> options, string current, params GUILayoutOption[] layout)
        {
            if (options == null || options.Count == 0)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.Popup(0, new[] { "(候補なし)" }, layout);
                EditorGUI.EndDisabledGroup();
                return null;
            }

            var values = new List<string>();
            current ??= string.Empty;
            for (var i = 0; i < options.Count; i++)
            {
                var n = options[i] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(n))
                    continue;
                values.Add(n);
            }

            var hasCurrent = !string.IsNullOrEmpty(current);
            var display = new List<string> { hasCurrent ? GetNestedLastSegment(current) : "(選択)" };
            display.AddRange(values); // メニューはフルネスト名

            var selectedIndex = hasCurrent ? 0 : 0; // ボタン表示は常に index=0
            var next = EditorGUILayout.Popup(selectedIndex, display.ToArray(), layout);
            if (next <= 0)
                return null;
            return values[next - 1];
        }

        private static string DrawSelectPopupOnlyRect(Rect rect, IReadOnlyList<string> options, string current)
        {
            if (options == null || options.Count == 0)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUI.Popup(rect, 0, new[] { "(候補なし)" });
                EditorGUI.EndDisabledGroup();
                return null;
            }

            var values = new List<string>();
            current ??= string.Empty;
            for (var i = 0; i < options.Count; i++)
            {
                var n = options[i] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(n))
                    continue;
                values.Add(n);
            }

            var hasCurrent = !string.IsNullOrEmpty(current);
            var display = new List<string> { hasCurrent ? GetNestedLastSegment(current) : "(選択)" };
            display.AddRange(values); // メニューはフルネスト名

            var selectedIndex = hasCurrent ? 0 : 0; // ボタン表示は常に index=0
            var next = EditorGUI.Popup(rect, selectedIndex, display.ToArray());
            if (next <= 0)
                return null;
            return values[next - 1];
        }

        private static string GetNestedLastSegment(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s ?? string.Empty;
            var i = s.LastIndexOf('/');
            if (i < 0 || i >= s.Length - 1)
                return s;
            return s.Substring(i + 1);
        }
    }
}
