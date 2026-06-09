using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Samirin33.AvatarEditor.Tools.Editor
{
    /// <summary>
    /// Transition Dest 用: 子ノードのズーム・スクロールグラフ。
    /// </summary>
    internal struct TransitionDestGraphNode
    {
        public string shortLabel;
        public Vector3 graphPosition;
        public bool hasGraphPosition;
        public AnimatorState targetState;
        public AnimatorStateMachine targetStateMachine;

        public bool IsStateMachine => targetStateMachine != null;
        public Object AsObject => IsStateMachine ? targetStateMachine : targetState;
    }

    internal static class TransitionDestGraphCanvas
    {
        private const float BaseNodeW = 250f;
        private const float BaseNodeH = 80f;
        private const float PixelsPerWorldUnitAtZoom1 = 2.2f;
        private const float ScreenPad = 28f;

        /// <summary>
        /// <paramref name="zoom"/> が 0 以下のとき、初回のみビューポートに全体が収まる倍率を計算して代入する。
        /// </summary>
        internal static void Draw(
            ref Vector2 scroll,
            ref float zoom,
            IReadOnlyList<TransitionDestGraphNode> nodes,
            ref Object chosenHighlight,
            System.Action<AnimatorStateMachine> onDrillIntoStateMachine,
            System.Action<AnimatorState> onPickState,
            Vector2 fitViewportPixels,
            params GUILayoutOption[] layoutOptions)
        {
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            var hasAny = false;
            for (var i = 0; i < nodes.Count; i++)
            {
                var r = nodes[i];
                if (!r.hasGraphPosition)
                    continue;
                hasAny = true;
                var p = new Vector2(r.graphPosition.x, r.graphPosition.y);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }

            if (!hasAny)
            {
                EditorGUILayout.HelpBox("座標がないノードです。", MessageType.Info);
                return;
            }

            var span = max - min;
            if (span.x < 40f)
                span.x = 40f;
            if (span.y < 40f)
                span.y = 40f;

            var vw = Mathf.Max(160f, fitViewportPixels.x);
            var vh = Mathf.Max(120f, fitViewportPixels.y);

            if (zoom <= 0f || float.IsNaN(zoom) || float.IsInfinity(zoom))
                zoom = ComputeInitialFitZoom(span, vw, vh);

            var z = Mathf.Clamp(zoom, 0.12f, 5f);
            zoom = z;

            BuildLayout(nodes, min, max, z, out var localRects, out var maxRight, out var maxBottom, out var pxPerUnit,
                out var nodeW, out var nodeH);

            var slack = 80f + nodes.Count * 24f;
            var contentW = Mathf.Max(maxRight + ScreenPad + slack, 200f);
            var contentH = Mathf.Max(maxBottom + ScreenPad + slack, 160f);
            const float zMaxLayout = 5f;
            var upperW = ScreenPad * 2f + span.x * PixelsPerWorldUnitAtZoom1 * zMaxLayout + BaseNodeW * zMaxLayout +
                nodes.Count * 120f + 400f;
            var upperH = ScreenPad * 2f + span.y * PixelsPerWorldUnitAtZoom1 * zMaxLayout + BaseNodeH * zMaxLayout +
                nodes.Count * 100f + 400f;
            contentW = Mathf.Max(contentW, upperW);
            contentH = Mathf.Max(contentH, upperH);

            var viewH = Mathf.Max(120f, fitViewportPixels.y);
            var viewport = GUILayoutUtility.GetRect(0f, viewH, layoutOptions);
            var contentRect = new Rect(0f, 0f, contentW, contentH);

            var e = Event.current;
            if (viewport.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseDrag && e.button == 2)
                {
                    scroll.x -= e.delta.x;
                    scroll.y -= e.delta.y;
                    e.Use();
                }

                if (e.type == EventType.MouseDown && e.button == 2)
                    e.Use();
            }

            if (e.type == EventType.ScrollWheel && viewport.Contains(e.mousePosition))
            {
                var zOld = Mathf.Clamp(zoom, 0.12f, 5f);
                var zNew = Mathf.Clamp(zoom - e.delta.y * 0.06f, 0.12f, 5f);
                if (!Mathf.Approximately(zOld, zNew))
                {
                    var mouseOff = (Vector2)e.mousePosition - viewport.position;
                    var doc = scroll + mouseOff;
                    var px0 = PixelsPerWorldUnitAtZoom1 * zOld;
                    var worldX = min.x + (doc.x - ScreenPad) / px0;
                    var worldY = max.y - (doc.y - ScreenPad) / px0;
                    var px1 = PixelsPerWorldUnitAtZoom1 * zNew;
                    var docNew = new Vector2(
                        ScreenPad + (worldX - min.x) * px1,
                        ScreenPad + (max.y - worldY) * px1);
                    scroll = docNew - mouseOff;
                    zoom = zNew;
                    z = zNew;
                    BuildLayout(nodes, min, max, z, out localRects, out maxRight, out maxBottom, out pxPerUnit,
                        out nodeW, out nodeH);
                    contentW = Mathf.Max(maxRight + ScreenPad + slack, 200f, upperW);
                    contentH = Mathf.Max(maxBottom + ScreenPad + slack, 160f, upperH);
                    contentRect = new Rect(0f, 0f, contentW, contentH);
                }

                e.Use();
            }

            EditorGUIUtility.AddCursorRect(viewport, MouseCursor.Pan);

            scroll = GUI.BeginScrollView(viewport, scroll, contentRect);

            EditorGUI.DrawRect(new Rect(0f, 0f, contentW, contentH), EditorGUIUtility.isProSkin
                ? new Color(0.11f, 0.11f, 0.12f, 1f)
                : new Color(0.93f, 0.93f, 0.94f, 1f));

            for (var i = 0; i < nodes.Count; i++)
            {
                var row = nodes[i];
                if (!row.hasGraphPosition || i >= localRects.Count)
                    continue;

                var lr = localRects[i];
                if (lr.width <= 0f)
                    continue;

                var r = new Rect(lr.x, lr.y, lr.width, lr.height);

                var isChosen = chosenHighlight != null && row.AsObject != null &&
                               ReferenceEquals(chosenHighlight, row.AsObject);
                var bg = isChosen
                    ? new Color(0.45f, 0.78f, 1f, 0.55f)
                    : row.IsStateMachine
                        ? new Color(0.32f, 0.52f, 0.88f, 0.42f)
                        : new Color(0.42f, 0.78f, 0.48f, 0.38f);
                EditorGUI.DrawRect(r, bg);

                var label = row.shortLabel ?? "";
                var maxChars = Mathf.Max(4, Mathf.FloorToInt(nodeW / 7f));
                if (label.Length > maxChars)
                    label = label.Substring(0, Mathf.Max(1, maxChars - 1)) + "…";

                if (GUI.Button(r, label, EditorStyles.miniButton))
                {
                    if (row.IsStateMachine && row.targetStateMachine != null)
                        onDrillIntoStateMachine?.Invoke(row.targetStateMachine);
                    else if (!row.IsStateMachine && row.targetState != null)
                        onPickState?.Invoke(row.targetState);

                    e.Use();
                }
            }

            GUI.EndScrollView();
        }

        private static float ComputeInitialFitZoom(Vector2 span, float viewportW, float viewportH)
        {
            var innerW = Mathf.Max(80f, viewportW - ScreenPad * 2f - 32f);
            var innerH = Mathf.Max(60f, viewportH - ScreenPad * 2f - 32f);
            var k = PixelsPerWorldUnitAtZoom1;
            var zx = innerW / (span.x * k + BaseNodeW + 1f);
            var zy = innerH / (span.y * k + BaseNodeH + 1f);
            return Mathf.Clamp(Mathf.Min(zx, zy) * 0.9f, 0.12f, 5f);
        }

        private static void BuildLayout(
            IReadOnlyList<TransitionDestGraphNode> nodes,
            Vector2 min,
            Vector2 max,
            float z,
            out List<Rect> localRects,
            out float maxRight,
            out float maxBottom,
            out float pxPerUnit,
            out float nodeW,
            out float nodeH)
        {
            var zz = Mathf.Clamp(z, 0.12f, 5f);
            pxPerUnit = PixelsPerWorldUnitAtZoom1 * zz;
            nodeW = Mathf.Max(36f, BaseNodeW * zz);
            nodeH = Mathf.Max(14f, BaseNodeH * zz);

            localRects = new List<Rect>(nodes.Count);
            var placed = new List<Rect>();
            maxRight = ScreenPad;
            maxBottom = ScreenPad;

            for (var i = 0; i < nodes.Count; i++)
            {
                var row = nodes[i];
                if (!row.hasGraphPosition)
                {
                    localRects.Add(default);
                    continue;
                }

                var p = new Vector2(row.graphPosition.x, row.graphPosition.y);
                var lx = ScreenPad + (p.x - min.x) * pxPerUnit;
                var ly = ScreenPad + (max.y - p.y) * pxPerUnit;
                var rr = new Rect(lx, ly, nodeW, nodeH);

                for (var k = 0; k < 12; k++)
                {
                    var overlap = false;
                    foreach (var pr in placed)
                    {
                        if (pr.Overlaps(rr))
                        {
                            overlap = true;
                            break;
                        }
                    }

                    if (!overlap)
                        break;
                    rr.y += Mathf.Max(4f, nodeH * 0.35f);
                    rr.x += (i % 2 == 0 ? 1f : -1f) * Mathf.Max(6f, nodeW * 0.12f);
                }

                placed.Add(rr);
                localRects.Add(rr);
                maxRight = Mathf.Max(maxRight, rr.xMax);
                maxBottom = Mathf.Max(maxBottom, rr.yMax);
            }
        }

        /// <summary>
        /// <paramref name="sm"/> の、<paramref name="limitRoot"/> ツリー内における直上の親ステートマシン（無ければ null）。
        /// </summary>
        internal static AnimatorStateMachine FindImmediateParentInSubtree(
            AnimatorStateMachine sm,
            AnimatorStateMachine limitRoot)
        {
            if (sm == null || limitRoot == null || ReferenceEquals(sm, limitRoot))
                return null;

            AnimatorStateMachine found = null;
            void Walk(AnimatorStateMachine parent)
            {
                if (parent == null || found != null)
                    return;
                foreach (var ch in parent.stateMachines)
                {
                    if (ch.stateMachine == null)
                        continue;
                    if (ReferenceEquals(ch.stateMachine, sm))
                    {
                        found = parent;
                        return;
                    }

                    Walk(ch.stateMachine);
                }
            }

            Walk(limitRoot);
            return found;
        }

        internal static string BuildBreadcrumbPath(AnimatorStateMachine root, AnimatorStateMachine current)
        {
            if (current == null)
                return "表示: —";

            var parts = new List<string>();
            var c = current;
            var guard = 0;
            while (c != null && guard++ < 64)
            {
                parts.Add(c.name);
                if (ReferenceEquals(c, root))
                    break;
                c = FindImmediateParentInSubtree(c, root);
            }

            parts.Reverse();
            return "表示: " + string.Join(" / ", parts);
        }
    }
}
