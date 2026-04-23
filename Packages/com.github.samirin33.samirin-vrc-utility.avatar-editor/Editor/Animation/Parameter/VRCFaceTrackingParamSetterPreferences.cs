namespace Samirin33.AvatarEditor.Animation.Editor
{
    public static class VRCFaceTrackingParamSetterPreferences
    {
        static readonly AvatarParamFavoriteStore Store = new AvatarParamFavoriteStore("FaceTracking");
        public static bool IsFavorite(string parameterName) => Store.IsFavorite(parameterName);
        public static void SetFavorite(string parameterName, bool favorite) => Store.SetFavorite(parameterName, favorite);
        public static void ClearCache() => Store.ClearCache();
    }
}
