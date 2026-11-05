using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
#if SAMIVRC_HAS_TMPRO
using TMPro;
using UnityEngine.TextCore.LowLevel;
#endif

namespace SamiVRCBlocksAvatar.Editor
{
    /// <summary>
    /// Expression Menu 向けの TMPro リッチテキストを、エディタウィンドウ内で描画します。
    /// Gesture Manager 非導入時のフラットプレビュー用。フォントはラジアルプレビューと同じエディタ標準です。
    /// </summary>
    sealed class MenuRichTextTmpPreview : IDisposable
    {
        const string LiberationSansSdfResourcesPath = "Fonts & Materials/LiberationSans SDF";

        public static bool IsAvailable
        {
            get
            {
#if SAMIVRC_HAS_TMPRO
                return true;
#else
                return false;
#endif
            }
        }

        PreviewRenderUtility _preview;
#if SAMIVRC_HAS_TMPRO
        Texture _lastTexture;
        string _lastText;
        Color _lastBg;
        int _lastW;
        int _lastH;
        bool _fontMissing;
        TextMeshPro _tmp;
        static TMP_FontAsset _editorDefaultFont;
        static TMP_FontAsset _previewHostFont;
        static bool _editorFontResolveAttempted;
#endif

        public string StatusMessage { get; private set; } = "";

        public void Draw(Rect rect, string text, Color background)
        {
            if (rect.width < 8f || rect.height < 8f)
                return;

#if !SAMIVRC_HAS_TMPRO
            DrawFallback(rect, text, background);
            return;
#else
            var w = Mathf.Max(8, Mathf.RoundToInt(rect.width));
            var h = Mathf.Max(8, Mathf.RoundToInt(rect.height));
            if (_lastTexture == null || _lastW != w || _lastH != h || _lastText != text || _lastBg != background)
                Render(w, h, text ?? "", background);

            if (_lastTexture != null)
                GUI.DrawTexture(rect, _lastTexture, ScaleMode.StretchToFill, false);
            else
                DrawFallback(rect, text, background);
#endif
        }

        public void Dispose()
        {
#if SAMIVRC_HAS_TMPRO
            if (_tmp != null)
            {
                var go = _tmp.gameObject;
                _tmp = null;
                if (go != null)
                    UnityEngine.Object.DestroyImmediate(go);
            }
            _lastTexture = null;
#endif
            if (_preview != null)
            {
                _preview.Cleanup();
                _preview = null;
            }
        }

        static void DrawFallback(Rect rect, string text, Color background)
        {
            EditorGUI.DrawRect(rect, background);
            var style = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                richText = true,
                fontSize = 16,
                normal = { textColor = Color.white },
                padding = new RectOffset(12, 12, 8, 8)
            };
            var font = ResolveEditorDefaultFont();
            if (font != null)
                style.font = font;
            GUI.Label(rect, string.IsNullOrEmpty(text) ? " " : text, style);
        }

        /// <summary>
        /// MenuRichTextRadialPreview.UseDefaultFont と同じ基準のエディタ標準フォント。
        /// </summary>
        static Font ResolveEditorDefaultFont()
        {
            var font = EditorStyles.standardFont;
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font;
        }

#if SAMIVRC_HAS_TMPRO
        void Ensure()
        {
            if (_preview != null && _tmp != null)
                return;

            Dispose();
            _preview = new PreviewRenderUtility();
            _preview.camera.orthographic = true;
            _preview.camera.nearClipPlane = 0.01f;
            _preview.camera.farClipPlane = 20f;
            _preview.camera.clearFlags = CameraClearFlags.SolidColor;
            _preview.camera.transform.position = new Vector3(0f, 0f, -8f);
            _preview.camera.transform.rotation = Quaternion.identity;
            _preview.ambientColor = Color.white;
            if (_preview.lights != null && _preview.lights.Length > 0)
            {
                _preview.lights[0].intensity = 1.6f;
                _preview.lights[0].transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                if (_preview.lights.Length > 1)
                    _preview.lights[1].intensity = 1.2f;
            }

            var go = EditorUtility.CreateGameObjectWithHideFlags(
                "MenuRichTextTmpPreview",
                HideFlags.HideAndDontSave,
                typeof(TextMeshPro));
            var previewScene = _preview.camera.scene;
            if (previewScene.IsValid())
                SceneManager.MoveGameObjectToScene(go, previewScene);
            else
                _preview.AddSingleGO(go);

            _tmp = go.GetComponent<TextMeshPro>();
            if (_tmp == null)
            {
                StatusMessage = "TextMeshPro プレビューの初期化に失敗しました。";
                return;
            }

            _tmp.alignment = TextAlignmentOptions.Center;
            _tmp.color = Color.white;
            _tmp.fontSize = 8f;
            _tmp.enableAutoSizing = true;
            _tmp.fontSizeMin = 2f;
            _tmp.fontSizeMax = 11f;
            _tmp.enableWordWrapping = true;
            _tmp.overflowMode = TextOverflowModes.Overflow;
            _tmp.richText = true;
            _tmp.extraPadding = true;
            _tmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _tmp.rectTransform.anchoredPosition3D = Vector3.zero;
            _tmp.rectTransform.sizeDelta = new Vector2(12f, 4.5f);
            var meshRenderer = _tmp.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
            }

