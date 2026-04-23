using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    public class AvatarParamFavoriteStore
    {
        readonly string _prefsKey;
        HashSet<string> _favorites;

        [Serializable]
        class Wrapper { public List<string> names = new List<string>(); }

        public AvatarParamFavoriteStore(string profileKey)
        {
            _prefsKey = $"VRCAvatarParamSetter_Favorites_{profileKey}_{Application.dataPath}";
        }

        HashSet<string> GetFavoriteSet()
        {
            if (_favorites != null)
                return _favorites;

            _favorites = new HashSet<string>();
            try
            {
                string json = EditorPrefs.GetString(_prefsKey, "[]");
                var list = JsonUtility.FromJson<Wrapper>(json);
                if (list?.names != null)
                {
                    foreach (var n in list.names)
                        _favorites.Add(n);
                }
            }
            catch
            {
                _favorites = new HashSet<string>();
            }

            return _favorites;
        }

        void Save()
        {
            var set = GetFavoriteSet();
            var w = new Wrapper { names = new List<string>(set) };
            EditorPrefs.SetString(_prefsKey, JsonUtility.ToJson(w));
        }

        public bool IsFavorite(string parameterName) => GetFavoriteSet().Contains(parameterName);

        public void SetFavorite(string parameterName, bool favorite)
        {
            var set = GetFavoriteSet();
            if (favorite)
                set.Add(parameterName);
            else
                set.Remove(parameterName);
            Save();
        }

        public void ClearCache() => _favorites = null;
    }
}
