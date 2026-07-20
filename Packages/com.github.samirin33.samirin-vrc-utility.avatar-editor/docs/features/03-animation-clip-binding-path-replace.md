# Animation Clip Binding Path Replace

## 概要

**アニメーションクリップ内のバインドパス**（例: ボーン名やオブジェクト名）を、一括でリネームするエディタウィンドウです。アバターのヒエラルキーを変更したあと、既存クリップのパスをまとめて書き換えたいときに使います。

## 開き方

- メニュー: **`samirin33 Editor Tools`** → **`Animation`** → **`Animation Clip Binding Path Replace`**
- ウィンドウタイトル: **Clip Binding Path Replace**
- [Animation Clip Selector](06-animation-clip-selector.md) の Missing 詳細から、対象 Clip・置換前パスを事前入力した状態で開くこともできます

## 使い方

### 基本手順

1. **対象の指定**
   - **クリップ配列**: 対象の AnimationClip をリストで指定
   - **ディレクトリ**: フォルダを指定し、その中のクリップを一括対象にする
2. **置換ルール**を設定
   - **パス（文字列）**: 「置換元」「置換後」の文字列を入力
   - **Transform 参照**: シーン内の Transform を「置換後」に指定すると、文字列フィールドと自動同期されます
3. **実行**ボタンで一括置換

前回の指定内容（対象の指定方法、クリップ配列、フォルダ、置換ルール）は EditorPrefs に保存され、ウィンドウを開き直しても復元されます。ディレクトリ指定の場合は、保存されていたフォルダを復元したうえで Clip を自動再取得します。

## 注意事項

- 置換は**元のアセットを直接書き換え**ます。必要に応じてバックアップや版管理で復元できるようにしてください。
- パスは文字列の部分置換です。意図しない一致に注意してください。

## 関連

- [Animation Clip Selector](06-animation-clip-selector.md) — Missing バインディング検出からの連携
- [Animator Controller Clip Replace](04-animator-controller-clip-replace.md) — クリップ**参照**の一括置換（Controller 内）
