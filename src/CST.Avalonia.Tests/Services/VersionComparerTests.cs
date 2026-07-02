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
        [InlineData("5.0.0-alpha", "5.0.0-alpha.1", VersionComparison.PatchOutdated)]   // NET-3: build added
        [InlineData("5.0.0-beta", "5.0.0-beta.2", VersionComparison.PatchOutdated)]     // NET-3: no build vs build
        [InlineData("5.0.0-alpha.2", "5.0.0-beta.1", VersionComparison.PatchOutdated)]  // alpha < beta
        [InlineData("5.0.0-alpha.1", "5.0.0-alpha", VersionComparison.NewerThanLatest)] // current is newer
        public void Compare_VariousVersions_ReturnsCorrectComparison(string current, string latest, VersionComparison expected)
        {
            var result = VersionComparer.Compare(current, latest);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("5.0.0-beta.1", null)]         // NET-2: beta channel removed post-GA -> null latest
        [InlineData(null, "5.0.0")]
        [InlineData("5.0.0", "")]
        [InlineData("5.0.0", "not-a-version")]
        [InlineData("garbage", "5.0.0")]
        public void Compare_MissingOrUnparseable_ReturnsUnknown(string? current, string? latest)
        {
            // Must NOT be Current: that would render as a false "you're on the latest version". (NET-2)
            Assert.Equal(VersionComparison.Unknown, VersionComparer.Compare(current, latest));
        }

        [Theory]
        [InlineData("5.0.0", 5, 0, 0, null, null)]
        [InlineData("5.0.0-beta.1", 5, 0, 0, "beta", 1)]
        [InlineData("5.0.0-beta1", 5, 0, 0, "beta", 1)] // NET-3: dot before build is optional
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
        [InlineData(VersionComparison.Unknown, "5.0.0", "Version status unknown")]
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

        [Theory]
        [InlineData("5.0.0-beta.5+abc1234", "5.0.0-beta.5")] // SemVer build metadata stripped
        [InlineData("5.0.0+build.99", "5.0.0")]
        [InlineData("5.0.0-beta.5", "5.0.0-beta.5")]          // no metadata -> unchanged
        [InlineData("  5.0.0+x  ", "5.0.0")]                  // trimmed
        [InlineData("5.0.0.0", "5.0.0.0")]                    // 4-part assembly version, no '+'
        [InlineData("+abc", "")]                              // metadata only
        [InlineData("", "")]
        [InlineData("   ", "")]
        [InlineData(null, "")]
        public void StripBuildMetadata_RemovesEverythingFromFirstPlus(string? input, string expected)
            => Assert.Equal(expected, VersionComparer.StripBuildMetadata(input));

        [Fact]
        public void ParseVersion_ToleratesBuildMetadata()
        {
            // The stripped form and the raw +metadata form parse to the same components.
            var stripped = VersionComparer.ParseVersion("5.0.0-beta.5");
            var withMeta = VersionComparer.ParseVersion(VersionComparer.StripBuildMetadata("5.0.0-beta.5+deadbee"));
            Assert.NotNull(stripped);
            Assert.NotNull(withMeta);
            Assert.Equal(0, stripped!.CompareTo(withMeta));
        }
    }
}