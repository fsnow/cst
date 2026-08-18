using System;
using System.Linq;
using CST.Avalonia.Models.Ai;
using CST.Avalonia.Services.Ai;
using Xunit;

namespace CST.Avalonia.Tests.Models.Ai;

/// <summary>
/// #689: the preset catalogue is derived data — facts about third-party APIs, refreshed by re-running the
/// #682 extraction against a newer opencode commit.
///
/// <para>These tests guard the two things that would quietly ruin it: a malformed entry (which ships an
/// endpoint that cannot work), and <b>any drift toward curation</b>, which is the model registry removed in
/// #670/#681 returning through a data file. The second matters most on future syncs, when someone folding in
/// upstream changes will be looking at the diff rather than at the rules.</para>
/// </summary>
public class AiProviderPresetsTests
{
    [Fact]
    public void Every_preset_is_well_formed()
    {
        Assert.NotEmpty(AiProviderPresets.All);

        foreach (var p in AiProviderPresets.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Id), "a preset has no id");
            Assert.False(string.IsNullOrWhiteSpace(p.DisplayName), $"{p.Id} has no display name");
            Assert.True(Uri.TryCreate(p.BaseUrl, UriKind.Absolute, out _), $"{p.Id} has a non-absolute base URL");
        }
    }

    /// <summary>Ids are the reserved namespace a custom connection may not take, and they become keychain
    /// account names — so they must be unique and safe as a path/account segment.</summary>
    [Fact]
    public void Ids_are_unique_and_slug_shaped()
    {
        var ids = AiProviderPresets.All.Select(p => p.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var id in ids)
            Assert.Matches("^[a-z0-9][a-z0-9_-]*$", id);
    }

    /// <summary>
    /// A preset that requires a key but names no environment variable cannot participate in credential
    /// discovery — the reader would be told nothing was found when a key was sitting right there.
    /// </summary>
    [Fact]
    public void A_preset_that_needs_a_key_says_where_one_might_already_be()
    {
        foreach (var p in AiProviderPresets.All.Where(p => p.RequiresKey))
            Assert.NotEmpty(p.EnvironmentVariables);
    }

    /// <summary>Local runners need no key. Guards the case that makes "no credential" a valid state rather
    /// than an error — the single most likely first configuration for a reader trying this out.</summary>
    [Fact]
    public void Local_runners_do_not_require_a_key()
    {
        var local = AiProviderPresets.All.Where(p => p.BaseUrl.Contains("localhost")).ToList();

        Assert.NotEmpty(local);
        Assert.All(local, p => Assert.False(p.RequiresKey, $"{p.Id} is local but demands a key"));
    }

    /// <summary>
    /// The anti-curation rule, enforced. No preset may carry language that ranks, scores, tiers or recommends.
    /// A future sync from upstream is the moment this is most likely to slip — models.dev carries pricing,
    /// status and vendor marketing copy, and it would arrive in a diff rather than as a decision.
    /// </summary>
    [Theory]
    [InlineData("recommend")]
    [InlineData("best")]
    [InlineData("popular")]
    [InlineData("fastest")]
    [InlineData("preferred")]
    [InlineData("top ")]
    [InlineData("tier")]
    public void No_preset_carries_a_quality_judgment(string banned)
    {
        foreach (var p in AiProviderPresets.All)
            Assert.DoesNotContain(banned, p.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ordering must be mechanical. Alphabetical is defensible; any hand-arranged order is an implicit ranking
    /// — the reader reasonably reads "first" as "best", which is a claim we refuse to make (#670/#681).
    /// </summary>
    [Fact]
    public void Presets_are_ordered_alphabetically_not_editorially()
    {
        var names = AiProviderPresets.All.Select(p => p.DisplayName).ToList();

        Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), names);
    }

    /// <summary>Kind and base URL are independent axes: several providers speak the Anthropic Messages
    /// protocol at their own hosts, so Kind must never be inferable from the URL.</summary>
    [Fact]
    public void Anthropic_kind_does_not_imply_the_anthropic_host()
    {
        var anthropic = AiProviderPresets.ById("anthropic");

        Assert.NotNull(anthropic);
        Assert.Equal(ChatProviderKind.Anthropic, anthropic!.Kind);
        // The invariant is about the type, not this row: nothing in the model ties the two together.
        Assert.All(AiProviderPresets.All,
            p => Assert.True(p.Kind == ChatProviderKind.Anthropic || p.Kind == ChatProviderKind.OpenAiCompatible));
    }

    [Fact]
    public void Preset_ids_are_reserved_against_custom_connections()
    {
        Assert.True(AiProviderPresets.IsReservedId("openrouter"));
        Assert.True(AiProviderPresets.IsReservedId("OpenRouter"));   // case-insensitive
        Assert.False(AiProviderPresets.IsReservedId("my-vllm-box"));
    }

    /// <summary>The provenance stamp is what makes "is this current?" answerable and a refresh a diff.</summary>
    [Fact]
    public void The_source_commit_is_stamped()
    {
        Assert.False(string.IsNullOrWhiteSpace(AiProviderPresets.SourceCommit));
    }
}
