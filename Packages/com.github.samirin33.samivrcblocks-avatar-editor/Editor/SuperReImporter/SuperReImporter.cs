#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// .unitypackage に同梱され、導入先でエクスポート対象フォルダを一度削除してから中身を再インポートする。
/// 旧バージョンで削除済みのファイルが残ること、GUID が食い違ったまま参照が壊れることを避ける。
/// Packages 上の編集用ソースは defineConstraints でコンパイルしない。ここが動くのは Assets へ展開されたときだけ。
/// </summary>
public static class SuperReImporter
{
    const string LogPrefix = "[SuperReImporter]";
    const string FolderName = "SuperReImporter";
    const string ScriptFileName = "SuperReImporter.cs";
    const string PayloadFileName = "Payload.unitypackage";
    const string SuppressFileName = "SuperReImporter.suppress";

    /// <summary>PackageExporter.SuperReImporterSuppressSessionKey と同じ。エクスポート中の誤実行を止める。</summary>
    const string SuppressSessionKey = "SuperReImporter.Suppress";

    const string ReimporterAssetPath = "Assets/Editor/" + FolderName;
    const string AvatarInstallerAssetPath = "Assets/SamiVRCBlocksAvatarInstaller";
    const string BoothManagerInstallerAssetPath = "Assets/Editor/SamirinBoothManagerInstaller";

    /// <summary>エクスポート時に '|' 区切りの Assets パスへ書き換える。</summary>
    const string TargetFolders = "__SUPER_REIMPORT_FOLDERS__";

    /// <summary>エクスポートごとの識別子。EditorPrefs のキーに使う。</summary>
    const string ExportToken = "__SUPER_REIMPORT_TOKEN__";

    const string PhaseDelete = "delete";
    const string PhaseImport = "import";
    const string PhaseCleanup = "cleanup";
    const string PhaseDone = "done";
    const string PhaseFailed = "failed";

    /// <summary>完了イベントを取りこぼしたとき、更新が止まってから後処理を検討するまでの秒数。</summary>
    const double IdleSecondsBeforeCleanup = 3.0;

    /// <summary>対象フォルダが戻らないまま打ち切るまでの秒数。</summary>
    const double IdleSecondsBeforeGiveUp = 30.0;

    static bool _busy;
    static bool _tickQueued;
    static double _idleSince = -1;

    static string Pref(string name)
    {
        return "SuperReImporter." + name + "." + ExportToken + "." + StableProjectId();
    }

    [InitializeOnLoadMethod]
    static void OnEditorLoad()
    {
        QueueTick();
    }

    static void QueueTick()
    {
        if (_tickQueued)
            return;
        _tickQueued = true;
        EditorApplication.delayCall += () =>
        {
            _tickQueued = false;
            Tick();
        };
    }

    static void Tick()
    {
        if (_busy)
            return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            QueueTick();
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (IsSuppressed() || !IsConfigured())
            return;
        if (!TryGetScriptAssetPath(out var scriptPath))
            return;

        var stamp = GetScriptStamp(scriptPath);
        var handled = EditorPrefs.GetString(Pref("Stamp"), "");
        var phase = GetPhase();
        var isNewImport = stamp != "0" && !string.Equals(stamp, handled, StringComparison.Ordinal);

        // 進行中の削除／再インポートを、新しいファイルスタンプで最初からやり直さない。
        if (isNewImport && phase != PhaseDelete && phase != PhaseImport && phase != PhaseCleanup)
        {
            BeginReset(scriptPath, stamp);
            return;
        }

        switch (phase)
        {
            case PhaseDelete:
                StartImport();
                break;
            case PhaseImport:
                Subscribe();
                EditorApplication.delayCall += PollImportFallback;
                break;
            case PhaseCleanup:
                CleanupSelf();
                break;
            case PhaseDone:
                if (ReimporterFolderExists())
                    CleanupSelf();
                break;
        }
    }

