using UnityEditor;
using UnityEngine;
using Samirin33.Editor;

namespace Samirin33.AvatarEditor.Tools.Editor
{
    /// <summary>
    /// オブジェクトにアタッチせずにエディタ上でFPSを制限するための独立ウィンドウ。
    /// 設定は EditorPrefs に保存され、ドメインリロード後も保持されます。
    /// </summary>
    public class FPSLimiterWindow : EditorWindow
    {
        private const string EditorPrefsKeyTargetFrameRate = "Samirin33.AvatarEditor.FPSLimiter.TargetFrameRate";
        private const int DefaultFrameRate = 90;

        private int _targetFrameRate;

        [MenuItem("SBAvatarEditor/Performance/FPS Limiter", false, 8)]
        public static void Open()
        {
            var w = GetWindow<FPSLimiterWindow>(false, "FPS Limiter", true);
            w.minSize = new Vector2(280, 180);
        }

        private void OnEnable()
        {
            _targetFrameRate = EditorPrefs.GetInt(EditorPrefsKeyTargetFrameRate, DefaultFrameRate);
            ApplyFrameRate();
        }

        private void ApplyFrameRate()
        {
            Application.targetFrameRate = _targetFrameRate;
        }

        private void SaveAndApply(int value)
        {
            _targetFrameRate = value;
            EditorPrefs.SetInt(EditorPrefsKeyTargetFrameRate, value);
            ApplyFrameRate();
            Repaint();
        }

        private void OnGUI()
        {
            SamirinEditorStyleHelper.DrawWithBlueBackground(() =>
            {
                EditorGUILayout.HelpBox(
                    "最高FPSを制限することができます！",
                    MessageType.Info);

                EditorGUILayout.Space(4);

                int sliderValue = _targetFrameRate < 1 ? 1 : (_targetFrameRate > 120 ? 120 : _targetFrameRate);
                EditorGUI.BeginChangeCheck();
                int newValue = EditorGUILayout.IntSlider("目標FPS", sliderValue, 1, 120);
                if (EditorGUI.EndChangeCheck())
                {
                    SaveAndApply(newValue);
                }

                EditorGUILayout.LabelField("プリセット", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("10 FPS"))
                    SaveAndApply(10);
                if (GUILayout.Button("30 FPS"))
                    SaveAndApply(30);
                if (GUILayout.Button("60 FPS"))
                    SaveAndApply(60);
                if (GUILayout.Button("120 FPS"))
                    SaveAndApply(120);
                if (GUILayout.Button("無制限"))
                    SaveAndApply(-1);

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(4);

                if (_targetFrameRate == -1)
                {
                    SamirinEditorStyleHelper.DrawWithDefaultFont(() =>
                        EditorGUILayout.HelpBox("現在: 無制限（-1）", MessageType.Info));
                }
            }, new Rect(0, 0, position.width, position.height));
        }
    }
}
