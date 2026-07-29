using UnityEditor;
using UnityEngine;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    public static class AvatarParamRangeResolver
    {
        public static string GetRangeText(AvatarParamDef param)
        {
            if (!string.IsNullOrEmpty(param.Range))
                return param.Range;

            string name = param.Name;

            switch (name)
            {
                case "IsLocal":
                case "Grounded":
                case "Seated":
                case "AFK":
                case "MuteSelf":
                case "InStation":
                case "Earmuffs":
                case "IsOnFriendsList":
                case "IsAnimatorEnabled":
                case "ScaleModified":
                case "EyeTrackingActive":
                case "ExpressionTrackingActive":
                case "LipTrackingActive":
                    return "true / false";
                case "PreviewMode":
                    return "0 / 1";
                case "Viseme":
                    return "0-14 (Oculus viseme) / 0-100 (Jawbone/Jawflap)";
                case "GestureLeft":
                case "GestureRight":
                    return "0 ~ 7";
                case "GestureLeftWeight":
                case "GestureRightWeight":
                case "Voice":
                case "Upright":
                case "EyeHeightAsPercent":
                    return "0.0 ~ 1.0";
                case "VRMode":
                    return "0 / 1";
                case "TrackingType":
                    return "0, 1, 2, 3, 4, 6";
            }

            if (name.StartsWith("v2/"))
            {
                if (name == "v2/EyeLid" || name == "v2/EyeLidLeft" || name == "v2/EyeLidRight")
                    return "0.0 ~ 1.0 (0.0 ~ 0.75=Open, 0.75 ~ 1.0=Widen)";

                if (name == "v2/EyeLeftX" || name == "v2/EyeLeftY" || name == "v2/EyeRightX" || name == "v2/EyeRightY"
                    || name == "v2/EyeX" || name == "v2/EyeY" || name == "v2/JawX" || name == "v2/JawZ"
                    || name == "v2/MouthUpperX" || name == "v2/MouthLowerX" || name == "v2/MouthX"
                    || name == "v2/TongueX" || name == "v2/TongueY" || name == "v2/TongueArchY"
                    || name == "v2/TongueShape" || name == "v2/CheekPuffSuckRight" || name == "v2/CheekPuffSuckLeft"
                    || name == "v2/CheekPuffSuck" || name == "v2/BrowExpressionRight" || name == "v2/BrowExpressionLeft"
                    || name == "v2/BrowExpression" || name == "v2/SmileFrownRight" || name == "v2/SmileFrownLeft"
                    || name == "v2/SmileFrown" || name == "v2/SmileSadRight" || name == "v2/SmileSadLeft"
                    || name == "v2/SmileSad")
                    return "-1.0 ~ 1.0";

                return "0.0 ~ 1.0";
            }

            if (param.Type == AnimatorControllerParameterType.Bool)
                return "true / false";
            if (param.Type == AnimatorControllerParameterType.Float)
                return "仕様依存 (通常 0.0 ~ 1.0)";
            if (param.Type == AnimatorControllerParameterType.Int)
                return "仕様依存";

            return "-";
        }
    }
}
