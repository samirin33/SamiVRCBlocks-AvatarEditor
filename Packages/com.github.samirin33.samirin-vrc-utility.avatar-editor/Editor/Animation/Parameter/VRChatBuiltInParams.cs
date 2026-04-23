using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    public static class VRChatBuiltInParams
    {
        public static IReadOnlyList<AvatarParamDef> All { get; } = new List<AvatarParamDef>
        {
            new AvatarParamDef("IsLocal", AnimatorControllerParameterType.Bool, false,
                "このアバターを自分が着用している場合 true、他人のアバターとして表示されている場合は false。"),
            new AvatarParamDef("PreviewMode", AnimatorControllerParameterType.Int, true,
                "エディタプレビュー用。本番では使用しないため通常は除外。"),
            new AvatarParamDef("Viseme", AnimatorControllerParameterType.Int, false,
                "リップシンク用。Oculus viseme インデックス 0–14。Jawbone/Jawflap 使用時は 0–100 で音量を表す。"),
            new AvatarParamDef("Voice", AnimatorControllerParameterType.Float, false,
                "マイク音量。0.0～1.0 の範囲。"),
            new AvatarParamDef("GestureLeft", AnimatorControllerParameterType.Int, false,
                "左手ジェスチャー。0=Neutral, 1=Fist, 2=HandOpen, 3=FingerPoint, 4=Victory, 5=RockNRoll, 6=HandGun, 7=ThumbsUp。"),
            new AvatarParamDef("GestureRight", AnimatorControllerParameterType.Int, false,
                "右手ジェスチャー。0=Neutral, 1=Fist, 2=HandOpen, 3=FingerPoint, 4=Victory, 5=RockNRoll, 6=HandGun, 7=ThumbsUp。"),
            new AvatarParamDef("GestureLeftWeight", AnimatorControllerParameterType.Float, false,
                "左手アナログトリガーの押し具合。0.0～1.0。トリガーを引くほど増加し、アナログジェスチャーに利用可能。"),
            new AvatarParamDef("GestureRightWeight", AnimatorControllerParameterType.Float, false,
                "右手アナログトリガーの押し具合。0.0～1.0。トリガーを引くほど増加。"),
            new AvatarParamDef("AngularY", AnimatorControllerParameterType.Float, true,
                "Y軸まわりの角速度。回転の速さに応じて変化。"),
            new AvatarParamDef("VelocityX", AnimatorControllerParameterType.Float, false,
                "左右方向の移動速度 (m/s)。", "m/s (実速度依存)"),
            new AvatarParamDef("VelocityY", AnimatorControllerParameterType.Float, false,
                "上下方向の移動速度 (m/s)。", "m/s (実速度依存)"),
            new AvatarParamDef("VelocityZ", AnimatorControllerParameterType.Float, false,
                "前後方向の移動速度 (m/s)。", "m/s (実速度依存)"),
            new AvatarParamDef("VelocityMagnitude", AnimatorControllerParameterType.Float, false,
                "移動速度の合計の大きさ（スカラー）。", "m/s (実速度依存)"),
            new AvatarParamDef("Upright", AnimatorControllerParameterType.Float, false,
                "直立度。0=うつ伏せに近い、1=まっすぐ立っている。"),
            new AvatarParamDef("Grounded", AnimatorControllerParameterType.Bool, false,
                "地面（または足場）に接触している場合 true。"),
            new AvatarParamDef("Seated", AnimatorControllerParameterType.Bool, true,
                "ステーションに座っている場合 true。"),
            new AvatarParamDef("AFK", AnimatorControllerParameterType.Bool, true,
                "離席中の場合 true。Endキー、HMDを外した時、一部のシステムメニュー表示時に true。"),
            new AvatarParamDef("TrackingType", AnimatorControllerParameterType.Int, true,
                "トラッキング種別。0=未初期化, 1=Generic, 2=ハンドのみ(遷移中), 3=3点(頭+手), 4=4点(+腰), 6=フルボディ。VRMode が 1 のとき 3/4/6 が有効。"),
            new AvatarParamDef("VRMode", AnimatorControllerParameterType.Int, false,
                "VR 利用時は 1、デスクトップ（非VR）時は 0。"),
            new AvatarParamDef("MuteSelf", AnimatorControllerParameterType.Bool, true,
                "自分をミュートしている場合 true。"),
            new AvatarParamDef("InStation", AnimatorControllerParameterType.Bool, true,
                "ステーション内にいる場合 true。"),
            new AvatarParamDef("Earmuffs", AnimatorControllerParameterType.Bool, true,
                "イヤーマフ機能がオンの場合 true（他人の声を減衰）。"),
            new AvatarParamDef("IsOnFriendsList", AnimatorControllerParameterType.Bool, true,
                "このユーザーが自分のフレンドリストに含まれる場合 true。"),
            new AvatarParamDef("AvatarVersion", AnimatorControllerParameterType.Int, true,
                "アバターのバージョン番号。同一アバターのバージョン判別に利用。", "整数 (Avatar Descriptor 設定値)"),
            new AvatarParamDef("IsAnimatorEnabled", AnimatorControllerParameterType.Bool, true,
                "アニメーターが有効になっている場合 true。"),
            new AvatarParamDef("ScaleModified", AnimatorControllerParameterType.Bool, true,
                "アバターのスケールが変更されている場合 true。"),
            new AvatarParamDef("ScaleFactor", AnimatorControllerParameterType.Float, true,
                "現在のスケール係数。デフォルトアバターサイズに対する倍率。", "倍率 (>0.0)"),
            new AvatarParamDef("ScaleFactorInverse", AnimatorControllerParameterType.Float, true,
                "スケール係数の逆数。計算用。", "倍率 (>0.0)"),
            new AvatarParamDef("EyeHeightAsMeters", AnimatorControllerParameterType.Float, true,
                "目の高さをメートルで表した値。", "m (>0.0)"),
            new AvatarParamDef("EyeHeightAsPercent", AnimatorControllerParameterType.Float, true,
                "目の高さをパーセント（0～1）で表した値。"),
        };
    }
}
