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
        // 遷移一覧 ReorderableList
        private void DrawEdgeSection(string title, List<TransitionRow> rows, ref ReorderableList reorderable, bool isOutgoingBucket)
        {
            if (rows.Count == 0)
                return;

            EditorGUILayout.LabelField(title, EditorStyles.label);

            EnsureReorderableList(rows, ref reorderable, isOutgoingBucket);
            reorderable.DoLayoutList();
            EditorGUILayout.Space(-EditorGUIUtility.standardVerticalSpacing);
            DrawAddParallelTransitionButtonRow(rows, isOutgoingBucket);
        }

        private void EnsureReorderableList(List<TransitionRow> rows, ref ReorderableList reorderable, bool isOutgoingBucket)
        {
            if (reorderable != null && reorderable.list == rows)
            {
                ApplyReorderableListCompactChrome(reorderable);
                return;
            }

            var bucket = isOutgoingBucket ? FocusedListBucket.Outgoing : FocusedListBucket.Incoming;

            reorderable = new ReorderableList(rows, typeof(TransitionRow), true, false, false, false)
            {
                drawElementCallback = (rect, index, _, _) =>
                {
                    if (index < 0 || index >= rows.Count) return;
                    var item = rows[index];
                    if (_selectionBucket == bucket && _selectedRowIndices.Contains(index))
                        EditorGUI.DrawRect(rect, new Color(0.25f, 0.45f, 0.85f, 0.18f));

                    ResolveEndpoints(item.group, item.transition,
                        out var srcLabel, out var srcObj,
                        out var dstLabel, out var dstObj);

                    var y = rect.y + 1f;
                    var h = EditorGUIUtility.singleLineHeight;
                    var x = rect.x + 12f;

                    var idxRect = new Rect(x, y, 28f, h);
                    x += 28f;
                    EditorGUI.LabelField(idxRect, $"{index + 1}.", EditorStyles.miniLabel);

                    var remaining = rect.xMax - x - 4f;
                    var iconW = 18f;
                    var gap = 4f;
                    const float actionBtnW = 22f;
                    var actionReserve = actionBtnW * 3f + gap * 2f;
                    var pairW = Mathf.Max(40f, (remaining - iconW - actionReserve - gap) * 0.5f);

                    var srcRect = new Rect(x, y, pairW, h);
                    x += pairW + gap * 0.5f;
                    var iconRect = new Rect(x, y, iconW, h);
                    x += iconW + gap * 0.5f;
                    var dstRect = new Rect(x, y, pairW, h);
                    x += pairW + gap * 0.5f;

                    var activeState = Selection.activeObject as AnimatorState;
                    var isSourceSelfState = activeState != null && ReferenceEquals(srcObj, activeState);
                    var isDestinationSelfState = activeState != null && ReferenceEquals(dstObj, activeState);

                    if (isSourceSelfState)
                        EditorGUI.LabelField(srcRect, srcLabel, CenteredLabelStyle);
                    else if (GUI.Button(srcRect, srcLabel, EditorStyles.miniButton))
                        SelectForInspector(srcObj);

                    GUI.Label(iconRect, BetweenIcon, EditorStyles.label);

                    if (isDestinationSelfState)
                        EditorGUI.LabelField(dstRect, dstLabel, CenteredLabelStyle);
                    else if (GUI.Button(dstRect, dstLabel, EditorStyles.miniButton))
                        SelectForInspector(dstObj);

                    var copyRect = new Rect(x, y, actionBtnW, h);
                    x += actionBtnW + gap;
                    var pasteOwRect = new Rect(x, y, actionBtnW, h);
                    x += actionBtnW + gap;
                    var deleteRect = new Rect(x, y, actionBtnW, h);

                    var e = Event.current;
                    var rowSelectableRect = new Rect(rect.x, rect.y, rect.width, rect.height);
                    var clickedOnInteractiveControl =
                        copyRect.Contains(e.mousePosition) ||
                        pasteOwRect.Contains(e.mousePosition) ||
                        deleteRect.Contains(e.mousePosition);

                    if (e.type == EventType.MouseDown && e.button == 0 && rowSelectableRect.Contains(e.mousePosition) &&
                        !clickedOnInteractiveControl)
                    {
                        var addToSelection = e.control || e.command;
                        if (addToSelection)
                        {
                            if (_selectionBucket != bucket)
                            {
                                _selectionBucket = bucket;
                                _selectedRowIndices.Clear();
                            }

                            if (!_selectedRowIndices.Add(index))
                                _selectedRowIndices.Remove(index);
                        }
                        else
                        {
                            _selectionBucket = bucket;
                            _selectedRowIndices.Clear();
                            _selectedRowIndices.Add(index);
                        }

                        if (isOutgoingBucket)
                        {
                            if (_reorderIncoming != null)
                                _reorderIncoming.index = -1;
                        }
                        else if (_reorderOutgoing != null)
                        {
                            _reorderOutgoing.index = -1;
                        }

                        _lastConditionBufferSignature = "";
                        e.Use();
                        Repaint();
                    }

                    if (GUI.Button(copyRect, CopyIcon))
                    {
                        AnimatorTransitionMultiCopy.CopyMergedSettings(item.transition);
                        Repaint();
                    }

                    if (GUI.Button(pasteOwRect, PasteOverwriteIcon))
                    {
                        var preservedBucket = _selectionBucket;
                        var preservedTransitionIds = GetSelectedRows()
                            .Select(r => r.transition)
                            .Where(t => t != null)
                            .Select(t => t.GetInstanceID())
                            .ToHashSet();
                        if (AnimatorTransitionMultiCopy.TryPasteMergedOverwrite(item.transition))
                        {
                            AssetDatabase.SaveAssets();
                            RefreshSelection();
                            RestoreRowSelection(preservedBucket, preservedTransitionIds);
                            InternalEditorUtility.RepaintAllViews();
                        }
                        Repaint();
                    }

                    if (GUI.Button(deleteRect, DeleteIcon))
                    {
                        var tr = item.transition;
                        EditorApplication.delayCall += () =>
                        {
                            if (tr == null)
                                return;
                            if (AnimatorTransitionEditOperations.TryDeleteTransition(tr, "Delete Transition"))
                            {
                                AssetDatabase.SaveAssets();
                                RefreshSelection();
                                Repaint();
                            }
                        };
                    }
                },
                elementHeight = EditorGUIUtility.singleLineHeight + 4f
            };

            ApplyReorderableListCompactChrome(reorderable);

            reorderable.onReorderCallbackWithDetails = (_, oldIndex, newIndex) =>
            {
                if (oldIndex == newIndex) return;
                _selectedRowIndices.Clear();
                _selectionBucket = FocusedListBucket.None;
                _lastConditionBufferSignature = "";
                ApplyOrder(rows);
                Repaint();
            };
        }

        /// <summary>
        /// 追加／削除ボタン非表示でもフッター高さが残ることがあるため、下のボタン行との隙間を詰める。
        /// </summary>
        private static void ApplyReorderableListCompactChrome(ReorderableList list)
        {
            if (list == null)
                return;
            list.footerHeight = 0f;
            list.headerHeight = 0f;
        }

        private void DrawAddParallelTransitionButtonRow(List<TransitionRow> rows, bool isOutgoingBucket)
        {
            if (rows == null || rows.Count == 0)
                return;

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button($"トランジションを追加", GUILayout.MinWidth(260f), GUILayout.Height(25f)))
                TryAddParallelTransition(rows, isOutgoingBucket);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void TryAddParallelTransition(List<TransitionRow> rows, bool isOutgoingBucket)
        {
            if (rows == null || rows.Count == 0)
                return;

            var bucket = isOutgoingBucket ? FocusedListBucket.Outgoing : FocusedListBucket.Incoming;
            TransitionRow template = null;

            if (_selectionBucket == bucket && _selectedRowIndices.Count > 0)
            {
                var ix = _selectedRowIndices.OrderBy(i => i).First();
                if (ix >= 0 && ix < rows.Count)
                    template = rows[ix];
            }

            template ??= rows[0];

            var t = template.transition;
            var c = template.group?.controller;
            if (c == null || t == null)
                return;

            var loc = AnimatorTransitionEditOperations.FindTransitionLocation(t, c);
            if (loc == null)
                return;

            Undo.RegisterCompleteObjectUndo(c, "Add Parallel Transition");
            var neu = AnimatorTransitionEditOperations.TryCreateParallelTransition(loc);
            if (neu == null)
            {
                EditorUtility.DisplayDialog("AnimatorStateController", "同じ経路のトランジションを追加できませんでした。", "OK");
                return;
            }

            EditorUtility.SetDirty(c);
            AssetDatabase.SaveAssets();
            // RefreshSelection() は Selection だけから再構築するため、未選択の新規トランジションが一覧に載らない。
            // 追加した行は現在のバケットのリストへ直接載せる（rows は _outgoing または _incoming と同一参照）。
            var g = FindGroup(neu);
            if (g != null)
            {
                rows.Add(new TransitionRow { transition = neu, group = g });
                if (isOutgoingBucket)
                    _reorderOutgoing = null;
                else
                    _reorderIncoming = null;
            }
            else
            {
                RefreshSelection();
            }

            Repaint();
            InternalEditorUtility.RepaintAllViews();
        }

        private static GUIContent BetweenIcon =>
            _cachedBetweenIcon ??= EditorGUIUtility.IconContent("d_preAudioPlayOn");

        private static GUIContent CopyIcon =>
            _cachedCopyIcon ??= EditorGUIUtility.IconContent("Grid.PickingTool", "コピー");

        private static GUIContent PasteOverwriteIcon =>
            _cachedPasteOwIcon ??= EditorGUIUtility.IconContent("Grid.FillTool", "ペースト");

        private static GUIContent DeleteIcon =>
            _cachedDeleteIcon ??= EditorGUIUtility.IconContent("winbtn_win_close", "削除");
    }
}
