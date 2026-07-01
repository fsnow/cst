using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// Matching-semantics tests for <see cref="DictionaryIndex"/> — a faithful port of CST4's
/// <c>FormDictionary.Search</c>. The algorithm is purely string/codepoint based, so these use plain
/// ASCII "headwords" as stand-ins for IPE; the real IPE round-trip is covered in
/// <see cref="DictionaryServiceTests"/>.
/// </summary>
public class DictionaryIndexTests
{
    private static DictionaryIndex Index(params string[] words)
        => new(words.Select(w => new DictionaryWord(w, "def:" + w)));

    private static string[] Words(IReadOnlyList<DictionaryWord> r) => r.Select(w => w.Word).ToArray();

    [Fact]
    public void EmptyIndex_ReturnsEmpty()
        => Assert.Empty(Index().Lookup("anything"));

    [Fact]
    public void EmptyQuery_ReturnsEmpty()
        => Assert.Empty(Index("apple", "banana").Lookup(""));

    [Fact]
    public void ExactMatch_NoPrefixFollowers_ReturnsOnlyExact()
    {
        // "apply" does not start with "apple", so only the exact entry comes back.
        var r = Index("apple", "apply", "banana").Lookup("apple");
        Assert.Equal(new[] { "apple" }, Words(r));
    }

    [Fact]
    public void ExactMatch_WithPrefixRun_ReturnsExactThenStartsWithRun()
    {
        var r = Index("car", "card", "care", "cat").Lookup("car");
        Assert.Equal(new[] { "car", "card", "care" }, Words(r));
    }

    [Fact]
    public void Miss_PrefixOfLongerWords_ReturnsAheadRun()
    {
        // "appl" matches nothing but is a prefix of apple/apply (4 common chars each).
        var r = Index("apple", "apply", "banana").Lookup("appl");
        Assert.Equal(new[] { "apple", "apply" }, Words(r));
    }

    [Fact]
    public void Miss_AheadRunStopsAtLesserCommonPrefix()
    {
        var r = Index("apply", "banana", "band", "bandana", "cat").Lookup("ban");
        Assert.Equal(new[] { "banana", "band", "bandana" }, Words(r));
    }

    [Fact]
    public void Miss_BehindOnly_ReturnsBehindRunInAscendingOrder()
    {
        // "bandz" sits after "bandana"; the tied-max side is behind (4 common chars with band/bandana).
        var r = Index("apply", "banana", "band", "bandana", "cat").Lookup("bandz");
        Assert.Equal(new[] { "band", "bandana" }, Words(r));
    }

    [Fact]
    public void Miss_TiedBothSides_ReturnsBehindThenAhead()
    {
        // "abn" shares 2 leading chars with both neighbors "abm" (behind) and "abp" (ahead).
        var r = Index("abm", "abp").Lookup("abn");
        Assert.Equal(new[] { "abm", "abp" }, Words(r));
    }

    [Fact]
    public void Miss_NoCommonPrefix_ReturnsEmpty()
    {
        Assert.Empty(Index("abm", "abp").Lookup("zzz"));
    }

    [Fact]
    public void CountReflectsEntries()
    {
        Assert.Equal(3, Index("a", "b", "c").Count);
    }
}
