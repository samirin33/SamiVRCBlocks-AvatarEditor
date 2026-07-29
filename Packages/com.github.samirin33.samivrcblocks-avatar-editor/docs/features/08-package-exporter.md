# Package Exporter（PackageExporter）

## 概要

**PackageExporter** は、プロジェクト内の指定フォルダ（`Assets/` 以下）を UnityPackage（`.unitypackage`）としてバージョン更新情報をつけて素早くエクスポートできます。
パッケージのバージョン更新情報は `PackageAssetInfo.json` で管理し、エクスポート前にソースフォルダ直下に保存してからパッケージに含めます。Package Exporter ウィンドウ（メニューから開くエディタ）から利用されます。

## 開き方

- **SBAvatarEditor → File → Package Exporter** で Package Exporter ウィンドウを開きます。
- ウィンドウ内でソースフォルダ・パッケージ名・バージョン・出力先を指定し、「エクスポート !」で実行します。

## 使い方（Package Exporter ウィンドウ）

1. 配布したい **ソースフォルダ**（Assets 内）をオブジェクトフィールドまたはパスで指定する。
2. **パッケージ名** と **バージョン**（Major / Minor / Patch）を入力する。
3. 作成・配布予定のギミックにSamiVRCBlocks-Avatar関連コンポーネントが含まれる場合は**パッケージに「AvatarInstaller」を含める**を有効にしてください。
4. PackageAssetInfo の作者・説明・Releases・関連URL を必要に応じて編集する。
5. **出力ディレクトリ** を指定し、必要なら「既存ファイルを上書きする」にチェックを入れる。
6. 「エクスポート !」を押すと、`{パッケージ名}_ver{バージョン}.unitypackage` が出力され、成功時はエクスプローラーで開かれる。

VN3 ライセンスを同じ出力先に生成したい場合は「VN3ライセンスを編集／指定フォルダに生成」から VN3 License Generator を開き、出力先は Package Exporter と共有されます。

## PackageExporter の API・役割

### 定数

| 名前 | 説明 |
|------|------|
| `AssetInfoFileName` | `"PackageAssetInfo.json"` — フォルダ直下に保存するメタ情報ファイル名。 |

### 主なメソッド

| メソッド | 説明 |
|----------|------|
| `LoadAssetInfo(string assetFolderPath)` | 指定フォルダ直下の `PackageAssetInfo.json` を読み込む。存在しない場合は `null`。 |
| `SaveAssetInfo(string assetFolderPath, PackageAssetInfo info)` | 指定フォルダ直下に `PackageAssetInfo.json` を書き込む。保存後に `AssetDatabase.Refresh()` を実行。 |
| `GetAssetPathsInFolder(string assetFolderPath)` | フォルダ以下（直下含む）の全アセットパスを取得。`Assets/` から始まるパスのみ。 |
| `ExportPackage(...)` | UnityPackage をエクスポート。出力前に `PackageAssetInfo` をソースフォルダ直下に保存し、そのフォルダ内のアセットをパッケージに含める。 |

### ExportPackage のパラメータ

- `sourceAssetFolder` — Assets/ 以下のソースフォルダパス
- `assetInfo` — パッケージ情報（名前・バージョン・作者・説明・URL・リリース履歴など）
- `packageName` — パッケージ表示名（出力ファイル名の「名前」部分）
- `version` — バージョン（x.x.x 形式）
- `outputDirectory` — 出力先ディレクトリ（フルパス）
- `overwrite` — 既存ファイルを上書きするか
- `includeInstallerFolder` — `Assets/AvatarInstaller` をパッケージに含めるか

戻り値は成功時は出力 `.unitypackage` のフルパス、失敗時は `null`。

## 注意事項

- ソースフォルダは `Assets/` 以下で有効なフォルダパスである必要があります。
- 同じ出力ファイルが既に存在し、かつ「既存ファイルを上書きする」がオフの場合はエクスポートされません。
- `PackageAssetInfo.json` はエクスポート前にソースフォルダ直下に上書き保存されます。

## 関連

- [VN3 License Generator（LicenseGenerator）](09-license-generator.md) — 同じ出力先に VN3License.txt を生成する機能と連携します。
