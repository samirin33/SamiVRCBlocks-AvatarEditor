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
    /// </summary>
    sealed class MenuRichTextTmpPreview : IDisposable
    {
        const string NotoSansJpPath =
            "Packages/com.github.samirin33.samivrcblocks-avatar/Editor/Fonts/NotoSansJP-Medium.ttf";
        const string BundledJpFontPath =
            "Packages/com.github.samirin33.samivrcblocks-avatar/Editor/Fonts/LightNovelPOPv2.otf";
        const string LiberationSansSdfResourcesPath = "Fonts & Materials/LiberationSans SDF";

        static Font _jpSourceFont;
        static bool _jpSourceResolved;

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
        static TMP_FontAsset _notoSansJpFont;
        static TMP_FontAsset _previewHostFont;
        static bool _notoResolveAttempted;
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
            var font = ResolveJapaneseSourceFont();
            if (font != null)
                style.font = font;
            GUI.Label(rect, string.IsNullOrEmpty(text) ? " " : text, style);
        }

        /// <summary>
        /// NotoSansJP（どこに置かれていても可）→ 同梱フォントの順で探す。毎フレーム検索しないよう結果をキャッシュする。
        /// </summary>
        static Font ResolveJapaneseSourceFont()
        {
            if (_jpSourceResolved && _jpSourceFont != null)
                return _jpSourceFont;
            if (_jpSourceResolved && ReferenceEquals(_jpSourceFont, null))
                return null;
            _jpSourceResolved = true;

            var font = AssetDatabase.LoadAssetAtPath<Font>(NotoSansJpPath);
            if (font == null)
            {
                foreach (var guid in AssetDatabase.FindAssets("NotoSansJP t:Font"))
                {
                    font = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(guid));
                    if (font != null)
                        break;
                }
            }
            if (font == null)
                font = AssetDatabase.LoadAssetAtPath<Font>(BundledJpFontPath);

            _jpSourceFont = font;
            return _jpSourceFont;
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
            var noto = GetOrCreateNotoSansJpFont();
            var host = GetOrCreatePreviewHostFont(noto);
            if (host == null)
            {
                _fontMissing = true;
                StatusMessage =
                    "プレビュー用の TMP フォントが見つかりません。NotoSansJP などの日本語フォントを追加するか、TMP Essential Resources をインポートしてください。";
                return;
            }

            _tmp.font = host;
            if (host.material != null)
                _tmp.fontSharedMaterial = host.material;
            _fontMissing = false;
            StatusMessage = noto == null
                ? "日本語フォントが見つからないため、日本語が表示されない場合があります。"
                : "";
        }

        static TMP_FontAsset GetOrCreatePreviewHostFont(TMP_FontAsset noto)
        {
            if (_previewHostFont != null)
            {
                BindNotoFallback(_previewHostFont, noto);
                return _previewHostFont;
            }

            var source = LoadDefaultTmpFont();
            if (source == null)
            {
                // ホストが取れない場合は日本語フォント単体（mark が前面になる可能性あり）
                return noto;
            }

            _previewHostFont = UnityEngine.Object.Instantiate(source);
            _previewHostFont.name = "MenuRichTextPreviewHost";
            _previewHostFont.hideFlags = HideFlags.HideAndDontSave;
            BindNotoFallback(_previewHostFont, noto);
            return _previewHostFont;
        }

        /// <summary>
        /// TMP Essential Resources 未インポートだと TMP_Settings.instance が null で例外になるため捕捉する。
        /// </summary>
        static TMP_FontAsset LoadDefaultTmpFont()
        {
            try
            {
                var font = TMP_Settings.defaultFontAsset;
                if (font != null)
                    return font;
            }
            catch (Exception)
            {
            }

            return Resources.Load<TMP_FontAsset>(LiberationSansSdfResourcesPath);
        }

        static void BindNotoFallback(TMP_FontAsset host, TMP_FontAsset noto)
        {
            if (host == null || noto == null || ReferenceEquals(host, noto))
                return;

            if (host.fallbackFontAssetTable == null)
                host.fallbackFontAssetTable = new List<TMP_FontAsset>();

            // 共有アセットを汚さないよう、インスタンス側だけ入れ替える
            host.fallbackFontAssetTable.Clear();
            host.fallbackFontAssetTable.Add(noto);
        }

        static TMP_FontAsset GetOrCreateNotoSansJpFont()
        {
            if (_notoSansJpFont != null)
                return _notoSansJpFont;
            if (_notoResolveAttempted && _notoSansJpFont == null)
                return null;
            _notoResolveAttempted = true;

            var source = ResolveJapaneseSourceFont();
            if (source == null)
                return null;

            try
            {
                _notoSansJpFont = TMP_FontAsset.CreateFontAsset(
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
                    _notoSansJpFont = TMP_FontAsset.CreateFontAsset(source);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[MenuRichText] 日本語 TMP フォント作成に失敗: " + ex.Message);
                    return null;
                }
            }

            if (_notoSansJpFont != null)
            {
                _notoSansJpFont.name = source.name + " (MenuRichText Runtime)";
                _notoSansJpFont.hideFlags = HideFlags.HideAndDontSave;
            }

            return _notoSansJpFont;
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
            if (_notoSansJpFont == null || string.IsNullOrEmpty(text))
                return;
            var plain = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]*>", "");
            if (string.IsNullOrEmpty(plain))
                return;
            try
            {
                _notoSansJpFont.TryAddCharacters(plain);
            }
            catch
            {
                // TMP バージョン差は無視
            }
        }
#endif
    }
}
