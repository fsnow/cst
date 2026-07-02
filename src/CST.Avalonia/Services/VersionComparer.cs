using System;
using System.Text.RegularExpressions;

namespace CST.Avalonia.Services
{
    /// <summary>
    /// Comparison result between two versions
    /// </summary>
    public enum VersionComparison
    {
        Current,
        PatchOutdated,
        MinorOutdated,
        MajorOutdated,
        PreReleaseToStable,
        NewerThanLatest,
        /// <summary>Either version was missing or unparseable — status can't be determined. (NET-2)</summary>
        Unknown
    }

    /// <summary>
    /// Parsed semantic version components
    /// </summary>
    public class SemanticVersion : IComparable<SemanticVersion>
    {
        public int Major { get; set; }
        public int Minor { get; set; }
        public int Patch { get; set; }
        public string? PreRelease { get; set; }
        public int? PreReleaseBuild { get; set; }
        public string Original { get; set; } = string.Empty;

        public bool IsPreRelease => !string.IsNullOrEmpty(PreRelease);

        public int CompareTo(SemanticVersion? other)
        {
            if (other == null) return 1;

            // Compare major.minor.patch
            var majorCmp = Major.CompareTo(other.Major);
            if (majorCmp != 0) return majorCmp;

            var minorCmp = Minor.CompareTo(other.Minor);
            if (minorCmp != 0) return minorCmp;

            var patchCmp = Patch.CompareTo(other.Patch);
            if (patchCmp != 0) return patchCmp;

            // If versions are equal up to patch, check pre-release
            // No pre-release is considered higher than pre-release
            if (!IsPreRelease && other.IsPreRelease) return 1;
            if (IsPreRelease && !other.IsPreRelease) return -1;

            // Both are pre-release or both are not
            if (IsPreRelease && other.IsPreRelease)
            {
                // Compare pre-release identifiers
                var preCmp = string.Compare(PreRelease, other.PreRelease, StringComparison.OrdinalIgnoreCase);
                if (preCmp != 0) return preCmp;

                // Compare pre-release build numbers if they exist
                if (PreReleaseBuild.HasValue && other.PreReleaseBuild.HasValue)
                    return PreReleaseBuild.Value.CompareTo(other.PreReleaseBuild.Value);

                if (PreReleaseBuild.HasValue && !other.PreReleaseBuild.HasValue) return 1;
                if (!PreReleaseBuild.HasValue && other.PreReleaseBuild.HasValue) return -1;
            }

            return 0;
        }

        public override string ToString() => Original;
    }

