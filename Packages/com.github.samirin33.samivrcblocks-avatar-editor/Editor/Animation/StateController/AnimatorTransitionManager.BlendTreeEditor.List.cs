using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Samirin33.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;
using AnimatorController = UnityEditor.Animations.AnimatorController;
using AnimatorControllerParameter = UnityEngine.AnimatorControllerParameter;
using AnimatorControllerParameterType = UnityEngine.AnimatorControllerParameterType;
using BlendTree = UnityEditor.Animations.BlendTree;

namespace Samirin33.AvatarEditor.Tools.Editor
{
    public sealed partial class AnimatorTransitionManager : EditorWindow
    {
        // ======================== Children テーブル ========================

        private static void BtEditor_DrawChildren(
            BlendTree bt, AnimatorController controller, string assetPath,
            BlendTreeType blendType, List<string> allParams)
        {
            var btId = bt.GetInstanceID();
            var originalChildren = bt.children ?? Array.Empty<ChildMotion>();
            if (!_btChildBuffers.TryGetValue(btId, out var editableChildren))
            {
                editableChildren = originalChildren.ToList();
                _btChildBuffers[btId] = editableChildren;
            }
            else if (!BtEditor_AreChildrenEqual(originalChildren, editableChildren))
            {
                editableChildren.Clear();
                editableChildren.AddRange(originalChildren);
            }

            if (!_btChildLists.TryGetValue(btId, out var list) || list == null)
            {
                list = new ReorderableList(editableChildren, typeof(ChildMotion), true, true, false, false);
                _btChildLists[btId] = list;
            }
            else
            {
                list.list = editableChildren;
            }

            list.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Motion");
            var sharedGroups = BtEditor_FindSharedMotionGroups(editableChildren);
            var hasAnyShared = sharedGroups.Count > 0;
            list.elementHeight = EditorGUIUtility.singleLineHeight + (hasAnyShared ? 8f : 6f);
            var pendingDeleteIndex = -1;
            list.drawElementCallback = (rect, index, _, _) =>
            {
                if (index < 0 || index >= editableChildren.Count)
                    return;

                var c = editableChildren[index];
                var y = rect.y + 2f;
                var h = EditorGUIUtility.singleLineHeight;
                var x = rect.x;
                var actionsW = 100f;
                var motionW = Mathf.Min(220f, Mathf.Max(100f, rect.width * 0.38f));
                var actionX = rect.xMax - actionsW;

                var motionRect = new Rect(x, y, motionW, h);
                c.motion = (Motion)EditorGUI.ObjectField(motionRect, c.motion, typeof(Motion), false);
                x = motionRect.xMax + 6f;

                if (blendType == BlendTreeType.Simple1D)
                {
                    c.threshold = EditorGUI.FloatField(new Rect(x, y, 64f, h), c.threshold);
                    x += 70f;
                }
                else if (blendType == BlendTreeType.Direct)
                {
                    c.directBlendParameter = DrawTextOrSelectFieldInlineRect(new Rect(x, y, 160f, h), c.directBlendParameter, allParams);
                }
                else
                {
                    var px = EditorGUI.FloatField(new Rect(x, y, 52f, h), c.position.x);
                    x += 56f;
                    var py = EditorGUI.FloatField(new Rect(x, y, 52f, h), c.position.y);
                    x += 58f;
                    c.position = new Vector2(px, py);
                }

                var btnW = 22f;
                var btnGap = 1f;
                // リンク関係表示マーカー
                if (BtEditor_IsLinkedMotion(c))
                {
                    var linkRect = new Rect(actionX - 18f, y, 16f, h);
                    GUI.Label(linkRect, "🔗", EditorStyles.miniLabel);
                }
                var bx = actionX;
                if (GUI.Button(new Rect(bx, y, btnW, h), "C", EditorStyles.miniButton))
                    BtEditor_CopyChildMotion(c, BtCopyMode.Clone);
                bx += btnW + btnGap;
                if (GUI.Button(new Rect(bx, y, btnW, h), "L", EditorStyles.miniButton))
                    BtEditor_CopyChildMotion(c, BtCopyMode.Link);
                bx += btnW + btnGap;
                using (new EditorGUI.DisabledScope(!_btClipboard.HasData))
                {
                    if (GUI.Button(new Rect(bx, y, btnW, h), "P", EditorStyles.miniButton))
                        c = BtEditor_PasteAsChildMotion(c, bt, assetPath);
                }
                bx += btnW + btnGap;
                if (GUI.Button(new Rect(bx, y, btnW, h), "X", EditorStyles.miniButton))
                {
                    pendingDeleteIndex = index;
                }

                editableChildren[index] = c;
            };

            if (blendType == BlendTreeType.Simple1D
                || blendType == BlendTreeType.SimpleDirectional2D
                || blendType == BlendTreeType.FreeformDirectional2D
                || blendType == BlendTreeType.FreeformCartesian2D)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("プレビュー 座標スナップ", GUILayout.Width(130f));
                _btPreviewSnapStep = EditorGUILayout.FloatField(_btPreviewSnapStep, GUILayout.Width(72f));
                if (_btPreviewSnapStep < 0f) _btPreviewSnapStep = 0f;
                if (Mathf.Approximately(_btPreviewSnapStep, 0f)) EditorGUILayout.LabelField("（0=オフ）", EditorStyles.miniLabel, GUILayout.Width(52f));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2f);
            }

