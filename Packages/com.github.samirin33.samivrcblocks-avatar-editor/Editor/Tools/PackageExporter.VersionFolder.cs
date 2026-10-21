using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace SamiVRCBlocksAvatar.Editor
{
    /// <summary>
    /// 「商品名_verx.x.x」バージョンフォルダへの出力と、前バージョンの付属ファイル引き継ぎ・zip 化。
    /// </summary>
    public static partial class PackageExporter
    {
        /// <summary>
        /// 同じ出力先にある一つ前のバージョンフォルダから、UnityPackage 以外を新しいフォルダへコピーする。
        /// コピー先に同名のファイルが既にある場合は上書きしない。
        /// </summary>
        static void CopyExtrasFromPreviousVersionFolder(
            string parentDirectory,
            string packageName,
            string version,
            string destinationFolder)
        {
            var previous = FindPreviousVersionFolder(parentDirectory, packageName, version);
            if (string.IsNullOrEmpty(previous))
            {
                Debug.Log("[PackageExporter] 一つ前のバージョンフォルダはありません。");
                return;
            }

            Debug.Log("[PackageExporter] 一つ前のバージョンフォルダ: " + previous);
            var copied = 0;
            try
            {
                foreach (var entry in Directory.GetFileSystemEntries(previous))
                {
                    var name = Path.GetFileName(entry);
                    if (string.IsNullOrEmpty(name))
                        continue;
                    if (File.Exists(entry) &&
                        name.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var dest = Path.Combine(destinationFolder, name);
                    if (Directory.Exists(entry))
                    {
                        copied += CopyDirectoryWithoutOverwrite(entry, dest);
                        continue;
                    }

                    if (!File.Exists(entry) || File.Exists(dest))
                        continue;
                    var destDir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);
                    File.Copy(entry, dest, false);
                    copied++;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PackageExporter] 前バージョンの付属ファイルのコピーに失敗: " + ex.Message);
                return;
            }

            if (copied == 0)
                Debug.Log("[PackageExporter] 前バージョンから新しくコピーした付属ファイルはありません。既存のファイルは残しています。");
            else
                Debug.Log("[PackageExporter] 前バージョンから付属ファイルをコピーしました: " + copied);
        }

        static string FindPreviousVersionFolder(string parentDirectory, string packageName, string version)
        {
            if (string.IsNullOrEmpty(parentDirectory) || !Directory.Exists(parentDirectory))
                return null;
            if (!TryParseVersion(version, out var currentMajor, out var currentMinor, out var currentPatch))
                return null;

            string bestPath = null;
            var bestMajor = -1;
            var bestMinor = -1;
            var bestPatch = -1;
            foreach (var dir in Directory.GetDirectories(parentDirectory))
            {
                var folderName = Path.GetFileName(dir);
                if (!TryParsePackageVersionFolder(folderName, packageName, out var major, out var minor, out var patch))
                    continue;
                if (!IsOlderVersion(major, minor, patch, currentMajor, currentMinor, currentPatch))
                    continue;
                if (bestPath != null && !IsOlderVersion(bestMajor, bestMinor, bestPatch, major, minor, patch))
                    continue;

                bestPath = dir;
                bestMajor = major;
                bestMinor = minor;
                bestPatch = patch;
            }

            return bestPath;
        }

        static bool TryParsePackageVersionFolder(
            string folderName,
            string packageName,
            out int major,
            out int minor,
            out int patch)
        {
            major = minor = patch = 0;
            if (string.IsNullOrEmpty(folderName) || string.IsNullOrEmpty(packageName))
                return false;

            var prefix = packageName + "_ver";
            if (!folderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
            return TryParseVersion(folderName.Substring(prefix.Length), out major, out minor, out patch);
        }

        static bool TryParseVersion(string version, out int major, out int minor, out int patch)
        {
            major = minor = patch = 0;
            if (string.IsNullOrEmpty(version))
                return false;
            var parts = version.Split('.');
            if (parts.Length != 3)
                return false;
            return int.TryParse(parts[0], out major)
                && int.TryParse(parts[1], out minor)
                && int.TryParse(parts[2], out patch)
                && major >= 0 && minor >= 0 && patch >= 0;
        }

        static bool IsOlderVersion(int major, int minor, int patch, int otherMajor, int otherMinor, int otherPatch)
        {
            if (major != otherMajor)
                return major < otherMajor;
            if (minor != otherMinor)
                return minor < otherMinor;
            return patch < otherPatch;
        }

        static int CopyDirectoryWithoutOverwrite(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);
            var copied = 0;
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var dest = Path.Combine(destinationDir, Path.GetFileName(file));
                if (File.Exists(dest))
                    continue;
                File.Copy(file, dest, false);
                copied++;
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var dest = Path.Combine(destinationDir, Path.GetFileName(dir));
                copied += CopyDirectoryWithoutOverwrite(dir, dest);
            }

            return copied;
        }

        static void TryCreateDirectoryZip(string sourceDirectory, string zipPath, bool overwrite)
        {
            try
            {
                if (File.Exists(zipPath))
                {
                    if (!overwrite)
                    {
                        Debug.LogWarning("[PackageExporter] zip が既にあるためスキップします: " + zipPath);
                        return;
                    }
                    File.Delete(zipPath);
                }

                ZipFile.CreateFromDirectory(
                    sourceDirectory,
                    zipPath,
                    System.IO.Compression.CompressionLevel.Optimal,
                    true);
                Debug.Log("[PackageExporter] zip を生成しました: " + zipPath);
            }
            catch (Exception ex)
            {
                Debug.LogError("[PackageExporter] zip の生成に失敗: " + ex.Message);
            }
        }
    }
}
