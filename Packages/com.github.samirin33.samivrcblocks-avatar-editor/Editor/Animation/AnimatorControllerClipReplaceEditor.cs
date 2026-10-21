#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Samirin33.Editor;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    public class AnimatorControllerClipReplaceEditor : EditorWindow
    {
        private enum TargetSource
        {
            Animator,
            AnimatorController
        }

        private TargetSource _targetSource = TargetSource.AnimatorController;
        private Animator _animator;
        private AnimatorController _animatorController;
        private AnimationClip _sourceClip;
        private AnimationClip _replacementClip;
        private int _previewMatchCount = -1;

        [MenuItem("SBAvatarEditor/Animation/Animator Controller Clip Replace", false, 5)]
        public static void Open()
        {
            var window = GetWindow<AnimatorControllerClipReplaceEditor>("Controller Clip Replace");
            window.minSize = new Vector2(360, 280);
        }

        private void OnGUI()
        {
            SamirinEditorStyleHelper.DrawWithBlueBackground(() =>
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("対象の指定", EditorStyles.boldLabel);
                _targetSource = (TargetSource)EditorGUILayout.EnumPopup("指定方法", _targetSource);

                if (_targetSource == TargetSource.Animator)
                {
                    _animator = (Animator)EditorGUILayout.ObjectField("Animator", _animator, typeof(Animator), true);
                    _animatorController = null;
                }
                else
                {
                    _animatorController = (AnimatorController)EditorGUILayout.ObjectField("Animator Controller", _animatorController, typeof(AnimatorController), false);
                    _animator = null;
                }

                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("置換設定", EditorStyles.boldLabel);
                _sourceClip = (AnimationClip)EditorGUILayout.ObjectField("置換元のClip", _sourceClip, typeof(AnimationClip), false);
                _replacementClip = (AnimationClip)EditorGUILayout.ObjectField("置換後のClip", _replacementClip, typeof(AnimationClip), false);
                EditorGUILayout.HelpBox("置換元のClipと一致している参照のみが置換されます。", MessageType.Info);

                EditorGUILayout.Space(8);

                var controller = GetTargetController();
                if (controller != null)
                {
                    DrawPreview(controller);
                }
                else
                {
                    _previewMatchCount = -1;
                }

                EditorGUILayout.Space(8);
                DrawExecute(controller);
            }, new Rect(0, 0, position.width, position.height));
        }

        private AnimatorController GetTargetController()
        {
            if (_targetSource == TargetSource.AnimatorController && _animatorController != null)
                return _animatorController;

            if (_targetSource == TargetSource.Animator && _animator != null && _animator.runtimeAnimatorController != null)
            {
                var runtime = _animator.runtimeAnimatorController;
                if (runtime is AnimatorOverrideController overrideCtrl)
                    return overrideCtrl.runtimeAnimatorController as AnimatorController;
                return runtime as AnimatorController;
            }

            return null;
        }

        private void DrawPreview(AnimatorController controller)
        {
            EditorGUILayout.LabelField("置換対象", EditorStyles.boldLabel);
            if (_sourceClip == null)
            {
                EditorGUILayout.HelpBox("置換元のClipを指定すると、一致する参照の数を表示できます。", MessageType.None);
            }
            else if (GUILayout.Button("一致する参照数を確認"))
            {
                _previewMatchCount = CountMatchingReferences(controller, _sourceClip);
            }

            if (_sourceClip != null && _previewMatchCount >= 0)
            {
                if (_previewMatchCount > 0)
                    EditorGUILayout.HelpBox($"「{_sourceClip.name}」に一致する参照が {_previewMatchCount} 件あります。", MessageType.Info);
                else
                    EditorGUILayout.HelpBox("置換元のClipに一致する参照は見つかりませんでした。", MessageType.Info);
            }
        }

        private void DrawExecute(AnimatorController controller)
        {
            bool hasController = controller != null;
            bool hasSource = _sourceClip != null;
            bool hasReplacement = _replacementClip != null;

            EditorGUI.BeginDisabledGroup(!hasController || !hasSource || !hasReplacement);
            if (GUILayout.Button("一致するClipを置換", GUILayout.Height(32)))
            {
                ExecuteReplace(controller);
            }
            EditorGUI.EndDisabledGroup();

            if (!hasController)
                EditorGUILayout.HelpBox("Animator または Animator Controller を指定してください。", MessageType.Info);
            else if (!hasSource)
                EditorGUILayout.HelpBox("置換元の Animation Clip を指定してください。", MessageType.Info);
            else if (!hasReplacement)
                EditorGUILayout.HelpBox("置換後の Animation Clip を指定してください。", MessageType.Info);
        }

        private void ExecuteReplace(AnimatorController controller)
        {
            if (controller == null || _sourceClip == null || _replacementClip == null) return;

            int replacedCount = 0;

            Undo.RecordObject(controller, "Animator Controller Clip Replace");

            var layers = controller.layers;
            var layersModified = false;
            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                if (!IsSyncedLayerSharingSourceStateMachine(layers, i))
                    replacedCount += ReplaceClipsInStateMachine(layer.stateMachine, _sourceClip, _replacementClip);

                if (layer.syncedLayerIndex < 0 || layer.syncedLayerIndex >= layers.Length)
                    continue;

                foreach (var state in CollectStates(layers[layer.syncedLayerIndex].stateMachine))
                {
                    var overrideMotion = layer.GetOverrideMotion(state);
                    if (overrideMotion == null) continue;
                    if (overrideMotion == _sourceClip)
                    {
                        layer.SetOverrideMotion(state, _replacementClip);
                        layersModified = true;
                        replacedCount++;
                    }
                    else if (overrideMotion is BlendTree overrideTree)
                    {
                        replacedCount += ReplaceClipsInBlendTree(overrideTree, _sourceClip, _replacementClip);
                    }
                }
            }

            // AnimatorControllerLayer はコピーで返るため、同期レイヤーの Override Motion は layers を再代入しないと反映されない
            if (layersModified)
                controller.layers = layers;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);

            _previewMatchCount = -1;
            Debug.Log($"[AnimatorControllerClipReplace] {replacedCount} 件のClipを置換しました。");
        }

        private static int CountMatchingReferences(AnimatorController controller, AnimationClip sourceClip)
        {
            int count = 0;
            var layers = controller.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                if (!IsSyncedLayerSharingSourceStateMachine(layers, i))
                    count += CountMatchingInStateMachine(layer.stateMachine, sourceClip);

                if (layer.syncedLayerIndex < 0 || layer.syncedLayerIndex >= layers.Length)
                    continue;

                foreach (var state in CollectStates(layers[layer.syncedLayerIndex].stateMachine))
                {
                    var overrideMotion = layer.GetOverrideMotion(state);
                    if (overrideMotion == sourceClip)
                        count++;
                    else if (overrideMotion is BlendTree overrideTree)
                        count += CountMatchingInBlendTree(overrideTree, sourceClip);
                }
            }
            return count;
        }

        private static bool IsSyncedLayerSharingSourceStateMachine(AnimatorControllerLayer[] layers, int index)
        {
            var layer = layers[index];
            if (layer.syncedLayerIndex < 0 || layer.syncedLayerIndex >= layers.Length)
                return false;
            return layer.stateMachine == null || layer.stateMachine == layers[layer.syncedLayerIndex].stateMachine;
        }

        private static List<AnimatorState> CollectStates(AnimatorStateMachine stateMachine)
        {
            var result = new List<AnimatorState>();
            CollectStatesRecursive(stateMachine, result);
            return result;
        }

        private static void CollectStatesRecursive(AnimatorStateMachine stateMachine, List<AnimatorState> result)
        {
            if (stateMachine == null) return;
            foreach (var child in stateMachine.states)
            {
                if (child.state != null)
                    result.Add(child.state);
            }
            foreach (var sub in stateMachine.stateMachines)
                CollectStatesRecursive(sub.stateMachine, result);
        }

        private static int CountMatchingInStateMachine(AnimatorStateMachine stateMachine, AnimationClip sourceClip)
        {
            if (stateMachine == null) return 0;
            int count = 0;
            foreach (var state in stateMachine.states)
            {
                count += CountMatchingInState(state.state, sourceClip);
            }
            foreach (var subStateMachine in stateMachine.stateMachines)
            {
                count += CountMatchingInStateMachine(subStateMachine.stateMachine, sourceClip);
            }
            return count;
        }

        private static int CountMatchingInState(AnimatorState state, AnimationClip sourceClip)
        {
            if (state == null) return 0;
            if (state.motion == sourceClip) return 1;
            if (state.motion is BlendTree blendTree)
                return CountMatchingInBlendTree(blendTree, sourceClip);
            return 0;
        }

        private static int CountMatchingInBlendTree(BlendTree blendTree, AnimationClip sourceClip)
        {
            if (blendTree == null) return 0;
            int count = 0;
            foreach (var child in blendTree.children)
            {
                if (child.motion == sourceClip)
                    count++;
                else if (child.motion is BlendTree childTree)
                    count += CountMatchingInBlendTree(childTree, sourceClip);
            }
            return count;
        }

        private static int ReplaceClipsInStateMachine(AnimatorStateMachine stateMachine, AnimationClip sourceClip, AnimationClip replacementClip)
        {
            if (stateMachine == null) return 0;
            int count = 0;

            foreach (var state in stateMachine.states)
            {
                count += ReplaceClipInState(state.state, sourceClip, replacementClip);
            }

            foreach (var subStateMachine in stateMachine.stateMachines)
            {
                count += ReplaceClipsInStateMachine(subStateMachine.stateMachine, sourceClip, replacementClip);
            }

            return count;
        }

        private static int ReplaceClipInState(AnimatorState state, AnimationClip sourceClip, AnimationClip replacementClip)
        {
            if (state == null) return 0;
            int count = 0;

            if (state.motion == sourceClip)
            {
                Undo.RecordObject(state, "Animator Controller Clip Replace");
                state.motion = replacementClip;
                count++;
            }
            else if (state.motion is BlendTree blendTree)
            {
                count += ReplaceClipsInBlendTree(blendTree, sourceClip, replacementClip);
            }

            return count;
        }

        private static int ReplaceClipsInBlendTree(BlendTree blendTree, AnimationClip sourceClip, AnimationClip replacementClip)
        {
            if (blendTree == null) return 0;
            int count = 0;
            var indicesToReplace = new List<int>();

            var children = blendTree.children;
            for (int i = 0; i < children.Length; i++)
            {
                var child = children[i];
                if (child.motion == sourceClip)
                {
                    indicesToReplace.Add(i);
                    count++;
                }
                else if (child.motion is BlendTree childTree)
                {
                    count += ReplaceClipsInBlendTree(childTree, sourceClip, replacementClip);
                }
            }

            if (indicesToReplace.Count > 0)
            {
                Undo.RecordObject(blendTree, "Animator Controller Clip Replace");
                var so = new SerializedObject(blendTree);
                var childrenProp = so.FindProperty("m_Childs") ?? so.FindProperty("m_ChildMotions");
                if (childrenProp != null)
                {
                    foreach (var idx in indicesToReplace)
                    {
                        if (idx >= childrenProp.arraySize) continue;
                        var childProp = childrenProp.GetArrayElementAtIndex(idx);
                        var motionProp = childProp.FindPropertyRelative("m_Motion") ?? childProp.FindPropertyRelative("motion");
                        if (motionProp != null)
                            motionProp.objectReferenceValue = replacementClip;
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            return count;
        }

    }
    #endif
}
