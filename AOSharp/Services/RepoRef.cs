using System;
using System.IO;
using AOSharp;

namespace AOSharp.Services
{
    /// <summary>
    /// Git remote URL plus optional branch or commit pin for a cloned repository.
    /// When <see cref="Commit"/> is set it takes precedence over <see cref="Branch"/>.
    /// </summary>
    public readonly struct RepoRef : IEquatable<RepoRef>
    {
        public string Url { get; }
        public string Branch { get; }
        public string Commit { get; }

        public RepoRef(string url, string branch = null, string commit = null)
        {
            Url = NormalizeUrl(url);
            Branch = NormalizeRef(branch);
            Commit = NormalizeRef(commit);
        }

        public static RepoRef FromUrl(string url) => new RepoRef(url);

        public static RepoRef FromPlugin(PluginModel plugin)
        {
            if (plugin == null || string.IsNullOrEmpty(plugin.RepoUrl))
                return default;
            return new RepoRef(plugin.RepoUrl, plugin.RepoBranch, plugin.RepoCommit);
        }

        public bool IsValid => !string.IsNullOrEmpty(Url);

        public bool IsCommitPinned => !string.IsNullOrEmpty(Commit);

        public bool IsBranchPinned => !IsCommitPinned && !string.IsNullOrEmpty(Branch);

        /// <summary>Stable key for clone directory and plugin config hashing.</summary>
        public string IdentityKey =>
            IsValid ? $"{Url}|{Branch ?? ""}|{Commit ?? ""}" : string.Empty;

        public bool Equals(RepoRef other) =>
            string.Equals(Url, other.Url, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Branch ?? "", other.Branch ?? "", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Commit ?? "", other.Commit ?? "", StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object obj) => obj is RepoRef other && Equals(other);

        public override int GetHashCode()
        {
            var h = StringComparer.OrdinalIgnoreCase.GetHashCode(Url ?? "");
            h = (h * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Branch ?? "");
            h = (h * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Commit ?? "");
            return h;
        }

        /// <summary>
        /// Display name for a repo plugin project. Appends <c> (branch)</c> when a non-default branch is pinned.
        /// </summary>
        public string FormatPluginDisplayName(string csprojFileName, string defaultBranch)
        {
            var baseName = Path.GetFileNameWithoutExtension(csprojFileName) ?? csprojFileName;
            if (!IsBranchPinned || string.IsNullOrEmpty(Branch))
                return baseName;

            if (!string.IsNullOrEmpty(defaultBranch) &&
                string.Equals(Branch, defaultBranch, StringComparison.OrdinalIgnoreCase))
                return baseName;

            return $"{baseName} ({Branch})";
        }

        public static string NormalizeUrl(string url) =>
            string.IsNullOrWhiteSpace(url) ? null : url.Trim().TrimEnd('/');

        private static string NormalizeRef(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