    /// <summary>
    /// Service for comparing semantic versions
    /// </summary>
    public static class VersionComparer
    {
        // Regex pattern for semantic versioning with optional pre-release.
        // Matches: 1.0.0, 5.0.0-beta.1, 5.0.0-beta1 (dot before the build is optional), 4.5.0-alpha, etc.
        private static readonly Regex VersionPattern = new Regex(
            @"^v?(\d+)\.(\d+)\.(\d+)(?:-([a-zA-Z]+)(?:\.?(\d+))?)?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Parse a version string into semantic version components
        /// </summary>
        public static SemanticVersion? ParseVersion(string? versionString)
        {
            if (string.IsNullOrWhiteSpace(versionString))
                return null;

            var match = VersionPattern.Match(versionString.Trim());
            if (!match.Success)
                return null;

            var version = new SemanticVersion
            {
                Original = versionString,
                Major = int.Parse(match.Groups[1].Value),
                Minor = int.Parse(match.Groups[2].Value),
                Patch = int.Parse(match.Groups[3].Value)
            };

            // Parse pre-release if present
            if (match.Groups[4].Success)
            {
                version.PreRelease = match.Groups[4].Value;

                // Parse pre-release build number if present
                if (match.Groups[5].Success && int.TryParse(match.Groups[5].Value, out var build))
                {
                    version.PreReleaseBuild = build;
                }
            }

            return version;
        }

        /// <summary>
        /// Compare current version against latest available version
        /// </summary>
        public static VersionComparison Compare(string? currentVersion, string? latestVersion)
        {
            var current = ParseVersion(currentVersion);
            var latest = ParseVersion(latestVersion);

            // A missing/unparseable version is not a match — surface "unknown" rather than a false
            // "you're on the latest version". (NET-2)
            if (current == null || latest == null)
                return VersionComparison.Unknown;

            var comparison = current.CompareTo(latest);

            // Current version is newer than latest (shouldn't happen normally)
            if (comparison > 0)
                return VersionComparison.NewerThanLatest;

            // Versions are equal
            if (comparison == 0)
                return VersionComparison.Current;

            // Current version is older - determine how much older
            if (current.Major < latest.Major)
                return VersionComparison.MajorOutdated;

            if (current.Minor < latest.Minor)
                return VersionComparison.MinorOutdated;

            if (current.Patch < latest.Patch)
                return VersionComparison.PatchOutdated;

            // Same version but current is pre-release and latest is stable
            if (current.IsPreRelease && !latest.IsPreRelease &&
                current.Major == latest.Major &&
                current.Minor == latest.Minor &&
                current.Patch == latest.Patch)
            {
                return VersionComparison.PreReleaseToStable;
            }

            // At this point comparison < 0 (current is older per SemanticVersion.CompareTo) and
            // major.minor.patch are equal (the outdated-major/minor/patch and pre-release→stable cases
            // returned above), so both are pre-release and current is the older pre-release — e.g.
            // 5.0.0-alpha vs 5.0.0-alpha.1, 5.0.0-beta vs 5.0.0-beta.2, or alpha vs beta. Surface a
            // pre-release/patch update rather than falling through to "Current", which contradicted
            // CompareTo and offered no update. (NET-3)
            if (current.IsPreRelease && latest.IsPreRelease)
                return VersionComparison.PatchOutdated;

            return VersionComparison.Current;
        }

        /// <summary>
        /// Check if a version matches a pattern (supports wildcards)
        /// </summary>
        public static bool MatchesPattern(string? version, string? pattern)
        {
            if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(pattern))
                return false;

            // Convert pattern to regex
            // Example: "5.0.0-alpha.*" -> matches any alpha version of 5.0.0
            var regexPattern = pattern
                .Replace(".", @"\.")
                .Replace("*", ".*")
                .Replace("?", ".");

            return Regex.IsMatch(version, $"^{regexPattern}$", RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Strip SemVer build metadata (everything from the first '+') and surrounding whitespace, e.g.
        /// "5.0.0-beta.5+abc1234" -> "5.0.0-beta.5". Returns the trimmed input when there is no '+', and an
        /// empty string for null/blank. Single source of truth so callers don't hand-roll Substring/Split. (#71)
        /// </summary>
        public static string StripBuildMetadata(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return string.Empty;

            var trimmed = version.Trim();
            var plus = trimmed.IndexOf('+');
            return plus >= 0 ? trimmed.Substring(0, plus) : trimmed;
        }

        /// <summary>
        /// Get a human-readable description of the version comparison
        /// </summary>
        public static string GetComparisonDescription(VersionComparison comparison, string? latestVersion)
        {
            return comparison switch
            {
                VersionComparison.Current => "You're running the latest version",
                VersionComparison.PatchOutdated => $"A patch update is available ({latestVersion})",
                VersionComparison.MinorOutdated => $"A minor update is available ({latestVersion})",
                VersionComparison.MajorOutdated => $"A major update is available ({latestVersion})",
                VersionComparison.PreReleaseToStable => $"The stable version is now available ({latestVersion})",
                VersionComparison.NewerThanLatest => "You're running a development version",
                VersionComparison.Unknown => "Version status unknown",
                _ => "Version status unknown"
            };
        }
    }
}