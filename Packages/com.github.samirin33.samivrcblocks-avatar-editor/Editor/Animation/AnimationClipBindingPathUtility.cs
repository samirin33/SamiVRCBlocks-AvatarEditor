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
    /// ヒエラルキー監視は AnimationClipBindingPathUtility.HierarchyWatcher.cs。
    /// </summary>
    public static partial class AnimationClipBindingPathUtility
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
                SaveClipsIfDirty(clipArray);

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
                SaveClipsIfDirty(clipList);

            return removed;
        }

        public static int RemoveBindingsAtPath(AnimationClip clip, string path)
        {
            if (clip == null || path == null)
                return 0;

            Undo.RecordObject(clip, "Remove Binding Path");
            var removed = RemoveBindingsAtPathWithoutUndo(clip, path);
            if (removed > 0)
                EditorUtility.SetDirty(clip);

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
                var count = ReplacePathInClipWithoutUndo(clip, from, to);
                if (count > 0)
                    EditorUtility.SetDirty(clip);
                replacedCount += count;
            }

            if (replacedCount > 0)
                SaveClipsIfDirty(clipArray);

            return replacedCount;
        }

        /// <summary>
        /// バインドパスを完全一致で置換する。ヒエラルキー変更の自動解決用。Undo と保存は呼び出し側が行う。
        /// </summary>
        public static int ReplaceExactPathsInClips(IEnumerable<AnimationClip> clips, IReadOnlyDictionary<string, string> exactPaths)
        {
            if (exactPaths == null || exactPaths.Count == 0)
                return 0;

            var clipArray = clips?.Where(c => c != null).Distinct().ToArray() ?? System.Array.Empty<AnimationClip>();
            if (clipArray.Length == 0)
                return 0;

            var replacedCount = 0;
            foreach (var clip in clipArray)
            {
                var count = ReplaceExactPathsInClipWithoutUndo(clip, exactPaths);
                if (count > 0)
                    EditorUtility.SetDirty(clip);
                replacedCount += count;
            }

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

        private static void SaveClipsIfDirty(IEnumerable<AnimationClip> clips)
        {
            foreach (var clip in clips)
            {
                if (clip != null && EditorUtility.IsPersistent(clip))
                    AssetDatabase.SaveAssetIfDirty(clip);
            }
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

        private static int ReplaceExactPathsInClipWithoutUndo(AnimationClip clip, IReadOnlyDictionary<string, string> exactPaths)
        {
            return MoveBindingPathsWithoutUndo(clip, path =>
                !string.IsNullOrEmpty(path) && exactPaths.TryGetValue(path, out var newPath) ? newPath ?? "" : path);
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
            return MoveBindingPathsWithoutUndo(clip, path => ApplyRules(path, rules));
        }

        private struct BindingMove
        {
            public EditorCurveBinding From;
            public EditorCurveBinding To;
            public bool IsObjectReference;
        }

        /// <summary>
        /// mapPath でパスが変わるバインディングを付け替える。
        /// 付け替え先に同じプロパティのカーブが残る場合は上書きせずスキップして警告する（入れ替えのような同時移動は許可）。
        /// </summary>
        private static int MoveBindingPathsWithoutUndo(AnimationClip clip, System.Func<string, string> mapPath)
        {
            var curveBindings = AnimationUtility.GetCurveBindings(clip);
            var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);

            var moves = new List<BindingMove>();
            AddMoves(curveBindings, false, mapPath, moves);
            AddMoves(objectBindings, true, mapPath, moves);
            if (moves.Count == 0)
                return 0;

            var movingKeys = new HashSet<string>(moves.Select(m => BindingKey(m.From)));
            var staticKeys = new HashSet<string>();
            foreach (var binding in curveBindings.Concat(objectBindings))
            {
                var key = BindingKey(binding);
                if (!movingKeys.Contains(key))
                    staticKeys.Add(key);
            }

            // スキップした移動元はその場に残るため、次の周回ではそこも埋まっている扱いにする
            var skipped = new bool[moves.Count];
            bool changed;
            do
            {
                changed = false;
                var occupied = new HashSet<string>(staticKeys);
                for (int i = 0; i < moves.Count; i++)
                {
                    if (skipped[i])
                        occupied.Add(BindingKey(moves[i].From));
                }

                for (int i = 0; i < moves.Count; i++)
                {
                    if (skipped[i])
                        continue;
                    if (!occupied.Add(BindingKey(moves[i].To)))
                    {
                        skipped[i] = true;
                        changed = true;
                    }
                }
            } while (changed);

            var accepted = new List<BindingMove>();
            var skippedMoves = new List<BindingMove>();
            for (int i = 0; i < moves.Count; i++)
            {
                if (skipped[i])
                    skippedMoves.Add(moves[i]);
                else
                    accepted.Add(moves[i]);
            }

            if (skippedMoves.Count > 0)
                LogSkippedMoves(clip, skippedMoves);
            if (accepted.Count == 0)
                return 0;

            // 入れ替え（A→B, B→A）で読み出し前に上書きしないよう、読み出し・削除・書き込みを分ける
            var curves = new AnimationCurve[accepted.Count];
            var keyframes = new ObjectReferenceKeyframe[accepted.Count][];
            for (int i = 0; i < accepted.Count; i++)
            {
                if (accepted[i].IsObjectReference)
                    keyframes[i] = AnimationUtility.GetObjectReferenceCurve(clip, accepted[i].From);
                else
                    curves[i] = AnimationUtility.GetEditorCurve(clip, accepted[i].From);
            }

            for (int i = 0; i < accepted.Count; i++)
            {
                if (accepted[i].IsObjectReference)
                    AnimationUtility.SetObjectReferenceCurve(clip, accepted[i].From, null);
                else
                    AnimationUtility.SetEditorCurve(clip, accepted[i].From, null);
            }

            for (int i = 0; i < accepted.Count; i++)
            {
                if (accepted[i].IsObjectReference)
                    AnimationUtility.SetObjectReferenceCurve(clip, accepted[i].To, keyframes[i]);
                else
                    AnimationUtility.SetEditorCurve(clip, accepted[i].To, curves[i]);
            }

            return accepted.Count;
        }

        private static void AddMoves(EditorCurveBinding[] bindings, bool isObjectReference, System.Func<string, string> mapPath, List<BindingMove> moves)
        {
            foreach (var binding in bindings)
            {
                var newPath = mapPath(binding.path);
                if (newPath == null || newPath == binding.path)
                    continue;

                var newBinding = binding;
                newBinding.path = newPath;
                moves.Add(new BindingMove { From = binding, To = newBinding, IsObjectReference = isObjectReference });
            }
        }

        private static string BindingKey(EditorCurveBinding binding)
        {
            return binding.path + "\u001f" + (binding.type != null ? binding.type.FullName : "") + "\u001f" + binding.propertyName;
        }

        private static void LogSkippedMoves(AnimationClip clip, List<BindingMove> skippedMoves)
        {
            const int maxLines = 10;
            var lines = skippedMoves
                .Take(maxLines)
                .Select(m => $"  {m.From.path} → {m.To.path} ({(m.To.type != null ? m.To.type.Name : "?")}.{m.To.propertyName})");
            var more = skippedMoves.Count > maxLines ? $"\n  ...ほか {skippedMoves.Count - maxLines} 件" : "";
            Debug.LogWarning(
                $"[AnimationClipBindingPath] 「{clip.name}」: 置換先に同じプロパティのキーが既にあるため、{skippedMoves.Count} 件のバインディングは置換せず残しました。\n" +
                string.Join("\n", lines) + more,
                clip);
        }

        /// <summary>
        /// rule.from と完全一致するパス、または rule.from + "/" で始まる子孫パスだけを置換する。
        /// 部分文字列一致にすると Body → Body_Shadow や Armature/Body2 まで書き換わってしまう。
        /// </summary>
        private static string ApplyRules(string path, IList<AnimationClipPathReplaceRule> rules)
        {
            var result = path ?? "";
            foreach (var rule in rules)
            {
                if (string.IsNullOrEmpty(rule.from))
                    continue;

                var to = rule.to ?? "";
                if (result == rule.from)
                {
                    result = to;
                }
                else if (result.StartsWith(rule.from + "/", System.StringComparison.Ordinal))
                {
                    var rest = result.Substring(rule.from.Length + 1);
                    result = string.IsNullOrEmpty(to) ? rest : to + "/" + rest;
                }
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
