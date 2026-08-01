using System;

namespace SamiVRCBlocksAvatar.Editor
{
    /// <summary>
    /// VN3ライセンス Ver.1.10（利用規約）の編集用データ。
    /// VRC向けサンプル（sample_vn3license110_JA）に準拠した個別条件 A～X を保持します。
    /// 参照: https://www.vn3.org/terms, https://www.vn3.org/guidance
    /// </summary>
    [Serializable]
    public class VN3LicenseInfo
    {
        public const string CurrentVersion = "1.10";

        /// <summary>センシティブ表現のプリセット（末尾が直接入力）</summary>
        public static readonly string[] SensitiveOptions =
        {
            "不許可",
            "許可",
            "プライベート除き禁止",
            "許可します(ただし棲み分けは行うこと)",
            "直接入力"
        };

        public const int SensitiveIndexCustom = 4;

        /// <summary>クレジット表記の選択肢</summary>
        public static readonly string[] CreditOptions =
        {
            "不要",
            "必要",
            "不要ですがあると嬉しいです"
        };

        public const int CreditModeNotRequired = 0;
        public const int CreditModeRequired = 1;
        public const int CreditModeAppreciated = 2;

        // ========== 簡易一覧・基本情報（Ver.1.10 冒頭2ページで表示される項目） ==========

        /// <summary>利用規約のバージョン（例: 1.10）</summary>
        public string licenseVersion = CurrentVersion;

        /// <summary>許諾対象データ（データの名称・アバター名など）</summary>
        public string dataName = "";

        /// <summary>権利者（作者名・法人名）</summary>
        public string rightsHolder = "";

        /// <summary>問い合わせ先</summary>
        public string contact = "";

        /// <summary>クレジット表記（必要時の表記例）</summary>
        public string credit = "";

        /// <summary>推奨するハッシュタグ</summary>
        public string recommendedHashtags = "";

        /// <summary>許諾期間および許諾の変更等（例: 無期限、購入日から1年間）</summary>
        public string licenseTerm = "";

        /// <summary>
        /// クレジット表記モード。
        /// 0=不要 / 1=必要 / 2=不要ですがあると嬉しいです
        /// </summary>
        public int creditMode = CreditModeRequired;

        /// <summary>V クレジット表記を必要とするか（creditMode==必要 の互換プロパティ）</summary>
        public bool creditRequired
        {
            get => creditMode == CreditModeRequired;
            set => creditMode = value ? CreditModeRequired : CreditModeNotRequired;
        }

        // ========== 個別条件 A～W（許可=true / 不許可=false） ==========

        /// <summary>A 個人利用</summary>
        public bool allowPersonalUse = true;

        /// <summary>B 法人利用</summary>
        public bool allowCorporateUse = false;

        /// <summary>C ソーシャルコミュニケーションプラットフォームへのアップロード</summary>
        public bool allowUploadToSocialPlatforms = true;

        /// <summary>D オンラインゲームプラットフォームへのアップロード（VRChat等）</summary>
        public bool allowUploadToOnlineGamePlatforms = true;

        /// <summary>E オンラインサービス内での第三者への利用の許諾</summary>
        public bool allowThirdPartyUseWithinService = false;

        /// <summary>F 性的表現（SensitiveOptions のインデックス）</summary>
        public int sensitiveSexual = 0;

        /// <summary>F 直接入力テキスト</summary>
        public string sensitiveSexualCustom = "";

        /// <summary>G 暴力的表現（SensitiveOptions のインデックス）</summary>
        public int sensitiveViolence = 0;

        /// <summary>G 直接入力テキスト</summary>
        public string sensitiveViolenceCustom = "";

        /// <summary>H 政治活動・宗教活動（SensitiveOptions のインデックス）</summary>
        public int sensitivePoliticalReligious = 0;

        /// <summary>H 直接入力テキスト</summary>
        public string sensitivePoliticalReligiousCustom = "";

        /// <summary>I 調整</summary>
        public bool allowAdjustment = true;

        /// <summary>J 改変</summary>
        public bool allowModification = true;

        /// <summary>K 他のデータを改変する目的での本データの利用</summary>
        public bool allowUseForModifyingOtherData = false;

        /// <summary>L 調整・改変の外部委託</summary>
        public bool allowExternalCommissionForModification = false;

        /// <summary>M 未改変状態での再配布</summary>
        public bool allowRedistributionUnmodified = false;

        /// <summary>N 改変したデータの配布</summary>
        public bool allowRedistributionModified = false;

        /// <summary>O 映像作品・配信・放送への利用</summary>
        public bool allowUseInVideo = false;

        /// <summary>P 出版物・電子出版物への利用</summary>
        public bool allowUseInPublication = false;

        /// <summary>Q 有体物（グッズ）への利用</summary>
        public bool allowUseInMerchandise = false;

        /// <summary>R 製品開発等のためのソフトウェアへの組み込み</summary>
        public bool allowEmbeddingInSoftware = false;

        /// <summary>S メッシュやウェイトを転用した衣装データの作成</summary>
        public bool allowMeshWeightForCostume = false;

        /// <summary>T 規格に準拠した新たなデータの作成</summary>
        public bool allowNewDataCompliantWithSpec = false;

        /// <summary>U データをモチーフにした二次的著作物の作成</summary>
        public bool allowDerivativeWorks = false;

        /// <summary>W 権利義務の譲渡等</summary>
        public bool allowTransferOfRights = false;

        /// <summary>X 特記事項（他のすべての定めより優先）</summary>
        public string specialNotes = "";

        /// <summary>センシティブ項目の表示用ラベル</summary>
        public static string SensitiveLabel(int value, string custom = null)
        {
            if (value == SensitiveIndexCustom)
                return string.IsNullOrWhiteSpace(custom) ? "（直接入力・未設定）" : custom.Trim();
            if (value >= 0 && value < SensitiveOptions.Length && value != SensitiveIndexCustom)
                return SensitiveOptions[value];
            return SensitiveOptions[0];
        }

        /// <summary>クレジット表記の簡易一覧用ラベル</summary>
        public static string CreditSummaryLabel(VN3LicenseInfo info)
        {
            if (info == null)
                return "不要";
            switch (info.creditMode)
            {
                case CreditModeAppreciated:
                    return CreditOptions[CreditModeAppreciated];
                case CreditModeNotRequired:
                    return "不要";
                default:
                    return string.IsNullOrEmpty(info.credit) ? "要（表記は別途指定）" : info.credit;
            }
        }

        /// <summary>V クレジット表記の個別条件用ラベル</summary>
        public static string CreditConditionLabel(VN3LicenseInfo info)
        {
            if (info == null)
                return "不要";
            switch (info.creditMode)
            {
                case CreditModeAppreciated:
                    return CreditOptions[CreditModeAppreciated];
                case CreditModeNotRequired:
                    return "不要";
                default:
                    return "必要";
            }
        }
    }
}
