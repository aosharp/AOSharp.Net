using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AOSharp.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace AOSharp.Services
{
    public enum AppUpdateStatus
    {
        Idle,
        Checking,
        UpToDate,
        Available,
        Downloading,
        Ready,
        Error
    }

    public sealed class AppUpdateSnapshot
    {
        public string CurrentVersion { get; set; }
        public string AvailableVersion { get; set; }
        public string ReleaseNotesUrl { get; set; }
        public AppUpdateStatus Status { get; set; } = AppUpdateStatus.Idle;
        public int DownloadProgressPercent { get; set; }
        public string Error { get; set; }
        public bool ReadyToApply { get; set; }

        /// <summary>When false, the UI should hide the update banner (user dismissed).</summary>
        public bool BannerVisible { get; set; }
    }

    public sealed class AppUpdateService
    {
        private static readonly HttpClient Http = CreateHttpClient();

        private readonly object _gate = new object();
        private AppUpdateSnapshot _snapshot;
        private CancellationTokenSource _downloadCts;

        public event Action StateChanged;

        public AppUpdateService()
        {
            _snapshot = new AppUpdateSnapshot
            {
                CurrentVersion = GetCurrentVersionString(),
                Status = AppUpdateStatus.Idle
            };
            RefreshReadyStateFromDisk();
        }

        public AppUpdateSnapshot GetSnapshot()
        {
            lock (_gate)
                return CloneSnapshot(_snapshot);
        }

        public async Task CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        {
            SetSnapshot(s =>
            {
                s.Status = AppUpdateStatus.Checking;
                s.Error = null;
            });

            try
            {
                using var response = await Http.GetAsync(AppUpdateConstants.LatestReleaseApiUrl, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var release = JObject.Parse(json);

                var tag = release["tag_name"]?.ToString();
                var available = NormalizeVersionTag(tag);
                var notesUrl = release["html_url"]?.ToString();
                var assets = release["assets"] as JArray;
                var zipUrl = FindAssetUrl(assets, AppUpdateConstants.ZipAssetName);

                if (string.IsNullOrWhiteSpace(available) || string.IsNullOrWhiteSpace(zipUrl))
                {
                    SetSnapshot(s =>
                    {
                        s.Status = AppUpdateStatus.Error;
                        s.Error = "Release metadata is missing a version tag or ZIP asset.";
                        s.AvailableVersion = null;
                    });
                    return;
                }

                var current = GetCurrentVersionString();
                if (CompareVersions(available, current) <= 0)
                {
                    ClearStalePendingUpdate(current);
                    SetSnapshot(s =>
                    {
                        s.CurrentVersion = current;
                        s.AvailableVersion = null;
                        s.ReleaseNotesUrl = notesUrl;
                        s.Status = AppUpdateStatus.UpToDate;
                        s.ReadyToApply = false;
                    });
                    return;
                }

                SetSnapshot(s =>
                {
                    s.CurrentVersion = current;
                    s.AvailableVersion = available;
                    s.ReleaseNotesUrl = notesUrl;
                    s.Status = AppUpdateStatus.Available;
                    s.ReadyToApply = HasReadyManifestForVersion(available);
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[AppUpdate] Check failed");
                SetSnapshot(s =>
                {
                    s.Status = AppUpdateStatus.Error;
                    s.Error = ex.Message;
                });
            }
        }

        public async Task DownloadUpdateAsync(CancellationToken cancellationToken = default)
        {
            AppUpdateSnapshot snap;
            lock (_gate)
            {
                snap = CloneSnapshot(_snapshot);
                if (string.IsNullOrWhiteSpace(snap.AvailableVersion))
                    throw new InvalidOperationException("No update is available to download.");
            }

            _downloadCts?.Cancel();
            _downloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _downloadCts.Token;

            SetSnapshot(s =>
            {
                s.Status = AppUpdateStatus.Downloading;
                s.DownloadProgressPercent = 0;
                s.Error = null;
                s.ReadyToApply = false;
            });

            try
            {
                using var response = await Http.GetAsync(AppUpdateConstants.LatestReleaseApiUrl, token)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var release = JObject.Parse(json);
                var assets = release["assets"] as JArray;
                var zipUrl = FindAssetUrl(assets, AppUpdateConstants.ZipAssetName);
                var hashUrl = FindAssetUrl(assets, AppUpdateConstants.Sha256AssetName);

                if (string.IsNullOrWhiteSpace(zipUrl))
                    throw new InvalidOperationException("Release ZIP asset was not found.");

                var version = snap.AvailableVersion;
                var updateRoot = GetVersionUpdateDirectory(version);
                Directory.CreateDirectory(updateRoot);

                var zipPath = Path.Combine(updateRoot, AppUpdateConstants.ZipAssetName);
                var hashPath = Path.Combine(updateRoot, AppUpdateConstants.Sha256AssetName);
                var extractDir = Path.Combine(updateRoot, "extracted");

                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, recursive: true);

                await DownloadFileAsync(zipUrl, zipPath, token, progress =>
                {
                    SetSnapshot(s => s.DownloadProgressPercent = progress);
                }).ConfigureAwait(false);

                string expectedHash = null;
                if (!string.IsNullOrWhiteSpace(hashUrl))
                {
                    await DownloadFileAsync(hashUrl, hashPath, token, _ => { }).ConfigureAwait(false);
                    expectedHash = ParseSha256File(hashPath);
                }

                if (!string.IsNullOrWhiteSpace(expectedHash))
                {
                    var actual = ComputeFileSha256Hex(zipPath);
                    if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Downloaded ZIP failed SHA256 verification.");
                }
                else
                {
                    Log.Warning("[AppUpdate] No SHA256 sidecar on release; skipping hash verification.");
                }

                ZipFile.ExtractToDirectory(zipPath, extractDir);
                extractDir = ResolveStagingRoot(extractDir);

                var installDir = AppUpdateApply.GetInstallDirectory();
                var mainExe = Path.Combine(installDir, "AOSharp.exe");
                var updaterExe = Path.Combine(installDir, "AOSharp.Updater.exe");

                var manifest = new PendingUpdateManifest
                {
                    Version = version,
                    InstallDir = installDir,
                    StagingDir = extractDir,
                    AssetSha256 = expectedHash,
                    MainExePath = mainExe,
                    UpdaterExePath = updaterExe
                };

                WritePendingManifest(manifest);

                SetSnapshot(s =>
                {
                    s.Status = AppUpdateStatus.Ready;
                    s.DownloadProgressPercent = 100;
                    s.ReadyToApply = true;
                    s.Error = null;
                });
            }
            catch (OperationCanceledException)
            {
                SetSnapshot(s =>
                {
                    s.Status = AppUpdateStatus.Available;
                    s.DownloadProgressPercent = 0;
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[AppUpdate] Download failed");
                SetSnapshot(s =>
                {
                    s.Status = AppUpdateStatus.Error;
                    s.Error = ex.Message;
                    s.ReadyToApply = false;
                });
            }
        }

        public void ApplyPendingUpdateAndShutdown()
        {
            var manifest = ReadPendingManifest();
            if (manifest == null || !Directory.Exists(manifest.StagingDir))
                throw new InvalidOperationException("No downloaded update is ready to apply.");

            AppUpdateApply.LaunchDeferredApplyAndExit(manifest, GetPendingManifestPath());
        }

        public void DismissUpdateBanner()
        {
            AppUpdateSnapshot snap;
            lock (_gate)
                snap = CloneSnapshot(_snapshot);

            var version = snap.AvailableVersion;
            if (string.IsNullOrWhiteSpace(version) && snap.ReadyToApply)
                version = ReadPendingManifest()?.Version;

            if (string.IsNullOrWhiteSpace(version))
                return;

            WriteDismissedVersion(version);
            SetSnapshot(s => { });
        }

        private static string GetDismissedVersionPath() =>
            Path.Combine(Directories.AppDataDirectory, "update-dismiss.txt");

        private static string GetDismissedVersion()
        {
            try
            {
                var path = GetDismissedVersionPath();
                return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        private static void WriteDismissedVersion(string version)
        {
            Directory.CreateDirectory(Directories.AppDataDirectory);
            File.WriteAllText(GetDismissedVersionPath(), version.Trim());
        }

        private static string ResolveStagingRoot(string extractDir)
        {
            if (File.Exists(Path.Combine(extractDir, "AOSharp.exe")))
                return extractDir;

            foreach (var sub in Directory.GetDirectories(extractDir))
            {
                if (File.Exists(Path.Combine(sub, "AOSharp.exe")))
                    return sub;
            }

            throw new InvalidOperationException(
                "Downloaded update ZIP does not contain AOSharp.exe at the root.");
        }

        private void RefreshReadyStateFromDisk()
        {
            var manifest = ReadPendingManifest();
            if (manifest == null || !Directory.Exists(manifest.StagingDir))
                return;

            var current = GetCurrentVersionString();
            if (CompareVersions(manifest.Version, current) <= 0)
            {
                ClearStalePendingUpdate(current);
                return;
            }

            SetSnapshot(s =>
            {
                s.CurrentVersion = current;
                s.AvailableVersion = manifest.Version;
                s.Status = AppUpdateStatus.Ready;
                s.ReadyToApply = true;
                s.DownloadProgressPercent = 100;
            });
        }

        private static void ClearStalePendingUpdate(string currentVersion)
        {
            var manifest = ReadPendingManifest();
            if (manifest == null)
                return;

            if (!string.IsNullOrWhiteSpace(currentVersion) &&
                CompareVersions(manifest.Version, currentVersion) <= 0)
            {
                TryDeletePendingUpdateArtifacts(manifest);
            }
        }

        private static void TryDeletePendingUpdateArtifacts(PendingUpdateManifest manifest)
        {
            try
            {
                File.Delete(GetPendingManifestPath());
            }
            catch
            {
                // ignore
            }

            if (manifest == null || string.IsNullOrWhiteSpace(manifest.StagingDir))
                return;

            try
            {
                if (Directory.Exists(manifest.StagingDir))
                {
                    var updateRoot = Directory.GetParent(manifest.StagingDir)?.FullName;
                    Directory.Delete(manifest.StagingDir, recursive: true);
                    if (!string.IsNullOrEmpty(updateRoot) &&
                        Directory.Exists(updateRoot) &&
                        !Directory.EnumerateFileSystemEntries(updateRoot).Any())
                    {
                        Directory.Delete(updateRoot, recursive: true);
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        private static bool HasReadyManifestOnDisk() =>
            ReadPendingManifest() != null;

        private bool HasReadyManifestForVersion(string version)
        {
            var manifest = ReadPendingManifest();
            return manifest != null &&
                   Directory.Exists(manifest.StagingDir) &&
                   string.Equals(manifest.Version, version, StringComparison.OrdinalIgnoreCase);
        }

        public static string GetPendingManifestPath() =>
            Path.Combine(Directories.AppDataDirectory, "pending-update.json");

        public static string GetVersionUpdateDirectory(string version) =>
            Path.Combine(Directories.AppDataDirectory, "updates", version ?? "unknown");

        public static void WritePendingManifest(PendingUpdateManifest manifest)
        {
            Directory.CreateDirectory(Directories.AppDataDirectory);
            File.WriteAllText(GetPendingManifestPath(), JsonConvert.SerializeObject(manifest, Formatting.Indented));
        }

        public static PendingUpdateManifest ReadPendingManifest()
        {
            var path = GetPendingManifestPath();
            if (!File.Exists(path))
                return null;
            try
            {
                return JsonConvert.DeserializeObject<PendingUpdateManifest>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }

        private static async Task DownloadFileAsync(
            string url,
            string destinationPath,
            CancellationToken cancellationToken,
            Action<int> reportProgress)
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long read = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer, 0, count, cancellationToken).ConfigureAwait(false);
                read += count;
                if (total.HasValue && total.Value > 0)
                {
                    var pct = (int)Math.Min(100, read * 100 / total.Value);
                    reportProgress?.Invoke(pct);
                }
            }

            reportProgress?.Invoke(100);
        }

        private static string FindAssetUrl(JArray assets, string name)
        {
            if (assets == null)
                return null;

            foreach (var asset in assets.OfType<JObject>())
            {
                if (string.Equals(asset["name"]?.ToString(), name, StringComparison.OrdinalIgnoreCase))
                    return asset["browser_download_url"]?.ToString();
            }

            return null;
        }

        private static string ParseSha256File(string path)
        {
            var line = File.ReadAllLines(path).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
            if (string.IsNullOrWhiteSpace(line))
                return null;

            var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0].Trim() : line.Trim();
        }

        private static string ComputeFileSha256Hex(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "", StringComparison.Ordinal);
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(AppUpdateConstants.UserAgent);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        public static string GetCurrentVersionString()
        {
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var fromInfo = NormalizeVersionString(info);
            if (!string.IsNullOrWhiteSpace(fromInfo) && !string.Equals(fromInfo, "0.0.0", StringComparison.Ordinal))
                return fromInfo;

            var fromAssembly = NormalizeVersionString(asm.GetName().Version?.ToString());
            if (!string.IsNullOrWhiteSpace(fromAssembly) && !string.Equals(fromAssembly, "0.0.0", StringComparison.Ordinal))
                return fromAssembly;

            if (!string.IsNullOrEmpty(Environment.ProcessPath) && File.Exists(Environment.ProcessPath))
            {
                var fvi = FileVersionInfo.GetVersionInfo(Environment.ProcessPath);
                var fromExe = NormalizeVersionString(fvi.ProductVersion) ??
                                NormalizeVersionString(fvi.FileVersion);
                if (!string.IsNullOrWhiteSpace(fromExe) && !string.Equals(fromExe, "0.0.0", StringComparison.Ordinal))
                    return fromExe;
            }

            return fromInfo ?? fromAssembly ?? "0.0.0";
        }

        private static string NormalizeVersionString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            value = value.Trim();
            var plus = value.IndexOf('+');
            if (plus >= 0)
                value = value.Substring(0, plus);

            if (Version.TryParse(value, out var version))
                return version.ToString(3);

            return value;
        }

        public static string NormalizeVersionTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return null;
            tag = tag.Trim();
            if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                tag = tag.Substring(1);
            return tag;
        }

        public static int CompareVersions(string a, string b)
        {
            if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb))
                return va.CompareTo(vb);
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private void SetSnapshot(Action<AppUpdateSnapshot> mutate)
        {
            lock (_gate)
            {
                mutate(_snapshot);
                _snapshot = CloneSnapshot(_snapshot);
            }

            try { StateChanged?.Invoke(); }
            catch { /* UI handlers should not break update flow */ }
        }

        private static AppUpdateSnapshot CloneSnapshot(AppUpdateSnapshot s)
        {
            var clone = new AppUpdateSnapshot
            {
                CurrentVersion = s.CurrentVersion,
                AvailableVersion = s.AvailableVersion,
                ReleaseNotesUrl = s.ReleaseNotesUrl,
                Status = s.Status,
                DownloadProgressPercent = s.DownloadProgressPercent,
                Error = s.Error,
                ReadyToApply = s.ReadyToApply
            };
            clone.BannerVisible = ComputeBannerVisible(clone);
            return clone;
        }

        private static bool ComputeBannerVisible(AppUpdateSnapshot s)
        {
            if (s.Status == AppUpdateStatus.Downloading)
                return true;

            var dismissed = GetDismissedVersion();
            var bannerVersion = s.ReadyToApply
                ? ReadPendingManifest()?.Version ?? s.AvailableVersion
                : s.AvailableVersion;

            if (!string.IsNullOrWhiteSpace(bannerVersion) &&
                string.Equals(dismissed, bannerVersion, StringComparison.OrdinalIgnoreCase))
                return false;

            if (s.ReadyToApply)
                return true;
            if (s.Status == AppUpdateStatus.Error)
                return true;
            if (s.Status == AppUpdateStatus.Available && !string.IsNullOrWhiteSpace(s.AvailableVersion))
                return true;

            return false;
        }
    }
}
