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

    /// <summary>
    /// インポート時に一度削除してから再インポートするフォルダ。未指定のフォルダは削除しない。
    /// </summary>
    public string[] superReimportFolders;

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
