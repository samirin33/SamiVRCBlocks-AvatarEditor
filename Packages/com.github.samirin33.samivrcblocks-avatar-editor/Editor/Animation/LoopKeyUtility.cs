#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    /// <summary>
    /// 編集中クリップの終点から始点へ戻る三次ベジエを、指定長の両端へ同じ値・同じ接線のキーとして置く。
    /// </summary>
    public static class LoopKeyUtility
    {
        public const float TimeEpsilon = 0.0001f;

        const float DefaultWeight = 1f / 3f;
        const float WeightMin = 0.0001f;
        const int MaxSkipDetails = 8;

        public sealed class ApplyResult
        {
            public int AppliedCount;
            public int SkippedCount;
            public string Message = "";
            public readonly List<string> Details = new List<string>();
        }

        public static bool IsEditable(AnimationClip clip, out string message)
        {
            message = null;
            if (clip == null)
            {
                message = "Animation ウィンドウでクリップを開いてください。";
                return false;
            }

            if ((clip.hideFlags & HideFlags.NotEditable) != 0)
            {
                message = "このクリップは編集できません。";
                return false;
            }

            if (EditorUtility.IsPersistent(clip) && !AssetDatabase.IsOpenForEdit(clip, out message))
                return false;

            string assetPath = AssetDatabase.GetAssetPath(clip);
            if (!string.IsNullOrEmpty(assetPath) && AssetImporter.GetAtPath(assetPath) is ModelImporter)
            {
                message = "FBX などモデル内のクリップは編集できません。";
                return false;
            }

            return true;
        }

        /// <summary>
        /// フロートカーブ全体の最初のキー時刻、最後のキー時刻、本数。キーが無いときは false。
        /// </summary>
        public static bool TryGetKeyframeSpan(AnimationClip clip, out float startTime, out float endTime, out int curveCount)
        {
            startTime = 0f;
            endTime = 0f;
            curveCount = 0;
            if (clip == null)
                return false;

            bool any = false;
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0)
                    continue;

                curveCount++;
                float first = curve.keys[0].time;
                float last = curve.keys[curve.length - 1].time;
                if (!any || first < startTime)
                    startTime = first;
                if (!any || last > endTime)
                    endTime = last;
                any = true;
            }

            return any;
        }

        public static ApplyResult Apply(AnimationClip clip, float length, bool enableLoopTime)
        {
            var result = new ApplyResult();
            if (!IsEditable(clip, out string blocked))
            {
                result.Message = blocked;
                return result;
            }

            if (length <= TimeEpsilon)
            {
                result.Message = "クリップ長は 0 より大きくしてください。";
                return result;
            }

            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            var changes = new List<CurveChange>();
            int hiddenSkips = 0;

            for (int i = 0; i < bindings.Length; i++)
            {
                EditorCurveBinding binding = bindings[i];
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null)
                    continue;

                if (!TryBuildLoopCurve(curve, length, out AnimationCurve built, out string reason))
                {
                    result.SkippedCount++;
                    if (result.Details.Count < MaxSkipDetails)
                        result.Details.Add(CurveLabel(binding) + ": " + reason);
                    else
                        hiddenSkips++;
                    continue;
                }

                changes.Add(new CurveChange(binding, built));
            }

            if (hiddenSkips > 0)
                result.Details.Add("ほか " + hiddenSkips + " 本");

            if (changes.Count == 0)
            {
                result.Message = result.SkippedCount > 0
                    ? "適用できるカーブがありません。"
                    : "フロートカーブがありません。";
                return result;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("ループキーを追加");
            Undo.RegisterCompleteObjectUndo(clip, "ループキーを追加");
            for (int i = 0; i < changes.Count; i++)
                AnimationUtility.SetEditorCurve(clip, changes[i].Binding, changes[i].Curve);

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            if (enableLoopTime)
                settings.loopTime = true;
            settings.startTime = 0f;
            settings.stopTime = length;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            Undo.CollapseUndoOperations(undoGroup);

            result.AppliedCount = changes.Count;
            result.Message = changes.Count + " 本のカーブにループキーを追加しました。";
            if (result.SkippedCount > 0)
                result.Message += " " + result.SkippedCount + " 本はスキップしました。";
            return result;
        }

        /// <summary>
        /// クリップ長より後にキーがあるときは、長さ以上のキーを捨て、長さより前の最後のキーを終点にする。
        /// </summary>
        static bool TrySelectLoopKeys(AnimationCurve source, float length, out Keyframe start, out Keyframe end, out List<KeyEdit> edits, out string reason)
        {
            start = default;
            end = default;
            edits = null;
            reason = null;

            Keyframe[] keys = source.keys;
            if (keys.Length == 0)
            {
                reason = "キーがありません。";
                return false;
            }

            if (keys[0].time < -TimeEpsilon)
            {
                reason = "始点が 0 より前です。";
                return false;
            }

            int endIndex = keys.Length - 1;
            bool hasKeyBeyond = keys[keys.Length - 1].time > length + TimeEpsilon;
            if (hasKeyBeyond)
            {
                endIndex = -1;
                for (int i = 0; i < keys.Length; i++)
                {
                    if (keys[i].time < length - TimeEpsilon)
                        endIndex = i;
                    else
                        break;
                }

                if (endIndex < 0)
                {
                    reason = "クリップ長より前にキーがありません。";
                    return false;
                }
            }

            start = keys[0];
            end = keys[endIndex];
            edits = new List<KeyEdit>(endIndex + 3);
            for (int i = 0; i <= endIndex; i++)
                edits.Add(Capture(source, i));
            return true;
        }

        static bool TryBuildLoopCurve(AnimationCurve source, float length, out AnimationCurve result, out string reason)
        {
            result = null;
            reason = null;
            if (!TrySelectLoopKeys(source, length, out Keyframe start, out Keyframe end, out List<KeyEdit> edits, out reason))
                return false;

            bool hasRight = start.time > TimeEpsilon;
            bool hasLeft = length - end.time > TimeEpsilon;
            if (!hasLeft && !hasRight)
            {
                reason = "ループ区間がありません。クリップ長を終点より長くするか、始点を 0 より後にしてください。";
                return false;
            }

            if (float.IsNaN(start.value) || float.IsNaN(end.value) || float.IsNaN(start.inTangent) || float.IsNaN(end.outTangent))
            {
                reason = "接線または値が不正です。";
                return false;
            }

            if (IsSteppedTangent(end.outTangent) || IsSteppedTangent(start.inTangent))
            {
                ApplyStepped(edits, length, hasLeft, hasRight);
            }
            else if (!hasLeft)
            {
                InsertLoopKey(edits, 0, 0f, end.value, end.outTangent, OutWeight(end), OutWeight(end), HasOutWeight(end));
            }
            else if (!hasRight)
            {
                AppendLoopKey(edits, length, start.value, start.inTangent, InWeight(start), InWeight(start), HasInWeight(start));
            }
            else if (!TryApplySplit(edits, start, end, length, out reason))
            {
                return false;
            }

            result = Rebuild(source, edits);
            return true;
        }

        static void ApplyStepped(List<KeyEdit> edits, float length, bool hasLeft, bool hasRight)
        {
            float hold = edits[edits.Count - 1].Keyframe.value;
            if (hasLeft)
            {
                KeyEdit endEdit = edits[edits.Count - 1];
                Keyframe key = endEdit.Keyframe;
                key.outTangent = float.PositiveInfinity;
                endEdit.Keyframe = key;
                endEdit.RightMode = AnimationUtility.TangentMode.Constant;
                endEdit.Broken = !TangentsMatch(key.inTangent, key.outTangent);
                edits[edits.Count - 1] = endEdit;
                edits.Add(ConstantEdit(length, hold));
            }

            if (hasRight)
            {
                KeyEdit startEdit = edits[0];
                Keyframe key = startEdit.Keyframe;
                key.inTangent = float.PositiveInfinity;
                startEdit.Keyframe = key;
                startEdit.LeftMode = AnimationUtility.TangentMode.Constant;
                startEdit.Broken = !TangentsMatch(key.inTangent, key.outTangent);
                edits[0] = startEdit;
                edits.Insert(0, ConstantEdit(0f, hold));
            }
        }

        static bool TryApplySplit(List<KeyEdit> edits, Keyframe start, Keyframe end, float length, out string reason)
        {
            reason = null;
            float duration = (length - end.time) + start.time;
            float seamTime = length - end.time;
            float outWeight = OutWeight(end);
            float inWeight = InWeight(start);

            var p0 = new Vector2(0f, end.value);
            var p1 = new Vector2(duration * outWeight, end.value + end.outTangent * duration * outWeight);
            var p2 = new Vector2(duration * (1f - inWeight), start.value - start.inTangent * duration * inWeight);
            var p3 = new Vector2(duration, start.value);

            float u = SolveTime(p0, p1, p2, p3, seamTime);
            float solvedTime = Bernstein(u, p0.x, p1.x, p2.x, p3.x);
            if (Mathf.Abs(solvedTime - seamTime) > 0.001f)
            {
                reason = "ループ区間の時刻をベジエ上で解けませんでした。";
                return false;
            }

            Split(p0, p1, p2, p3, u,
                out Vector2 left0, out Vector2 left1, out Vector2 left2, out Vector2 seam,
                out Vector2 right1, out Vector2 right2, out Vector2 right3);

            float tangent = DerivativeSlope(p0, p1, p2, p3, u);
            if (float.IsNaN(tangent) || float.IsNaN(seam.y))
            {
                reason = "ループキーを計算できませんでした。";
                return false;
            }

            if (edits.Count == 1)
            {
                KeyEdit only = edits[0];
                Keyframe key = only.Keyframe;
                if (TryOutHandle(left0, left1, seam, out float endOutTangent, out float endOutWeight))
                {
                    key.outTangent = endOutTangent;
                    key.outWeight = endOutWeight;
                    key.weightedMode |= WeightedMode.Out;
                    only.RightMode = AnimationUtility.TangentMode.Free;
                }

                if (TryInHandle(seam, right2, right3, out float startInTangent, out float startInWeight))
                {
                    key.inTangent = startInTangent;
                    key.inWeight = startInWeight;
                    key.weightedMode |= WeightedMode.In;
                    only.LeftMode = AnimationUtility.TangentMode.Free;
                }

                only.Keyframe = key;
                only.Broken = !TangentsMatch(key.inTangent, key.outTangent);
                edits[0] = only;
            }
            else
            {
                KeyEdit endEdit = edits[edits.Count - 1];
                Keyframe endKey = endEdit.Keyframe;
                if (TryOutHandle(left0, left1, seam, out float endOutTangent, out float endOutWeight))
                {
                    endKey.outTangent = endOutTangent;
                    endKey.outWeight = endOutWeight;
                    endKey.weightedMode |= WeightedMode.Out;
                    endEdit.RightMode = AnimationUtility.TangentMode.Free;
                }

                endEdit.Keyframe = endKey;
                endEdit.Broken = !TangentsMatch(endKey.inTangent, endKey.outTangent);
                edits[edits.Count - 1] = endEdit;

                KeyEdit startEdit = edits[0];
                Keyframe startKey = startEdit.Keyframe;
                if (TryInHandle(seam, right2, right3, out float startInTangent, out float startInWeight))
                {
                    startKey.inTangent = startInTangent;
                    startKey.inWeight = startInWeight;
                    startKey.weightedMode |= WeightedMode.In;
                    startEdit.LeftMode = AnimationUtility.TangentMode.Free;
                }

                startEdit.Keyframe = startKey;
                startEdit.Broken = !TangentsMatch(startKey.inTangent, startKey.outTangent);
                edits[0] = startEdit;
            }

            float seamInWeight = InHandleWeight(left0, left2, seam);
            float seamOutWeight = OutHandleWeight(seam, right1, right3);
            InsertLoopKey(edits, 0, 0f, seam.y, tangent, seamInWeight, seamOutWeight, true);
            AppendLoopKey(edits, length, seam.y, tangent, seamInWeight, seamOutWeight, true);
            return true;
        }

        static void InsertLoopKey(List<KeyEdit> edits, int index, float time, float value, float tangent, float inWeight, float outWeight, bool weighted)
        {
            edits.Insert(index, LoopEdit(time, value, tangent, inWeight, outWeight, weighted));
        }

        static void AppendLoopKey(List<KeyEdit> edits, float time, float value, float tangent, float inWeight, float outWeight, bool weighted)
        {
            edits.Add(LoopEdit(time, value, tangent, inWeight, outWeight, weighted));
        }

        static KeyEdit Capture(AnimationCurve curve, int index)
        {
            Keyframe key = curve.keys[index];
            var left = AnimationUtility.GetKeyLeftTangentMode(curve, index);
            var right = AnimationUtility.GetKeyRightTangentMode(curve, index);
            bool broken = AnimationUtility.GetKeyBroken(curve, index);
            if (left != AnimationUtility.TangentMode.Constant)
                left = AnimationUtility.TangentMode.Free;
            if (right != AnimationUtility.TangentMode.Constant)
                right = AnimationUtility.TangentMode.Free;
            return new KeyEdit(key, left, right, broken);
        }

        static KeyEdit LoopEdit(float time, float value, float tangent, float inWeight, float outWeight, bool weighted)
        {
            var key = new Keyframe(time, value, tangent, tangent);
            if (weighted)
            {
                key.weightedMode = WeightedMode.Both;
                key.inWeight = Mathf.Clamp(inWeight, WeightMin, 1f);
                key.outWeight = Mathf.Clamp(outWeight, WeightMin, 1f);
            }
            else
            {
                key.weightedMode = WeightedMode.None;
                key.inWeight = DefaultWeight;
                key.outWeight = DefaultWeight;
            }

            bool broken = weighted && Mathf.Abs(key.inWeight - key.outWeight) > 0.001f;
            return new KeyEdit(
                key,
                AnimationUtility.TangentMode.Free,
                AnimationUtility.TangentMode.Free,
                broken);
        }

        static KeyEdit ConstantEdit(float time, float value)
        {
            var key = new Keyframe(time, value, float.PositiveInfinity, float.PositiveInfinity)
            {
                weightedMode = WeightedMode.None,
                inWeight = DefaultWeight,
                outWeight = DefaultWeight
            };
            return new KeyEdit(
                key,
                AnimationUtility.TangentMode.Constant,
                AnimationUtility.TangentMode.Constant,
                false);
        }

        static AnimationCurve Rebuild(AnimationCurve source, List<KeyEdit> edits)
        {
            var keys = new Keyframe[edits.Count];
            for (int i = 0; i < edits.Count; i++)
                keys[i] = edits[i].Keyframe;

            var curve = new AnimationCurve(keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode
            };

            for (int i = 0; i < edits.Count; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, edits[i].LeftMode);
                AnimationUtility.SetKeyRightTangentMode(curve, i, edits[i].RightMode);
                AnimationUtility.SetKeyBroken(curve, i, edits[i].Broken);
                curve.MoveKey(i, edits[i].Keyframe);
            }

            return curve;
        }

        static float OutWeight(Keyframe key)
        {
            if (HasOutWeight(key))
                return Mathf.Clamp(key.outWeight, WeightMin, 1f);
            return DefaultWeight;
        }

        static float InWeight(Keyframe key)
        {
            if (HasInWeight(key))
                return Mathf.Clamp(key.inWeight, WeightMin, 1f);
            return DefaultWeight;
        }

        static bool HasOutWeight(Keyframe key)
        {
            return (key.weightedMode & WeightedMode.Out) != 0;
        }

        static bool HasInWeight(Keyframe key)
        {
            return (key.weightedMode & WeightedMode.In) != 0;
        }

        static bool IsSteppedTangent(float tangent)
        {
            return float.IsInfinity(tangent);
        }

        static bool TangentsMatch(float a, float b)
        {
            if (float.IsPositiveInfinity(a) && float.IsPositiveInfinity(b))
                return true;
            if (float.IsNegativeInfinity(a) && float.IsNegativeInfinity(b))
                return true;
            if (float.IsInfinity(a) || float.IsInfinity(b) || float.IsNaN(a) || float.IsNaN(b))
                return false;
            float scale = Mathf.Max(1f, Mathf.Abs(a), Mathf.Abs(b));
            return Mathf.Abs(a - b) <= 0.0001f * scale;
        }

        static float Bernstein(float u, float p0, float p1, float p2, float p3)
        {
            float s = 1f - u;
            float ss = s * s;
            float uu = u * u;
            return (ss * s * p0) + (3f * ss * u * p1) + (3f * s * uu * p2) + (uu * u * p3);
        }

        static float BernsteinDerivative(float u, float p0, float p1, float p2, float p3)
        {
            float s = 1f - u;
            return (3f * s * s * (p1 - p0)) + (6f * s * u * (p2 - p1)) + (3f * u * u * (p3 - p2));
        }

        static float DerivativeSlope(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float u)
        {
            float dx = BernsteinDerivative(u, p0.x, p1.x, p2.x, p3.x);
            float dy = BernsteinDerivative(u, p0.y, p1.y, p2.y, p3.y);
            if (Mathf.Abs(dx) < 1e-6f)
                return 0f;
            return dy / dx;
        }

        static float SolveTime(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float time)
        {
            float span = p3.x - p0.x;
            if (span <= 1e-6f)
                return 0f;
            if (time <= p0.x + 1e-6f)
                return 0f;
            if (time >= p3.x - 1e-6f)
                return 1f;

            float u = Mathf.Clamp01((time - p0.x) / span);
            for (int i = 0; i < 20; i++)
            {
                float x = Bernstein(u, p0.x, p1.x, p2.x, p3.x);
                float dx = BernsteinDerivative(u, p0.x, p1.x, p2.x, p3.x);
                float error = x - time;
                if (Mathf.Abs(error) <= 1e-5f)
                    return u;
                if (Mathf.Abs(dx) < 1e-6f)
                    break;
                u = Mathf.Clamp01(u - error / dx);
            }

            float bestU = u;
            float bestError = float.MaxValue;
            const int samples = 24;
            for (int i = 0; i <= samples; i++)
            {
                float sample = i / (float)samples;
                float error = Mathf.Abs(Bernstein(sample, p0.x, p1.x, p2.x, p3.x) - time);
                if (error < bestError)
                {
                    bestError = error;
                    bestU = sample;
                }
            }

            float lo = Mathf.Clamp01(bestU - (1f / samples));
            float hi = Mathf.Clamp01(bestU + (1f / samples));
            for (int i = 0; i < 24; i++)
            {
                float mid = (lo + hi) * 0.5f;
                float x = Bernstein(mid, p0.x, p1.x, p2.x, p3.x);
                if (x < time)
                    lo = mid;
                else
                    hi = mid;
            }

            return (lo + hi) * 0.5f;
        }

        static void Split(
            Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float u,
            out Vector2 left0, out Vector2 left1, out Vector2 left2, out Vector2 seam,
            out Vector2 right1, out Vector2 right2, out Vector2 right3)
        {
            Vector2 a = Vector2.Lerp(p0, p1, u);
            Vector2 b = Vector2.Lerp(p1, p2, u);
            Vector2 c = Vector2.Lerp(p2, p3, u);
            Vector2 d = Vector2.Lerp(a, b, u);
            Vector2 e = Vector2.Lerp(b, c, u);
            left0 = p0;
            left1 = a;
            left2 = d;
            seam = Vector2.Lerp(d, e, u);
            right1 = e;
            right2 = c;
            right3 = p3;
        }

        static bool TryOutHandle(Vector2 start, Vector2 handle, Vector2 end, out float tangent, out float weight)
        {
            return TryHandle(handle.x - start.x, handle.y - start.y, end.x - start.x, out tangent, out weight);
        }

        static bool TryInHandle(Vector2 start, Vector2 handle, Vector2 end, out float tangent, out float weight)
        {
            return TryHandle(end.x - handle.x, end.y - handle.y, end.x - start.x, out tangent, out weight);
        }

        static bool TryHandle(float toward, float rise, float span, out float tangent, out float weight)
        {
            tangent = 0f;
            weight = DefaultWeight;
            if (span <= 1e-5f || toward <= 1e-5f)
                return false;
            weight = Mathf.Clamp(toward / span, WeightMin, 1f);
            tangent = rise / toward;
            return !float.IsNaN(tangent) && !float.IsInfinity(tangent);
        }

        static float OutHandleWeight(Vector2 start, Vector2 handle, Vector2 end)
        {
            TryOutHandle(start, handle, end, out _, out float weight);
            return weight;
        }

        static float InHandleWeight(Vector2 start, Vector2 handle, Vector2 end)
        {
            TryInHandle(start, handle, end, out _, out float weight);
            return weight;
        }

        static string CurveLabel(EditorCurveBinding binding)
        {
            if (string.IsNullOrEmpty(binding.path))
                return binding.propertyName;
            return binding.path + " / " + binding.propertyName;
        }

        struct CurveChange
        {
            public EditorCurveBinding Binding;
            public AnimationCurve Curve;

            public CurveChange(EditorCurveBinding binding, AnimationCurve curve)
            {
                Binding = binding;
                Curve = curve;
            }
        }

        struct KeyEdit
        {
            public Keyframe Keyframe;
            public AnimationUtility.TangentMode LeftMode;
            public AnimationUtility.TangentMode RightMode;
            public bool Broken;

            public KeyEdit(Keyframe keyframe, AnimationUtility.TangentMode leftMode, AnimationUtility.TangentMode rightMode, bool broken)
            {
                Keyframe = keyframe;
                LeftMode = leftMode;
                RightMode = rightMode;
                Broken = broken;
            }
        }
    }
}
#endif
