namespace Samirin33.AvatarEditor.Animation.Editor
{
    public static class VRCFaceTrackingCategoryResolver
    {
        public static string GetHeader(string parameterName)
        {
            switch (parameterName)
            {
                case "v2/EyeLeftX":
                    return "Eye Gaze Parameters";
                case "v2/EyeLidRight":
                    return "Eye Expression Parameters";
                case "v2/BrowPinchRight":
                    return "Brow Parameters";
                case "v2/NoseSneerRight":
                    return "Nose Parameters";
                case "v2/CheekSquintRight":
                    return "Cheek Parameters";
                case "v2/JawOpen":
                    return "Jaw Parameters";
                case "v2/LipSuckUpperRight":
                    return "Lip Parameters";
                case "v2/MouthUpperUpRight":
                    return "Mouth Parameters";
                case "v2/TongueOut":
                    return "Tongue Parameters";
                case "v2/SoftPalateClose":
                    return "Neck Parameters";
                case "v2/EyeX":
                    return "Simplified Eye Parameters";
                case "v2/BrowDownRight":
                    return "Simplified Brow Parameters";
                case "v2/MouthX":
                    return "Simplified Mouth Parameters";
                case "v2/LipSuckUpper":
                    return "Simplified Lip Parameters";
                case "v2/NoseSneer":
                    return "Simplified Nose and Cheek Parameters";
                case "EyeTrackingActive":
                    return "Tracking Active Parameters";
                default:
                    return null;
            }
        }
    }
}
