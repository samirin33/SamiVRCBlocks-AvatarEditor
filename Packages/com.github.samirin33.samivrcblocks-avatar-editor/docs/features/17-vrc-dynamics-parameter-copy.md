# VRC Dynamics Parameter Copy

## 概要

**VRC PhysBone** / **VRC Contact Receiver** の公式 Inspector 末尾に、Animation パラメーター名のコピー／追加 UI を Harmony で差し込みます。

参考:

- [PhysBones](https://creators.vrchat.com/common-components/physbones/)
- [Contacts](https://creators.vrchat.com/common-components/contacts/)

## 開き方

対象コンポーネントを選択すると、Inspector 下部に **Animation Parameter** ボックスが表示されます。

## 使い方

### 共通

1. Animator ウィンドウで対象の `.controller` を開く（開いている Controller 名が表示される）
2. 各行の **Copy** で名前をコピー、**追加** で開いている Animator にパラメーターを追加
3. **すべてコピー** / **すべて追加** で一括操作
4. 既に同名がある場合は **済** と表示され追加できません

### VRC PhysBone

Parameter（接頭辞）設定後、次が並びます。

| パラメーター | 型 |
|---|---|
| `{Parameter}_IsGrabbed` | Bool |
| `{Parameter}_IsPosed` | Bool |
| `{Parameter}_Angle` | Float |
| `{Parameter}_Stretch` | Float |
| `{Parameter}_Squish` | Float |

### VRC Contact Receiver

1. **Parameter** を設定する（Avatar Descriptor の同期リストへの追加は不要）
2. SDK 3.10.6+ で Advanced Parameters を有効にしている場合、有効分だけ追加表示

| Receiver Type | 追加時の型 |
|---|---|
| Constant / OnEnter | Bool |
| Proximity | Float |

Advanced（フラグ有効時のみ）:

| パラメーター | 型 |
|---|---|
| `{Parameter}_SenderType` | Int |
| `{Parameter}_SenderLocalPositionX/Y/Z` | Float |
| `{Parameter}_SenderLossyScaleX/Y/Z` | Float |
| `{Parameter}_SenderVelocityX/Y/Z`, `_SenderVelocityMagnitude` | Float |
| `{Parameter}_PointSeparation` | Float |
| `{Parameter}_ProximitySpeed` | Float |
| `{Parameter}_CollisionTag_{tag}` | Bool |

## 注意事項

- Parameter が空のときは一覧が出ません。
- 「追加」は Animator ウィンドウで Controller を開いているときのみ有効です。
- Advanced Parameters は計算コストが高いため、必要なものだけ有効にしてください。
