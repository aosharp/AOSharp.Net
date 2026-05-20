using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using AOSharp.Data;
using Serilog;

namespace AOSharp.Services
{
    /// <summary>Applies staged release files into the install folder (robocopy + deferred batch).</summary>
    internal static class AppUpdateApply
    {
        private static readonly string[] ExcludedDirectoryNames =
        {
            "Plugins",
            "repos",
            "logs"
        };

        public static string GetInstallDirectory()
        {
            if (!string.IsNullOrEmpty(Environment.ProcessPath))
            {
                var dir = Path.GetDirectoryName(Environment.ProcessPath);
                if (!string.IsNullOrEmpty(dir))
                    return Path.GetFullPath(dir);
            }

            return Path.GetFullPath(AppContext.BaseDirectory);
        }

        /// <summary>
        /// When a previous apply was interrupted, copy staged files now (no AOSharp process yet)
        /// and restart so the new AOSharp.dll is loaded.
        /// </summary>
        public static bool TryApplyOnStartupAndRestartIfNeeded()
        {
            var manifest = AppUpdateService.ReadPendingManifest();
            if (manifest == null || !Directory.Exists(manifest.StagingDir))
                return false;

            if (Process.GetProcessesByName("AOSharp").Length > 1)
                return false;

            var installDir = GetInstallDirectory();
            AppendLog($"Startup apply: staging={manifest.StagingDir} install={installDir}");

            if (!RunRobocopy(manifest.StagingDir, installDir))
            {
                AppendLog("Startup apply: robocopy failed.");
                return false;
            }

            TryDeleteManifest();
            AppendLog("Startup apply: success, restarting to load new binaries.");
            return true;
        }

        public static void LaunchDeferredApplyAndExit(PendingUpdateManifest manifest, string manifestPath)
        {
            manifest.InstallDir = GetInstallDirectory();
            manifest.MainExePath = Path.Combine(manifest.InstallDir, "AOSharp.exe");
            manifest.UpdaterExePath = Path.Combine(manifest.InstallDir, "AOSharp.Updater.exe");
            AppUpdateService.WritePendingManifest(manifest);

            var batchPath = Path.Combine(Path.GetTempPath(), $"aosharp-update-{Guid.NewGuid():N}.cmd");
            File.WriteAllText(batchPath, BuildDeferredBatch(manifest, manifestPath, batchPath), Encoding.UTF8);

            AppendLog($"Launching deferred update batch: {batchPath}");

            var started = Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"\"{batchPath}\"\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });

            if (started == null)
                throw new InvalidOperationException("Failed to start the update helper (cmd.exe).");

            Log.Information("[AppUpdate] Deferred apply started via {BatchPath}", batchPath);
            Environment.Exit(0);
        }

        public static bool RunRobocopy(string stagingDir, string installDir)
        {
            Directory.CreateDirectory(installDir);

            var excludeDirs = string.Join(" ", ExcludedDirectoryNames);
            var args =
                $"\"{stagingDir}\" \"{installDir}\" /E /IS /IT /R:10 /W:2 /XD {excludeDirs} /XF *.pdb *.exp *.lib NativeHost.log";

            using var process = Process.Start(new ProcessStartInfo("robocopy", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process == null)
                return false;

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            AppendLog($"robocopy exit={process.ExitCode}");
            if (!string.IsNullOrWhiteSpace(stdout))
                AppendLog(stdout.TrimEnd());
            if (!string.IsNullOrWhiteSpace(stderr))
                AppendLog(stderr.TrimEnd());

            // https://learn.microsoft.com/windows-server/administration/windows-commands/robocopy#exit-return-codes
            return process.ExitCode < 8;
        }

        private static string BuildDeferredBatch(PendingUpdateManifest manifest, string manifestPath, string batchPath)
        {
            var logPath = Path.Combine(Directories.AppDataDirectory, "update.log");
            var mainExe = manifest.MainExePath;
            var installDir = manifest.InstallDir.TrimEnd('\\');
            var stagingDir = manifest.StagingDir.TrimEnd('\\');
            var excludeDirs = string.Join(" ", ExcludedDirectoryNames);

            return $@"@echo off
setlocal EnableExtensions EnableDelayedExpansion
set ""LOG={logPath}""
set ""MANIFEST={manifestPath}""
set ""STAGING={stagingDir}""
set ""INSTALL={installDir}""
set ""MAINEXE={mainExe}""
set ""BATCH={batchPath}""
echo --- %DATE% %TIME% deferred apply --->>""%LOG%""
echo STAGING=%STAGING%>>""%LOG%""
echo INSTALL=%INSTALL%>>""%LOG%""
:wait_loop
tasklist /FI ""IMAGENAME eq AOSharp.exe"" 2>nul | find /I ""AOSharp.exe"" >nul
if %ERRORLEVEL%==0 (
  timeout /t 1 /nobreak >nul
  goto wait_loop
)
echo AOSharp exited, running robocopy...>>""%LOG%""
robocopy ""%STAGING%"" ""%INSTALL%"" /E /IS /IT /R:10 /W:2 /XD {excludeDirs} /XF *.pdb *.exp *.lib NativeHost.log >>""%LOG%"" 2>&1
set ROBO=%ERRORLEVEL%
echo robocopy exit=!ROBO!>>""%LOG%""
if !ROBO! GEQ 8 (
  echo Update failed.>>""%LOG%""
  endlocal & exit /b 1
)
del ""%MANIFEST%"" 2>nul
start """" /D ""%INSTALL%"" ""%MAINEXE%""
echo Update succeeded, restarted AOSharp.>>""%LOG%""
del ""%BATCH%"" 2>nul
endlocal
";
        }

        private static void TryDeleteManifest()
        {
            try
            {
                var path = AppUpdateService.GetPendingManifestPath();
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // ignore
            }
        }

        private static void AppendLog(string message)
        {
            try
            {
                Directory.CreateDirectory(Directories.AppDataDirectory);
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                File.AppendAllText(Path.Combine(Directories.AppDataDirectory, "update.log"), line);
            }
            catch
            {
                // ignore
            }
        }
    }
}
