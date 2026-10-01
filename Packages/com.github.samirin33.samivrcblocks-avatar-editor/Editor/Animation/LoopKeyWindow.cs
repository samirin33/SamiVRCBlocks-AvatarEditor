#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Samirin33.Editor;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    /// <summary>
    /// Animation ウィンドウで開いているクリップに、ループ用の両端キーを追加する。
    /// </summary>
    public class LoopKeyWindow : EditorWindow
    {
        AnimationClip _trackedClip;
        float _length = 1f;
        bool _enableLoopTime = true;
        string _status = "";
        MessageType _statusType = MessageType.Info;
        string[] _details = System.Array.Empty<string>();
        Vector2 _scroll;

        [MenuItem("SBAvatarEditor/Animation/Loop Key", false, 7)]
        public static void Open()
        {
            var window = GetWindow<LoopKeyWindow>(false, "ループキー", true);
            window.minSize = new Vector2(420f, 360f);
            window.Show();
        }

        void OnEnable()
        {
            minSize = new Vector2(420f, 360f);
        }

        void OnInspectorUpdate()
        {
            Repaint();
        }

        void OnGUI()
        {
            SamirinEditorStyleHelper.DrawWithBlueBackground(DrawContents, new Rect(0f, 0f, position.width, position.height));
        }

        void DrawContents()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "終点から始点へ戻るベジエの継ぎ目を、指定した長さの最初と最後のフレームへ同じ値・同じ接線で置きます。長さより後のキーは削除し、長さより前の最後のキーを終点にします。",
                MessageType.Info);

            AnimationWindowHelper.TryGetAnimationWindowStateUnfocused(out _, out AnimationClip clip, out _);
            TrackClip(clip);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("編集中のクリップ", clip, typeof(AnimationClip), false);
            }

            if (clip == null)
            {
                EditorGUILayout.HelpBox("Animation ウィンドウでクリップを開いてください。", MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            if (!LoopKeyUtility.IsEditable(clip, out string blocked))
            {
                EditorGUILayout.HelpBox(blocked, MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.Space(6);
            DrawSpan(clip);

            EditorGUILayout.Space(8);
            float frameRate = FrameRateOf(clip);
            EditorGUILayout.LabelField("フレームレート", frameRate.ToString("0.###") + " fps");

            EditorGUI.BeginChangeCheck();
            float seconds = EditorGUILayout.FloatField("クリップ長（秒）", _length);
            if (EditorGUI.EndChangeCheck())
                _length = Mathf.Max(0f, seconds);

            EditorGUI.BeginChangeCheck();
            int frames = EditorGUILayout.IntField("クリップ長（フレーム）", SecondsToFrames(_length, frameRate));
            if (EditorGUI.EndChangeCheck())
                _length = Mathf.Max(0, frames) / frameRate;

            _enableLoopTime = EditorGUILayout.Toggle("Loop Time を有効にする", _enableLoopTime);

            EditorGUILayout.Space(8);
            if (GUILayout.Button("ループキーを追加", GUILayout.Height(28f)))
                Apply(clip);

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.HelpBox(_status, _statusType);
                for (int i = 0; i < _details.Length; i++)
                    EditorGUILayout.LabelField(_details[i], EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        void TrackClip(AnimationClip clip)
        {
            if (_trackedClip == clip)
                return;

            _trackedClip = clip;
            _status = "";
            _details = System.Array.Empty<string>();
            if (clip == null)
            {
                _length = 1f;
                return;
            }

            _length = clip.length > LoopKeyUtility.TimeEpsilon ? clip.length : 1f;
        }

        static void DrawSpan(AnimationClip clip)
        {
            float frameRate = FrameRateOf(clip);
            EditorGUILayout.LabelField("現在の長さ", FormatTime(clip.length, frameRate));
            if (LoopKeyUtility.TryGetKeyframeSpan(clip, out float start, out float end, out int curveCount))
            {
                EditorGUILayout.LabelField("始点", FormatTime(start, frameRate));
                EditorGUILayout.LabelField("終点", FormatTime(end, frameRate));
                EditorGUILayout.LabelField("フロートカーブ", curveCount + " 本");
            }
            else
            {
                EditorGUILayout.LabelField("始点", "—");
                EditorGUILayout.LabelField("終点", "—");
                EditorGUILayout.LabelField("フロートカーブ", "0 本");
            }
        }

        static float FrameRateOf(AnimationClip clip)
        {
            if (clip == null || clip.frameRate < 1f)
                return 60f;
            return clip.frameRate;
        }

        static int SecondsToFrames(float seconds, float frameRate)
        {
            return Mathf.Max(0, Mathf.RoundToInt(seconds * frameRate));
        }

        static string FormatTime(float seconds, float frameRate)
        {
            return seconds.ToString("0.000") + " 秒 / " + SecondsToFrames(seconds, frameRate) + " フレーム";
        }

        void Apply(AnimationClip clip)
        {
            LoopKeyUtility.ApplyResult result = LoopKeyUtility.Apply(clip, _length, _enableLoopTime);
            _status = result.Message;
            _details = result.Details.ToArray();
            _statusType = result.AppliedCount > 0
                ? (result.SkippedCount > 0 ? MessageType.Warning : MessageType.Info)
                : MessageType.Warning;

            if (result.AppliedCount > 0 && AnimationWindowReflection.TryGetAnimationWindowState(out object state))
                AnimationWindowReflection.ResampleAnimation(state);
        }
    }
}
#endif
