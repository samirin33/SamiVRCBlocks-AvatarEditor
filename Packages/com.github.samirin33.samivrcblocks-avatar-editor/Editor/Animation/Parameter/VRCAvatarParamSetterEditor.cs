using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Samirin33.Editor;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    public class VRCAvatarParamSetterEditor : EditorWindow
    {
        const string VRChatReferenceUrl = "https://creators.vrchat.com/avatars/animator-parameters/";
        const string VrcftReferenceUrl = "https://docs.vrcft.io/docs/tutorial-avatars/tutorial-avatars-extras/parameters";
        RuntimeAnimatorController _animatorController;
        static bool _showParamDescriptions = true;
        static Vector2 _paramDescriptionScroll;
        static bool _showFavoritesOnly;

        [MenuItem("SamiVRCBlocks-AvatarEditor/Parameter/VRChat Avatar Param Setter", false, 6)]
        public static void Open()
        {
            var w = GetWindow<VRCAvatarParamSetterEditor>();
            w.titleContent = new GUIContent("VRChat Param Setter");
        }

        void OnGUI()
        {
            SamirinEditorStyleHelper.DrawWithBlueBackground(() =>
            {
                EditorGUILayout.Space(4);
                DrawReferenceLinks();
                _animatorController = (RuntimeAnimatorController)EditorGUILayout.ObjectField(
                    "Animator Controller",
                    _animatorController,
                    typeof(RuntimeAnimatorController),
                    false);

                EditorGUILayout.Space(4);

                // ビルトインパラメータの説明は Animator が未設定でも表示する
                DrawParamDescriptionSection();

                DrawFavoriteFilter();

                if (_animatorController == null)
                {
                    EditorGUILayout.HelpBox("Animator Controller を指定してください。", MessageType.Info);
                    return;
                }

                var controller = _animatorController as AnimatorController;
                if (controller == null)
                {
                    EditorGUILayout.HelpBox("Animator Controller アセット（.controller）を指定してください。", MessageType.Warning);
                    return;
                }
            }, new Rect(0, 0, position.width, position.height));
        }

        void DrawReferenceLinks()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("VRChat Parameters", GUILayout.Height(22)))
                Application.OpenURL(VRChatReferenceUrl);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }

        void DrawFavoriteFilter()
        {
            EditorGUILayout.BeginHorizontal();
            _showFavoritesOnly = GUILayout.Toggle(_showFavoritesOnly, "お気に入りのみ表示", "Button", GUILayout.Height(22));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }

        void DrawParamDescriptionSection()
        {
            _showParamDescriptions = EditorGUILayout.BeginFoldoutHeaderGroup(_showParamDescriptions, "ビルトインパラメータの役割・詳細");
            if (_showParamDescriptions)
            {
                EditorGUILayout.Space(2);
                _paramDescriptionScroll = EditorGUILayout.BeginScrollView(_paramDescriptionScroll, GUILayout.ExpandHeight(true));

                var controller = _animatorController as AnimatorController;

                foreach (var p in VRChatBuiltInParams.All)
                {
                    bool isFavorite = VRCAvatarParamSetterPreferences.IsFavorite(p.Name);
                    if (_showFavoritesOnly && !isFavorite)
                        continue;

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinHeight(0));
                    EditorGUILayout.BeginHorizontal();
                    bool newFavorite = GUILayout.Toggle(isFavorite, isFavorite ? "★" : "☆", "Button", GUILayout.Width(30));
                    if (newFavorite != isFavorite)
                        VRCAvatarParamSetterPreferences.SetFavorite(p.Name, newFavorite);
                    EditorGUILayout.LabelField($"{p.Name} ({p.Type})", EditorStyles.label);
                    EditorGUILayout.EndHorizontal();
                    if (!string.IsNullOrEmpty(p.Description))
                        EditorGUILayout.LabelField(p.Description, GetWrappedLabelStyle());
                    EditorGUILayout.LabelField("可動範囲: " + AvatarParamRangeResolver.GetRangeText(p), GetWrappedLabelStyle());

                    EditorGUILayout.Space(2);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();

                    // パラメータ名コピー
                    if (GUILayout.Button("名前をコピー", GUILayout.Width(110)))
                    {
                        EditorGUIUtility.systemCopyBuffer = p.Name;
                    }

                    // Animator への追加ボタン
                    bool hasController = controller != null;
                    bool hasParam = hasController && VRCAvatarParamSetterCore.HasParameter(controller, p.Name);

                    using (new EditorGUI.DisabledScope(!hasController || hasParam))
                    {
                        var label = hasParam ? "追加済み" : "Animatorへ追加";
                        if (GUILayout.Button(label, GUILayout.Width(120)))
                        {
                            Undo.RecordObject(controller, "Add VRChat parameter: " + p.Name);
                            controller.AddParameter(p.Name, p.Type);
                            EditorUtility.SetDirty(controller);
                            AssetDatabase.SaveAssetIfDirty(controller);
                        }
                    }

                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(2);
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        GUIStyle GetWrappedLabelStyle()
        {
            var style = new GUIStyle(EditorStyles.label);
            style.wordWrap = true;
            return style;
        }
    }
}
