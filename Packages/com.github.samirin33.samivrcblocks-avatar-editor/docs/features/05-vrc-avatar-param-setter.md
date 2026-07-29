# VRChat Avatar Param Setter

## 概要

**Animator Controller** に、VRChat で必要な **ビルトインパラメータ**（例: `VRCEmote`、`VRCFace` など）が不足している場合に、**一括で追加**するエディタウィンドウです。また、各パラメータの役割・詳細を一覧で確認できます。

## 開き方

- メニュー: **`SBAvatarEditor`** → **`Parameter`** → **`VRChat Avatar Param Setter`**
- ウィンドウタイトル: **VRChat Param Setter**

## 使い方

### 基本手順

1. **Animator Controller** フィールドに、対象の `.controller` アセットを指定
2. **「不足している VRChat パラメータを一括追加」** ボタンをクリック
3. 不足しているパラメータだけが Controller に追加されます

### ビルトインパラメータの説明

- **「ビルトインパラメータの役割・詳細」** を開くと、各パラメータの名前・型・説明を一覧で確認できます。
- Animator Controller を未指定でも、この説明だけは表示されます。

### オプション・設定

- Preferences から、追加するパラメータの種類やデフォルト値などをカスタマイズできます（詳細はウィンドウ内の **Preferences** リンクから）。

## 注意事項

- 既に存在するパラメータは上書きされません。不足分のみ追加されます。
- `.controller` アセット（Animator Controller）を指定してください。Override Controller だけでは不足パラメータの判定ができない場合があります。
