# Animator デフォルト設定

## 概要

Animator ウィンドウで**新規作成するステート・遷移・レイヤー**に、あらかじめ決めた初期値を自動で適用する機能です。Write Defaults や遷移時間などを毎回同じ値にしたいときに便利です。

## 開き方

- メニュー: **`SamiVRCBlocks-AvatarEditor`** → **`Settings`** → **`Animator Default Setting`**
- Preferences（`Preferences/Samirin Editor Tools/...`）からも同様の設定を確認・変更できます。
- 有効化すると、新規作成時のみ自動で適用されます（既存のステートは変更されません）。

## 使い方

### 基本

1. 上記メニューまたは Preferences でデフォルト値を設定する
2. Animator ウィンドウを開き、レイヤーやステートを**新規作成**する
3. 有効時は、設定したデフォルト値が自動で反映される

### 設定できる項目（例）

- **ステート**: Write Defaults（VRChat では false 推奨）、Mirror、Speed
- **遷移**: Duration、Fixed Duration、Exit Time、Offset、Can Transition To Self
- **レイヤー**: デフォルトの Weight
- **ステート名**: 重複時のナンバリング（有効/無効、区切り文字）

### オプション・設定

- 機能のオン/オフは Preferences で切り替え可能です。
- 新規「空のステート」には、指定した Motion（例: proxy_empty）を自動で割り当てられます。

## 注意事項

- 既存のステートや遷移には適用されません。あくまで「新規作成時」のみです。
- Write Defaults は VRChat では `false` が推奨です。
