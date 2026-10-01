using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    public enum TransformWiggleSquishMode
    {
        PositionWiggle = 0,
        ScaleSquish = 1,
        RotationWiggle = 2
    }

    /// <summary>
    /// ウィグル / スクウィッシュ曲線のパラメータ。
    /// 値は base + (amplitude * sin(2π * frequency * (t + timeOffset)) + noise) * exp(-damping * t)。
    /// ループでないとき、残りの振れ幅 (|amplitude| + |noise|) * exp(-damping * t) が閾値を下回った時刻でクリップを終える。
    /// </summary>
    [Serializable]
    public class TransformWiggleSquishSettings
    {
        public TransformWiggleSquishMode mode = TransformWiggleSquishMode.PositionWiggle;
        public Vector3 amplitude = new Vector3(0.01f, 0.035f, 0.01f);
        public Vector3 noise = Vector3.zero;
        public float noiseStrength;
        public float noiseFrequency = 4f;
        public Vector3 noiseAxisFrequency = new Vector3(4f, 4f, 4f);
        public int noiseSeed = 1;
        public float frequency = 8f;
        public Vector3 basePosition = Vector3.zero;
        public Vector3 baseRotation = Vector3.zero;
        public Vector3 baseScale = Vector3.one;
        public float damping = 4f;
        public float settleThreshold = 0.001f;
        public Vector3 timeOffset = new Vector3(0f, 0.02f, 0.04f);
        public float sampleRate = 60f;
        public float maxDuration = 2.5f;
        public bool loop;
        public int loopCycles = 2;
        public float previewSpeed = 1f;
        public bool optimizeCurve = true;
        public float optimizeTolerance = 0.01f;
        public int settingsVersion;
        public bool manualPath;
        public string bindingPath = "";

        public void Sanitize()
        {
            amplitude = SanitizeVector(amplitude);
            noise = SanitizeVector(noise);
            basePosition = SanitizeVector(basePosition);
            baseRotation = SanitizeVector(baseRotation);
            baseScale = SanitizeVector(baseScale);
            timeOffset = SanitizeVector(timeOffset);
            if (float.IsNaN(noiseStrength) || float.IsInfinity(noiseStrength))
                noiseStrength = 0f;
            if (float.IsNaN(noiseFrequency) || noiseFrequency < 0f)
                noiseFrequency = 0f;
            noiseFrequency = Mathf.Min(noiseFrequency, 120f);
            if (float.IsNaN(frequency) || frequency < 0f)
                frequency = 0f;
            if (float.IsNaN(damping) || damping < 0f)
                damping = 0f;
            if (float.IsNaN(settleThreshold) || settleThreshold < 0f)
                settleThreshold = 0f;
            if (float.IsNaN(sampleRate))
                sampleRate = 60f;
            sampleRate = Mathf.Clamp(Mathf.Round(sampleRate), 5f, 60f);
            if (float.IsNaN(maxDuration))
                maxDuration = 2.5f;
            maxDuration = Mathf.Clamp(maxDuration, 0.05f, 10f);
            loopCycles = Mathf.Clamp(loopCycles, 1, 16);
            if (float.IsNaN(previewSpeed))
                previewSpeed = 1f;
            previewSpeed = Mathf.Clamp(previewSpeed, 0.1f, 2f);
            if (float.IsNaN(optimizeTolerance))
                optimizeTolerance = 0.01f;
            optimizeTolerance = Mathf.Clamp(optimizeTolerance, 0.001f, 0.05f);
            if (bindingPath == null)
                bindingPath = "";
            if (!Enum.IsDefined(typeof(TransformWiggleSquishMode), mode))
                mode = TransformWiggleSquishMode.PositionWiggle;
            if (settingsVersion < 1)
            {
                optimizeCurve = true;
                settingsVersion = 1;
            }

            if (settingsVersion < 2)
            {
                float shared = noiseFrequency > 0f ? noiseFrequency : 4f;
                noiseAxisFrequency = new Vector3(shared, shared, shared);
                settingsVersion = 2;
            }

            if (settingsVersion < 3)
            {
                noiseStrength = LargestComponent(noise);
                float frequency = Mathf.Max(noiseAxisFrequency.x, Mathf.Max(noiseAxisFrequency.y, noiseAxisFrequency.z));
                if (frequency > 0f)
                    noiseFrequency = frequency;
                settingsVersion = 3;
            }
        }

        private static float LargestComponent(Vector3 value)
        {
            float strength = value.x;
            float best = Mathf.Abs(value.x);
            if (Mathf.Abs(value.y) > best)
            {
                best = Mathf.Abs(value.y);
                strength = value.y;
            }

            if (Mathf.Abs(value.z) > best)
                strength = value.z;
            return strength;
        }

        private static Vector3 SanitizeVector(Vector3 value)
        {
            if (float.IsNaN(value.x) || float.IsInfinity(value.x))
                value.x = 0f;
            if (float.IsNaN(value.y) || float.IsInfinity(value.y))
                value.y = 0f;
            if (float.IsNaN(value.z) || float.IsInfinity(value.z))
                value.z = 0f;
            return value;
        }
    }

    /// <summary>
    /// Transform のローカル位置・回転ウィグル、またはローカルスケールスクウィッシュ曲線を作る。
    /// </summary>
    public static class TransformWiggleSquishUtility
    {
        public const int MaxKeysPerCurve = 3600;

        private static readonly string[][] PositionAxes =
        {
            new[] { "m_LocalPosition.x", "localPosition.x" },
            new[] { "m_LocalPosition.y", "localPosition.y" },
            new[] { "m_LocalPosition.z", "localPosition.z" }
        };

        private static readonly string[][] RotationAxes =
        {
            new[] { "localEulerAnglesRaw.x", "localEulerAngles.x" },
            new[] { "localEulerAnglesRaw.y", "localEulerAngles.y" },
            new[] { "localEulerAnglesRaw.z", "localEulerAngles.z" }
        };

        private static readonly string[][] ScaleAxes =
        {
            new[] { "m_LocalScale.x", "localScale.x" },
            new[] { "m_LocalScale.y", "localScale.y" },
            new[] { "m_LocalScale.z", "localScale.z" }
        };

        public static Vector3 BaseValue(TransformWiggleSquishSettings settings)
        {
            switch (settings.mode)
            {
                case TransformWiggleSquishMode.RotationWiggle:
                    return settings.baseRotation;
                case TransformWiggleSquishMode.ScaleSquish:
                    return settings.baseScale;
                default:
                    return settings.basePosition;
            }
        }

        public static float GetAxis(Vector3 value, int axis)
        {
            switch (axis)
            {
                case 0:
                    return value.x;
                case 1:
                    return value.y;
                default:
                    return value.z;
            }
        }

        public static float Evaluate(TransformWiggleSquishSettings settings, int axis, float time)
        {
            return GetAxis(BaseValue(settings), axis) + EvaluateDelta(settings, axis, time);
        }

        public static float EvaluateDelta(TransformWiggleSquishSettings settings, int axis, float time)
        {
            float amplitude = GetAxis(settings.amplitude, axis);
            float phase = GetAxis(settings.timeOffset, axis);
            float omega = Mathf.PI * 2f * settings.frequency;
            float wave = amplitude * Mathf.Sin(omega * (time + phase));
            if (settings.loop)
                return wave + EvaluateNoise(settings, axis, time);

            if (HasSettled(settings, time))
                return 0f;

            float envelope = Mathf.Exp(-settings.damping * Mathf.Max(0f, time));
            return wave * envelope + EvaluateNoise(settings, axis, time);
        }

        public static float EvaluateDerivative(TransformWiggleSquishSettings settings, int axis, float time)
        {
            if (HasNoise(settings))
            {
                if (!settings.loop && HasSettled(settings, time))
                    return 0f;

                float dt = 1f / Mathf.Max(settings.sampleRate, 5f);
                float next = EvaluateDelta(settings, axis, time + dt);
                float previous = EvaluateDelta(settings, axis, time - dt);
                return (next - previous) / (2f * dt);
            }

            float amplitude = GetAxis(settings.amplitude, axis);
            float phase = GetAxis(settings.timeOffset, axis);
            float omega = Mathf.PI * 2f * settings.frequency;
            if (settings.loop)
                return amplitude * omega * Mathf.Cos(omega * (time + phase));

            if (HasSettled(settings, time))
                return 0f;

            float envelope = Mathf.Exp(-settings.damping * Mathf.Max(0f, time));
            float angle = omega * (time + phase);
            float sine = Mathf.Sin(angle);
            float cosine = Mathf.Cos(angle);
            return amplitude * envelope * (omega * cosine - settings.damping * sine);
        }

        /// <summary>
        /// 全軸がベースへスナップし終える時刻。スナップしない場合は PositiveInfinity。
        /// </summary>
        public static float ComputeSettleTime(TransformWiggleSquishSettings settings)
        {
            if (settings.loop)
                return ComputeRequestedDuration(settings);

            float maxTime = 0f;
            bool any = false;
            for (int axis = 0; axis < 3; axis++)
            {
                float amplitude = AxisExtent(settings, axis);
                if (amplitude <= settings.settleThreshold)
                    continue;

                any = true;
                if (settings.damping <= 0.00001f || settings.settleThreshold <= 0f)
                    return float.PositiveInfinity;

                float time = Mathf.Log(amplitude / settings.settleThreshold) / settings.damping;
                if (time > maxTime)
                    maxTime = time;
            }

            return any ? maxTime : 0f;
        }

        public static float ComputeRequestedDuration(TransformWiggleSquishSettings settings)
        {
            float minDuration = 1f / settings.sampleRate;
            if (settings.loop)
            {
                float raw = settings.frequency <= 0.0001f
                    ? settings.maxDuration
                    : Mathf.Max(1, settings.loopCycles) / settings.frequency;
                return Mathf.Max(minDuration, raw);
            }

            float settle = ComputeSettleTime(settings);
            float duration = float.IsPositiveInfinity(settle)
                ? settings.maxDuration
                : Mathf.Min(settings.maxDuration, settle);
            return Mathf.Max(minDuration, duration);
        }

        public static float ComputeDuration(TransformWiggleSquishSettings settings)
        {
            float requested = ComputeRequestedDuration(settings);
            float keyLimited = (MaxKeysPerCurve - 1) / settings.sampleRate;
            return Mathf.Min(requested, keyLimited);
        }

        public static AnimationCurve BuildCurve(TransformWiggleSquishSettings settings, int axis, float startTime)
        {
            float duration = ComputeDuration(settings);
            List<float> times = settings.optimizeCurve
                ? BuildOptimizedTimes(settings, axis, duration)
                : BuildDenseTimes(settings, duration);
            var keys = new List<Keyframe>(times.Count);
            for (int i = 0; i < times.Count; i++)
            {
                float localTime = i == times.Count - 1 ? duration : times[i];
                keys.Add(i == times.Count - 1
                    ? MakeEndKey(settings, axis, localTime)
                    : MakeKey(settings, axis, localTime));
            }

            return FinishCurve(settings, axis, keys, Mathf.Max(0f, startTime));
        }

        public static AnimationClip CreateClip(TransformWiggleSquishSettings settings, string bindingPath, string clipName)
        {
            var clip = new AnimationClip
            {
                name = string.IsNullOrEmpty(clipName) ? "WiggleSquish" : clipName,
                legacy = false
            };
            ApplyToClip(clip, settings, bindingPath, 0f);
            return clip;
        }

        public static void ApplyToClip(AnimationClip clip, TransformWiggleSquishSettings settings, string bindingPath, float startTime)
        {
            if (clip == null)
                return;

            settings.Sanitize();
            if (float.IsNaN(startTime) || float.IsInfinity(startTime) || startTime < 0f)
                startTime = 0f;

            string path = bindingPath ?? "";
            string[][] axes = AxesFor(settings.mode);
            float motionEnd = startTime + ComputeDuration(settings);
            for (int axis = 0; axis < 3; axis++)
            {
                string[] names = axes[axis];
                AnimationCurve existing = ReadCombinedCurve(clip, path, names);
                AnimationCurve generated = BuildCurve(settings, axis, startTime);
                AnimationCurve merged = MergeOutsideKeys(existing, generated, startTime, motionEnd);
                RemoveCurves(clip, path, names);
                var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), names[0]);
                AnimationUtility.SetEditorCurve(clip, binding, merged);
            }

            clip.frameRate = settings.sampleRate;
            clip.legacy = false;

            AnimationClipSettings clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
            clipSettings.loopTime = settings.loop;
            clipSettings.startTime = Mathf.Min(clipSettings.startTime, GetFirstKeyTime(clip));
            clipSettings.stopTime = Mathf.Max(motionEnd, GetLastKeyTime(clip));
            AnimationUtility.SetAnimationClipSettings(clip, clipSettings);
        }

        public static bool HasTargetCurves(AnimationClip clip, string bindingPath, TransformWiggleSquishMode mode)
        {
            if (clip == null)
                return false;

            string path = bindingPath ?? "";
            string[][] axes = AxesFor(mode);
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            for (int i = 0; i < bindings.Length; i++)
            {
                EditorCurveBinding binding = bindings[i];
                if (binding.type != typeof(Transform) || binding.path != path)
                    continue;
                for (int axis = 0; axis < axes.Length; axis++)
                {
                    string[] names = axes[axis];
                    for (int p = 0; p < names.Length; p++)
                    {
                        if (binding.propertyName == names[p])
                            return true;
                    }
                }
            }

            return false;
        }

        public static Transform FindAnimatorRoot(Transform target)
        {
            Transform current = target;
            while (current != null)
            {
                if (current.GetComponent<Animator>() != null)
                    return current;
                current = current.parent;
            }

            return target;
        }

        /// <summary>
        /// root から target への相対パス。target が root 自身なら空文字。階層外なら null。
        /// </summary>
        public static string GetRelativePath(Transform target, Transform root)
        {
            if (target == null || root == null)
                return null;
            if (target == root)
                return "";

            var names = new List<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                names.Add(current.name);
                current = current.parent;
            }

            if (current != root)
                return null;

            names.Reverse();
            return string.Join("/", names);
        }

        public static bool IsHumanoidBone(Transform target, Transform root)
        {
            if (target == null || root == null)
                return false;

            Animator animator = root.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                return false;

            int boneCount = (int)HumanBodyBones.LastBone;
            for (int i = 0; i < boneCount; i++)
            {
                Transform bone = animator.GetBoneTransform((HumanBodyBones)i);
                if (bone == target)
                    return true;
            }

            return false;
        }

        private static List<float> BuildDenseTimes(TransformWiggleSquishSettings settings, float duration)
        {
            float sampleRate = settings.sampleRate;
            var times = new List<float>(Mathf.CeilToInt(duration * sampleRate) + 2);
            int steps = Mathf.Max(0, Mathf.FloorToInt(duration * sampleRate - 0.0001f));
            for (int step = 0; step <= steps; step++)
            {
                float time = step / sampleRate;
                if (time >= duration - 0.00005f)
                    break;
                times.Add(time);
            }

            times.Add(duration);
            return times;
        }

        private static List<float> BuildOptimizedTimes(TransformWiggleSquishSettings settings, int axis, float duration)
        {
            var times = new List<float>(32) { 0f };
            AppendOptimizedKnots(settings, axis, 0f, duration, ComputeOptimizeTolerance(settings), times);
            if (times[times.Count - 1] < duration - 0.00005f)
                times.Add(duration);
            return times;
        }

        private static void AppendOptimizedKnots(
            TransformWiggleSquishSettings settings,
            int axis,
            float t0,
            float t1,
            float tolerance,
            List<float> times)
        {
            float dt = t1 - t0;
            float minSpan = 1f / Mathf.Max(settings.sampleRate, 5f);
            if (dt <= minSpan + 0.0000001f || times.Count >= MaxKeysPerCurve - 1 || HermiteError(settings, axis, t0, t1) <= tolerance)
            {
                times.Add(t1);
                return;
            }

            float split = FindWorstHermiteTime(settings, axis, t0, t1);
            float margin = Mathf.Max(minSpan * 0.5f, dt * 0.15f);
            if (split <= t0 + margin || split >= t1 - margin)
                split = (t0 + t1) * 0.5f;

            AppendOptimizedKnots(settings, axis, t0, split, tolerance, times);
            AppendOptimizedKnots(settings, axis, split, t1, tolerance, times);
        }

        private static float ComputeOptimizeTolerance(TransformWiggleSquishSettings settings)
        {
            float peak = 0.001f;
            for (int axis = 0; axis < 3; axis++)
                peak = Mathf.Max(peak, AxisExtent(settings, axis));
            return Mathf.Max(0.00001f, settings.optimizeTolerance * peak);
        }

        private static float HermiteError(TransformWiggleSquishSettings settings, int axis, float t0, float t1)
        {
            float worst = 0f;
            float dt = t1 - t0;
            int probes = Mathf.Clamp(Mathf.CeilToInt(dt * settings.sampleRate), 8, 24);
            float v0 = Evaluate(settings, axis, t0);
            float v1 = Evaluate(settings, axis, t1);
            float s0 = EvaluateDerivative(settings, axis, t0);
            float s1 = EvaluateDerivative(settings, axis, t1);
            for (int i = 1; i < probes; i++)
            {
                float u = i / (float)probes;
                float time = Mathf.Lerp(t0, t1, u);
                float error = Mathf.Abs(Hermite(u, v0, v1, s0, s1, dt) - Evaluate(settings, axis, time));
                if (error > worst)
                    worst = error;
            }

            return worst;
        }

        private static float FindWorstHermiteTime(TransformWiggleSquishSettings settings, int axis, float t0, float t1)
        {
            float worst = 0f;
            float worstTime = (t0 + t1) * 0.5f;
            float dt = t1 - t0;
            int probes = Mathf.Clamp(Mathf.CeilToInt(dt * settings.sampleRate), 8, 24);
            float v0 = Evaluate(settings, axis, t0);
            float v1 = Evaluate(settings, axis, t1);
            float s0 = EvaluateDerivative(settings, axis, t0);
            float s1 = EvaluateDerivative(settings, axis, t1);
            for (int i = 1; i < probes; i++)
            {
                float u = i / (float)probes;
                float time = Mathf.Lerp(t0, t1, u);
                float error = Mathf.Abs(Hermite(u, v0, v1, s0, s1, dt) - Evaluate(settings, axis, time));
                if (error > worst)
                {
                    worst = error;
                    worstTime = time;
                }
            }

            return worstTime;
        }

        private static float Hermite(float u, float v0, float v1, float slope0, float slope1, float dt)
        {
            float u2 = u * u;
            float u3 = u2 * u;
            float h00 = 2f * u3 - 3f * u2 + 1f;
            float h10 = u3 - 2f * u2 + u;
            float h01 = -2f * u3 + 3f * u2;
            float h11 = u3 - u2;
            return h00 * v0 + h10 * dt * slope0 + h01 * v1 + h11 * dt * slope1;
        }

        private static AnimationCurve FinishCurve(TransformWiggleSquishSettings settings, int axis, List<Keyframe> keys, float startTime)
        {
            if (startTime > 0f)
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    Keyframe shifted = keys[i];
                    shifted.time += startTime;
                    keys[i] = shifted;
                }
            }

            var curve = new AnimationCurve(keys.ToArray());
            const float bezierWeight = 1f / 3f;
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Free);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Free);
                AnimationUtility.SetKeyBroken(curve, i, false);
            }

            for (int i = 0; i < curve.length; i++)
            {
                Keyframe key = curve.keys[i];
                float localTime = key.time - startTime;
                float derivative = EvaluateDerivative(settings, axis, localTime);
                key.inTangent = derivative;
                key.outTangent = derivative;
                if (settings.optimizeCurve)
                {
                    key.weightedMode = WeightedMode.Both;
                    key.inWeight = bezierWeight;
                    key.outWeight = bezierWeight;
                }
                else
                {
                    key.weightedMode = WeightedMode.None;
                }

                curve.MoveKey(i, key);
            }

            curve.preWrapMode = WrapMode.Clamp;
            curve.postWrapMode = settings.loop ? WrapMode.Loop : WrapMode.ClampForever;
            return curve;
        }

        private static Keyframe MakeKey(TransformWiggleSquishSettings settings, int axis, float time)
        {
            float derivative = EvaluateDerivative(settings, axis, time);
            return new Keyframe(time, Evaluate(settings, axis, time), derivative, derivative);
        }

        private static Keyframe MakeEndKey(TransformWiggleSquishSettings settings, int axis, float time)
        {
            if (settings.loop)
            {
                Keyframe start = MakeKey(settings, axis, 0f);
                return new Keyframe(time, start.value, start.inTangent, start.outTangent);
            }

            if (HasSettled(settings, time))
                return new Keyframe(time, GetAxis(BaseValue(settings), axis), 0f, 0f);

            return MakeKey(settings, axis, time);
        }

        private static bool HasSettled(TransformWiggleSquishSettings settings, float time)
        {
            if (settings.settleThreshold <= 0f || settings.damping <= 0.00001f)
                return false;

            for (int axis = 0; axis < 3; axis++)
            {
                float amplitude = AxisExtent(settings, axis);
                if (amplitude <= settings.settleThreshold)
                    continue;

                float remaining = amplitude * Mathf.Exp(-settings.damping * Mathf.Max(0f, time));
                if (remaining > settings.settleThreshold)
                    return false;
            }

            return true;
        }

        private static float AxisExtent(TransformWiggleSquishSettings settings, int axis)
        {
            return Mathf.Abs(GetAxis(settings.amplitude, axis)) + Mathf.Abs(settings.noiseStrength);
        }

        private static bool HasNoise(TransformWiggleSquishSettings settings)
        {
            return Mathf.Abs(settings.noiseStrength) > 0.0000001f;
        }

        private static float EvaluateNoise(TransformWiggleSquishSettings settings, int axis, float time)
        {
            float amount = settings.noiseStrength;
            if (Mathf.Abs(amount) <= 0.0000001f)
                return 0f;

            float unit = SampleUnitNoise(settings, axis, time);
            if (settings.loop)
                return amount * unit;

            float envelope = Mathf.Exp(-settings.damping * Mathf.Max(0f, time));
            return amount * unit * envelope;
        }

        private static float SampleUnitNoise(TransformWiggleSquishSettings settings, int axis, float time)
        {
            float x;
            float y;
            float frequency = settings.noiseFrequency;
            if (settings.loop)
            {
                float duration = Mathf.Max(ComputeDuration(settings), 0.0001f);
                float u = Mathf.Repeat(time, duration) / duration;
                int turns = 0;
                if (frequency > 0.0001f)
                    turns = Mathf.Max(1, Mathf.RoundToInt(frequency * duration));

                float angle = u * Mathf.PI * 2f * turns;
                const float radius = 3.5f;
                x = NoiseOffset(settings.noiseSeed, axis, 0) + Mathf.Cos(angle) * radius;
                y = NoiseOffset(settings.noiseSeed, axis, 1) + Mathf.Sin(angle) * radius;
            }
            else
            {
                x = time * frequency + NoiseOffset(settings.noiseSeed, axis, 0);
                y = NoiseOffset(settings.noiseSeed, axis, 1);
            }

            return (Mathf.PerlinNoise(x, y) - 0.5f) * 2f;
        }

        private static float NoiseOffset(int seed, int axis, int channel)
        {
            unchecked
            {
                int hash = seed * 73856093 ^ (axis + 1) * 19349663 ^ (channel + 1) * 83492791;
                hash &= 0x7fffffff;
                return (hash % 10000) * 0.1f;
            }
        }

        private static float GetFirstKeyTime(AnimationClip clip)
        {
            float start = float.PositiveInfinity;
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            for (int i = 0; i < bindings.Length; i++)
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, bindings[i]);
                if (curve == null || curve.length == 0)
                    continue;
                start = Mathf.Min(start, curve.keys[0].time);
            }

            return float.IsPositiveInfinity(start) ? 0f : start;
        }

        private static float GetLastKeyTime(AnimationClip clip)
        {
            float end = 0f;
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            for (int i = 0; i < bindings.Length; i++)
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, bindings[i]);
                if (curve == null || curve.length == 0)
                    continue;
                end = Mathf.Max(end, curve.keys[curve.length - 1].time);
            }

            return end;
        }

        private static string[][] AxesFor(TransformWiggleSquishMode mode)
        {
            switch (mode)
            {
                case TransformWiggleSquishMode.RotationWiggle:
                    return RotationAxes;
                case TransformWiggleSquishMode.ScaleSquish:
                    return ScaleAxes;
                default:
                    return PositionAxes;
            }
        }

        private static AnimationCurve ReadCombinedCurve(AnimationClip clip, string path, string[] names)
        {
            var keys = new List<Keyframe>();
            bool found = false;
            for (int i = 0; i < names.Length; i++)
            {
                var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), names[i]);
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0)
                    continue;

                found = true;
                Keyframe[] curveKeys = curve.keys;
                for (int k = 0; k < curveKeys.Length; k++)
                {
                    bool duplicate = false;
                    for (int e = 0; e < keys.Count; e++)
                    {
                        if (Mathf.Abs(keys[e].time - curveKeys[k].time) < 0.00005f)
                        {
                            duplicate = true;
                            break;
                        }
                    }

                    if (!duplicate)
                        keys.Add(curveKeys[k]);
                }
            }

            if (!found)
                return null;

            keys.Sort((a, b) => a.time.CompareTo(b.time));
            var combined = new AnimationCurve(keys.ToArray());
            for (int i = 0; i < keys.Count; i++)
                combined.MoveKey(i, keys[i]);
            return combined;
        }

        private static AnimationCurve MergeOutsideKeys(AnimationCurve existing, AnimationCurve generated, float startTime, float endTime)
        {
            const float edge = 0.00005f;
            var keys = new List<Keyframe>();
            if (existing != null)
            {
                Keyframe[] existingKeys = existing.keys;
                for (int i = 0; i < existingKeys.Length; i++)
                {
                    float time = existingKeys[i].time;
                    if (time < startTime - edge || time > endTime + edge)
                        keys.Add(existingKeys[i]);
                }
            }

            Keyframe[] generatedKeys = generated.keys;
            for (int i = 0; i < generatedKeys.Length; i++)
                keys.Add(generatedKeys[i]);

            keys.Sort((a, b) => a.time.CompareTo(b.time));
            var curve = new AnimationCurve(keys.ToArray());
            for (int i = 0; i < keys.Count; i++)
                curve.MoveKey(i, keys[i]);

            curve.preWrapMode = generated.preWrapMode;
            curve.postWrapMode = generated.postWrapMode;
            return curve;
        }

        private static void RemoveCurves(AnimationClip clip, string path, string[] properties)
        {
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            for (int i = 0; i < bindings.Length; i++)
            {
                EditorCurveBinding binding = bindings[i];
                if (binding.type != typeof(Transform) || binding.path != path)
                    continue;

                for (int p = 0; p < properties.Length; p++)
                {
                    if (binding.propertyName != properties[p])
                        continue;
                    AnimationUtility.SetEditorCurve(clip, binding, null);
                    break;
                }
            }
        }
    }
}
