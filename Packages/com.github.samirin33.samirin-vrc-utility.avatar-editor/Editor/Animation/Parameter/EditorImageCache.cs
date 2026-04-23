using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    public static class EditorImageCache
    {
        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        static readonly HashSet<string> Loading = new HashSet<string>();

        public static Texture2D GetOrRequest(string url, EditorWindow window)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            if (Cache.TryGetValue(url, out var cached))
                return cached;

            if (Loading.Contains(url))
                return null;

            Loading.Add(url);
            var req = UnityWebRequestTexture.GetTexture(url);
            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                Loading.Remove(url);
                if (req.result == UnityWebRequest.Result.Success)
                {
                    Cache[url] = DownloadHandlerTexture.GetContent(req);
                }
                req.Dispose();
                window?.Repaint();
            };

            return null;
        }
    }
}
