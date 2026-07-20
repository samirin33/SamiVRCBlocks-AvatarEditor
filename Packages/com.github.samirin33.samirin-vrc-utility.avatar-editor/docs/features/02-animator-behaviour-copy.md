# Animator Behaviour コピー・ペースト

## 概要

Animator の**ステートにアタッチされた StateMachineBehaviour** を、右クリックメニューからコピー・ペーストできる機能です。コピーした型の Behaviour を「新規追加して値をペースト」したり、同じ型への「値の上書きペースト」、**ステート全体**のまとめてコピー・ペーストにも対応します。

## 開き方

- **Animator ウィンドウ**で、対象の Behaviour を右クリック（CONTEXT / インスペクタ）
- [AnimatorStateController](11-animator-state-controller.md) の Behaviour パネルからも Copy / Paste / ペースト(新規) を実行できます

## 使い方

### Behaviour のコピー・ペースト

1. コピー元のステートで、コピーしたい **StateMachineBehaviour** を右クリック
2. **Copy** を選択
3. 貼り付け先で次のいずれかを実行
   - **Paste as New（ペースト新規）**: コピーした型の Behaviour をステートに新規追加し、値をペースト  
     （右クリック先の Behaviour 型と一致していなくても可）
   - **Paste Values（値のペースト）**: 既に同じ型の Behaviour がある場合、その値だけ上書き

## 注意事項

- 「Paste as New」はコピー済みであれば型不一致でも実行でき、コピー元と同じ型の Behaviour がステートに追加されます。
- 「Paste Values」は、貼り付け先に同じ型の Behaviour がすでに存在する場合にのみ有効です。
- ステートのコピー・ペースト時は、遷移の接続先が同じレイヤー内のステートである場合に正しく復元されます。

## 関連

- [AnimatorStateController](11-animator-state-controller.md)
