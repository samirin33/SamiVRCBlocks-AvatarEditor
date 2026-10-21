using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SamiVRCBlocksAvatar.Editor
{
    /// <summary>
    /// エクスポート用に Assets へ一時配置するファイルの .meta を、配置先パスから決まる固定 GUID で書き出す。
    /// Packages 側の原本と GUID が重なると導入先で GUID 衝突になり、毎回ランダムだと
    /// 複数商品を導入したときに同じパスへ別 GUID が来て「SuperReImporter 1.cs」のような重複ができる。
    /// </summary>
    internal static class StagedAssetMeta
    {
        const string GuidSalt = "samirin33.staged-export/";

        static readonly Regex GuidLineRegex = new Regex(@"^guid:\s*[0-9a-fA-F]{32}\s*$", RegexOptions.Multiline);

        /// <summary>配置先 Assets パスから固定 GUID を作る。大文字小文字の違いでは変わらない。</summary>
        public static string GuidFor(string stagedAssetPath)
        {
            var key = GuidSalt + (stagedAssetPath ?? "").Replace("\\", "/").TrimEnd('/').ToLowerInvariant();
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
                var sb = new StringBuilder(32);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        public static void WriteFolderMeta(string folderFullPath, string stagedAssetPath)
        {
            var text =
                "fileFormatVersion: 2\n" +
                "guid: " + GuidFor(stagedAssetPath) + "\n" +
                "folderAsset: yes\n" +
                "DefaultImporter:\n" +
                "  externalObjects: {}\n" +
                "  userData: \n" +
                "  assetBundleName: \n" +
                "  assetBundleVariant: \n";
            File.WriteAllText(folderFullPath.TrimEnd('/', '\\') + ".meta", text);
        }

        /// <summary>
        /// sourceMetaFullPath があれば importer 設定はそのまま GUID だけ差し替え、無ければ拡張子に応じた既定 importer で書く。
        /// </summary>
        public static void WriteFileMeta(string fileFullPath, string stagedAssetPath, string sourceMetaFullPath = null)
        {
            var guid = GuidFor(stagedAssetPath);
            string text = null;
            if (!string.IsNullOrEmpty(sourceMetaFullPath) && File.Exists(sourceMetaFullPath))
            {
                var source = File.ReadAllText(sourceMetaFullPath);
                if (GuidLineRegex.IsMatch(source))
                    text = GuidLineRegex.Replace(source, "guid: " + guid, 1);
            }

            if (text == null)
                text = "fileFormatVersion: 2\nguid: " + guid + "\n" + DefaultImporterBlock(Path.GetExtension(fileFullPath));

            File.WriteAllText(fileFullPath + ".meta", text);
        }

        static string DefaultImporterBlock(string extension)
        {
            var ext = (extension ?? "").ToLowerInvariant();
            if (ext == ".cs")
            {
                return
                    "MonoImporter:\n" +
                    "  externalObjects: {}\n" +
                    "  serializedVersion: 2\n" +
                    "  defaultReferences: []\n" +
                    "  executionOrder: 0\n" +
                    "  icon: {instanceID: 0}\n" +
                    "  userData: \n" +
                    "  assetBundleName: \n" +
                    "  assetBundleVariant: \n";
            }

            string importer;
            switch (ext)
            {
                case ".asmdef":
                    importer = "AssemblyDefinitionImporter";
                    break;
                case ".json":
                case ".txt":
                    importer = "TextScriptImporter";
                    break;
                default:
                    importer = "DefaultImporter";
                    break;
            }

            return
                importer + ":\n" +
                "  externalObjects: {}\n" +
                "  userData: \n" +
                "  assetBundleName: \n" +
                "  assetBundleVariant: \n";
        }

        /// <summary>
        /// sourceDir 以下を destDir へコピーし、各ファイル・フォルダに固定 GUID の .meta を書く。
        /// skipFile が true を返すファイル（.meta は常に除外）とその .meta はコピーしない。
        /// </summary>
        public static void CopyTreeWithStableMeta(
            string sourceDir,
            string destDir,
            string destAssetPath,
            Func<string, bool> skipFile,
            Func<string, bool> skipDirectory = null)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var name = Path.GetFileName(file);
                if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (skipFile != null && skipFile(name))
                    continue;

                var destFile = Path.Combine(destDir, name);
                File.Copy(file, destFile, true);
                WriteFileMeta(destFile, destAssetPath + "/" + name, file + ".meta");
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var name = Path.GetFileName(dir);
                if (skipDirectory != null && skipDirectory(name))
                    continue;

                var destSub = Path.Combine(destDir, name);
                var destSubAssetPath = destAssetPath + "/" + name;
                CopyTreeWithStableMeta(dir, destSub, destSubAssetPath, skipFile, skipDirectory);
                WriteFolderMeta(destSub, destSubAssetPath);
            }
        }
    }
}
