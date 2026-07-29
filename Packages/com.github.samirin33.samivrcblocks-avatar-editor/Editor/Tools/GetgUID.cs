using UnityEngine;
using UnityEditor;

namespace Samirin33.AvatarEditor.Tools.Editor
{
    public static class GetgUID
    {
        [MenuItem("Assets/Copy GUID", false, 100)]
        public static void CopyGUID()
        {
            var guids = GetSelectedAssetGUIDs();
            if (guids == null || guids.Length == 0) return;

            var text = guids.Length == 1 ? guids[0] : string.Join("\n", guids);
            EditorGUIUtility.systemCopyBuffer = text;
            Debug.Log($"GUIDをコピーしました ({guids.Length}件):\n{text}");
        }

        [MenuItem("Assets/Copy GUID", true, 100)]
        public static bool CopyGUIDValidate()
        {
            return HasSelectedAssets();
        }

        private static bool HasSelectedAssets()
        {
            var guids = Selection.assetGUIDs;
            return guids != null && guids.Length > 0;
        }

        private static string[] GetSelectedAssetGUIDs()
        {
            return Selection.assetGUIDs;
        }
    }
}