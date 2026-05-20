namespace AOSharp.Services
{
    /// <summary>GitHub Releases source for portable loader updates.</summary>
    public static class AppUpdateConstants
    {
        public const string GitHubOwner = "aosharp";
        public const string GitHubRepo = "AOSharp.Net";
        public const string ZipAssetName = "AOSharp-win-x64.zip";
        public const string Sha256AssetName = "AOSharp-win-x64.zip.sha256";
        public const string UserAgent = "AOSharp-Loader-Updater";

        public static string LatestReleaseApiUrl =>
            $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
    }
}
