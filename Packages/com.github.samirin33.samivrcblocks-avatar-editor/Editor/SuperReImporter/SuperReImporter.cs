#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// .unitypackage に同梱され、導入先でエクスポート対象フォルダを一度取り除いてから中身を再インポートする。
/// 旧バージョンで削除済みのファイルが残ること、GUID が食い違ったまま参照が壊れることを避ける。
/// 取り除くフォルダは確認ダイアログを出したうえで Library/SamiVRCBlocks/SuperReImporterBackup へ退避し、
/// 失敗時は退避分を元の場所へ戻して自分自身（Assets/Editor/SuperReImporter）も削除する。
/// Packages 上の編集用ソースは defineConstraints でコンパイルしない。ここが動くのは Assets へ展開されたときだけ。
/// このファイルは導入先へ単体でコピーされるため、パッケージ内の他のコードに依存させない。
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

    /// <summary>Assets の外に置き、退避中に再インポートやコンパイルの対象にならないようにする。</summary>
    const string BackupRootRelative = "Library/SamiVRCBlocks/SuperReImporterBackup";
    const string BackupManifestFileName = "moved.txt";
    const int MaxBackupsToKeep = 5;

    enum ResetChoice
    {
        BackupAndReplace,
        Cancel,
        OverwriteWithoutRemoving
    }

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

            var existing = new List<string>();
            for (int i = 0; i < folders.Count; i++)
            {
                if (AssetDatabase.IsValidFolder(folders[i]) || Directory.Exists(ToAbsolute(folders[i])))
                    existing.Add(folders[i]);
            }

            var choice = existing.Count > 0 ? AskResetChoice(existing) : ResetChoice.OverwriteWithoutRemoving;
            if (choice == ResetChoice.Cancel)
            {
                CancelByUser(stamp, payloadFull);
                return;
            }

            var tempPayload = Path.Combine(
                Path.GetTempPath(),
                "SuperReImporter_" + ExportToken + "_" + StableProjectId() + ".unitypackage");
            File.Copy(payloadFull, tempPayload, true);

            EditorPrefs.SetString(Pref("Payload"), tempPayload);
            EditorPrefs.SetString(Pref("Stamp"), stamp);
            EditorPrefs.DeleteKey(Pref("Backup"));
            EditorPrefs.SetString(Pref("Phase"), PhaseDelete);
            EditorPrefs.SetBool(Pref("Launched"), false);
            EditorPrefs.SetInt(Pref("Retries"), 0);

            if (existing.Count == 0)
            {
                Log("既存の対象フォルダがないため、そのまま再インポートします。");
            }
            else if (choice == ResetChoice.OverwriteWithoutRemoving)
            {
                Log("既存フォルダを残したまま上書きインポートします: " + string.Join(", ", existing.ToArray()));
            }
            else
            {
                var backupRoot = CreateBackupRoot();
                EditorPrefs.SetString(Pref("Backup"), backupRoot);
                Log("指定フォルダを退避してから再インポートします: " + string.Join(", ", existing.ToArray()) +
                    " / 退避先: " + backupRoot);

                EditorUtility.DisplayProgressBar("SuperReImporter", "既存フォルダを退避しています...", 0.35f);
                try
                {
                    for (int i = 0; i < existing.Count; i++)
                        MoveExportFolderToBackup(existing[i], backupRoot);
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                    AssetDatabase.Refresh();
                }
            }

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

    /// <summary>
    /// 失敗として終える。退避済みフォルダを戻し、Payload を手動インポート用に Library へ残して、自分自身を削除する。
    /// </summary>
    static void MarkFailed(string stamp, string message)
    {
        if (!string.IsNullOrEmpty(stamp))
            EditorPrefs.SetString(Pref("Stamp"), stamp);
        EditorPrefs.SetString(Pref("Phase"), PhaseFailed);
        EditorPrefs.SetBool(Pref("Launched"), false);
        Unsubscribe();
        EditorUtility.ClearProgressBar();

        var backupRoot = EditorPrefs.GetString(Pref("Backup"), "");
        var restoreNote = "";
        try
        {
            if (!string.IsNullOrEmpty(backupRoot))
            {
                var notRestored = RestoreBackup(backupRoot);
                restoreNote = notRestored.Count == 0
                    ? "退避したフォルダを元の場所へ戻しました。"
                    : "次の項目は同じ場所に別のファイルがあるため戻していません（退避先に残っています）:\n  " +
                      string.Join("\n  ", notRestored.ToArray());
            }
        }
        catch (Exception e)
        {
            restoreNote = "退避したフォルダの復元に失敗しました: " + e.Message;
            LogError(restoreNote);
        }

        string savedPayload = null;
        try
        {
            var tempPayload = EditorPrefs.GetString(Pref("Payload"), "");
            var payloadSource = !string.IsNullOrEmpty(tempPayload) && File.Exists(tempPayload)
                ? tempPayload
                : ToAbsolute(ReimporterAssetPath + "/" + PayloadFileName);
            if (File.Exists(payloadSource))
            {
                if (string.IsNullOrEmpty(backupRoot))
                    backupRoot = CreateBackupRoot();
                savedPayload = SavePayloadCopy(backupRoot, payloadSource);
            }
        }
        catch (Exception e)
        {
            LogWarning("Payload の保存に失敗: " + e.Message);
        }

        DeleteTempPayload();
        EditorPrefs.DeleteKey(Pref("Payload"));
        EditorPrefs.DeleteKey(Pref("Backup"));

        var summary = "SuperReImporter による再インポートに失敗しました。\n" + message;
        if (!string.IsNullOrEmpty(restoreNote))
            summary += "\n\n" + restoreNote;
        if (!string.IsNullOrEmpty(backupRoot))
            summary += "\n\n退避先: " + backupRoot;
        if (!string.IsNullOrEmpty(savedPayload))
            summary += "\n\n手動でインポートする場合はこのファイルを使ってください:\n" + savedPayload;
        LogError(summary);
        if (!Application.isBatchMode)
            EditorUtility.DisplayDialog("SuperReImporter", summary, "OK");

        RemoveSelfFolder();
    }

    static ResetChoice AskResetChoice(List<string> existingFolders)
    {
        if (Application.isBatchMode)
            return ResetChoice.BackupAndReplace;

        const int maxListed = 12;
        var listed = new List<string>();
        for (int i = 0; i < existingFolders.Count && i < maxListed; i++)
            listed.Add("・" + existingFolders[i]);
        if (existingFolders.Count > maxListed)
            listed.Add("ほか " + (existingFolders.Count - maxListed) + " 件");

        var message =
            "パッケージの更新のため、次のフォルダを一度取り除いてから入れ直します。\n" +
            "フォルダ内で編集・追加したファイルも取り除かれます（削除はせず、プロジェクトの " +
            BackupRootRelative + " に退避します）。\n\n" +
            string.Join("\n", listed.ToArray());

        var result = EditorUtility.DisplayDialogComplex(
            "SuperReImporter",
            message,
            "退避して入れ替える",
            "キャンセル",
            "取り除かずに上書き");
        switch (result)
        {
            case 0: return ResetChoice.BackupAndReplace;
            case 2: return ResetChoice.OverwriteWithoutRemoving;
            default: return ResetChoice.Cancel;
        }
    }

    static void CancelByUser(string stamp, string payloadFull)
    {
        EditorPrefs.SetString(Pref("Stamp"), stamp);
        EditorPrefs.SetString(Pref("Phase"), PhaseDone);
        EditorPrefs.SetBool(Pref("Launched"), false);

        string saved = null;
        try
        {
            saved = SavePayloadCopy(CreateBackupRoot(), payloadFull);
        }
        catch (Exception e)
        {
            LogWarning("Payload の保存に失敗: " + e.Message);
        }

        Log("キャンセルしました。既存フォルダは変更していません。" +
            (string.IsNullOrEmpty(saved) ? "" : " 後で更新する場合はこのファイルを手動でインポートしてください: " + saved));
        RemoveSelfFolder();
        if (!ReimporterFolderExists())
            EditorPrefs.DeleteKey(Pref("Stamp"));
    }

    static void RemoveSelfFolder()
    {
        try
        {
            if (!ReimporterFolderExists())
                return;
            Log("SuperReImporter を削除します: " + ReimporterAssetPath);
            ForceDeleteAsset(ReimporterAssetPath);
            AssetDatabase.Refresh();
            if (ReimporterFolderExists())
                LogWarning("SuperReImporter フォルダが残っています。手動で削除してください: " + ReimporterAssetPath);
        }
        catch (Exception e)
        {
            LogWarning("SuperReImporter の削除に失敗: " + e.Message);
        }
    }

    static void DeleteTempPayload()
    {
        var tempPayload = EditorPrefs.GetString(Pref("Payload"), "");
        if (string.IsNullOrEmpty(tempPayload) || !File.Exists(tempPayload))
            return;
        try
        {
            File.Delete(tempPayload);
        }
        catch (Exception e)
        {
            LogWarning("一時ファイルの削除に失敗: " + e.Message);
        }
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

            DeleteTempPayload();
            var backupRoot = EditorPrefs.GetString(Pref("Backup"), "");
            EditorPrefs.SetString(Pref("Phase"), PhaseDone);
            EditorPrefs.DeleteKey(Pref("Payload"));
            EditorPrefs.DeleteKey(Pref("Backup"));
            EditorPrefs.SetBool(Pref("Launched"), false);
            EditorPrefs.SetInt(Pref("Retries"), 0);

            if (!string.IsNullOrEmpty(backupRoot) && Directory.Exists(backupRoot))
                Log("更新前のフォルダは退避先に残しています。不要なら削除してください: " + backupRoot);

            RemoveSelfFolder();
            if (!ReimporterFolderExists())
            {
                // スタンプを消す。同じ更新時刻のまま再インポートされても、次回は新しい導入として扱う。
                EditorPrefs.DeleteKey(Pref("Stamp"));
                Log("完了しました。パッケージを再インポートしました。");
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

    /// <summary>
    /// 対象フォルダを退避先へ移す。失敗時は例外をそのまま投げ、呼び出し側（MarkFailed）で元に戻す。
    /// </summary>
    static void MoveExportFolderToBackup(string assetFolder, string backupRoot)
    {
        var folder = (assetFolder ?? "").Replace("\\", "/").TrimEnd('/');
        if (string.IsNullOrEmpty(folder) || folder == "Assets")
        {
            LogWarning("Assets ルートは退避しません。");
            return;
        }
        if (!folder.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            return;
        if (IsProtectedAssetPath(folder))
        {
            Log("インポーターのため退避しません: " + folder);
            return;
        }

        var keeps = new List<string>();
        AddKeepIfInside(keeps, folder, ReimporterAssetPath);
        AddKeepIfInside(keeps, folder, AvatarInstallerAssetPath);
        AddKeepIfInside(keeps, folder, BoothManagerInstallerAssetPath);

        if (keeps.Count > 0)
        {
            Log("指定フォルダのうち、インポーターは残して退避します: " + folder);
            BackupTreeExcept(ToAbsolute(folder), keeps, backupRoot);
        }
        else
        {
            MovePathAndMetaToBackup(ToAbsolute(folder), backupRoot);
        }
    }

    static void AddKeepIfInside(List<string> keeps, string parentAssetPath, string keepAssetPath)
    {
        if (keepAssetPath.StartsWith(parentAssetPath + "/", StringComparison.OrdinalIgnoreCase))
            keeps.Add(ToAbsolute(keepAssetPath));
    }

    /// <summary>
    /// directory 以下を退避する。keeps とその先祖は残す。
    /// </summary>
    static void BackupTreeExcept(string directoryFullPath, List<string> keepFullPaths, string backupRoot)
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
                BackupTreeExceptEntry(entry, keepFullPaths, backupRoot);
            }
            return;
        }

        MovePathAndMetaToBackup(directoryFullPath, backupRoot);
    }

    static void BackupTreeExceptEntry(string entryFullPath, List<string> keepFullPaths, string backupRoot)
    {
        if (IsUnderAny(entryFullPath, keepFullPaths))
            return;

        if (Directory.Exists(entryFullPath) && ContainsAnyKeep(entryFullPath, keepFullPaths))
        {
            BackupTreeExcept(entryFullPath, keepFullPaths, backupRoot);
            return;
        }

        MovePathAndMetaToBackup(entryFullPath, backupRoot);
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

    static string ProjectRoot()
    {
        return Path.GetFullPath(Path.GetDirectoryName(Application.dataPath));
    }

    static string ToProjectRelative(string fullPath)
    {
        var root = ProjectRoot().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                   Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(fullPath);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("プロジェクト外のパスは退避できません: " + full);
        return full.Substring(root.Length).Replace("\\", "/");
    }

    static string FromProjectRelative(string baseDirectory, string relative)
    {
        return Path.Combine(baseDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    static string CreateBackupRoot()
    {
        var parent = FromProjectRelative(ProjectRoot(), BackupRootRelative);
        Directory.CreateDirectory(parent);
        PruneOldBackups(parent);

        var name = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var root = Path.Combine(parent, name);
        for (int i = 1; Directory.Exists(root); i++)
            root = Path.Combine(parent, name + "_" + i);
        Directory.CreateDirectory(root);
        return root;
    }

    static void PruneOldBackups(string parent)
    {
        try
        {
            var dirs = Directory.GetDirectories(parent);
            if (dirs.Length < MaxBackupsToKeep)
                return;
            Array.Sort(dirs, StringComparer.Ordinal);
            for (int i = 0; i <= dirs.Length - MaxBackupsToKeep; i++)
                Directory.Delete(dirs[i], true);
        }
        catch (Exception e)
        {
            LogWarning("古い退避フォルダの削除に失敗: " + e.Message);
        }
    }

    static void MovePathAndMetaToBackup(string fullPath, string backupRoot)
    {
        if (!Directory.Exists(fullPath) && !File.Exists(fullPath))
            return;

        var relative = ToProjectRelative(fullPath);
        var dest = FromProjectRelative(backupRoot, relative);
        var destParent = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(destParent))
            Directory.CreateDirectory(destParent);

        // 移動途中で失敗しても復元対象に含まれるよう、先に記録する。
        File.AppendAllText(Path.Combine(backupRoot, BackupManifestFileName), relative + "\n");
        MovePath(fullPath, dest);

        var meta = fullPath + ".meta";
        if (File.Exists(meta))
            MovePath(meta, dest + ".meta");
        Log("退避しました: " + relative);
    }

    /// <summary>
    /// 退避した項目を元の場所へ戻す。元の場所に既に何かある項目は戻さず、その一覧を返す。
    /// </summary>
    static List<string> RestoreBackup(string backupRoot)
    {
        var notRestored = new List<string>();
        var manifest = Path.Combine(backupRoot, BackupManifestFileName);
        if (!File.Exists(manifest))
            return notRestored;

        var lines = File.ReadAllLines(manifest);
        var restoredAny = false;
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var relative = (lines[i] ?? "").Trim();
            if (string.IsNullOrEmpty(relative))
                continue;

            var backup = FromProjectRelative(backupRoot, relative);
            if (!Directory.Exists(backup) && !File.Exists(backup))
                continue;

            var original = FromProjectRelative(ProjectRoot(), relative);
            if (Directory.Exists(original) || File.Exists(original))
            {
                notRestored.Add(relative);
                continue;
            }

            var parent = Path.GetDirectoryName(original);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            MovePath(backup, original);
            if (File.Exists(backup + ".meta") && !File.Exists(original + ".meta"))
                MovePath(backup + ".meta", original + ".meta");
            restoredAny = true;
            Log("元に戻しました: " + relative);
        }

        if (restoredAny)
            AssetDatabase.Refresh();
        return notRestored;
    }

    static string SavePayloadCopy(string backupRoot, string payloadFullPath)
    {
        var dest = Path.Combine(backupRoot, PayloadFileName);
        File.Copy(payloadFullPath, dest, true);
        return dest;
    }

    static void MovePath(string source, string destination)
    {
        if (Directory.Exists(source))
        {
            try
            {
                Directory.Move(source, destination);
            }
            catch (IOException)
            {
                // 別ボリューム（シンボリックリンク先など）では Move できないのでコピーしてから消す。
                CopyDirectory(source, destination);
                Directory.Delete(source, true);
            }
            return;
        }

        if (File.Exists(destination))
            File.Delete(destination);
        File.Move(source, destination);
    }

    static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
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
