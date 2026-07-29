# Animator Controller Clip Replace

## 概要

**Animator Controller**（または Animator が参照している Controller）のなかで、特定の **AnimationClip への参照**を、別のクリップに一括で置き換えるエディタウィンドウです。クリップの差し替えや統合時に便利です。

## 開き方

- メニュー: **`SamiVRCBlocks-AvatarEditor`** → **`Animation`** → **`Animator Controller Clip Replace`**
- ウィンドウタイトル: **Controller Clip Replace**

## 使い方

### 基本手順

1. **対象の指定**
   - **Animator Controller**: 対象の `.controller` アセットを指定
   - **Animator**: シーン上の Animator を指定（その Runtime Controller が対象）
2. **置換元の Clip** と **置換後の Clip** を指定
3. **プレビュー**で、何件の参照が置換されるか確認
4. **実行**で一括置換

### オプション・設定

- 「置換元のClip」と一致している参照**のみ**が置換されます。他のクリップは変更されません。
- 複数回実行すれば、複数の「置換元 → 置換後」を順番に適用できます。

## 注意事項

- Controller アセットを直接書き換えます。必要に応じてバックアップやバージョン管理で復元できるようにしてください。
- 置換後は Animator Controller が Dirty になるため、保存を忘れずに行ってください。

## 関連

- [Animation Clip Binding Path Replace](03-animation-clip-binding-path-replace.md) — クリップ**内のパス**の一括リネーム
