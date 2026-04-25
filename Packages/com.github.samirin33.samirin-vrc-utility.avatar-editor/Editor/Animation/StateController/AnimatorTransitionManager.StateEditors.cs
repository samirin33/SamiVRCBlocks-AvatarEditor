using System;
using System.Collections.Generic;
using System.Linq;
using Samirin33.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;
using AnimatorController = UnityEditor.Animations.AnimatorController;
using AnimatorControllerParameter = UnityEngine.AnimatorControllerParameter;
using AnimatorControllerParameterType = UnityEngine.AnimatorControllerParameterType;


namespace Samirin33.AvatarEditor.Tools.Editor
{
    public sealed partial class AnimatorTransitionManager : EditorWindow
    {
        // State / SubState インスペクタ
        private void DrawSelectedStateEditor()
        {
            if (Selection.activeObject is not AnimatorState state)
                return;

            var path = AssetDatabase.GetAssetPath(state);
            if (string.IsNullOrEmpty(path))
                return;

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField("ステート設定", EditorStyles.label);

            var changed = false;
            var newName = EditorGUILayout.TextField("ステート名", state.name);
            var newMotion = (Motion)EditorGUILayout.ObjectField("AnimationClip / Motion", state.motion, typeof(Motion), false);


            var useMotionTime = state.timeParameterActive;

            var newSpeed = state.speed;
            var newSpeedMultiplierEnabled = state.speedParameterActive;
            var newSpeedMultiplierParameter = state.speedParameter;
            var newMotionTimeParameter = state.timeParameter;

            if (!useMotionTime)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    newSpeed = EditorGUILayout.FloatField("Speed", state.speed);
                    newSpeedMultiplierEnabled = EditorGUILayout.ToggleLeft("Multiplier", state.speedParameterActive, GUILayout.Width(90f));
                    if (newMotion is AnimationClip clipForLoop)
                    {
                        var animSettings = AnimationUtility.GetAnimationClipSettings(clipForLoop);
                        var newLoopTime = EditorGUILayout.ToggleLeft(
                            new GUIContent("ループ", "アニメーションの Loop Time インポート設定です。"), animSettings.loopTime);
                        if (newLoopTime != animSettings.loopTime)
                        {
                            Undo.RecordObject(clipForLoop, "Set Animation Clip Loop");
                            animSettings.loopTime = newLoopTime;
                            AnimationUtility.SetAnimationClipSettings(clipForLoop, animSettings);
                            EditorUtility.SetDirty(clipForLoop);
                        }
                    }
                }

                if (newSpeedMultiplierEnabled)
                {
                    var floatParams = GetFloatParameterNames(controller);
                    newSpeedMultiplierParameter = DrawFloatParameterPopup("Multiplier", state.speedParameter, floatParams);
                }
            }

            useMotionTime = EditorGUILayout.ToggleLeft("MotionTime を使用", useMotionTime);
            if (useMotionTime)
            {
                var floatParams = GetFloatParameterNames(controller);
                newMotionTimeParameter = DrawFloatParameterPopup("MotionTime", state.timeParameter, floatParams);
            }

            var newWriteDefaults = EditorGUILayout.Toggle("Write Default", state.writeDefaultValues);

            if (newName != state.name) changed = true;
            if (newMotion != state.motion) changed = true;
            if (useMotionTime != state.timeParameterActive) changed = true;
            if (!useMotionTime && !Mathf.Approximately(newSpeed, state.speed)) changed = true;
            if (!useMotionTime && newSpeedMultiplierEnabled != state.speedParameterActive) changed = true;
            if (!useMotionTime && newSpeedMultiplierEnabled && newSpeedMultiplierParameter != state.speedParameter) changed = true;
            if (useMotionTime && newMotionTimeParameter != state.timeParameter) changed = true;
            if (newWriteDefaults != state.writeDefaultValues) changed = true;

            if (changed)
            {
                Undo.RecordObject(state, "Edit Animator State");
                state.name = newName;
                state.motion = newMotion;
                state.timeParameterActive = useMotionTime;
                if (useMotionTime)
                {
                    state.timeParameter = newMotionTimeParameter;
                    state.speedParameterActive = false;
                }
                else
                {
                    state.speed = newSpeed;
                    state.speedParameterActive = newSpeedMultiplierEnabled;
                    state.speedParameter = newSpeedMultiplierEnabled ? newSpeedMultiplierParameter : string.Empty;
                }

                state.writeDefaultValues = newWriteDefaults;
                EditorUtility.SetDirty(state);
                EditorUtility.SetDirty(controller);
                InternalEditorUtility.RepaintAllViews();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8f);
        }

        /// <summary>
        /// ネストしたサブステートマシン（サブステート）選択時に名前を編集する。
        /// レイヤー直下のルート <see cref="AnimatorStateMachine"/> はここでは扱わない。
        /// </summary>
        private void DrawSelectedSubStateMachineNameEditor()
        {
            if (Selection.activeObject is not AnimatorStateMachine stateMachine)
                return;

            var path = AssetDatabase.GetAssetPath(stateMachine);
            if (string.IsNullOrEmpty(path))
                return;

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
                return;

            if (!IsNestedStateMachine(stateMachine, controller))
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField("サブステート", EditorStyles.label);
            var newName = EditorGUILayout.TextField("名前", stateMachine.name);
            if (newName == stateMachine.name)
            {
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(8f);
                return;
            }

            Undo.RecordObject(stateMachine, "Rename SubStateMachine");
            stateMachine.name = newName;
            EditorUtility.SetDirty(stateMachine);
            EditorUtility.SetDirty(controller);
            InternalEditorUtility.RepaintAllViews();

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8f);
        }

        private static bool IsNestedStateMachine(AnimatorStateMachine sm, AnimatorController controller)
        {
            if (sm == null || controller == null)
                return false;

            foreach (var layer in controller.layers)
            {
                if (ReferenceEquals(layer.stateMachine, sm))
                    return false;
            }

            return true;
        }
    }
}