            ApplyPreviewFonts();
        }

        /// <summary>
        /// VRC メニューと同様、文字をフォールバック側サブメッシュに載せて mark を背面に回す。
        /// 単一フォントだと mark ジオメトリが文字の前面に描画される。
        /// </summary>
        void ApplyPreviewFonts()
        {
            var glyphFont = GetOrCreateEditorDefaultTmpFont();
            if (glyphFont == null)
            {
                _fontMissing = true;
                StatusMessage = "エディタ標準フォントから TMP フォントを作成できませんでした。";
                return;
            }

            var host = GetOrCreatePreviewHostFont(glyphFont);
            _tmp.font = host;
            if (host.material != null)
                _tmp.fontSharedMaterial = host.material;
            _fontMissing = false;
            StatusMessage = "";
        }

        static TMP_FontAsset GetOrCreatePreviewHostFont(TMP_FontAsset glyphFont)
        {
            if (_previewHostFont != null)
            {
                BindGlyphFallback(_previewHostFont, glyphFont);
                return _previewHostFont;
            }

            TMP_FontAsset source = null;
            if (TMP_Settings.defaultFontAsset != null)
                source = TMP_Settings.defaultFontAsset;
            if (source == null)
                source = Resources.Load<TMP_FontAsset>(LiberationSansSdfResourcesPath);

            if (source == null)
            {
                // ホストが取れない場合は標準フォント単体（mark が前面になる可能性あり）
                return glyphFont;
            }

            _previewHostFont = UnityEngine.Object.Instantiate(source);
            _previewHostFont.name = "MenuRichTextPreviewHost";
            _previewHostFont.hideFlags = HideFlags.HideAndDontSave;
            BindGlyphFallback(_previewHostFont, glyphFont);
            return _previewHostFont;
        }

        static void BindGlyphFallback(TMP_FontAsset host, TMP_FontAsset glyphFont)
        {
            if (host == null || glyphFont == null || ReferenceEquals(host, glyphFont))
                return;

            if (host.fallbackFontAssetTable == null)
                host.fallbackFontAssetTable = new List<TMP_FontAsset>();

            // 共有アセットを汚さないよう、インスタンス側だけ入れ替える
            host.fallbackFontAssetTable.Clear();
            host.fallbackFontAssetTable.Add(glyphFont);
        }

        static TMP_FontAsset GetOrCreateEditorDefaultTmpFont()
        {
            if (_editorDefaultFont != null)
                return _editorDefaultFont;
            if (_editorFontResolveAttempted && _editorDefaultFont == null)
                return null;
            _editorFontResolveAttempted = true;

            var source = ResolveEditorDefaultFont();
            if (source == null)
                return null;

            try
            {
                _editorDefaultFont = TMP_FontAsset.CreateFontAsset(
                    source,
                    90,
                    9,
                    GlyphRenderMode.SDFAA,
                    1024,
                    1024,
                    AtlasPopulationMode.Dynamic);
            }
            catch (Exception)
            {
                try
                {
                    _editorDefaultFont = TMP_FontAsset.CreateFontAsset(source);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[MenuRichText] エディタ標準 TMP フォント作成に失敗: " + ex.Message);
                    return null;
                }
            }

            if (_editorDefaultFont != null)
            {
                _editorDefaultFont.name = "EditorDefault (MenuRichText Runtime)";
                _editorDefaultFont.hideFlags = HideFlags.HideAndDontSave;
            }

            return _editorDefaultFont;
        }

        void Render(int width, int height, string text, Color background)
        {
            try
            {
                Ensure();
            }
            catch (Exception ex)
            {
                StatusMessage = "プレビューの初期化に失敗しました: " + ex.Message;
                _lastTexture = null;
                return;
            }

            if (_preview == null || _tmp == null || _fontMissing)
            {
                _lastTexture = null;
                return;
            }

            ApplyPreviewFonts();

            var previousLog = Application.GetStackTraceLogType(LogType.Warning);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            try
            {
                TryAddCharacters(text);
                _tmp.text = string.IsNullOrEmpty(text) ? " " : text;
                var aspect = (float)width / Mathf.Max(1, height);
                var worldHeight = 3.6f;
                var worldWidth = worldHeight * aspect;
                _tmp.rectTransform.sizeDelta = new Vector2(worldWidth * 0.92f, worldHeight * 0.78f);
                _tmp.ForceMeshUpdate(true);
            }
            finally
            {
                Application.SetStackTraceLogType(LogType.Warning, previousLog);
            }

            _preview.camera.backgroundColor = background;
            _preview.camera.orthographicSize = 1.8f;
            _preview.camera.aspect = (float)width / Mathf.Max(1, height);

            var previewRect = new Rect(0f, 0f, width, height);
            _preview.BeginPreview(previewRect, GUIStyle.none);
            _preview.camera.Render();
            _lastTexture = _preview.EndPreview();
            _lastW = width;
            _lastH = height;
            _lastText = text;
            _lastBg = background;
        }

        static void TryAddCharacters(string text)
        {
            if (_editorDefaultFont == null || string.IsNullOrEmpty(text))
                return;
            var plain = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]*>", "");
            if (string.IsNullOrEmpty(plain))
                return;
            try
            {
                _editorDefaultFont.TryAddCharacters(plain);
            }
            catch
            {
                // TMP バージョン差は無視
            }
        }
#endif
    }
}
