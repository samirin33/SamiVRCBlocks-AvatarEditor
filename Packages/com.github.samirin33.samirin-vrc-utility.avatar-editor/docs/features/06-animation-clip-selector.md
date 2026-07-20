# Animation Clip Selector

## 概要

**Animation ウィンドウ**で編集中の Animator を検出し、その Animator Controller に含まれる **編集可能な AnimationClip 一覧**を表示するエディタウィンドウです。クリップをクリックすると Animation ウィンドウの編集対象が切り替わり、同じパスに複数クリップがバインドされている**競合**や、ルートから見つからない **Missing** パスも確認・修正できます。

## 開き方

- メニュー: **`samirin33 Editor Tools`** → **`Animation`** → **`Animation Clip Selector`**
- ウィンドウタイトル: **Animation Clip Selector**

## 使い方

### 基本手順

1. **Animation ウィンドウ**を開き、編集したい GameObject（アバターなど）を選択して Animator を表示する
2. **Animation Clip Selector** を開く
3. 編集中の Animator に紐づくクリップが、レイヤー・サブステートマシン（任意で BlendTree）ごとに一覧表示される
4. 一覧でクリップをクリックすると、**Animation ウィンドウの編集対象**がそのクリップに切り替わる
5. 同じバインドパスに複数クリップが割り当てられている場合は**競合**として表示され、詳細ウィンドウで確認・削除できる
6. ルートから見つからない Transform パスがある場合は **Missing** として表示され、クリックで詳細ポップアップから [Binding Path Replace](03-animation-clip-binding-path-replace.md) への連携や削除ができる。同じ Missing パスを複数 Clip が持つ場合は、全 Clip をまとめて置換・削除できる

### 競合・Missing の表示

- クリップ行のアイコンをクリックすると、それぞれ詳細ウィンドウが開きます
- **折りたたんだフォールアウト**配下に競合 / Missing がある場合は、フォールアウト名の右側にも同じ系統のアイコンが表示されます

### 競合詳細ウィンドウでの削除

| 操作 | 内容 |
|------|------|
| **選択 Clip から削除** | ウィンドウを開いた Clip だけから該当キーを削除 |
| **{レイヤー名}レイヤー側からすべて削除** | 選択 Clip と同じレイヤー上の該当 Clip すべてから削除 |
| **他レイヤー側からすべて削除** | 他レイヤーの競合 Clip すべてから削除 |
| **この Clip から削除** | 一覧の Clip 1 件だけから削除 |

### Missing 詳細ウィンドウでの操作

- **全 N Clip で置換 / 削除**: 同じ Missing パスを持つ Controller 内の全 Clip を対象
- **選択 Clip のみ**: 開いた Clip だけを対象
- Binding Path Replace 連携時は、対象 Clip と置換前パスが事前入力されます

### オプション・設定

- **設定アセット**（AnimationClipSelectorSettings）で、アイテム間の余白や、競合警告を出さないクリップ（無視リスト）などを設定できます
- **BlendTree 入れ子表示** トグルで、BlendTree を `{ステート名}: {BlendTree名}` の折りたたみグループとして表示するか、親グループに平坦化するかを切り替えられます
- ツールバーの **再読み込み**（↻）ボタンで、Clip 一覧と競合・Missing 表示を手動で更新できます
- 設定アセットは `Assets/SamirinVRCUtility/Editor/AnimationClipSelectorSettings.asset` に自動作成されます

## 注意事項

- Animation ウィンドウで「編集中のルート」が決まっていないと、Selector 側で正しい Controller を取得できない場合があります。先に Animation ウィンドウで対象を選んでください
- 競合は「異なるレイヤーで同じパス＋属性を制御している」状態を検出します。意図的な場合は無視リストに追加できます
- BlendTree 内の Clip から **Animator 使用箇所選択**を行う場合、検索スコープは所属サブステートまで（BlendTree 名は含めない）で解決されます

## 関連

- [Animation Clip Binding Path Replace](03-animation-clip-binding-path-replace.md) — Missing パスの置換
- [Animator デフォルト設定](01-animator-default-setting.md)
- [VRChat Avatar Param Setter](05-vrc-avatar-param-setter.md) — 同じ Controller のパラメータを整える場合
