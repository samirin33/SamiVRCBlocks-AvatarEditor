using System.Collections.Generic;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using Samirin33.Editor;

namespace Samirin33.AvatarEditor.Tools.Editor
{
#if UNITY_2022_2_OR_NEWER
    /// <summary>
    /// AnimatorBinding のショートカット一覧と、Unity ショートカット管理への案内。
    /// </summary>
    internal sealed class AnimatorBindingSettingsProvider : SettingsProvider
    {
        private const string SettingsPath = "Preferences/Samirin Editor Tools/Animator Binding";

        private static readonly (string shortcutId, string labelJa, string hint)[] Rows =
        {
            (AnimatorBinding.ShortcutIds.MergedCopy, "トランジション設定のまとめてコピー", null),
            (AnimatorBinding.ShortcutIds.MergedPasteOverwrite, "まとめて上書きペースト", null),
            (AnimatorBinding.ShortcutIds.MergedPasteAdditive, "まとめて追加ペースト", null),
            (AnimatorBinding.ShortcutIds.NewTransitionConvergeToLast, "新規トランジション - 最後へ収束 (Ctrl+Shift+T 既定)",
                "複数ステート: 最後に選択したステートへ他から遷移を追加。1件のみ: 次に遷移先ステートを選択するとトランジション作成（ドラフト矢印は非公開 API のため2ステップ選択）。"),
            (AnimatorBinding.ShortcutIds.NewTransitionDivergeFromFirst, "新規トランジション - 先頭から拡散 (Ctrl+Shift+D 既定)",
                "複数ステート: 最初に選択したステートから他へ遷移を追加。1件のみ: 上記と同じく次の選択で遷移先を指定。"),
            (AnimatorBinding.ShortcutIds.NewStateAtCursor, "新規ステート作成 - エディタ中心 (Ctrl+Shift+N 既定)",
                "Animator エディタの表示中心に New State を作成。中心座標を取得できない場合は、選択ステート付近にフォールバックして作成。")
        };

        public AnimatorBindingSettingsProvider(string path, SettingsScope scopes)
            : base(path, scopes)
        {
        }

        public override void OnGUI(string searchContext)
        {
            SamirinEditorStyleHelper.DrawWithBlueBackgroundForSettingsGui(() =>
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Animator Binding（キーボードショートカット）", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "以下のショートカットは Unity の「ショートカット管理」に登録されています。\n" +
                    "トランジション設定のコピー／ペーストや順序変更は、AnimatorTransitionEditOperations で本ツール群（まとめてコピー等）と共有されています。\n" +
                    "キーを変更する場合: メニュー「Edit > Shortcuts...」（または「Edit > Shortcuts...」に相当する項目）を開き、検索欄に「Samirin」と入力して割り当てを変更してください。\n" +
                    "※ デフォルトのショートカットプロファイルでは、競合時に上書きできない場合があります。その場合はショートカット用にプロファイルを複製してから変更してください。",
                    MessageType.Info);

                EditorGUILayout.Space(6);

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    for (var i = 0; i < Rows.Length; i++)
                    {
                        var row = Rows[i];
                        EditorGUILayout.LabelField(row.labelJa, EditorStyles.boldLabel);
                        if (!string.IsNullOrEmpty(row.hint))
                            EditorGUILayout.LabelField(row.hint, EditorStyles.wordWrappedMiniLabel);

                        var binding = SafeGetBinding(row.shortcutId);
                        EditorGUILayout.SelectableLabel(
                            string.IsNullOrEmpty(binding) ? "(未割り当て)" : binding,
                            EditorStyles.textField,
                            GUILayout.Height(EditorGUIUtility.singleLineHeight));

                        EditorGUILayout.LabelField($"ID: {row.shortcutId}", EditorStyles.miniLabel);
                        EditorGUILayout.Space(4);
                    }
                }

                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("ショートカット管理を開く", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Edit > Shortcuts を開く（英語メニュー名）"))
                        TryOpenShortcutsWindow();
                }

                EditorGUILayout.Space(4);
            });
        }

        private static string SafeGetBinding(string shortcutId)
        {
            try
            {
                var b = ShortcutManager.instance.GetShortcutBinding(shortcutId);
                var s = b.ToString();
                return string.IsNullOrEmpty(s) ? "" : s;
            }
            catch
            {
                return "(取得不可)";
            }
        }

        private static void TryOpenShortcutsWindow()
        {
            var candidates = new[]
            {
                "Edit/Shortcuts...",
                "Edit/Shortcuts",
                "Window/Shortcuts",
                "Window/General/Shortcuts"
            };
            foreach (var path in candidates)
            {
                if (EditorApplication.ExecuteMenuItem(path))
                    return;
            }

            EditorUtility.DisplayDialog(
                "Shortcuts",
                "ショートカットウィンドウのメニューが見つかりませんでした。\n手動で Edit > Shortcuts...（環境により名称が異なります）を開いてください。",
                "OK");
        }

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new AnimatorBindingSettingsProvider(SettingsPath, SettingsScope.User)
            {
                label = "Animator Binding",
                keywords = new HashSet<string>(new[]
                {
                    "Animator", "Samirin", "Shortcut", "Transition", "Copy", "Paste", "キーボード", "ショートカット",
                    "State", "ステート"
                })
            };
        }
    }
#endif
}
