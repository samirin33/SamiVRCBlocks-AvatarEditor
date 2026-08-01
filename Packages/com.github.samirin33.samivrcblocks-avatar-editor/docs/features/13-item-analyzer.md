# Item Analyzer（ItemAnalyzer）

## 概要

指定ディレクトリ内のプレファブを解析し、**lilAvatarUtils** 由来のメッシュ／マテリアル／テクスチャ／PhysBone 等の統計と、**MA Information（NDMF ParameterInfo）** 由来の同期パラメータービット数を表示・書き出すエディタウィンドウです。

## 開き方

- メニュー: **`SBAvatarEditor`** → **`Performance`** → **`Item Analyzer`**
- Package Exporter から配布フォルダを指定して開くこともできます（`OpenWithDirectory`）
- ウィンドウタイトル: **Item Analyzer**

## 使い方

### 基本手順

1. **対象ディレクトリ** に Assets 内のフォルダを指定する（パスは EditorPrefs に保存されます）
2. 必要なら **再スキャン** / **全選択** / **全解除** で解析対象を調整する
3. **選択プレファブを解析** を押す
4. 解析結果を確認し、必要に応じてコピーまたは CSV 書き出しを行う

### ディレクトリまわり

| 操作 | 説明 |
|------|------|
| フォルダ | Project 内のフォルダアセットを指定 |
| 再スキャン | 配下の `.prefab` を再検出 |
| 全選択 / 全解除 | 一覧のチェックを一括変更 |
| Exporterの配布フォルダ | Package Exporter に設定中のソースフォルダを読み込む |

プレファブ名をクリックすると Project 上で Ping／選択します。一覧の高さはスプリッタで変更でき、値は EditorPrefs に保存されます。

### 解析結果に含まれる項目

| 項目 | 取得元 |
|------|--------|
| ポリゴン数 / 頂点数 | lilAvatarUtils |
| マテリアル数 / マテリアルスロット数 | lilAvatarUtils |
| テクスチャサイズ | lilAvatarUtils |
| PhysBone数 / PhysBoneコライダー数 | lilAvatarUtils |
| ライト数 / カメラ数 | lilAvatarUtils |
| 同期パラメーター（ビット） | MA Information（NDMF ParameterInfo の BitUsage） |

一部だけ利用可能な場合は、取得できた項目のみ表示し、未取得分は警告として残します。

### 結果の書き出し

| 操作 | 説明 |
|------|------|
| 結果をコピー | 成功した全件をテキストでクリップボードへ |
| 各結果の「コピー」 | 1 件分をクリップボードへ |
| 結果をファイルに書き出し | UTF-8 BOM 付き CSV を保存ダイアログで出力 |
| Booth Information に解析結果を書き出し | `Assets/samirin33` 配下の対象フォルダ時のみ表示。Booth Information フォルダへ解析結果ファイルを出力 |

CSV 列: Name, Path, Polygons, Vertices, Materials, MaterialSlots, TextureSizeBytes, TextureSize, PhysBones, PhysBoneColliders, Lights, Cameras, SyncParameterBits, Warnings

## 依存パッケージ

ウィンドウ上部に検出状況が表示されます。

- **lilAvatarUtils** — 未検出時はメッシュ／マテリアル／テクスチャ／PhysBone／ライト／カメラが未取得
- **MA Information（Modular Avatar / NDMF）** — 未検出時は同期パラメーターが未取得
- 両方未検出の場合は解析を実行できません

## 注意事項

- 対象は指定フォルダ配下のプレファブです。シーン上のオブジェクトは直接解析しません。

## 関連

- [Package Exporter](08-package-exporter.md) — 配布フォルダの指定に合わせて解析できます。
