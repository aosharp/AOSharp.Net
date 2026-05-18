using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Timers;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Newtonsoft.Json;
using AOSharp.Data;
using AOSharp.Models;
using AOSharp.Services;
using AOSharp.Tweaks;
using Serilog;

namespace AOSharp
{
    /// <summary>
    /// Mediates all communication between the WebView2 React UI and the C# backend.
    /// Owns the ProfilesModel timer and serialises app state as JSON pushed to React.
    /// </summary>
    public class WebMessageBridge
    {
        private readonly Microsoft.Web.WebView2.Wpf.WebView2 _webView;
        private readonly Config _config;
        private readonly ProfilesModel _profilesModel;
        private readonly RepoCompiler _repoCompiler;
        private readonly Dispatcher _dispatcher;

        private Profile _activeProfile;
        private bool _isCompiling;
        private readonly List<InjectJob> _injectJobs = new List<InjectJob>();
        private readonly object _injectJobsLock = new object();
        private readonly System.Threading.SemaphoreSlim _manifestDependencyLock =
            new System.Threading.SemaphoreSlim(1, 1);
        private Timer _updateCheckTimer;
        private readonly HashSet<string> _previouslyActiveProfileNames = new HashSet<string>();

        private sealed class InjectJob
        {
            public string ProfileId { get; set; }
            public string ProfileName { get; set; }
            public string Status { get; set; }
            public string Message { get; set; }
        }

        public WebMessageBridge(
            Microsoft.Web.WebView2.Wpf.WebView2 webView,
            Config config,
            ProfilesModel profilesModel,
            RepoCompiler repoCompiler,
            Dispatcher dispatcher)
        {
            _webView = webView;
            _config = config;
            _profilesModel = profilesModel;
            _repoCompiler = repoCompiler;
            _dispatcher = dispatcher;

            _repoCompiler.Progress += OnCompileProgress;

            profilesModel.ProfilesRefreshed += (_, _) => OnProfilesRefreshed();

            // Push state whenever plugins collection changes
            _config.Plugins.CollectionChanged += (_, _) =>
            {
                _config.Save();
                SendState();
            };

            // Background update checks: once at startup, then every 5 min
            _updateCheckTimer = new Timer(TimeSpan.FromMinutes(5).TotalMilliseconds);
            _updateCheckTimer.Elapsed += async (_, _) => await HandleCheckUpdatesAsync();
            _updateCheckTimer.AutoReset = true;
            _updateCheckTimer.Start();
            _ = HandleCheckUpdatesAsync();

            // Populate local commit hashes immediately (no network) so Commit column is populated before fetch completes
            _ = Task.Run(() => InitializeLocalCommits());
        }

        // ── Inbound (React → C#) ────────────────────────────────────────────

        public void OnMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string raw;
            try { raw = e.TryGetWebMessageAsString(); }
            catch { raw = e.WebMessageAsJson; }

            BridgeMessage msg;
            try { msg = JsonConvert.DeserializeObject<BridgeMessage>(raw); }
            catch (Exception ex)
            {
                Log.Warning($"[Bridge] Failed to parse message: {ex.Message}");
                return;
            }

            _ = DispatchAsync(msg);
        }

