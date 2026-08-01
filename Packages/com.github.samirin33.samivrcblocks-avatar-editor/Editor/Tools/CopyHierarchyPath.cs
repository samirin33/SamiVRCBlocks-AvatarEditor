using UnityEngine;
using UnityEditor;

namespace Samirin33.AvatarEditor.Tools.Editor
{
    public static class CopyHierarchyPath
    {
        private const string MenuRoot = "GameObject/Copy Hierarchy Path ";

        [MenuItem(MenuRoot + "From Root", false, 30)]
        public static void CopyPathFromRoot()
        {
            var go = GetTargetGameObject();
            if (go == null) return;

            var path = GetPathFromRoot(go.transform);
            CopyToClipboard(path);
            Debug.Log($"Copied path (from Root): {path}");
        }

        [MenuItem(MenuRoot + "From Root", true, 30)]
        public static bool CopyPathFromRootValidate()
        {
            return GetTargetGameObject() != null;
        }

        [MenuItem(MenuRoot + "From Animator", false, 31)]
        public static void CopyPathFromAnimator()
        {
            var go = GetTargetGameObject();
            if (go == null) return;

            var animatorRoot = FindParentWithAnimator(go.transform);
            if (animatorRoot == null)
            {
                Debug.LogWarning("No parent with Animator (AnimationController) found. Copying path from Root instead.");
                CopyPathFromRoot();
                return;
            }

            var path = GetPathFrom(animatorRoot, go.transform);
            CopyToClipboard(path);
            Debug.Log($"Copied path (from Animator): {path}");
        }

        [MenuItem(MenuRoot + "From Animator", true, 31)]
        public static bool CopyPathFromAnimatorValidate()
        {
            return GetTargetGameObject() != null;
        }

        private static GameObject GetTargetGameObject()
        {
            if (Selection.activeGameObject != null)
                return Selection.activeGameObject;
            return null;
        }

        /// <summary>
        /// Rootから対象オブジェクトまでのヒエラルキーパスを取得
        /// </summary>
        public static string GetPathFromRoot(Transform target)
        {
            if (target == null) return string.Empty;
            var path = target.name;
            var current = target.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }

        /// <summary>
        /// 指定した親から対象オブジェクトまでの相対パスを取得
        /// </summary>
        public static string GetPathFrom(Transform root, Transform target)
        {
            if (root == null || target == null) return string.Empty;
            if (root == target) return target.name;

            var path = target.name;
            var current = target.parent;
            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return current == root ? path : string.Empty;
        }

        /// <summary>
        /// 自身または親でAnimator（RuntimeAnimatorController）を持つオブジェクトを検索
        /// </summary>
        private static Transform FindParentWithAnimator(Transform start)
        {
            var current = start;
            while (current != null)
            {
                var animator = current.GetComponent<Animator>();
                if (animator != null && animator.runtimeAnimatorController != null)
                    return current;
                current = current.parent;
            }
            return null;
        }

        private static void CopyToClipboard(string text)
        {
            EditorGUIUtility.systemCopyBuffer = text;
        }
    }
}
