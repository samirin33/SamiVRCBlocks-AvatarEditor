/*
 * This is a part of https://github.com/anatawa12/VPMPackageAutoInstaller.
 * 
 * MIT License
 * 
 * Copyright (c) 2022 anatawa12
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Anatawa12.SimpleJson;
using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;
using VrcGet = Anatawa12.VrcGet;

[assembly: InternalsVisibleTo("com.anatawa12.vpm-package-auto-installer.tester")]

namespace Anatawa12.VpmPackageAutoInstaller
{
    public static class VpmPackageAutoInstaller
    {
        /// <summary>
        /// unitypackage を展開せず、config.json 文字列から VPM の確認と更新を行うときの結果。
        /// </summary>
        public enum InstallOutcome
        {
            Updated,
            AlreadySatisfied,
            Failed,
            Cancelled
        }

        static bool _promptUser;

        static bool ShouldPrompt() => _promptUser && !IsNoPrompt();

        private enum Progress
        {
            LoadingConfigJson,
            LoadingVpmManifestJson,
            LoadingGlobalSettingsJson,
            DownloadingRepositories,
            DownloadingNewRepositories,
            ResolvingDependencies,
            Prompting,
            SavingRemoteRepositories,
            DownloadingAndExtractingPackages,
            SavingConfigChanges,
            RefreshingUnityPackageManger,
            Finish
        }

        private static void ShowProgress(string message, Progress progress)
        {
            var ratio = (float)progress / (float)Progress.Finish;
            EditorUtility.DisplayProgressBar("VPAI Installer", message, ratio);
        }


        private static bool IsNoPrompt()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup buildTargetGroup = BuildPipeline.GetBuildTargetGroup(target);
            return PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup)
                .Contains("VPM_PACKAGE_AUTO_INSTALLER_NO_PROMPT");
        }

        /// <summary>
        /// config.json の JSON 文字列を直接渡して、リポジトリ追加とパッケージの確認・更新を行う。
        /// promptUser が false のときは、インストール確認と「更新なし」を含むダイアログを出さない。
        /// </summary>
        public static InstallOutcome Run(string configJson, bool promptUser)
        {
            _promptUser = promptUser;
            EditorUtility.DisplayProgressBar("VPAI Installer", "Starting Installer...", 0.0f);
            try
            {
                return DoInstallImpl(configJson).Execute();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                if (ShouldPrompt())
                    EditorUtility.DisplayDialog("ERROR", "Error installing packages", "ok");
                return InstallOutcome.Failed;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static async SyncedTask<InstallOutcome> DoInstallImpl(string configJsonText)
        {
            ShowProgress("Loading VPAI config...", Progress.LoadingConfigJson);
            if (string.IsNullOrEmpty(configJsonText))
            {
                const string message = "config.json not found. installing package failed.";
                if (ShouldPrompt())
                    EditorUtility.DisplayDialog("ERROR", message, "ok");
                Debug.LogError(message);
                return InstallOutcome.Failed;
            }

            var config =
                new VpaiConfig(new JsonParser(configJsonText).Parse(JsonType.Obj));

            var unityVersion = VrcGet.UnityVersion.parse(Application.unityVersion);
            if (unityVersion != null && config.minimumUnity != null)
            {
                var minUnity = new VrcGet.UnityVersion(config.minimumUnity.major, config.minimumUnity.minor, 0,
                    VrcGet.ReleaseType.Alpha, 0);
                if (unityVersion < minUnity)
                {
                    var message = MinimumUnityVersionMessage(config.minimumUnity.major, config.minimumUnity.minor);
                    if (ShouldPrompt())
                        EditorUtility.DisplayDialog("ERROR", message, "OK");
                    Debug.LogError(message);
                    return InstallOutcome.Failed;
                }
            }

            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent",
                "VpmPackageAutoInstaller/0.3 (github:anatawa12/VpmPackageAutoInstaller) " +
                "vrc-get/0.1.10 (github:anatawa12/vrc-get, VPAI is based on vrc-get but reimplemented in C#)");
            ShowProgress("Loading Packages/vpm-manifest.json...", Progress.LoadingVpmManifestJson);
            var env = await VrcGet.Environment.load_default(client);
            ShowProgress("Loading settings.json...", Progress.LoadingGlobalSettingsJson);
            var unityProject = await VrcGet.UnityProject.find_unity_project(new VrcGet.Path(Directory.GetCurrentDirectory()));
            ShowProgress("Downloading package information...", Progress.DownloadingRepositories);
            await env.load_package_infos(true);

            ShowProgress("Downloading new repositories...", Progress.DownloadingNewRepositories);
            await Task.WhenAll(config.vpmRepositories.Select(repoUrl => env.AddPendingRepository(repoUrl.url, null, repoUrl.headers)));

            ShowProgress("Resolving dependencies...", Progress.ResolvingDependencies);
            var includePrerelease = config.includePrerelease;

            var unityIncompatibles = new Dictionary<string, VrcGet.VersionRange>();
            VrcGet.PartialUnityVersion min_unity_version = null;
            var dependencies = config.VpmDependencies.SelectMany(kvp =>
            {
                var exact = Version.TryParse(kvp.Value.ToString(), out _);
                var package = env.find_package_by_name(kvp.Key, VrcGet.PackageSelector.range_for_pre(unityProject.unity_version, kvp.Value, includePrerelease));
                if (package == null)
                {
                    var withoutUnity = env.find_package_by_name(kvp.Key, VrcGet.PackageSelector.range_for_pre(null, kvp.Value, includePrerelease));
                    if (withoutUnity == null)
                        throw new Exception($"package not found: {kvp.Key} version {kvp.Value}");
                    unityIncompatibles[kvp.Key] = kvp.Value;
                    return Array.Empty<(VrcGet.PackageInfo, bool)>();
                }
                return new[] { (package.Value, exact) };
            }).ToList();

            if (unityIncompatibles.Count != 0)
            {
                var message = UnityIncompatibleMessage(env, unityIncompatibles, includePrerelease);
                // we found some package requires newer version of unity
                if (ShouldPrompt())
                    EditorUtility.DisplayDialog("ERROR!", message, "OK");
                Debug.LogError(message);
                return InstallOutcome.Failed;
            }

            VrcGet.AddPackageRequest request;
            try
            {
                request = await unityProject.add_package_request(env, dependencies, true, includePrerelease);
            }
            catch (VrcGet.VrcGetException e)
            {
                if (ShouldPrompt())
                    EditorUtility.DisplayDialog("ERROR!",
                        "Installing package failed due to conflicts:\n" +
                        e.Message + "\n\n" +
                        "Please see console for more details", "OK");
                Debug.LogException(e);
                return InstallOutcome.Failed;
            }

            if (request.unity_conflicts().Length != 0)
            {
                var confirmMessage = new StringBuilder("The following packages are incompatible with current unity!\n");
                foreach (var conflict in request.unity_conflicts())
                    confirmMessage.Append("- ").Append(conflict).Append("\n");
                confirmMessage.Append("\nPlease change unity version before installing packages!");
                var message = confirmMessage.ToString();
                // we found some package requires newer version of unity
                if (ShouldPrompt())
                    EditorUtility.DisplayDialog("ERROR!", message, "OK");
                Debug.LogError(message);
                return InstallOutcome.Failed;
            }

            if (request.locked().Count == 0)
            {
                if (ShouldPrompt() && !config.silentIfInstalled)
                    EditorUtility.DisplayDialog("Nothing TO DO!", "All Packages are Installed!", "OK");
                return InstallOutcome.AlreadySatisfied;
            }

            ShowProgress("Prompting to user...", Progress.Prompting);
            if (ShouldPrompt())
            {
                var confirmMessage = new StringBuilder("You're installing the following packages:");

                foreach (var (name, version) in 
                         request.locked().Select(x => (Name: x.name(), Version: x.version()))
                             .Concat(request.dependencies().Select(x => (Name: x.name, Version: x.dep.version.as_single_version())))
                             .Distinct())
                    confirmMessage.Append('\n').Append(name).Append(" version ").Append(version);

                if (env.PendingRepositories.Count != 0)
                {
                    confirmMessage.Append("\n\nThis will add following repositories:");
                    foreach (var (_, url) in env.PendingRepositories)
                        // ReSharper disable once PossibleNullReferenceException
                        confirmMessage.Append('\n').Append(url);
                }

                if (request.legacy_folders().Count != 0 || request.legacy_files().Count != 0)
                {
                    confirmMessage.Append("\n\nYou're also deleting the following files/folders:");
                    foreach (var path in request.legacy_folders().Concat(request.legacy_files()))
                        confirmMessage.Append('\n').Append(path);
                }

                if (request.legacy_packages().Count != 0)
                {
                    confirmMessage.Append("\n\nYou're also deleting the following legacy Packages:");
                    foreach (var name in request.legacy_packages())
                        confirmMessage.Append("\n- ").Append(name);
                }

                if (!EditorUtility.DisplayDialog("Confirm", confirmMessage.ToString(), "Install", "Cancel"))
                    return InstallOutcome.Cancelled;
            }

            // user confirm got. now, edit settings

            ShowProgress("Saving remote repositories...", Progress.SavingRemoteRepositories);
            await env.SavePendingRepositories();

            ShowProgress("Downloading & Extracting packages...", Progress.DownloadingAndExtractingPackages);

            await unityProject.do_add_package_request(env, request);

            ShowProgress("Saving config changes...", Progress.SavingConfigChanges);
            await unityProject.save();
            await env.save();

            ShowProgress("Refreshing Unity Package Manager...", Progress.RefreshingUnityPackageManger);
            ResolveUnityPackageManger();

            ShowProgress("Almost done!", Progress.Finish);
            return InstallOutcome.Updated;
        }

        private static string UnityIncompatibleMessage(VrcGet.Environment env,
            Dictionary<string, VrcGet.VersionRange> unityIncompatibles, bool includePrerelease)
        {
            if (unityIncompatibles.ContainsKey("com.vrchat.avatars")
                || unityIncompatibles.ContainsKey("com.vrchat.worlds")
                || unityIncompatibles.ContainsKey("com.vrchat.base")
                || unityIncompatibles.ContainsKey("com.vrchat.core.vpm-resolver"))
            {
                // VRCSDK has both upper and lower limit. it might hit upper limit
                if (!Application.unityVersion.StartsWith("2019.", StringComparison.Ordinal))
                {
                    // it's not unity 2019 so the tool looks requires 2019.x SDK but not 2019.x
                    return "You're installing tools that require VRCSDK for Unity 2019.x " +
                           "but you're using other versions of VRCSDK!\n" +
                           "Please use older VRCSDK or wait for tool updates!";
                }
            }

            // otherwise, Upgrade Unity is required.
            var minimumVersion = unityIncompatibles
                .Select(p => MinimumUnityVersionForPackage(p.Key, p.Value))
                .Max();

            return MinimumUnityVersionMessage(minimumVersion.major, minimumVersion.minor);

            VrcGet.PartialUnityVersion MinimumUnityVersionForPackage(string package, VrcGet.VersionRange range) =>
                env.find_packages(package)
                    .Where(x => range.match_pre(x.version(), includePrerelease))
                    .Where(x => x.unity() != null)
                    .Select(x => x.unity())
                    .Min();
        }

        private static string MinimumUnityVersionMessage(ushort major, byte minor) =>
            $"You're installing packages requires unity {major}.{minor} or later! \n" +
            $"Please upgrade your unity to {major}.{minor} or later!";

        internal static void ResolveUnityPackageManger()
        {
            var method = typeof(UnityEditor.PackageManager.Client).GetMethod(
                name: "Resolve",
                bindingAttr: System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic |
                             System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            if (method != null)
                method.Invoke(null, null);
        }

    }

    static class Extensions
    {
        public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> self, out TKey key,
            out TValue value) => (key, value) = (self.Key, self.Value);
    }

    sealed class VpaiConfig
    {
        [NotNull] public readonly VpaiRepository[] vpmRepositories;
        public readonly bool includePrerelease;
        public readonly bool silentIfInstalled;
        public readonly VrcGet.PartialUnityVersion minimumUnity;
        [NotNull] public readonly Dictionary<string, VrcGet.VersionRange> VpmDependencies;

        public VpaiConfig(JsonObj json)
        {
            vpmRepositories = json.Get("vpmRepositories", JsonType.List, true)?.Select(v => new VpaiRepository(v))?.ToArray() ?? Array.Empty<VpaiRepository>();
            includePrerelease = json.Get("includePrerelease", JsonType.Bool, true);
            silentIfInstalled = json.Get("silentIfInstalled", JsonType.Bool, true);
            VpmDependencies = json.Get("vpmDependencies", JsonType.Obj, true)
                                  ?.ToDictionary(x => x.Item1, x => VrcGet.VersionRange.Parse((string)x.Item2))
                              ?? new Dictionary<string, VrcGet.VersionRange>();
            var unityVersion = json.Get("minimumUnity", JsonType.String, true);
            minimumUnity = unityVersion != null
                ? VrcGet.PartialUnityVersion.parse(unityVersion) ?? throw new Exception("invalid minimumUnity")
                : null;
        }
    }

    sealed class VpaiRepository
    {
        public readonly string url;
        public readonly Dictionary<string, string> headers;

        public VpaiRepository(object obj)
        {
            if (obj is string s)
            {
                url = s;
                headers = new Dictionary<string, string>();
            }
            else if (obj is JsonObj json)
            {
                url = json.Get("url", JsonType.String);
                headers = json.Get("vpmDependencies", JsonType.Obj, true)
                              ?.ToDictionary(x => x.Item1, x => (string)x.Item2)
                          ?? new Dictionary<string, string>();
            }
        }
    }
}
