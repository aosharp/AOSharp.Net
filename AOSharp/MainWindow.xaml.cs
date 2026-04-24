using System;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using AOSharp.Data;
using AOSharp.Models;
using AOSharp.Services;
using Serilog;

namespace AOSharp
{
    public partial class MainWindow : Window
    {
        private Config _config;
        private ProfilesModel _profilesModel;
        private WebMessageBridge _bridge;

        public MainWindow()
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.File("Log.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            _config = Config.Load(Directories.ConfigFilePath);
            _profilesModel = new ProfilesModel(_config);

            InitializeComponent();

            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await WebView.EnsureCoreWebView2Async();

                // Map virtual hostname → React dist folder
                string distPath = GetDistPath();
                WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "aosharp.ui",
                    distPath,
                    CoreWebView2HostResourceAccessKind.Allow);

                // Wire up bridge
                var repoCompiler = new RepoCompiler();
                _bridge = new WebMessageBridge(WebView, _config, _profilesModel, repoCompiler, Dispatcher);
                WebView.CoreWebView2.WebMessageReceived += _bridge.OnMessageReceived;

                // Log any navigation / console errors for diagnostics
                WebView.CoreWebView2.NavigationCompleted += (_, args) =>
                {
                    if (!args.IsSuccess)
                        Log.Error($"[UI] Navigation failed: {args.WebErrorStatus}");
                    else
                        Log.Information("[UI] Navigation succeeded");
                };

                WebView.CoreWebView2.WebResourceResponseReceived += (_, args) =>
                {
                    if (args.Response.StatusCode >= 400)
                        Log.Warning($"[UI] Resource error {args.Response.StatusCode}: {args.Request.Uri}");
                };

                WebView.Source = new Uri("https://aosharp.ui/index.html");
            }
            catch (Exception ex)
            {
                Log.Error($"WebView2 initialisation failed: {ex}");
                MessageBox.Show($"Failed to initialise WebView2:\n{ex.Message}", "AO#", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string GetDistPath()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;

            // Deployed: ui/ folder copied next to the exe by build.bat
            string deployed = Path.GetFullPath(Path.Combine(exeDir, "ui"));
            if (Directory.Exists(deployed))
            {
                Log.Information($"[UI] Using deployed dist: {deployed}");
                return deployed;
            }

            // Dev: exe is at Loader\bin\{Config}\net8.0-windows\ → go up 3 to reach Loader\
            string dev = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "AOSharp.UI", "dist"));
            Log.Information($"[UI] Using dev dist: {dev} (exists={Directory.Exists(dev)})");
            return dev;
        }
    }
}
