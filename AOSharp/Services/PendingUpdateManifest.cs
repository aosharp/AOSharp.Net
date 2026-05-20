using Newtonsoft.Json;

namespace AOSharp.Services
{
    public sealed class PendingUpdateManifest
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("installDir")]
        public string InstallDir { get; set; }

        [JsonProperty("stagingDir")]
        public string StagingDir { get; set; }

        [JsonProperty("assetSha256")]
        public string AssetSha256 { get; set; }

        [JsonProperty("mainExePath")]
        public string MainExePath { get; set; }

        [JsonProperty("updaterExePath")]
        public string UpdaterExePath { get; set; }
    }
}
