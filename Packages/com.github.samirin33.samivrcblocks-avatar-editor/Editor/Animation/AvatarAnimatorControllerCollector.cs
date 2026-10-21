using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    /// <summary>Animator Controller と、その表示名（例: "FX: MyFX"）。</summary>
    internal struct AvatarControllerEntry
    {
        public string Label;
        public RuntimeAnimatorController Controller;
    }

    /// <summary>
    /// Animator / VRCAvatarDescriptor / MA Merge Animator から、アバタールート基準のパスで動く Animator Controller を集める。
    /// VRC SDK と MA はこのアセンブリから直接参照できないためリフレクションで読む。
    /// </summary>
    internal static class AvatarAnimatorControllerCollector
    {
        private const string DescriptorTypeName = "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor";
        private const string MergeAnimatorTypeName = "nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();

        /// <summary>
        /// Animator 自身の Controller と、同じ GameObject の VRCAvatarDescriptor の Playable Layers（Default 以外）を返す。
        /// 同じ Controller は 1 回だけ含める。
        /// </summary>
        public static List<AvatarControllerEntry> GetControllers(Animator animator)
        {
            var result = new List<AvatarControllerEntry>();
            if (animator == null)
                return result;

            if (animator.runtimeAnimatorController != null)
                AddUnique(result, "Animator", animator.runtimeAnimatorController);

            foreach (var entry in GetDescriptorControllers(animator.gameObject))
                AddUnique(result, entry.Label, entry.Controller);

            return result;
        }

        /// <summary>GameObject に付いた VRCAvatarDescriptor の Base / Special レイヤーの Controller（Default 以外）。</summary>
        public static List<AvatarControllerEntry> GetDescriptorControllers(GameObject avatarRoot)
        {
            var result = new List<AvatarControllerEntry>();
            if (avatarRoot == null)
                return result;

            var descriptorType = FindType(DescriptorTypeName);
            if (descriptorType == null)
                return result;

            var descriptor = avatarRoot.GetComponent(descriptorType);
            if (descriptor == null)
                return result;

            CollectLayers(descriptor, "baseAnimationLayers", result);
            CollectLayers(descriptor, "specialAnimationLayers", result);
            return result;
        }

        /// <summary>
        /// ルート配下の MA Merge Animator のうち、クリップのパスがルート基準になるもの（Absolute、またはルート直付けの Relative）の Controller。
        /// それ以外は Merge Animator 位置からの相対パスなので、ルート基準のパス置換対象にしてはいけない。
        /// </summary>
        public static List<RuntimeAnimatorController> GetRootRelativeMergeAnimatorControllers(Transform root)
        {
            var result = new List<RuntimeAnimatorController>();
            if (root == null)
                return result;

            var mergeType = FindType(MergeAnimatorTypeName);
            if (mergeType == null)
                return result;

            var animatorField = mergeType.GetField("animator", Flags);
            var pathModeField = mergeType.GetField("pathMode", Flags);
            var relativeRootField = mergeType.GetField("relativePathRoot", Flags);
            if (animatorField == null || pathModeField == null)
                return result;

            foreach (var component in root.GetComponentsInChildren(mergeType, true))
            {
                if (component == null)
                    continue;
                if (!(animatorField.GetValue(component) is RuntimeAnimatorController controller) || controller == null)
                    continue;

                var pathMode = pathModeField.GetValue(component)?.ToString();
                var usesRootPaths = pathMode == "Absolute"
                    || (pathMode == "Relative" && component.transform == root && IsEmptyObjectReference(relativeRootField?.GetValue(component)));
                if (usesRootPaths && !result.Contains(controller))
                    result.Add(controller);
            }

            return result;
        }

        private static bool IsEmptyObjectReference(object reference)
        {
            if (reference == null)
                return true;
            var pathField = reference.GetType().GetField("referencePath", Flags);
            if (pathField == null)
                return false;
            return string.IsNullOrEmpty(pathField.GetValue(reference) as string);
        }

        private static void CollectLayers(Component descriptor, string fieldName, List<AvatarControllerEntry> result)
        {
            var field = descriptor.GetType().GetField(fieldName, Flags);
            if (!(field?.GetValue(descriptor) is IEnumerable layers))
                return;

            foreach (var layer in layers)
            {
                if (layer == null)
                    continue;
                var layerType = layer.GetType();
                var isDefault = layerType.GetField("isDefault", Flags)?.GetValue(layer) is bool b && b;
                if (isDefault)
                    continue;
                if (!(layerType.GetField("animatorController", Flags)?.GetValue(layer) is RuntimeAnimatorController controller) || controller == null)
                    continue;

                var typeLabel = layerType.GetField("type", Flags)?.GetValue(layer)?.ToString() ?? fieldName;
                AddUnique(result, typeLabel, controller);
            }
        }

        private static void AddUnique(List<AvatarControllerEntry> list, string label, RuntimeAnimatorController controller)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Controller == controller)
                    return;
            }

            list.Add(new AvatarControllerEntry { Label = label, Controller = controller });
        }

        private static Type FindType(string fullName)
        {
            if (TypeCache.TryGetValue(fullName, out var cached))
                return cached;

            Type found = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                found = assembly.GetType(fullName, false);
                if (found != null)
                    break;
            }

            TypeCache[fullName] = found;
            return found;
        }
    }
}
