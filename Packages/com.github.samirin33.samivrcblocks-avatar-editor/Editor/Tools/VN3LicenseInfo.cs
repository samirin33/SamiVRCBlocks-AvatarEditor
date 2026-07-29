using System;

namespace Samirin.VRCUtility.AvatarEditor.Editor
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

        // ========== 簡易一覧・基本情報（Ver.1.10 冒頭2ページで表示される項目） ==========

        /// <summary>利用規約のバージョン（例: 1.10）</summary>
        public string licenseVersion = CurrentVersion;

        /// <summary>許諾対象データ（データの名称・アバター名など）</summary>
        public string dataName = "";

        /// <summary>権利者（作者名・法人名）</summary>
        public string rightsHolder = "";

        /// <summary>問い合わせ先</summary>
        public string contact = "";

        /// <summary>クレジット表記（表示する場合の表記例）</summary>
        public string credit = "";

        /// <summary>推奨するハッシュタグ</summary>
        public string recommendedHashtags = "";

        /// <summary>許諾期間および許諾の変更等（例: 無期限、購入日から1年間）</summary>
        public string licenseTerm = "";

        /// <summary>V クレジット表記を必要とするか</summary>
        public bool creditRequired = true;

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

        /// <summary>F 性的表現（0=不許可, 1=許可, 2=プライベート利用を除き禁止）</summary>
        public int sensitiveSexual = 0;

        /// <summary>G 暴力的表現（0=不許可, 1=許可, 2=プライベート利用を除き禁止）</summary>
        public int sensitiveViolence = 0;

        /// <summary>H 政治活動・宗教活動（0=不許可, 1=許可, 2=プライベート利用を除き禁止）</summary>
        public int sensitivePoliticalReligious = 0;

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

        /// <summary>センシティブ項目の表示用（0→不許可, 1→許可, 2→プライベート除き禁止）</summary>
        public static string SensitiveLabel(int value)
        {
            if (value == 1) return "許可";
            if (value == 2) return "プライベート除き禁止";
            return "不許可";
        }
    }
}
