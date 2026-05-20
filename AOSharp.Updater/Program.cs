using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;

namespace AOSharp.Updater
{
    internal static class Program
    {
        private static readonly string[] PreservedTopLevelNames =
        {
            "Plugins",
            "repos",
            "logs"
        };

        private static string _logPath;

        public static int Main(string[] args)
        {
            try
            {
                if (args.Length < 1)
                {
                    Console.Error.WriteLine("Usage: AOSharp.Updater.exe <pending-update.json>");
                    return 1;
                }

                var manifestPath = args[0].Trim('"');
                InitLog(manifestPath);

                Log($"Manifest: {manifestPath}");
                if (!File.Exists(manifestPath))
                {
                    Log($"Manifest not found: {manifestPath}");
                    return 1;
                }

                var manifest = JsonConvert.DeserializeObject<PendingUpdateManifest>(File.ReadAllText(manifestPath));
                if (manifest == null ||
                    string.IsNullOrWhiteSpace(manifest.InstallDir) ||
                    string.IsNullOrWhiteSpace(manifest.StagingDir))
                {
                    Log("Manifest is invalid.");
                    return 1;
                }

                if (!Directory.Exists(manifest.StagingDir))
                {
                    Log($"Staging directory not found: {manifest.StagingDir}");
                    return 1;
                }

                var installDir = Path.GetFullPath(manifest.InstallDir);
                var stagingDir = Path.GetFullPath(manifest.StagingDir);
                var mainExe = string.IsNullOrWhiteSpace(manifest.MainExePath)
                    ? Path.Combine(installDir, "AOSharp.exe")
                    : manifest.MainExePath;

                Log($"Install: {installDir}");
                Log($"Staging: {stagingDir}");

                var processName = Path.GetFileNameWithoutExtension(mainExe);
                WaitForProcessExit(processName, TimeSpan.FromMinutes(2));
                Log($"Process '{processName}' has exited.");

                var failures = ApplyUpdate(stagingDir, installDir);
                if (failures.Count > 0)
                {
                    Log("Some files could not be updated:");
                    foreach (var f in failures)
                        Log($"  {f}");
                    return 2;
                }

                Log("Update applied successfully.");

                try
                {
                    File.Delete(manifestPath);
                }
                catch (Exception ex)
                {
                    Log($"Could not delete manifest (non-fatal): {ex.Message}");
                }

                if (File.Exists(mainExe))
                {
                    Process.Start(new ProcessStartInfo(mainExe)
                    {
                        WorkingDirectory = installDir,
                        UseShellExecute = true
                    });
                    Log("Restarted AOSharp.");
                }

                return 0;
            }
            catch (Exception ex)
            {
                try { Log($"Fatal: {ex}"); } catch { Console.Error.WriteLine(ex); }
                return 1;
            }
        }

        private static void InitLog(string manifestPath)
        {
            try
            {
                var appData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AOSharp");
                Directory.CreateDirectory(appData);
                _logPath = Path.Combine(appData, "update.log");
                File.AppendAllText(_logPath,
                    $"{Environment.NewLine}--- {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---{Environment.NewLine}");
            }
            catch
            {
                _logPath = null;
            }
        }

        private static void Log(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Console.WriteLine(line);
            if (string.IsNullOrEmpty(_logPath))
                return;
            try
            {
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch
            {
                // ignore
            }
        }

        private static void WaitForProcessExit(string processName, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (Process.GetProcessesByName(processName).Length == 0)
                    return;
                Thread.Sleep(250);
            }

            Log($"Warning: timed out waiting for '{processName}' to exit; continuing anyway.");
        }

        private static List<string> ApplyUpdate(string stagingDir, string installDir)
        {
            Directory.CreateDirectory(installDir);
            var failures = new List<string>();
            var runningUpdater = string.Empty;
            try
            {
                runningUpdater = Path.GetFullPath(Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty);
            }
            catch
            {
                // ignore
            }

            foreach (var sourcePath in Directory.EnumerateFiles(stagingDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(stagingDir, sourcePath);
                if (ShouldPreserveRelativePath(relative))
                    continue;

                var destPath = Path.GetFullPath(Path.Combine(installDir, relative));
                if (!string.IsNullOrEmpty(runningUpdater) &&
                    string.Equals(destPath, runningUpdater, StringComparison.OrdinalIgnoreCase))
                {
                    Log($"Skipping in-use updater: {relative}");
                    continue;
                }

                if (!TryReplaceFile(sourcePath, destPath))
                    failures.Add(relative);
            }

            return failures;
        }

        private static bool ShouldPreserveRelativePath(string relativePath)
        {
            var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
            var parts = normalized.Split(Path.DirectorySeparatorChar);

            if (parts.Length == 0)
                return false;

            if (PreservedTopLevelNames.Any(n =>
                    string.Equals(parts[0], n, StringComparison.OrdinalIgnoreCase)))
                return true;

            if (parts[0].EndsWith(".WebView2", StringComparison.OrdinalIgnoreCase))
                return true;

            if (parts.Length == 1 &&
                parts[0].StartsWith("Log", StringComparison.OrdinalIgnoreCase) &&
                parts[0].EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static bool TryReplaceFile(string sourceFile, string destFile, int maxAttempts = 12)
        {
            var destDir = Path.GetDirectoryName(destFile);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    if (File.Exists(destFile))
                    {
                        File.SetAttributes(destFile, FileAttributes.Normal);
                        File.Delete(destFile);
                    }

                    File.Copy(sourceFile, destFile, overwrite: false);
                    return true;
                }
                catch (IOException ex) when (IsSharingViolation(ex))
                {
                    Thread.Sleep(250 * (attempt + 1));
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(250 * (attempt + 1));
                }
            }

            return false;
        }

        private static bool IsSharingViolation(IOException ex)
        {
            const int sharingViolation = unchecked((int)0x80070020);
            const int lockViolation = unchecked((int)0x80070021);
            var hr = ex.HResult;
            return hr == sharingViolation || hr == lockViolation;
        }
    }
}
