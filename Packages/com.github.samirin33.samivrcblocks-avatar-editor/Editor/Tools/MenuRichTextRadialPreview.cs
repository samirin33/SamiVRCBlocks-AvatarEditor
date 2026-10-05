using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SamiVRCBlocksAvatar.Editor
{
    /// <summary>
    /// Gesture Manager が入っているとき、そのラジアルメニューと同じ画像・扇の数・配置でプレビューします。
    /// 扇の角度とラベル位置は RadialMenu.AddButtons と同じ計算です。
    /// </summary>
    sealed class MenuRichTextRadialPreview
    {
        const float WheelSize = 300f;
        const float LabelRadius = 100f;
        const float InnerSize = 100f;
        const float SliceFont = 12f;
        const float AxisFont = 8f;
        const float DefaultTextLiftEm = 0.35f;
        const int MaxControls = 8;

        static readonly Color DefaultMain = new Color(0.14f, 0.18f, 0.2f);
        static readonly Color DefaultBorder = new Color(0.1f, 0.35f, 0.38f);
        static readonly Color DefaultSelected = new Color(0.07f, 0.55f, 0.58f);
        static readonly Color CenterIdle = new Color(0.06f, 0.27f, 0.29f);
        static readonly Color CenterSelected = new Color(0.06f, 0.2f, 0.22f);
        static readonly Color RadialInner = new Color(0.21f, 0.24f, 0.27f);
        static readonly Color SubIconBg = new Color(0.22f, 0.24f, 0.27f);
        static readonly Color LabelColor = new Color(0.824f, 0.824f, 0.824f, 1f);

        static bool _resolved;
        static bool _available;
        static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();

        public static bool IsAvailable
        {
            get
            {
                if (_resolved)
                    return _available;
                _resolved = true;
                _available = Load("Vrc3/BSX_GM_Default") != null;
                return _available;
            }
        }

        public static VisualElement Build(MenuRichTextUtility.Target target, string liveText, UnityEngine.Object source, Color background)
        {
            if (!IsAvailable)
                return null;

            var model = PreviewModel.Create(target, liveText, source);
            var host = new VisualElement();
            host.pickingMode = PickingMode.Ignore;
            UseDefaultFont(host);
            host.style.alignItems = Align.Center;
            host.style.marginLeft = 4f;
            host.style.marginRight = 4f;
            host.style.marginBottom = 4f;
            host.style.paddingTop = 6f;
            host.style.paddingBottom = 8f;
            host.style.backgroundColor = background;

            var wheel = new VisualElement();
            wheel.pickingMode = PickingMode.Ignore;
            wheel.style.width = WheelSize;
            wheel.style.height = WheelSize;
            wheel.style.position = Position.Relative;
            wheel.style.flexShrink = 0f;
            wheel.style.overflow = Overflow.Visible;
            host.Add(wheel);

            var colors = GmColors.Load();
            DrawWheel(wheel, model, colors);
            if (model.Puppet == PuppetKind.Axis)
                DrawAxisPuppet(wheel, model, colors);
            else if (model.Puppet == PuppetKind.Radial)
                DrawRadialPuppet(wheel, model, colors);

            if (!string.IsNullOrEmpty(model.Note))
            {
                var note = new Label(model.Note);
                note.pickingMode = PickingMode.Ignore;
                note.style.unityTextAlign = TextAnchor.MiddleCenter;
                note.style.whiteSpace = WhiteSpace.Normal;
                note.style.color = new Color(1f, 1f, 1f, 0.75f);
                note.style.fontSize = 11f;
                note.style.marginTop = 4f;
                note.style.width = WheelSize;
                host.Add(note);
            }

            if (!string.IsNullOrEmpty(model.OverflowText))
            {
                var overflow = RichTextLabel.Create(model.OverflowText, SliceFont, WheelSize, centerOnBounds: false);
                overflow.style.position = Position.Relative;
                overflow.style.top = -SliceFont * DefaultTextLiftEm;
                host.Add(overflow);
            }

            return host;
        }

        static void DrawWheel(VisualElement wheel, PreviewModel model, GmColors colors)
        {
            var count = model.Slices.Count;
            var cx = WheelSize * 0.5f;
            var cy = WheelSize * 0.5f;
            if (count > 0)
            {
                for (var i = 0; i < count; i++)
                {
                    SlicePose(i, count, out var progress, out var rotationDeg, out _);
                    var slice = model.Slices[i];
                    var pie = new PieElement
                    {
                        Progress = progress,
                        CenterColor = slice.Selected ? CenterSelected : CenterIdle,
                        FillColor = slice.Selected ? colors.Selected : colors.Main,
                        BorderColor = colors.Border
                    };
                    pie.pickingMode = PickingMode.Ignore;
                    pie.style.position = Position.Absolute;
                    pie.style.left = 0f;
                    pie.style.top = 0f;
                    pie.style.width = WheelSize;
                    pie.style.height = WheelSize;
                    pie.transform.rotation = Quaternion.Euler(0f, 0f, rotationDeg);
                    wheel.Add(pie);
                }

                for (var i = 0; i < count; i++)
                {
                    SlicePose(i, count, out _, out var rotationDeg, out _);
                    // 中心から外へ 1 本だけ伸ばす。線を中心で折り返すと、偶数個のとき対向の仕切りと重なって二重に見える。
                    var spoke = WheelSize * 0.5f - 2f;
                    var line = new VisualElement();
                    line.pickingMode = PickingMode.Ignore;
                    line.style.position = Position.Absolute;
                    line.style.width = spoke;
                    line.style.height = 2f;
                    line.style.backgroundColor = colors.Border;
                    line.style.left = cx;
                    line.style.top = cy - 1f;
                    line.style.transformOrigin = new TransformOrigin(0f, Length.Percent(50f), 0f);
                    line.transform.rotation = Quaternion.Euler(0f, 0f, rotationDeg - 90f);
                    wheel.Add(line);
                }
            }

            var inner = new PieElement
            {
                Progress = 1f,
                CenterColor = RadialInner,
                FillColor = RadialInner,
                BorderColor = colors.Border
            };
            inner.pickingMode = PickingMode.Ignore;
            inner.style.position = Position.Absolute;
            inner.style.width = InnerSize;
            inner.style.height = InnerSize;
            inner.style.left = cx - InnerSize * 0.5f;
            inner.style.top = cy - InnerSize * 0.5f;
            wheel.Add(inner);

            for (var i = 0; i < count; i++)
            {
                SlicePose(i, count, out _, out _, out var textAngle);
                var slice = model.Slices[i];
                var x = Mathf.Sin(textAngle) * LabelRadius;
                var y = Mathf.Cos(textAngle) * LabelRadius;
                wheel.Add(CreateSliceLabel(cx + x, cy + y, slice, SliceFont));
            }
        }

        static VisualElement CreateSliceLabel(float centerX, float centerY, SliceInfo slice, float fontSize)
        {
            var holder = new VisualElement();
            holder.pickingMode = PickingMode.Ignore;
            holder.style.position = Position.Absolute;
            holder.style.width = 100f;
            holder.style.height = 100f;
            holder.style.left = centerX - 50f;
            holder.style.top = centerY - 50f;
            holder.style.overflow = Overflow.Visible;

            // アイコンは文字の行数やサイズに関わらず、扇の中心に固定する
            var icon = new VisualElement();
            icon.pickingMode = PickingMode.Ignore;
            icon.style.position = Position.Absolute;
            icon.style.left = 25f;
            icon.style.top = 25f;
            icon.style.width = 50f;
            icon.style.height = 50f;
            icon.style.backgroundImage = new StyleBackground(slice.Icon != null ? slice.Icon : Load("Vrc3/BSX_GM_Default"));
            icon.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            holder.Add(icon);

            if (slice.SubIcon != null)
            {
                var badge = new VisualElement();
                badge.pickingMode = PickingMode.Ignore;
                badge.style.position = Position.Absolute;
                badge.style.top = 18f;
                badge.style.left = 35f;
                badge.style.width = 25f;
                badge.style.height = 25f;
                badge.style.borderTopLeftRadius = 15f;
                badge.style.borderTopRightRadius = 15f;
                badge.style.borderBottomLeftRadius = 15f;
                badge.style.borderBottomRightRadius = 15f;
                badge.style.backgroundColor = SubIconBg;
                badge.style.alignItems = Align.Center;
                badge.style.justifyContent = Justify.Center;
                var sub = new VisualElement();
                sub.pickingMode = PickingMode.Ignore;
                sub.style.width = 20f;
                sub.style.height = 20f;
                sub.style.backgroundImage = new StyleBackground(slice.SubIcon);
                sub.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                badge.Add(sub);
                icon.Add(badge);
            }

            var caption = RichTextLabel.Create(slice.Text, fontSize, 96f);
            caption.style.position = Position.Absolute;
            caption.style.left = 2f;
            // top は文字塊の中心。0.35em はリッチテキストではなく UI 位置として上にずらす
            caption.style.top = 84f - fontSize * DefaultTextLiftEm;
            caption.style.width = 96f;
            holder.Add(caption);
            return holder;
        }

        static void DrawAxisPuppet(VisualElement wheel, PreviewModel model, GmColors colors)
        {
            var cx = WheelSize * 0.5f;
            var cy = WheelSize * 0.5f;
            AddCircle(wheel, cx, cy, 140f, colors.Main, colors.Main, colors.Border);
            for (var i = 0; i < 4; i++)
                AddDiameter(wheel, cx, cy, 68f, colors.Border, 45f + 90f * i);
            AddCircle(wheel, cx, cy, 65f, RadialInner, RadialInner, colors.Border);

            var icons = new[]
            {
                Load("Vrc3/BSX_GM_Axis_Up"),
                Load("Vrc3/BSX_GM_Axis_Right"),
                Load("Vrc3/BSX_GM_Axis_Down"),
                Load("Vrc3/BSX_GM_Axis_Left")
            };
            var offsets = new[]
            {
                new Vector2(0f, -50f),
                new Vector2(50f, 0f),
                new Vector2(0f, 50f),
                new Vector2(-50f, 0f)
            };
            for (var i = 0; i < model.AxisLabels.Count; i++)
            {
                var label = model.AxisLabels[i];
                if (label.Slot < 0 || label.Slot >= offsets.Length)
                    continue;
                var pos = offsets[label.Slot];
                var icon = label.Icon != null ? label.Icon : icons[label.Slot];
                wheel.Add(CreateAxisLabel(cx + pos.x, cy + pos.y, icon, label.Text, label.Selected, colors));
            }
        }

        static VisualElement CreateAxisLabel(float centerX, float centerY, Texture2D icon, string text, bool selected, GmColors colors)
        {
            var holder = new VisualElement();
            holder.pickingMode = PickingMode.Ignore;
            holder.style.position = Position.Absolute;
            holder.style.width = 24f;
            holder.style.height = 24f;
            holder.style.left = centerX - 12f;
            holder.style.top = centerY - 12f;
            if (icon != null)
            {
                holder.style.backgroundImage = new StyleBackground(icon);
                holder.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            }

            if (selected)
            {
                holder.style.borderTopWidth = 1f;
                holder.style.borderBottomWidth = 1f;
                holder.style.borderLeftWidth = 1f;
                holder.style.borderRightWidth = 1f;
                holder.style.borderTopColor = colors.Selected;
                holder.style.borderBottomColor = colors.Selected;
                holder.style.borderLeftColor = colors.Selected;
                holder.style.borderRightColor = colors.Selected;
            }

            var caption = RichTextLabel.Create(text, AxisFont, 72f);
            caption.style.position = Position.Absolute;
            caption.style.top = 30f - AxisFont * DefaultTextLiftEm;
            caption.style.left = -24f;
            caption.style.width = 72f;
            holder.Add(caption);
            return holder;
        }

        static void DrawRadialPuppet(VisualElement wheel, PreviewModel model, GmColors colors)
        {
            var cx = WheelSize * 0.5f;
            var cy = WheelSize * 0.5f;
            AddCircle(wheel, cx, cy, 100f, colors.Main, colors.Main, colors.Border);
            AddCircle(wheel, cx, cy, 96f, colors.Selected, colors.Selected, colors.Selected);
            AddCircle(wheel, cx, cy, 65f, RadialInner, RadialInner, colors.Border);

            var bubble = new VisualElement();
            bubble.pickingMode = PickingMode.Ignore;
            bubble.style.position = Position.Absolute;
            bubble.style.width = 70f;
            bubble.style.height = 22f;
            bubble.style.left = cx - 35f;
            bubble.style.top = cy - 11f;
            bubble.style.backgroundColor = new Color(0.11f, 0.11f, 0.11f, 0.49f);
            bubble.style.borderTopLeftRadius = 10f;
            bubble.style.borderTopRightRadius = 10f;
            bubble.style.borderBottomLeftRadius = 10f;
            bubble.style.borderBottomRightRadius = 10f;
            const float radialFont = 11f;
            var radialText = RichTextLabel.Create(model.RadialText, radialFont, 68f);
            radialText.style.position = Position.Absolute;
            radialText.style.left = 1f;
            radialText.style.top = 11f - radialFont * DefaultTextLiftEm;
            radialText.style.width = 68f;
            bubble.Add(radialText);
            wheel.Add(bubble);
        }

        static void AddCircle(VisualElement parent, float cx, float cy, float size, Color center, Color fill, Color border)
        {
            var pie = new PieElement
            {
                Progress = 1f,
                CenterColor = center,
                FillColor = fill,
                BorderColor = border
            };
            pie.pickingMode = PickingMode.Ignore;
            pie.style.position = Position.Absolute;
            pie.style.width = size;
            pie.style.height = size;
            pie.style.left = cx - size * 0.5f;
            pie.style.top = cy - size * 0.5f;
            parent.Add(pie);
        }

        static void AddDiameter(VisualElement parent, float cx, float cy, float length, Color color, float euler)
        {
            var line = new VisualElement();
            line.pickingMode = PickingMode.Ignore;
            line.style.position = Position.Absolute;
            line.style.width = length;
            line.style.height = 2f;
            line.style.backgroundColor = color;
            line.style.left = cx - length * 0.5f;
            line.style.top = cy - 1f;
            line.transform.rotation = Quaternion.Euler(0f, 0f, euler);
            parent.Add(line);
        }

        /// <summary>
        /// Gesture Manager の AddButtons と同じ扇の進行・回転・ラベル角度です。
        /// </summary>
        static void SlicePose(int index, int count, out float progress, out float rotationDeg, out float textAngleRad)
        {
            progress = 1f / Mathf.Max(1, count);
            var rotation = Mathf.Rad2Deg * -Mathf.PI / count;
            var angle = Mathf.PI;
            for (var i = 0; i < index; i++)
            {
                rotation += 360f / count;
                angle -= Mathf.PI * 2f / count;
            }

            rotationDeg = rotation;
            textAngleRad = angle;
        }

        static Texture2D Load(string path)
        {
            if (Textures.TryGetValue(path, out var cached))
                return cached;
            var tex = Resources.Load<Texture2D>(path);
            Textures[path] = tex;
            return tex;
        }

        static Texture2D SubIconFor(int controlType)
        {
            switch (controlType)
            {
                case MenuRichTextUtility.ControlTypeToggle: return Load("Vrc3/BSX_GM_Toggle");
                case MenuRichTextUtility.ControlTypeSubMenu: return Load("Vrc3/BSX_GM_Option");
                case MenuRichTextUtility.ControlTypeTwoAxis: return Load("Vrc3/BSX_GM_2_Axis");
                case MenuRichTextUtility.ControlTypeFourAxis: return Load("Vrc3/BSX_GM_4_Axis");
                case MenuRichTextUtility.ControlTypeRadial: return Load("Vrc3/BSX_GM_Radial");
                default: return null;
            }
        }

        sealed class PieElement : VisualElement
        {
            public float Progress = 1f;
            public Color CenterColor;
            public Color FillColor;
            public Color BorderColor;
            public float BorderWidth = 2f;

            public PieElement()
            {
                generateVisualContent += OnGenerate;
            }

            void OnGenerate(MeshGenerationContext mgc)
            {
                var rect = contentRect;
                if (rect.width < 2f || rect.height < 2f || Progress <= 0.001f)
                    return;

                var full = Progress >= 0.999f;
                var seg = full ? 64 : Mathf.Max(1, Mathf.RoundToInt(64f * Progress));
                var points = full ? seg : seg + 1;
                var tris = full ? points : points - 1;
                var sweep = Progress * Mathf.PI * 2f;

                var vertices = new Vertex[1 + points * 2];
                vertices[0] = new Vertex
                {
                    position = GmPos(0f, 0f, rect.width, rect.height),
                    tint = CenterColor
                };

                var innerW = Mathf.Max(1f, rect.width - BorderWidth * 2f);
                var innerH = Mathf.Max(1f, rect.height - BorderWidth * 2f);
                for (var i = 0; i < points; i++)
                {
                    var t = points == 1 ? 0f : sweep * i / (points - (full ? 0 : 1));
                    if (full)
                        t = sweep * i / points;
                    var sx = Mathf.Sin(t);
                    var sy = -Mathf.Cos(t);
                    vertices[1 + i] = new Vertex
                    {
                        position = GmPos(sx, sy, rect.width, rect.height),
                        tint = BorderColor
                    };
                    var inner = GmPos(sx, sy, innerW, innerH);
                    inner.x += BorderWidth;
                    inner.y += BorderWidth;
                    vertices[1 + points + i] = new Vertex
                    {
                        position = inner,
                        tint = FillColor
                    };
                }

                var indices = new ushort[tris * 6];
                for (var i = 0; i < tris; i++)
                {
                    var a = (ushort)(1 + i);
                    var b = (ushort)(1 + (i + 1) % points);
                    indices[i * 3] = 0;
                    indices[i * 3 + 1] = a;
                    indices[i * 3 + 2] = b;
                    var ia = (ushort)(1 + points + i);
                    var ib = (ushort)(1 + points + (i + 1) % points);
                    var o = tris * 3 + i * 3;
                    indices[o] = 0;
                    indices[o + 1] = ia;
                    indices[o + 2] = ib;
                }

                var write = mgc.Allocate(vertices.Length, indices.Length);
                write.SetAllVertices(vertices);
                write.SetAllIndices(indices);
            }

            static Vector3 GmPos(float x, float y, float w, float h)
            {
                return new Vector3((x * 0.5f + 0.5f) * w, (y * 0.5f + 0.5f) * h, Vertex.nearZ);
            }
        }

        struct GmColors
        {
            public Color Main;
            public Color Border;
            public Color Selected;

            public static GmColors Load()
            {
                return new GmColors
                {
                    Main = Pref("GM3 Main Color", DefaultMain),
                    Border = Pref("GM3 Border Color", DefaultBorder),
                    Selected = Pref("GM3 Selected Color", DefaultSelected)
                };
            }

            static Color Pref(string key, Color fallback)
            {
                var raw = EditorPrefs.GetString(key, "");
                if (string.IsNullOrEmpty(raw))
                    return fallback;
                return ColorUtility.TryParseHtmlString(raw, out var color) ? color : fallback;
            }
        }

        enum PuppetKind
        {
            None,
            Axis,
            Radial
        }

        sealed class SliceInfo
        {
            public string Text;
            public Texture2D Icon;
            public Texture2D SubIcon;
            public bool Selected;
        }

        sealed class AxisLabelInfo
        {
            public int Slot;
            public string Text;
            public Texture2D Icon;
            public bool Selected;
        }

        sealed class PreviewModel
        {
            public readonly List<SliceInfo> Slices = new List<SliceInfo>();
            public readonly List<AxisLabelInfo> AxisLabels = new List<AxisLabelInfo>();
            public PuppetKind Puppet;
            public string RadialText = "";
            public string Note = "";
            public string OverflowText = "";

            public static PreviewModel Create(MenuRichTextUtility.Target target, string liveText, UnityEngine.Object source)
            {
                var model = new PreviewModel();
                liveText = liveText ?? "";
                if (target == null || !target.IsValid)
                {
                    model.Note = "編集対象がありません。";
                    return model;
                }

                if (target.Kind == MenuRichTextUtility.TargetKind.VrcControlName ||
                    target.Kind == MenuRichTextUtility.TargetKind.VrcControlLabel)
                    FillFromVrcMenu(model, target, liveText, source);
                else
                    FillFromMaItem(model, target, liveText);

                return model;
            }

            static void FillFromVrcMenu(PreviewModel model, MenuRichTextUtility.Target target, string liveText, UnityEngine.Object source)
            {
                var menu = target.Owner;
                if (menu == null)
                    return;

                var so = new SerializedObject(menu);
                so.Update();
                var controls = so.FindProperty("controls");
                if (controls == null || !controls.isArray)
                    return;

                var isMain = IsAvatarRootMenu(menu, source);
                var prefix = isMain ? 2 : 1;
                var shown = Mathf.Min(controls.arraySize, MaxControls);
                var editingName = target.Kind == MenuRichTextUtility.TargetKind.VrcControlName;
                var selectedControl = target.ControlIndex;

                model.Slices.Add(new SliceInfo
                {
                    Text = "Back",
                    Icon = Load(isMain ? "Vrc3/BSX_GM_BackHome" : "Vrc3/BSX_GM_Back")
                });
                if (isMain)
                {
                    model.Slices.Add(new SliceInfo
                    {
                        Text = "Quick Actions",
                        Icon = Load("Vrc3/BSX_GM_Gear")
                    });
                }

                for (var i = 0; i < shown; i++)
                {
                    var control = controls.GetArrayElementAtIndex(i);
                    var type = ReadInt(control, "type");
                    var name = ReadString(control, "name");
                    var icon = ReadTexture(control, "icon");
                    var selected = i == selectedControl && editingName;
                    model.Slices.Add(new SliceInfo
                    {
                        Text = selected ? liveText : name,
                        Icon = icon,
                        SubIcon = SubIconFor(type),
                        Selected = i == selectedControl
                    });
                }

                if (selectedControl >= MaxControls)
                {
                    model.Note = "9件目以降はラジアルメニューに出ません。";
                    if (editingName)
                        model.OverflowText = liveText;
                }

                if (target.Kind != MenuRichTextUtility.TargetKind.VrcControlLabel || selectedControl < 0 || selectedControl >= controls.arraySize)
                    return;

                var edited = controls.GetArrayElementAtIndex(selectedControl);
                var editedType = ReadInt(edited, "type");
                if (editedType == MenuRichTextUtility.ControlTypeTwoAxis || editedType == MenuRichTextUtility.ControlTypeFourAxis)
                    FillAxisLabels(model, edited, target.LabelIndex, liveText);
                else if (editedType == MenuRichTextUtility.ControlTypeRadial)
                {
                    model.Puppet = PuppetKind.Radial;
                    model.RadialText = liveText;
                }
            }

            static void FillFromMaItem(PreviewModel model, MenuRichTextUtility.Target target, string liveText)
            {
                var self = target.Owner as Component;
                if (self == null)
                    return;

                var siblings = CollectSiblingMenuItems(self);
                var shown = Mathf.Min(siblings.Count, MaxControls);
                var selfIndex = siblings.IndexOf(self);
                var editingName = target.Kind == MenuRichTextUtility.TargetKind.MaItemName;

                model.Slices.Add(new SliceInfo
                {
                    Text = "Back",
                    Icon = Load("Vrc3/BSX_GM_Back")
                });

                for (var i = 0; i < shown; i++)
                {
                    var item = siblings[i];
                    var so = new SerializedObject(item);
                    so.Update();
                    var control = so.FindProperty("Control");
                    var type = ReadInt(control, "type");
                    var icon = control != null ? ReadTexture(control, "icon") : null;
                    var name = MaDisplayText(item, so);
                    var selected = i == selfIndex && editingName;
                    model.Slices.Add(new SliceInfo
                    {
                        Text = selected ? liveText : name,
                        Icon = icon,
                        SubIcon = SubIconFor(type),
                        Selected = i == selfIndex
                    });
                }

                if (selfIndex >= MaxControls)
                {
                    model.Note = "9件目以降はラジアルメニューに出ません。";
                    if (editingName)
                        model.OverflowText = liveText;
                }

                if (target.Kind != MenuRichTextUtility.TargetKind.MaItemLabel)
                    return;

                var selfSo = new SerializedObject(self);
                selfSo.Update();
                var selfControl = selfSo.FindProperty("Control");
                var selfType = selfControl != null ? ReadInt(selfControl, "type") : 0;
                if (selfType == MenuRichTextUtility.ControlTypeTwoAxis || selfType == MenuRichTextUtility.ControlTypeFourAxis)
                    FillAxisLabels(model, selfControl, target.LabelIndex, liveText);
                else if (selfType == MenuRichTextUtility.ControlTypeRadial)
                {
                    model.Puppet = PuppetKind.Radial;
                    model.RadialText = liveText;
                }
            }

            static void FillAxisLabels(PreviewModel model, SerializedProperty control, int selectedSlot, string liveText)
            {
                model.Puppet = PuppetKind.Axis;
                var labels = control != null ? control.FindPropertyRelative("labels") : null;
                var count = labels != null && labels.isArray ? Mathf.Min(labels.arraySize, 4) : 0;
                for (var i = 0; i < count; i++)
                {
                    var label = labels.GetArrayElementAtIndex(i);
                    model.AxisLabels.Add(new AxisLabelInfo
                    {
                        Slot = i,
                        Text = i == selectedSlot ? liveText : ReadString(label, "name"),
                        Icon = ReadTexture(label, "icon"),
                        Selected = i == selectedSlot
                    });
                }
            }

            static List<Component> CollectSiblingMenuItems(Component self)
            {
                var list = new List<Component>();
                var maType = MenuRichTextUtility.MaMenuItemType;
                var parent = self.transform.parent;
                if (parent == null || maType == null)
                {
                    list.Add(self);
                    return list;
                }

                for (var i = 0; i < parent.childCount; i++)
                {
                    var child = parent.GetChild(i);
                    if (child == null)
                        continue;
                    var item = child.GetComponent(maType) as Component;
                    if (item != null)
                        list.Add(item);
                }

                if (!list.Contains(self))
                    list.Insert(0, self);
                return list;
            }

            static bool IsAvatarRootMenu(UnityEngine.Object menu, UnityEngine.Object source)
            {
                if (menu == null)
                    return false;
                if (MatchesAvatarMenu(source, menu))
                    return true;
                var descType = MenuRichTextUtility.VrcAvatarDescriptorType;
                if (descType == null)
                    return false;
                if (source is GameObject go && MatchesAvatarMenu(go.GetComponent(descType), menu))
                    return true;
                if (source is Component component && MatchesAvatarMenu(component.GetComponent(descType), menu))
                    return true;
                return false;
            }

            static bool MatchesAvatarMenu(UnityEngine.Object descriptor, UnityEngine.Object menu)
            {
                var descType = MenuRichTextUtility.VrcAvatarDescriptorType;
                if (descriptor == null || descType == null || !descType.IsInstanceOfType(descriptor))
                    return false;
                var so = new SerializedObject(descriptor);
                var prop = so.FindProperty("expressionsMenu");
                return prop != null && prop.objectReferenceValue == menu;
            }

            static string MaDisplayText(Component item, SerializedObject so)
            {
                var label = so.FindProperty("label");
                var text = label != null ? label.stringValue : "";
                if (!string.IsNullOrEmpty(text))
                    return text;
                return item != null ? item.gameObject.name : "";
            }

            static string ReadString(SerializedProperty parent, string relative)
            {
                if (parent == null)
                    return "";
                var child = parent.FindPropertyRelative(relative);
                return child != null ? child.stringValue ?? "" : "";
            }

            static int ReadInt(SerializedProperty parent, string relative)
            {
                if (parent == null)
                    return 0;
                var child = parent.FindPropertyRelative(relative);
                return child != null ? child.intValue : 0;
            }

            static Texture2D ReadTexture(SerializedProperty parent, string relative)
            {
                if (parent == null)
                    return null;
                var child = parent.FindPropertyRelative(relative);
                return child != null ? child.objectReferenceValue as Texture2D : null;
            }
        }

        /// <summary>
        /// ウィンドウ側のカスタムフォントは継承せず、エディタ標準のフォントを使う。
        /// </summary>
        static void UseDefaultFont(VisualElement element)
        {
            var font = EditorStyles.standardFont;
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                return;

            element.style.unityFont = font;
            element.style.unityFontDefinition = FontDefinition.FromFont(font);
        }

        /// <summary>
        /// Gesture Manager のメニュー文字と同じ基準（12px、絶対 size は 0.5 倍）でリッチテキストを描きます。
        /// </summary>
        sealed class RichTextLabel : VisualElement
        {
            readonly bool _centerOnBounds;
            float _appliedShift = float.NaN;

            RichTextLabel(float width, bool centerOnBounds)
            {
                _centerOnBounds = centerOnBounds;
                pickingMode = PickingMode.Ignore;
                style.flexDirection = FlexDirection.Row;
                style.flexWrap = Wrap.Wrap;
                style.justifyContent = Justify.Center;
                style.alignItems = Align.Center;
                style.overflow = Overflow.Visible;
                style.width = width;
                if (_centerOnBounds)
                    RegisterCallback<GeometryChangedEvent>(_ => CenterOnVisualBounds());
            }

            public static VisualElement Create(string text, float baseFont, float width, bool centerOnBounds = true)
            {
                var label = new RichTextLabel(width, centerOnBounds);
                label.Rebuild(text ?? "", baseFont);
                return label;
            }

            /// <summary>
            /// 押し上げたあとの見た目の縦範囲の中央を、親の top に合わせます。
            /// </summary>
            void CenterOnVisualBounds()
            {
                var min = float.MaxValue;
                var max = float.MinValue;
                foreach (var child in Children())
                {
                    if (!(child is TextRun run))
                        continue;
                    var y = run.layout.y;
                    var height = run.layout.height;
                    if (float.IsNaN(y) || float.IsNaN(height) || height <= 0f)
                        return;

                    var top = y - run.Lift;
                    var bottom = top + height;
                    if (top < min)
                        min = top;
                    if (bottom > max)
                        max = bottom;
                }

                if (min > max)
                    return;

                var shift = -((min + max) * 0.5f);
                if (!float.IsNaN(_appliedShift) && Mathf.Abs(shift - _appliedShift) < 0.05f)
                    return;

                _appliedShift = shift;
                style.translate = new Translate(new Length(0f), new Length(shift), 0f);
            }

            void Rebuild(string text, float baseFont)
            {
                Clear();
                var state = new RichState(baseFont);
                var i = 0;
                while (i < text.Length)
                {
                    if (text[i] == '<')
                    {
                        var end = text.IndexOf('>', i + 1);
                        if (end < 0)
                        {
                            AddRun(text.Substring(i), state);
                            break;
                        }

                        var tag = text.Substring(i + 1, end - i - 1);
                        if (tag.Trim().Equals("br", StringComparison.OrdinalIgnoreCase))
                            Add(LineBreak());
                        else
                            ApplyTag(tag, state, baseFont);
                        i = end + 1;
                        continue;
                    }

                    var next = text.IndexOf('<', i);
                    if (next < 0)
                        next = text.Length;
                    var slice = text.Substring(i, next - i);
                    var line = 0;
                    while (line <= slice.Length)
                    {
                        var br = slice.IndexOf('\n', line);
                        if (br < 0)
                        {
                            AddRun(slice.Substring(line), state);
                            break;
                        }

                        AddRun(slice.Substring(line, br - line), state);
                        Add(LineBreak());
                        line = br + 1;
                        if (line == slice.Length)
                            break;
                    }

                    i = next;
                }

                if (childCount == 0)
                    AddRun(" ", state);

                // 後ろの voffset が、その分だけ前の文字を押し上げる
                ApplyVOffsetPush();
            }

            void ApplyVOffsetPush()
            {
                var runs = new List<TextRun>();
                foreach (var child in Children())
                {
                    if (child is TextRun run)
                        runs.Add(run);
                }

                var below = 0f;
                for (var i = runs.Count - 1; i >= 0; i--)
                {
                    var own = runs[i].OwnVOffset;
                    runs[i].Lift = own + below;
                    runs[i].style.translate = new Translate(new Length(0f), new Length(-runs[i].Lift), 0f);
                    below += own;
                }
            }

            void AddRun(string text, RichState state)
            {
                if (string.IsNullOrEmpty(text))
                    return;

                var run = new TextRun();
                run.text = text;
                run.pickingMode = PickingMode.Ignore;
                run.style.fontSize = state.Font;
                run.style.color = state.Color;
                run.style.unityFontStyleAndWeight = state.Style;
                run.style.unityTextAlign = TextAnchor.MiddleCenter;
                run.style.whiteSpace = WhiteSpace.Normal;
                run.style.marginLeft = 0f;
                run.style.marginRight = 0f;
                run.style.marginTop = 0f;
                run.style.marginBottom = 0f;
                run.style.paddingLeft = 0f;
                run.style.paddingRight = 0f;
                run.style.paddingTop = 0f;
                run.style.paddingBottom = 0f;
                if (state.Mark.a > 0.01f)
                {
                    run.style.backgroundColor = state.Mark;
                    run.style.paddingLeft = 1f;
                    run.style.paddingRight = 1f;
                }

                run.OwnVOffset = state.VOffset;
                Add(run);
            }

            sealed class TextRun : Label
            {
                public float OwnVOffset;
                public float Lift;
            }

            static VisualElement LineBreak()
            {
                var br = new VisualElement();
                br.pickingMode = PickingMode.Ignore;
                br.style.width = Length.Percent(100);
                br.style.height = 0f;
                return br;
            }

            static void ApplyTag(string tag, RichState state, float baseFont)
            {
                tag = (tag ?? "").Trim();
                if (tag.Length == 0)
                    return;

                var close = tag[0] == '/';
                var body = close ? tag.Substring(1).Trim() : tag;
                var eq = body.IndexOf('=');
                var name = (eq >= 0 ? body.Substring(0, eq) : body).Trim().ToLowerInvariant();
                var attr = eq >= 0 ? body.Substring(eq + 1).Trim() : "";

                switch (name)
                {
                    case "b":
                        if (close) state.Bold = Mathf.Max(0, state.Bold - 1);
                        else state.Bold++;
                        break;
                    case "i":
                        if (close) state.Italic = Mathf.Max(0, state.Italic - 1);
                        else state.Italic++;
                        break;
                    case "color":
                        if (close) state.PopColor();
                        else if (TryParseColor(attr, out var color)) state.PushColor(color);
                        break;
                    case "mark":
                        if (close) state.PopMark();
                        else if (TryParseColor(attr, out var mark)) state.PushMark(mark);
                        break;
                    case "size":
                        if (close) state.PopFont();
                        else if (TryParseSize(attr, baseFont, out var px)) state.PushFont(px);
                        break;
                    case "sup":
                    case "sub":
                        if (close) state.PopFont();
                        else state.PushFont(4f);
                        break;
                    case "voffset":
                        if (close) state.PopVOffset();
                        else state.PushVOffset(ParseVOffset(attr, state.Font));
                        break;
                }
            }

            static bool TryParseSize(string attr, float baseFont, out float px)
            {
                px = baseFont;
                attr = (attr ?? "").Trim();
                if (attr.Length == 0)
                    return false;
                if (attr.EndsWith("%", StringComparison.Ordinal))
                {
                    if (!TryFloat(attr.Substring(0, attr.Length - 1), out var percent))
                        return false;
                    px = baseFont * percent / 100f;
                    return true;
                }

                if (attr.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                    return TryFloat(attr.Substring(0, attr.Length - 2), out px);
                if (attr.EndsWith("em", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryFloat(attr.Substring(0, attr.Length - 2), out var em))
                        return false;
                    px = em * baseFont;
                    return true;
                }

                if (!TryFloat(attr, out var absolute))
                    return false;
                // Gesture Manager は単位なし size を DefaultScale 0.5 倍で描く
                px = absolute * 0.5f;
                return true;
            }

            static float ParseVOffset(string attr, float font)
            {
                attr = (attr ?? "").Trim();
                var em = attr.EndsWith("em", StringComparison.OrdinalIgnoreCase);
                var px = attr.EndsWith("px", StringComparison.OrdinalIgnoreCase);
                if (em)
                    attr = attr.Substring(0, attr.Length - 2);
                else if (px)
                    attr = attr.Substring(0, attr.Length - 2);
                if (!TryFloat(attr, out var value))
                    return 0f;
                // VRChat のメニュー文字と若干vOffsetの移動量がことなるので補正する。
                const float worldScale = 0.75f;
                if (px)
                    return value * worldScale;
                return value * font * worldScale;
            }

            static bool TryFloat(string raw, out float value)
            {
                return float.TryParse((raw ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            static bool TryParseColor(string attr, out Color color)
            {
                color = Color.white;
                attr = (attr ?? "").Trim();
                if (attr.Length == 0)
                    return false;
                switch (attr.ToLowerInvariant())
                {
                    case "red": color = Color.red; return true;
                    case "blue": color = Color.blue; return true;
                    case "white": color = Color.white; return true;
                    case "black": color = Color.black; return true;
                    case "green": color = Color.green; return true;
                    case "orange": color = new Color(1f, 0.5f, 0f); return true;
                    case "yellow": color = new Color(1f, 0.92f, 0f); return true;
                    case "purple": color = new Color(0.63f, 0.13f, 0.94f); return true;
                }

                if (attr[0] != '#')
                    attr = "#" + attr;
                return ColorUtility.TryParseHtmlString(attr, out color);
            }

            sealed class RichState
            {
                public int Bold;
                public int Italic;
                readonly float _base;
                readonly Stack<float> _fonts = new Stack<float>();
                readonly Stack<Color> _colors = new Stack<Color>();
                readonly Stack<Color> _marks = new Stack<Color>();
                readonly Stack<float> _offsets = new Stack<float>();

                public RichState(float baseFont)
                {
                    _base = baseFont;
                }

                public float Font => _fonts.Count > 0 ? _fonts.Peek() : _base;
                public Color Color => _colors.Count > 0 ? _colors.Peek() : LabelColor;
                public Color Mark => _marks.Count > 0 ? _marks.Peek() : new Color(0f, 0f, 0f, 0f);
                public float VOffset => _offsets.Count > 0 ? _offsets.Peek() : 0f;

                public FontStyle Style
                {
                    get
                    {
                        if (Bold > 0 && Italic > 0)
                            return FontStyle.BoldAndItalic;
                        if (Bold > 0)
                            return FontStyle.Bold;
                        if (Italic > 0)
                            return FontStyle.Italic;
                        return FontStyle.Normal;
                    }
                }

                public void PushFont(float px) => _fonts.Push(px);
                public void PopFont() { if (_fonts.Count > 0) _fonts.Pop(); }
                public void PushColor(Color color) => _colors.Push(color);
                public void PopColor() { if (_colors.Count > 0) _colors.Pop(); }
                public void PushMark(Color color) => _marks.Push(color);
                public void PopMark() { if (_marks.Count > 0) _marks.Pop(); }
                public void PushVOffset(float px) => _offsets.Push(px);
                public void PopVOffset() { if (_offsets.Count > 0) _offsets.Pop(); }
            }
        }
    }
}
