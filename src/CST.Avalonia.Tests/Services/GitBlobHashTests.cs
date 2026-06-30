using System;
using System.Text;
using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// Unit tests for the Git-blob-SHA integrity helper used to verify downloaded XML files against the SHA the
/// GitHub tree API reports (#65). Test vectors cross-checked with `git hash-object`.
/// </summary>
public class GitBlobHashTests
{
    [Fact]
    public void Compute_EmptyContent_MatchesGitEmptyBlobId()
        => Assert.Equal("e69de29bb2d1d6434b8b29ae775ad8c2e48c5391", GitBlobHash.Compute(Array.Empty<byte>()));

    [Fact]
    public void Compute_KnownContent_MatchesGitHashObject()
        => Assert.Equal("3b18e512dba79e4c8300dd08aeb37f8e728b8dad",
                        GitBlobHash.Compute(Encoding.ASCII.GetBytes("hello world\n")));

    [Fact]
    public void Compute_Is40LowercaseHexChars()
    {
        var sha = GitBlobHash.Compute(Encoding.UTF8.GetBytes("anything"));
        Assert.Equal(40, sha.Length);
        Assert.Matches("^[0-9a-f]{40}$", sha);
    }

    [Fact]
    public void Matches_CorrectSha_True()
        => Assert.True(GitBlobHash.Matches(Encoding.ASCII.GetBytes("hello world\n"),
                                           "3b18e512dba79e4c8300dd08aeb37f8e728b8dad"));

    [Fact]
    public void Matches_IsCaseInsensitive()
        => Assert.True(GitBlobHash.Matches(Encoding.ASCII.GetBytes("hello world\n"),
                                           "3B18E512DBA79E4C8300DD08AEB37F8E728B8DAD"));

    [Fact]
    public void Matches_WrongSha_False()
        => Assert.False(GitBlobHash.Matches(Encoding.ASCII.GetBytes("hello world\n"),
                                            "0000000000000000000000000000000000000000"));

    [Fact]
    public void Matches_CorruptedContent_False()
    {
        // One byte flipped (same length): the SHA must still catch it.
        var good = Encoding.ASCII.GetBytes("hello world\n");
        var expected = GitBlobHash.Compute(good);
        var corrupt = (byte[])good.Clone();
        corrupt[0] ^= 0xFF;
        Assert.False(GitBlobHash.Matches(corrupt, expected));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Matches_NullOrEmptyExpected_False(string? expected)
        => Assert.False(GitBlobHash.Matches(Encoding.ASCII.GetBytes("x"), expected));

    [Fact]
    public void Matches_Utf16Content_StableAndDetectsTruncation()
    {
        // The corpus is UTF-16-LE; the hash is over the exact bytes (encoding-agnostic), so it round-trips and
        // a truncated (partial) download is detected.
        var bytes = Encoding.Unicode.GetBytes("\u0927\u092E\u094D\u092E"); // "dhamma"
        var sha = GitBlobHash.Compute(bytes);
        Assert.True(GitBlobHash.Matches(bytes, sha));
        Assert.False(GitBlobHash.Matches(bytes[..^2], sha)); // 2 bytes short = partial download
    }
}