        private async Task DispatchAsync(BridgeMessage msg)
        {
            try
            {
                switch (msg.Type)
                {
                    case "getState":
                        SendState();
                        break;

                    case "selectProfile":
                        _activeProfile = _profilesModel.Profiles
                            .FirstOrDefault(p => p.Name == msg.ProfileId);
                        ApplyActiveLoadoutHighlight();
                        SendState();
                        break;

                    case "inject":
                        if (_activeProfile != null)
                            _ = TryInjectProfileAsync(_activeProfile);
                        break;

                    case "eject":
                        HandleEject();
                        break;

                    case "compileAll":
                        await HandleCompileAllAsync();
                        break;

                    case "compilePlugin":
                        await HandleCompilePluginAsync(msg.Key);
                        break;

                    case "updatePlugin":
                        await HandleUpdatePluginAsync(msg.Key, msg.TrustRepo);
                        break;

                    case "checkUpdates":
                        await HandleCheckUpdatesAsync();
                        break;

                    case "addDllPlugin":
                        HandleAddDllPlugin(msg.Path);
                        break;

                    case "fetchRepoCsprojs":
                        await HandleFetchRepoCsprojsAsync(msg.Url);
                        break;

                    case "addRepoPlugin":
                        await HandleAddRepoPluginAsync(msg.Url, msg.ProjectFilePath);
                        break;

                    case "removePlugin":
                        HandleRemovePlugin(msg.Key);
                        break;

                    case "openUrl":
                        _dispatcher.Invoke(() => HandleOpenUrl(msg.Url));
                        break;

                    case "openLogFile":
                        _dispatcher.Invoke(HandleOpenLogFile);
                        break;

                    case "assignLoadout":
                        HandleAssignLoadout(msg.ProfileId, msg.LoadoutId);
                        break;

                    case "createLoadout":
                        HandleCreateLoadout(msg.Name, msg.PluginKeys, msg.SourceLoadoutId);
                        break;

                    case "updateLoadout":
                        HandleUpdateLoadout(msg.LoadoutId, msg.Name, msg.PluginKeys);
                        break;

                    case "deleteLoadout":
                        HandleDeleteLoadout(msg.LoadoutId);
                        break;

                    case "duplicateLoadout":
                        HandleDuplicateLoadout(msg.LoadoutId, msg.Name);
                        break;

                    case "setAutoInject":
                        HandleSetAutoInject(msg.Enabled);
                        break;

                    case "browseDll":
                        _dispatcher.Invoke(() => HandleBrowseDll());
                        break;

                    case "browseDirectory":
                        _dispatcher.Invoke(() => HandleBrowseDirectory());
                        break;

                    case "browseCsproj":
                        _dispatcher.Invoke(() => HandleBrowseCsproj());
                        break;

                    case "enableLargeAddressAware":
                        await HandleEnableLargeAddressAwareAsync(msg.InstallDir);
                        break;

                    // React-side error reporting (window.onerror / ErrorBoundary)
                    case "toast":
                        Log.Error($"[UI] {msg.Title}: {msg.Message}");
                        break;

                    default:
                        Log.Warning($"[Bridge] Unknown message type: {msg.Type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Bridge] Error handling '{msg.Type}': {ex.Message}");
                SendToast("error", "Error", ex.Message);
            }
        }

        // ── Action handlers ──────────────────────────────────────────────────

        private void HandleOpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                Log.Warning("[Bridge] openUrl rejected (only http/https allowed)");
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error($"[Bridge] openUrl failed: {ex.Message}");
                SendToast("error", "Could not open link", ex.Message);
            }
        }

        /// <summary>
        /// Serilog daily rolling: {name}{yyyyMMdd}{ext} next to the configured template (see MainWindow logger).
        /// </summary>
        private static string GetCurrentRollingLogPath()
        {
            string template = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log.txt");
            string dir = Path.GetDirectoryName(template);
            string stem = Path.GetFileNameWithoutExtension(template);
            string ext = Path.GetExtension(template);
            return Path.Combine(string.IsNullOrEmpty(dir) ? AppDomain.CurrentDomain.BaseDirectory : dir,
                stem + DateTime.Now.ToString("yyyyMMdd") + ext);
        }

        private void HandleOpenLogFile()
        {
            string path = GetCurrentRollingLogPath();
            try
            {
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    return;
                }

                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error($"[Bridge] openLogFile failed: {ex.Message}");
                SendToast("error", "Could not open log", ex.Message);
            }
        }

        private Loadout GetLoadoutForProfile(Profile profile)
        {
            if (profile == null || string.IsNullOrEmpty(profile.LoadoutId))
                return null;
            return _config.Loadouts.FirstOrDefault(l => l.Id == profile.LoadoutId);
        }

        private IEnumerable<string> ResolveInjectPaths(Loadout loadout)
        {
            if (loadout?.PluginKeys == null)
                yield break;

            foreach (var key in loadout.PluginKeys)
            {
                if (!_config.Plugins.TryGetValue(key, out var plugin))
                    continue;
                if (plugin.IsLibrary || !plugin.IsCompiled)
                    continue;
                if (string.IsNullOrEmpty(plugin.Path))
                    continue;
                yield return plugin.Path;
            }
        }

        private bool HasUncompiledLoadoutPlugins(Loadout loadout)
        {
            if (loadout?.PluginKeys == null)
                return false;

            return loadout.PluginKeys.Any(key =>
                _config.Plugins.TryGetValue(key, out var plugin) &&
                !plugin.IsLibrary &&
                !plugin.IsCompiled);
        }

        private bool IsInjecting
        {
            get
            {
                lock (_injectJobsLock)
                    return _injectJobs.Any(j =>
                        j.Status == "pending" || j.Status == "injecting");
            }
        }

        private List<object> SnapshotInjectQueue()
        {
            lock (_injectJobsLock)
            {
                return _injectJobs
                    .Select(j => (object)new
                    {
                        profileId = j.ProfileId,
                        profileName = j.ProfileName,
                        status = j.Status,
                        message = j.Message
                    })
                    .ToList();
            }
        }

        private void SendInjectProgress()
        {
            PostMessage(new
            {
                type = "injectProgress",
                isInjecting = IsInjecting,
                queue = SnapshotInjectQueue()
            });
        }

        private bool TryEnqueueInjectJob(Profile profile, out string error)
        {
            error = null;
            if (profile == null || profile.IsInjected)
                return false;

            lock (_injectJobsLock)
            {
                if (_injectJobs.Any(j =>
                        j.ProfileId == profile.Name &&
                        (j.Status == "pending" || j.Status == "injecting")))
                    return false;

                _injectJobs.Add(new InjectJob
                {
                    ProfileId = profile.Name,
                    ProfileName = profile.Name,
                    Status = "pending",
                    Message = "Queued"
                });
            }

            SendInjectProgress();
            return true;
        }

        private void SetInjectJobStatus(string profileId, string status, string message)
        {
            lock (_injectJobsLock)
            {
                var job = _injectJobs.FirstOrDefault(j => j.ProfileId == profileId);
                if (job == null)
                    return;
                job.Status = status;
                if (message != null)
                    job.Message = message;
            }

            _dispatcher.BeginInvoke(SendInjectProgress);
        }

        private void RemoveInjectJob(string profileId)
        {
            lock (_injectJobsLock)
                _injectJobs.RemoveAll(j => j.ProfileId == profileId);

            _dispatcher.BeginInvoke(() =>
            {
                SendInjectProgress();
                SendState();
            });
        }

        private async Task<bool> TryInjectProfileAsync(Profile profile)
        {
            if (profile == null || profile.IsInjected)
                return false;

            var loadout = GetLoadoutForProfile(profile);
            if (loadout == null)
            {
                SendToast("error", "Inject", "No loadout assigned to this character.");
                return false;
            }

            if (HasUncompiledLoadoutPlugins(loadout))
            {
                SendToast("error", "Inject",
                    "One or more plugins in this loadout are not compiled. Compile them first.");
                return false;
            }

            var pluginPaths = ResolveInjectPaths(loadout).ToList();
            if (!TryEnqueueInjectJob(profile, out _))
                return false;

            _ = RunInjectJobAsync(profile, pluginPaths);
            return true;
        }

        private async Task RunInjectJobAsync(Profile profile, List<string> pluginPaths)
        {
            var profileId = profile.Name;
            try
            {
                SetInjectJobStatus(profileId, "injecting", "Preparing…");

                bool sdkOk = await Task.Run(RepoCompiler.ApplyPendingBootstrapSdkUpdates);
                if (!sdkOk)
                {
                    SetInjectJobStatus(profileId, "failed",
                        "AOSharp.SDK update could not be applied. Restart the loader.");
                    SendToast("error", "Inject",
                        "Updated AOSharp.SDK files are in Plugins\\AOSharp.Bootstrap\\.update-staging but could not be applied. Close any other AOSharp instance and try again, or restart the loader.");
                    return;
                }

                SetInjectJobStatus(profileId, "injecting", "Injecting bootstrap…");
                bool ok = await Task.Run(() => profile.Inject(pluginPaths));

                if (!ok)
                {
                    SetInjectJobStatus(profileId, "failed", "Injection failed");
                    SendToast("error", "Inject", $"Failed to inject {profile.Name}.");
                    return;
                }

                _dispatcher.Invoke(() =>
                {
                    EnsureProfileInConfig(profile);
                    _config.Save();
                    profile.PropertyChanged -= OnProfilePropertyChanged;
                    profile.PropertyChanged += OnProfilePropertyChanged;
                    SendState();
                });

                SetInjectJobStatus(profileId, "succeeded", "Injected");
            }
            catch (Exception ex)
            {
                Log.Error($"[Bridge] Inject failed for {profileId}: {ex.Message}");
                SetInjectJobStatus(profileId, "failed", ex.Message);
                SendToast("error", "Inject", ex.Message);
            }
            finally
            {
                await Task.Delay(400);
                RemoveInjectJob(profileId);
            }
        }

        private void OnProfilePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Profile.IsInjected))
                SendState();
        }

        private void EnsureProfileInConfig(Profile profile)
        {
            if (profile == null)
                return;

            if (!_config.Profiles.Any(p => p.Name == profile.Name))
                _config.Profiles.Add(profile);
        }

        private void HandleEject()
        {
            _activeProfile?.Eject();
            SendState();
            _ = Task.Run(async () =>
            {
                await Task.Delay(750);
                RepoCompiler.ApplyPendingBootstrapSdkUpdates();
            });
        }

        private async Task HandleCompileAllAsync()
        {
            if (_isCompiling) return;
            _isCompiling = true;
            SendState();

            try
            {
                var toCompile = _config.Plugins
                    .Where(p => p.Value.PluginType == PluginType.Repo && !RepoCompiler.HasCompiledOutput(p.Value))
                    .ToList();

                if (toCompile.Count == 0)
                {
                    SendToast("info", "Compile Plugins", "All plugins are already compiled.");
                    return;
                }

                var consumers = toCompile
                    .Where(p => !p.Value.IsManifestDependency)
                    .Select(p => p.Value)
                    .ToList();
                await EnsureManifestDependenciesForConsumersAsync(consumers, pullFirst: false);

                toCompile = _config.Plugins
                    .Where(p => p.Value.PluginType == PluginType.Repo && !RepoCompiler.HasCompiledOutput(p.Value))
                    .ToList();

                if (toCompile.Count == 0)
                {
                    SendToast("info", "Compile Plugins", "All plugins are already compiled.");
                    return;
                }

                var result = await _repoCompiler.CompileAll(
                    toCompile,
                    pullFirst: false,
                    onGroupComplete: partial => _dispatcher.Invoke(() => ApplyCompileResult(partial)),
                    precompiledLibraries: _config.GetCompiledLibraryPaths(),
                    forceRebuild: false);

                if (result.AllSucceeded)
                {
                    if (RepoCompiler.HasPendingBootstrapSdkUpdates())
                        SendToast("info", "Compile Plugins",
                            "Plugins compiled. Some AOSharp.SDK files could not update live — restart the loader to apply (.update-staging).");
                    else
                        SendToast("info", "Compile Plugins", "All plugins compiled successfully.");
                }
                else
                    SendToast("error", "Compile Plugins", "One or more plugins failed. Check Log.txt.", openLogOnClick: true);
            }
            finally
            {
                _isCompiling = false;
                SendState();
            }
        }

        private async Task HandleCompilePluginAsync(string key)
        {
            if (key == null || !_config.Plugins.TryGetValue(key, out var plugin)) return;
            if (_isCompiling) return;

            _isCompiling = true;
            SendState();
            PostMessage(new { type = "compileProgress", pluginName = plugin.Name, message = "Compiling..." });

            try
            {
                if (!plugin.IsManifestDependency)
                    await EnsureManifestDependenciesForConsumersAsync(new[] { plugin }, pullFirst: false);

                var result = await _repoCompiler.CompileOne(
                    key, plugin, _config.GetCompiledLibraryPaths(), pullFirst: false);

                _dispatcher.Invoke(() => ApplyCompileResult(result));

                if (result.AllSucceeded)
                {
                    if (!plugin.IsManifestDependency)
                        PruneManifestDependenciesAndRefreshState();
                    SendToast("info", "Compile Plugin", $"{plugin.Name} compiled successfully.");
                }
                else
                    SendToast("error", "Compile Plugin", $"{plugin.Name} failed. Check Log.txt.", openLogOnClick: true);
            }
            catch (Exception ex)
            {
                SendToast("error", "Compile Plugin", ex.Message, openLogOnClick: true);
            }
            finally
            {
                _isCompiling = false;
                SendState();
            }
        }

        /// <summary>
        /// Explicitly pulls and recompiles a single repo plugin.
        /// This is the only path that pulls new code from the remote.
        /// </summary>
        private async Task HandleUpdatePluginAsync(string key, bool trustRepo = false)
        {
            if (key == null || !_config.Plugins.TryGetValue(key, out var plugin)) return;
            if (_isCompiling) return;

            if (trustRepo)
            {
                plugin.TrustedRepo = true;
                _config.Save();
            }

            _isCompiling = true;
            SendState();

            try
            {
                var localPath = RepoCompiler.GetLocalRepoPath(plugin.RepoUrl);
                if (!await Task.Run(() => _repoCompiler.CloneOrPull(plugin.RepoUrl, localPath)))
                {
                    SendToast("error", "Update Plugin", "Failed to pull repository.", openLogOnClick: true);
                    return;
                }

                RepoCompiler.ApplyManifestToPlugin(plugin, localPath);
                await EnsureManifestDependenciesForConsumersAsync(new[] { plugin }, pullFirst: true);

                var result = await _repoCompiler.CompileOne(
                    key, plugin, _config.GetCompiledLibraryPaths(), pullFirst: false);

                _dispatcher.Invoke(() => ApplyCompileResult(result));

                if (result.AllSucceeded)
                {
                    PruneManifestDependenciesAndRefreshState();

                    var newCommit = _repoCompiler.GetLocalCommit(plugin.RepoUrl);
                    foreach (var p in _config.Plugins.Values.Where(p =>
                                 p.PluginType == PluginType.Repo &&
                                 string.Equals(p.RepoUrl, plugin.RepoUrl, StringComparison.OrdinalIgnoreCase)))
                    {
                        p.HasUpdate = false;
                        if (!string.IsNullOrEmpty(newCommit))
                        {
                            p.LocalCommit = newCommit;
                            p.RemoteCommit = newCommit;
                        }
                    }

                    _config.Save();
                    SendToast("info", "Update Plugin", $"{plugin.Name} updated and compiled successfully.");
                }
                else
                {
                    SendToast("error", "Update Plugin", $"{plugin.Name} update failed. Check Log.txt.", openLogOnClick: true);
                }
            }
            catch (Exception ex)
            {
                SendToast("error", "Update Plugin", ex.Message, openLogOnClick: true);
            }
            finally
            {
                _isCompiling = false;
                SendState();
            }
        }

        /// <summary>
        /// Runs <c>git fetch</c> for every cloned repo and updates the HasUpdate flag
        /// without modifying any working tree. State is pushed if any flags changed.
        /// </summary>
        private async Task HandleCheckUpdatesAsync()
        {
            var repoUrls = _config.Plugins.Values
                .Where(p => p.PluginType == PluginType.Repo && !string.IsNullOrEmpty(p.RepoUrl))
                .Select(p => p.RepoUrl)
                .Distinct()
                .ToList();

            bool anyChanged = false;

            foreach (var url in repoUrls)
            {
                var localPath = RepoCompiler.GetLocalRepoPath(url);
                if (!Directory.Exists(Path.Combine(localPath, ".git")))
                    continue; // Not yet cloned — nothing to check

                var (hasUpdate, localCommit, remoteCommit) = await Task.Run(() => _repoCompiler.CheckForUpdate(url));

                foreach (var p in _config.Plugins.Values.Where(p => p.RepoUrl == url))
                {
                    if (p.HasUpdate != hasUpdate || p.LocalCommit != localCommit || p.RemoteCommit != remoteCommit)
                    {
                        p.HasUpdate = hasUpdate;
                        p.LocalCommit = localCommit;
                        p.RemoteCommit = remoteCommit;
                        anyChanged = true;
                    }
                }
            }

            if (anyChanged)
                _dispatcher.BeginInvoke(SendState);
        }

        /// <summary>
        /// Reads local HEAD hashes for all already-cloned repos without any network access.
        /// Called once at startup so the Commit column is populated immediately.
        /// </summary>
        private void InitializeLocalCommits()
        {
            RefreshLocalCommits(GetAllRepoUrls(), pushState: true);
        }

        private IEnumerable<string> GetAllRepoUrls() =>
            _config.Plugins.Values
                .Where(p => p.PluginType == PluginType.Repo && !string.IsNullOrEmpty(p.RepoUrl))
                .Select(p => p.RepoUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Updates <see cref="PluginModel.LocalCommit"/> from cloned repos (no fetch).
        /// </summary>
        private void RefreshLocalCommits(IEnumerable<string> repoUrls, bool pushState = true)
        {
            bool anyChanged = false;

            foreach (var url in repoUrls ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(url))
                    continue;

                var commit = _repoCompiler.GetLocalCommit(url);
                if (commit == null)
                    continue;

                foreach (var p in _config.Plugins.Values.Where(p =>
                             p.PluginType == PluginType.Repo &&
                             string.Equals(p.RepoUrl, url, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!string.Equals(p.LocalCommit, commit, StringComparison.OrdinalIgnoreCase))
                    {
                        p.LocalCommit = commit;
                        anyChanged = true;
                    }
                }
            }

            if (anyChanged && pushState)
                _dispatcher.BeginInvoke(SendState);
        }

        private void HandleAddDllPlugin(string path)        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            if (path.Contains(@"\obj\"))
            {
                SendToast("error", "Add Plugin",
                    $"Path should not include \\obj\\. Did you mean {path.Replace("\\obj\\", "\\bin\\")}?");
                return;
            }

            var name = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).ProductName;
            if (string.IsNullOrWhiteSpace(name))
                name = Path.GetFileNameWithoutExtension(path);

            _config.Plugins.Add(Utils.HashFromFile(path), new PluginModel
            {
                PluginType = PluginType.Dll,
                Name = name,
                Path = path,
                Section = Models.PluginSections.Other
            });
        }

        private async Task HandleFetchRepoCsprojsAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                PostMessage(new { type = "repoCsprojs", projects = Array.Empty<object>() });
                return;
            }

            url = url.Trim();
            var localPath = RepoCompiler.GetLocalRepoPath(url);

            bool ok = await Task.Run(() => _repoCompiler.CloneOrPull(url, localPath));
            if (!ok)
            {
                SendToast("error", "Add Plugin", "Failed to clone repository.");
                PostMessage(new { type = "repoCsprojs", projects = Array.Empty<object>() });
                return;
            }

            var discovered = await Task.Run(() => _repoCompiler.DiscoverProjects(localPath));
            var projects = discovered
                .Select(p => new
                {
                    name = p.name,
                    path = p.csprojPath,
                    isLibrary = p.isLibrary,
                    section = p.section,
                    author = p.author,
                    description = p.description
                })
                .ToArray();

            PostMessage(new { type = "repoCsprojs", projects });
        }

        private async Task HandleAddRepoPluginAsync(string url, string projectFilePath)
        {
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(projectFilePath)) return;

            url = url.Trim();
            projectFilePath = projectFilePath.Trim();

            if (projectFilePath.IndexOf($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                projectFilePath.IndexOf($"{Path.AltDirectorySeparatorChar}obj{Path.AltDirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SendToast("error", "Add Plugin",
                    "Do not add a project under an obj folder. Select the .csproj next to Manifest.json in the project directory.");
                return;
            }

            var relPath = Path.GetRelativePath(RepoCompiler.GetLocalRepoPath(url), projectFilePath);
            var key = Utils.HashFromString(url + "|" + relPath);

            if (_config.Plugins.ContainsKey(key))
            {
                SendToast("error", "Add Plugin", $"{Path.GetFileNameWithoutExtension(projectFilePath)} is already added.");
                return;
            }

            var name = Path.GetFileNameWithoutExtension(projectFilePath);

            var plugin = new PluginModel
            {
                PluginType = PluginType.Repo,
                Name = name,
                RepoUrl = url,
                ProjectFilePath = projectFilePath,
                Path = string.Empty,
                IsManifestDependency = false
            };
            RepoCompiler.ApplyManifestToPlugin(plugin, RepoCompiler.GetLocalRepoPath(url));
            _config.Plugins.Add(key, plugin);

            await EnsureManifestDependenciesForConsumersAsync(new[] { plugin }, pullFirst: false);
        }

        private void HandleRemovePlugin(string key)
        {
            if (key == null) return;

            if (!PluginDependencyManager.CanRemove(_config, key, _repoCompiler, out var reason))
            {
                if (!string.IsNullOrEmpty(reason))
                    SendToast("error", "Remove Plugin", reason);
                return;
            }

            if (!_config.Plugins.TryGetValue(key, out var plugin))
                return;

            if (!_config.Plugins.Remove(key))
                return;

            RepoCompiler.DeletePluginArtifacts(plugin, _config);

            PluginDependencyManager.RemovePluginKeyFromLoadouts(_config, key);

            foreach (var prunedKey in PluginDependencyManager.PruneOrphanDependencies(_config, _repoCompiler))
                PluginDependencyManager.RemovePluginKeyFromLoadouts(_config, prunedKey);

            _config.Save();
        }

        private bool IsLoadoutLocked(string loadoutId) =>
            !string.IsNullOrEmpty(loadoutId) &&
            _profilesModel.Profiles.Any(p => p.LoadoutId == loadoutId && p.IsInjected);

        private void HandleAssignLoadout(string profileId, string loadoutId)
        {
            var profile = _profilesModel.Profiles.FirstOrDefault(p => p.Name == profileId);
            if (profile == null || string.IsNullOrEmpty(loadoutId))
                return;

            if (profile.IsInjected)
            {
                SendToast("error", "Loadout", "Eject before changing loadout.");
                return;
            }

            if (!_config.Loadouts.Any(l => l.Id == loadoutId))
            {
                SendToast("error", "Loadout", "Loadout not found.");
                return;
            }

            profile.LoadoutId = loadoutId;
            EnsureProfileInConfig(profile);
            _config.Save();

            if (_activeProfile?.Name == profile.Name)
                ApplyActiveLoadoutHighlight();

            SendState();
        }

        private void HandleCreateLoadout(string name, List<string> pluginKeys, string sourceLoadoutId)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            var keys = pluginKeys ?? new List<string>();

            if (!string.IsNullOrEmpty(sourceLoadoutId))
            {
                var source = _config.Loadouts.FirstOrDefault(l => l.Id == sourceLoadoutId);
                if (source?.PluginKeys != null)
                    keys = new List<string>(source.PluginKeys);
            }

            var loadout = new Loadout
            {
                Id = Guid.NewGuid().ToString(),
                Name = name.Trim(),
                PluginKeys = PruneLoadoutKeys(keys)
            };

            _config.Loadouts.Add(loadout);
            _config.Save();
            SendState();
        }

        private void HandleUpdateLoadout(string loadoutId, string name, List<string> pluginKeys)
        {
            var loadout = _config.Loadouts.FirstOrDefault(l => l.Id == loadoutId);
            if (loadout == null)
                return;

            if (IsLoadoutLocked(loadoutId))
            {
                SendToast("error", "Loadout", "This loadout is in use on an injected character. Eject to edit.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(name))
                loadout.Name = name.Trim();

            if (pluginKeys != null)
                loadout.PluginKeys = PruneLoadoutKeys(pluginKeys);

            _config.Save();
            SendState();
        }

        private void HandleDeleteLoadout(string loadoutId)
        {
            if (loadoutId == Config.DefaultLoadoutId)
            {
                SendToast("error", "Loadout", "The Default loadout cannot be deleted.");
                return;
            }

            if (IsLoadoutLocked(loadoutId))
            {
                SendToast("error", "Loadout", "This loadout is in use on an injected character. Eject to delete.");
                return;
            }

            if (_config.Profiles.Any(p => p.LoadoutId == loadoutId) ||
                _profilesModel.Profiles.Any(p => p.LoadoutId == loadoutId))
            {
                SendToast("error", "Loadout", "A character is still assigned to this loadout.");
                return;
            }

            var loadout = _config.Loadouts.FirstOrDefault(l => l.Id == loadoutId);
            if (loadout != null)
            {
                _config.Loadouts.Remove(loadout);
                _config.Save();
                SendState();
            }
        }

        private void HandleDuplicateLoadout(string loadoutId, string name)
        {
            var source = _config.Loadouts.FirstOrDefault(l => l.Id == loadoutId);
            if (source == null || string.IsNullOrWhiteSpace(name))
                return;

            var loadout = new Loadout
            {
                Id = Guid.NewGuid().ToString(),
                Name = name.Trim(),
                PluginKeys = source.PluginKeys != null
                    ? new List<string>(source.PluginKeys)
                    : new List<string>()
            };

            _config.Loadouts.Add(loadout);
            _config.Save();
            SendState();
        }

        private void HandleSetAutoInject(bool enabled)
        {
            _config.AutoInject = enabled;
            _config.Save();
            SendState();
        }

        private List<string> PruneLoadoutKeys(IEnumerable<string> keys)
        {
            return keys
                .Where(k => !string.IsNullOrEmpty(k) &&
                            _config.Plugins.ContainsKey(k) &&
                            !PluginManifest.GetEffectiveIsLibrary(_config.Plugins[k]))
                .Distinct()
                .ToList();
        }

        private void AutoInjectProfile(Profile profile)
        {
            try
            {
                _ = TryInjectProfileAsync(profile);
            }
            catch (Exception ex)
            {
                Log.Error($"[Bridge] Auto-inject failed: {ex.Message}");
            }
        }

        private void OnProfilesRefreshed()
        {
            var currentlyActive = _profilesModel.Profiles
                .Where(p => p.IsActive)
                .Select(p => p.Name)
                .ToHashSet();

            var newlyActive = currentlyActive
                .Where(name => !_previouslyActiveProfileNames.Contains(name))
                .ToList();

            foreach (var profile in _profilesModel.Profiles.Where(p => p.IsActive))
            {
                if (string.IsNullOrEmpty(profile.LoadoutId))
                    profile.LoadoutId = Config.DefaultLoadoutId;

                EnsureProfileInConfig(profile);
            }

            if (newlyActive.Any())
                _config.Save();

            _previouslyActiveProfileNames.Clear();
            foreach (var name in currentlyActive)
                _previouslyActiveProfileNames.Add(name);

            if (_config.AutoInject)
            {
                foreach (var name in newlyActive)
                {
                    var profile = _profilesModel.Profiles.FirstOrDefault(p => p.Name == name);
                    if (profile != null && profile.IsActive && !profile.IsInjected)
                    {
                        _activeProfile = profile;
                        ApplyActiveLoadoutHighlight();
                        AutoInjectProfile(profile);
                    }
                }
            }

            SendState();
        }

        private void HandleBrowseDll()
        {
            var dialog = new OpenFileDialog { Filter = "DLL Files (*.dll)|*.dll" };
            if (dialog.ShowDialog() == true)
                PostMessage(new { type = "browseResult", kind = "dll", path = dialog.FileName });
        }

        private void HandleBrowseDirectory()
        {
            var dialog = new OpenFolderDialog { Title = "Select AO Install Directory" };
            if (dialog.ShowDialog() == true)
                PostMessage(new { type = "browseResult", kind = "directory", path = dialog.FolderName });
        }

        private void HandleBrowseCsproj()
        {
            var dialog = new OpenFileDialog { Filter = "C# Project Files (*.csproj)|*.csproj", Title = "Select Project File" };
            if (dialog.ShowDialog() == true)
                PostMessage(new { type = "browseResult", kind = "csproj", path = dialog.FileName });
        }

        private async Task HandleEnableLargeAddressAwareAsync(string installDir)
        {
            if (string.IsNullOrWhiteSpace(installDir)) return;

            try
            {
                await Task.Run(() => new LargeAddressAwareTweak(installDir).Run());
                SendToast("info", "Tweaks", "Large address aware enabled successfully.");
            }
            catch (Exception ex)
            {
                SendToast("error", "Tweaks", $"Failed: {ex.Message}");
            }
        }

        // ── State serialisation ──────────────────────────────────────────────

        public void SendState()
        {
            try
            {
                var profiles = _profilesModel.Profiles
                    .Select(p => new
                    {
                        id = p.Name,
                        name = p.Name,
                        loadoutId = p.LoadoutId ?? Config.DefaultLoadoutId,
                        isInjected = p.IsInjected,
                        isActive = p.IsActive
                    })
                    .ToList();

                var loadouts = _config.Loadouts
                    .Select(l => new
                    {
                        id = l.Id,
                        name = l.Name,
                        pluginKeys = l.PluginKeys ?? new List<string>(),
                        isLocked = IsLoadoutLocked(l.Id),
                        isDefault = l.Id == Config.DefaultLoadoutId
                    })
                    .ToList();

                var plugins = _config.Plugins.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new
                    {
                        pluginType = kvp.Value.PluginType.ToString(),
                        name = kvp.Value.Name,
                        path = kvp.Value.Path,
                        repoUrl = kvp.Value.RepoUrl,
                        projectFilePath = kvp.Value.ProjectFilePath,
                        isStub = kvp.Value.IsStub,
                        autoUpdate = kvp.Value.AutoUpdate,
                        isLibrary = PluginManifest.GetEffectiveIsLibrary(kvp.Value),
                        section = PluginManifest.GetEffectiveSection(kvp.Value),
                        isDefault = kvp.Value.IsDefault,
                        isManifestDependency = kvp.Value.IsManifestDependency,
                        canRemove = PluginDependencyManager.CanRemove(_config, kvp.Key, _repoCompiler, out var removeReason),
                        removeBlockedReason = removeReason,
                        isCompiled = kvp.Value.IsCompiled,
                        isEnabled = kvp.Value.IsEnabled,
                        hasUpdate = kvp.Value.HasUpdate,
                        trustedRepo = kvp.Value.TrustedRepo,
                        localCommit = kvp.Value.LocalCommit,
                        remoteCommit = kvp.Value.RemoteCommit,
                        author = kvp.Value.Author,
                        description = kvp.Value.Description,
                        dependencyRepoUrls = kvp.Value.DependencyRepoUrls
                    });

                ApplyActiveLoadoutHighlight();

                var state = new
                {
                    type = "state",
                    profiles,
                    loadouts,
                    plugins,
                    activeProfileId = _activeProfile?.Name,
                    autoInject = _config.AutoInject,
                    isCompiling = _isCompiling,
                    isInjecting = IsInjecting,
                    injectQueue = SnapshotInjectQueue()
                };

                PostMessage(state);
            }
            catch (Exception ex)
            {
                Log.Warning($"[Bridge] SendState failed: {ex.Message}");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void ApplyActiveLoadoutHighlight()
        {
            var activeKeys = new HashSet<string>();
            var loadout = GetLoadoutForProfile(_activeProfile);
            if (loadout?.PluginKeys != null)
            {
                foreach (var key in loadout.PluginKeys)
                    activeKeys.Add(key);
            }

            foreach (var kvp in _config.Plugins)
                kvp.Value.IsEnabled = activeKeys.Contains(kvp.Key);
        }

        private void ApplyCompileResult(CompileResult result)
        {
            _config.Plugins.CollectionChanged -= OnPluginsChanged;
            try
            {
                foreach (var key in result.KeysToRemove)
                    _config.Plugins.Remove(key);

                foreach (var kvp in result.NewEntries)
                {
                    if (!_config.Plugins.ContainsKey(kvp.Key))
                        _config.Plugins.Add(kvp.Key, kvp.Value);
                    else
                    {
                        var existing = _config.Plugins[kvp.Key];
                        existing.Path = kvp.Value.Path;
                        existing.Author = kvp.Value.Author;
                        existing.Description = kvp.Value.Description;
                        existing.IsLibrary = kvp.Value.IsLibrary;
                        existing.Section = kvp.Value.Section;
                        existing.IsManifestDependency = kvp.Value.IsManifestDependency;
                        existing.DependencyRepoUrls = kvp.Value.DependencyRepoUrls != null
                            ? new List<string>(kvp.Value.DependencyRepoUrls)
                            : new List<string>();
                    }
                }
            }
            finally
            {
                _config.Plugins.CollectionChanged += OnPluginsChanged;
            }

            SyncManifestDependencyPathsFromDisk();

            _config.EnsureDefaultsPublic();
            RefreshLocalCommits(GetAllRepoUrls(), pushState: false);
            _config.Save();
            SendState();

            var consumers = result.NewEntries.Values
                .Where(p => p != null && !p.IsManifestDependency)
                .ToList();
            if (consumers.Count > 0)
                _ = RefreshManifestDependenciesForConsumersAsync(consumers);
        }

        /// <summary>
        /// After a compile, manifest-dependency entries may have DLLs on disk before Path is set on the model.
        /// </summary>
        private void SyncManifestDependencyPathsFromDisk()
        {
            foreach (var plugin in _config.Plugins.Values)
            {
                if (!plugin.IsManifestDependency || plugin.PluginType != PluginType.Repo)
                    continue;

                var projectName = !string.IsNullOrEmpty(plugin.ProjectFilePath)
                    ? Path.GetFileNameWithoutExtension(plugin.ProjectFilePath)
                    : plugin.Name;

                var dll = RepoCompiler.TryResolveOutputDll(projectName);
                if (!string.IsNullOrEmpty(dll))
                    plugin.Path = dll;
            }
        }

        /// <summary>
        /// Reads each consumer's manifest, clones/adds dependency plugins, and refreshes commit hashes in the UI.
        /// Call before compile/update so dependencies exist in the grid (same as add-repo flow).
        /// </summary>
        private async Task EnsureManifestDependenciesForConsumersAsync(
            IEnumerable<PluginModel> consumers,
            bool pullFirst)
        {
            await _manifestDependencyLock.WaitAsync();
            try
            {
                foreach (var consumer in consumers.Where(c =>
                             c != null &&
                             c.PluginType == PluginType.Repo &&
                             !c.IsManifestDependency &&
                             !string.IsNullOrEmpty(c.RepoUrl)))
                {
                    var local = RepoCompiler.GetLocalRepoPath(consumer.RepoUrl);
                    if (!string.IsNullOrEmpty(consumer.ProjectFilePath) && File.Exists(consumer.ProjectFilePath))
                        RepoCompiler.ApplyManifestToPlugin(consumer, local);

                    await PluginDependencyManager.EnsureDependenciesAsync(_config, _repoCompiler, consumer, pullFirst);
                }

                _dispatcher.Invoke(PruneManifestDependenciesAndRefreshState);
            }
            finally
            {
                _manifestDependencyLock.Release();
            }
        }

        private void PruneManifestDependenciesAndRefreshState()
        {
            foreach (var prunedKey in PluginDependencyManager.PruneOrphanDependencies(_config, _repoCompiler))
                PluginDependencyManager.RemovePluginKeyFromLoadouts(_config, prunedKey);

            RefreshLocalCommits(GetAllRepoUrls(), pushState: false);
            _config.Save();
            SendState();
        }

        private async Task RefreshManifestDependenciesAsync(PluginModel consumer) =>
            await EnsureManifestDependenciesForConsumersAsync(new[] { consumer }, pullFirst: false);

        private async Task RefreshManifestDependenciesForConsumersAsync(List<PluginModel> consumers) =>
            await EnsureManifestDependenciesForConsumersAsync(consumers, pullFirst: false);

        private void OnPluginsChanged(object sender,
            System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            _config.Save();
            SendState();
        }

        private void OnCompileProgress(object sender, CompileProgressEventArgs args)
        {
            Log.Information($"[Compile] {args.PluginName}: {args.Message}");
            _dispatcher.BeginInvoke(() =>
            {
                PostMessage(new { type = "compileProgress", pluginName = args.PluginName, message = args.Message });
                if (!string.IsNullOrWhiteSpace(args.DllPath) && File.Exists(args.DllPath))
                    TryApplyCompiledDllPath(args.PluginName, args.DllPath);
            });
        }

        private void TryApplyCompiledDllPath(string pluginName, string dllPath)
        {
            var plugin = _config.Plugins.Values.FirstOrDefault(p =>
                string.Equals(p.Name, pluginName, StringComparison.OrdinalIgnoreCase));
            if (plugin == null)
                return;

            if (string.Equals(plugin.Path, dllPath, StringComparison.OrdinalIgnoreCase))
            {
                SendState();
                return;
            }

            plugin.Path = dllPath;
            SendState();
        }

        private void SendToast(string level, string title, string message, bool openLogOnClick = false)
        {
            if (openLogOnClick)
                PostMessage(new { type = "toast", level, title, message, openLogOnClick = true });
            else
                PostMessage(new { type = "toast", level, title, message });
        }

        private void PostMessage(object payload)
        {
            try
            {
                string json = JsonConvert.SerializeObject(payload);
                _dispatcher.BeginInvoke(() =>
                {
                    try { _webView.CoreWebView2?.PostWebMessageAsString(json); }
                    catch (Exception ex) { Log.Warning($"[Bridge] PostMessage failed: {ex.Message}"); }
                });
            }
            catch (Exception ex)
            {
                Log.Warning($"[Bridge] Serialize failed: {ex.Message}");
            }
        }

        // Nested DTO for incoming messages
        private class BridgeMessage
        {
            [JsonProperty("type")] public string Type { get; set; }
            [JsonProperty("profileId")] public string ProfileId { get; set; }
            [JsonProperty("key")] public string Key { get; set; }
            [JsonProperty("trustRepo")] public bool TrustRepo { get; set; }
            [JsonProperty("path")] public string Path { get; set; }
            [JsonProperty("url")] public string Url { get; set; }
            [JsonProperty("isLibrary")] public bool IsLibrary { get; set; }
            [JsonProperty("projectFilePath")] public string ProjectFilePath { get; set; }
            [JsonProperty("enabled")] public bool Enabled { get; set; }
            [JsonProperty("loadoutId")] public string LoadoutId { get; set; }
            [JsonProperty("name")] public string Name { get; set; }
            [JsonProperty("pluginKeys")] public List<string> PluginKeys { get; set; }
            [JsonProperty("sourceLoadoutId")] public string SourceLoadoutId { get; set; }
            [JsonProperty("installDir")] public string InstallDir { get; set; }
            // Error reporting from React window.onerror / ErrorBoundary
            [JsonProperty("level")] public string Level { get; set; }
            [JsonProperty("title")] public string Title { get; set; }
            [JsonProperty("message")] public string Message { get; set; }
        }
    }
}
