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
        // クリップボード
        private void DrawClipboardSummary()
        {
            var ctrls = GetControllersForParameterMenu();
            if (ctrls.Count == 0)
            {
                var rowsForCtrl = GetTransitionRowsForSettingsPanel();
                ctrls = CollectAnimatorControllers(rowsForCtrl.Select(r => r.transition).Where(t => t != null).ToList());
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            _foldoutClipboardPanel = EditorGUILayout.Foldout(_foldoutClipboardPanel, "クリップボード", true,
                FoldoutStyleNormal);
            if (!_foldoutClipboardPanel)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            var items = AnimatorTransitionMultiCopy.GetMergedClipboardItems();
            if (items.Count == 0)
            {
                HelpBoxFullWidth("コピーされた設定はありません。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            EnsureClipboardSlotFoldouts(items.Count);

            for (var i = 0; i < items.Count; i++)
            {
                var s = items[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
                _clipboardSlotFold[i] = EditorGUILayout.Foldout(_clipboardSlotFold[i], $"{i + 1}. ", true,
                    FoldoutStyleNormal);
                if (_clipboardSlotFold[i])
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
                    EditorGUILayout.LabelField("ブレンド / 中断", EditorStyles.label);
                    DrawClipboardBlendReadOnly(s);
                    EditorGUILayout.EndVertical();

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
                    EditorGUILayout.LabelField("条件", EditorStyles.label);
                    DrawClipboardConditionsReadOnly(s, ctrls);
                    EditorGUILayout.EndVertical();
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(4f);
            }

            EditorGUILayout.EndVertical();
        }

        private void EnsureClipboardSlotFoldouts(int count)
        {
            while (_clipboardSlotFold.Count < count)
            {
                _clipboardSlotFold.Add(false);
                _clipboardSlotFoldBlend.Add(false);
                _clipboardSlotFoldConditions.Add(false);
            }

            while (_clipboardSlotFold.Count > count)
            {
                var last = _clipboardSlotFold.Count - 1;
                _clipboardSlotFold.RemoveAt(last);
                _clipboardSlotFoldBlend.RemoveAt(last);
                _clipboardSlotFoldConditions.RemoveAt(last);
            }
        }

        private static void DrawClipboardBlendReadOnly(AnimatorTransitionEditOperations.TransitionSettings s)
        {
            if (s == null)
                return;

            if (!s.hasBlendSettings)
            {
                EditorGUILayout.LabelField("（ブレンド設定なし / Entry 等）", EditorStyles.wordWrappedLabel);
                return;
            }

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.FloatField("Duration", s.duration);
            EditorGUILayout.FloatField("Offset", s.offset);
            EditorGUILayout.Toggle("Has Exit Time", s.hasExitTime);
            EditorGUILayout.FloatField("Exit Time", s.exitTime);
            EditorGUILayout.Toggle("Fixed Duration", s.hasFixedDuration);
            EditorGUILayout.EnumPopup("Interruption Source", s.interruptionSource);
            EditorGUILayout.Toggle("Ordered Interruption", s.orderedInterruption);
            if (s.isFromAnyState)
                EditorGUILayout.Toggle("Can Transition To Self", s.canTransitionToSelf);
            EditorGUI.EndDisabledGroup();
        }

        private static void DrawClipboardConditionsReadOnly(AnimatorTransitionEditOperations.TransitionSettings s,
            List<AnimatorController> ctrls)
        {
            if (s == null)
                return;

            if (s.conditions == null || s.conditions.Length == 0)
            {
                EditorGUILayout.LabelField("（条件なし）", EditorStyles.wordWrappedLabel);
                return;
            }

            foreach (var c in s.conditions)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
                DrawClipboardConditionRow(c, ctrls);
                EditorGUILayout.EndVertical();
            }
        }

        private static void DrawClipboardConditionRow(AnimatorTransitionEditOperations.ConditionData c,
            List<AnimatorController> ctrls)
        {
            var pType = ResolveParameterType(ctrls, c.parameter);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(FormatConditionModeDisplay(c.mode), GUILayout.Width(132f));
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField(string.IsNullOrEmpty(c.parameter) ? "(未設定)" : c.parameter,
                EditorStyles.wordWrappedLabel, GUILayout.ExpandWidth(true));
            EditorGUI.BeginDisabledGroup(true);
            DrawClipboardConditionValuePreview(pType, c.mode, c.threshold);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawClipboardConditionValuePreview(AnimatorControllerParameterType? pType,
            AnimatorConditionMode mode, float threshold)
        {
            switch (pType)
            {
                case AnimatorControllerParameterType.Bool:
                    EditorGUILayout.Toggle(mode == AnimatorConditionMode.If, GUILayout.Width(64f));
                    GUILayout.Label("(Bool)", EditorStyles.miniLabel, GUILayout.Width(40f));
                    break;
                case AnimatorControllerParameterType.Trigger:
                    EditorGUILayout.LabelField("—", GUILayout.Width(72f));
                    GUILayout.Label("(Trigger)", EditorStyles.miniLabel, GUILayout.Width(56f));
                    break;
                case AnimatorControllerParameterType.Int:
                    EditorGUILayout.IntField(Mathf.RoundToInt(threshold), GUILayout.Width(88f));
                    GUILayout.Label("(Int)", EditorStyles.miniLabel, GUILayout.Width(36f));
                    break;
                case AnimatorControllerParameterType.Float:
                    EditorGUILayout.FloatField(threshold, GUILayout.Width(88f));
                    GUILayout.Label("(Float)", EditorStyles.miniLabel, GUILayout.Width(44f));
                    break;
                default:
                    EditorGUILayout.FloatField(threshold, GUILayout.Width(88f));
                    GUILayout.Label("(値)", EditorStyles.miniLabel, GUILayout.Width(36f));
                    break;
            }
        }
    }
}
