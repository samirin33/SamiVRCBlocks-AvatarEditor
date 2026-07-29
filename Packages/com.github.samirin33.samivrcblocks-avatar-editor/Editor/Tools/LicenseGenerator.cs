using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Samirin.VRCUtility.AvatarEditor.Editor
{
    /// <summary>
    /// VN3ライセンス Ver.1.10 のテキストを生成し、指定フォルダのテキストファイルから読み込むユーティリティ。
    /// JSONは使用せず、VN3License.txt のみを入出力とします。
    /// </summary>
    public static class LicenseGenerator
    {
        public const string LicenseTextFileName = "VN3License.txt";

        /// <summary>許諾期間が空白の場合に出力する既定の文言（VN3ライセンスに基づく）</summary>
        const string DefaultLicenseTermText =
            "許諾期間はユーザーとなった日から開始され、期間の定めはありません。権利者が、権利者の管理するウェブサイトやブログ等に本利用規約に関する条件の変更(追加、変更または削除を含みますがこれに限られません)を掲示し合理的な方法で周知した場合において、その効力発生日以降にユーザーが本データを利用した場合は、当該変更に同意したものとみなします。従って、ユーザーは、合理的な範囲内で定期的に権利者の発信する情報を確認しなければなりません。";

        static string GetDefaultLicenseTermText() => DefaultLicenseTermText;

        /// <summary>
        /// 指定フォルダ内の VN3License.txt を読み込み、VN3LicenseInfo に復元する。
        /// </summary>
        public static VN3LicenseInfo LoadFromFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return null;
            var path = Path.Combine(folderPath, LicenseTextFileName).Replace("\\", "/");
            if (!File.Exists(path)) return null;
            try
            {
                var text = File.ReadAllText(path);
                return ParseLicenseText(text);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LicenseGenerator] Failed to load: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 生成したライセンステキストを解析し、VN3LicenseInfo に復元する。
        /// </summary>
        public static VN3LicenseInfo ParseLicenseText(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var info = new VN3LicenseInfo();
            var lines = new List<string>();
            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                    lines.Add(line);
            }

            var i = 0;
            while (i < lines.Count)
            {
                var line = lines[i];
                if (line.StartsWith("【許諾対象データ】 "))
                {
                    info.dataName = line.Substring("【許諾対象データ】 ".Length).Trim();
                    if (info.dataName == "—") info.dataName = "";
                }
                else if (line.StartsWith("【権利者】 "))
                {
                    info.rightsHolder = line.Substring("【権利者】 ".Length).Trim();
                    if (info.rightsHolder == "—") info.rightsHolder = "";
                }
                else if (line.StartsWith("【問い合わせ先】 "))
                {
                    info.contact = line.Substring("【問い合わせ先】 ".Length).Trim();
                    if (info.contact == "—") info.contact = "";
                }
                else if (line.StartsWith("【クレジット表記】 "))
                {
                    var val = line.Substring("【クレジット表記】 ".Length).Trim();
                    if (val == "不要") { info.creditRequired = false; info.credit = ""; }
                    else if (val == "要（表記は別途指定）") { info.creditRequired = true; info.credit = ""; }
                    else { info.creditRequired = true; info.credit = val; }
                }
                else if (line.StartsWith("【推奨ハッシュタグ】 "))
                {
                    info.recommendedHashtags = line.Substring("【推奨ハッシュタグ】 ".Length).Trim();
                    if (info.recommendedHashtags == "—") info.recommendedHashtags = "";
                }
                else if (line.StartsWith("【許諾期間】"))
                {
                    var rest = line.Substring("【許諾期間】".Length).Trim();
                    if (rest.Length > 0)
                        info.licenseTerm = rest;
                    else if (i + 1 < lines.Count && lines[i + 1].TrimStart().StartsWith("許諾期間はユーザーとなった日から"))
                        info.licenseTerm = ""; // 既定文言の場合は空のまま
                    i++;
                }
                else if (line.StartsWith("【利用規約バージョン】 "))
                {
                    info.licenseVersion = line.Substring("【利用規約バージョン】 ".Length).Trim();
                }
                else if (line == "【個別条件】")
                {
                    i++;
                    while (i < lines.Count)
                    {
                        var cond = lines[i];
                        if (cond.StartsWith("【X 特記事項】") || cond.StartsWith("────────────────") || cond.StartsWith("本記載のほか"))
                            break;
                        ParseConditionLine(cond, info);
                        i++;
                    }
                    continue;
                }
                else if (line.StartsWith("【X 特記事項】"))
                {
                    var notes = new List<string>();
                    i++;
                    while (i < lines.Count && !lines[i].StartsWith("────────────────") && !lines[i].StartsWith("本記載のほか"))
                    {
                        notes.Add(lines[i]);
                        i++;
                    }
                    info.specialNotes = string.Join(Environment.NewLine, notes).Trim();
                    continue;
                }
                i++;
            }
            return info;
        }

        static void ParseConditionLine(string line, VN3LicenseInfo info)
        {
            var colon = line.IndexOf(": ");
            if (colon < 0) return;
            var value = line.Substring(colon + 2).Trim();
            var allow = value == "許可" || value == "必要";
            if (line.Contains("A 個人利用")) info.allowPersonalUse = allow;
            else if (line.Contains("B 法人利用")) info.allowCorporateUse = allow;
            else if (line.Contains("C ソーシャル")) info.allowUploadToSocialPlatforms = allow;
            else if (line.Contains("D オンラインゲーム")) info.allowUploadToOnlineGamePlatforms = allow;
            else if (line.Contains("E オンラインサービス内")) info.allowThirdPartyUseWithinService = allow;
            else if (line.Contains("F 性的表現")) info.sensitiveSexual = ParseSensitive(value);
            else if (line.Contains("G 暴力的表現")) info.sensitiveViolence = ParseSensitive(value);
            else if (line.Contains("H 政治")) info.sensitivePoliticalReligious = ParseSensitive(value);
            else if (line.Contains("I 調整")) info.allowAdjustment = allow;
            else if (line.Contains("J 改変")) info.allowModification = allow;
            else if (line.Contains("K 他データ改変")) info.allowUseForModifyingOtherData = allow;
            else if (line.Contains("L 調整・改変の外部")) info.allowExternalCommissionForModification = allow;
            else if (line.Contains("M 未改変状態")) info.allowRedistributionUnmodified = allow;
            else if (line.Contains("N 改変したデータの配布")) info.allowRedistributionModified = allow;
            else if (line.Contains("O 映像作品")) info.allowUseInVideo = allow;
            else if (line.Contains("P 出版物")) info.allowUseInPublication = allow;
            else if (line.Contains("Q 有体物")) info.allowUseInMerchandise = allow;
            else if (line.Contains("R ソフトウェアへの組み込み")) info.allowEmbeddingInSoftware = allow;
            else if (line.Contains("S メッシュ・ウェイト転用")) info.allowMeshWeightForCostume = allow;
            else if (line.Contains("T 規格準拠の新たな")) info.allowNewDataCompliantWithSpec = allow;
            else if (line.Contains("U データをモチーフにした")) info.allowDerivativeWorks = allow;
            else if (line.Contains("V クレジット表記")) info.creditRequired = (value == "必要");
            else if (line.Contains("W 権利義務の譲渡")) info.allowTransferOfRights = allow;
        }

        static int ParseSensitive(string value)
        {
            if (value == "許可") return 1;
            if (value == "プライベート除き禁止") return 2;
            return 0;
        }

        const string Vn3Url = "https://www.vn3.org/";

        /// <summary>
        /// 単体で利用規約として効力を有するテキストを生成する。基本的にVN3に準拠する旨と vn3.org へのリンクを含む。
        /// </summary>
        public static string BuildLicenseText(VN3LicenseInfo info)
        {
            if (info == null) return "";

            var lines = new List<string>
            {
                "════════════════════════════════════════",
                "  本データの利用規約",
                "════════════════════════════════════════",
                "",
                "本テキストは、本データの利用に関する利用規約として効力を有します。",
                "",
                "本利用規約は、基本的にVN3ライセンス（Ver.1.10）に準拠します。",
                "VN3ライセンスの詳細は以下を参照してください。",
                "  " + Vn3Url,
                "",
                "以下に、権利者が定める許諾対象・個別条件等を記載します。",
                "",
                "────────────────────────────────────────",
                "【許諾対象データ】 " + (string.IsNullOrEmpty(info.dataName) ? "—" : info.dataName),
                "【権利者】 " + (string.IsNullOrEmpty(info.rightsHolder) ? "—" : info.rightsHolder),
                "【問い合わせ先】 " + (string.IsNullOrEmpty(info.contact) ? "—" : info.contact),
                "【クレジット表記】 " + (info.creditRequired ? (string.IsNullOrEmpty(info.credit) ? "要（表記は別途指定）" : info.credit) : "不要"),
                "【推奨ハッシュタグ】 " + (string.IsNullOrEmpty(info.recommendedHashtags) ? "—" : info.recommendedHashtags),
                "【許諾期間】" + (string.IsNullOrEmpty(info.licenseTerm) ? (Environment.NewLine + "  " + GetDefaultLicenseTermText()) : (" " + info.licenseTerm)),
                "【利用規約バージョン】 " + (string.IsNullOrEmpty(info.licenseVersion) ? VN3LicenseInfo.CurrentVersion : info.licenseVersion),
                "",
                "────────────────────────────────────────",
                "【個別条件】",
                "  A 個人利用: " + (info.allowPersonalUse ? "許可" : "不許可"),
                "  B 法人利用: " + (info.allowCorporateUse ? "許可" : "不許可"),
                "  C ソーシャルプラットフォームへのアップロード: " + (info.allowUploadToSocialPlatforms ? "許可" : "不許可"),
                "  D オンラインゲームプラットフォームへのアップロード（VRChat等）: " + (info.allowUploadToOnlineGamePlatforms ? "許可" : "不許可"),
                "  E オンラインサービス内での第三者への利用の許諾: " + (info.allowThirdPartyUseWithinService ? "許可" : "不許可"),
                "  F 性的表現: " + VN3LicenseInfo.SensitiveLabel(info.sensitiveSexual),
                "  G 暴力的表現: " + VN3LicenseInfo.SensitiveLabel(info.sensitiveViolence),
                "  H 政治活動・宗教活動: " + VN3LicenseInfo.SensitiveLabel(info.sensitivePoliticalReligious),
                "  I 調整: " + (info.allowAdjustment ? "許可" : "不許可"),
                "  J 改変: " + (info.allowModification ? "許可" : "不許可"),
                "  K 他データ改変目的での利用: " + (info.allowUseForModifyingOtherData ? "許可" : "不許可"),
                "  L 調整・改変の外部委託: " + (info.allowExternalCommissionForModification ? "許可" : "不許可"),
                "  M 未改変状態での再配布: " + (info.allowRedistributionUnmodified ? "許可" : "不許可"),
                "  N 改変したデータの配布: " + (info.allowRedistributionModified ? "許可" : "不許可"),
                "  O 映像作品・配信・放送: " + (info.allowUseInVideo ? "許可" : "不許可"),
                "  P 出版物・電子出版物: " + (info.allowUseInPublication ? "許可" : "不許可"),
                "  Q 有体物（グッズ）: " + (info.allowUseInMerchandise ? "許可" : "不許可"),
                "  R ソフトウェアへの組み込み: " + (info.allowEmbeddingInSoftware ? "許可" : "不許可"),
                "  S メッシュ・ウェイト転用した衣装データの作成: " + (info.allowMeshWeightForCostume ? "許可" : "不許可"),
                "  T 規格準拠の新たなデータの作成: " + (info.allowNewDataCompliantWithSpec ? "許可" : "不許可"),
                "  U データをモチーフにした二次的著作物: " + (info.allowDerivativeWorks ? "許可" : "不許可"),
                "  V クレジット表記: " + (info.creditRequired ? "必要" : "不要"),
                "  W 権利義務の譲渡等: " + (info.allowTransferOfRights ? "許可" : "不許可"),
                ""
            };

            if (!string.IsNullOrEmpty(info.specialNotes))
            {
                lines.Add("【X 特記事項】（他の定めより優先）");
                lines.Add(info.specialNotes);
                lines.Add("");
            }

            lines.Add("────────────────────────────────────────");
            lines.Add("本記載のほか、VN3ライセンスの基本条項（語の定義、免責、禁止行為、準拠法等）が本利用規約に適用されます。");
            lines.Add("詳細は " + Vn3Url + " を参照してください。");
            lines.Add("");
            lines.Add("════════════════════════════════════════");
            lines.Add("  以上が本データの利用規約です。");
            lines.Add("════════════════════════════════════════");
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// 指定フォルダに VN3License.txt のみを生成する（JSONは生成しない）。
        /// </summary>
        public static string GenerateToFolder(string outputFolderPath, VN3LicenseInfo info)
        {
            if (info == null || string.IsNullOrEmpty(outputFolderPath))
            {
                Debug.LogWarning("[LicenseGenerator] outputFolderPath and info are required.");
                return null;
            }

            var dir = outputFolderPath.Replace("\\", "/").TrimEnd('/');
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var textPath = Path.Combine(dir, LicenseTextFileName).Replace("\\", "/");
            var text = BuildLicenseText(info);
            File.WriteAllText(textPath, text);

            Debug.Log($"[LicenseGenerator] Generated: {textPath}");
            return textPath;
        }
    }
}
