using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
public class SamiVRCBlocksAvatarInstallerEditor
{
    private const string LogPrefix = "[SamiVRCBlocksAvatar][Installer]";
    /// <summary>
    /// 仮展開（エクスポート）時に InstallerImport がステージ先 unitypackage の GUID で書き換える。
    /// Packages 原本の値は参照用フォールバック。
    /// </summary>
    private const string TargetAssetGUID = "65eefbae1426e1d4d8c006945561537a";
    private const string PackagePath = "Packages/com.github.samirin33.samivrcblocks-avatar";
    private const string InstallerFolderName = "SamiVRCBlocksAvatarInstaller";
    private const string LegacyInstallerFolderName = "AvatarInstaller";
    private const string InstallerFolderAssetPath = "Assets/" + InstallerFolderName;
    private const string UnityPackageFileName = "SamiVRCBlocksInstaller.unitypackage";
    private const string LegacyUnityPackageFileName = "AvatarsInstaller.unitypackage";
    private const string UnityPackageAssetPath =
        InstallerFolderAssetPath + "/" + UnityPackageFileName;

    private static readonly string[] InstallerFolderNames =
    {
        InstallerFolderName,
        LegacyInstallerFolderName
    };

    private static readonly string[] UnityPackageFileNames =
    {
        UnityPackageFileName,
        LegacyUnityPackageFileName
    };

    /// <summary>ImportPackage 完了イベントを逃しても消せるよう、最低待機秒数（ドメインリロード耐性）。</summary>
    private const double MinSecondsBeforeFallbackDelete = 8.0;

    private static bool _isRunning;
    private static bool _importStarted;

    private static string PendingDeletePrefsKey =>
        "SamiVRCBlocksAvatar.Installer.PendingDelete." + Application.dataPath;
    private static string PendingDeleteTicksPrefsKey =>
        "SamiVRCBlocksAvatar.Installer.PendingDeleteTicks." + Application.dataPath;

    [InitializeOnLoadMethod]
    private static void OnEditorLoad()
    {
        // unitypackage 初回展開時は、自スクリプト未コンパイルのため OnPostprocessAllAssets を逃す。
        // コンパイル後の InitializeOnLoad で必ず起動する。
        EditorApplication.delayCall += OnEditorLoadDelayed;
    }

    private static void OnEditorLoadDelayed()
    {
        ResumePendingCleanupIfNeeded();
        TryAutoStartImport();
    }

    /// <summary>
    /// Assets 配下にインストーラが残っていれば自動開始する。
    /// （Packages 原本は defineConstraints で未コンパイルのため、ここに到達しない）
    /// </summary>
    private static void TryAutoStartImport()
    {
        if (_isRunning || IsPendingDelete())
            return;
        if (!InstallerFolderExists())
            return;

        _isRunning = true;
        Log("InitializeOnLoad: Installer フォルダを検出 → TryStartImport を開始");
        TryStartImport(60);
    }

    private static void Log(string message) => Debug.Log($"{LogPrefix} {message}");
    private static void LogWarning(string message) => Debug.LogWarning($"{LogPrefix} {message}");
    private static void LogError(string message) => Debug.LogError($"{LogPrefix} {message}");

    private static void SetPendingDelete(bool pending)
    {
        if (pending)
        {
            EditorPrefs.SetBool(PendingDeletePrefsKey, true);
            EditorPrefs.SetString(PendingDeleteTicksPrefsKey, System.DateTime.UtcNow.Ticks.ToString());
            Log($"削除予約 ON (ticks={System.DateTime.UtcNow.Ticks})");
        }
        else
        {
            EditorPrefs.DeleteKey(PendingDeletePrefsKey);
            EditorPrefs.DeleteKey(PendingDeleteTicksPrefsKey);
            Log("削除予約 OFF");
        }
    }

    private static bool IsPendingDelete() => EditorPrefs.GetBool(PendingDeletePrefsKey, false);

    private static double GetPendingDeleteElapsedSeconds()
    {
        var ticksStr = EditorPrefs.GetString(PendingDeleteTicksPrefsKey, "");
        if (string.IsNullOrEmpty(ticksStr) || !long.TryParse(ticksStr, out var ticks))
            return double.MaxValue;
        return (System.DateTime.UtcNow - new System.DateTime(ticks, System.DateTimeKind.Utc)).TotalSeconds;
    }

