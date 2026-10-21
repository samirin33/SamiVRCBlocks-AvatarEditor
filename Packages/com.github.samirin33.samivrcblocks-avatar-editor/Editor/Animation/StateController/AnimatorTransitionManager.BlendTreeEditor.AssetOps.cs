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
        // ======================== 内包⇔アセット判定・親探索 ========================

        private static bool BtEditor_IsMainBlendTreeAsset(BlendTree bt)
        {
            if (bt == null) return false;
            var p = AssetDatabase.GetAssetPath(bt);
            if (string.IsNullOrEmpty(p)) return false;
            return ReferenceEquals(AssetDatabase.LoadMainAssetAtPath(p), bt);
        }

        private static bool BtEditor_IsEmbeddedSubAsset(BlendTree bt)
        {
            if (bt == null || !AssetDatabase.Contains(bt)) return false;
            return !BtEditor_IsMainBlendTreeAsset(bt);
        }

        private static bool BtEditor_TryFindAssetParentBlendTreeInSameAsset(BlendTree child, out BlendTree parent)
        {
            parent = null;
            if (child == null) return false;
            var path = AssetDatabase.GetAssetPath(child);
            if (string.IsNullOrEmpty(path)) return false;
            var all = AssetDatabase.LoadAllAssetsAtPath(path).OfType<BlendTree>().ToArray();
            for (var i = 0; i < all.Length; i++)
            {
                var cand = all[i];
                if (cand == null || ReferenceEquals(cand, child)) continue;
                if (!BtEditor_IsMainBlendTreeAsset(cand)) continue;
                if (BtEditor_IsReferencedBy(cand, child))
                {
                    parent = cand;
                    return true;
                }
            }
            return false;
        }

        private static int _btAssetParentCacheTargetId;
        private static BlendTree _btAssetParentCacheResult;
        private static double _btAssetParentCacheTime = double.NegativeInfinity;
        private const double BtAssetParentCacheSeconds = 1.0;

        /// <summary>OnGUI から毎フレーム呼ばれるため、全体検索の結果を短時間キャッシュする。</summary>
        private static bool BtEditor_TryFindAnyAssetParentBlendTreeCached(BlendTree target, out BlendTree parent)
        {
            parent = null;
            if (target == null) return false;
            var now = EditorApplication.timeSinceStartup;
            var id = target.GetInstanceID();
            if (id != _btAssetParentCacheTargetId || now - _btAssetParentCacheTime > BtAssetParentCacheSeconds)
            {
                BtEditor_TryFindAnyAssetParentBlendTree(target, out _btAssetParentCacheResult);
                _btAssetParentCacheTargetId = id;
                _btAssetParentCacheTime = now;
            }
            parent = _btAssetParentCacheResult;
            return parent != null;
        }

        private static bool BtEditor_TryFindAnyAssetParentBlendTree(BlendTree target, out BlendTree parent)
        {
            parent = null;
            if (target == null) return false;

            var targetPath = AssetDatabase.GetAssetPath(target);
            var targetDir = string.IsNullOrEmpty(targetPath) ? null : Path.GetDirectoryName(targetPath)?.Replace('\\', '/');
            var visited = new HashSet<string>();

            // 親は同じフォルダにあることが多いので、先に近傍だけを探してから全体検索に落とす
            if (!string.IsNullOrEmpty(targetDir) && AssetDatabase.IsValidFolder(targetDir) &&
                BtEditor_TryFindAssetParentIn(AssetDatabase.FindAssets("t:BlendTree", new[] { targetDir }), target, visited, out parent))
                return true;

            return BtEditor_TryFindAssetParentIn(AssetDatabase.FindAssets("t:BlendTree"), target, visited, out parent);
        }

        private static bool BtEditor_TryFindAssetParentIn(string[] guids, BlendTree target, HashSet<string> visited, out BlendTree parent)
        {
            parent = null;
            for (var i = 0; i < guids.Length; i++)
            {
                var p = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(p) || !visited.Add(p)) continue;
                // t:BlendTree は BlendTree を内包する .controller も返すため、メインが BlendTree のものだけをロードする
                if (AssetDatabase.GetMainAssetTypeAtPath(p) != typeof(BlendTree)) continue;
                var cand = AssetDatabase.LoadMainAssetAtPath(p) as BlendTree;
                if (cand == null || ReferenceEquals(cand, target)) continue;
                if (BtEditor_IsReferencedBy(cand, target))
                {
                    parent = cand;
                    return true;
                }
            }
            return false;
        }

        private static void BtEditor_ExtractEmbeddedToExternalAsset(BlendTree bt, BlendTree parentAssetBlendTree)
        {
            if (bt == null || parentAssetBlendTree == null) return;
            var parentPath = AssetDatabase.GetAssetPath(parentAssetBlendTree);
            var defaultDir = Path.GetDirectoryName(parentPath);
            var savePath = EditorUtility.SaveFilePanelInProject(
                "BlendTree をアセットとして保存",
                bt.name + ".asset", "asset",
                "保存先を選択してください", defaultDir);
            if (string.IsNullOrEmpty(savePath)) return;

            var newBt = new BlendTree();
            AssetDatabase.CreateAsset(newBt, savePath);
            BtEditor_CopyFields(bt, newBt);
            BtEditor_CloneChildrenIntoAsset(bt, newBt, savePath, AssetDatabase.GetAssetPath(bt));

            Undo.RegisterCompleteObjectUndo(parentAssetBlendTree, "Extract BlendTree to Asset");
            BtEditor_ReplaceRefsInBT(parentAssetBlendTree, bt, newBt);
            BtEditor_DestroyEmbeddedSubtrees(bt, AssetDatabase.GetAssetPath(bt));
            Undo.DestroyObjectImmediate(bt);

            SaveOwningAssets(parentAssetBlendTree, newBt);
            AssetDatabase.Refresh();
            Selection.activeObject = newBt;
            EditorGUIUtility.PingObject(newBt);
            InternalEditorUtility.RepaintAllViews();
        }

        private static void BtEditor_EmbedIntoParentBlendTreeAsset(BlendTree bt)
        {
            if (bt == null) return;
            if (!BtEditor_TryFindAnyAssetParentBlendTree(bt, out var parent))
            {
                EditorUtility.DisplayDialog(
                    "AnimatorStateController",
                    "このBlendTreeを参照している親BlendTree（アセット型）が見つかりません。",
                    "OK");
                return;
            }

            Undo.RegisterCompleteObjectUndo(parent, "Embed BlendTree into Parent BlendTree");
            var embedded = BtEditor_DeepCloneIntoAsset(bt, parent);
            BtEditor_ReplaceRefsInBT(parent, bt, embedded);
            SaveOwningAssets(parent);
            AssetDatabase.Refresh();
            Selection.activeObject = embedded;
            EditorGUIUtility.PingObject(embedded);
            InternalEditorUtility.RepaintAllViews();
        }

        // ======================== アセットに抽出（内包 → 外部アセット） ========================

        private static void BtEditor_ExtractToAsset(BlendTree bt, AnimatorController controller)
        {
            if (!EditorUtility.DisplayDialog(
                    "BlendTree をアセットに抽出",
                    "コントローラ側の変更は Undo で戻せますが、作成したアセットファイルは削除されません。続行しますか？",
                    "抽出する", "キャンセル"))
                return;

            var ctrlPath = AssetDatabase.GetAssetPath(controller);
            var defaultDir = Path.GetDirectoryName(ctrlPath);
            var savePath = EditorUtility.SaveFilePanelInProject(
                "BlendTree をアセットとして保存",
                bt.name + ".asset", "asset",
                "保存先を選択してください", defaultDir);
            if (string.IsNullOrEmpty(savePath)) return;

            // 新しいルートを作ってアセット化し、サブツリーを追加してフィールドをコピー
            var newBt = new BlendTree();
            AssetDatabase.CreateAsset(newBt, savePath);
            BtEditor_CopyFields(bt, newBt);
            BtEditor_CloneChildrenIntoAsset(bt, newBt, savePath, ctrlPath);

            // controller 内の参照を差し替え
            Undo.RegisterCompleteObjectUndo(controller, "Extract BlendTree to Asset");
            BtEditor_ReplaceRefsInController(controller, bt, newBt);

            // 旧サブアセットを削除
            BtEditor_DestroyEmbeddedSubtrees(bt, ctrlPath);
            Undo.DestroyObjectImmediate(bt);

            SaveOwningAssets(controller, newBt);
            AssetDatabase.Refresh();
            Selection.activeObject = newBt;
            EditorGUIUtility.PingObject(newBt);
            EditorApplication.delayCall += () =>
            {
                if (newBt != null)
                {
                    Selection.activeObject = newBt;
                    EditorGUIUtility.PingObject(newBt);
                }
            };
            InternalEditorUtility.RepaintAllViews();
        }

        private static void BtEditor_CopyFields(BlendTree src, BlendTree dst)
        {
            dst.name = src.name;
            dst.blendType = src.blendType;
            dst.blendParameter = src.blendParameter;
            dst.blendParameterY = src.blendParameterY;
            dst.minThreshold = src.minThreshold;
            dst.maxThreshold = src.maxThreshold;
            dst.useAutomaticThresholds = src.useAutomaticThresholds;
            if (BtEditor_TryGetNormalizedBlendValues(src, out var norm))
                BtEditor_SetNormalizedBlendValues(dst, norm);
        }

        private static void BtEditor_CloneChildrenIntoAsset(
            BlendTree src, BlendTree dst, string newAssetPath, string srcOwnerPath)
        {
            var targetAsset = AssetDatabase.LoadMainAssetAtPath(newAssetPath);
            var srcChildren = src.children;
            var dstChildren = (ChildMotion[])srcChildren.Clone();

            for (var i = 0; i < srcChildren.Length; i++)
            {
                if (srcChildren[i].motion is BlendTree subSrc &&
                    AssetDatabase.GetAssetPath(subSrc) == srcOwnerPath)
                {
                    var subDst = new BlendTree();
                    AssetDatabase.AddObjectToAsset(subDst, targetAsset);
                    BtEditor_ConfigureEmbeddedBlendTree(subDst);
                    BtEditor_CopyFields(subSrc, subDst);
                    BtEditor_CloneChildrenIntoAsset(subSrc, subDst, newAssetPath, srcOwnerPath);
                    dstChildren[i].motion = subDst;
                }
            }
            dst.children = dstChildren;
        }

        private static void BtEditor_ReplaceRefsInController(
            AnimatorController ctrl, BlendTree old, BlendTree newBt)
        {
            var layers = ctrl.layers;
            var layersChanged = false;
            for (var i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                BtEditor_ReplaceRefsInSM(layer.stateMachine, old, newBt);

                if (layer.syncedLayerIndex < 0 || layer.syncedLayerIndex >= layers.Length)
                    continue;
                foreach (var st in BtEditor_CollectStates(layers[layer.syncedLayerIndex].stateMachine))
                {
                    var overrideMotion = layer.GetOverrideMotion(st);
                    if (ReferenceEquals(overrideMotion, old))
                    {
                        layer.SetOverrideMotion(st, newBt);
                        layersChanged = true;
                    }
                    else if (overrideMotion is BlendTree overrideBt)
                    {
                        BtEditor_ReplaceRefsInBT(overrideBt, old, newBt);
                    }
                }
            }

            if (layersChanged)
            {
                // layers はコピーで返るため、書き戻さないと同期レイヤーの Override Motion が反映されない
                Undo.RecordObject(ctrl, "Replace BlendTree Reference");
                ctrl.layers = layers;
                EditorUtility.SetDirty(ctrl);
            }
        }

        private static List<AnimatorState> BtEditor_CollectStates(AnimatorStateMachine sm)
        {
            var result = new List<AnimatorState>();
            void Walk(AnimatorStateMachine m)
            {
                if (m == null) return;
                foreach (var child in m.states)
                {
                    if (child.state != null)
                        result.Add(child.state);
                }
                foreach (var sub in m.stateMachines)
                    Walk(sub.stateMachine);
            }
            Walk(sm);
            return result;
        }

        private static void BtEditor_ReplaceRefsInSM(
            AnimatorStateMachine sm, BlendTree old, BlendTree newBt)
        {
            if (sm == null) return;
            foreach (var child in sm.states)
            {
                var st = child.state;
                if (st == null) continue;
                if (ReferenceEquals(st.motion, old))
                {
                    Undo.RecordObject(st, "Replace BlendTree Reference");
                    st.motion = newBt;
                    EditorUtility.SetDirty(st);
                }
                else if (st.motion is BlendTree parentBt)
                {
                    BtEditor_ReplaceRefsInBT(parentBt, old, newBt);
                }
            }
            foreach (var sub in sm.stateMachines)
                BtEditor_ReplaceRefsInSM(sub.stateMachine, old, newBt);
        }

        private static void BtEditor_ReplaceRefsInBT(BlendTree bt, BlendTree old, BlendTree newBt)
        {
            if (bt == null) return;
            var children = bt.children;
            var changed = false;
            for (var i = 0; i < children.Length; i++)
            {
                if (ReferenceEquals(children[i].motion, old))
                {
                    children[i].motion = newBt;
                    changed = true;
                }
                else if (children[i].motion is BlendTree sub)
                {
                    BtEditor_ReplaceRefsInBT(sub, old, newBt);
                }
            }
            if (changed)
            {
                Undo.RecordObject(bt, "Replace BlendTree Reference");
                bt.children = children;
                EditorUtility.SetDirty(bt);
            }
        }

        // ======================== コントローラに内包（外部アセット → 内包） ========================

        private static void BtEditor_EmbedIntoController(BlendTree bt)
        {
            // Animatorウィンドウで編集中のControllerを優先対象にする
            var ctrl = BtEditor_GetEditingAnimatorWindowController();
            if (ctrl == null)
                ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GetAssetPath(bt));

            if (ctrl == null)
            {
                EditorUtility.DisplayDialog(
                    "AnimatorStateController",
                    "Animatorウィンドウで編集中のAnimatorControllerが見つかりません。",
                    "OK");
                return;
            }

            if (!BtEditor_IsReferencedByController(ctrl, bt))
            {
                EditorUtility.DisplayDialog(
                    "AnimatorStateController",
                    "編集中のAnimatorController内で、このBlendTreeを参照していません。",
                    "OK");
                return;
            }

            Undo.RegisterCompleteObjectUndo(ctrl, "Embed BlendTree into Controller");
            var embedded = BtEditor_DeepCloneIntoAsset(bt, ctrl);
            BtEditor_ReplaceRefsInController(ctrl, bt, embedded);

            SaveOwningAssets(ctrl);
            AssetDatabase.Refresh();
            Selection.activeObject = embedded;
            EditorGUIUtility.PingObject(embedded);
            EditorApplication.delayCall += () =>
            {
                if (embedded != null)
                {
                    Selection.activeObject = embedded;
                    EditorGUIUtility.PingObject(embedded);
                }
            };
            InternalEditorUtility.RepaintAllViews();
        }

        private static bool BtEditor_IsReferencedBy(BlendTree parent, BlendTree target)
        {
            if (parent == null) return false;
            foreach (var c in parent.children)
            {
                if (ReferenceEquals(c.motion, target)) return true;
                if (c.motion is BlendTree sub && BtEditor_IsReferencedBy(sub, target)) return true;
            }
            return false;
        }

        private static bool BtEditor_IsReferencedByController(AnimatorController ctrl, BlendTree target)
        {
            if (ctrl == null || target == null)
                return false;
            var layers = ctrl.layers;
            foreach (var layer in layers)
            {
                if (BtEditor_IsReferencedInStateMachine(layer.stateMachine, target))
                    return true;

                if (layer.syncedLayerIndex < 0 || layer.syncedLayerIndex >= layers.Length)
                    continue;
                foreach (var st in BtEditor_CollectStates(layers[layer.syncedLayerIndex].stateMachine))
                {
                    var overrideMotion = layer.GetOverrideMotion(st);
                    if (ReferenceEquals(overrideMotion, target))
                        return true;
                    if (overrideMotion is BlendTree overrideBt && BtEditor_IsReferencedBy(overrideBt, target))
                        return true;
                }
            }

            return false;
        }

        private static bool BtEditor_IsReferencedInStateMachine(AnimatorStateMachine sm, BlendTree target)
        {
            if (sm == null || target == null)
                return false;
            foreach (var child in sm.states)
            {
                var st = child.state;
                if (st == null)
                    continue;
                if (ReferenceEquals(st.motion, target))
                    return true;
                if (st.motion is BlendTree bt && BtEditor_IsReferencedBy(bt, target))
                    return true;
            }

            foreach (var sub in sm.stateMachines)
            {
                if (BtEditor_IsReferencedInStateMachine(sub.stateMachine, target))
                    return true;
            }

            return false;
        }

        private static BlendTree BtEditor_DeepCloneIntoAsset(BlendTree src, Object targetAsset)
        {
            var dst = new BlendTree();
            AssetDatabase.AddObjectToAsset(dst, targetAsset);
            Undo.RegisterCreatedObjectUndo(dst, "Embed BlendTree");
            BtEditor_ConfigureEmbeddedBlendTree(dst);
            BtEditor_CopyFields(src, dst);

            var srcChildren = src.children;
            var dstChildren = (ChildMotion[])srcChildren.Clone();
            for (var i = 0; i < srcChildren.Length; i++)
            {
                if (srcChildren[i].motion is BlendTree subBt)
                    dstChildren[i].motion = BtEditor_DeepCloneIntoAsset(subBt, targetAsset);
            }
            dst.children = dstChildren;
            return dst;
        }

        private static void BtEditor_ConfigureEmbeddedBlendTree(BlendTree bt)
        {
            if (bt == null)
                return;
            bt.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
            EditorUtility.SetDirty(bt);
        }
    }
}