            var hasCurrent = BtEditor_TryGetBlendInputPoint(bt, out var currentX, out var currentY);
            BtEditor_DrawChildrenBlendSpacePreview(
                bt, blendType, editableChildren, controller, list, hasCurrent, currentX, currentY);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("空のMotionを追加", GUILayout.Width(128f)))
                    editableChildren.Add(BtEditor_CreateEmptyChildMotion(blendType, allParams));

                if (GUILayout.Button("空のBlendTreeを追加", GUILayout.Width(140f)))
                {
                    if (BtEditor_TryCreateEmbeddedEmptyBlendTree(bt, assetPath, out var subBt))
                    {
                        var c = BtEditor_CreateEmptyChildMotion(blendType, allParams);
                        c.motion = subBt;
                        editableChildren.Add(c);
                    }
                }
            }

            list.DoLayoutList();
            BtEditor_DrawSharedMotionLinks(editableChildren, sharedGroups);
            BtEditor_HandleMotionDropIntoList(GUILayoutUtility.GetLastRect(), editableChildren, blendType, allParams);
            if (pendingDeleteIndex >= 0 && pendingDeleteIndex < editableChildren.Count)
            {
                editableChildren.RemoveAt(pendingDeleteIndex);
                if (list.index >= editableChildren.Count)
                    list.index = editableChildren.Count - 1;
            }

            if (!BtEditor_AreChildrenEqual(originalChildren, editableChildren))
            {
                Undo.RecordObject(bt, "Edit BlendTree Children");
                bt.children = editableChildren.ToArray();
                EditorUtility.SetDirty(bt);
                if (controller != null) EditorUtility.SetDirty(controller);
                InternalEditorUtility.RepaintAllViews();
            }
        }

        private static ChildMotion BtEditor_CreateEmptyChildMotion(BlendTreeType blendType, List<string> allParams)
        {
            var directDefaultParam = string.Empty;
            if (blendType == BlendTreeType.Direct && allParams != null)
            {
                for (var i = 0; i < allParams.Count; i++)
                {
                    var p = allParams[i];
                    if (string.IsNullOrWhiteSpace(p))
                        continue;
                    directDefaultParam = p;
                    break;
                }
            }

            return new ChildMotion
            {
                motion = null,
                threshold = 0f,
                position = Vector2.zero,
                timeScale = 1f,
                cycleOffset = 0f,
                directBlendParameter = directDefaultParam,
                mirror = false
            };
        }

        private static bool BtEditor_TryCreateEmbeddedEmptyBlendTree(BlendTree hostBt, string hostAssetPath, out BlendTree subBt)
        {
            subBt = null;
            if (hostBt == null)
                return false;
            if (!AssetDatabase.Contains(hostBt) || string.IsNullOrEmpty(hostAssetPath))
            {
                Debug.LogWarning("BlendTree のアセット先が取得できないため、空のBlendTreeを追加できません。");
                return false;
            }
            subBt = new BlendTree { name = "New BlendTree", blendType = BlendTreeType.Simple1D };
            AssetDatabase.AddObjectToAsset(subBt, hostBt);
            Undo.RegisterCreatedObjectUndo(subBt, "Add Empty BlendTree");
            BtEditor_ConfigureEmbeddedBlendTree(subBt);
            SaveOwningAssets(hostBt);
            return true;
        }

        private static void BtEditor_HandleMotionDropIntoList(
            Rect listRect, List<ChildMotion> editableChildren, BlendTreeType blendType, List<string> allParams)
        {
            var e = Event.current;
            if (e == null || editableChildren == null)
                return;
            if (!listRect.Contains(e.mousePosition))
                return;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)
                return;

            var refs = DragAndDrop.objectReferences;
            var hasMotion = false;
            for (var i = 0; i < refs.Length; i++)
            {
                if (refs[i] is Motion)
                {
                    hasMotion = true;
                    break;
                }
            }
            if (!hasMotion)
                return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                for (var i = 0; i < refs.Length; i++)
                {
                    if (refs[i] is not Motion motion)
                        continue;
                    var c = BtEditor_CreateEmptyChildMotion(blendType, allParams);
                    c.motion = motion;
                    editableChildren.Add(c);
                }
            }
            e.Use();
        }
    }
}
