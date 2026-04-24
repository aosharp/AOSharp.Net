using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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

            // Push state whenever the timer refreshes profiles
            profilesModel.ProfilesRefreshed += (_, _) => SendState();

            // Push state whenever plugins collection changes
            _config.Plugins.CollectionChanged += (_, _) =>
            {
                _config.Save();
                SendState();
            };
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
                        ApplyEnabledPlugins();
                        SendState();
                        break;

                    case "inject":
                        await HandleInjectAsync();
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

                    case "addDllPlugin":
                        HandleAddDllPlugin(msg.Path);
                        break;

                    case "fetchRepoCsprojs":
                        await HandleFetchRepoCsprojsAsync(msg.Url);
                        break;

                    case "addRepoPlugin":
                        HandleAddRepoPlugin(msg.Url, msg.ProjectFilePath);
                        break;

                    case "removePlugin":
                        HandleRemovePlugin(msg.Key);
                        break;

                    case "togglePlugin":
                        HandleTogglePlugin(msg.Key, msg.Enabled);
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

        private async Task HandleInjectAsync()
        {
            if (_activeProfile == null) return;

            var plugins = _config.Plugins
                .Where(x =>
                    _activeProfile.EnabledPlugins.Contains(x.Key) &&
                    !x.Value.IsLibrary &&
                    x.Value.IsCompiled)
                .Select(x => x.Value.Path);

            if (!plugins.Any())
            {
                SendToast("error", "Inject", "No compiled, non-library plugins are selected.");
                return;
            }

            bool ok = _activeProfile.Inject(plugins);

            if (!ok)
                SendToast("error", "Inject", "Failed to inject.");

            // Hook disconnect so we push state when pipe drops
            _activeProfile.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Profile.IsInjected))
                    SendState();
            };

            SendState();
        }

        private void HandleEject()
        {
            _activeProfile?.Eject();
            SendState();
        }

        private async Task HandleCompileAllAsync()
        {
            if (_isCompiling) return;
            _isCompiling = true;
            SendState();

            try
            {
                var result = await _repoCompiler.CompileAll(
                    _config.Plugins.ToList(),
                    pullFirst: true,
                    onGroupComplete: partial => _dispatcher.Invoke(() => ApplyCompileResult(partial)));

                if (result.AllSucceeded)
                    SendToast("info", "Compile Plugins", "All plugins compiled successfully.");
                else
                    SendToast("error", "Compile Plugins", "One or more plugins failed. Check Log.txt.");
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

            try
            {
                var result = await _repoCompiler.CompileOne(
                    key, plugin, _config.GetCompiledLibraryPaths(), pullFirst: true);

                ApplyCompileResult(result);

                if (result.AllSucceeded)
                    SendToast("info", "Compile Plugin", $"{plugin.Name} compiled successfully.");
                else
                    SendToast("error", "Compile Plugin", $"{plugin.Name} failed. Check Log.txt.");
            }
            catch (Exception ex)
            {
                SendToast("error", "Compile Plugin", ex.Message);
            }
        }

        private void HandleAddDllPlugin(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            if (path.Contains(@"\obj\"))
            {
                SendToast("error", "Add Plugin",
                    $"Path should not include \\obj\\. Did you mean {path.Replace("\\obj\\", "\\bin\\")}?");
                return;
            }

            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            _config.Plugins.Add(Utils.HashFromFile(path), new PluginModel
            {
                PluginType = PluginType.Dll,
                Name = info.ProductName,
                Version = info.FileVersion,
                Path = path
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
                .Select(p => new { name = p.name, path = p.csprojPath, isLibrary = p.isLibrary })
                .ToArray();

            PostMessage(new { type = "repoCsprojs", projects });
        }

        private void HandleAddRepoPlugin(string url, string projectFilePath)
        {
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(projectFilePath)) return;

            url = url.Trim();
            projectFilePath = projectFilePath.Trim();

            var relPath = Path.GetRelativePath(RepoCompiler.GetLocalRepoPath(url), projectFilePath);
            var key = Utils.HashFromString(url + "|" + relPath);

            if (_config.Plugins.ContainsKey(key))
            {
                SendToast("error", "Add Plugin", $"{Path.GetFileNameWithoutExtension(projectFilePath)} is already added.");
                return;
            }

            var name = Path.GetFileNameWithoutExtension(projectFilePath);
            bool isLibrary = RepoCompiler.ReadIsLibrary(projectFilePath);

            _config.Plugins.Add(key, new PluginModel
            {
                PluginType = PluginType.Repo,
                Name = name,
                RepoUrl = url,
                IsLibrary = isLibrary,
                ProjectFilePath = projectFilePath,
                Path = string.Empty
            });
        }

        private void HandleRemovePlugin(string key)
        {
            if (key == null) return;
            if (_config.Plugins.TryGetValue(key, out var p) && p.IsDefault) return;
            _config.Plugins.Remove(key);
        }

        private void HandleTogglePlugin(string key, bool enabled)
        {
            if (_activeProfile == null || key == null) return;

            if (enabled)
                _activeProfile.EnabledPlugins.Add(key);
            else
                _activeProfile.EnabledPlugins.Remove(key);

            if (!_config.Profiles.Contains(_activeProfile))
                _config.Profiles.Add(_activeProfile);

            if (_config.Plugins.TryGetValue(key, out var p))
                p.IsEnabled = enabled;

            _config.Save();
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
                        isInjected = p.IsInjected,
                        isActive = p.IsActive,
                        enabledPlugins = p.EnabledPlugins.ToList()
                    })
                    .ToList();

                var plugins = _config.Plugins.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new
                    {
                        pluginType = kvp.Value.PluginType.ToString(),
                        name = kvp.Value.Name,
                        version = kvp.Value.Version,
                        path = kvp.Value.Path,
                        repoUrl = kvp.Value.RepoUrl,
                        projectFilePath = kvp.Value.ProjectFilePath,
                        isStub = kvp.Value.IsStub,
                        autoUpdate = kvp.Value.AutoUpdate,
                        isLibrary = kvp.Value.IsLibrary,
                        isDefault = kvp.Value.IsDefault,
                        isCompiled = kvp.Value.IsCompiled,
                        isEnabled = kvp.Value.IsEnabled
                    });

                var state = new
                {
                    type = "state",
                    profiles,
                    plugins,
                    activeProfileId = _activeProfile?.Name,
                    isCompiling = _isCompiling
                };

                PostMessage(state);
            }
            catch (Exception ex)
            {
                Log.Warning($"[Bridge] SendState failed: {ex.Message}");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void ApplyEnabledPlugins()
        {
            foreach (var kvp in _config.Plugins)
                kvp.Value.IsEnabled = _activeProfile != null &&
                                      _activeProfile.EnabledPlugins.Contains(kvp.Key);
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
                        _config.Plugins[kvp.Key].Path = kvp.Value.Path;
                }
            }
            finally
            {
                _config.Plugins.CollectionChanged += OnPluginsChanged;
            }

            _config.EnsureDefaultsPublic();
            _config.Save();
            SendState();
        }

        private void OnPluginsChanged(object sender,
            System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            _config.Save();
            SendState();
        }

        private void OnCompileProgress(object sender, CompileProgressEventArgs args)
        {
            Log.Information($"[Compile] {args.PluginName}: {args.Message}");
            PostMessage(new { type = "compileProgress", pluginName = args.PluginName, message = args.Message });
        }

        private void SendToast(string level, string title, string message)
            => PostMessage(new { type = "toast", level, title, message });

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
            [JsonProperty("path")] public string Path { get; set; }
            [JsonProperty("url")] public string Url { get; set; }
            [JsonProperty("isLibrary")] public bool IsLibrary { get; set; }
            [JsonProperty("projectFilePath")] public string ProjectFilePath { get; set; }
            [JsonProperty("enabled")] public bool Enabled { get; set; }
            [JsonProperty("installDir")] public string InstallDir { get; set; }
            // Error reporting from React window.onerror / ErrorBoundary
            [JsonProperty("level")] public string Level { get; set; }
            [JsonProperty("title")] public string Title { get; set; }
            [JsonProperty("message")] public string Message { get; set; }
        }
    }
}
