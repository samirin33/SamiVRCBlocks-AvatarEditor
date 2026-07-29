# Animator Binding（ショートカット）

## 概要

Animator 編集でキーボードショートカットを扱えるようにします。トランジションの一括作成、遷移先を後で選ぶ Make Transition モード、ステート作成、設定コピー/ペーストをショートカットで実行できます。

トップメニューの `Animator Binding/` サブメニュー項目は非表示です。操作はショートカット、Preferences、CONTEXT メニューから行います。

## 開き方

- メニュー: **`SBAvatarEditor`** → **`Settings`** → **`Animator Binding`**
- Preferences: **`Preferences/Samirin Editor Tools/Animator Binding`**
- 実行時は Animator ウィンドウで対象ステート／トランジションを選択して使います

## 使い方

### 基本手順

1. Animator ウィンドウでステートまたはトランジションを選択する
2. ショートカットを実行する（収束/拡散、コピー/ペースト、新規ステート作成）
3. 必要に応じて次の選択で遷移先を確定する（単一選択時の Make Transition モード）

### 主なショートカット（デフォルト）

| キー | 機能 |
|------|------|
| `Alt + C` | Merged Copy — 選択トランジションをまとめてコピー |
| `Alt + V` | Merged Paste Overwrite — 上書きペースト |
| `Alt + A` | Merged Paste Additive — 追加ペースト |
| `Alt + M` | New Transition Converge To Last — 最後のステートへ収束 |
| `T` | New Transition Diverge From First — 先頭から拡散 |
| `Alt + N` | New State At Screen Center — 画面中心に新規ステート |

- `Converge To Last` / `Diverge From First` は、単一選択時は次のステート選択で 1 本のトランジションを作成できます
- キー割り当ては Unity の「Edit > Shortcuts...」で `Samirin` 検索から変更できます

### トランジション設定のコピー／ペースト

- ショートカット、またはトランジションの **CONTEXT（右クリック）メニュー** から実行できます
- トップメニュー `SBAvatarEditor/Animator Binding/...` の項目は表示されません

## 注意事項

- デフォルトのショートカットプロファイルでは競合時に上書きできない場合があります。必要ならショートカット用プロファイルを複製してから変更してください
- コピー／ペースト処理は AnimatorStateController など他ツールとも共有されています

## 関連

- [AnimatorStateController](11-animator-state-controller.md)
- [Animator Behaviour コピー・ペースト](02-animator-behaviour-copy.md)
