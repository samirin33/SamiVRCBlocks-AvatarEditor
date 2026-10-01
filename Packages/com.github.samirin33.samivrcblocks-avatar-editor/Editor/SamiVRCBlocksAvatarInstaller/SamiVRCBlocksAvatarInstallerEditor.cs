using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;

/*
 * VPM の確認・更新は VPMPackageAutoInstaller を利用しています。
 * Copyright (c) 2022 anatawa12
 * https://github.com/anatawa12/VPMPackageAutoInstaller
 * MIT License。全文と関連クレジットは ThirdParty/VPMPackageAutoInstaller/LICENSE と NOTICE.txt。
 */
public class SamiVRCBlocksAvatarInstallerEditor
{
    private const string LogPrefix = "[SamiVRCBlocksAvatar][Installer]";
    private const string InstallerFolderName = "SamiVRCBlocksAvatarInstaller";
    private const string LegacyInstallerFolderName = "AvatarInstaller";
    private const string VpaiConfigFileName = "vpai-config.json";

    private static readonly string[] InstallerFolderNames =
    {
        InstallerFolderName,
        LegacyInstallerFolderName
    };

    private static bool _isRunning;

    private static string HookDonePrefsKey =>
        "SamiVRCBlocksAvatar.Installer.HookDone." + Application.dataPath;

    [InitializeOnLoadMethod]
    private static void OnEditorLoad()
    {
        EditorApplication.delayCall += OnEditorLoadDelayed;
    }

    private static void OnEditorLoadDelayed()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (IsHookDone())
        {
            if (InstallerFolderExists())
                ScheduleDeleteInstallerFolder(3);
            else
                SetHookDone(false);
            return;
        }

        TryAutoStart();
    }

    /// <summary>
    /// Assets 配下にインストーラが残っていれば VPM の確認・更新を始める。
    /// Packages 原本は defineConstraints で未コンパイルのため、ここに到達しない。
    /// </summary>
    private static void TryAutoStart()
    {
        if (_isRunning || IsHookDone())
            return;
        if (!InstallerFolderExists())
            return;

        _isRunning = true;
        Log("Installer フォルダを検出 → VPM の確認・更新を開始");
        EditorApplication.delayCall += () => RunWhenEditorIdle(30);
    }

    private static void Log(string message) => Debug.Log($"{LogPrefix} {message}");
    private static void LogWarning(string message) => Debug.LogWarning($"{LogPrefix} {message}");
    private static void LogError(string message) => Debug.LogError($"{LogPrefix} {message}");

    private static void SetHookDone(bool done)
    {
        if (done)
            EditorPrefs.SetBool(HookDonePrefsKey, true);
        else
            EditorPrefs.DeleteKey(HookDonePrefsKey);
    }

    private static bool IsHookDone() => EditorPrefs.GetBool(HookDonePrefsKey, false);

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

    private static string ReadVpaiConfig()
    {
        foreach (var folderName in InstallerFolderNames)
        {
            var path = Path.Combine(Application.dataPath, folderName, VpaiConfigFileName);
            if (!File.Exists(path))
                continue;
            return File.ReadAllText(path);
        }

        return null;
    }

    private static void RunWhenEditorIdle(int retriesLeft)
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            if (retriesLeft > 0)
                EditorApplication.delayCall += () => RunWhenEditorIdle(retriesLeft - 1);
            else
            {
                LogWarning("エディタの更新が終わらないため VPM 確認を中断しました。");
                _isRunning = false;
            }
            return;
        }

        RunVpmCheck();
    }

    private static void RunVpmCheck()
    {
        try
        {
            var configJson = ReadVpaiConfig();
            if (string.IsNullOrEmpty(configJson))
            {
                LogError($"{VpaiConfigFileName} が見つからないため VPM の確認を中止しました。");
                return;
            }

            // promptUser: false — インストール確認と、更新がなかったときのダイアログは出さない。
            var outcome = Anatawa12.VpmPackageAutoInstaller.VpmPackageAutoInstaller.Run(configJson, false);
            Log($"VPM 確認結果: {outcome}");

            if (outcome != Anatawa12.VpmPackageAutoInstaller.VpmPackageAutoInstaller.InstallOutcome.Updated &&
                outcome != Anatawa12.VpmPackageAutoInstaller.VpmPackageAutoInstaller.InstallOutcome.AlreadySatisfied)
            {
                LogError("VPM の更新に失敗したため Installer フォルダは残します。");
                return;
            }

            SetHookDone(true);
            OnVpmCheckCompleted(outcome);
            ScheduleDeleteInstallerFolder(3);
        }
        catch (System.Exception ex)
        {
            LogError($"VPM の確認に失敗しました: {ex.Message}");
        }
        finally
        {
            _isRunning = false;
        }
    }

    /// <summary>
    /// VPM の確認・更新が終わったあと（更新あり・更新なしの両方）に呼ばれる。
    /// ダイアログは出さない。独自処理はここへ追加する。
    /// </summary>
    private static void OnVpmCheckCompleted(
        Anatawa12.VpmPackageAutoInstaller.VpmPackageAutoInstaller.InstallOutcome outcome)
    {
    }

    private static void ScheduleDeleteInstallerFolder(int framesLeft)
    {
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

                Directory.Delete(fullPath, true);
                var metaPath = fullPath + ".meta";
                if (File.Exists(metaPath))
                    File.Delete(metaPath);
            }

            AssetDatabase.Refresh();

            if (!InstallerFolderExists())
            {
                SetHookDone(false);
                Log("Installer フォルダ削除完了");
            }
            else
                LogWarning("Installer フォルダが残っています");
        }
        catch (System.Exception ex)
        {
            LogWarning($"Installer フォルダの削除に失敗しました: {ex.Message}");
        }
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

                if (_isRunning || IsHookDone())
                    break;

                _isRunning = true;
                EditorApplication.delayCall += () => RunWhenEditorIdle(30);
                break;
            }
        }
    }
}
#endif
