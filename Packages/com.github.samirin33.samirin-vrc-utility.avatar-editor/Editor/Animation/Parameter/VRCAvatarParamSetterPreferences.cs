namespace Samirin33.AvatarEditor.Animation.Editor
{
    public static class VRCAvatarParamSetterPreferences
    {
        static readonly AvatarParamFavoriteStore Store = new AvatarParamFavoriteStore("Builtin");
        public static bool IsFavorite(string parameterName) => Store.IsFavorite(parameterName);
        public static void SetFavorite(string parameterName, bool favorite) => Store.SetFavorite(parameterName, favorite);
        public static void ClearCache() => Store.ClearCache();
    }
}
