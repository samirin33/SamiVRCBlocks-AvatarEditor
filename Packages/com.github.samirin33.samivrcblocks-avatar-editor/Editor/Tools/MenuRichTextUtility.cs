using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SamiVRCBlocksAvatar.Editor
{
    /// <summary>
    /// MA Menu Item / VRC Expressions Menu の表示名ターゲットと、TMPro リッチテキストの加工処理です。
    /// </summary>
    static class MenuRichTextUtility
    {
        public const int ControlTypeButton = 101;
        public const int ControlTypeToggle = 102;
        public const int ControlTypeSubMenu = 103;
        public const int ControlTypeTwoAxis = 201;
        public const int ControlTypeFourAxis = 202;
        public const int ControlTypeRadial = 203;

        static readonly Regex TagRegex = new Regex("<[^>]*>", RegexOptions.Compiled);
        static readonly string[] AxisNames = { "Up", "Right", "Down", "Left" };

        public enum TargetKind
        {
            MaItemName,
            MaItemLabel,
            VrcControlName,
            VrcControlLabel
        }

        public sealed class Target
        {
            public TargetKind Kind;
            public Object Owner;
            public string OwnerPath;
            public string KindLabel;
            public int ControlIndex = -1;
            public int LabelIndex = -1;
            public string PropertyPath;
            public string PlainPreview;
            public string RawText;

            public string ListLabel
            {
                get
                {
                    var preview = string.IsNullOrEmpty(PlainPreview) ? "(空)" : PlainPreview;
                    if (preview.Length > 24)
                        preview = preview.Substring(0, 24) + "…";
                    return $"{KindLabel}  {preview}";
                }
            }

            public bool IsValid => Owner != null;
        }

        public static Type FindType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return null;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = null;
                try
                {
                    type = assembly.GetType(fullName, false);
                }
                catch
                {
                    // ignored
                }

                if (type != null)
                    return type;
            }

            return null;
        }

        public static Type MaMenuItemType =>
            FindType("nadena.dev.modular_avatar.core.ModularAvatarMenuItem");

        public static Type VrcExpressionsMenuType =>
            FindType("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu");

        public static Type VrcAvatarDescriptorType =>
            FindType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");

        public static bool IsMaAvailable => MaMenuItemType != null;
        public static bool IsVrcMenuAvailable => VrcExpressionsMenuType != null;

        public static bool IsVrcExpressionsMenu(Object obj)
        {
            var type = VrcExpressionsMenuType;
            return obj != null && type != null && type.IsInstanceOfType(obj);
        }

        public static bool IsMaMenuItem(Object obj)
        {
            var type = MaMenuItemType;
            return obj != null && type != null && type.IsInstanceOfType(obj);
        }

        public static string ControlTypeName(int type)
        {
            switch (type)
            {
                case ControlTypeButton: return "Button";
                case ControlTypeToggle: return "Toggle";
                case ControlTypeSubMenu: return "SubMenu";
                case ControlTypeTwoAxis: return "TwoAxis";
                case ControlTypeFourAxis: return "FourAxis";
                case ControlTypeRadial: return "Radial";
                default: return type.ToString();
            }
        }

        public static string StripTags(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            // 旧「改行無視」で残った Word Joiner も除去
            return TagRegex.Replace(text, "").Replace("\u2060", "");
        }

        public static string StripTagsInRange(string text, int selA, int selB, out int newStart, out int newEnd)
        {
            text = text ?? "";
            GetSelectionRange(text, selA, selB, out var start, out var end);
            if (start >= end)
            {
                newStart = start;
                newEnd = end;
                return text;
            }

            var stripped = StripTags(text.Substring(start, end - start));
            var result = text.Substring(0, start) + stripped + text.Substring(end);
            newStart = start;
            newEnd = start + stripped.Length;
            return result;
        }

        public static string ToHtmlRgb(Color color)
        {
            return ColorUtility.ToHtmlStringRGB(color);
        }

        public static string ToHtmlRgba(Color color)
        {
            return ColorUtility.ToHtmlStringRGBA(color);
        }

        /// <summary>
        /// 不透明なら #RRGGBB、半透明なら #RRGGBBAA。
        /// </summary>
        public static string ToHtmlColor(Color color)
        {
            return color.a < 0.999f ? ToHtmlRgba(color) : ToHtmlRgb(color);
        }

        /// <summary>
        /// プレビュー用途の互換 API。実メニュー向けタグ書き換えには使わない。
        /// </summary>
        public static string EnsureMarkTagAlpha(string text, byte defaultAlpha = 0xAA)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? "";
            var alpha = defaultAlpha.ToString("X2");
            return System.Text.RegularExpressions.Regex.Replace(
                text,
                @"<mark=#([0-9A-Fa-f]{6})(?![0-9A-Fa-f])>",
                m => "<mark=#" + m.Groups[1].Value + alpha + ">",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        public static Color ParseColorOr(string html, Color fallback)
        {
            if (string.IsNullOrEmpty(html))
                return fallback;
            var s = html.Trim();
            if (!s.StartsWith("#"))
                s = "#" + s;
            return ColorUtility.TryParseHtmlString(s, out var c) ? c : fallback;
        }

        /// <summary>
        /// mark 用色のパース。#RRGGBB / #RRGGBBAA 両対応。
        /// </summary>
        public static Color ParseMarkColorOr(string html, Color fallback)
        {
            return ParseColorOr(html, fallback);
        }

        public static void GetSelectionRange(string text, int a, int b, out int start, out int end)
        {
            text = text ?? "";
            start = Mathf.Clamp(Mathf.Min(a, b), 0, text.Length);
            end = Mathf.Clamp(Mathf.Max(a, b), 0, text.Length);
        }

        public static bool HasSelection(string text, int selA, int selB)
        {
            GetSelectionRange(text, selA, selB, out var start, out var end);
            return start < end;
        }

        public static string GetSelectionPreview(string text, int selA, int selB, int maxChars = 32)
        {
            GetSelectionRange(text, selA, selB, out var start, out var end);
            if (start >= end)
                return "";
            var plain = StripTags(text.Substring(start, end - start));
            if (plain.Length > maxChars)
                return plain.Substring(0, maxChars) + "…";
            return plain;
        }

        public static string WrapOrInsert(string text, int selA, int selB, string open, string close, out int newStart, out int newEnd)
        {
            text = text ?? "";
            GetSelectionRange(text, selA, selB, out var start, out var end);

            if (TryUnwrap(text, start, end, open, close, out var unwrapped, out newStart, out newEnd))
            {
                SnapSelectionToContent(unwrapped, ref newStart, ref newEnd);
                return unwrapped;
            }

            if (start == end)
            {
                var inserted = text.Insert(start, open + close);
                newStart = start + open.Length;
                newEnd = newStart;
                return inserted;
            }

            var inner = text.Substring(start, end - start);
            var wrapped = text.Substring(0, start) + open + inner + close + text.Substring(end);
            newStart = start + open.Length;
            newEnd = newStart + inner.Length;
            SnapSelectionToContent(wrapped, ref newStart, ref newEnd);
            return wrapped;
        }

        public static string ReplaceOrWrapTag(string text, int selA, int selB, string tagName, string open, string close, out int newStart, out int newEnd)
        {
            text = text ?? "";
            GetSelectionRange(text, selA, selB, out var start, out var end);
            if (TryFindWrappingTag(text, start, end, tagName, out var openStart, out var openEnd, out var closeStart, out var closeEnd))
            {
                // 既存タグがあれば値を更新する（同じ値でも解除しない。解除は UnwrapTag 側）
                var inner = text.Substring(openEnd, closeStart - openEnd);
                var result = text.Substring(0, openStart) + open + inner + close + text.Substring(closeEnd);
                newStart = openStart + open.Length;
                newEnd = newStart + inner.Length;
                SnapSelectionToContent(result, ref newStart, ref newEnd);
                return result;
            }

            return WrapOrInsert(text, start, end, open, close, out newStart, out newEnd);
        }

        public static string UnwrapTag(string text, int selA, int selB, string tagName, out int newStart, out int newEnd)
        {
            text = text ?? "";
            GetSelectionRange(text, selA, selB, out var start, out var end);
            newStart = start;
            newEnd = end;
            if (!TryFindWrappingTag(text, start, end, tagName, out var openStart, out var openEnd, out var closeStart, out var closeEnd))
                return text;

            var inner = text.Substring(openEnd, closeStart - openEnd);
            var result = text.Substring(0, openStart) + inner + text.Substring(closeEnd);
            newStart = openStart;
            newEnd = openStart + inner.Length;
            SnapSelectionToContent(result, ref newStart, ref newEnd);
            return result;
        }

        /// <summary>
        /// 選択がタグ文字（特に閉じタグの &lt;）に食い込まないよう、内容側へスナップする。
        /// start/end はキャレット位置（end は排他的）。
        /// </summary>
        public static void SnapSelectionToContent(string text, ref int start, ref int end)
        {
            text = text ?? "";
            GetSelectionRange(text, start, end, out start, out end);
            if (start >= end)
                return;

            while (start < end && start < text.Length && text[start] == '<')
            {
                var gt = text.IndexOf('>', start);
                if (gt < 0 || gt + 1 > end)
                    break;
                start = gt + 1;
            }

            while (end > start)
            {
                // 閉じタグの '<' まで選ばれている（排他 end が '<' の次）
                if (text[end - 1] == '<')
                {
                    end--;
                    continue;
                }

                // 排他 end や直前文字がタグ内部
                if (end < text.Length && IsIndexStrictlyInsideTag(text, end))
                {
                    var lt = text.LastIndexOf('<', end);
                    if (lt >= start)
                    {
                        end = lt;
                        continue;
                    }
                }

                if (IsIndexStrictlyInsideTag(text, end - 1))
                {
                    var lt = text.LastIndexOf('<', end - 1);
                    if (lt >= start)
                    {
                        end = lt;
                        continue;
                    }

                    end--;
                    continue;
                }

                break;
            }
        }

        static bool IsIndexStrictlyInsideTag(string text, int index)
        {
            if (index < 0 || index >= text.Length)
                return false;
            var c = text[index];
            if (c == '<' || c == '>')
                return false;
            var lt = text.LastIndexOf('<', index);
            if (lt < 0)
                return false;
            var gt = text.IndexOf('>', lt);
            return gt > index;
        }

        public static string WrapColor(string text, int selA, int selB, Color color, out int newStart, out int newEnd)
        {
            var open = "<color=#" + ToHtmlColor(color) + ">";
            return ReplaceOrWrapTag(text, selA, selB, "color", open, "</color>", out newStart, out newEnd);
        }

        public static string WrapSizePercent(string text, int selA, int selB, int percent, out int newStart, out int newEnd)
        {
            percent = Mathf.Clamp(percent, 1, 300);
            return ReplaceOrWrapTag(text, selA, selB, "size", "<size=" + percent + "%>", "</size>", out newStart, out newEnd);
        }

        public static string WrapVOffset(string text, int selA, int selB, float em, out int newStart, out int newEnd)
        {
            var value = em.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            return ReplaceOrWrapTag(text, selA, selB, "voffset", "<voffset=" + value + "em>", "</voffset>", out newStart, out newEnd);
        }

        public static string WrapMark(string text, int selA, int selB, Color color, out int newStart, out int newEnd)
        {
            var open = "<mark=#" + ToHtmlColor(color) + ">";
            return ReplaceOrWrapTag(text, selA, selB, "mark", open, "</mark>", out newStart, out newEnd);
        }

        public struct SelectionFormat
        {
            public bool Bold;
            public bool Italic;
            public bool Underline;
            public bool Strikethrough;
            public bool Subscript;
            public bool Superscript;
            public bool SmallCaps;
            public bool Mark;
            public bool HasColor;
            public Color Color;
            public bool HasMarkColor;
            public Color MarkColor;
            public bool HasSize;
            public int SizePercent;
            public bool HasVOffset;
            public float VOffsetEm;
            public bool HasGradient;
            public Gradient Gradient;
        }

        /// <summary>
        /// 選択範囲を包むタグから、ツールバー表示用の書式状態を読み取る。
        /// </summary>
        public static SelectionFormat InspectSelection(string text, int selA, int selB)
        {
            var state = new SelectionFormat
            {
                Color = Color.white,
                MarkColor = new Color(1f, 0.878f, 0.4f, 1f),
                SizePercent = 100
            };
            text = text ?? "";
            GetSelectionRange(text, selA, selB, out var start, out var end);
            SnapSelectionToContent(text, ref start, ref end);
            if (start >= end)
                return state;

            state.Bold = IsWrappedBy(text, start, end, "b");
            state.Italic = IsWrappedBy(text, start, end, "i");
            state.Underline = IsWrappedBy(text, start, end, "u");
            state.Strikethrough = IsWrappedBy(text, start, end, "s");
            state.Subscript = IsWrappedBy(text, start, end, "sub");
            state.Superscript = IsWrappedBy(text, start, end, "sup");
            state.SmallCaps = IsWrappedBy(text, start, end, "smallcaps");
            state.Mark = IsWrappedBy(text, start, end, "mark");

            if (TryDetectPerCharColorGradient(text, start, end, out var gradColors, out _, out _))
            {
                state.HasGradient = true;
                state.Gradient = BuildGradientFromColors(gradColors);
            }
            else if (TryFindWrappingTag(text, start, end, "color", out var cOpenStart, out var cOpenEnd, out _, out _) &&
                     TryParseOpenTagValue(text, cOpenStart, cOpenEnd, out var colorRaw))
            {
                state.HasColor = true;
                state.Color = ParseColorOr(colorRaw.TrimStart('#'), Color.white);
            }

            if (TryFindWrappingTag(text, start, end, "mark", out var mOpenStart, out var mOpenEnd, out _, out _) &&
                TryParseOpenTagValue(text, mOpenStart, mOpenEnd, out var markRaw))
            {
                state.HasMarkColor = true;
                state.MarkColor = ParseMarkColorOr(markRaw.TrimStart('#'), state.MarkColor);
            }

            if (TryFindWrappingTag(text, start, end, "size", out var sOpenStart, out var sOpenEnd, out _, out _) &&
                TryParseOpenTagValue(text, sOpenStart, sOpenEnd, out var sizeRaw) &&
                TryParseSizePercent(sizeRaw, out var percent))
            {
                state.HasSize = true;
                state.SizePercent = percent;
            }

            if (TryFindWrappingTag(text, start, end, "voffset", out var vOpenStart, out var vOpenEnd, out _, out _) &&
                TryParseOpenTagValue(text, vOpenStart, vOpenEnd, out var vRaw) &&
                TryParseVOffsetEm(vRaw, out var em))
            {
                state.HasVOffset = true;
                state.VOffsetEm = em;
            }

            return state;
        }

        static bool IsWrappedBy(string text, int start, int end, string tagName)
        {
            return TryFindWrappingTag(text, start, end, tagName, out _, out _, out _, out _);
        }

        static bool TryParseOpenTagValue(string text, int openStart, int openEnd, out string value)
        {
            value = "";
            if (openStart < 0 || openEnd <= openStart + 1 || openEnd > text.Length)
                return false;
            // <tag=VALUE> / <tag=#RRGGBB> / <mark#RRGGBB>
            var inner = text.Substring(openStart + 1, openEnd - openStart - 2);
            var eq = inner.IndexOf('=');
            if (eq >= 0)
            {
                value = inner.Substring(eq + 1).Trim();
                return value.Length > 0;
            }

            var hash = inner.IndexOf('#');
            if (hash >= 0)
            {
                value = inner.Substring(hash).Trim();
                return value.Length > 0;
            }

            return false;
        }

        static bool TryParseSizePercent(string raw, out int percent)
        {
            percent = 100;
            raw = (raw ?? "").Trim();
            if (raw.EndsWith("%", StringComparison.Ordinal))
                raw = raw.Substring(0, raw.Length - 1).Trim();
            if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                return false;
            percent = Mathf.Clamp(Mathf.RoundToInt(v), 1, 300);
            return true;
        }

        static bool TryParseVOffsetEm(string raw, out float em)
        {
            em = 0f;
            raw = (raw ?? "").Trim();
            if (raw.EndsWith("em", StringComparison.OrdinalIgnoreCase))
                raw = raw.Substring(0, raw.Length - 2).Trim();
            return float.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out em);
        }

        public static string ApplyGradient(string text, int selA, int selB, Gradient gradient, out int newStart, out int newEnd)
        {
            text = text ?? "";
            GetSelectionRange(text, selA, selB, out var start, out var end);
            if (start == end)
            {
                start = 0;
                end = text.Length;
            }

            if (gradient == null)
            {
                newStart = start;
                newEnd = end;
                return text;
            }

            var inner = text.Substring(start, end - start);
            var colored = ColorizeVisibleCharacters(inner, t => gradient.Evaluate(t));
            var result = text.Substring(0, start) + colored + text.Substring(end);
            newStart = start;
            newEnd = start + colored.Length;
            // グラデ全体（連続する color タグ列）を選択対象にする
            if (TryDetectPerCharColorGradient(result, newStart, newEnd, out _, out var runStart, out var runEnd))
            {
                newStart = runStart;
                newEnd = runEnd;
            }

            return result;
        }

        public static string ApplyGradient(string text, int selA, int selB, Color from, Color to, out int newStart, out int newEnd)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return ApplyGradient(text, selA, selB, g, out newStart, out newEnd);
        }

        public static string ApplyRainbow(string text, int selA, int selB, out int newStart, out int newEnd)
        {
            text = text ?? "";
            GetSelectionRange(text, selA, selB, out var start, out var end);
            if (start == end)
            {
                start = 0;
                end = text.Length;
            }

            var inner = text.Substring(start, end - start);
            var colored = ColorizeVisibleCharacters(inner, t => Color.HSVToRGB(Mathf.Repeat(t, 1f), 0.85f, 1f));
            var result = text.Substring(0, start) + colored + text.Substring(end);
            newStart = start;
            newEnd = start + colored.Length;
            return result;
        }

        public static List<Target> CollectFromObject(Object obj)
        {
            var list = new List<Target>();
            if (obj == null)
                return list;

            if (IsVrcExpressionsMenu(obj))
            {
                CollectFromMenuAsset(obj, list, new HashSet<int>(), "");
                return list;
            }

            if (IsMaMenuItem(obj))
            {
                CollectFromMaItem(obj, list);
                var maComponent = obj as Component;
                if (maComponent != null)
                    CollectChildMaItems(maComponent.gameObject, obj, list);
                return list;
            }

            GameObject go = null;
            if (obj is GameObject gameObject)
                go = gameObject;
            else if (obj is Component component)
                go = component.gameObject;

            if (go != null)
                CollectFromGameObject(go, list);

            return list;
        }

        public static List<Target> CollectFromSelection()
        {
            var list = new List<Target>();
            var seen = new HashSet<string>();
            foreach (var obj in Selection.objects)
            {
                foreach (var t in CollectFromObject(obj))
                {
                    var id = t.Owner != null ? t.Owner.GetInstanceID() : 0;
                    var key = id + ":" + (int)t.Kind + ":" + t.ControlIndex + ":" + t.LabelIndex;
                    if (!seen.Add(key))
                        continue;
                    list.Add(t);
                }
            }

            return list;
        }

        public static string Read(Target target)
        {
            if (target == null || !target.IsValid)
                return "";

            if (target.Kind == TargetKind.MaItemName)
                return GetMaDisplayText(target.Owner);

            var so = new SerializedObject(target.Owner);
            var prop = so.FindProperty(target.PropertyPath);
            return prop != null ? prop.stringValue ?? "" : "";
        }

        public static bool Write(Target target, string text, string undoName = "Menu Rich Text")
        {
            if (target == null || !target.IsValid)
                return false;

            var owner = target.Owner;
            Undo.RecordObject(owner, undoName);

            if (target.Kind == TargetKind.MaItemName)
            {
                var so = new SerializedObject(owner);
                var labelProp = so.FindProperty("label");
                if (labelProp == null)
                    return false;
                so.Update();
                labelProp.stringValue = text ?? "";
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                var so2 = new SerializedObject(owner);
                var prop = so2.FindProperty(target.PropertyPath);
                if (prop == null)
                    return false;
                so2.Update();
                prop.stringValue = text ?? "";
                so2.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(owner);
            if (PrefabUtility.IsPartOfPrefabInstance(owner))
                PrefabUtility.RecordPrefabInstancePropertyModifications(owner);

            target.RawText = text ?? "";
            target.PlainPreview = StripTags(target.RawText);
            return true;
        }

        public static void Ping(Target target)
        {
            if (target == null || !target.IsValid)
                return;
            EditorGUIUtility.PingObject(target.Owner);
            Selection.activeObject = target.Owner;
        }

        static string GetMaDisplayText(Object maItem)
        {
            var so = new SerializedObject(maItem);
            var labelProp = so.FindProperty("label");
            var label = labelProp != null ? labelProp.stringValue : "";
            if (!string.IsNullOrEmpty(label))
                return label;

            var component = maItem as Component;
            return component != null ? component.gameObject.name : "";
        }

        static void CollectFromGameObject(GameObject go, List<Target> list)
        {
            if (go == null)
                return;

            var maType = MaMenuItemType;
            var selfItem = maType != null ? go.GetComponent(maType) : null;
            if (selfItem != null)
                CollectFromMaItem(selfItem, list);

            var descType = VrcAvatarDescriptorType;
            var selfDesc = descType != null ? go.GetComponent(descType) : null;
            if (selfDesc != null)
            {
                var so = new SerializedObject(selfDesc);
                var menuProp = so.FindProperty("expressionsMenu");
                var menu = menuProp != null ? menuProp.objectReferenceValue : null;
                if (menu != null)
                    CollectFromMenuAsset(menu, list, new HashSet<int>(), "");
            }

            if (maType == null)
                return;

            // 自身に MA Menu Item があっても、子階層もすべて収集する
            CollectChildMaItems(go, selfItem, list);
        }

        static void CollectChildMaItems(GameObject root, Object skipItem, List<Target> list)
        {
            var maType = MaMenuItemType;
            if (root == null || maType == null)
                return;

            var children = root.GetComponentsInChildren(maType, true);
            foreach (var item in children)
            {
                if (item == null || item == skipItem)
                    continue;
                CollectFromMaItem(item, list);
            }
        }

        static void CollectFromMaItem(Object maItem, List<Target> list)
        {
            if (maItem == null)
                return;

            var component = maItem as Component;
            var path = component != null ? GetHierarchyPath(component.transform) : maItem.name;
            var so = new SerializedObject(maItem);
            var typeProp = so.FindProperty("Control.type");
            var typeVal = typeProp != null ? typeProp.intValue : ControlTypeToggle;
            var raw = GetMaDisplayText(maItem);

            list.Add(new Target
            {
                Kind = TargetKind.MaItemName,
                Owner = maItem,
                OwnerPath = path,
                KindLabel = $"MA {ControlTypeName(typeVal)}",
                PropertyPath = "label",
                RawText = raw,
                PlainPreview = StripTags(raw)
            });

            var labels = so.FindProperty("Control.labels");
            if (labels == null || !labels.isArray)
                return;
            if (typeVal != ControlTypeTwoAxis && typeVal != ControlTypeFourAxis && typeVal != ControlTypeRadial)
                return;

            for (var i = 0; i < labels.arraySize; i++)
            {
                var nameProp = labels.GetArrayElementAtIndex(i).FindPropertyRelative("name");
                var labelRaw = nameProp != null ? nameProp.stringValue ?? "" : "";
                var axis = i < AxisNames.Length ? AxisNames[i] : (i + 1).ToString();
                list.Add(new Target
                {
                    Kind = TargetKind.MaItemLabel,
                    Owner = maItem,
                    OwnerPath = path,
                    KindLabel = $"MA {axis}",
                    ControlIndex = 0,
                    LabelIndex = i,
                    PropertyPath = $"Control.labels.Array.data[{i}].name",
                    RawText = labelRaw,
                    PlainPreview = StripTags(labelRaw)
                });
            }
        }

        static void CollectFromMenuAsset(Object menu, List<Target> list, HashSet<int> visited, string breadcrumb)
        {
            if (menu == null || VrcExpressionsMenuType == null || !VrcExpressionsMenuType.IsInstanceOfType(menu))
                return;
            if (!visited.Add(menu.GetInstanceID()))
                return;

            var so = new SerializedObject(menu);
            var controls = so.FindProperty("controls");
            if (controls == null || !controls.isArray)
                return;

            var menuName = string.IsNullOrEmpty(breadcrumb) ? menu.name : breadcrumb + " / " + menu.name;
            for (var i = 0; i < controls.arraySize; i++)
            {
                var control = controls.GetArrayElementAtIndex(i);
                var nameProp = control.FindPropertyRelative("name");
                var typeProp = control.FindPropertyRelative("type");
                var typeVal = typeProp != null ? typeProp.intValue : 0;
                var raw = nameProp != null ? nameProp.stringValue ?? "" : "";
                list.Add(new Target
                {
                    Kind = TargetKind.VrcControlName,
                    Owner = menu,
                    OwnerPath = menuName,
                    KindLabel = $"VRC [{i}] {ControlTypeName(typeVal)}",
                    ControlIndex = i,
                    PropertyPath = $"controls.Array.data[{i}].name",
                    RawText = raw,
                    PlainPreview = StripTags(raw)
                });

                var labels = control.FindPropertyRelative("labels");
                if (labels != null && labels.isArray &&
                    (typeVal == ControlTypeTwoAxis || typeVal == ControlTypeFourAxis || typeVal == ControlTypeRadial))
                {
                    for (var li = 0; li < labels.arraySize; li++)
                    {
                        var labelName = labels.GetArrayElementAtIndex(li).FindPropertyRelative("name");
                        var labelRaw = labelName != null ? labelName.stringValue ?? "" : "";
                        var axis = li < AxisNames.Length ? AxisNames[li] : (li + 1).ToString();
                        list.Add(new Target
                        {
                            Kind = TargetKind.VrcControlLabel,
                            Owner = menu,
                            OwnerPath = menuName,
                            KindLabel = $"VRC [{i}] {axis}",
                            ControlIndex = i,
                            LabelIndex = li,
                            PropertyPath = $"controls.Array.data[{i}].labels.Array.data[{li}].name",
                            RawText = labelRaw,
                            PlainPreview = StripTags(labelRaw)
                        });
                    }
                }

                var subMenuProp = control.FindPropertyRelative("subMenu");
                var subMenu = subMenuProp != null ? subMenuProp.objectReferenceValue : null;
                if (subMenu != null)
                    CollectFromMenuAsset(subMenu, list, visited, menuName);
            }
        }

        static string GetHierarchyPath(Transform t)
        {
            if (t == null)
                return "";
            var parts = new List<string>();
            while (t != null)
            {
                parts.Add(t.name);
                t = t.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        static bool TryUnwrap(string text, int start, int end, string open, string close, out string result, out int newStart, out int newEnd)
        {
            result = text;
            newStart = start;
            newEnd = end;

            if (start < end)
            {
                var inner = text.Substring(start, end - start);
                if (inner.StartsWith(open, StringComparison.Ordinal) && inner.EndsWith(close, StringComparison.Ordinal) &&
                    inner.Length >= open.Length + close.Length)
                {
                    var core = inner.Substring(open.Length, inner.Length - open.Length - close.Length);
                    result = text.Substring(0, start) + core + text.Substring(end);
                    newStart = start;
                    newEnd = start + core.Length;
                    return true;
                }
            }

            if (start >= open.Length && end + close.Length <= text.Length &&
                string.CompareOrdinal(text, start - open.Length, open, 0, open.Length) == 0 &&
                string.CompareOrdinal(text, end, close, 0, close.Length) == 0)
            {
                result = text.Substring(0, start - open.Length) + text.Substring(start, end - start) +
                         text.Substring(end + close.Length);
                newStart = start - open.Length;
                newEnd = newStart + (end - start);
                return true;
            }

            return false;
        }

        static bool TryFindWrappingTag(string text, int start, int end, string tagName, out int openStart, out int openEnd, out int closeStart, out int closeEnd)
        {
            openStart = openEnd = closeStart = closeEnd = -1;
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(tagName) || start > end)
                return false;

            // 選択が開タグから始まる場合
            if (start < end && start < text.Length && text[start] == '<' &&
                TryParseNamedOpenTagAt(text, start, tagName, out openStart, out openEnd) &&
                TryFindMatchingCloseTag(text, openEnd, tagName, out closeStart, out closeEnd))
            {
                if (end == closeEnd || end == closeStart)
                    return true;
            }

            // 選択がタグ内側（直前が '>'、直後が閉じタグ）
            if (start > 0 && text[start - 1] == '>' &&
                TryFindOpenTagBefore(text, start, tagName, out openStart, out openEnd) &&
                TryFindMatchingCloseTag(text, openEnd, tagName, out closeStart, out closeEnd) &&
                closeStart == end)
                return true;

            return false;
        }

        static bool TryParseNamedOpenTagAt(string text, int index, string tagName, out int openStart, out int openEnd)
        {
            openStart = openEnd = -1;
            if (index < 0 || index >= text.Length || text[index] != '<')
                return false;
            var gt = text.IndexOf('>', index);
            if (gt < 0 || !IsNamedOpenTag(text, index, gt, tagName))
                return false;
            openStart = index;
            openEnd = gt + 1;
            return true;
        }

        static bool TryFindOpenTagBefore(string text, int contentStart, string tagName, out int openStart, out int openEnd)
        {
            openStart = openEnd = -1;
            if (contentStart <= 0 || contentStart > text.Length || text[contentStart - 1] != '>')
                return false;
            var lt = text.LastIndexOf('<', contentStart - 2);
            if (lt < 0)
                return false;
            return TryParseNamedOpenTagAt(text, lt, tagName, out openStart, out openEnd) && openEnd == contentStart;
        }

        static bool TryFindMatchingCloseTag(string text, int searchFrom, string tagName, out int closeStart, out int closeEnd)
        {
            closeStart = closeEnd = -1;
            var close = "</" + tagName + ">";
            var depth = 1;
            var i = searchFrom;
            while (i < text.Length && depth > 0)
            {
                if (text[i] != '<')
                {
                    i++;
                    continue;
                }

                if (i + close.Length <= text.Length &&
                    string.Compare(text, i, close, 0, close.Length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    depth--;
                    if (depth == 0)
                    {
                        closeStart = i;
                        closeEnd = i + close.Length;
                        return true;
                    }

                    i += close.Length;
                    continue;
                }

                if (TryParseNamedOpenTagAt(text, i, tagName, out _, out var nestedOpenEnd))
                {
                    depth++;
                    i = nestedOpenEnd;
                    continue;
                }

                var gt = text.IndexOf('>', i);
                i = gt < 0 ? text.Length : gt + 1;
            }

            return false;
        }

        static bool TryDetectPerCharColorGradient(string text, int start, int end, out Color[] colors, out int runStart, out int runEnd)
        {
            colors = null;
            runStart = runEnd = -1;
            text = text ?? "";
            GetSelectionRange(text, start, end, out start, out end);
            if (start >= end)
                return false;

            var spans = new List<(int Start, int End, Color Color)>();
            var i = 0;
            while (i < text.Length)
            {
                if (!TryParsePerCharColorSpanAt(text, i, out var spanStart, out var spanEnd, out var col))
                {
                    i++;
                    continue;
                }

                spans.Add((spanStart, spanEnd, col));
                i = spanEnd;
            }

            if (spans.Count < 2)
                return false;

            // 選択と交差する連続ランを探す
            for (var runBegin = 0; runBegin < spans.Count;)
            {
                var runFinish = runBegin;
                while (runFinish + 1 < spans.Count && spans[runFinish + 1].Start == spans[runFinish].End)
                    runFinish++;

                var rs = spans[runBegin].Start;
                var re = spans[runFinish].End;
                var spanCount = runFinish - runBegin + 1;
                if (spanCount >= 2 && re > start && rs < end)
                {
                    // 選択がこのランの一部または全体と重なる
                    var list = new Color[spanCount];
                    for (var k = 0; k < spanCount; k++)
                        list[k] = spans[runBegin + k].Color;
                    colors = list;
                    runStart = rs;
                    runEnd = re;
                    return true;
                }

                runBegin = runFinish + 1;
            }

            return false;
        }

        static bool TryParsePerCharColorSpanAt(string text, int index, out int spanStart, out int spanEnd, out Color color)
        {
            spanStart = spanEnd = -1;
            color = Color.white;
            if (!TryParseNamedOpenTagAt(text, index, "color", out spanStart, out var openEnd))
                return false;
            if (!TryParseOpenTagValue(text, spanStart, openEnd, out var raw))
                return false;
            if (openEnd >= text.Length || text[openEnd] == '<')
                return false;
            // グラデ適用は可視1文字ずつ
            var contentIndex = openEnd;
            if (contentIndex + 1 > text.Length)
                return false;
            const string close = "</color>";
            if (contentIndex + 1 + close.Length > text.Length)
                return false;
            if (string.Compare(text, contentIndex + 1, close, 0, close.Length, StringComparison.OrdinalIgnoreCase) != 0)
                return false;

            color = ParseColorOr(raw.TrimStart('#'), Color.white);
            spanEnd = contentIndex + 1 + close.Length;
            return true;
        }

        static Gradient BuildGradientFromColors(Color[] colors)
        {
            var g = new Gradient();
            if (colors == null || colors.Length == 0)
            {
                g.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
                return g;
            }

            var n = Mathf.Clamp(colors.Length, 1, 8);
            var colorKeys = new GradientColorKey[n];
            var alphaKeys = new GradientAlphaKey[n];
            for (var i = 0; i < n; i++)
            {
                var t = n == 1 ? 0f : (float)i / (n - 1);
                var src = colors.Length == 1
                    ? 0
                    : Mathf.Clamp(Mathf.RoundToInt(t * (colors.Length - 1)), 0, colors.Length - 1);
                var c = colors[src];
                colorKeys[i] = new GradientColorKey(c, t);
                alphaKeys[i] = new GradientAlphaKey(c.a, t);
            }

            g.SetKeys(colorKeys, alphaKeys);
            return g;
        }

        static bool IsNamedOpenTag(string text, int lt, int gt, string tagName)
        {
            if (lt < 0 || gt <= lt + 1 || gt >= text.Length || text[lt] != '<' || text[gt] != '>')
                return false;
            var i = lt + 1;
            if (text[i] == '/')
                return false;
            if (string.Compare(text, i, tagName, 0, tagName.Length, StringComparison.OrdinalIgnoreCase) != 0)
                return false;
            var after = i + tagName.Length;
            return after == gt || text[after] == ' ' || text[after] == '=' || text[after] == '\t' || text[after] == '#';
        }

        static string ColorizeVisibleCharacters(string inner, Func<float, Color> colorAt)
        {
            inner = inner ?? "";
            var visibleCount = CountVisibleCharacters(inner);
            if (visibleCount <= 0)
                return inner;

            var sb = new StringBuilder(inner.Length * 8);
            var inTag = false;
            var visibleIndex = 0;
            for (var i = 0; i < inner.Length; i++)
            {
                var c = inner[i];
                if (c == '<')
                    inTag = true;
                if (inTag)
                {
                    sb.Append(c);
                    if (c == '>')
                        inTag = false;
                    continue;
                }

                if (char.IsWhiteSpace(c) || c == '\u2060')
                {
                    sb.Append(c);
                    continue;
                }

                var t = visibleCount <= 1 ? 0f : (float)visibleIndex / (visibleCount - 1);
                var col = colorAt(t);
                sb.Append("<color=#").Append(ToHtmlColor(col)).Append('>').Append(c).Append("</color>");
                visibleIndex++;
            }

            return sb.ToString();
        }

        static int CountVisibleCharacters(string text)
        {
            var count = 0;
            var inTag = false;
            foreach (var c in text)
            {
                if (c == '<')
                    inTag = true;
                if (inTag)
                {
                    if (c == '>')
                        inTag = false;
                    continue;
                }

                if (!char.IsWhiteSpace(c) && c != '\u2060')
                    count++;
            }

            return count;
        }
    }
}
