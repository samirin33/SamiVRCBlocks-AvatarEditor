using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Samirin33.Editor;
using System.Collections.Generic;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    public class VRCFaceTrackingParamSetterEditor : EditorWindow
    {
        const float PreviewSize = 104f;
        const string VRChatReferenceUrl = "https://creators.vrchat.com/avatars/animator-parameters/";
        const string VrcftReferenceUrl = "https://docs.vrcft.io/docs/tutorial-avatars/tutorial-avatars-extras/parameters";
        RuntimeAnimatorController _animatorController;
        static bool _showParamDescriptions = true;
        static Vector2 _paramDescriptionScroll;
        static bool _showFavoritesOnly;
        static readonly Dictionary<string, bool> _sectionExpanded = new Dictionary<string, bool>();

        [MenuItem("samirin33 Editor Tools/VRChat FaceTracking Param Setter")]
        public static void Open()
        {
            var w = GetWindow<VRCFaceTrackingParamSetterEditor>();
            w.titleContent = new GUIContent("VRCFT Param Setter");
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
            });
        }

        void DrawReferenceLinks()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("VRCFaceTracking Parameters", GUILayout.Height(22)))
                Application.OpenURL(VrcftReferenceUrl);
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
            _showParamDescriptions = EditorGUILayout.BeginFoldoutHeaderGroup(_showParamDescriptions, "FaceTrackingパラメータの役割・詳細");
            if (_showParamDescriptions)
            {
                EditorGUILayout.Space(2);
                _paramDescriptionScroll = EditorGUILayout.BeginScrollView(_paramDescriptionScroll, GUILayout.ExpandHeight(true));
                var controller = _animatorController as AnimatorController;
                string currentHeader = null;
                bool isCurrentSectionExpanded = true;

                foreach (var p in VRCFaceTrackingParams.All)
                {
                    bool isFavorite = VRCFaceTrackingParamSetterPreferences.IsFavorite(p.Name);
                    if (_showFavoritesOnly && !isFavorite)
                        continue;

                    string detectedHeader = VRCFaceTrackingCategoryResolver.GetHeader(p.Name);
                    string sectionHeader = !string.IsNullOrEmpty(detectedHeader)
                        ? detectedHeader
                        : (currentHeader ?? "Other");
                    if (sectionHeader != currentHeader)
                    {
                        currentHeader = sectionHeader;
                        EditorGUILayout.Space(6);
                        bool expanded = GetSectionExpanded(sectionHeader);
                        bool newExpanded = EditorGUILayout.Foldout(expanded, sectionHeader, true);
                        if (newExpanded != expanded)
                            SetSectionExpanded(sectionHeader, newExpanded);
                        isCurrentSectionExpanded = newExpanded;
                    }

                    if (!isCurrentSectionExpanded)
                        continue;

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinHeight(0));
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.BeginVertical();

                    EditorGUILayout.BeginHorizontal();
                    bool newFavorite = GUILayout.Toggle(isFavorite, isFavorite ? "★" : "☆", "Button", GUILayout.Width(30));
                    if (newFavorite != isFavorite)
                        VRCFaceTrackingParamSetterPreferences.SetFavorite(p.Name, newFavorite);
                    EditorGUILayout.LabelField($"{p.Name} ({p.Type})", EditorStyles.label);
                    EditorGUILayout.EndHorizontal();

                    if (!string.IsNullOrEmpty(p.Description))
                        EditorGUILayout.LabelField(p.Description, GetWrappedLabelStyle());
                    EditorGUILayout.LabelField("可動範囲: " + AvatarParamRangeResolver.GetRangeText(p), GetWrappedLabelStyle());
                    EditorGUILayout.EndVertical();

                    DrawReferenceImagesRight(p.Name);
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.Space(2);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("名前をコピー", GUILayout.Width(110)))
                    {
                        EditorGUIUtility.systemCopyBuffer = p.Name;
                    }

                    bool hasController = controller != null;
                    bool hasParam = hasController && VRCAvatarParamSetterCore.HasParameter(controller, p.Name);
                    using (new EditorGUI.DisabledScope(!hasController || hasParam))
                    {
                        var label = hasParam ? "追加済み" : "Animatorへ追加";
                        if (GUILayout.Button(label, GUILayout.Width(120)))
                        {
                            Undo.RecordObject(controller, "Add VRCFT parameter: " + p.Name);
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

        bool GetSectionExpanded(string header)
        {
            if (_sectionExpanded.TryGetValue(header, out bool expanded))
                return expanded;
            _sectionExpanded[header] = false;
            return false;
        }

        void SetSectionExpanded(string header, bool expanded)
        {
            _sectionExpanded[header] = expanded;
        }

        GUIStyle GetWrappedLabelStyle()
        {
            var style = new GUIStyle(EditorStyles.label);
            style.wordWrap = true;
            return style;
        }

        void DrawReferenceImagesRight(string parameterName)
        {
            if (!VRCFaceTrackingReferenceImageResolver.TryGetImageUrls(parameterName, out List<string> urls))
                return;

            EditorGUILayout.BeginVertical(GUILayout.Width(PreviewSize * 2f + 6f));
            int maxShow = Mathf.Min(urls.Count, 2);
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < maxShow; i++)
            {
                var texture = EditorImageCache.GetOrRequest(urls[i], this);
                if (texture != null)
                    GUILayout.Label(texture, GUILayout.Width(PreviewSize), GUILayout.Height(PreviewSize));
                else
                    GUILayout.Box("Loading", GUILayout.Width(PreviewSize), GUILayout.Height(PreviewSize));
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }
    }
}
