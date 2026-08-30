using TPSteelSeriesGG;
using Xunit;

namespace TPSteelSeriesGG.Tests;

/// <summary>
/// The update checker's pure logic: tag parsing and version comparison.
/// The network side is deliberately untested; it is designed to fail silently.
/// </summary>
public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v2.1.0", "2.1.0")]
    [InlineData("2.1.0", "2.1.0")]
    [InlineData("V2.1.0", "2.1.0")]
    [InlineData(" v2.1.0 ", "2.1.0")]
    [InlineData("v2.1.0-beta.1", "2.1.0")]
    [InlineData("v2.1.0+build42", "2.1.0")]
    public void ParseVersion_AcceptsUsualTagShapes(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), UpdateChecker.ParseVersion(tag));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v2")]      // needs at least major.minor.patch
    [InlineData("v2.1")]
    public void ParseVersion_RejectsUnusableTags(string? tag)
    {
        Assert.Null(UpdateChecker.ParseVersion(tag));
    }

    [Theory]
    [InlineData("v2.0.1", "2.0.0", true)]
    [InlineData("v2.1.0", "2.0.9", true)]
    [InlineData("v2.0.0", "2.0.0", false)]
    [InlineData("v1.9.9", "2.0.0", false)]
    public void IsNewer_ComparesNumbersFirst(string remoteTag, string current, bool expected)
    {
        Assert.Equal(expected, UpdateChecker.IsNewer(remoteTag, current));
    }

    [Theory]
    [InlineData("v2.1.0", "2.1.0-alpha.1", true)]   // alpha user is told about the final
    [InlineData("v2.1.0", "2.1.0-rc.2", true)]
    [InlineData("v2.1.0-rc.1", "2.1.0", false)]     // a mistagged prerelease never beats the release
    [InlineData("v2.1.0-alpha.2", "2.1.0-alpha.1", false)] // same numbers, both prerelease: no claim
    [InlineData("v2.2.0", "2.3.0-alpha.1", false)]  // alpha of a future version stays ahead
    [InlineData("garbage", "2.0.0", false)]
    public void IsNewer_HandlesPrereleases(string remoteTag, string current, bool expected)
    {
        Assert.Equal(expected, UpdateChecker.IsNewer(remoteTag, current));
    }

    [Fact]
    public void HasPrereleaseSuffix_IgnoresBuildMetadata()
    {
        Assert.True(UpdateChecker.HasPrereleaseSuffix("2.1.0-alpha.1+abc123"));
        Assert.False(UpdateChecker.HasPrereleaseSuffix("2.1.0+abc123"));
    }
}