    static bool IsConfigured()
    {
        if (string.IsNullOrEmpty(ExportToken))
            return false;
        if (ExportToken.IndexOf("SUPER_REIMPORT", StringComparison.Ordinal) >= 0)
            return false;
        if ((TargetFolders ?? "").IndexOf("SUPER_REIMPORT", StringComparison.Ordinal) >= 0)
            return false;
        return true;
    }

    static bool IsSuppressed()
    {
        if (SessionState.GetBool(SuppressSessionKey, false))
            return true;
        return File.Exists(ToAbsolute(ReimporterAssetPath + "/" + SuppressFileName));
    }

    static string GetPhase()
    {
        return EditorPrefs.GetString(Pref("Phase"), "");
    }

    static void BeginReset(string scriptPath, string stamp)
    {
        _busy = true;
        try
        {
            var folders = ParseTargetFolders();
            var payloadFull = ToAbsolute(ReimporterAssetPath + "/" + PayloadFileName);
            if (!File.Exists(payloadFull))
            {
                MarkFailed(stamp, "再インポート用の Payload.unitypackage が見つかりません: " + payloadFull);
                return;
            }

            var tempPayload = Path.Combine(
                Path.GetTempPath(),
                "SuperReImporter_" + ExportToken + "_" + StableProjectId() + ".unitypackage");
            File.Copy(payloadFull, tempPayload, true);

            EditorPrefs.SetString(Pref("Payload"), tempPayload);
            EditorPrefs.SetString(Pref("Stamp"), stamp);
            EditorPrefs.SetString(Pref("Phase"), PhaseDelete);
            EditorPrefs.SetBool(Pref("Launched"), false);
            EditorPrefs.SetInt(Pref("Retries"), 0);

            if (folders.Count == 0)
                Log("削除対象フォルダがないため、削除せず再インポートします。");
            else
                Log("指定フォルダを一度削除してから再インポートします: " + string.Join(", ", folders.ToArray()));

            EditorUtility.DisplayProgressBar("SuperReImporter", "既存フォルダを削除しています...", 0.35f);
            for (int i = 0; i < folders.Count; i++)
                DeleteExportFolder(folders[i]);
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();

            // スクリプト削除でドメインリロードした場合は、次回の Tick が再インポートへ進む。
            QueueTick();
        }
        catch (Exception e)
        {
            MarkFailed(stamp, "リセット中に例外: " + e);
        }
        finally
        {
            _busy = false;
            EditorUtility.ClearProgressBar();
        }
    }

    static void StartImport()
    {
        if (_busy)
            return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            QueueTick();
            return;
        }

        var phase = GetPhase();
        if (phase != PhaseDelete && phase != PhaseImport)
            return;

        if (EditorPrefs.GetBool(Pref("Launched"), false))
        {
            Subscribe();
            EditorApplication.delayCall += PollImportFallback;
            return;
        }

        var tempPayload = EditorPrefs.GetString(Pref("Payload"), "");
        if (string.IsNullOrEmpty(tempPayload) || !File.Exists(tempPayload))
        {
            MarkFailed(null, "再インポート用ファイルが見つかりません: " + tempPayload);
            return;
        }

