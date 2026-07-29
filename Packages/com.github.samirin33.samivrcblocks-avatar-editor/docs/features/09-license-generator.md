# VN3 License Generator（LicenseGenerator）

## 概要

**LicenseGenerator** は、VN3ライセンス Ver.1.10（VRC向け）に基づく利用規約のテキストを生成します。
生成されるテキストは単体で利用規約として効力を持つ形式で、「基本的にVN3に準拠する」旨と https://www.vn3.org/ へのリンクを含みます。VN3 License Generator ウィンドウ（メニューから開くエディタ）から利用されます。

## 開き方

- **SBAvatarEditor → File → VN3 License Generator** で VN3 License ウィンドウを開きます。
- Package Exporter ウィンドウの「VN3ライセンスを編集／指定フォルダに生成」から開くと、出力先が Package Exporter の出力ディレクトリと共有されます。

## 使い方（VN3 License Generator ウィンドウ）

1. **簡易一覧・基本情報** — 許諾対象データ・権利者・問い合わせ先・クレジット表記・推奨ハッシュタグ・許諾期間・利用規約バージョンを入力する。許諾期間が空白の場合は、既定の許諾期間文言（ユーザーとなった日から開始・期間の定めなし・規約変更の周知等）が出力される。
2. **個別条件 A～X** — 利用主体（A/B）、オンラインサービス（C/D/E）、センシティブな表現（F/G/H）、加工（I/J/K/L）、再配布（M/N）、メディア・プロダクト（O/P/Q/R）、二次創作（S/T/U）、その他（V/W）、特記事項（X）を許可/不許可で設定する。
3. **出力ディレクトリ** を指定する（Package Exporter と同一の EditorPrefs で共有されるため、Package Exporter で設定した出力先がそのまま使える）。
4. 既存の `VN3License.txt` から編集を再開する場合は「出力先フォルダから VN3License.txt を読み込み」を押す。
5. 「指定フォルダに生成」を押すと、出力先に `VN3License.txt` が作成され、成功時はエクスプローラーで開かれる。

正式な PDF 版が必要な場合は [vn3.org](https://www.vn3.org/) のジェネレータで作成してください。ウィンドウ内の「vn3.org を開く」ボタンからも開けます。

## LicenseGenerator の API・役割

### 定数

| 名前 | 説明 |
|------|------|
| `LicenseTextFileName` | `"VN3License.txt"` — 生成・読み込み対象のファイル名。 |

### 主なメソッド

| メソッド | 説明 |
|----------|------|
| `LoadFromFolder(string folderPath)` | 指定フォルダ内の `VN3License.txt` を読み込み、内容を解析して `VN3LicenseInfo` に復元する。ファイルがない・解析失敗時は `null`。 |
| `ParseLicenseText(string text)` | 生成したライセンステキスト文字列を解析し、`VN3LicenseInfo` に復元する。 |
| `BuildLicenseText(VN3LicenseInfo info)` | `VN3LicenseInfo` から、単体で利用規約として効力を有するテキストを生成する。VN3準拠の旨と vn3.org へのリンクを含む。 |
| `GenerateToFolder(string outputFolderPath, VN3LicenseInfo info)` | 指定フォルダに **VN3License.txt のみ** を生成する（JSON は生成しない）。成功時は生成したテキストファイルのフルパスを返す。 |

## 注意事項

- 入出力は **VN3License.txt のみ** です。
- 許諾期間を空白にした場合、生成テキストには「許諾期間はユーザーとなった日から開始され、期間の定めはありません。権利者が…」の既定文言が出力されます。
- 読み込みは、本ツールで生成した形式の `VN3License.txt` を前提としています。形式が異なるファイルは正しく復元されない場合があります。

## 関連

- [Package Exporter（PackageExporter）](08-package-exporter.md) — 出力先を共有し、同じフォルダにパッケージとライセンスを出力する連携が可能。
- [VN3ライセンス](https://www.vn3.org/) — 公式サイト・ジェネレータ。
