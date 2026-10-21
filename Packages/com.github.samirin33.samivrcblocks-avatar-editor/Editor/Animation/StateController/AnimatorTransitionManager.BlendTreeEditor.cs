using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Samirin33.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;
using AnimatorController = UnityEditor.Animations.AnimatorController;
using AnimatorControllerParameter = UnityEngine.AnimatorControllerParameter;
using AnimatorControllerParameterType = UnityEngine.AnimatorControllerParameterType;
using BlendTree = UnityEditor.Animations.BlendTree;

namespace Samirin33.AvatarEditor.Tools.Editor
{
    public sealed partial class AnimatorTransitionManager : EditorWindow
    {
        // BlendTree エディタ（インスペクタ表示・コピー/ペースト・内包⇔アセット変換）

        // ---- 静的状態 ----

        private static BlendTree _btEditorPinnedTarget;
        private static readonly Dictionary<int, ReorderableList> _btChildLists = new Dictionary<int, ReorderableList>();
        private static readonly Dictionary<int, List<ChildMotion>> _btChildBuffers = new Dictionary<int, List<ChildMotion>>();

        private struct BtPreview1DState
        {
            public bool isCustom;
            public float tMin;
            public float tMax;
        }

        private struct BtPreview2DState
        {
            public bool isCustom;
            public float minX, maxX, minY, maxY;
        }

        private static readonly Dictionary<int, BtPreview1DState> _btPreview1D = new Dictionary<int, BtPreview1DState>();
        private static readonly Dictionary<int, BtPreview2DState> _btPreview2D = new Dictionary<int, BtPreview2DState>();
        private static readonly Dictionary<int, int> _btPreview1DDataSig = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> _btPreview2DDataSig = new Dictionary<int, int>();
        // _btChildClipboard / _btChildClipboardHasValue は _btClipboard に統合済み

        /// <summary>プレビュー上をドラッグしたときの座標のスナップ（0 以下でスナップなし）</summary>
        private static float _btPreviewSnapStep = 0.5f;

        private enum BtPointDragMode { None, OneDChild, TwoDChild, OneDCurrent, TwoDCurrent }

        private static int _btPointDragTargetId = -1;
        private static int _btPointDragChildIndex = -1;
        private static BtPointDragMode _btPointDragMode;
        private static readonly Dictionary<int, Vector2> _btCurrentInputFallback = new Dictionary<int, Vector2>();

        private static readonly string[] _btTypeLabels =
        {
            "Simple 1D", "Simple Directional 2D", "Freeform Directional 2D", "Freeform Cartesian 2D", "Direct"
        };

        private static readonly BlendTreeType[] _btTypeValues =
        {
            BlendTreeType.Simple1D,
            BlendTreeType.SimpleDirectional2D,
            BlendTreeType.FreeformDirectional2D,
            BlendTreeType.FreeformCartesian2D,
            BlendTreeType.Direct
        };

        // ======================== メイン描画 ========================

        private void DrawSelectedBlendTreeEditor()
        {
            var bt = BtEditor_GetCurrentTargetBlendTree();
            if (bt == null)
                return;

            var assetPath = AssetDatabase.GetAssetPath(bt);
            if (string.IsNullOrEmpty(assetPath))
                return;

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
            var isEmbedded = controller != null;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));

