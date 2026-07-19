#if UNITY_EDITOR
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// .unitypackage 等で Assets に展開されたとき、GitHub から SamirinBoothManager を取得して自己削除する。
/// Packages 配下（開発用）では動作しない。
/// </summary>
public static class SamirinBoothManagerInstaller
{
    const string LogPrefix = "[SamirinBoothManagerInstaller]";
    const string ScriptFileName = "SamirinBoothManagerInstaller.cs";
    const string InstallerFolderName = "SamirinBoothManagerInstaller";

    const string RepoOwner = "samirin33";
    const string RepoName = "SamirinBoothManager";
    const string Branch = "main";
    const string ZipUrl =
        "https://github.com/" + RepoOwner + "/" + RepoName + "/archive/refs/heads/" + Branch + ".zip";

    const string ManagerAssetPath = "Assets/samirin33/SamirinBoothManager";
    const string InformationAssetPath = "Assets/samirin33/SamirinBoothInformation";

    /// <summary>InformationChecker と同じキー。インストール直後の二重ダウンロードを抑止する。</summary>
    const string InformationCheckerSessionCheckedKey = "samirin33.InformationChecker.SessionChecked";

    /// <summary>SBM_UIMain.PrefsOpenAfterInstallKey と同値（Manager 未導入時でも参照できるようリテラル）。</summary>
    const string PrefsOpenAfterInstallKey = "samirin33.SamirinBoothManagerInstaller.OpenWindow";

    static string PendingDeletePrefsKey =>
        "SamirinBoothManagerInstaller.PendingDelete." + Application.dataPath;

    static string InstallLockSessionKey =>
        "SamirinBoothManagerInstaller.InstallLock." + Application.dataPath;

    static bool _isRunning;

    [InitializeOnLoadMethod]
    static void OnEditorLoad()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (IsPendingDelete())
            {
                TryDeleteInstaller();
                // 削除後もウィンドウ起動予約があれば試す（Packages 側スクリプトが残る場合）
                TryOpenItemCenterAfterInstall(8);
                return;
            }

            // インストール完了後のウィンドウ起動（Assets インストーラ削除後でも Packages 側から実行可）
            if (EditorPrefs.GetBool(PrefsOpenAfterInstallKey, false))
            {
                TryOpenItemCenterAfterInstall(8);
                return;
            }

