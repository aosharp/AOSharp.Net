using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace AOSharp
{
    /// <summary>
    /// Syncs the non-client (caption) area with the Windows 10/11 per-app light/dark setting.
    /// </summary>
    internal static class WindowsTitleBarTheme
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);

        /// <summary>True if Settings → Personalize → "Choose your mode" uses light for apps (default if key missing).</summary>
        public static bool IsAppsLightTheme()
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", writable: false);
                if (key?.GetValue("AppsUseLightTheme") is int v)
                    return v != 0;
            }
            catch
            {
                // ignore
            }
            return true;
        }

        public static void ApplyToWindow(Window window)
        {
            nint hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == 0)
                return;

            bool useDarkCaption = !IsAppsLightTheme();
            int value = useDarkCaption ? 1 : 0;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
    }
}