            // ---- ヘッダー行（コピー/ペースト） ----
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("BlendTree", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("複製コピー", GUILayout.Width(80f)))
                    BtEditor_CopyBlendTree(bt, BtCopyMode.Clone);
                if (GUILayout.Button("リンクコピー", GUILayout.Width(88f)))
                    BtEditor_CopyBlendTree(bt, BtCopyMode.Link);
                EditorGUI.BeginDisabledGroup(!_btClipboard.HasData);
                if (GUILayout.Button("ペースト", GUILayout.Width(68f)))
                    BtEditor_PasteUnified(bt, controller, assetPath);
                EditorGUI.EndDisabledGroup();
            }
            // ---- クリップボード情報表示 ----
            BtEditor_DrawClipboardInfo();

            // ---- 変換行（内包⇔アセット） ----
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (isEmbedded)
                {
                    if (GUILayout.Button("アセットファイルに抽出", GUILayout.Width(160f)))
                        BtEditor_ExtractToAsset(bt, controller);
                }
                else
                {
                    if (BtEditor_IsEmbeddedSubAsset(bt))
                    {
                        var hasParentInAsset = BtEditor_TryFindAssetParentBlendTreeInSameAsset(bt, out var parentInAsset);
                        using (new EditorGUI.DisabledScope(!hasParentInAsset))
                        {
                            if (GUILayout.Button("自身をアセット化", GUILayout.Width(170f)))
                                BtEditor_ExtractEmbeddedToExternalAsset(bt, parentInAsset);
                        }
                    }
                    else if (BtEditor_IsMainBlendTreeAsset(bt))
                    {
                        var hasAssetParent = BtEditor_TryFindAnyAssetParentBlendTreeCached(bt, out _);
                        using (new EditorGUI.DisabledScope(!hasAssetParent))
                        {
                            if (GUILayout.Button("親に内包", GUILayout.Width(150f)))
                                BtEditor_EmbedIntoParentBlendTreeAsset(bt);
                        }
                    }
                    else if (GUILayout.Button("コントローラに内包", GUILayout.Width(150f)))
                    {
                        BtEditor_EmbedIntoController(bt);
                    }
                }
            }

            EditorGUILayout.Space(4f);

            // ---- 基本フィールド ----
            var floatParams = BtEditor_CollectParameterNames(controller, AnimatorControllerParameterType.Float);
            var allParams = BtEditor_IsLikelyBlendShapeContext(bt)
                ? BtEditor_CollectParameterNames(controller, AnimatorControllerParameterType.Float)
                : BtEditor_CollectParameterNames(controller);

            EditorGUI.BeginChangeCheck();

            var newName = EditorGUILayout.TextField("名前", bt.name);

            var curTypeIdx = Array.IndexOf(_btTypeValues, bt.blendType);
            if (curTypeIdx < 0) curTypeIdx = 0;
            var newTypeIdx = EditorGUILayout.Popup("Blend Type", curTypeIdx, _btTypeLabels);
            var newType = _btTypeValues[newTypeIdx];

            var newParam = bt.blendParameter ?? "";
            var newParamY = bt.blendParameterY ?? "";

            if (newType == BlendTreeType.Simple1D)
            {
                newParam = BtEditor_ParamPopup("Parameter", bt.blendParameter, floatParams);
            }
            else if (newType != BlendTreeType.Direct)
            {
                newParam = BtEditor_ParamPopup("Parameter X", bt.blendParameter, floatParams);
                newParamY = BtEditor_ParamPopup("Parameter Y", bt.blendParameterY, floatParams);
            }

            var newMin = bt.minThreshold;
            var newMax = bt.maxThreshold;
            var newAutoThresh = bt.useAutomaticThresholds;
            if (newType == BlendTreeType.Simple1D)
            {
                newAutoThresh = EditorGUILayout.Toggle("Compute Thresholds", bt.useAutomaticThresholds);
                if (!newAutoThresh)
                {
                    newMin = EditorGUILayout.FloatField("Min Threshold", bt.minThreshold);
                    newMax = EditorGUILayout.FloatField("Max Threshold", bt.maxThreshold);
                }
            }

            var hasNormalizedBlendProp = BtEditor_TryGetNormalizedBlendValues(bt, out var currentNorm);
            var newNorm = currentNorm;
            if (newType != BlendTreeType.Direct)
            {
                if (hasNormalizedBlendProp)
                    newNorm = EditorGUILayout.Toggle("Normalize Blend Values", currentNorm);
                else
                    EditorGUILayout.LabelField("Normalize Blend Values", "(このUnityバージョンでは未対応)");
            }

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(bt, "Edit BlendTree");
                bt.name = newName;
                bt.blendType = newType;
                bt.blendParameter = newParam;
                bt.blendParameterY = newParamY;
                bt.minThreshold = newMin;
                bt.maxThreshold = newMax;
                bt.useAutomaticThresholds = newAutoThresh;
                if (hasNormalizedBlendProp && !Mathf.Approximately(newNorm ? 1f : 0f, currentNorm ? 1f : 0f))
                    BtEditor_SetNormalizedBlendValues(bt, newNorm);
                EditorUtility.SetDirty(bt);
                if (controller != null) EditorUtility.SetDirty(controller);
                InternalEditorUtility.RepaintAllViews();
            }

            // ---- Children ----
            EditorGUILayout.Space(6f);
            BtEditor_DrawChildren(bt, controller, assetPath, newType, allParams);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8f);
        }

        private static BlendTree BtEditor_GetCurrentTargetBlendTree()
        {
            var active = Selection.activeObject;
            if (active is BlendTree selectedBt)
            {
                _btEditorPinnedTarget = selectedBt;
                return selectedBt;
            }

            if (active is AnimatorState
                || active is AnimatorStateMachine
                || active is AnimatorTransitionBase
                || active is AnimatorController)
            {
                _btEditorPinnedTarget = null;
                return null;
            }

            if (_btEditorPinnedTarget == null)
                return null;

            return _btEditorPinnedTarget;
        }

        /// <summary>子モーション一覧の上に、1D では横軸上の配置、2D では (posX, posY) の空間配置を可視化する。</summary>
        private static void BtEditor_DrawChildrenBlendSpacePreview(
            BlendTree bt, BlendTreeType blendType, List<ChildMotion> children, AnimatorController controller,
            ReorderableList childList, bool hasCurrent, float currentX, float currentY)
        {
            BtEditor_FinishBlendPointDragOnMouseUp();

            if (bt == null || children == null || children.Count == 0)
                return;

            if (blendType == BlendTreeType.Simple1D)
            {
                BtEditor_DrawChildrenBlend1DPreview(bt, children, controller, childList, hasCurrent, ref currentX);
            }
            else if (blendType == BlendTreeType.SimpleDirectional2D
                     || blendType == BlendTreeType.FreeformDirectional2D
                     || blendType == BlendTreeType.FreeformCartesian2D)
            {
                BtEditor_DrawChildrenBlend2DPreview(bt, children, controller, childList, hasCurrent, ref currentX, ref currentY);
            }
        }

        private static int BtEditor_GetChildListSelectedIndex(ReorderableList list, int childCount)
        {
            if (list == null || childCount <= 0) return -1;
            if (list.index < 0 || list.index >= childCount) return -1;
            return list.index;
        }

        private static void BtEditor_FinishBlendPointDragOnMouseUp()
        {
            var e = Event.current;
            if (e == null || e.type != EventType.MouseUp || e.button != 0)
                return;
            if (_btPointDragMode == BtPointDragMode.None)
                return;
            _btPointDragMode = BtPointDragMode.None;
            _btPointDragTargetId = -1;
            _btPointDragChildIndex = -1;
        }

        private static float BtEditor_SnapCoordinate(float v, float step) =>
            step <= 0f ? v : Mathf.Round(v / step) * step;

        private static bool BtEditor_IsViewFrozenForPointDrag(int key) =>
            key != 0 && _btPointDragMode != BtPointDragMode.None && _btPointDragTargetId == key;

        /// <summary>スナップが 0 のときは可視域に合わせたおおよその目盛り</summary>
        private static float BtEditor_GetPreviewGridStep(float vMin, float vMax, float userSnap)
        {
            if (userSnap > 0f) return userSnap;
            var span = Mathf.Max(1e-6f, vMax - vMin);
            return Mathf.Max(1e-4f, span / 12f);
        }

        private static bool BtEditor_PreviewDataIsOnIntegerLine(float tG) =>
            Mathf.Abs(tG - Mathf.Round(tG)) < 0.0001f;

        private static bool BtEditor_TryGetBlendInputPoint(BlendTree bt, out float x, out float y)
        {
            x = 0f;
            y = 0f;
            if (bt == null)
                return false;
            var btId = bt.GetInstanceID();
            if (!_btCurrentInputFallback.TryGetValue(btId, out var cur))
            {
                cur = Vector2.zero;
                _btCurrentInputFallback[btId] = cur;
            }
            x = cur.x;
            y = cur.y;

            if (bt.blendType == BlendTreeType.Simple1D)
                return !string.IsNullOrEmpty(bt.blendParameter);
            if (bt.blendType == BlendTreeType.Direct)
                return false;
            var haveX = !string.IsNullOrEmpty(bt.blendParameter);
            var haveY = !string.IsNullOrEmpty(bt.blendParameterY);
            if (!haveX && !haveY)
                return false;
            return true;
        }

        private static void BtEditor_SetBlendInputPoint(BlendTree bt, float x, float y)
        {
            if (bt == null)
                return;
            _btCurrentInputFallback[bt.GetInstanceID()] = new Vector2(x, y);
            EditorUtility.SetDirty(bt);
        }

        private static void BtEditor_DrawBlendCurrentMarkerGui(Vector2 center, float halfExtent)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            var fill = new Color(1f, 0.92f, 0.15f, 1f);
            var b = new Color(0.15f, 0.12f, 0.02f, 1f);
            var s = halfExtent * 2f;
            var r = new Rect(center.x - halfExtent, center.y - halfExtent, s, s);
            EditorGUI.DrawRect(r, fill);
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1f), b);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), b);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1f, r.height), b);
            EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), b);
        }

        private static void BtEditor_NormalizeWeights(float[] w)
        {
            if (w == null || w.Length == 0) return;
            var sum = 0f;
            for (var i = 0; i < w.Length; i++) sum += Mathf.Max(0f, w[i]);
            if (sum <= 1e-6f)
            {
                var u = 1f / w.Length;
                for (var i = 0; i < w.Length; i++) w[i] = u;
                return;
            }
            var inv = 1f / sum;
            for (var i = 0; i < w.Length; i++) w[i] = Mathf.Max(0f, w[i]) * inv;
        }

        private static float[] BtEditor_ComputeWeights1D(IReadOnlyList<ChildMotion> children, float x)
        {
            var n = children?.Count ?? 0;
            var w = new float[n];
            if (n == 0) return w;
            var idx = Enumerable.Range(0, n).ToArray();
            Array.Sort(idx, (a, b) => children[a].threshold.CompareTo(children[b].threshold));
            if (n == 1)
            {
                w[0] = 1f;
                return w;
            }
            var t0 = children[idx[0]].threshold;
            if (x <= t0)
            {
                w[idx[0]] = 1f;
                return w;
            }
            var tN = children[idx[n - 1]].threshold;
            if (x >= tN)
            {
                w[idx[n - 1]] = 1f;
                return w;
            }
            for (var i = 0; i < n - 1; i++)
            {
                var i0 = idx[i];
                var i1 = idx[i + 1];
                var a = children[i0].threshold;
                var b = children[i1].threshold;
                if (x < a || x > b) continue;
                var span = Mathf.Max(1e-6f, b - a);
                var t = Mathf.Clamp01((x - a) / span);
                w[i0] = 1f - t;
                w[i1] = t;
                return w;
            }
            w[idx[n - 1]] = 1f;
            return w;
        }

        private static float[] BtEditor_ComputeWeights2D(BlendTreeType type, IReadOnlyList<ChildMotion> children, Vector2 p)
        {
            var n = children?.Count ?? 0;
            var w = new float[n];
            if (n == 0) return w;
            switch (type)
            {
                case BlendTreeType.FreeformCartesian2D:
                    for (var i = 0; i < n; i++)
                    {
                        var d = Vector2.Distance(children[i].position, p);
                        w[i] = 1f / (d * d + 0.0001f);
                    }
                    break;
                case BlendTreeType.SimpleDirectional2D:
                case BlendTreeType.FreeformDirectional2D:
                    {
                        var pMag = p.magnitude;
                        var pDir = pMag > 1e-6f ? p / pMag : Vector2.zero;
                        for (var i = 0; i < n; i++)
                        {
                            var c = children[i].position;
                            var cMag = c.magnitude;
                            if (cMag <= 1e-6f)
                            {
                                w[i] = Mathf.Clamp01(1f - pMag);
                                continue;
                            }
                            var cDir = c / cMag;
                            var dot = Mathf.Clamp01((Vector2.Dot(cDir, pDir) + 1f) * 0.5f);
                            var radial = type == BlendTreeType.SimpleDirectional2D
                                ? 1f / (Mathf.Abs(cMag - pMag) + 0.35f)
                                : Mathf.Exp(-Mathf.Abs(cMag - pMag) * 1.5f);
                            w[i] = dot * radial;
                        }
                    }
                    break;
                default:
                    for (var i = 0; i < n; i++) w[i] = 1f / n;
                    break;
            }
            BtEditor_NormalizeWeights(w);
            return w;
        }

        private static void BtEditor_Draw1DDataGrid(
            Rect r, float tMin, float tMax, float innerL, float y0, float y1)
        {
            if (tMax < tMin) (tMin, tMax) = (tMax, tMin);
            var w = r.width - innerL * 2f;
            if (w < 1e-3f) return;
            var invSpan = 1f / Mathf.Max(1e-7f, tMax - tMin);
            var step = BtEditor_GetPreviewGridStep(tMin, tMax, _btPreviewSnapStep);
            if (step <= 0f) return;
            var cMinor = new Color(0.28f, 0.5f, 0.5f, 0.2f);
            var cInt = new Color(0.45f, 0.65f, 0.85f, 0.38f);
            if (EditorGUIUtility.isProSkin) { cMinor.a = 0.22f; cInt = new Color(0.4f, 0.6f, 0.8f, 0.4f); }
            var m0 = Mathf.CeilToInt(tMin / step - 0.0001f);
            var m1 = Mathf.FloorToInt(tMax / step + 0.0001f);
            const int maxLines = 400;
            if (m1 - m0 > maxLines) m1 = m0 + maxLines;
            for (var m = m0; m <= m1; m++)
            {
                var tG = m * step;
                if (tG < tMin - 1e-4f || tG > tMax + 1e-4f) continue;
                var u = (tG - tMin) * invSpan;
                if (u < 0f || u > 1f) continue;
                var cx = r.x + innerL + u * w;
                var col = BtEditor_PreviewDataIsOnIntegerLine(tG) ? cInt : cMinor;
                EditorGUI.DrawRect(new Rect(cx - 0.5f, y0, 1f, y1 - y0), col);
            }
        }

        private static float BtEditor_GetGridStep2D(float minX, float maxX, float minY, float maxY) =>
            _btPreviewSnapStep > 0f
                ? _btPreviewSnapStep
                : Mathf.Max(1e-4f, Mathf.Max(maxX - minX, maxY - minY) / 12f);

        private static void BtEditor_Draw2DDataGrid(
            Rect plot, float minX, float maxX, float minY, float maxY, float invX, float invY, float zGrid)
        {
            if (zGrid <= 0f) return;
            var cMinor = new Color(0.28f, 0.5f, 0.5f, 0.18f);
            var cInt = new Color(0.4f, 0.6f, 0.8f, 0.3f);
            if (EditorGUIUtility.isProSkin) { cMinor.a = 0.2f; cInt = new Color(0.4f, 0.6f, 0.8f, 0.32f); }
            // 垂直: x = m*step
            {
                var m0 = Mathf.CeilToInt(minX / zGrid - 0.0001f);
                var m1 = Mathf.FloorToInt(maxX / zGrid + 0.0001f);
                if (m1 - m0 > 400) m1 = m0 + 400;
                for (var m = m0; m <= m1; m++)
                {
                    var xD = m * zGrid;
                    if (xD < minX - 1e-4f || xD > maxX + 1e-4f) continue;
                    var sx = plot.x + (xD - minX) * invX * plot.width;
                    if (sx < plot.x - 0.5f || sx > plot.xMax + 0.5f) continue;
                    var isInt = BtEditor_PreviewDataIsOnIntegerLine(xD);
                    EditorGUI.DrawRect(new Rect(sx - 0.5f, plot.y, 1f, plot.height), isInt ? cInt : cMinor);
                }
            }
            // 水平: y = m*step
            {
                var m0 = Mathf.CeilToInt(minY / zGrid - 0.0001f);
                var m1 = Mathf.FloorToInt(maxY / zGrid + 0.0001f);
                if (m1 - m0 > 400) m1 = m0 + 400;
                for (var m = m0; m <= m1; m++)
                {
                    var yD = m * zGrid;
                    if (yD < minY - 1e-4f || yD > maxY + 1e-4f) continue;
                    var sy = plot.yMax - (yD - minY) * invY * plot.height;
                    if (sy < plot.y - 0.5f || sy > plot.yMax + 0.5f) continue;
                    var isInt = BtEditor_PreviewDataIsOnIntegerLine(yD);
                    EditorGUI.DrawRect(new Rect(plot.x, sy - 0.5f, plot.width, 1f), isInt ? cInt : cMinor);
                }
            }
        }

        private static void BtEditor_AddDataRangeMargin(ref float a, ref float b, float marginRatio = 0.1f)
        {
            if (a > b) (a, b) = (b, a);
            var span = b - a;
            if (span < 1e-6f)
            {
                a -= 0.5f;
                b += 0.5f;
                span = 1f;
            }
            var m = Mathf.Max(span * marginRatio, Mathf.Max(Mathf.Abs(a), Mathf.Abs(b), 0.1f) * 0.02f);
            a -= m;
            b += m;
        }

        /// <summary>子のしきい値が変わってもゼーロームがリセットされない（ビュー用の合図：子数と BT の Min/Max 帯域のみ）。</summary>
        private static int BtEditor_HashChildren1DViewOnly(IReadOnlyList<ChildMotion> children, BlendTree bt)
        {
            unchecked
            {
                var h = 17;
                h = h * 31 + (children?.Count ?? 0);
                h = h * 31 + (bt != null && bt.useAutomaticThresholds ? 1 : 0);
                if (bt != null && !bt.useAutomaticThresholds)
                {
                    h = h * 31 + bt.minThreshold.GetHashCode();
                    h = h * 31 + bt.maxThreshold.GetHashCode();
                }
                return h;
            }
        }

        /// <summary>座標移動中にビューが毎回フィットに戻らないよう子数ベースの合図にする。</summary>
        private static int BtEditor_HashChildren2DViewOnly(IReadOnlyList<ChildMotion> children) =>
            children?.Count ?? 0;

        private static void BtEditor_1DPreviewDataBounds(BlendTree bt, IReadOnlyList<ChildMotion> children, out float tMin, out float tMax)
        {
            BtEditor_Compute1DThresholdRange(bt, children, out tMin, out tMax);
            if (tMax < tMin) (tMin, tMax) = (tMax, tMin);
            if (Mathf.Approximately(tMax, tMin)) { tMin -= 0.5f; tMax += 0.5f; }
            BtEditor_AddDataRangeMargin(ref tMin, ref tMax, 0.1f);
        }

        private static void BtEditor_DrawChildrenBlend1DPreview(
            BlendTree bt, List<ChildMotion> children, AnimatorController controller, ReorderableList childList,
            bool hasCurrent, ref float currentX)
        {
            const float h = 86f;
            const float innerL = 8f;
            var r = GUILayoutUtility.GetRect(10f, h, GUILayout.ExpandWidth(true));
            var key = bt != null ? bt.GetInstanceID() : 0;
            BtEditor_1DPreviewDataBounds(bt, children, out var dMin, out var dMax);
            var sig = BtEditor_HashChildren1DViewOnly(children, bt);
            if (key != 0 && !BtEditor_IsViewFrozenForPointDrag(key))
            {
                if (!_btPreview1DDataSig.TryGetValue(key, out var lastSig) || lastSig != sig)
                {
                    _btPreview1DDataSig[key] = sig;
                    _btPreview1D[key] = new BtPreview1DState { isCustom = false, tMin = dMin, tMax = dMax };
                }
            }

            float tMin, tMax;
            if (key != 0 && _btPreview1D.TryGetValue(key, out var st) && st.isCustom)
            {
                tMin = st.tMin;
                tMax = st.tMax;
            }
            else
            {
                tMin = dMin;
                tMax = dMax;
            }
            BtEditor_Handle1DPreviewInput(r, key, ref tMin, ref tMax);
            if (tMax < tMin) (tMin, tMax) = (tMax, tMin);

            var bg = EditorGUIUtility.isProSkin
                ? new Color(0.12f, 0.12f, 0.12f, 1f)
                : new Color(0.9f, 0.9f, 0.9f, 1f);
            var trackC = new Color(0.45f, 0.45f, 0.45f, 1f);
            var nodeC = new Color(0.3f, 0.6f, 0.95f, 1f);
            var nodeCActive = new Color(0.95f, 0.7f, 0.2f, 1f);
            var nodeCListSelected = new Color(0.35f, 0.88f, 0.48f, 1f);
            var selIdx = BtEditor_GetChildListSelectedIndex(childList, children.Count);
            var labelC = EditorGUIUtility.isProSkin
                ? new Color(0.85f, 0.85f, 0.85f, 1f)
                : new Color(0.1f, 0.1f, 0.1f, 1f);
            var fitButtonStyle = new GUIStyle(EditorStyles.miniButton) { fontSize = 9 };
            var zeroButtonStyle = new GUIStyle(EditorStyles.miniButton) { fontSize = 9 };

            EditorGUI.DrawRect(r, bg);
            if (key != 0 && GUI.Button(new Rect(r.xMax - 52f, r.y + 2f, 50f, 16f), "フィット", fitButtonStyle))
            {
                _btPreview1D[key] = new BtPreview1DState { isCustom = false, tMin = dMin, tMax = dMax };
                tMin = dMin;
                tMax = dMax;
            }
            if (key != 0 && GUI.Button(new Rect(r.xMax - 104f, r.y + 2f, 50f, 16f), "黄点=0", zeroButtonStyle))
            {
                BtEditor_TryGetBlendInputPoint(bt, out _, out var keepY);
                currentX = 0f;
                BtEditor_SetBlendInputPoint(bt, 0f, keepY);
            }

            var invSpan = 1f / Mathf.Max(1e-7f, tMax - tMin);
            var trackY = r.y + r.height * 0.5f;
            BtEditor_Draw1DDataGrid(r, tMin, tMax, innerL, r.y + 20f, r.y + r.height - 30f);
            BtEditor_Process1DCurrentPointDrag(bt, r, key, tMin, tMax, trackY, innerL, hasCurrent, ref currentX);
            BtEditor_Process1DPointDrag(bt, children, controller, r, key, tMin, tMax, trackY, innerL, childList);
            var currentWeights = hasCurrent ? BtEditor_ComputeWeights1D(children, currentX) : null;

            var tr = new Rect(r.x + 6f, trackY, Mathf.Max(4f, r.width - 12f), 2f);
            EditorGUI.DrawRect(tr, trackC);
            for (var i = 0; i < children.Count; i++)
            {
                var t = children[i].threshold;
                var u = (t - tMin) * invSpan;
                u = Mathf.Clamp01(u);
                var cx = r.x + innerL + u * (r.width - innerL * 2f);
                const float cr = 5f;
                var dot = new Rect(cx - cr, trackY - cr, cr * 2f, cr * 2f);
                var pick = _btPointDragMode == BtPointDragMode.OneDChild && _btPointDragTargetId == key && _btPointDragChildIndex == i;
                var dotCol = nodeC;
                if (i == selIdx) dotCol = nodeCListSelected;
                if (pick) dotCol = nodeCActive;
                EditorGUI.DrawRect(dot, dotCol);
                if (currentWeights != null && Event.current.type == EventType.Repaint)
                {
                    var wv = Mathf.Clamp01(currentWeights[i]);
                    if (wv > 0.001f)
                    {
                        Handles.BeginGUI();
                        Handles.color = new Color(1f, 1f, 1f, 0.2f + wv * 0.5f);
                        Handles.DrawWireDisc(new Vector3(cx, trackY, 0f), Vector3.forward, 4f + 20f * wv);
                        Handles.DrawWireDisc(new Vector3(cx, trackY, 0f), Vector3.forward, 2f + 10f * wv);
                        Handles.EndGUI();
                    }
                }
                var name = BtEditor_GetChildPreviewLabel(children[i], i);
                var ts = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter, normal = { textColor = labelC } };
                const float lw = 72f;
                GUI.Label(new Rect(cx - lw * 0.5f, r.y + 20f, lw, 14f), name, ts);
            }

            if (hasCurrent)
            {
                var uCur = Mathf.Clamp01((currentX - tMin) / Mathf.Max(1e-7f, tMax - tMin));
                var xCur = r.x + innerL + uCur * (r.width - innerL * 2f);
                if (Event.current.type == EventType.Repaint)
                {
                    Handles.BeginGUI();
                    Handles.color = new Color(1f, 0.95f, 0.25f, 1f);
                    Handles.DrawWireDisc(new Vector3(xCur, trackY, 0f), Vector3.forward, 6.5f);
                    Handles.EndGUI();
                }
                BtEditor_DrawBlendCurrentMarkerGui(new Vector2(xCur, trackY), 4f);
            }

            var axis = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9 };
            var fmt = Mathf.Abs(tMax - tMin) < 0.01f ? "F4" : "F2";
            GUI.Label(new Rect(r.x + 4f, r.y + r.height - 28f, 80f, 12f), tMin.ToString(fmt), axis);
            var ra = new GUIStyle(axis) { alignment = TextAnchor.MiddleRight };
            GUI.Label(new Rect(r.xMax - 84f, r.y + r.height - 28f, 80f, 12f), tMax.ToString(fmt), ra);
            var hint = new GUIStyle(EditorStyles.miniLabel) { fontSize = 8, wordWrap = true };
            hint.normal.textColor = new Color(labelC.r, labelC.g, labelC.b, 0.75f);
            GUI.Label(new Rect(r.x + 4f, r.y + r.height - 16f, r.width - 8f, 14f), "左ドラッグ: 点移動 ・ 黄点: 現在値 ・ 中: パン", hint);
        }

        private static void BtEditor_Process1DCurrentPointDrag(
            BlendTree bt, Rect r, int key, float tMin, float tMax, float trackY, float innerL, bool hasCurrent, ref float currentX)
        {
            if (bt == null || key == 0 || !hasCurrent) return;
            var e = Event.current;
            if (e == null) return;
            var w = r.width - innerL * 2f;
            if (w < 1e-4f) return;
            var ts = Mathf.Max(1e-7f, tMax - tMin);
            var uCur = Mathf.Clamp01((currentX - tMin) / ts);
            var xCur = r.x + innerL + uCur * w;
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                var d2 = (e.mousePosition - new Vector2(xCur, trackY)).sqrMagnitude;
                if (d2 > 11f * 11f) return;
                _btPointDragMode = BtPointDragMode.OneDCurrent;
                _btPointDragTargetId = key;
                _btPointDragChildIndex = -1;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0
                     && _btPointDragMode == BtPointDragMode.OneDCurrent && _btPointDragTargetId == key)
            {
                var u = (e.mousePosition.x - (r.x + innerL)) / w;
                currentX = tMin + u * ts;
                BtEditor_TryGetBlendInputPoint(bt, out _, out var keepY);
                BtEditor_SetBlendInputPoint(bt, currentX, keepY);
                e.Use();
            }
        }

        private static void BtEditor_Process1DPointDrag(
            BlendTree bt, List<ChildMotion> children, AnimatorController controller,
            Rect r, int key, float tMin, float tMax, float trackY, float innerL, ReorderableList childList)
        {
            if (bt == null || key == 0) return;
            var e = Event.current;
            if (e == null) return;
            var w = r.width - innerL * 2f;
            if (w < 1e-4f) return;
            var ts = tMax - tMin;
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (!r.Contains(e.mousePosition)) return;
                const float hitR = 12f;
                var bestI = -1;
                var bestD = 1e9f;
                var invS = 1f / Mathf.Max(1e-7f, ts);
                for (var i = 0; i < children.Count; i++)
                {
                    var t = children[i].threshold;
                    var uD = (t - tMin) * invS;
                    uD = Mathf.Clamp01(uD);
                    var cx = r.x + innerL + uD * w;
                    var d = (e.mousePosition - new Vector2(cx, trackY)).sqrMagnitude;
                    if (d < bestD && d <= hitR * hitR) { bestD = d; bestI = i; }
                }
                if (bestI < 0) return;
                if (childList != null) childList.index = bestI;
                Undo.RecordObject(bt, "Move blend child");
                if (controller != null) Undo.RecordObject(controller, "Move blend child");
                _btPreview1D[key] = new BtPreview1DState { isCustom = true, tMin = tMin, tMax = tMax };
                _btPointDragMode = BtPointDragMode.OneDChild;
                _btPointDragTargetId = key;
                _btPointDragChildIndex = bestI;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0
                     && _btPointDragMode == BtPointDragMode.OneDChild && _btPointDragTargetId == key
                     && _btPointDragChildIndex >= 0 && _btPointDragChildIndex < children.Count)
            {
                var u = (e.mousePosition.x - (r.x + innerL)) / w;
                var tNew = tMin + u * ts;
                tNew = BtEditor_SnapCoordinate(tNew, _btPreviewSnapStep);
                var c = children[_btPointDragChildIndex];
                c.threshold = tNew;
                children[_btPointDragChildIndex] = c;
                e.Use();
            }
        }

        private static void BtEditor_Handle1DPreviewInput(Rect r, int key, ref float tMin, ref float tMax)
        {
            if (key == 0) return;
            var e = Event.current;
            if (e == null) return;
            const float innerL = 8f;
            var w = r.width - innerL * 2f;
            if (w < 1f) w = 1f;
            if (!r.Contains(e.mousePosition)) return;

            if (e.type == EventType.ScrollWheel)
            {
                var tSpan = tMax - tMin;
                if (tSpan < 1e-6f) return;
                var u = (e.mousePosition.x - (r.x + innerL)) / w;
                u = Mathf.Clamp01(u);
                // スクロール上で範囲を狭める（拡大）、下で拡げる（縮小）
                var g = Mathf.Exp(e.delta.y * 0.05f);
                g = Mathf.Clamp(g, 0.75f, 1.45f);
                var nSpan = Mathf.Max(1e-5f, tSpan * g);
                var focalT = tMin + u * tSpan;
                tMin = focalT - u * nSpan;
                tMax = tMin + nSpan;
                _btPreview1D[key] = new BtPreview1DState { isCustom = true, tMin = tMin, tMax = tMax };
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 2)
            {
                var tSpan2 = tMax - tMin;
                var dT = -e.delta.x / w * tSpan2;
                tMin += dT;
                tMax += dT;
                _btPreview1D[key] = new BtPreview1DState { isCustom = true, tMin = tMin, tMax = tMax };
                e.Use();
            }
        }

        private static void BtEditor_Compute1DThresholdRange(BlendTree bt, IReadOnlyList<ChildMotion> children, out float tMin, out float tMax)
        {
            tMin = float.MaxValue;
            tMax = float.MinValue;
            for (var i = 0; i < children.Count; i++)
            {
                var t = children[i].threshold;
                tMin = Mathf.Min(tMin, t);
                tMax = Mathf.Max(tMax, t);
            }
            if (bt != null && !bt.useAutomaticThresholds)
            {
                tMin = Mathf.Min(tMin, bt.minThreshold);
                tMax = Mathf.Max(tMax, bt.maxThreshold);
            }
        }

        private static void BtEditor_2DPreviewDataBounds(IReadOnlyList<ChildMotion> children, out float minX, out float maxX, out float minY, out float maxY)
        {
            BtEditor_Compute2DBounds(children, out minX, out maxX, out minY, out maxY);
            if (maxX < minX) (minX, maxX) = (maxX, minX);
            if (maxY < minY) (minY, maxY) = (maxY, minY);
            if (Mathf.Approximately(maxX, minX)) { minX -= 0.5f; maxX += 0.5f; }
            if (Mathf.Approximately(maxY, minY)) { minY -= 0.5f; maxY += 0.5f; }
            BtEditor_AddDataRangeMargin(ref minX, ref maxX, 0.1f);
            BtEditor_AddDataRangeMargin(ref minY, ref maxY, 0.1f);
        }

        private static void BtEditor_DrawChildrenBlend2DPreview(
            BlendTree bt, List<ChildMotion> children, AnimatorController controller, ReorderableList childList,
            bool hasCurrent, ref float currentX, ref float currentY)
        {
            const float h = 184f;
            var r = GUILayoutUtility.GetRect(10f, h, GUILayout.ExpandWidth(true));
            var key = bt != null ? bt.GetInstanceID() : 0;
            BtEditor_2DPreviewDataBounds(children, out var dMinX, out var dMaxX, out var dMinY, out var dMaxY);
            var sig = BtEditor_HashChildren2DViewOnly(children);
            if (key != 0 && !BtEditor_IsViewFrozenForPointDrag(key))
            {
                if (!_btPreview2DDataSig.TryGetValue(key, out var lastSig) || lastSig != sig)
                {
                    _btPreview2DDataSig[key] = sig;
                    _btPreview2D[key] = new BtPreview2DState
                    {
                        isCustom = false,
                        minX = dMinX,
                        maxX = dMaxX,
                        minY = dMinY,
                        maxY = dMaxY
                    };
                }
            }

            float minX, maxX, minY, maxY;
            if (key != 0 && _btPreview2D.TryGetValue(key, out var st) && st.isCustom)
            {
                minX = st.minX; maxX = st.maxX; minY = st.minY; maxY = st.maxY;
            }
            else
            {
                minX = dMinX; maxX = dMaxX; minY = dMinY; maxY = dMaxY;
            }

            const float pad = 8f;
            const float footerH = 32f;
            var innerW = r.width - pad * 2f;
            var innerH = r.height - pad * 2f - footerH;
            var plot = new Rect(r.x + pad, r.y + pad + 18f, innerW, innerH);
            BtEditor_Handle2DPreviewInput(r, plot, key, ref minX, ref maxX, ref minY, ref maxY);
            if (maxX < minX) (minX, maxX) = (maxX, minX);
            if (maxY < minY) (minY, maxY) = (maxY, minY);

            BtEditor_Process2DCurrentPointDrag(bt, plot, key, minX, maxX, minY, maxY, hasCurrent, ref currentX, ref currentY);
            BtEditor_Process2DPointDrag(bt, children, controller, plot, key, minX, maxX, minY, maxY, childList);
            var currentP = new Vector2(currentX, currentY);
            var currentWeights = hasCurrent ? BtEditor_ComputeWeights2D(bt.blendType, children, currentP) : null;

            var spanX = maxX - minX;
            var spanY = maxY - minY;
            var invX = 1f / Mathf.Max(1e-7f, spanX);
            var invY = 1f / Mathf.Max(1e-7f, spanY);
            var bg = EditorGUIUtility.isProSkin
                ? new Color(0.12f, 0.12f, 0.12f, 1f)
                : new Color(0.9f, 0.9f, 0.9f, 1f);
            var axisC = new Color(0.5f, 0.5f, 0.5f, 1f);
            var pointC = new Color(0.3f, 0.6f, 0.95f, 1f);
            var pointCActive = new Color(0.95f, 0.7f, 0.2f, 1f);
            var pointCListSelected = new Color(0.35f, 0.88f, 0.48f, 1f);
            var selIdx2D = BtEditor_GetChildListSelectedIndex(childList, children.Count);
            var labelC = EditorGUIUtility.isProSkin
                ? new Color(0.85f, 0.85f, 0.85f, 1f)
                : new Color(0.1f, 0.1f, 0.1f, 1f);
            var fitButtonStyle = new GUIStyle(EditorStyles.miniButton) { fontSize = 9 };
            var zeroButtonStyle = new GUIStyle(EditorStyles.miniButton) { fontSize = 9 };

            EditorGUI.DrawRect(r, bg);
            if (key != 0 && GUI.Button(new Rect(r.xMax - 52f, r.y + 2f, 50f, 16f), "フィット", fitButtonStyle))
            {
                _btPreview2D[key] = new BtPreview2DState
                {
                    isCustom = false,
                    minX = dMinX,
                    maxX = dMaxX,
                    minY = dMinY,
                    maxY = dMaxY
                };
                minX = dMinX; maxX = dMaxX; minY = dMinY; maxY = dMaxY;
                spanX = maxX - minX; spanY = maxY - minY;
                invX = 1f / Mathf.Max(1e-7f, spanX);
                invY = 1f / Mathf.Max(1e-7f, spanY);
            }
            if (key != 0 && GUI.Button(new Rect(r.xMax - 104f, r.y + 2f, 50f, 16f), "黄点=0", zeroButtonStyle))
            {
                currentX = 0f;
                currentY = 0f;
                BtEditor_SetBlendInputPoint(bt, 0f, 0f);
            }

            var hint = new GUIStyle(EditorStyles.miniLabel) { fontSize = 8 };
            hint.normal.textColor = new Color(labelC.r, labelC.g, labelC.b, 0.75f);
            GUI.Label(new Rect(r.x + 4f, r.y + 2f, r.width - 60f, 16f), "左ドラッグ: 点移動 ・ 黄点: 現在値 ・ 中: パン", hint);

            EditorGUI.DrawRect(plot, new Color(0.08f, 0.08f, 0.08f, 1f));
            {
                var zG = BtEditor_GetGridStep2D(minX, maxX, minY, maxY);
                BtEditor_Draw2DDataGrid(plot, minX, maxX, minY, maxY, invX, invY, zG);
            }
            if (minX <= 0f && maxX >= 0f)
            {
                var x0 = plot.x + (0f - minX) * invX * plot.width;
                if (x0 >= plot.x && x0 <= plot.xMax) EditorGUI.DrawRect(new Rect(x0, plot.y, 1f, plot.height), axisC);
            }
            if (minY <= 0f && maxY >= 0f)
            {
                var y0 = plot.yMax - (0f - minY) * invY * plot.height;
                if (y0 >= plot.y && y0 <= plot.yMax) EditorGUI.DrawRect(new Rect(plot.x, y0, plot.width, 1f), axisC);
            }

            for (var i = 0; i < children.Count; i++)
            {
                var p = children[i].position;
                var nx = (p.x - minX) * invX;
                var ny = (p.y - minY) * invY;
                nx = Mathf.Clamp01(nx);
                ny = Mathf.Clamp01(ny);
                var sx = plot.x + nx * plot.width;
                var sy = plot.yMax - ny * plot.height;
                const float d = 6f;
                var pick2 = _btPointDragMode == BtPointDragMode.TwoDChild && _btPointDragTargetId == key && _btPointDragChildIndex == i;
                var pCol = pointC;
                if (i == selIdx2D) pCol = pointCListSelected;
                if (pick2) pCol = pointCActive;
                EditorGUI.DrawRect(new Rect(sx - d * 0.5f, sy - d * 0.5f, d, d), pCol);
                if (currentWeights != null && Event.current.type == EventType.Repaint)
                {
                    var wv = Mathf.Clamp01(currentWeights[i]);
                    if (wv > 0.001f)
                    {
                        Handles.BeginGUI();
                        Handles.color = new Color(1f, 1f, 1f, 0.18f + wv * 0.5f);
                        Handles.DrawWireDisc(new Vector3(sx, sy, 0f), Vector3.forward, 5f + 22f * wv);
                        Handles.DrawWireDisc(new Vector3(sx, sy, 0f), Vector3.forward, 2f + 11f * wv);
                        Handles.EndGUI();
                    }
                }
                var name = BtEditor_GetChildPreviewLabel(children[i], i);
                var ts = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, normal = { textColor = labelC } };
                GUI.Label(new Rect(sx - 36f, sy - 18f, 72f, 12f), name, ts);
            }

            var foot = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, normal = { textColor = labelC } };
            var f = spanX < 0.01f ? "F4" : "F2";
            GUI.Label(new Rect(r.x + 4f, r.yMax - 28f, r.width - 8f, 24f), $"X: {minX.ToString(f)} — {maxX.ToString(f)} ・  Y: {minY.ToString(f)} — {maxY.ToString(f)}", foot);
            if (hasCurrent)
            {
                var ux = Mathf.Clamp01((currentX - minX) / Mathf.Max(1e-7f, maxX - minX));
                var uy = Mathf.Clamp01((currentY - minY) / Mathf.Max(1e-7f, maxY - minY));
                var px = plot.x + ux * plot.width;
                var py = plot.yMax - uy * plot.height;
                if (Event.current.type == EventType.Repaint)
                {
                    Handles.BeginGUI();
                    Handles.color = new Color(1f, 0.95f, 0.25f, 1f);
                    Handles.DrawWireDisc(new Vector3(px, py, 0f), Vector3.forward, 7f);
                    Handles.EndGUI();
                }
                BtEditor_DrawBlendCurrentMarkerGui(new Vector2(px, py), 4.5f);
            }
        }

        private static void BtEditor_Process2DCurrentPointDrag(
            BlendTree bt, Rect plot, int key, float minX, float maxX, float minY, float maxY, bool hasCurrent, ref float currentX, ref float currentY)
        {
            if (bt == null || key == 0 || !hasCurrent) return;
            var e = Event.current;
            if (e == null) return;
            var ux = Mathf.Clamp01((currentX - minX) / Mathf.Max(1e-7f, maxX - minX));
            var uy = Mathf.Clamp01((currentY - minY) / Mathf.Max(1e-7f, maxY - minY));
            var px = plot.x + ux * plot.width;
            var py = plot.yMax - uy * plot.height;
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                var d2 = (e.mousePosition - new Vector2(px, py)).sqrMagnitude;
                if (d2 > 11f * 11f) return;
                _btPointDragMode = BtPointDragMode.TwoDCurrent;
                _btPointDragTargetId = key;
                _btPointDragChildIndex = -1;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0
                     && _btPointDragMode == BtPointDragMode.TwoDCurrent && _btPointDragTargetId == key)
            {
                var tx = (e.mousePosition.x - plot.x) / Mathf.Max(1e-4f, plot.width);
                var ty = (e.mousePosition.y - plot.y) / Mathf.Max(1e-4f, plot.height);
                currentX = minX + tx * (maxX - minX);
                currentY = maxY - ty * (maxY - minY);
                BtEditor_SetBlendInputPoint(bt, currentX, currentY);
                e.Use();
            }
        }

        private static void BtEditor_Process2DPointDrag(
            BlendTree bt, List<ChildMotion> children, AnimatorController controller,
            Rect plot, int key, float minX, float maxX, float minY, float maxY, ReorderableList childList)
        {
            if (bt == null || key == 0) return;
            var e = Event.current;
            if (e == null) return;
            var spanX = maxX - minX;
            var spanY = maxY - minY;
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (!plot.Contains(e.mousePosition)) return;
                const float hitR = 12f;
                var bestI = -1;
                var bestD = 1e9f;
                for (var i = 0; i < children.Count; i++)
                {
                    var p = children[i].position;
                    var nx = (p.x - minX) / Mathf.Max(1e-7f, spanX);
                    var ny = (p.y - minY) / Mathf.Max(1e-7f, spanY);
                    nx = Mathf.Clamp01(nx);
                    ny = Mathf.Clamp01(ny);
                    var sx = plot.x + nx * plot.width;
                    var sy = plot.yMax - ny * plot.height;
                    var d = (e.mousePosition - new Vector2(sx, sy)).sqrMagnitude;
                    if (d < bestD && d <= hitR * hitR) { bestD = d; bestI = i; }
                }
                if (bestI < 0) return;
                if (childList != null) childList.index = bestI;
                Undo.RecordObject(bt, "Move blend child");
                if (controller != null) Undo.RecordObject(controller, "Move blend child");
                _btPreview2D[key] = new BtPreview2DState
                {
                    isCustom = true,
                    minX = minX,
                    maxX = maxX,
                    minY = minY,
                    maxY = maxY
                };
                _btPointDragMode = BtPointDragMode.TwoDChild;
                _btPointDragTargetId = key;
                _btPointDragChildIndex = bestI;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0
                     && _btPointDragMode == BtPointDragMode.TwoDChild && _btPointDragTargetId == key
                     && _btPointDragChildIndex >= 0 && _btPointDragChildIndex < children.Count)
            {
                var mx = e.mousePosition.x;
                var my = e.mousePosition.y;
                var ux = (mx - plot.x) / Mathf.Max(1e-4f, plot.width);
                var tGui = (my - plot.y) / Mathf.Max(1e-4f, plot.height);
                var newX = minX + ux * spanX;
                var newY = maxY - tGui * spanY;
                newX = BtEditor_SnapCoordinate(newX, _btPreviewSnapStep);
                newY = BtEditor_SnapCoordinate(newY, _btPreviewSnapStep);
                var c = children[_btPointDragChildIndex];
                c.position = new Vector2(newX, newY);
                children[_btPointDragChildIndex] = c;
                e.Use();
            }
        }

        private static void BtEditor_Handle2DPreviewInput(Rect fullR, Rect plot, int key, ref float minX, ref float maxX, ref float minY, ref float maxY)
        {
            if (key == 0) return;
            var e = Event.current;
            if (e == null) return;
            var spanX = maxX - minX;
            var spanY = maxY - minY;
            if (e.type == EventType.ScrollWheel)
            {
                if (!plot.Contains(e.mousePosition)) return;
                if (spanX < 1e-6f || spanY < 1e-6f) return;
                var tx = (e.mousePosition.x - plot.x) / Mathf.Max(1e-4f, plot.width);
                var tGui = (e.mousePosition.y - plot.y) / Mathf.Max(1e-4f, plot.height);
                tx = Mathf.Clamp01(tx);
                tGui = Mathf.Clamp01(tGui);
                var g = Mathf.Exp(e.delta.y * 0.05f);
                g = Mathf.Clamp(g, 0.75f, 1.45f);
                var focalX = minX + tx * spanX;
                var focalY = maxY - tGui * spanY;
                var ux = (focalX - minX) / Mathf.Max(1e-6f, spanX);
                var uy = (focalY - minY) / Mathf.Max(1e-6f, spanY);
                var nSpanX = Mathf.Max(1e-5f, spanX * g);
                var nSpanY = Mathf.Max(1e-5f, spanY * g);
                minX = focalX - ux * nSpanX;
                maxX = minX + nSpanX;
                minY = focalY - uy * nSpanY;
                maxY = minY + nSpanY;
                _btPreview2D[key] = new BtPreview2DState { isCustom = true, minX = minX, maxX = maxX, minY = minY, maxY = maxY };
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 2)
            {
                if (!fullR.Contains(e.mousePosition)) return;
                if (spanX < 1e-6f || spanY < 1e-6f) return;
                var dX = -e.delta.x / Mathf.Max(1e-4f, plot.width) * spanX;
                var dY = e.delta.y / Mathf.Max(1e-4f, plot.height) * spanY;
                minX += dX; maxX += dX;
                minY += dY; maxY += dY;
                _btPreview2D[key] = new BtPreview2DState { isCustom = true, minX = minX, maxX = maxX, minY = minY, maxY = maxY };
                e.Use();
            }
        }

        private static void BtEditor_Compute2DBounds(IReadOnlyList<ChildMotion> children, out float minX, out float maxX, out float minY, out float maxY)
        {
            minX = minY = float.MaxValue;
            maxX = maxY = float.MinValue;
            for (var i = 0; i < children.Count; i++)
            {
                var p = children[i].position;
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }
        }

        private static string BtEditor_GetChildPreviewLabel(ChildMotion c, int index)
        {
            if (c.motion != null)
            {
                var n = c.motion.name;
                if (n.Length > 10) n = n.Substring(0, 9) + "…";
                return n;
            }
            return "#" + index;
        }

        // ======================== パラメータポップアップ ========================

        private static string BtEditor_ParamPopup(string label, string current, List<string> names)
        {
            return DrawTextOrSelectField(label, current, names);
        }

        private static bool BtEditor_AreChildrenEqual(IReadOnlyList<ChildMotion> a, IReadOnlyList<ChildMotion> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;

            for (var i = 0; i < a.Count; i++)
            {
                var x = a[i];
                var y = b[i];
                if (!ReferenceEquals(x.motion, y.motion)) return false;
                if (!Mathf.Approximately(x.threshold, y.threshold)) return false;
                if (!Mathf.Approximately(x.position.x, y.position.x)) return false;
                if (!Mathf.Approximately(x.position.y, y.position.y)) return false;
                if (!Mathf.Approximately(x.timeScale, y.timeScale)) return false;
                if (!Mathf.Approximately(x.cycleOffset, y.cycleOffset)) return false;
                if (!string.Equals(x.directBlendParameter, y.directBlendParameter, StringComparison.Ordinal)) return false;
                if (x.mirror != y.mirror) return false;
            }

            return true;
        }

        private static bool BtEditor_TryGetNormalizedBlendValues(BlendTree bt, out bool value)
        {
            value = false;
            if (bt == null)
                return false;
            using (var so = new SerializedObject(bt))
            {
                var p = so.FindProperty("m_NormalizedBlendValues");
                if (p == null || p.propertyType != SerializedPropertyType.Boolean)
                    return false;
                value = p.boolValue;
                return true;
            }
        }

        private static bool BtEditor_SetNormalizedBlendValues(BlendTree bt, bool on)
        {
            if (bt == null)
                return false;
            using (var so = new SerializedObject(bt))
            {
                var p = so.FindProperty("m_NormalizedBlendValues");
                if (p == null || p.propertyType != SerializedPropertyType.Boolean)
                    return false;
                p.boolValue = on;
                so.ApplyModifiedPropertiesWithoutUndo();
                return true;
            }
        }

        private static List<string> BtEditor_GetAllParams(AnimatorController controller)
        {
            var result = new List<string>();
            if (controller?.parameters == null) return result;
            foreach (var p in controller.parameters)
                if (p != null && !string.IsNullOrEmpty(p.name))
                    result.Add(p.name);
            return result;
        }

        private static List<string> BtEditor_CollectParameterNames(
            AnimatorController preferredController,
            params AnimatorControllerParameterType[] allowedTypes)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var typeFilter = (allowedTypes != null && allowedTypes.Length > 0)
                ? new HashSet<AnimatorControllerParameterType>(allowedTypes)
                : null;

            void AddFromController(AnimatorController c)
            {
                if (c?.parameters == null) return;
                foreach (var p in c.parameters)
                {
                    if (p == null || string.IsNullOrWhiteSpace(p.name))
                        continue;
                    if (typeFilter != null && !typeFilter.Contains(p.type))
                        continue;
                    set.Add(p.name);
                }
            }

            AddFromController(preferredController);
            AddFromController(BtEditor_GetEditingAnimatorWindowController());
            foreach (var n in BtEditor_CollectSelectionParameterNames(typeFilter))
                set.Add(n);

            return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static AnimatorController BtEditor_GetEditingAnimatorWindowController()
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

        private static List<string> BtEditor_CollectSelectionParameterNames(HashSet<AnimatorControllerParameterType> typeFilter)
        {
            var controllers = new HashSet<AnimatorController>();
            void TryAddControllerFromObj(Object o)
            {
                if (o == null) return;
                if (o is AnimatorController ac)
                {
                    controllers.Add(ac);
                    return;
                }

                var path = AssetDatabase.GetAssetPath(o);
                if (string.IsNullOrEmpty(path)) return;
                var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (c != null)
                    controllers.Add(c);
            }

            foreach (var o in Selection.objects)
                TryAddControllerFromObj(o);
            foreach (var id in Selection.instanceIDs)
                TryAddControllerFromObj(EditorUtility.InstanceIDToObject(id));
            TryAddControllerFromObj(Selection.activeObject);

            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in controllers)
            {
                if (c?.parameters == null) continue;
                foreach (var p in c.parameters)
                {
                    if (p == null || string.IsNullOrWhiteSpace(p.name))
                        continue;
                    if (typeFilter != null && !typeFilter.Contains(p.type))
                        continue;
                    set.Add(p.name);
                }
            }

            return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool BtEditor_IsLikelyBlendShapeContext(BlendTree bt)
        {
            if (bt == null)
                return false;

            foreach (var c in bt.children)
            {
                if (c.motion is AnimationClip clip)
                {
                    var bindings = AnimationUtility.GetCurveBindings(clip);
                    for (var i = 0; i < bindings.Length; i++)
                    {
                        if (!string.IsNullOrEmpty(bindings[i].propertyName) &&
                            bindings[i].propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                            return true;
                    }
                }
                else if (c.motion is BlendTree subBt && BtEditor_IsLikelyBlendShapeContext(subBt))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