            TryStartInstallFromAssets();
        };
    }

    /// <summary>
    /// メニューが登録されるまで待ってから Item Center を開く。
    /// </summary>
    static void TryOpenItemCenterAfterInstall(int retriesLeft)
    {
        if (!EditorPrefs.GetBool(PrefsOpenAfterInstallKey, false))
            return;

        EditorApplication.delayCall += () =>
        {
            if (!EditorPrefs.GetBool(PrefsOpenAfterInstallKey, false))
                return;

            if (EditorApplication.ExecuteMenuItem("samirin33/Samirin's Item Center"))
            {
                EditorPrefs.DeleteKey(PrefsOpenAfterInstallKey);
                Log("Samirin's Item Center を開きました。");
                return;
            }

            if (retriesLeft > 0)
                TryOpenItemCenterAfterInstall(retriesLeft - 1);
            else
                LogWarning("Samirin's Item Center メニューが見つかりませんでした。後でメニューから開いてください。");
        };
    }

    static void Log(string message) => Debug.Log($"{LogPrefix} {message}");
    static void LogWarning(string message) => Debug.LogWarning($"{LogPrefix} {message}");
    static void LogError(string message) => Debug.LogError($"{LogPrefix} {message}");

    static bool IsPendingDelete() => EditorPrefs.GetBool(PendingDeletePrefsKey, false);

    static void SetPendingDelete(bool pending)
    {
        if (pending)
            EditorPrefs.SetBool(PendingDeletePrefsKey, true);
        else
            EditorPrefs.DeleteKey(PendingDeletePrefsKey);
    }

    static bool IsInstallLocked() =>
        _isRunning || SessionState.GetBool(InstallLockSessionKey, false) || IsPendingDelete();

    static void SetInstallLock(bool locked)
    {
        SessionState.SetBool(InstallLockSessionKey, locked);
    }

    /// <summary>
    /// Assets 上の本スクリプトのパスを返す。Packages のみの場合は false。
    /// </summary>
    static bool TryGetAssetsInstallerScriptPath(out string scriptAssetPath)
    {
        scriptAssetPath = null;
        var guids = AssetDatabase.FindAssets("SamirinBoothManagerInstaller t:MonoScript");
        for (int i = 0; i < guids.Length; i++)
        {
            var path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrEmpty(path))
                continue;
            if (!path.EndsWith(ScriptFileName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                continue;

            scriptAssetPath = path.Replace("\\", "/");
            return true;
        }

        return false;
    }

    static void TryStartInstallFromAssets()
    {
        if (IsInstallLocked())
        {
            Log("インストール処理は既に実行中／予約済みのためスキップします。");
            return;
        }

        if (!TryGetAssetsInstallerScriptPath(out var scriptPath))
            return;

        Log($"Assets 上のインストーラを検出: {scriptPath}");
        _ = RunInstallAndSelfDeleteAsync(scriptPath);
    }

    static async Task RunInstallAndSelfDeleteAsync(string scriptAssetPath)
    {
        if (IsInstallLocked())
            return;

        _isRunning = true;
        SetInstallLock(true);

        try
        {
            // 初回読み込み時の案内（OK 後にダウンロード開始）
            EditorUtility.DisplayDialog(
                "samirin33",
                "samirin33製アイテムのアップデート情報を取得するため、Unity起動時などにデータのダウンロードを行います。",
                "OK");

            // Manager が既にあれば InformationChecker に委譲（ダウンロード処理の重複を避ける）
            var installed = await TryForceInstallViaInformationCheckerAsync();
            if (!installed)
            {
                // 初回ブートストラップ（InformationChecker 未導入時のみ）
                installed = await BootstrapDownloadManagerAsync();
            }

            if (!installed)
            {
                SetInstallLock(false);
                return;
            }

            Log($"配置完了: {ManagerAssetPath}");

            // InformationChecker の自動取得と二重ダウンロードしない
            SessionState.SetBool(InformationCheckerSessionCheckedKey, true);

            // リロード後に Item Center を開く
            EditorPrefs.SetBool(PrefsOpenAfterInstallKey, true);

            // Refresh 前に削除予約（ドメインリロード耐性）
            SetPendingDelete(true);
            ScheduleDeleteInstaller(3);
            AssetDatabase.Refresh();
        }
        catch (Exception e)
        {
            LogError("インストール中に例外: " + e);
            SetPendingDelete(false);
            EditorPrefs.DeleteKey(PrefsOpenAfterInstallKey);
            SetInstallLock(false);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            _isRunning = false;
        }
    }

    /// <summary>
    /// 既に InformationChecker がある場合はそちらで強制取得する。
    /// </summary>
    static async Task<bool> TryForceInstallViaInformationCheckerAsync()
    {
        var type = FindTypeByName("InformationChecker");
        if (type == null)
            return false;

        var method = type.GetMethod(
            "ForceInstallAsync",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (method == null)
            return false;

        Log("InformationChecker.ForceInstallAsync に委譲します。");
        try
        {
            var taskObj = method.Invoke(null, new object[] { false });
            if (taskObj is Task<bool> boolTask)
                return await boolTask;
            if (taskObj is Task task)
            {
                await task;
                return true;
            }
        }
        catch (Exception e)
        {
            LogWarning("InformationChecker への委譲に失敗したためブートストラップに切り替えます: " + e.Message);
        }

        return false;
    }

    static Type FindTypeByName(string typeName)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type type = null;
            try
            {
                type = assemblies[i].GetType(typeName, false);
            }
            catch
            {
                // ignore
            }

            if (type != null)
                return type;
        }

        return null;
    }

    /// <summary>
    /// InformationChecker 未導入時の初回取得。
    /// </summary>
    static async Task<bool> BootstrapDownloadManagerAsync()
    {
        string tempRoot = null;
        try
        {
            EditorUtility.DisplayProgressBar("SamirinBoothManager", "GitHub からダウンロード中...", 0.1f);

            tempRoot = Path.Combine(Path.GetTempPath(), "SamirinBoothManagerInstaller_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var zipPath = Path.Combine(tempRoot, "repo.zip");

            if (!await DownloadFileAsync(ZipUrl, zipPath))
            {
                LogError("ダウンロードに失敗しました: " + ZipUrl);
                return false;
            }

            EditorUtility.DisplayProgressBar("SamirinBoothManager", "展開中...", 0.45f);
            var extractRoot = Path.Combine(tempRoot, "extract");
            ZipFile.ExtractToDirectory(zipPath, extractRoot);

            var repoRoot = FindRepoRoot(extractRoot);
            if (string.IsNullOrEmpty(repoRoot))
            {
                LogError("zip 内にリポジトリルートが見つかりませんでした。");
                return false;
            }

            var remoteManager = Path.Combine(repoRoot, "SamirinBoothManager");
            var remoteInformation = Path.Combine(repoRoot, "SamirinBoothInformation");

            if (!Directory.Exists(remoteManager))
            {
                LogError("リモートに SamirinBoothManager フォルダがありません。");
                return false;
            }

            EditorUtility.DisplayProgressBar("SamirinBoothManager", "Assets へコピー中...", 0.75f);

            CopyDirectoryContents(remoteManager, ToAbsolutePath(ManagerAssetPath));
            if (Directory.Exists(remoteInformation))
                CopyDirectoryContents(remoteInformation, ToAbsolutePath(InformationAssetPath));
            else
                LogWarning("リモートに SamirinBoothInformation が無いため Manager のみ配置しました。");

            return true;
        }
        finally
        {
            try
            {
                if (!string.IsNullOrEmpty(tempRoot) && Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, true);
            }
            catch (Exception e)
            {
                LogWarning("一時フォルダの削除に失敗: " + e.Message);
            }
        }
    }

    static void ScheduleDeleteInstaller(int framesLeft)
    {
        EditorApplication.delayCall += () =>
        {
            if (framesLeft > 0)
            {
                ScheduleDeleteInstaller(framesLeft - 1);
                return;
            }

            TryDeleteInstaller();
        };
    }

    static void TryDeleteInstaller()
    {
        if (!TryGetAssetsInstallerScriptPath(out var scriptPath) && !IsPendingDelete())
        {
            SetPendingDelete(false);
            SetInstallLock(false);
            return;
        }

        // フォルダごと消す（Assets/.../SamirinBoothManagerInstaller）
        string deleteTarget = null;
        if (!string.IsNullOrEmpty(scriptPath))
        {
            var dir = Path.GetDirectoryName(scriptPath)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(dir) &&
                dir.EndsWith("/" + InstallerFolderName, StringComparison.OrdinalIgnoreCase))
                deleteTarget = dir;
            else
                deleteTarget = scriptPath;
        }
        else
        {
            var candidates = new[]
            {
                "Assets/" + InstallerFolderName,
                "Assets/samirin33/" + InstallerFolderName,
                "Assets/Editor/" + InstallerFolderName
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                if (AssetDatabase.IsValidFolder(candidates[i]) ||
                    File.Exists(ToAbsolutePath(candidates[i] + "/" + ScriptFileName)))
                {
                    deleteTarget = candidates[i];
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(deleteTarget))
        {
            LogWarning("削除対象のインストーラが見つかりません。予約を解除します。");
            SetPendingDelete(false);
            SetInstallLock(false);
            return;
        }

        Log($"インストーラを削除します: {deleteTarget}");
        try
        {
            bool deleted = AssetDatabase.DeleteAsset(deleteTarget);
            if (!deleted)
            {
                var fullPath = ToAbsolutePath(deleteTarget);
                if (Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, true);
                    var meta = fullPath + ".meta";
                    if (File.Exists(meta))
                        File.Delete(meta);
                    deleted = true;
                }
                else if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    var meta = fullPath + ".meta";
                    if (File.Exists(meta))
                        File.Delete(meta);
                    deleted = true;
                }

                if (deleted)
                    AssetDatabase.Refresh();
            }

            if (deleted)
                Log("インストーラの削除が完了しました。");
            else
                LogWarning("インストーラの削除に失敗しました。");
        }
        catch (Exception e)
        {
            LogWarning("インストーラ削除中に例外: " + e.Message);
        }
        finally
        {
            SetPendingDelete(false);
            SetInstallLock(false);
        }
    }

    static async Task<bool> DownloadFileAsync(string url, string destinationPath)
    {
        using (var request = UnityWebRequest.Get(url))
        {
            request.timeout = 120;
            var operation = request.SendWebRequest();
            while (!operation.isDone)
                await Task.Yield();

#if UNITY_2020_2_OR_NEWER
            if (request.result != UnityWebRequest.Result.Success)
#else
            if (request.isNetworkError || request.isHttpError)
#endif
            {
                LogError("Download error: " + request.error);
                return false;
            }

            var data = request.downloadHandler.data;
            if (data == null || data.Length == 0)
                return false;

            File.WriteAllBytes(destinationPath, data);
            return true;
        }
    }

    static string FindRepoRoot(string extractRoot)
    {
        if (!Directory.Exists(extractRoot))
            return null;

        foreach (var dir in Directory.GetDirectories(extractRoot))
        {
            if (Directory.Exists(Path.Combine(dir, "SamirinBoothManager")) ||
                Directory.Exists(Path.Combine(dir, "SamirinBoothInformation")))
                return dir;
        }

        if (Directory.Exists(Path.Combine(extractRoot, "SamirinBoothManager")) ||
            Directory.Exists(Path.Combine(extractRoot, "SamirinBoothInformation")))
            return extractRoot;

        return null;
    }

    static void CopyDirectoryContents(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = file.Substring(sourceDir.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var destFile = Path.Combine(destinationDir, relative);
            var destFolder = Path.GetDirectoryName(destFile);
            if (!string.IsNullOrEmpty(destFolder))
                Directory.CreateDirectory(destFolder);
            File.Copy(file, destFile, true);
        }
    }

    static string ToAbsolutePath(string assetPath)
    {
        var projectRoot = Path.GetDirectoryName(Application.dataPath);
        return Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>
    /// Assets へ本スクリプトがインポートされたときに起動する。
    /// InitializeOnLoad と二重起動しないようロックを確認する。
    /// </summary>
    class SamirinBoothManagerInstallerPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            for (int i = 0; i < importedAssets.Length; i++)
            {
                var path = importedAssets[i];
                if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!path.EndsWith(ScriptFileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (IsInstallLocked())
                {
                    Log("OnPostprocessAllAssets: 既にインストール中のためスキップ");
                    break;
                }

                Log($"OnPostprocessAllAssets: {path}");
                EditorApplication.delayCall += TryStartInstallFromAssets;
                break;
            }
        }
    }
}
#endif
