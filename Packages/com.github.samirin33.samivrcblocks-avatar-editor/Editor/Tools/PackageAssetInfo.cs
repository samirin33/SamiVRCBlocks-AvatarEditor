using System;
using UnityEngine;

[Serializable]
public class PackageAssetInfo
{
    public string name;
    public string version;
    public string author;
    public string description;

    public UrlInfo[] urls;
    public ReleaseInfo[] releases;

    /// <summary>
    /// 配布フォルダに加えて同梱する関連フォルダ（Assets/ からのパス）。
    /// </summary>
    public string[] relatedFolders;

    [Serializable]
    public class UrlInfo
    {
        public string urlDescription;
        public string url;
    }

    [Serializable]
    public class ReleaseInfo
    {
        public string version;
        public string releaseDate;
        public string[] releaseNotes;
    }
}