    private static bool IsPackageAlreadyImported()
    {
        string packageDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", PackagePath));
        if (Directory.Exists(packageDir))
        {
            Log($"パッケージ検出: ディレクトリあり → {packageDir}");
            return true;
        }

        string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
        if (File.Exists(manifestPath))
        {
            try
            {
                string manifestJson = File.ReadAllText(manifestPath);
                if (manifestJson.Contains("com.github.samirin33.samivrcblocks-avatar"))
                {
                    Log("パッケージ検出: manifest.json に依存あり");
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                LogWarning($"manifest.json の読み込みに失敗: {ex.Message}");
            }
        }

        Log("パッケージ未導入");
        return false;
    }

    private static bool InstallerFolderExists()
    {
        foreach (var folderName in InstallerFolderNames)
        {
            var assetPath = "Assets/" + folderName;
            if (AssetDatabase.IsValidFolder(assetPath))
                return true;
            if (Directory.Exists(Path.Combine(Application.dataPath, folderName)))
                return true;
        }
        return false;
    }

    private static string ResolveUnityPackageFullPath()
    {
        // GUID は Packages 側と衝突し得るため、配布先の Assets パスを優先する。
        foreach (var folderName in InstallerFolderNames)
        {
            foreach (var fileName in UnityPackageFileNames)
            {
                string preferredFullPath = Path.GetFullPath(
                    Path.Combine(Application.dataPath, folderName, fileName));
                if (File.Exists(preferredFullPath))
                {
                    Log($"unitypackage: Assets 固定パスを使用 → {preferredFullPath}");
                    return preferredFullPath;
                }
            }
        }

        string guidPath = AssetDatabase.GUIDToAssetPath(TargetAssetGUID);
        Log($"GUID解決: {TargetAssetGUID} → '{guidPath}'");

        string assetPath = string.IsNullOrEmpty(guidPath) ? UnityPackageAssetPath : guidPath;
        if (string.IsNullOrEmpty(guidPath))
            Log($"GUID未解決のため固定パスを使用: {assetPath}");

        // Packages 側を掴んだ場合は Assets 配布物ではないのでスキップ
        if (!string.IsNullOrEmpty(guidPath) &&
            guidPath.Replace("\\", "/").StartsWith("Packages/", System.StringComparison.OrdinalIgnoreCase))
        {
            LogWarning($"GUID が Packages 側を指しているため無視: {guidPath}");
            assetPath = UnityPackageAssetPath;
        }

        string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        bool exists = File.Exists(fullPath);
        Log($"unitypackage フルパス: {fullPath} (exists={exists})");
        return exists ? fullPath : null;
    }

    private static void ResumePendingCleanupIfNeeded()
    {
        if (!IsPendingDelete())
            return;

        if (!InstallerFolderExists())
        {
            Log("削除予約はあるがフォルダが無いため予約を解除");
            SetPendingDelete(false);
            return;
        }

        var elapsed = GetPendingDeleteElapsedSeconds();
        Log($"ドメインリロード後: 削除予約を検出 (elapsed={elapsed:F1}s)");

        // ImportPackage 完了コールバックがロストしている場合の再開。
        // 最低待機を満たしていれば削除、未満ならポーリング継続。
        if (elapsed >= MinSecondsBeforeFallbackDelete)
            ScheduleDeleteInstallerFolder(5);
        else
            EditorApplication.delayCall += () => PollDeleteAfterImport();
    }

    private static void ScheduleDeleteInstallerFolder(int framesLeft)
    {
        Log($"削除をスケジュール (残り={framesLeft})");
        EditorApplication.delayCall += () =>
        {
            if (framesLeft > 0)
            {
                ScheduleDeleteInstallerFolder(framesLeft - 1);
                return;
            }

            DeleteInstallerFolder();
        };
    }

    private static void DeleteInstallerFolder()
    {
        Log("Installer フォルダ削除を実行します");
        try
        {
            foreach (var folderName in InstallerFolderNames)
            {
                var assetPath = "Assets/" + folderName;
                bool deleted = false;
                if (AssetDatabase.IsValidFolder(assetPath))
                {
                    deleted = AssetDatabase.DeleteAsset(assetPath);
                    Log($"AssetDatabase.DeleteAsset ({assetPath}) 結果: {deleted}");
                }

                var fullPath = Path.Combine(Application.dataPath, folderName);
                if (!Directory.Exists(fullPath))
                    continue;

                if (!deleted)
                    Log($"AssetDatabase 未削除のためディスク削除を試行: {fullPath}");
                else
                    Log($"AssetDatabase 削除後も残存のためディスク削除を試行: {fullPath}");

                Directory.Delete(fullPath, true);
                var metaPath = fullPath + ".meta";
                if (File.Exists(metaPath))
                    File.Delete(metaPath);
            }

            AssetDatabase.Refresh();

            if (!InstallerFolderExists())
                Log("Installer フォルダ削除完了");
            else
                LogWarning("Installer フォルダが残っています");
        }
        catch (System.Exception ex)
        {
            LogWarning($"Installer フォルダの削除に失敗しました: {ex.Message}");
        }
        finally
        {
            SetPendingDelete(false);
            _isRunning = false;
            _importStarted = false;
            Log("フロー終了");
        }
    }

    private static void OnImportPackageCompleted(string packageName)
    {
        Log($"importPackageCompleted: packageName='{packageName}' → 削除へ進みます");
        AssetDatabase.importPackageCompleted -= OnImportPackageCompleted;
        AssetDatabase.importPackageFailed -= OnImportPackageFailed;
        // unitypackage 展開完了済み。パッケージ配置確認は待たない。
        ScheduleDeleteInstallerFolder(8);
    }

    private static void OnImportPackageFailed(string packageName, string errorMessage)
    {
        LogError($"importPackageFailed: packageName='{packageName}', error='{errorMessage}'");
        AssetDatabase.importPackageCompleted -= OnImportPackageCompleted;
        AssetDatabase.importPackageFailed -= OnImportPackageFailed;
        SetPendingDelete(false);
        _isRunning = false;
        _importStarted = false;
        Log("インポート失敗のためフォルダは削除しません");
    }

    private static void TryStartImport(int retriesLeft)
    {
        Log($"TryStartImport: retriesLeft={retriesLeft}, importStarted={_importStarted}, pendingDelete={IsPendingDelete()}");

        string fullPath = ResolveUnityPackageFullPath();
        if (!string.IsNullOrEmpty(fullPath))
        {
            if (_importStarted)
            {
                Log("ImportPackage は既に開始済みのため待機します");
                return;
            }

            _importStarted = true;
            SetPendingDelete(true);
            bool already = IsPackageAlreadyImported();
            Log($"ImportPackage を開始します (alreadyImported={already}): {fullPath}");
            AssetDatabase.importPackageCompleted += OnImportPackageCompleted;
            AssetDatabase.importPackageFailed += OnImportPackageFailed;
            AssetDatabase.ImportPackage(fullPath, false);

            // 完了イベントがドメインリロードで消えた場合のフォールバック
            EditorApplication.delayCall += PollDeleteAfterImport;
            return;
        }

        if (retriesLeft > 0)
        {
            Log($"unitypackage 未準備のため待機します (残り {retriesLeft})");
            EditorApplication.delayCall += () => TryStartImport(retriesLeft - 1);
            return;
        }

        LogError("Installer 用 .unitypackage が見つからないためインポートを中止しました。フォルダは削除します。");
        SetPendingDelete(true);
        ScheduleDeleteInstallerFolder(3);
    }

    /// <summary>
    /// importPackageCompleted ロスト時のフォールバック。最低待機後に削除する。
    /// （既にパッケージ導入済みでも、インポート中に即削除しない）
    /// </summary>
    private static void PollDeleteAfterImport()
    {
        if (!IsPendingDelete())
        {
            Log("ポーリング: 削除予約が無いため終了");
            return;
        }

        if (!InstallerFolderExists())
        {
            Log("ポーリング: フォルダ消失を確認、予約解除");
            SetPendingDelete(false);
            return;
        }

        var elapsed = GetPendingDeleteElapsedSeconds();
        if (elapsed >= MinSecondsBeforeFallbackDelete)
        {
            Log($"ポーリング: フォールバック削除へ (elapsed={elapsed:F1}s >= {MinSecondsBeforeFallbackDelete}s)");
            ScheduleDeleteInstallerFolder(3);
            return;
        }

        Log($"ポーリング待機中 (elapsed={elapsed:F1}s / {MinSecondsBeforeFallbackDelete}s)");
        EditorApplication.delayCall += PollDeleteAfterImport;
    }

    private class SamiVRCBlocksAvatarInstallerAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            foreach (string path in importedAssets)
            {
                var normalized = path.Replace("\\", "/");
                if (!normalized.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase) ||
                    !normalized.EndsWith("SamiVRCBlocksAvatarInstallerEditor.cs", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                Log($"OnPostprocessAllAssets: 検出 path='{normalized}', _isRunning={_isRunning}, pendingDelete={IsPendingDelete()}");

                if (IsPendingDelete())
                {
                    Log("削除予約中のため ImportPackage は再実行せず、フォールバック削除監視を継続");
                    EditorApplication.delayCall += PollDeleteAfterImport;
                    break;
                }

                if (_isRunning)
                {
                    Log("既にフロー実行中のためスキップ");
                    break;
                }

                _isRunning = true;
                Log("delayCall で TryStartImport を予約します");
                EditorApplication.delayCall += () => TryStartImport(60);
                break;
            }
        }
    }
}
#endif
