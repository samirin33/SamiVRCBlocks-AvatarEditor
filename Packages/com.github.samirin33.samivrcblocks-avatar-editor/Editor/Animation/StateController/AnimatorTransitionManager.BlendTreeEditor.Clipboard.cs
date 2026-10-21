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
        // ---- シリアライズ用データクラス ----

        [Serializable]
        private class BlendTreeNodeData
        {
            public string name;
            public int blendType;
            public string blendParameter;
            public string blendParameterY;
            public float minThreshold;
            public float maxThreshold;
            public bool useAutomaticThresholds;
            public bool normalizeBlendValues;
            public List<BlendTreeChildNodeData> children = new List<BlendTreeChildNodeData>();
        }

        [Serializable]
        private class BlendTreeChildNodeData
        {
            public bool isEmbeddedBlendTree;
            public BlendTreeNodeData subTree;
            public string motionGuid;
            public float threshold;
            public float posX;
            public float posY;
            public float timeScale = 1f;
            public float cycleOffset;
            public string directBlendParameter;
            public bool mirror;
        }

        // ---- 統一クリップボード ----

        private enum BtClipboardKind { None, BlendTree, ChildMotion }
        private enum BtCopyMode { Clone, Link }

        private sealed class BtClipboard
        {
            public BtClipboardKind kind = BtClipboardKind.None;
            public BtCopyMode copyMode = BtCopyMode.Clone;

            // BlendTree 全体コピー用
            public string blendTreeJson;

            // ChildMotion 単体コピー用
            public ChildMotion childMotion;

            // Link コピー時に元の Motion の InstanceID を記録（リンク関係表示用）
            public int linkedMotionInstanceId;
            // Link コピー時に元の BlendTree の InstanceID を記録
            public int linkedBlendTreeInstanceId;

            public bool HasData => kind != BtClipboardKind.None;
        }

        private static readonly BtClipboard _btClipboard = new BtClipboard();

        // ======================== コピー / ペースト ========================

        // ======================== 統一コピー / ペースト ========================

        /// <summary>BlendTree全体をクリップボードにコピーする。</summary>
        private static void BtEditor_CopyBlendTree(BlendTree bt, BtCopyMode mode)
        {
            if (bt == null) return;
            _btClipboard.kind = BtClipboardKind.BlendTree;
            _btClipboard.copyMode = mode;
            _btClipboard.blendTreeJson = JsonUtility.ToJson(BtEditor_Serialize(bt), true);
            _btClipboard.childMotion = default;
            _btClipboard.linkedBlendTreeInstanceId = bt.GetInstanceID();
            _btClipboard.linkedMotionInstanceId = 0;
        }

        /// <summary>ChildMotion単体をクリップボードにコピーする。</summary>
        private static void BtEditor_CopyChildMotion(ChildMotion c, BtCopyMode mode)
        {
            _btClipboard.kind = BtClipboardKind.ChildMotion;
            _btClipboard.copyMode = mode;
            _btClipboard.childMotion = c;
            _btClipboard.blendTreeJson = null;
            _btClipboard.linkedMotionInstanceId = c.motion != null ? c.motion.GetInstanceID() : 0;
            _btClipboard.linkedBlendTreeInstanceId = 0;

            // ChildMotion もシリアライズしておく（複製ペースト用にBlendTree子ツリーの場合）
            if (c.motion is BlendTree subBt)
            {
                _btClipboard.blendTreeJson = JsonUtility.ToJson(BtEditor_Serialize(subBt), true);
                _btClipboard.linkedBlendTreeInstanceId = subBt.GetInstanceID();
            }
        }

        /// <summary>統一ペースト: クリップボードの内容をBlendTree全体に適用する。</summary>
        private static void BtEditor_PasteUnified(BlendTree bt, AnimatorController controller, string assetPath)
        {
            if (bt == null || !_btClipboard.HasData) return;

            if (_btClipboard.kind == BtClipboardKind.BlendTree)
            {
                if (string.IsNullOrEmpty(_btClipboard.blendTreeJson)) return;
                var data = JsonUtility.FromJson<BlendTreeNodeData>(_btClipboard.blendTreeJson);
                if (data == null) return;

                if (_btClipboard.copyMode == BtCopyMode.Link)
                {
                    // リンクペースト: コピー元のBlendTreeを探して参照を差し替える
                    var srcBt = EditorUtility.InstanceIDToObject(_btClipboard.linkedBlendTreeInstanceId) as BlendTree;
                    if (srcBt != null && srcBt != bt)
                    {
                        // 現在のbtの代わりにsrcBtを参照するようコントローラ内の参照を差し替え
                        if (controller != null)
                        {
                            Undo.RegisterCompleteObjectUndo(controller, "Link Paste BlendTree");
                            BtEditor_ReplaceRefsInController(controller, bt, srcBt);
                            SaveOwningAssets(controller);
                            Selection.activeObject = srcBt;
                            EditorGUIUtility.PingObject(srcBt);
                            InternalEditorUtility.RepaintAllViews();
                            return;
                        }
                    }
                    // コピー元が見つからない場合はデータ上書き（フォールバック）
                }

                // 複製ペースト（またはフォールバック）
                // 子の破棄を挟むため RecordObject の差分ではなく完全スナップショットで記録する
                Undo.RegisterCompleteObjectUndo(bt, "Paste BlendTree");
                BtEditor_DestroyEmbeddedSubtrees(bt, assetPath);
                BtEditor_ApplyData(bt, data, assetPath);
                SaveOwningAssets(bt, controller);
                InternalEditorUtility.RepaintAllViews();
            }
            else if (_btClipboard.kind == BtClipboardKind.ChildMotion)
            {
                // ChildMotionをBlendTree全体にペーストする場合: childrenに追加
                var editableChildren = bt.children.ToList();
                var newChild = BtEditor_PasteAsChildMotion(_btClipboard.childMotion, bt, assetPath);
                editableChildren.Add(newChild);
                Undo.RecordObject(bt, "Paste ChildMotion into BlendTree");
                bt.children = editableChildren.ToArray();
                SaveOwningAssets(bt, controller);
                InternalEditorUtility.RepaintAllViews();
            }
        }

        /// <summary>クリップボードの内容をChildMotionとしてペーストする。</summary>
        private static ChildMotion BtEditor_PasteAsChildMotion(ChildMotion fallback, BlendTree hostBt, string assetPath)
        {
            if (!_btClipboard.HasData) return fallback;

            if (_btClipboard.kind == BtClipboardKind.ChildMotion)
            {
                if (_btClipboard.copyMode == BtCopyMode.Link)
                {
                    // リンク: 同じMotion参照をそのまま使う
                    return _btClipboard.childMotion;
                }
                else
                {
                    // 複製: 子がBlendTreeの場合は新しいオブジェクトとして複製
                    var c = _btClipboard.childMotion;
                    if (c.motion is BlendTree subBt && !string.IsNullOrEmpty(_btClipboard.blendTreeJson))
                    {
                        var data = JsonUtility.FromJson<BlendTreeNodeData>(_btClipboard.blendTreeJson);
                        if (data != null && hostBt != null && !string.IsNullOrEmpty(assetPath))
                        {
                            var targetAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                            if (targetAsset != null)
                            {
                                var newSubBt = new BlendTree();
                                AssetDatabase.AddObjectToAsset(newSubBt, targetAsset);
                                Undo.RegisterCreatedObjectUndo(newSubBt, "Paste ChildMotion");
                                BtEditor_ConfigureEmbeddedBlendTree(newSubBt);
                                BtEditor_ApplyData(newSubBt, data, assetPath);
                                c.motion = newSubBt;
                            }
                        }
                    }
                    return c;
                }
            }
            else if (_btClipboard.kind == BtClipboardKind.BlendTree)
            {
                if (_btClipboard.copyMode == BtCopyMode.Link)
                {
                    // リンク: コピー元BlendTreeをそのまま参照
                    var srcBt = EditorUtility.InstanceIDToObject(_btClipboard.linkedBlendTreeInstanceId) as BlendTree;
                    if (srcBt != null)
                    {
                        var c = fallback;
                        c.motion = srcBt;
                        return c;
                    }
                }

                // 複製: JSONから新しいBlendTreeを作成
                if (!string.IsNullOrEmpty(_btClipboard.blendTreeJson) && hostBt != null && !string.IsNullOrEmpty(assetPath))
                {
                    var data = JsonUtility.FromJson<BlendTreeNodeData>(_btClipboard.blendTreeJson);
                    if (data != null)
                    {
                        var targetAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                        if (targetAsset != null)
                        {
                            var newBt = new BlendTree();
                            AssetDatabase.AddObjectToAsset(newBt, targetAsset);
                            Undo.RegisterCreatedObjectUndo(newBt, "Paste BlendTree");
                            BtEditor_ConfigureEmbeddedBlendTree(newBt);
                            BtEditor_ApplyData(newBt, data, assetPath);
                            var c = fallback;
                            c.motion = newBt;
                            return c;
                        }
                    }
                }
            }

            return fallback;
        }

        /// <summary>指定のChildMotionがリンクコピーで貼り付けられたものかを判定する。</summary>
        private static bool BtEditor_IsLinkedMotion(ChildMotion c)
        {
            if (c.motion == null) return false;
            if (!_btClipboard.HasData || _btClipboard.copyMode != BtCopyMode.Link) return false;

            var motionId = c.motion.GetInstanceID();
            if (_btClipboard.linkedMotionInstanceId != 0 && motionId == _btClipboard.linkedMotionInstanceId)
                return true;
            if (_btClipboard.linkedBlendTreeInstanceId != 0 && motionId == _btClipboard.linkedBlendTreeInstanceId)
                return true;
            return false;
        }

        /// <summary>
        /// リスト内で同じMotionを共有（リンク）しているChildMotionのインデックスを収集する。
        /// BlendTree子ツリーで同一のInstanceIDを持つものをグルーピングする。
        /// </summary>
        private static Dictionary<int, List<int>> BtEditor_FindSharedMotionGroups(IReadOnlyList<ChildMotion> children)
        {
            var groups = new Dictionary<int, List<int>>();
            if (children == null) return groups;

            for (var i = 0; i < children.Count; i++)
            {
                var m = children[i].motion;
                if (m == null) continue;
                var id = m.GetInstanceID();
                if (!groups.TryGetValue(id, out var list))
                {
                    list = new List<int>();
                    groups[id] = list;
                }
                list.Add(i);
            }

            // 単独参照は除外
            var keys = groups.Keys.ToList();
            foreach (var k in keys)
            {
                if (groups[k].Count < 2)
                    groups.Remove(k);
            }
            return groups;
        }

        /// <summary>同じMotionを共有しているリンク関係をリスト下部に表示する。</summary>
        private static void BtEditor_DrawSharedMotionLinks(IReadOnlyList<ChildMotion> children, Dictionary<int, List<int>> sharedGroups)
        {
            if (sharedGroups == null || sharedGroups.Count == 0)
                return;

            var linkStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                richText = true,
                wordWrap = true
            };
            linkStyle.normal.textColor = new Color(0.4f, 0.75f, 1f, 1f);

            EditorGUILayout.LabelField("🔗 リンク関係", EditorStyles.miniLabel);
            foreach (var kvp in sharedGroups)
            {
                var motionObj = EditorUtility.InstanceIDToObject(kvp.Key);
                var motionName = motionObj != null ? motionObj.name : "(?)";
                var indices = string.Join(", ", kvp.Value.Select(i => $"#{i + 1}"));
                EditorGUILayout.LabelField($"  {motionName}: {indices} が同一参照", linkStyle);
            }
            EditorGUILayout.Space(2f);
        }

        /// <summary>クリップボード情報の表示。</summary>
        private static void BtEditor_DrawClipboardInfo()
        {
            if (!_btClipboard.HasData) return;

            var modeLabel = _btClipboard.copyMode == BtCopyMode.Link ? "リンク" : "複製";
            var kindLabel = _btClipboard.kind == BtClipboardKind.BlendTree ? "BlendTree" : "ChildMotion";
            var info = $"📋 クリップボード: {kindLabel} ({modeLabel})";

            if (_btClipboard.copyMode == BtCopyMode.Link)
            {
                Object linkedObj = null;
                if (_btClipboard.linkedBlendTreeInstanceId != 0)
                    linkedObj = EditorUtility.InstanceIDToObject(_btClipboard.linkedBlendTreeInstanceId);
                else if (_btClipboard.linkedMotionInstanceId != 0)
                    linkedObj = EditorUtility.InstanceIDToObject(_btClipboard.linkedMotionInstanceId);

                if (linkedObj != null)
                    info += $" → {linkedObj.name}";
                else
                    info += " → (元オブジェクト消失)";
            }

            var style = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 10,
                richText = true
            };
            var color = _btClipboard.copyMode == BtCopyMode.Link
                ? new Color(0.3f, 0.7f, 1f, 0.15f)
                : new Color(0.5f, 0.5f, 0.5f, 0.1f);
            var rect = EditorGUILayout.GetControlRect(false, 20f);
            EditorGUI.DrawRect(rect, color);
            EditorGUI.LabelField(rect, info, style);
        }

        private static BlendTreeNodeData BtEditor_Serialize(BlendTree bt)
        {
            var btPath = AssetDatabase.GetAssetPath(bt);
            var data = new BlendTreeNodeData
            {
                name = bt.name,
                blendType = (int)bt.blendType,
                blendParameter = bt.blendParameter,
                blendParameterY = bt.blendParameterY,
                minThreshold = bt.minThreshold,
                maxThreshold = bt.maxThreshold,
                useAutomaticThresholds = bt.useAutomaticThresholds,
                normalizeBlendValues = BtEditor_TryGetNormalizedBlendValues(bt, out var norm) && norm,
                children = new List<BlendTreeChildNodeData>()
            };

            foreach (var c in bt.children)
            {
                var cd = new BlendTreeChildNodeData
                {
                    threshold = c.threshold,
                    posX = c.position.x,
                    posY = c.position.y,
                    timeScale = Mathf.Approximately(c.timeScale, 0f) ? 1f : c.timeScale,
                    cycleOffset = c.cycleOffset,
                    directBlendParameter = c.directBlendParameter,
                    mirror = c.mirror
                };

                if (c.motion is BlendTree subBt)
                {
                    var subPath = AssetDatabase.GetAssetPath(subBt);
                    cd.isEmbeddedBlendTree = subPath == btPath;
                    cd.subTree = BtEditor_Serialize(subBt);
                    if (!cd.isEmbeddedBlendTree)
                        cd.motionGuid = AssetDatabase.AssetPathToGUID(subPath);
                }
                else if (c.motion != null)
                {
                    cd.motionGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c.motion));
                }

                data.children.Add(cd);
            }
            return data;
        }

        private static void BtEditor_ApplyData(BlendTree bt, BlendTreeNodeData data, string assetPath)
        {
            bt.name = data.name ?? bt.name;
            bt.blendType = (BlendTreeType)data.blendType;
            bt.blendParameter = data.blendParameter ?? "";
            bt.blendParameterY = data.blendParameterY ?? "";
            bt.minThreshold = data.minThreshold;
            bt.maxThreshold = data.maxThreshold;
            bt.useAutomaticThresholds = data.useAutomaticThresholds;
            BtEditor_SetNormalizedBlendValues(bt, data.normalizeBlendValues);

            var targetAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            var newChildren = new List<ChildMotion>();

            foreach (var cd in data.children ?? new List<BlendTreeChildNodeData>())
            {
                Motion motion = null;
                if (cd.isEmbeddedBlendTree && cd.subTree != null)
                {
                    var subBt = new BlendTree();
                    AssetDatabase.AddObjectToAsset(subBt, targetAsset);
                    Undo.RegisterCreatedObjectUndo(subBt, "Paste BlendTree");
                    BtEditor_ConfigureEmbeddedBlendTree(subBt);
                    BtEditor_ApplyData(subBt, cd.subTree, assetPath);
                    motion = subBt;
                }
                else if (!string.IsNullOrEmpty(cd.motionGuid))
                {
                    var motionPath = AssetDatabase.GUIDToAssetPath(cd.motionGuid);
                    if (!string.IsNullOrEmpty(motionPath))
                        motion = AssetDatabase.LoadAssetAtPath<Motion>(motionPath);
                }

                newChildren.Add(new ChildMotion
                {
                    motion = motion,
                    threshold = cd.threshold,
                    position = new Vector2(cd.posX, cd.posY),
                    timeScale = Mathf.Approximately(cd.timeScale, 0f) ? 1f : cd.timeScale,
                    cycleOffset = cd.cycleOffset,
                    directBlendParameter = cd.directBlendParameter ?? "",
                    mirror = cd.mirror
                });
            }
            bt.children = newChildren.ToArray();
        }

        private static void BtEditor_DestroyEmbeddedSubtrees(BlendTree bt, string ownerAssetPath)
        {
            if (bt == null) return;
            foreach (var c in bt.children)
            {
                if (c.motion is BlendTree sub && AssetDatabase.GetAssetPath(sub) == ownerAssetPath)
                {
                    BtEditor_DestroyEmbeddedSubtrees(sub, ownerAssetPath);
                    Undo.DestroyObjectImmediate(sub);
                }
            }
        }
    }
}
