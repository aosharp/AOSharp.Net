using System;
using System.IO;

namespace AOSharp.Models
{
    /// <summary>
    /// A <c>Manifest.json</c> <c>dependencies</c> entry: a git repo URL, optionally with a project path,
    /// or a <c>.csproj</c> path relative to the declaring project folder.
    /// </summary>
    public sealed class ManifestDependencyReference
    {
        public string RawEntry { get; private set; }

        /// <summary>Clone key for <see cref="RepoCompiler.GetLocalRepoPath"/>.</summary>
        public string RepoUrl { get; private set; }

        /// <summary>Project path within the cloned repo (forward slashes), or null to pick a default .csproj.</summary>
        public string CsprojRelativePath { get; private set; }

        public static bool TryParse(
            string entry,
            string declaringProjectDirectory,
            string declaringRepoRoot,
            out ManifestDependencyReference reference)
        {
            reference = null;
            if (string.IsNullOrWhiteSpace(entry))
                return false;

            entry = entry.Trim();
            if (!entry.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                reference = new ManifestDependencyReference
                {
                    RawEntry = entry,
                    RepoUrl = entry
                };
                return true;
            }

            if (entry.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                entry.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseHttpUrlWithCsproj(entry, out var repoUrl, out var relativeCsproj))
                {
                    reference = new ManifestDependencyReference
                    {
                        RawEntry = entry,
                        RepoUrl = repoUrl,
                        CsprojRelativePath = relativeCsproj
                    };
                    return true;
                }

                return false;
            }

            if (Path.IsPathRooted(entry) && File.Exists(entry) &&
                !IsIntermediateCsprojUnderRepo(entry, declaringRepoRoot))
            {
                reference = ResolveLocalCsprojPath(entry, declaringRepoRoot, entry);
                return reference != null;
            }

            if (!string.IsNullOrEmpty(declaringProjectDirectory))
            {
                var combined = Path.GetFullPath(Path.Combine(declaringProjectDirectory, entry));
                if (File.Exists(combined) &&
                    !IsIntermediateCsprojUnderRepo(combined, declaringRepoRoot))
                {
                    reference = ResolveLocalCsprojPath(combined, declaringRepoRoot, entry);
                    return reference != null;
                }
            }

            return false;
        }

        public string GetBuildKey() =>
            (RepoUrl ?? string.Empty) + "|" + (CsprojRelativePath ?? string.Empty);

        public bool SpecifiesCsproj => !string.IsNullOrEmpty(CsprojRelativePath);

        private static ManifestDependencyReference ResolveLocalCsprojPath(
            string absoluteCsprojPath,
            string declaringRepoRoot,
            string rawEntry)
        {
            absoluteCsprojPath = Path.GetFullPath(absoluteCsprojPath);
            if (!string.IsNullOrEmpty(declaringRepoRoot) && Directory.Exists(declaringRepoRoot))
            {
                var repoRoot = Path.GetFullPath(declaringRepoRoot);
                if (absoluteCsprojPath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
                {
                    var relative = Path.GetRelativePath(repoRoot, absoluteCsprojPath)
                        .Replace('\\', '/');
                    return new ManifestDependencyReference
                    {
                        RawEntry = rawEntry,
                        RepoUrl = null,
                        CsprojRelativePath = relative
                    };
                }
            }

            return null;
        }

        private static bool IsIntermediateCsprojUnderRepo(string csprojPath, string repoRoot)
        {
            if (string.IsNullOrEmpty(csprojPath))
                return false;

            if (string.IsNullOrEmpty(repoRoot) || !Directory.Exists(repoRoot))
                return PathContainsDirectorySegment(csprojPath, "obj");

            string relative;
            try
            {
                relative = Path.GetRelativePath(Path.GetFullPath(repoRoot), Path.GetFullPath(csprojPath));
            }
            catch
            {
                return false;
            }

            if (relative.StartsWith("..", StringComparison.Ordinal))
                return PathContainsDirectorySegment(csprojPath, "obj");

            foreach (var segment in relative.Split(
                         new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("ref", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("refint", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("refs", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool PathContainsDirectorySegment(string filePath, string segment)
        {
            var sep = Path.DirectorySeparatorChar;
            var alt = Path.AltDirectorySeparatorChar;
            return filePath.Contains($"{sep}{segment}{sep}", StringComparison.OrdinalIgnoreCase) ||
                   filePath.Contains($"{alt}{segment}{alt}", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseHttpUrlWithCsproj(string entry, out string repoUrl, out string csprojRelative)
        {
            repoUrl = null;
            csprojRelative = null;

            var csprojEnd = entry.IndexOf(".csproj", StringComparison.OrdinalIgnoreCase) + ".csproj".Length;
            if (!Uri.TryCreate(entry.Substring(0, csprojEnd), UriKind.Absolute, out var uri))
                return false;

            var segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
                return false;

            var owner = segments[0];
            var repo = segments[1];
            if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                repo = repo[..^4];

            repoUrl = $"{uri.Scheme}://{uri.Host}/{owner}/{repo}";

            var startIdx = 2;
            if (segments.Length > 3 &&
                (segments[2].Equals("tree", StringComparison.OrdinalIgnoreCase) ||
                 segments[2].Equals("blob", StringComparison.OrdinalIgnoreCase)))
                startIdx = 4;

            if (segments.Length <= startIdx)
                return false;

            csprojRelative = string.Join("/", segments, startIdx, segments.Length - startIdx);
            return csprojRelative.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
        }
    }
}
