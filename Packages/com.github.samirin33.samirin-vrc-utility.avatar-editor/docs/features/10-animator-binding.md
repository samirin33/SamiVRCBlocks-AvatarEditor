# Animator Binding（ショートカット）

## 概要

Animator 編集でキーボードショートカットや追加機能を扱えるようにします。トランジションの一括作成、遷移先を後で選ぶ Make Transition モード、ステート作成、設定コピー/ペーストをショートカットで実行できます。

## 開き方

- メニュー: `samirin33 Editor Tools/Animator Binding (Shortcuts)`
- `Preferences/Samirin Editor Tools/Animator Binding` から割り当て確認・変更ができます。
- 実行時は Animator ウィンドウで対象ステートを選択して使います。

## 使い方

### 基本手順

1. Animator ウィンドウでステートを選択する
2. ショートカットを実行する（収束/拡散、コピー/ペースト、新規ステート作成）
3. 必要に応じて次の選択で遷移先を確定する（単一選択時の Make Transition モード）

### オプション・設定

- 主なショートカット（デフォルト）
  - `Alt + C`: Merged Copy 選択しているトランジションをコピーします。複数ある場合はまとめてコピーされます。
  - `Alt + V`: Merged Paste Overwrite 選択しているトランジションにコピーした情報をペーストします。元々あった設定は上書きされます。
  - `Alt + A`: Merged Paste Additive 選択しているトランジションにコピーした情報をペーストします。元々あった設定に追加する形になります。
  - `Alt + M`: New Transition Converge To Last 複数のステートを選択した後に仕様すると、最後のステートに収束するようなトランジションを作成できます。
  - `T`: New Transition Diverge From First 複数のステートを選択した後に仕様すると、最初のステートからそのほかに拡散するようなトランジションを作成できます。
  - `Alt + N`: New State At Screen Center 画面中心に新しいステートを作成します。
- `Converge To Last` / `Diverge From First` は、単一選択している場合、次のステート選択で1本のトランジションを作成できます。

## 注意事項

- 

## 関連

- 
