using Xunit;
using CST.Avalonia.Services;

namespace CST.Avalonia.Tests.Services
{
    public class VersionComparerTests
    {
        [Theory]
        [InlineData("5.0.0", "5.0.0", VersionComparison.Current)]
        [InlineData("5.0.0", "5.0.1", VersionComparison.PatchOutdated)]
        [InlineData("5.0.0", "5.1.0", VersionComparison.MinorOutdated)]
        [InlineData("5.0.0", "6.0.0", VersionComparison.MajorOutdated)]
        [InlineData("5.0.1", "5.0.0", VersionComparison.NewerThanLatest)]
        [InlineData("5.0.0-beta.1", "5.0.0-beta.1", VersionComparison.Current)]
        [InlineData("5.0.0-beta.1", "5.0.0-beta.2", VersionComparison.PatchOutdated)]
        [InlineData("5.0.0-beta.1", "5.0.0", VersionComparison.PreReleaseToStable)]
        [InlineData("v5.0.0", "5.0.0", VersionComparison.Current)] // Handle 'v' prefix
        public void Compare_VariousVersions_ReturnsCorrectComparison(string current, string latest, VersionComparison expected)
        {
            var result = VersionComparer.Compare(current, latest);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("5.0.0", 5, 0, 0, null, null)]
        [InlineData("5.0.0-beta.1", 5, 0, 0, "beta", 1)]
        [InlineData("5.0.0-alpha", 5, 0, 0, "alpha", null)]
        [InlineData("v5.0.0", 5, 0, 0, null, null)]
        public void ParseVersion_ValidVersions_ParsesCorrectly(string version, int major, int minor, int patch, string? preRelease, int? preReleaseBuild)
        {
            var parsed = VersionComparer.ParseVersion(version);

            Assert.NotNull(parsed);
            Assert.Equal(major, parsed.Major);
            Assert.Equal(minor, parsed.Minor);
            Assert.Equal(patch, parsed.Patch);
            Assert.Equal(preRelease, parsed.PreRelease);
            Assert.Equal(preReleaseBuild, parsed.PreReleaseBuild);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-a-version")]
        [InlineData("5")]
        [InlineData("5.0")]
        public void ParseVersion_InvalidVersions_ReturnsNull(string? version)
        {
            var parsed = VersionComparer.ParseVersion(version);
            Assert.Null(parsed);
        }

        [Theory]
        [InlineData("5.0.0-alpha.1", "5.0.0-alpha.*", true)]
        [InlineData("5.0.0-alpha.2", "5.0.0-alpha.*", true)]
        [InlineData("5.0.0-beta.1", "5.0.0-alpha.*", false)]
        [InlineData("5.0.0", "5.0.*", true)]
        [InlineData("5.1.0", "5.0.*", false)]
        [InlineData("5.0.0", "5.?.0", true)]
        [InlineData("5.1.0", "5.?.0", true)]
        [InlineData("5.2.0", "5.?.0", true)]
        public void MatchesPattern_VariousPatterns_MatchesCorrectly(string version, string pattern, bool expected)
        {
            var result = VersionComparer.MatchesPattern(version, pattern);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(VersionComparison.Current, "5.0.0", "You're running the latest version")]
        [InlineData(VersionComparison.PatchOutdated, "5.0.1", "A patch update is available (5.0.1)")]
        [InlineData(VersionComparison.MinorOutdated, "5.1.0", "A minor update is available (5.1.0)")]
        [InlineData(VersionComparison.MajorOutdated, "6.0.0", "A major update is available (6.0.0)")]
        [InlineData(VersionComparison.PreReleaseToStable, "5.0.0", "The stable version is now available (5.0.0)")]
        [InlineData(VersionComparison.NewerThanLatest, "5.0.0", "You're running a development version")]
        public void GetComparisonDescription_VariousComparisons_ReturnsCorrectDescription(VersionComparison comparison, string latestVersion, string expectedStart)
        {
            var description = VersionComparer.GetComparisonDescription(comparison, latestVersion);
            Assert.StartsWith(expectedStart, description);
        }

        [Fact]
        public void SemanticVersion_CompareTo_SortsCorrectly()
        {
            var versions = new[]
            {
                VersionComparer.ParseVersion("4.0.0"),
                VersionComparer.ParseVersion("5.0.0-alpha.1"),
                VersionComparer.ParseVersion("5.0.0-alpha.2"),
                VersionComparer.ParseVersion("5.0.0-beta.1"),
                VersionComparer.ParseVersion("5.0.0"),
                VersionComparer.ParseVersion("5.0.1"),
                VersionComparer.ParseVersion("5.1.0"),
                VersionComparer.ParseVersion("6.0.0")
            };

            var sorted = versions.OrderBy(v => v).ToArray();

            Assert.Equal("4.0.0", sorted[0]?.ToString());
            Assert.Equal("5.0.0-alpha.1", sorted[1]?.ToString());
            Assert.Equal("5.0.0-alpha.2", sorted[2]?.ToString());
            Assert.Equal("5.0.0-beta.1", sorted[3]?.ToString());
            Assert.Equal("5.0.0", sorted[4]?.ToString());
            Assert.Equal("5.0.1", sorted[5]?.ToString());
            Assert.Equal("5.1.0", sorted[6]?.ToString());
            Assert.Equal("6.0.0", sorted[7]?.ToString());
        }
    }
}