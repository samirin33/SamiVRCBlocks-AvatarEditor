using System;
using UnityEditor;
using UnityEngine;
using Samirin33.Editor;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    /// <summary>
    /// Transform のローカル位置ウィグル、またはローカルスケールのスクウィッシュを
    /// スライダーで調整してプレビューし、AnimationClip へ書き出す。
    /// </summary>
    public class TransformWiggleSquishWindow : EditorWindow
    {
        private const string KeySettings = "Samirin33.AvatarEditor.TransformWiggleSquish.Settings";
        private const float PreviewHoldSeconds = 0.4f;
        private const float GraphHeight = 112f;

        private static readonly Color SectionBarColor = new Color(0.40f, 0.44f, 0.47f, 0.38f);
        private static readonly Color SectionLabelColor = new Color(0.82f, 0.84f, 0.86f, 0.70f);

        private static readonly TransformWiggleSquishMode[] ModeOrder =
        {
            TransformWiggleSquishMode.PositionWiggle,
            TransformWiggleSquishMode.RotationWiggle,
            TransformWiggleSquishMode.ScaleSquish
        };

        private static readonly string[] ModeLabels = { "位置ウィグル", "回転ウィグル", "スケールスクウィッシュ" };
        private static readonly Color32 CurveX = new Color32(255, 96, 96, 255);
        private static readonly Color32 CurveY = new Color32(88, 220, 130, 255);
        private static readonly Color32 CurveZ = new Color32(96, 170, 255, 255);

        private TransformWiggleSquishSettings _settings = new TransformWiggleSquishSettings();
        private Transform _target;
        private Transform _pathRoot;
        private AnimationClip _outputClip;
        private AnimationClip _previewClip;
        private AnimationModeDriver _animationDriver;
        private Texture2D _graphTexture;
        private GUIStyle _graphLabelStyle;
        private GUIStyle _sectionHeaderStyle;
        private bool _hasAnimationContext;
        private Vector2 _scroll;
        private string _status = "";
        private string _graphMinLabel = "";
        private string _graphMaxLabel = "";
        private MessageType _statusType = MessageType.Info;
        private bool _loaded;
        private bool _previewing;
        private bool _previewRegistered;
        private bool _graphDirty = true;
        private bool _appliedSinceEdit;
        private bool _humanoidCached;
        private bool _isHumanoidBone;
        private Transform _humanoidTarget;
        private Transform _humanoidRoot;
        private float _previewTime;
        private double _lastPreviewStamp;

        [MenuItem("SBAvatarEditor/Animation/Transform Wiggle Squish", false, 6)]
        public static void Open()
        {
            var window = GetWindow<TransformWiggleSquishWindow>(false, "Wiggle / Squish", true);
            window.minSize = new Vector2(920f, 480f);
            window.Show();
        }

        private void OnEnable()
        {
            minSize = new Vector2(920f, 480f);
            LoadPreferences();
            _loaded = true;
            _graphDirty = true;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            StopPreview();
            DestroyAnimationDriver();
            DestroyGraphTexture();
            if (_loaded)
                SavePreferences();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
                StopPreview();
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }

        private void OnGUI()
        {
            if (_settings == null)
                _settings = new TransformWiggleSquishSettings();
            _settings.Sanitize();

            SamirinEditorStyleHelper.DrawWithBlueBackground(DrawContents, new Rect(0f, 0f, position.width, position.height));
        }

        private void DrawContents()
        {
            SyncFromAnimationWindow();
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 108f;
            EditorGUILayout.BeginVertical();
            try
            {
                EditorGUILayout.Space(4f);
                SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont(
                    "Animation ウィンドウで編集中のオブジェクト、基準ルート、クリップに書き込みます。残りの振幅が閾値を下回った時刻で、このカーブは終わります。",
                    MessageType.Info);

                float leftWidth = Mathf.Clamp((position.width - 36f) * 0.46f, 320f, 520f);
                _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical(GUILayout.Width(leftWidth));
                DrawTargetSection();
                DrawPreviewSection();
                EditorGUILayout.EndVertical();
                GUILayout.Space(8f);
                EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
                EditorGUI.BeginChangeCheck();
                DrawWaveSection();
                DrawBaseSection();
                DrawTimeSection();
                if (EditorGUI.EndChangeCheck())
                    MarkParametersEdited();
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(8f);
                EditorGUILayout.EndScrollView();

                DrawApplyFooter();
            }
            finally
            {
                EditorGUIUtility.labelWidth = previousLabelWidth;
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawTargetSection()
        {
            BeginSection("編集対象");
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("対象", _target, typeof(Transform), true);
                EditorGUILayout.ObjectField("基準ルート", _pathRoot, typeof(Transform), true);
                EditorGUILayout.ObjectField("クリップ", _outputClip, typeof(AnimationClip), false);
            }

            string shown = string.IsNullOrEmpty(_settings.bindingPath) ? "(基準ルート自身)" : _settings.bindingPath;
            EditorGUILayout.LabelField("バインドパス", _hasAnimationContext ? shown : "—");

            if (!_hasAnimationContext)
            {
                EditorGUILayout.HelpBox("Animation ウィンドウで、動かすオブジェクトとクリップを開いてください。", MessageType.Warning);
            }
            else if (_pathRoot != null && _pathRoot.GetComponent<Animator>() == null)
            {
                EditorGUILayout.HelpBox("基準ルートに Animator がありません。パスはそのオブジェクトからの相対です。", MessageType.Warning);
            }
            else if (_pathRoot != null && IsHumanoidBoneCached(_target, _pathRoot))
            {
                EditorGUILayout.HelpBox("Humanoid の人型ボーンでは、位置とスケールが無視され、回転は筋肉に置き換わることがあります。", MessageType.Warning);
            }

            EndSection();
        }

        private void DrawPreviewSection()
        {
            BeginSection("プレビュー");
            float duration = TransformWiggleSquishUtility.ComputeDuration(_settings);
            float requested = TransformWiggleSquishUtility.ComputeRequestedDuration(_settings);
            float settle = _settings.loop
                ? requested
                : TransformWiggleSquishUtility.ComputeSettleTime(_settings);

            string lengthLabel = _settings.loop
                ? $"ループ {duration:0.00} 秒"
                : float.IsPositiveInfinity(settle)
                    ? "終了しない"
                    : $"終了 {duration:0.00} 秒";
            EditorGUILayout.LabelField(lengthLabel);

            DrawGraph(duration);
            DrawLegend();
            DrawDurationNotice(duration, requested, settle);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(_previewing ? "プレビュー停止" : "プレビュー", GUILayout.Height(24f)))
            {
                if (_previewing)
                    StopPreview();
                else
                    StartPreview();
            }

            using (new EditorGUI.DisabledScope(!_previewing))
            {
                if (GUILayout.Button("先頭へ", GUILayout.Width(64f), GUILayout.Height(24f)))
                    _previewTime = 0f;
            }

            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            _settings.previewSpeed = EditorGUILayout.Slider("再生速度", _settings.previewSpeed, 0.1f, 2f);
            if (EditorGUI.EndChangeCheck())
                SavePreferences();

            if (_previewing)
            {
                float clipLength = _previewClip != null ? _previewClip.length : duration;
                float shown = Mathf.Clamp(_previewTime, 0f, Mathf.Max(clipLength, 0.0001f));
                EditorGUILayout.LabelField("再生位置", $"{shown:0.00} / {clipLength:0.00} 秒");
            }

            EndSection();
        }

        private void DrawDurationNotice(float duration, float requested, float settle)
        {
            string message;
            MessageType type;
            if (_settings.loop)
            {
                message = "ループ中は閾値で終わらず、周期数で長さが決まります。";
                type = MessageType.None;
            }
            else if (float.IsPositiveInfinity(settle))
            {
                message = "閾値か減衰率が 0 のため、上限時間で終わります。";
                type = MessageType.None;
            }
            else if (settle > _settings.maxDuration + 0.0001f)
            {
                message = $"閾値を下回るのは {settle:0.00} 秒後です。上限 {_settings.maxDuration:0.00} 秒で打ち切ります。";
                type = MessageType.Warning;
            }
            else if (duration < requested - 0.0001f)
            {
                message = "キー数の上限のため、クリップを短くしています。";
                type = MessageType.Warning;
            }
            else
            {
                message = $"残りの振幅が閾値を下回る {duration:0.00} 秒で終わります。";
                type = MessageType.None;
            }

            Rect rect = GUILayoutUtility.GetRect(10f, 40f, GUILayout.ExpandWidth(true));
            EditorGUI.HelpBox(rect, message, type);
        }

        private void DrawLegend()
        {
            EditorGUILayout.BeginHorizontal();
            DrawLegendItem(CurveX, "X");
            DrawLegendItem(CurveY, "Y");
            DrawLegendItem(CurveZ, "Z");
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("0 がベース", EditorStyles.miniLabel, GUILayout.Width(80f));
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawLegendItem(Color32 color, string label)
        {
            Rect rect = GUILayoutUtility.GetRect(12f, 16f, GUILayout.Width(12f), GUILayout.Height(16f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + 3f, 10f, 10f), color);
            GUILayout.Label(label, GUILayout.Width(18f));
        }

        private void DrawGraph(float duration)
        {
            Rect rect = GUILayoutUtility.GetRect(10f, GraphHeight, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint)
                return;

            int width = Mathf.Max(8, Mathf.RoundToInt(rect.width));
            int height = Mathf.Max(8, Mathf.RoundToInt(rect.height));
            EnsureGraphTexture(width, height);
            if (_graphDirty || _graphTexture == null)
                PaintGraph(width, height, duration);

            if (_graphTexture != null)
                GUI.DrawTexture(rect, _graphTexture, ScaleMode.StretchToFill, true);

            if (_previewing && duration > 0.0001f)
            {
                float shown = Mathf.Clamp(_previewTime, 0f, duration);
                float x = Mathf.Lerp(rect.x, rect.xMax - 1f, shown / duration);
                EditorGUI.DrawRect(new Rect(x, rect.y, 1f, rect.height), new Color(1f, 1f, 1f, 0.9f));
            }

            GUIStyle style = GraphLabelStyle;
            GUI.Label(new Rect(rect.x + 4f, rect.y + 2f, 160f, 16f), _graphMaxLabel, style);
            GUI.Label(new Rect(rect.x + 4f, rect.yMax - 16f, 160f, 16f), _graphMinLabel, style);
        }

        private void DrawWaveSection()
        {
            BeginSection("波形");
            EditorGUI.BeginChangeCheck();
            int modeIndex = GUILayout.Toolbar(ModeIndex(_settings.mode), ModeLabels, GUILayout.Height(22f));
            if (EditorGUI.EndChangeCheck())
                _settings.mode = ModeOrder[modeIndex];

            if (GUILayout.Button(new GUIContent("初期値", "選択中のモードの振幅や振動数などを初期値に戻します。")))
                ApplyCurrentPreset();

            bool rotation = _settings.mode == TransformWiggleSquishMode.RotationWiggle;
            _settings.amplitude = DrawVector3SliderFields(
                new GUIContent("振幅", rotation
                    ? "度です。スライダーは -45 から 45 で、数値欄には範囲外も入れられます。"
                    : "スライダーは -1 から 1 です。左の数値欄には範囲外も入れられます。"),
                _settings.amplitude,
                rotation ? -45f : -1f,
                rotation ? 45f : 1f);
            float noiseMax = rotation ? 15f : 1f;
            _settings.noiseStrength = DrawRangeSlider(
                new GUIContent("ノイズの強さ", rotation
                    ? "3軸に同じ強さのノイズを足します。度です。スライダーは 0 から 15 で、数値欄には範囲外も入れられます。"
                    : "3軸に同じ強さのノイズを足します。スライダーは 0 から 1 で、数値欄には範囲外も入れられます。"),
                _settings.noiseStrength,
                0f,
                noiseMax);
            _settings.noiseFrequency = DrawRangeSlider(
                new GUIContent("ノイズの周波数 (Hz)", "3軸共通のノイズの細かさです。スライダーは 0 から 30 で、数値欄には範囲外も入れられます。ループ中はクリップの長さで一周し、先頭と末尾がつながります。"),
                _settings.noiseFrequency,
                0f,
                30f);
            EditorGUILayout.BeginHorizontal();
            _settings.noiseSeed = EditorGUILayout.IntField(new GUIContent("シード", "同じシードなら同じノイズになります。"), _settings.noiseSeed);
            if (GUILayout.Button(new GUIContent("ランダム", "別のノイズに切り替えます。"), GUILayout.Width(72f)))
                _settings.noiseSeed = UnityEngine.Random.Range(1, 999983);
            EditorGUILayout.EndHorizontal();
            if (_settings.mode == TransformWiggleSquishMode.ScaleSquish)
            {
                if (GUILayout.Button(new GUIContent("Yを主軸に配分", "Y の振幅を主軸にし、X と Z をその半分の逆符号にします。")))
                {
                    float y = _settings.amplitude.y;
                    if (Mathf.Approximately(y, 0f))
                        y = 0.24f;
                    _settings.amplitude = new Vector3(-0.5f * y, y, -0.5f * y);
                }
            }

            _settings.frequency = EditorGUILayout.Slider(new GUIContent("振動数 (Hz)", "1 秒あたりの往復回数です。"), _settings.frequency, 0f, 30f);
            using (new EditorGUI.DisabledScope(_settings.loop))
            {
                _settings.damping = EditorGUILayout.Slider(new GUIContent("減衰率", "大きいほど速く小さくなります。包絡線は exp(-減衰率 × 時間) です。"), _settings.damping, 0f, 30f);
                _settings.settleThreshold = EditorGUILayout.Slider(
                    new GUIContent("終了閾値", "減衰した振幅とノイズの合計がこの値を下回った時刻でカーブを終え、最後のキーをベースにします。0 では上限時間まで続きます。"),
                    _settings.settleThreshold,
                    0f,
                    1f);
            }

            EndSection();
        }

        private void DrawBaseSection()
        {
            BeginSection("ベース");
            switch (_settings.mode)
            {
                case TransformWiggleSquishMode.RotationWiggle:
                    _settings.baseRotation = DrawBaseRow("回転", _settings.baseRotation, -180f, 180f, TransformWiggleSquishMode.RotationWiggle);
                    break;
                case TransformWiggleSquishMode.ScaleSquish:
                    _settings.baseScale = DrawBaseRow("スケール", _settings.baseScale, -2f, 5f, TransformWiggleSquishMode.ScaleSquish);
                    break;
                default:
                    _settings.basePosition = DrawBaseRow("位置", _settings.basePosition, -5f, 5f, TransformWiggleSquishMode.PositionWiggle);
                    break;
            }

            EndSection();
        }

        private void DrawTimeSection()
        {
            BeginSection("時間");
            EditorGUILayout.BeginHorizontal();
            _settings.timeOffset = DrawVector3Sliders("ずれ（秒）", _settings.timeOffset, -2f, 2f);
            if (GUILayout.Button(new GUIContent("1/4", "全軸の時間のずれに、4 分の 1 周期を足します。"), GUILayout.Width(36f)))
                ShiftQuarterCycle();
            EditorGUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(_settings.loop && _settings.frequency > 0.0001f))
            {
                _settings.maxDuration = EditorGUILayout.Slider(
                    new GUIContent("上限時間", "閾値まで戻る前に、この秒数で打ち切ります。"),
                    _settings.maxDuration,
                    0.05f,
                    10f);
            }

            _settings.sampleRate = EditorGUILayout.IntSlider("サンプルレート", Mathf.RoundToInt(_settings.sampleRate), 5, 60);
            _settings.optimizeCurve = EditorGUILayout.Toggle(
                new GUIContent("キーを最適化", "許容誤差に収まるベジエハンドルを計算し、必要なキーだけを置きます。"),
                _settings.optimizeCurve);
            using (new EditorGUI.DisabledScope(!_settings.optimizeCurve))
            {
                float percent = _settings.optimizeTolerance * 100f;
                percent = EditorGUILayout.Slider(
                    new GUIContent("許容誤差 (%)", "波形の大きさに対する誤差です。小さいほどキーが増えます。"),
                    percent,
                    0.1f,
                    5f);
                _settings.optimizeTolerance = percent / 100f;
            }

            _settings.loop = EditorGUILayout.Toggle(new GUIContent("ループ", "減衰と閾値を使わず、周期がつながる波形にします。"), _settings.loop);
            if (_settings.loop)
                _settings.loopCycles = EditorGUILayout.IntSlider("周期数", _settings.loopCycles, 1, 8);
            EndSection();
        }

        private Vector3 DrawBaseRow(string label, Vector3 value, float min, float max, TransformWiggleSquishMode channel)
        {
            EditorGUILayout.BeginHorizontal();
            value = DrawVector3Sliders(label, value, min, max);
            using (new EditorGUI.DisabledScope(_target == null || _previewing))
            {
                if (GUILayout.Button("現在値", GUILayout.Width(52f)))
                    value = CaptureBase(channel);
            }

            EditorGUILayout.EndHorizontal();
            return value;
        }

        private static int ModeIndex(TransformWiggleSquishMode mode)
        {
            for (int i = 0; i < ModeOrder.Length; i++)
            {
                if (ModeOrder[i] == mode)
                    return i;
            }

            return 0;
        }

        private static float DrawRangeSlider(GUIContent label, float value, float min, float max)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            float previous = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 1f;
            EditorGUI.BeginChangeCheck();
            float typed = EditorGUILayout.FloatField(GUIContent.none, value, GUILayout.Width(58f));
            bool fieldChanged = EditorGUI.EndChangeCheck();
            EditorGUI.BeginChangeCheck();
            float shown = Mathf.Clamp(fieldChanged ? typed : value, min, max);
            float slid = GUILayout.HorizontalSlider(shown, min, max);
            bool sliderChanged = EditorGUI.EndChangeCheck();
            EditorGUIUtility.labelWidth = previous;
            EditorGUILayout.EndHorizontal();
            if (sliderChanged && !fieldChanged)
                return slid;
            return fieldChanged ? typed : value;
        }

        private static Vector3 DrawVector3SliderFields(GUIContent label, Vector3 value, float min, float max)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            value.x = DrawSliderWithField("X", value.x, min, max);
            value.y = DrawSliderWithField("Y", value.y, min, max);
            value.z = DrawSliderWithField("Z", value.z, min, max);
            EditorGUILayout.EndHorizontal();
            return value;
        }

        private static float DrawSliderWithField(string axis, float value, float min, float max)
        {
            float previous = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 14f;
            EditorGUI.BeginChangeCheck();
            float typed = EditorGUILayout.FloatField(axis, value, GUILayout.Width(58f));
            bool fieldChanged = EditorGUI.EndChangeCheck();
            EditorGUI.BeginChangeCheck();
            float shown = Mathf.Clamp(fieldChanged ? typed : value, min, max);
            float slid = GUILayout.HorizontalSlider(shown, min, max, GUILayout.MinWidth(28f));
            bool sliderChanged = EditorGUI.EndChangeCheck();
            EditorGUIUtility.labelWidth = previous;
            if (sliderChanged && !fieldChanged)
                return slid;
            return fieldChanged ? typed : value;
        }

        private static Vector3 DrawVector3Sliders(string label, Vector3 value, float min, float max)
        {
            EditorGUILayout.PrefixLabel(label);
            value.x = DrawAxisSlider("X", value.x, min, max);
            value.y = DrawAxisSlider("Y", value.y, min, max);
            value.z = DrawAxisSlider("Z", value.z, min, max);
            return value;
        }

        private static float DrawAxisSlider(string axis, float value, float min, float max)
        {
            float previous = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 14f;
            float result = value < min || value > max
                ? EditorGUILayout.FloatField(axis, value)
                : EditorGUILayout.Slider(axis, value, min, max);
            EditorGUIUtility.labelWidth = previous;
            return result;
        }

        private void DrawApplyFooter()
        {
            EditorGUILayout.Space(4f);
            DrawSectionBar("適用");
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "選んだ種類だけを書き換えます。生成した範囲の外にあるキーは残します。",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField("開始位置", $"{ReadAnimationWindowTime():0.00} 秒（Animation の再生位置）");
            using (new EditorGUI.DisabledScope(!_hasAnimationContext || _outputClip == null))
            {
                if (GUILayout.Button("編集中のクリップに適用", GUILayout.Height(28f)))
                    ApplyToExisting(_outputClip);
            }
            Rect statusRect = GUILayoutUtility.GetRect(10f, 36f, GUILayout.ExpandWidth(true));
            if (!string.IsNullOrEmpty(_status))
                EditorGUI.HelpBox(statusRect, _status, _statusType);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4f);
        }

        private void BeginSection(string title)
        {
            EditorGUILayout.Space(6f);
            DrawSectionBar(title);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        }

        private void EndSection()
        {
            EditorGUILayout.EndVertical();
        }

        private void DrawSectionBar(string title)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 22f);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, SectionBarColor);
            GUI.Label(new Rect(rect.x + 8f, rect.y, rect.width - 12f, rect.height), title, SectionHeaderStyle);
        }

        private GUIStyle SectionHeaderStyle
        {
            get
            {
                if (_sectionHeaderStyle == null)
                {
                    _sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        fontSize = 12,
                        padding = new RectOffset(0, 0, 0, 0),
                        normal = { textColor = SectionLabelColor }
                    };
                }

                return _sectionHeaderStyle;
            }
        }

        private void ApplyCurrentPreset()
        {
            switch (_settings.mode)
            {
                case TransformWiggleSquishMode.RotationWiggle:
                    ApplyRotationPreset();
                    break;
                case TransformWiggleSquishMode.ScaleSquish:
                    ApplySquishPreset();
                    break;
                default:
                    ApplyWigglePreset();
                    break;
            }
        }

        private void ApplyWigglePreset()
        {
            _settings.amplitude = new Vector3(0.01f, 0.035f, 0.01f);
            _settings.frequency = 8f;
            _settings.damping = 4f;
            _settings.settleThreshold = 0.0008f;
            _settings.timeOffset = new Vector3(0f, 0.02f, 0.04f);
            _settings.loop = false;
            _settings.maxDuration = 2.5f;
        }

        private void ApplyRotationPreset()
        {
            _settings.amplitude = new Vector3(2f, 8f, 1.5f);
            _settings.frequency = 6f;
            _settings.damping = 4f;
            _settings.settleThreshold = 0.05f;
            _settings.timeOffset = new Vector3(0f, 0.03f, 0.06f);
            _settings.loop = false;
            _settings.maxDuration = 2.5f;
        }

        private void ApplySquishPreset()
        {
            _settings.amplitude = new Vector3(-0.12f, 0.24f, -0.12f);
            _settings.frequency = 6f;
            _settings.damping = 5f;
            _settings.settleThreshold = 0.002f;
            float quarter = 0.25f / _settings.frequency;
            _settings.timeOffset = new Vector3(quarter + 0.012f, quarter, quarter + 0.02f);
            _settings.loop = false;
            _settings.maxDuration = 2f;
        }

        private void ShiftQuarterCycle()
        {
            if (_settings.frequency <= 0.0001f)
            {
                SetStatus("振動数が 0 のときは、周期でずれを足せません。", MessageType.Warning);
                return;
            }

            float shift = 0.25f / _settings.frequency;
            _settings.timeOffset += new Vector3(shift, shift, shift);
        }

        private Vector3 CaptureBase(TransformWiggleSquishMode channel)
        {
            if (_target == null)
            {
                SetStatus("現在値を取る対象 Transform がありません。", MessageType.Warning);
                return Vector3.zero;
            }

            bool restart = _previewing;
            float time = _previewTime;
            if (restart)
                StopPreview();

            Vector3 captured;
            switch (channel)
            {
                case TransformWiggleSquishMode.RotationWiggle:
                    captured = _target.localEulerAngles;
                    break;
                case TransformWiggleSquishMode.ScaleSquish:
                    captured = _target.localScale;
                    break;
                default:
                    captured = _target.localPosition;
                    break;
            }

            _graphDirty = true;
            if (restart)
            {
                StartPreview();
                _previewTime = time;
            }

            return captured;
        }

        private void MarkParametersEdited()
        {
            _graphDirty = true;
            if (_previewing)
                RebuildPreviewClip();
            if (_appliedSinceEdit)
            {
                _appliedSinceEdit = false;
                SetStatus("数値を変更しました。クリップへ反映するには、もう一度適用してください。", MessageType.Warning);
            }

            SavePreferences();
        }

        private void StartPreview()
        {
            if (_previewing)
                return;

            AnimationWindowReflection.StopAnimationWindowPreview();
            if (AnimationMode.InAnimationMode() && !AnimationMode.InAnimationMode(GetAnimationDriver()))
            {
                SetStatus("Animation ウィンドウのプレビューを止められませんでした。", MessageType.Warning);
                return;
            }

            if (!TryGetPreviewRoot(out _, out string rootError))
            {
                SetStatus(rootError, MessageType.Warning);
                return;
            }

            if (!RebuildPreviewClip())
                return;

            AnimationMode.StartAnimationMode(GetAnimationDriver());
            _previewing = true;
            _previewTime = 0f;
            _lastPreviewStamp = EditorApplication.timeSinceStartup;
            if (!_previewRegistered)
            {
                EditorApplication.update += OnPreviewUpdate;
                _previewRegistered = true;
            }

            SamplePreview();
            SceneView.RepaintAll();
            Repaint();
        }

        private void StopPreview()
        {
            bool wasPreviewing = _previewing || _previewClip != null;
            if (_previewRegistered)
            {
                EditorApplication.update -= OnPreviewUpdate;
                _previewRegistered = false;
            }

            _previewing = false;
            if (_animationDriver != null && AnimationMode.InAnimationMode(_animationDriver))
                AnimationMode.StopAnimationMode(_animationDriver);

            DestroyPreviewClip();
            if (!wasPreviewing)
                return;

            SceneView.RepaintAll();
            Repaint();
        }

        private void OnPreviewUpdate()
        {
            if (!_previewing || _previewClip == null)
            {
                StopPreview();
                return;
            }

            if (!TryGetPreviewRoot(out _, out _))
            {
                StopPreview();
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            float delta = (float)(now - _lastPreviewStamp);
            _lastPreviewStamp = now;
            if (delta < 0f || delta > 0.1f)
                delta = 0.016f;

            _previewTime += delta * Mathf.Max(0.1f, _settings.previewSpeed);
            float length = Mathf.Max(_previewClip.length, 0.0001f);
            if (_settings.loop)
                _previewTime = Mathf.Repeat(_previewTime, length);
            else if (_previewTime > length + PreviewHoldSeconds)
                _previewTime = 0f;

            SamplePreview();
            SceneView.RepaintAll();
            Repaint();
        }

        private void SamplePreview()
        {
            if (_previewClip == null || !TryGetPreviewRoot(out Transform root, out _))
                return;

            float length = Mathf.Max(_previewClip.length, 0.0001f);
            float sampleTime = Mathf.Clamp(Mathf.Min(_previewTime, length), 0f, length);
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(root.gameObject, _previewClip, sampleTime);
            AnimationMode.EndSampling();
        }

        private bool RebuildPreviewClip()
        {
            if (!TryGetBindingPath(out string path, out string error))
            {
                SetStatus(error, MessageType.Warning);
                return false;
            }

            DestroyPreviewClip();
            _previewClip = TransformWiggleSquishUtility.CreateClip(_settings, path, "WiggleSquishPreview");
            _previewClip.hideFlags = HideFlags.HideAndDontSave;
            return true;
        }

        private void ApplyToExisting(AnimationClip clip)
        {
            if (clip == null)
            {
                SetStatus("Animation ウィンドウでクリップを開いてください。", MessageType.Warning);
                return;
            }

            if (EditorUtility.IsPersistent(clip) && !AssetDatabase.IsOpenForEdit(clip, out string message))
            {
                EditorUtility.DisplayDialog("Wiggle / Squish", "このクリップは編集できません。\n" + message, "OK");
                return;
            }

            if (!TryGetBindingPath(out string path, out string error))
            {
                SetStatus(error, MessageType.Warning);
                return;
            }

            if (TransformWiggleSquishUtility.HasTargetCurves(clip, path, _settings.mode))
            {
                string property = PropertyLabel(_settings.mode);
                string pathLabel = string.IsNullOrEmpty(path) ? "(ルート自身)" : path;
                bool overwrite = EditorUtility.DisplayDialog(
                    "Wiggle / Squish",
                    $"パス「{pathLabel}」の{property}のうち、再生位置から始まる範囲のキーを置き換えます。範囲の外のキーは残します。",
                    "置き換え",
                    "キャンセル");
                if (!overwrite)
                    return;
            }

            float startTime = ReadAnimationWindowTime();
            Undo.RecordObject(clip, "Apply Wiggle Squish");
            TransformWiggleSquishUtility.ApplyToClip(clip, _settings, path, startTime);
            EditorUtility.SetDirty(clip);
            if (AnimationWindowReflection.TryGetAnimationWindowState(out object animationState))
                AnimationWindowReflection.ResampleAnimation(animationState);
            if (EditorUtility.IsPersistent(clip))
                AssetDatabase.SaveAssetIfDirty(clip);

            _outputClip = clip;
            _appliedSinceEdit = true;
            EditorGUIUtility.PingObject(clip);
            string savedPath = AssetDatabase.GetAssetPath(clip);
            string applied = $"適用しました。開始 {startTime:0.00} 秒。";
            SetStatus(string.IsNullOrEmpty(savedPath) ? applied : applied + " " + savedPath, MessageType.Info);
            SavePreferences();
        }

        private static string PropertyLabel(TransformWiggleSquishMode mode)
        {
            switch (mode)
            {
                case TransformWiggleSquishMode.RotationWiggle:
                    return "ローカル回転";
                case TransformWiggleSquishMode.ScaleSquish:
                    return "ローカルスケール";
                default:
                    return "ローカル位置";
            }
        }

        private static float ReadAnimationWindowTime()
        {
            if (!AnimationWindowReflection.TryGetAnimationWindowState(out object state))
                return 0f;
            if (!AnimationWindowReflection.TryGetStateTime(state, out float time))
                return 0f;
            if (float.IsNaN(time) || float.IsInfinity(time) || time < 0f)
                return 0f;
            return time;
        }

        private void SyncFromAnimationWindow()
        {
            _settings.manualPath = false;
            object state = null;
            GameObject rootObject = null;
            AnimationClip clip = null;
            if (AnimationWindowReflection.TryGetAnimationWindowState(out state) && state != null)
            {
                const System.Reflection.BindingFlags flags =
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic;
                var stateType = state.GetType();
                rootObject = stateType.GetProperty("activeRootGameObject", flags)?.GetValue(state) as GameObject;
                clip = stateType.GetProperty("activeAnimationClip", flags)?.GetValue(state) as AnimationClip;
            }

            Transform root = rootObject != null ? rootObject.transform : null;
            Transform target = ResolveAnimationTarget(root, state);
            _hasAnimationContext = root != null && target != null && clip != null;
            if (root == null || target == null)
            {
                if (_previewing)
                    StopPreview();
                _target = null;
                _pathRoot = null;
                _outputClip = null;
                _settings.bindingPath = "";
                return;
            }

            bool targetChanged = target != _target;
            bool changed = targetChanged || root != _pathRoot || clip != _outputClip;
            if (!changed)
                return;

            if (_previewing)
                StopPreview();

            _target = target;
            _pathRoot = root;
            _outputClip = clip;
            string path = TransformWiggleSquishUtility.GetRelativePath(target, root);
            _settings.bindingPath = path ?? "";
            if (targetChanged)
            {
                _settings.basePosition = target.localPosition;
                _settings.baseRotation = target.localEulerAngles;
                _settings.baseScale = target.localScale;
            }

            _graphDirty = true;
            SavePreferences();
        }

        private static Transform ResolveAnimationTarget(Transform root, object state)
        {
            if (root == null)
                return null;

            if (state != null && AnimationWindowReflection.TryGetSelectedHierarchyPath(state, out string path))
            {
                if (string.IsNullOrEmpty(path))
                    return root;

                Transform fromHierarchy = root.Find(path);
                if (fromHierarchy != null)
                    return fromHierarchy;
            }

            Transform selected = Selection.activeTransform;
            if (selected != null && (selected == root || selected.IsChildOf(root)))
                return selected;

            return root;
        }

        private bool IsHumanoidBoneCached(Transform target, Transform root)
        {
            if (_humanoidCached && _humanoidTarget == target && _humanoidRoot == root)
                return _isHumanoidBone;

            _humanoidTarget = target;
            _humanoidRoot = root;
            _isHumanoidBone = TransformWiggleSquishUtility.IsHumanoidBone(target, root);
            _humanoidCached = true;
            return _isHumanoidBone;
        }

        private Transform ResolveRoot()
        {
            if (_target == null)
                return _pathRoot;
            if (_pathRoot != null)
                return _pathRoot;
            return TransformWiggleSquishUtility.FindAnimatorRoot(_target);
        }

        private bool TryGetBindingPath(out string path, out string error)
        {
            if (_settings.manualPath)
            {
                path = _settings.bindingPath ?? "";
                error = null;
                return true;
            }

            if (_target == null)
            {
                path = "";
                error = "Animation ウィンドウで、動かすオブジェクトとクリップを開いてください。";
                return false;
            }

            Transform root = ResolveRoot();
            string relative = TransformWiggleSquishUtility.GetRelativePath(_target, root);
            if (relative == null)
            {
                path = "";
                error = "対象は基準ルートの子ではありません。";
                return false;
            }

            path = relative;
            error = null;
            return true;
        }

        private bool TryGetPreviewRoot(out Transform root, out string error)
        {
            if (_target == null)
            {
                root = null;
                error = "Animation ウィンドウで、シーン上の対象を開いてください。";
                return false;
            }

            root = ResolveRoot();
            if (root == null)
            {
                error = "プレビューする基準ルートがありません。";
                return false;
            }

            if (!_settings.manualPath && TransformWiggleSquishUtility.GetRelativePath(_target, root) == null)
            {
                root = null;
                error = "対象は基準ルートの子ではありません。";
                return false;
            }

            if (!root.gameObject.scene.IsValid())
            {
                root = null;
                error = "プレビューはシーン上のオブジェクトで行えます。";
                return false;
            }

            error = null;
            return true;
        }

        private void EnsureGraphTexture(int width, int height)
        {
            if (_graphTexture != null && _graphTexture.width == width && _graphTexture.height == height)
                return;

            DestroyGraphTexture();
            _graphTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _graphDirty = true;
        }

        private void PaintGraph(int width, int height, float duration)
        {
            if (_graphTexture == null)
                return;

            var pixels = new Color32[width * height];
            var background = new Color32(18, 20, 26, 255);
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = background;

            var samples = new Vector3[width];
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            float safeDuration = Mathf.Max(duration, 0.0001f);
            for (int x = 0; x < width; x++)
            {
                float time = safeDuration * x / (width - 1);
                float xValue = TransformWiggleSquishUtility.EvaluateDelta(_settings, 0, time);
                float yValue = TransformWiggleSquishUtility.EvaluateDelta(_settings, 1, time);
                float zValue = TransformWiggleSquishUtility.EvaluateDelta(_settings, 2, time);

                samples[x] = new Vector3(xValue, yValue, zValue);
                min = Mathf.Min(min, xValue, yValue, zValue);
                max = Mathf.Max(max, xValue, yValue, zValue);
            }

            if (float.IsInfinity(min) || float.IsInfinity(max) || Mathf.Approximately(min, max))
            {
                min -= 1f;
                max += 1f;
            }
            else
            {
                float pad = (max - min) * 0.08f;
                min -= pad;
                max += pad;
            }

            if (min < 0f && max > 0f)
            {
                int zeroY = ValueToPixel(0f, min, max, height);
                var grid = new Color32(70, 74, 88, 255);
                for (int x = 0; x < width; x++)
                    pixels[zeroY * width + x] = grid;
            }

            DrawSeries(pixels, width, height, samples, 0, min, max, CurveX);
            DrawSeries(pixels, width, height, samples, 1, min, max, CurveY);
            DrawSeries(pixels, width, height, samples, 2, min, max, CurveZ);

            _graphTexture.SetPixels32(pixels);
            _graphTexture.Apply(false, false);
            _graphMinLabel = min.ToString("0.###");
            _graphMaxLabel = max.ToString("0.###");
            _graphDirty = false;
        }

        private static void DrawSeries(Color32[] pixels, int width, int height, Vector3[] samples, int axis, float min, float max, Color32 color)
        {
            int previousY = ValueToPixel(AxisOf(samples[0], axis), min, max, height);
            for (int x = 1; x < width; x++)
            {
                int y = ValueToPixel(AxisOf(samples[x], axis), min, max, height);
                DrawLine(pixels, width, height, x - 1, previousY, x, y, color);
                previousY = y;
            }
        }

        private static float AxisOf(Vector3 value, int axis)
        {
            switch (axis)
            {
                case 0:
                    return value.x;
                case 1:
                    return value.y;
                default:
                    return value.z;
            }
        }

        private static int ValueToPixel(float value, float min, float max, int height)
        {
            float u = Mathf.InverseLerp(min, max, value);
            return Mathf.Clamp(Mathf.RoundToInt(u * (height - 1)), 0, height - 1);
        }

        private static void DrawLine(Color32[] pixels, int width, int height, int x0, int y0, int x1, int y1, Color32 color)
        {
            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            while (true)
            {
                Plot(pixels, width, height, x0, y0, color);
                if (x0 == x1 && y0 == y1)
                    break;

                int doubled = err * 2;
                if (doubled > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }

                if (doubled < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        private static void Plot(Color32[] pixels, int width, int height, int x, int y, Color32 color)
        {
            if ((uint)x >= (uint)width || (uint)y >= (uint)height)
                return;

            pixels[y * width + x] = color;
            if (y + 1 < height)
                pixels[(y + 1) * width + x] = color;
        }

        private GUIStyle GraphLabelStyle
        {
            get
            {
                if (_graphLabelStyle == null)
                {
                    _graphLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
                    };
                }

                return _graphLabelStyle;
            }
        }

        private void DestroyGraphTexture()
        {
            if (_graphTexture == null)
                return;
            DestroyImmediate(_graphTexture);
            _graphTexture = null;
        }

        private AnimationModeDriver GetAnimationDriver()
        {
            if (_animationDriver == null)
            {
                _animationDriver = CreateInstance<AnimationModeDriver>();
                _animationDriver.hideFlags = HideFlags.HideAndDontSave;
            }

            return _animationDriver;
        }

        private void DestroyAnimationDriver()
        {
            if (_animationDriver == null)
                return;

            if (AnimationMode.InAnimationMode(_animationDriver))
                AnimationMode.StopAnimationMode(_animationDriver);

            DestroyImmediate(_animationDriver);
            _animationDriver = null;
        }

        private void DestroyPreviewClip()
        {
            if (_previewClip == null)
                return;
            DestroyImmediate(_previewClip);
            _previewClip = null;
        }

        private void SetStatus(string message, MessageType type)
        {
            _status = message ?? "";
            _statusType = type;
            Repaint();
        }

        private void LoadPreferences()
        {
            string json = EditorPrefs.GetString(KeySettings, "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    TransformWiggleSquishSettings loaded = JsonUtility.FromJson<TransformWiggleSquishSettings>(json);
                    if (loaded != null)
                        _settings = loaded;
                }
                catch (Exception)
                {
                    _settings = new TransformWiggleSquishSettings();
                }
            }

            _settings.Sanitize();
            _settings.manualPath = false;
        }

        private void SavePreferences()
        {
            if (_settings == null)
                return;

            _settings.Sanitize();
            _settings.manualPath = false;
            EditorPrefs.SetString(KeySettings, JsonUtility.ToJson(_settings));
        }
    }
}
