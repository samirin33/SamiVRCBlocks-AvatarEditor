using System.IO;
using UnityEditor;
using UnityEngine;
using Samirin33.Editor;

namespace SamiVRCBlocksAvatar.Editor
{
    /// <summary>
    /// VN3ライセンス Ver.1.10 の情報を編集し、指定フォルダに生成するエディタウィンドウ。
    /// VRC向けサンプル（sample_vn3license110_JA）に基づく個別条件 A～X を編集します。
    /// Package Exporter の出力先と同一の EditorPrefs キーで連携します。
    /// Assets/samirin33 配下の場合は SamirinBoothInformation にも出力できます。
    /// </summary>
    public class VN3LicenseEditorWindow : EditorWindow
    {
        const string EditorPrefsKeyOutputDirectory = "SamiVRCBlocksAvatar.PackageExporter.OutputDirectory";
        const string EditorPrefsKeySourceFolder = "SamiVRCBlocksAvatar.PackageExporter.SourceFolder";

        string _outputDirectory = "";
        string _sourceFolderPath = "";
        VN3LicenseInfo _info;
        Vector2 _scrollPosition;
        bool _basicFoldout = true;
        bool _useFoldout = true;
        bool _onlineFoldout = true;
        bool _sensitiveFoldout = true;
        bool _modificationFoldout = true;
        bool _redistributionFoldout = true;
        bool _mediaFoldout = true;
        bool _derivativeFoldout = true;
        bool _otherFoldout = true;
        bool _notesFoldout = true;

        [MenuItem("SBAvatarEditor/File/VN3 License Generator", false, 1)]
        public static void Open()
        {
            var w = GetWindow<VN3LicenseEditorWindow>(false, "VN3 License", true);
            w.minSize = new Vector2(360, 480);
        }

        void OnEnable()
        {
            _outputDirectory = EditorPrefs.GetString(EditorPrefsKeyOutputDirectory, "");
            _sourceFolderPath = EditorPrefs.GetString(EditorPrefsKeySourceFolder, "");
            if (_info == null)
                _info = new VN3LicenseInfo();
        }

