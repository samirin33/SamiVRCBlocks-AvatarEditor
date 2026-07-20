using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    public struct AnimationClipPathReplaceRule
    {
        public string from;
        public string to;
    }

    /// <summary>
    /// AnimationClip のバインドパス検出・置換・削除の共通処理。
    /// </summary>
    public static class AnimationClipBindingPathUtility
    {
        public static IReadOnlyList<string> GetMissingBindingPaths(Transform root, AnimationClip clip)
        {
            var missingPaths = new HashSet<string>();
            if (root == null || clip == null)
                return missingPaths.ToList();

            foreach (var path in CollectBindingPaths(clip))
            {
                if (string.IsNullOrEmpty(path))
                    continue;
                if (root.Find(path) == null)
                    missingPaths.Add(path);
            }

            return missingPaths.OrderBy(p => p).ToList();
        }

        public static int GetMissingBindingPathCount(Transform root, AnimationClip clip)
        {
            return GetMissingBindingPaths(root, clip).Count;
        }

        public static bool ClipHasBindingAtPath(AnimationClip clip, string path)
        {
            if (clip == null || path == null)
                return false;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path == path)
                    return true;
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (binding.path == path)
                    return true;
            }

            return false;
        }

        public static List<AnimationClip> GetClipsWithMissingPath(Transform root, IEnumerable<AnimationClip> scopeClips, string path)
        {
            var result = new List<AnimationClip>();
            if (root == null || string.IsNullOrEmpty(path) || scopeClips == null)
                return result;

            if (root.Find(path) != null)
                return result;

            foreach (var clip in scopeClips)
            {
                if (clip == null || result.Contains(clip))
                    continue;
                if (ClipHasBindingAtPath(clip, path))
                    result.Add(clip);
            }

            return result;
        }

        public static int RemoveBindingsAtPathFromClips(IEnumerable<AnimationClip> clips, string path)
        {
            if (string.IsNullOrEmpty(path))
                return 0;

            var clipArray = clips?.Where(c => c != null).Distinct().ToArray() ?? System.Array.Empty<AnimationClip>();
            if (clipArray.Length == 0)
                return 0;

            Undo.RegisterCompleteObjectUndo(clipArray, "Remove Binding Path");
            var removed = 0;
            foreach (var clip in clipArray)
            {
                var count = RemoveBindingsAtPathWithoutUndo(clip, path);
                if (count > 0)
                    EditorUtility.SetDirty(clip);
                removed += count;
            }

            if (removed > 0)
                AssetDatabase.SaveAssets();

            return removed;
        }

        public static int RemoveMissingBindingsFromClips(Transform root, IEnumerable<AnimationClip> clips, IEnumerable<string> paths = null)
        {
            if (root == null || clips == null)
                return 0;

            var clipList = clips.Where(c => c != null).Distinct().ToList();
            if (clipList.Count == 0)
                return 0;

            var removed = 0;
            if (paths != null)
            {
                var pathList = paths.ToList();
                Undo.RegisterCompleteObjectUndo(clipList.ToArray(), "Remove Missing Bindings");
                foreach (var clip in clipList)
                {
                    var clipRemoved = 0;
                    foreach (var path in pathList)
                        clipRemoved += RemoveBindingsAtPathWithoutUndo(clip, path);
                    if (clipRemoved > 0)
                        EditorUtility.SetDirty(clip);
                    removed += clipRemoved;
                }
            }
            else
            {
                foreach (var clip in clipList)
                    removed += RemoveMissingBindings(root, clip);
            }

            if (removed > 0)
                AssetDatabase.SaveAssets();

            return removed;
        }

        public static int RemoveBindingsAtPath(AnimationClip clip, string path)
        {
            if (clip == null || path == null)
                return 0;

            Undo.RecordObject(clip, "Remove Binding Path");
            var removed = 0;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path != path)
                    continue;
                AnimationUtility.SetEditorCurve(clip, binding, null);
                removed++;
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (binding.path != path)
                    continue;
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                removed++;
            }

            return removed;
        }

        public static int RemoveMissingBindings(Transform root, AnimationClip clip, IEnumerable<string> paths = null)
        {
            if (clip == null || root == null)
                return 0;

            var targetPaths = paths?.ToList() ?? GetMissingBindingPaths(root, clip).ToList();
            if (targetPaths.Count == 0)
                return 0;

            Undo.RecordObject(clip, "Remove Missing Bindings");
            var removed = 0;
            foreach (var path in targetPaths)
                removed += RemoveBindingsAtPathWithoutUndo(clip, path);

            if (removed > 0)
                EditorUtility.SetDirty(clip);

            return removed;
        }

        public static int ReplacePathInClip(AnimationClip clip, string from, string to)
        {
            if (clip == null || string.IsNullOrEmpty(from))
                return 0;

            return ReplacePathsInClip(clip, new List<AnimationClipPathReplaceRule>
            {
                new AnimationClipPathReplaceRule { from = from, to = to ?? "" }
            });
        }

        public static int ReplacePathInClips(IEnumerable<AnimationClip> clips, string from, string to)
        {
            if (string.IsNullOrEmpty(from))
                return 0;

            var clipArray = clips?.Where(c => c != null).ToArray() ?? System.Array.Empty<AnimationClip>();
            if (clipArray.Length == 0)
                return 0;

            Undo.RegisterCompleteObjectUndo(clipArray, "Animation Clip Binding Path Replace");
            var replacedCount = 0;
            foreach (var clip in clipArray)
            {
                replacedCount += ReplacePathInClipWithoutUndo(clip, from, to);
                EditorUtility.SetDirty(clip);
            }

            if (replacedCount > 0)
                AssetDatabase.SaveAssets();

            return replacedCount;
        }

        public static int ReplacePathsInClip(AnimationClip clip, IList<AnimationClipPathReplaceRule> rules)
        {
            if (clip == null || rules == null || rules.Count == 0)
                return 0;

            Undo.RecordObject(clip, "Animation Clip Binding Path Replace");
            var count = ReplacePathsInClipWithoutUndo(clip, rules);
            if (count > 0)
                EditorUtility.SetDirty(clip);
            return count;
        }

        public static string GetPathFromAnimatorRoot(Transform transform)
        {
            if (transform == null)
                return null;

            Transform current = transform;
            Transform animatorRoot = null;

            while (current != null)
            {
                if (current.GetComponent<Animator>() != null)
                {
                    animatorRoot = current;
                    break;
                }
                current = current.parent;
            }

            if (animatorRoot == null)
                return null;

            return GetTransformPath(transform, animatorRoot);
        }

        public static Transform TryResolveTransformFromPath(string path, Transform preferredRoot = null)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            if (preferredRoot != null)
            {
                var fromPreferred = FindChildByPath(preferredRoot, path);
                if (fromPreferred != null)
                    return fromPreferred;
            }

            foreach (var animator in Resources.FindObjectsOfTypeAll<Animator>())
            {
                if (animator == null)
                    continue;

                var found = FindChildByPath(animator.transform, path);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static IEnumerable<string> CollectBindingPaths(AnimationClip clip)
        {
            var paths = new HashSet<string>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                paths.Add(binding.path);
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                paths.Add(binding.path);
            return paths;
        }

        private static int RemoveBindingsAtPathWithoutUndo(AnimationClip clip, string path)
        {
            var removed = 0;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path != path)
                    continue;
                AnimationUtility.SetEditorCurve(clip, binding, null);
                removed++;
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (binding.path != path)
                    continue;
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                removed++;
            }

            return removed;
        }

        private static int ReplacePathInClipWithoutUndo(AnimationClip clip, string from, string to)
        {
            return ReplacePathsInClipWithoutUndo(clip, new List<AnimationClipPathReplaceRule>
            {
                new AnimationClipPathReplaceRule { from = from, to = to ?? "" }
            });
        }

        private static int ReplacePathsInClipWithoutUndo(AnimationClip clip, IList<AnimationClipPathReplaceRule> rules)
        {
            var count = 0;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var newPath = ApplyRules(binding.path, rules);
                if (newPath == binding.path)
                    continue;

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                AnimationUtility.SetEditorCurve(clip, binding, null);
                var newBinding = binding;
                newBinding.path = newPath;
                AnimationUtility.SetEditorCurve(clip, newBinding, curve);
                count++;
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                var newPath = ApplyRules(binding.path, rules);
                if (newPath == binding.path)
                    continue;

                var keyframes = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                var newBinding = binding;
                newBinding.path = newPath;
                AnimationUtility.SetObjectReferenceCurve(clip, newBinding, keyframes);
                count++;
            }

            return count;
        }

        private static string ApplyRules(string path, IList<AnimationClipPathReplaceRule> rules)
        {
            var result = path;
            foreach (var rule in rules)
            {
                if (!string.IsNullOrEmpty(rule.from))
                    result = result.Replace(rule.from, rule.to ?? "");
            }

            return result;
        }

        private static Transform FindChildByPath(Transform root, string path)
        {
            if (root == null)
                return null;

            if (string.IsNullOrEmpty(path))
                return root;

            var current = root;
            foreach (var part in path.Split('/'))
            {
                if (string.IsNullOrEmpty(part))
                    continue;

                Transform child = null;
                for (int i = 0; i < current.childCount; i++)
                {
                    var candidate = current.GetChild(i);
                    if (candidate.name == part)
                    {
                        child = candidate;
                        break;
                    }
                }

                if (child == null)
                    return null;

                current = child;
            }

            return current;
        }

        private static string GetTransformPath(Transform transform, Transform pathRoot = null)
        {
            if (transform == null)
                return null;

            var parts = new List<string>();
            var t = transform;
            if (pathRoot != null)
            {
                while (t != null && t != pathRoot)
                {
                    parts.Add(t.name);
                    t = t.parent;
                }

                if (t != pathRoot)
                    return null;

                parts.Reverse();
            }
            else
            {
                while (t != null)
                {
                    parts.Add(t.name);
                    t = t.parent;
                }

                parts.Reverse();
            }

            return parts.Count > 0 ? string.Join("/", parts) : "";
        }
    }
}
