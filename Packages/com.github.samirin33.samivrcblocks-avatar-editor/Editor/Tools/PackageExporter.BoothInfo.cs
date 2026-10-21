using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SamiVRCBlocksAvatar.Editor
{
    /// <summary>
    /// Assets/samirin33 配下の配布物と SamirinBoothInformation / SamirinBoothAssetInfo、PackageAssetInfo.json の読み書き。
    /// </summary>
    public static partial class PackageExporter
    {
        /// <summary>Assets/samirin33 直下またはその配下か。</summary>
        public static bool IsUnderSamirin33Folder(string assetFolderPath)
        {
            if (string.IsNullOrEmpty(assetFolderPath))
                return false;
            var normalized = assetFolderPath.Replace("\\", "/").TrimEnd('/');
            return normalized == "Assets/samirin33"
                || normalized.StartsWith("Assets/samirin33/");
        }

        public const string BoothInformationFolder = "Assets/samirin33/SamirinBoothInformation";
        public const string ItemAnalysisFileName = "ItemAnalysis.txt";
        const string BoothAssetInfoTypeName = "SamirinBoothAssetInfo";
        const string Samirin33Root = "Assets/samirin33";
        static readonly HashSet<string> ExcludedSamirin33ProductNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SamirinBoothInformation",
            "SamirinBoothManager",
            "Editor",
            "Script"
        };

        /// <summary>
        /// Assets/samirin33 配下の配布物キー（例: Assets/samirin33/DeskPen/... → DeskPen）を取得します。
        /// </summary>
        public static bool TryGetSamirin33ProductKey(string sourceAssetFolder, out string productKey)
        {
            productKey = null;
            if (string.IsNullOrEmpty(sourceAssetFolder))
                return false;

            var normalized = sourceAssetFolder.Replace("\\", "/").TrimEnd('/');
            if (normalized == Samirin33Root)
                return false;

            if (normalized.StartsWith(BoothInformationFolder + "/", StringComparison.OrdinalIgnoreCase))
            {
                var relative = normalized.Substring(BoothInformationFolder.Length).TrimStart('/');
                if (string.IsNullOrEmpty(relative))
                    return false;
                productKey = relative.Split('/')[0];
            }
            else if (normalized.StartsWith(Samirin33Root + "/", StringComparison.OrdinalIgnoreCase))
            {
                var relative = normalized.Substring(Samirin33Root.Length).TrimStart('/');
                if (string.IsNullOrEmpty(relative))
                    return false;
                productKey = relative.Split('/')[0];
            }
            else
            {
                return false;
            }

            if (string.IsNullOrEmpty(productKey) || ExcludedSamirin33ProductNames.Contains(productKey))
            {
                productKey = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// 配布フォルダに対応する SamirinBoothInformation 配下フォルダを解決します。
        /// 例: Assets/samirin33/DeskPen → Assets/samirin33/SamirinBoothInformation/DeskPen
        /// </summary>
        public static bool TryGetBoothInformationOutputFolder(
            string sourceAssetFolder,
            out string boothInfoFolderAssetPath,
            bool createIfMissing = false)
        {
            boothInfoFolderAssetPath = null;
            if (!TryGetSamirin33ProductKey(sourceAssetFolder, out var productKey))
                return false;

            boothInfoFolderAssetPath = (BoothInformationFolder + "/" + productKey).Replace("\\", "/");

            // BoothAssetInfo があれば、その実ファイルの親フォルダを優先
            var boothAsset = FindBoothAssetInfoForFolder(Samirin33Root + "/" + productKey)
                            ?? FindBoothAssetInfoForFolder(sourceAssetFolder);
            if (boothAsset != null)
            {
                var assetPath = AssetDatabase.GetAssetPath(boothAsset).Replace("\\", "/");
                var parent = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
                if (!string.IsNullOrEmpty(parent) &&
                    parent.StartsWith(BoothInformationFolder + "/", StringComparison.OrdinalIgnoreCase))
                {
                    boothInfoFolderAssetPath = parent;
                }
            }

            if (createIfMissing)
                AssetFolderUtility.EnsureAssetFolder(boothInfoFolderAssetPath);

            return createIfMissing || AssetDatabase.IsValidFolder(boothInfoFolderAssetPath);
        }

        /// <summary>
        /// SamirinBoothInformation 配下フォルダにテキストファイルを書き出します。
        /// </summary>
        public static string WriteTextToBoothInformationFolder(
            string sourceAssetFolder,
            string fileName,
            string contents)
        {
            if (string.IsNullOrEmpty(fileName))
                throw new ArgumentException("fileName is required.", nameof(fileName));

            if (!TryGetBoothInformationOutputFolder(sourceAssetFolder, out var assetFolder, createIfMissing: true))
            {
                Debug.LogWarning("[PackageExporter] Booth Information 出力先を解決できません: " + sourceAssetFolder);
                return null;
            }

            var fullFolder = AssetPathToFullPath(assetFolder);
            if (string.IsNullOrEmpty(fullFolder))
                return null;

            var fullPath = Path.Combine(fullFolder, fileName).Replace("\\", "/");
            try
            {
                if (!Directory.Exists(fullFolder))
                    Directory.CreateDirectory(fullFolder);
                File.WriteAllText(fullPath, contents ?? "", new System.Text.UTF8Encoding(true));
            }
            catch (Exception e)
            {
                Debug.LogError("[PackageExporter] Booth Information への書き出しに失敗: " + fullPath + " / " + e.Message);
                return null;
            }

            AssetDatabase.Refresh();
            return fullPath;
        }

        /// <summary>
        /// Assembly-CSharp 上の SamirinBoothAssetInfo 型を取得（パッケージから直接参照できないため）。
        /// </summary>
        public static Type GetBoothAssetInfoType()
        {
            return ReflectionUtil.FindType(BoothAssetInfoTypeName);
        }

        public static bool IsBoothAssetInfo(UnityEngine.Object obj)
        {
            return obj != null && obj.GetType().Name == BoothAssetInfoTypeName;
        }

        /// <summary>
        /// 配布フォルダに対応する SamirinBoothAssetInfo を探す（folderName / フォルダ名 / name）。
        /// </summary>
        public static UnityEngine.Object FindBoothAssetInfoForFolder(string sourceAssetFolder)
        {
            if (string.IsNullOrEmpty(sourceAssetFolder))
                return null;

            var folderKey = Path.GetFileName(sourceAssetFolder.Replace("\\", "/").TrimEnd('/'));
            if (string.IsNullOrEmpty(folderKey))
                return null;

            if (!AssetDatabase.IsValidFolder(BoothInformationFolder))
                return null;

            var guids = AssetDatabase.FindAssets("t:" + BoothAssetInfoTypeName, new[] { BoothInformationFolder });
            UnityEngine.Object fallbackByName = null;

            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]).Replace('\\', '/');
                if (path.EndsWith("/Manger.asset", StringComparison.OrdinalIgnoreCase))
                    continue;

                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (!IsBoothAssetInfo(asset))
                    continue;

                var so = new SerializedObject(asset);
                var folderName = so.FindProperty("folderName")?.stringValue ?? "";
                var displayName = so.FindProperty("name")?.stringValue ?? "";

                if (!string.IsNullOrEmpty(folderName) &&
                    string.Equals(folderName, folderKey, StringComparison.OrdinalIgnoreCase))
                    return asset;

                if (fallbackByName == null &&
                    string.Equals(displayName, folderKey, StringComparison.OrdinalIgnoreCase))
                    fallbackByName = asset;
            }

            return fallbackByName;
        }

        /// <summary>
        /// SamirinBoothAssetInfo から PackageAssetInfo の基本情報・関連URL・更新履歴を反映する。
        /// </summary>
        /// <returns>反映に成功したら true</returns>
        public static bool TryApplyFromBoothAssetInfo(
            UnityEngine.Object boothAsset,
            PackageAssetInfo target,
            out string packageName,
            out string version)
        {
            packageName = null;
            version = null;
            if (target == null || !IsBoothAssetInfo(boothAsset))
                return false;

            var so = new SerializedObject(boothAsset);
            var folderName = so.FindProperty("folderName")?.stringValue?.Trim() ?? "";
            var displayName = so.FindProperty("name")?.stringValue?.Trim() ?? "";
            var description = so.FindProperty("description")?.stringValue ?? "";
            var major = so.FindProperty("majorVertion")?.intValue ?? 0;
            var minor = so.FindProperty("minorVertion")?.intValue ?? 0;
            var patch = so.FindProperty("patchVertion")?.intValue ?? 0;
            var boothUrl = so.FindProperty("url")?.stringValue?.Trim() ?? "";
            var youtubeUrl = so.FindProperty("youtubeUrl")?.stringValue?.Trim() ?? "";

            packageName = !string.IsNullOrEmpty(folderName) ? folderName : displayName;
            version = $"{major}.{minor}.{patch}";

            if (!string.IsNullOrEmpty(packageName))
                target.name = packageName;
            target.version = version;
            target.author = "samirin33";
            target.description = description;

            var urls = new List<PackageAssetInfo.UrlInfo>();
            if (!string.IsNullOrEmpty(boothUrl))
            {
                urls.Add(new PackageAssetInfo.UrlInfo
                {
                    urlDescription = "Booth",
                    url = boothUrl
                });
            }
            if (!string.IsNullOrEmpty(youtubeUrl))
            {
                urls.Add(new PackageAssetInfo.UrlInfo
                {
                    urlDescription = "YouTube",
                    url = youtubeUrl
                });
            }
            target.urls = urls.ToArray();
            target.releases = BuildReleasesFromUpdateInfos(so.FindProperty("updateInfos"));

            return true;
        }

        /// <summary>
        /// updateInfos（古い→新しい順想定）を releases（最新が先頭）へ変換する。
        /// </summary>
        static PackageAssetInfo.ReleaseInfo[] BuildReleasesFromUpdateInfos(SerializedProperty updateInfosProp)
        {
            var list = new List<PackageAssetInfo.ReleaseInfo>();
            if (updateInfosProp == null || !updateInfosProp.isArray)
                return list.ToArray();

            for (var i = 0; i < updateInfosProp.arraySize; i++)
            {
                var item = updateInfosProp.GetArrayElementAtIndex(i);
                var updateName = item.FindPropertyRelative("updateName")?.stringValue?.Trim() ?? "";
                var updateDescription = item.FindPropertyRelative("updateDescription")?.stringValue?.Trim() ?? "";
                var dateProp = item.FindPropertyRelative("updateDate");
                var year = dateProp?.FindPropertyRelative("year")?.intValue ?? 0;
                var month = dateProp?.FindPropertyRelative("month")?.intValue ?? 0;
                var day = dateProp?.FindPropertyRelative("day")?.intValue ?? 0;

                var notes = string.IsNullOrEmpty(updateDescription)
                    ? new string[0]
                    : new[] { updateDescription };

                list.Add(new PackageAssetInfo.ReleaseInfo
                {
                    version = string.IsNullOrEmpty(updateName) ? null : updateName,
                    releaseDate = FormatBoothDate(year, month, day),
                    releaseNotes = notes
                });
            }

            // 最新を先頭に
            list.Reverse();
            return list.ToArray();
        }

        static string FormatBoothDate(int year, int month, int day)
        {
            if (year <= 0 || month <= 0 || day <= 0)
                return null;
            return $"{year}/{month}/{day}";
        }

        /// <summary>
        /// 指定フォルダ直下の PackageAssetInfo.json を読み込む。存在しない場合は null。
        /// </summary>
        public static PackageAssetInfo LoadAssetInfo(string assetFolderPath)
        {
            if (string.IsNullOrEmpty(assetFolderPath)) return null;
            var fullPath = AssetPathToFullPath(Path.Combine(assetFolderPath, AssetInfoFileName).Replace("\\", "/"));
            if (fullPath == null || !File.Exists(fullPath)) return null;
            var path = fullPath;
            try
            {
                var json = File.ReadAllText(path);
                return JsonUtility.FromJson<PackageAssetInfo>(json);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PackageExporter] Failed to load PackageAssetInfo: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 指定フォルダ直下に PackageAssetInfo.json を書き込む。
        /// </summary>
        public static void SaveAssetInfo(string assetFolderPath, PackageAssetInfo info)
        {
            if (string.IsNullOrEmpty(assetFolderPath) || info == null) return;
            var path = AssetPathToFullPath(Path.Combine(assetFolderPath, AssetInfoFileName).Replace("\\", "/"));
            if (path == null) return;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = JsonUtility.ToJson(info, true);
            File.WriteAllText(path, json);
            AssetDatabase.Refresh();
        }
    }
}
