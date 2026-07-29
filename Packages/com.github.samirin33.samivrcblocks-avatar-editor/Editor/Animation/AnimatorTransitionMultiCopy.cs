using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Samirin33.AvatarEditor.Tools.Editor
{
    /// <summary>
    /// Animator のトランジションについて、選択をまとめてコピー・ペーストする。
    /// Unity 2022.3 ではタイミング系は <see cref="AnimatorStateTransition"/> のみ（Any State 遷移も同型）。
    /// <see cref="AnimatorTransition"/>（Entry 等）は条件のみが API 上取り扱える。
    /// <list type="bullet">
    /// <item>ショートカットまたは CONTEXT メニューから実行可能（トップメニューの Animator Binding/ は非表示）。</item>
    /// <item>インスペクター等でトランジションを右クリックしたときの CONTEXT メニューにも項目を追加。</item>
    /// </list>
    /// </summary>
    public static class AnimatorTransitionMultiCopy
    {
        // CONTEXT/AnimatorStateTransition/…（ショートカット表記の同期用・MenuItem パスと一致させる）
        private const string MenuContextStateCopy = "CONTEXT/AnimatorStateTransition/トランジション設定をまとめてコピー";
        private const string MenuContextStatePasteOverwrite = "CONTEXT/AnimatorStateTransition/トランジション設定をまとめて上書きペースト";
        private const string MenuContextStatePasteAdditive = "CONTEXT/AnimatorStateTransition/トランジション設定をまとめて追加ペースト";
        // CONTEXT/AnimatorTransition/…
        private const string MenuContextEntryCopy = "CONTEXT/AnimatorTransition/トランジション設定をまとめてコピー";
        private const string MenuContextEntryPasteOverwrite = "CONTEXT/AnimatorTransition/トランジション設定をまとめて上書きペースト";
        private const string MenuContextEntryPasteAdditive = "CONTEXT/AnimatorTransition/トランジション設定をまとめて追加ペースト";
        // SamiVRCBlocks-AvatarEditor/Animator Binding/…
        private const string MenuToolsCopy = "SamiVRCBlocks-AvatarEditor/Animator Binding/トランジション設定をまとめてコピー";
        private const string MenuToolsPasteOverwrite = "SamiVRCBlocks-AvatarEditor/Animator Binding/トランジション設定をまとめて上書きペースト";
        private const string MenuToolsPasteAdditive = "SamiVRCBlocks-AvatarEditor/Animator Binding/トランジション設定をまとめて追加ペースト";

        private const string EditorPrefsKey = "SamirinEditorTools.AnimatorTransitionMultiCopy.Json";

        private static string ClipboardJson
        {
            get => EditorPrefs.GetString(EditorPrefsKey, "");
            set => EditorPrefs.SetString(EditorPrefsKey, value ?? "");
        }

        public static bool HasClipboard => !string.IsNullOrEmpty(ClipboardJson);

        private enum PasteMode
        {
            /// <summary>条件・ブレンド等をクリップボードで置き換え。</summary>
            Overwrite,
            /// <summary>既存トランジションには条件のみ追加。新規作成したトランジションにはクリップボードをそのまま適用。</summary>
            Additive
        }

        [Serializable]
        private sealed class ClipboardPayload
        {
            public AnimatorTransitionEditOperations.TransitionSettings[] items =
                Array.Empty<AnimatorTransitionEditOperations.TransitionSettings>();
        }

        /// <param name="command">CONTEXT から呼ぶときは指定。ツールバーメニューからは null。</param>
        private static List<AnimatorTransitionBase> GetOrderedSelection(MenuCommand command, Func<Object, bool> filter)
        {
            var list = new List<AnimatorTransitionBase>();
            var seen = new HashSet<int>();

            void TryAdd(Object o)
            {
                if (o == null || !filter(o) || !(o is AnimatorTransitionBase tb)) return;
                if (!seen.Add(tb.GetInstanceID())) return;
                list.Add(tb);
            }

            foreach (var o in Selection.objects)
                TryAdd(o);

            // Selection.objects に含まれないサブアセットがある環境向けに instanceID でも拾う
            foreach (var id in Selection.instanceIDs)
                TryAdd(EditorUtility.InstanceIDToObject(id));

            if (list.Count == 0 && command != null && command.context is AnimatorTransitionBase ctx)
                TryAdd(ctx);
            else if (list.Count == 0 && command == null && Selection.activeObject is AnimatorTransitionBase active)
                TryAdd(active);

            return list;
        }

        /// <summary>Entry 等の <see cref="AnimatorTransition"/>（ステート間の <see cref="AnimatorStateTransition"/> は除外）。</summary>
        private static bool IsEntryTransition(Object o)
        {
            return o is AnimatorTransition && !(o is AnimatorStateTransition);
        }

        private static void CopyFromSelection(MenuCommand command, Func<Object, bool> filter)
        {
            var list = GetOrderedSelection(command, filter);
            if (list.Count == 0) return;

            var payload = new ClipboardPayload
            {
                items = new AnimatorTransitionEditOperations.TransitionSettings[list.Count]
            };
            for (var i = 0; i < list.Count; i++)
                payload.items[i] = AnimatorTransitionEditOperations.Capture(list[i]);

            ClipboardJson = JsonUtility.ToJson(payload);
        }

        private static void PasteToSelection(MenuCommand command, Func<Object, bool> filter, PasteMode mode)
        {
            var selected = GetOrderedSelection(command, filter);
            PasteToTargetList(selected, mode, filter);
        }

        /// <summary>
        /// クリップボードを指定トランジション列に適用する共通処理（メニュー・<see cref="AnimatorTransitionManager"/> からも使用）。
        /// </summary>
        private static void PasteToTargetList(
            IReadOnlyList<AnimatorTransitionBase> selected,
            PasteMode mode,
            Func<Object, bool> filter)
        {
            if (!HasClipboard) return;
            var payload = JsonUtility.FromJson<ClipboardPayload>(ClipboardJson);
            if (payload?.items == null || payload.items.Length == 0) return;
            if (selected == null || selected.Count == 0) return;

            var clipCount = payload.items.Length;
            var controller = AnimatorTransitionEditOperations.GetController(selected[0]);
            var loc = AnimatorTransitionEditOperations.FindTransitionLocation(selected[0], controller);

            var work = new List<(AnimatorTransitionBase tr, bool isNew)>();
            for (var i = 0; i < selected.Count; i++)
                work.Add((selected[i], false));

            var guard = 0;
            while (work.Count < clipCount && loc != null && guard < 512)
            {
                guard++;
                var neu = AnimatorTransitionEditOperations.TryCreateParallelTransition(loc);
                if (neu == null || !filter(neu))
                {
                    Debug.LogWarning(
                        "[AnimatorTransitionMultiCopy] クリップボード件数に足りない分のトランジションを追加できませんでした（先頭選択と同一トポロジで作成する必要があります）。");
                    break;
                }

                work.Add((neu, true));
            }

            if (work.Count == 0) return;

            var controllers = new HashSet<AnimatorController>();
            for (var i = 0; i < work.Count; i++)
            {
                var c = AnimatorTransitionEditOperations.GetController(work[i].tr);
                if (c != null) controllers.Add(c);
            }

            foreach (var c in controllers)
                Undo.RegisterCompleteObjectUndo(c, mode == PasteMode.Overwrite
                    ? "トランジション設定をまとめて上書きペースト"
                    : "トランジション設定をまとめて追加ペースト");

            // 選択・生成したすべてのトランジションに適用（クリップボードは i % clipCount で巡回）
            for (var i = 0; i < work.Count; i++)
            {
                var (tr, isNew) = work[i];
                var src = payload.items[i % clipCount];
                if (mode == PasteMode.Overwrite)
                    AnimatorTransitionEditOperations.ApplyOverwrite(tr, src);
                else if (isNew)
                    AnimatorTransitionEditOperations.ApplyOverwrite(tr, src);
                else
                    AnimatorTransitionEditOperations.ApplyAdditiveConditionsOnly(tr, src);

                AnimatorTransitionEditOperations.MarkControllerDirty(tr);
            }
        }

        private static bool ValidatePaste(MenuCommand command, Func<Object, bool> filter)
        {
            return HasClipboard && GetOrderedSelection(command, filter).Count > 0;
        }

        [InitializeOnLoadMethod]
        private static void InitializeMenuHotkeyDisplay()
        {
            SyncMenuHotkeyDisplayFromShortcutSettings();
        }

        private static void SyncMenuHotkeyDisplayFromShortcutSettings()
        {
            AnimatorMenuHotkeyDisplay.TrySetFromShortcutId(MenuContextStateCopy, AnimatorBinding.ShortcutIds.MergedCopy);
            AnimatorMenuHotkeyDisplay.TrySetFromShortcutId(MenuContextStatePasteOverwrite, AnimatorBinding.ShortcutIds.MergedPasteOverwrite);
            AnimatorMenuHotkeyDisplay.TrySetFromShortcutId(MenuContextStatePasteAdditive, AnimatorBinding.ShortcutIds.MergedPasteAdditive);
            AnimatorMenuHotkeyDisplay.TrySetFromShortcutId(MenuContextEntryCopy, AnimatorBinding.ShortcutIds.MergedCopy);
            AnimatorMenuHotkeyDisplay.TrySetFromShortcutId(MenuContextEntryPasteOverwrite, AnimatorBinding.ShortcutIds.MergedPasteOverwrite);
            AnimatorMenuHotkeyDisplay.TrySetFromShortcutId(MenuContextEntryPasteAdditive, AnimatorBinding.ShortcutIds.MergedPasteAdditive);
            AnimatorMenuHotkeyDisplay.TrySetFromShortcutId(MenuToolsCopy, AnimatorBinding.ShortcutIds.MergedCopy);
            AnimatorMenuHotkeyDisplay.TrySetFromShortcutId(MenuToolsPasteOverwrite, AnimatorBinding.ShortcutIds.MergedPasteOverwrite);
            AnimatorMenuHotkeyDisplay.TrySetFromShortcutId(MenuToolsPasteAdditive, AnimatorBinding.ShortcutIds.MergedPasteAdditive);
        }

        // --- AnimatorStateTransition（ステート間・Any State 含む） ---

        [MenuItem(MenuContextStateCopy, false, 400)]
        private static void CopyStateTransitions(MenuCommand command)
        {
            CopyFromSelection(command, o => o is AnimatorStateTransition);
        }

        [MenuItem(MenuContextStateCopy, true)]
        private static bool CopyStateTransitionsValidate(MenuCommand command)
        {
            SyncMenuHotkeyDisplayFromShortcutSettings();
            return GetOrderedSelection(command, o => o is AnimatorStateTransition).Count > 0;
        }

        [MenuItem(MenuContextStatePasteOverwrite, false, 401)]
        private static void PasteStateTransitionsOverwrite(MenuCommand command)
        {
            PasteToSelection(command, o => o is AnimatorStateTransition, PasteMode.Overwrite);
        }

        [MenuItem(MenuContextStatePasteOverwrite, true)]
        private static bool PasteStateTransitionsOverwriteValidate(MenuCommand command)
        {
            SyncMenuHotkeyDisplayFromShortcutSettings();
            return ValidatePaste(command, o => o is AnimatorStateTransition);
        }

        [MenuItem(MenuContextStatePasteAdditive, false, 402)]
        private static void PasteStateTransitionsAdditive(MenuCommand command)
        {
            PasteToSelection(command, o => o is AnimatorStateTransition, PasteMode.Additive);
        }

        [MenuItem(MenuContextStatePasteAdditive, true)]
        private static bool PasteStateTransitionsAdditiveValidate(MenuCommand command)
        {
            SyncMenuHotkeyDisplayFromShortcutSettings();
            return ValidatePaste(command, o => o is AnimatorStateTransition);
        }

        // --- AnimatorTransition（Entry 等・タイミング API なし） ---

        [MenuItem(MenuContextEntryCopy, false, 400)]
        private static void CopyAnimatorTransitions(MenuCommand command)
        {
            CopyFromSelection(command, IsEntryTransition);
        }

        [MenuItem(MenuContextEntryCopy, true)]
        private static bool CopyAnimatorTransitionsValidate(MenuCommand command)
        {
            SyncMenuHotkeyDisplayFromShortcutSettings();
            return GetOrderedSelection(command, IsEntryTransition).Count > 0;
        }

        [MenuItem(MenuContextEntryPasteOverwrite, false, 401)]
        private static void PasteAnimatorTransitionsOverwrite(MenuCommand command)
        {
            PasteToSelection(command, IsEntryTransition, PasteMode.Overwrite);
        }

        [MenuItem(MenuContextEntryPasteOverwrite, true)]
        private static bool PasteAnimatorTransitionsOverwriteValidate(MenuCommand command)
        {
            SyncMenuHotkeyDisplayFromShortcutSettings();
            return ValidatePaste(command, IsEntryTransition);
        }

        [MenuItem(MenuContextEntryPasteAdditive, false, 402)]
        private static void PasteAnimatorTransitionsAdditive(MenuCommand command)
        {
            PasteToSelection(command, IsEntryTransition, PasteMode.Additive);
        }

        [MenuItem(MenuContextEntryPasteAdditive, true)]
        private static bool PasteAnimatorTransitionsAdditiveValidate(MenuCommand command)
        {
            SyncMenuHotkeyDisplayFromShortcutSettings();
            return ValidatePaste(command, IsEntryTransition);
        }

        // --- Animator ウィンドウでトランジションを選択した状態で使うトップメニュー ---
        // メニュー表示は非表示（ショートカット・公開 API は継続）。必要なら MenuItem を復帰。

        private static bool IsAnyKnownTransition(Object o) => o is AnimatorTransitionBase;

        // [MenuItem(MenuToolsCopy, false, 106)]
        private static void ToolsMenuCopy()
        {
            CopyFromSelection(null, IsAnyKnownTransition);
        }

        // [MenuItem(MenuToolsCopy, true)]
        private static bool ToolsMenuCopyValidate()
        {
            SyncMenuHotkeyDisplayFromShortcutSettings();
            return GetOrderedSelection(null, IsAnyKnownTransition).Count > 0;
        }

        // [MenuItem(MenuToolsPasteOverwrite, false, 107)]
        private static void ToolsMenuPasteOverwrite()
        {
            PasteToSelection(null, IsAnyKnownTransition, PasteMode.Overwrite);
        }

        // [MenuItem(MenuToolsPasteOverwrite, true)]
        private static bool ToolsMenuPasteOverwriteValidate()
        {
            SyncMenuHotkeyDisplayFromShortcutSettings();
            return ValidatePaste(null, IsAnyKnownTransition);
        }

        // [MenuItem(MenuToolsPasteAdditive, false, 108)]
        private static void ToolsMenuPasteAdditive()
        {
            PasteToSelection(null, IsAnyKnownTransition, PasteMode.Additive);
        }

        // [MenuItem(MenuToolsPasteAdditive, true)]
        private static bool ToolsMenuPasteAdditiveValidate()
        {
            SyncMenuHotkeyDisplayFromShortcutSettings();
            return ValidatePaste(null, IsAnyKnownTransition);
        }

        #region 公開 API（ショートカット・AnimatorBinding からの呼び出し用）

        /// <summary>ツールメニューと同じ「まとめてコピー」（選択は <see cref="AnimatorTransitionBase"/>）。</summary>
        public static void PerformMergedCopyFromSelection()
        {
            CopyFromSelection(null, IsAnyKnownTransition);
        }

        /// <summary>ツールメニューと同じ「まとめて上書きペースト」。</summary>
        public static void PerformMergedPasteOverwriteFromSelection()
        {
            PasteToSelection(null, IsAnyKnownTransition, PasteMode.Overwrite);
        }

        /// <summary>ツールメニューと同じ「まとめて追加ペースト」。</summary>
        public static void PerformMergedPasteAdditiveFromSelection()
        {
            PasteToSelection(null, IsAnyKnownTransition, PasteMode.Additive);
        }

        /// <summary>単一トランジションの設定を、ツールと同じクリップボード形式で保存する。</summary>
        public static void CopyMergedSettings(AnimatorTransitionBase transition)
        {
            if (transition == null) return;
            var payload = new ClipboardPayload
            {
                items = new[] { AnimatorTransitionEditOperations.Capture(transition) }
            };
            ClipboardJson = JsonUtility.ToJson(payload);
        }

        /// <summary>クリップボードを単一トランジションに上書きペーストする。</summary>
        public static bool TryPasteMergedOverwrite(AnimatorTransitionBase target)
        {
            if (target == null || !HasClipboard) return false;
            PasteToTargetList(new List<AnimatorTransitionBase> { target }, PasteMode.Overwrite, IsAnyKnownTransition);
            return true;
        }

        /// <summary>クリップボードを単一トランジションに追加ペーストする。</summary>
        public static bool TryPasteMergedAdditive(AnimatorTransitionBase target)
        {
            if (target == null || !HasClipboard) return false;
            PasteToTargetList(new List<AnimatorTransitionBase> { target }, PasteMode.Additive, IsAnyKnownTransition);
            return true;
        }

        /// <summary>現在のクリップボードに格納されているコピー件（まとめてコピーと同一形式）。</summary>
        public static IReadOnlyList<AnimatorTransitionEditOperations.TransitionSettings> GetMergedClipboardItems()
        {
            if (!HasClipboard) return Array.Empty<AnimatorTransitionEditOperations.TransitionSettings>();
            var payload = JsonUtility.FromJson<ClipboardPayload>(ClipboardJson);
            if (payload?.items == null || payload.items.Length == 0)
                return Array.Empty<AnimatorTransitionEditOperations.TransitionSettings>();
            return payload.items;
        }

        #endregion
    }
}