        _busy = true;
        try
        {
            Log("再インポートを開始します: " + tempPayload);
            EditorPrefs.SetString(Pref("Phase"), PhaseImport);
            EditorPrefs.SetBool(Pref("Launched"), true);
            _idleSince = -1;
            Subscribe();
            EditorUtility.DisplayProgressBar("SuperReImporter", "パッケージを再インポートしています...", 0.75f);
            AssetDatabase.ImportPackage(tempPayload, false);
            EditorUtility.ClearProgressBar();
            EditorApplication.delayCall += PollImportFallback;
        }
        catch (Exception e)
        {
            EditorPrefs.SetBool(Pref("Launched"), false);
            MarkFailed(null, "再インポートの開始に失敗: " + e.Message);
        }
        finally
        {
            _busy = false;
            EditorUtility.ClearProgressBar();
        }
    }

    static void PollImportFallback()
    {
        if (GetPhase() != PhaseImport)
            return;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            _idleSince = -1;
            EditorApplication.delayCall += PollImportFallback;
            return;
        }

        if (_idleSince < 0)
            _idleSince = EditorApplication.timeSinceStartup;

        var idle = EditorApplication.timeSinceStartup - _idleSince;
        if (idle < IdleSecondsBeforeCleanup)
        {
            EditorApplication.delayCall += PollImportFallback;
            return;
        }

        // 削除後に対象フォルダが戻っていれば、再インポートは届いている。
        // 削除対象が無い場合は、更新が止まっていれば完了とみなす。
        var restoredFolders = ParseTargetFolders();
        if (restoredFolders.Count == 0 || AnyTargetExists(restoredFolders))
        {
            Log("対象フォルダの復元を確認したため後処理へ進みます。");
            Unsubscribe();
            EditorPrefs.SetString(Pref("Phase"), PhaseCleanup);
            QueueTick();
            return;
        }

        if (idle < IdleSecondsBeforeGiveUp)
        {
            EditorApplication.delayCall += PollImportFallback;
            return;
        }

        MarkFailed(null, "再インポートの完了を確認できませんでした。対象フォルダが復元されていません。");
    }

    static void OnImportCompleted(string packageName)
    {
        if (GetPhase() != PhaseImport)
            return;
        if (!IsOurPayload(packageName))
            return;

        Log("再インポートが完了しました: " + packageName);
        Unsubscribe();
        EditorPrefs.SetString(Pref("Phase"), PhaseCleanup);
        QueueTick();
    }

    static void OnImportFailed(string packageName, string errorMessage)
    {
        if (GetPhase() != PhaseImport)
            return;
        if (!IsOurPayload(packageName))
            return;

        LogError("再インポートに失敗しました: " + errorMessage);
        Unsubscribe();

        var retries = EditorPrefs.GetInt(Pref("Retries"), 0);
        if (retries < 1)
        {
            EditorPrefs.SetInt(Pref("Retries"), retries + 1);
            EditorPrefs.SetBool(Pref("Launched"), false);
            EditorPrefs.SetString(Pref("Phase"), PhaseImport);
            Log("再インポートを 1 回だけ再試行します。");
            EditorApplication.delayCall += StartImport;
            return;
        }

        MarkFailed(null, "再インポートに失敗しました。Payload.unitypackage を手動でインポートしてください。 " + errorMessage);
    }

    static void OnImportCancelled(string packageName)
    {
        if (GetPhase() != PhaseImport)
            return;
        if (!IsOurPayload(packageName))
            return;

        Unsubscribe();
        MarkFailed(null, "再インポートがキャンセルされました: " + packageName);
    }

    static bool IsOurPayload(string packageName)
    {
        if (string.IsNullOrEmpty(packageName))
            return false;
        return packageName.IndexOf(ExportToken, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static void Subscribe()
    {
        AssetDatabase.importPackageCompleted -= OnImportCompleted;
        AssetDatabase.importPackageFailed -= OnImportFailed;
        AssetDatabase.importPackageCancelled -= OnImportCancelled;
        AssetDatabase.importPackageCompleted += OnImportCompleted;
        AssetDatabase.importPackageFailed += OnImportFailed;
        AssetDatabase.importPackageCancelled += OnImportCancelled;
    }

    static void Unsubscribe()
    {
        AssetDatabase.importPackageCompleted -= OnImportCompleted;
        AssetDatabase.importPackageFailed -= OnImportFailed;
        AssetDatabase.importPackageCancelled -= OnImportCancelled;
    }

    static void MarkFailed(string stamp, string message)
    {
        if (!string.IsNullOrEmpty(stamp))
            EditorPrefs.SetString(Pref("Stamp"), stamp);
        EditorPrefs.SetString(Pref("Phase"), PhaseFailed);
        EditorPrefs.SetBool(Pref("Launched"), false);
        Unsubscribe();
        EditorUtility.ClearProgressBar();
        LogError(message);
    }

    static void CleanupSelf()
    {
        if (GetPhase() == PhaseFailed)
            return;
        if (_busy)
            return;

        _busy = true;
        try
        {
            Unsubscribe();
            EditorUtility.ClearProgressBar();

            var tempPayload = EditorPrefs.GetString(Pref("Payload"), "");
            EditorPrefs.SetString(Pref("Phase"), PhaseDone);
            EditorPrefs.DeleteKey(Pref("Payload"));
            EditorPrefs.SetBool(Pref("Launched"), false);
            EditorPrefs.SetInt(Pref("Retries"), 0);

            if (!string.IsNullOrEmpty(tempPayload) && File.Exists(tempPayload))
            {
                try
                {
                    File.Delete(tempPayload);
                }
                catch (Exception e)
                {
                    LogWarning("一時ファイルの削除に失敗: " + e.Message);
                }
            }

            if (!ReimporterFolderExists())
            {
                // スタンプを消す。同じ更新時刻のまま再インポートされても、次回は新しい導入として扱う。
                EditorPrefs.DeleteKey(Pref("Stamp"));
                Log("完了しました。");
                return;
            }

            Log("SuperReImporter を削除します: " + ReimporterAssetPath);
            ForceDeleteAsset(ReimporterAssetPath);
            AssetDatabase.Refresh();
            if (!ReimporterFolderExists())
            {
                EditorPrefs.DeleteKey(Pref("Stamp"));
                Log("完了しました。対象フォルダを削除し、パッケージを再インポートしました。");
            }
            else
            {
                LogWarning("再インポートは完了しましたが、SuperReImporter フォルダが残っています: " + ReimporterAssetPath);
            }
        }
        catch (Exception e)
        {
            LogWarning("SuperReImporter の削除に失敗: " + e.Message);
        }
        finally
        {
            _busy = false;
            EditorUtility.ClearProgressBar();
        }
    }

    static List<string> ParseTargetFolders()
    {
        var result = new List<string>();
        var parts = TargetFolders.Split('|');
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < parts.Length; i++)
        {
            var folder = (parts[i] ?? "").Replace("\\", "/").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(folder) || folder == "Assets")
                continue;
            if (!folder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                continue;
            if (IsProtectedAssetPath(folder))
                continue;
            if (!seen.Add(folder))
                continue;
            result.Add(folder);
        }
        return result;
    }

    static bool AnyTargetExists(List<string> folders)
    {
        for (int i = 0; i < folders.Count; i++)
        {
            if (AssetDatabase.IsValidFolder(folders[i]) || Directory.Exists(ToAbsolute(folders[i])))
                return true;
        }
        return false;
    }

    static bool IsProtectedAssetPath(string assetPath)
    {
        return IsSameOrUnderAsset(assetPath, ReimporterAssetPath)
            || IsSameOrUnderAsset(assetPath, AvatarInstallerAssetPath)
            || IsSameOrUnderAsset(assetPath, BoothManagerInstallerAssetPath);
    }

    static bool IsSameOrUnderAsset(string assetPath, string parentAssetPath)
    {
        var path = (assetPath ?? "").Replace("\\", "/").TrimEnd('/');
        var parent = (parentAssetPath ?? "").Replace("\\", "/").TrimEnd('/');
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(parent))
            return false;
        if (string.Equals(path, parent, StringComparison.OrdinalIgnoreCase))
            return true;
        return path.StartsWith(parent + "/", StringComparison.OrdinalIgnoreCase);
    }

    static void DeleteExportFolder(string assetFolder)
    {
        var folder = (assetFolder ?? "").Replace("\\", "/").TrimEnd('/');
        if (string.IsNullOrEmpty(folder) || folder == "Assets")
        {
            LogWarning("Assets ルートは削除しません。");
            return;
        }
        if (!folder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            return;
        if (IsProtectedAssetPath(folder))
        {
            Log("インポーターのため削除しません: " + folder);
            return;
        }

        var keeps = new List<string>();
        AddKeepIfInside(keeps, folder, ReimporterAssetPath);
        AddKeepIfInside(keeps, folder, AvatarInstallerAssetPath);
        AddKeepIfInside(keeps, folder, BoothManagerInstallerAssetPath);

        try
        {
            if (keeps.Count > 0)
            {
                Log("指定フォルダのうち、インポーターは残して削除します: " + folder);
                DeleteTreeExcept(ToAbsolute(folder), keeps);
            }
            else
            {
                ForceDeleteAsset(folder);
            }
        }
        catch (Exception e)
        {
            LogWarning("フォルダ削除に失敗: " + folder + " / " + e.Message);
        }
    }

    static void AddKeepIfInside(List<string> keeps, string parentAssetPath, string keepAssetPath)
    {
        if (keepAssetPath.StartsWith(parentAssetPath + "/", StringComparison.OrdinalIgnoreCase))
            keeps.Add(ToAbsolute(keepAssetPath));
    }

    /// <summary>
    /// directory 以下を消す。keeps とその先祖は残す。
    /// </summary>
    static void DeleteTreeExcept(string directoryFullPath, List<string> keepFullPaths)
    {
        if (string.IsNullOrEmpty(directoryFullPath) || !Directory.Exists(directoryFullPath))
            return;
        if (IsUnderAny(directoryFullPath, keepFullPaths))
            return;

        if (ContainsAnyKeep(directoryFullPath, keepFullPaths))
        {
            var entries = Directory.GetFileSystemEntries(directoryFullPath);
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;
                DeleteTreeExceptEntry(entry, keepFullPaths);
            }
            return;
        }

        DeletePathAndMeta(directoryFullPath);
    }

    static void DeleteTreeExceptEntry(string entryFullPath, List<string> keepFullPaths)
    {
        if (IsUnderAny(entryFullPath, keepFullPaths))
            return;

        if (Directory.Exists(entryFullPath) && ContainsAnyKeep(entryFullPath, keepFullPaths))
        {
            DeleteTreeExcept(entryFullPath, keepFullPaths);
            return;
        }

        DeletePathAndMeta(entryFullPath);
    }

    static bool IsUnderAny(string fullPath, List<string> keepFullPaths)
    {
        for (int i = 0; i < keepFullPaths.Count; i++)
        {
            if (IsSameOrChild(fullPath, keepFullPaths[i]))
                return true;
        }
        return false;
    }

    static bool ContainsAnyKeep(string directoryFullPath, List<string> keepFullPaths)
    {
        for (int i = 0; i < keepFullPaths.Count; i++)
        {
            var keep = keepFullPaths[i];
            if (IsSameOrChild(keep, directoryFullPath) && !IsSameOrChild(directoryFullPath, keep))
                return true;
        }
        return false;
    }

    static void DeletePathAndMeta(string fullPath)
    {
        if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, true);
        else if (File.Exists(fullPath))
            File.Delete(fullPath);

        var meta = fullPath + ".meta";
        if (File.Exists(meta))
            File.Delete(meta);
    }

    static void ForceDeleteAsset(string assetPath)
    {
        var normalized = (assetPath ?? "").Replace("\\", "/").TrimEnd('/');
        if (string.IsNullOrEmpty(normalized) || normalized == "Assets")
            return;
        if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            return;

        var removed = false;
        if (AssetDatabase.IsValidFolder(normalized) ||
            AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(normalized) != null)
        {
            removed = AssetDatabase.DeleteAsset(normalized);
        }

        var full = ToAbsolute(normalized);
        if (Directory.Exists(full))
        {
            Directory.Delete(full, true);
            removed = true;
        }
        else if (File.Exists(full))
        {
            File.Delete(full);
            removed = true;
        }

        var meta = full + ".meta";
        if (File.Exists(meta))
        {
            File.Delete(meta);
            removed = true;
        }

        if (removed)
            Log("削除しました: " + normalized);
        else
            Log("削除対象はありません: " + normalized);
    }

    static bool ReimporterFolderExists()
    {
        if (AssetDatabase.IsValidFolder(ReimporterAssetPath))
            return true;
        return Directory.Exists(ToAbsolute(ReimporterAssetPath));
    }

    static bool TryGetScriptAssetPath(out string scriptAssetPath)
    {
        scriptAssetPath = null;
        var guids = AssetDatabase.FindAssets("SuperReImporter t:MonoScript");
        for (int i = 0; i < guids.Length; i++)
        {
            var path = AssetDatabase.GUIDToAssetPath(guids[i]).Replace("\\", "/");
            if (string.IsNullOrEmpty(path))
                continue;
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!path.EndsWith("/" + ScriptFileName, StringComparison.OrdinalIgnoreCase))
                continue;
            scriptAssetPath = path;
            return true;
        }

        var fallback = ReimporterAssetPath + "/" + ScriptFileName;
        if (File.Exists(ToAbsolute(fallback)))
        {
            scriptAssetPath = fallback;
            return true;
        }

        return false;
    }

    /// <summary>
    /// ドメインリロードでは変わらず、ファイルを消してから再配置すると変わる値。
    /// 更新時刻だけだと unitypackage が時刻を保持した再インポートを見逃す。
    /// </summary>
    static string GetScriptStamp(string assetPath)
    {
        var full = ToAbsolute(assetPath);
        if (!File.Exists(full))
            return "0";
        var info = new FileInfo(full);
        return info.CreationTimeUtc.Ticks.ToString() + ":" +
               info.LastWriteTimeUtc.Ticks.ToString() + ":" +
               info.Length.ToString();
    }

    static string ToAbsolute(string assetPath)
    {
        var relative = (assetPath ?? "").Replace("/", Path.DirectorySeparatorChar.ToString());
        var projectRoot = Path.GetDirectoryName(Application.dataPath);
        return Path.GetFullPath(Path.Combine(projectRoot, relative));
    }

    /// <summary>string.GetHashCode はドメインをまたぐと変わり得るため、パスから安定した値を作る。</summary>
    static string StableProjectId()
    {
        unchecked
        {
            uint hash = 2166136261;
            var value = Application.dataPath ?? "";
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 16777619;
            }
            return hash.ToString("X8");
        }
    }

    static bool IsSameOrChild(string path, string parent)
    {
        path = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        parent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(path, parent, StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWith(parent + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    static void Log(string message) => Debug.Log(LogPrefix + " " + message);
    static void LogWarning(string message) => Debug.LogWarning(LogPrefix + " " + message);
    static void LogError(string message) => Debug.LogError(LogPrefix + " " + message);

    sealed class SuperReImporterPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (importedAssets == null)
                return;

            for (int i = 0; i < importedAssets.Length; i++)
            {
                var path = (importedAssets[i] ?? "").Replace("\\", "/");
                if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!path.EndsWith("/" + ScriptFileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (IsSuppressed() || !IsConfigured())
                    return;

                var phase = GetPhase();
                if (phase == PhaseDelete || phase == PhaseImport || phase == PhaseCleanup)
                    return;

                // 同じ内容の再インポートではコンパイルが走らず、スタンプも変わらないことがある。
                EditorPrefs.DeleteKey(Pref("Stamp"));
                Log("パッケージの再インポートを検出しました: " + path);
                QueueTick();
                return;
            }
        }
    }
}
#endif