        void OnGUI()
        {
            SamirinEditorStyleHelper.DrawWithBlueBackground(() =>
            {
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));

                var prevLabelWidth = EditorGUIUtility.labelWidth;
                try
                {
                    EditorGUIUtility.labelWidth = Mathf.Max(EditorGUIUtility.labelWidth, 280f);
                    EditorGUILayout.Space(4);
                    SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont(
                        "VN3ライセンス Ver.1.10（VRC向け）に基づく利用規約の情報を編集し、指定フォルダに VN3License.txt を生成します。PDF版は vn3.org のジェネレータで作成してください。",
                        MessageType.Info);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("vn3.org を開く", GUILayout.ExpandWidth(false)))
                        Application.OpenURL("https://www.vn3.org/");
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space(4);

                    if (_info == null)
                        _info = new VN3LicenseInfo();

                    // ========== 簡易一覧・基本情報 ==========
                    _basicFoldout = EditorGUILayout.Foldout(_basicFoldout, "簡易一覧・基本情報", true);
                    if (_basicFoldout)
                    {
                        EditorGUI.indentLevel++;
                        _info.dataName = EditorGUILayout.TextField("許諾対象データ", _info.dataName ?? "");
                        _info.rightsHolder = EditorGUILayout.TextField("権利者", _info.rightsHolder ?? "");
                        _info.contact = EditorGUILayout.TextField("問い合わせ先", _info.contact ?? "");
                        _info.creditMode = Mathf.Clamp(
                            EditorGUILayout.Popup("クレジット表記", _info.creditMode, VN3LicenseInfo.CreditOptions),
                            0,
                            VN3LicenseInfo.CreditOptions.Length - 1);
                        if (_info.creditMode == VN3LicenseInfo.CreditModeRequired)
                            _info.credit = EditorGUILayout.TextField("クレジット表記（例）", _info.credit ?? "");
                        _info.recommendedHashtags = EditorGUILayout.TextField("推奨ハッシュタグ", _info.recommendedHashtags ?? "");
                        _info.licenseTerm = EditorGUILayout.TextField("許諾期間", _info.licenseTerm ?? "");
                        EditorGUILayout.HelpBox(
                            "空白の場合、「許諾期間はユーザーとなった日から開始され、期間の定めはありません。権利者がウェブサイト等で規約変更を周知した後の利用で変更に同意したものとみなす」となります。",
                            MessageType.None);
                        _info.licenseVersion = EditorGUILayout.TextField("利用規約バージョン", _info.licenseVersion ?? VN3LicenseInfo.CurrentVersion);
                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.Space(4);

                    // ========== 利用主体 A,B ==========
                    _useFoldout = EditorGUILayout.Foldout(_useFoldout, "利用主体", true);
                    if (_useFoldout)
                    {
                        EditorGUI.indentLevel++;
                        _info.allowPersonalUse = EditorGUILayout.Toggle("A 個人利用", _info.allowPersonalUse);
                        _info.allowCorporateUse = EditorGUILayout.Toggle("B 法人利用", _info.allowCorporateUse);
                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.Space(2);

                    // ========== オンラインサービス C,D,E ==========
                    _onlineFoldout = EditorGUILayout.Foldout(_onlineFoldout, "オンラインサービスへのアップロード", true);
                    if (_onlineFoldout)
                    {
                        EditorGUI.indentLevel++;
                        _info.allowUploadToSocialPlatforms = EditorGUILayout.Toggle("C ソーシャルプラットフォーム", _info.allowUploadToSocialPlatforms);
                        _info.allowUploadToOnlineGamePlatforms = EditorGUILayout.Toggle("D オンラインゲーム（VRChat等）", _info.allowUploadToOnlineGamePlatforms);
                        _info.allowThirdPartyUseWithinService = EditorGUILayout.Toggle("E サービス内での第三者への許諾", _info.allowThirdPartyUseWithinService);
                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.Space(2);

                    // ========== センシティブ F,G,H ==========
                    _sensitiveFoldout = EditorGUILayout.Foldout(_sensitiveFoldout, "センシティブな表現", true);
                    if (_sensitiveFoldout)
                    {
                        EditorGUI.indentLevel++;
                        DrawSensitiveField("F 性的表現", ref _info.sensitiveSexual, ref _info.sensitiveSexualCustom);
                        DrawSensitiveField("G 暴力的表現", ref _info.sensitiveViolence, ref _info.sensitiveViolenceCustom);
                        DrawSensitiveField("H 政治・宗教活動", ref _info.sensitivePoliticalReligious, ref _info.sensitivePoliticalReligiousCustom);
                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.Space(2);

                    // ========== 加工 I,J,K,L ==========
                    _modificationFoldout = EditorGUILayout.Foldout(_modificationFoldout, "加工", true);
                    if (_modificationFoldout)
                    {
                        EditorGUI.indentLevel++;
                        _info.allowAdjustment = EditorGUILayout.Toggle("I 調整", _info.allowAdjustment);
                        _info.allowModification = EditorGUILayout.Toggle("J 改変", _info.allowModification);
                        _info.allowUseForModifyingOtherData = EditorGUILayout.Toggle("K 他データ改変目的での利用", _info.allowUseForModifyingOtherData);
                        _info.allowExternalCommissionForModification = EditorGUILayout.Toggle("L 調整・改変の外部委託", _info.allowExternalCommissionForModification);
                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.Space(2);

                    // ========== 再配布 M,N ==========
                    _redistributionFoldout = EditorGUILayout.Foldout(_redistributionFoldout, "再配布・配布", true);
                    if (_redistributionFoldout)
                    {
                        EditorGUI.indentLevel++;
                        _info.allowRedistributionUnmodified = EditorGUILayout.Toggle("M 未改変での再配布", _info.allowRedistributionUnmodified);
                        _info.allowRedistributionModified = EditorGUILayout.Toggle("N 改変したデータの配布", _info.allowRedistributionModified);
                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.Space(2);

                    // ========== メディア・プロダクト O,P,Q,R ==========
                    _mediaFoldout = EditorGUILayout.Foldout(_mediaFoldout, "メディア・プロダクトへの使用", true);
                    if (_mediaFoldout)
                    {
                        EditorGUI.indentLevel++;
                        _info.allowUseInVideo = EditorGUILayout.Toggle("O 映像・配信・放送", _info.allowUseInVideo);
                        _info.allowUseInPublication = EditorGUILayout.Toggle("P 出版物・電子出版物", _info.allowUseInPublication);
                        _info.allowUseInMerchandise = EditorGUILayout.Toggle("Q 有体物（グッズ）", _info.allowUseInMerchandise);
                        _info.allowEmbeddingInSoftware = EditorGUILayout.Toggle("R ソフトウェアへの組み込み", _info.allowEmbeddingInSoftware);
                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.Space(2);

                    // ========== 二次創作 S,T,U ==========
                    _derivativeFoldout = EditorGUILayout.Foldout(_derivativeFoldout, "二次創作", true);
                    if (_derivativeFoldout)
                    {
                        EditorGUI.indentLevel++;
                        _info.allowMeshWeightForCostume = EditorGUILayout.Toggle("S メッシュ・ウェイト転用した衣装", _info.allowMeshWeightForCostume);
                        _info.allowNewDataCompliantWithSpec = EditorGUILayout.Toggle("T 規格準拠の新データ作成", _info.allowNewDataCompliantWithSpec);
                        _info.allowDerivativeWorks = EditorGUILayout.Toggle("U モチーフにした二次的著作物", _info.allowDerivativeWorks);
                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.Space(2);

                    // ========== その他 W ==========
                    _otherFoldout = EditorGUILayout.Foldout(_otherFoldout, "その他", true);
                    if (_otherFoldout)
                    {
                        EditorGUI.indentLevel++;
                        _info.allowTransferOfRights = EditorGUILayout.Toggle("W 権利義務の譲渡等", _info.allowTransferOfRights);
                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.Space(2);

                    // ========== X 特記事項 ==========
                    _notesFoldout = EditorGUILayout.Foldout(_notesFoldout, "X 特記事項（最優先）", true);
                    if (_notesFoldout)
                    {
                        EditorGUI.indentLevel++;
                        _info.specialNotes = EditorGUILayout.TextArea(_info.specialNotes ?? "", GUILayout.MinHeight(50));
                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.Space(8);

                    // ========== 出力先 ==========
                    EditorGUILayout.LabelField("出力先（Package Exporter と共有）");
                    EditorGUILayout.BeginHorizontal();
                    _outputDirectory = EditorGUILayout.TextField("出力ディレクトリ", _outputDirectory ?? "", GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("参照", GUILayout.MinWidth(36)))
                    {
                        var selected = EditorUtility.OpenFolderPanel("出力先を選択", _outputDirectory, "");
                        if (!string.IsNullOrEmpty(selected))
                            _outputDirectory = selected;
                    }
                    EditorGUILayout.EndHorizontal();
                    if (!string.IsNullOrEmpty(_outputDirectory) && GUILayout.Button("出力先フォルダから VN3License.txt を読み込み", GUILayout.ExpandWidth(false)))
                    {
                        var loaded = LicenseGenerator.LoadFromFolder(_outputDirectory);
                        if (loaded != null)
                        {
                            _info = loaded;
                            Debug.Log("[VN3 License] Loaded from " + _outputDirectory);
                        }
                        else
                            EditorUtility.DisplayDialog("読み込み", "指定フォルダに VN3License.txt が見つからないか、形式が正しくありません。", "OK");
                    }
                    EditorGUILayout.Space(6);

                    GUI.enabled = !string.IsNullOrEmpty(_outputDirectory);
                    if (GUILayout.Button("指定フォルダに生成", GUILayout.Height(28), GUILayout.ExpandWidth(true)))
                    {
                        EditorPrefs.SetString(EditorPrefsKeyOutputDirectory, _outputDirectory);
                        var result = LicenseGenerator.GenerateToFolder(_outputDirectory, _info);
                        if (result != null && File.Exists(result))
                            EditorUtility.RevealInFinder(result);
                    }
                    GUI.enabled = true;

                    DrawBoothInformationSection();

                    EditorGUILayout.Space(4);
                }
                finally
                {
                    EditorGUIUtility.labelWidth = prevLabelWidth;
                }
                EditorGUILayout.EndScrollView();
            }, new Rect(0, 0, position.width, position.height));
        }

        void DrawSensitiveField(string label, ref int index, ref string custom)
        {
            if (index < 0 || index >= VN3LicenseInfo.SensitiveOptions.Length)
                index = 0;

            index = EditorGUILayout.Popup(label, index, VN3LicenseInfo.SensitiveOptions);
            if (index == VN3LicenseInfo.SensitiveIndexCustom)
                custom = EditorGUILayout.TextField("　直接入力", custom ?? "");
        }

        void DrawBoothInformationSection()
        {
            _sourceFolderPath = EditorPrefs.GetString(EditorPrefsKeySourceFolder, _sourceFolderPath ?? "");
            var hasBoothTarget = PackageExporter.TryGetBoothInformationOutputFolder(
                _sourceFolderPath, out var boothFolder);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("SamirinBoothInformation（Assets/samirin33 配下向け）");
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("配布フォルダ", _sourceFolderPath ?? "");

            if (!PackageExporter.IsUnderSamirin33Folder(_sourceFolderPath))
            {
                SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont(
                    "Package Exporter の配布フォルダが Assets/samirin33 配下のとき、対応する Booth Information フォルダへライセンスを出力できます。",
                    MessageType.Info);
                return;
            }

            if (!hasBoothTarget)
            {
                SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont(
                    "Booth Information の出力先を解決できません。製品フォルダ名を確認してください。",
                    MessageType.Warning);
                return;
            }

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("Booth Information", boothFolder);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Booth Information から読み込み", GUILayout.ExpandWidth(true)))
            {
                var fullFolder = PackageExporter.ToFullPath(boothFolder);
                var loaded = LicenseGenerator.LoadFromFolder(fullFolder);
                if (loaded != null)
                {
                    _info = loaded;
                    Debug.Log("[VN3 License] Loaded from Booth Information: " + boothFolder);
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "読み込み",
                        $"Booth Information に {LicenseGenerator.LicenseTextFileName} が見つからないか、形式が正しくありません。\n{boothFolder}",
                        "OK");
                }
            }

            if (GUILayout.Button("Booth Information に生成", GUILayout.ExpandWidth(true)))
            {
                var result = PackageExporter.WriteTextToBoothInformationFolder(
                    _sourceFolderPath,
                    LicenseGenerator.LicenseTextFileName,
                    LicenseGenerator.BuildLicenseText(_info));
                if (!string.IsNullOrEmpty(result) && File.Exists(result))
                    EditorUtility.RevealInFinder(result);
                else
                    EditorUtility.DisplayDialog("生成", "Booth Information への書き出しに失敗しました。", "OK");
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
