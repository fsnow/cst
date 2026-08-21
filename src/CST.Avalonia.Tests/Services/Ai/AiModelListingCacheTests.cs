using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CST.Avalonia.Services.Ai;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// Remembering each connection's listing between sessions. (#790)
///
/// <para>The reason this exists at all: the listing lived in memory for one window and was discarded, and it
/// is fetched only when a group is first expanded — so every launch opened on the reader's own saved models
/// where the provider offers many more. Three against thirteen, in the report that prompted it.</para>
/// </summary>
public class AiModelListingCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "cst-listing-cache-" + Guid.NewGuid().ToString("N"));

    private AiModelListingCache Cache() =>
        new(Path.Combine(_dir, "ai-model-listings.json"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Nothing_cached_reads_as_empty_rather_than_null()
    {
        Assert.Empty(Cache().Get("groq"));
    }

    /// <summary>The whole point: a listing recorded in one session is there for the next one, before any
    /// network call.</summary>
    [Fact]
    public void A_listing_survives_into_a_new_instance()
    {
        Cache().Put("groq", new[]
        {
            new AiCatalogModel("openai/gpt-oss-120b", "GPT-OSS 120B"),
            new AiCatalogModel("qwen/qwen3.6-27b", "Qwen3.6 27B"),
        });

        var reopened = Cache().Get("groq");

        Assert.Equal(
            new[] { "openai/gpt-oss-120b", "qwen/qwen3.6-27b" },
            reopened.Select(m => m.Id));
    }

    /// <summary>Connections do not share an entry — the bug class #678 was, one layer up.</summary>
    [Fact]
    public void Connections_keep_separate_listings()
    {
        var cache = Cache();
        cache.Put("groq", new[] { new AiCatalogModel("a", "A") });
        cache.Put("openrouter", new[] { new AiCatalogModel("b", "B") });

        Assert.Equal("a", Cache().Get("groq").Single().Id);
        Assert.Equal("b", Cache().Get("openrouter").Single().Id);
    }

    [Fact]
    public void A_later_listing_replaces_the_earlier_one()
    {
        var cache = Cache();
        cache.Put("groq", new[] { new AiCatalogModel("old", "Old") });
        cache.Put("groq", new[] { new AiCatalogModel("new", "New") });

        Assert.Equal("new", Cache().Get("groq").Single().Id);
    }

    /// <summary>
    /// A connection removed and recreated under the same id must not inherit the old one's listing: it looks
    /// like the app knowing something it cannot know, and is wrong the moment the new connection points
    /// somewhere else.
    /// </summary>
    [Fact]
    public void A_listing_is_dropped_when_its_connection_is_gone()
    {
        var cache = Cache();
        cache.Put("groq", new[] { new AiCatalogModel("a", "A") });
        cache.Put("openrouter", new[] { new AiCatalogModel("b", "B") });

        cache.Forget("groq");

        Assert.Empty(Cache().Get("groq"));
        Assert.Single(Cache().Get("openrouter"));
    }

    /// <summary>The published facts travel with the ids, or the per-turn picker's hover card goes blank on a
    /// cache-seeded model. (#726, #671)</summary>
    [Fact]
    public void Published_facts_survive_the_round_trip()
    {
        Cache().Put("groq", new[]
        {
            new AiCatalogModel(
                "m", "M",
                ContextLength: 131072,
                SupportedParameters: new[] { "reasoning_effort" },
                ReasoningEfforts: new[] { "low", "high" },
                DefaultReasoningEffort: "high"),
        });

        var model = Cache().Get("groq").Single();

        Assert.Equal(131072, model.ContextLength);
        Assert.Equal(new[] { "low", "high" }, model.ReasoningEfforts);
        Assert.Equal("high", model.DefaultReasoningEffort);
        Assert.True(model.AcceptsReasoningEffort);
    }

    /// <summary>
    /// An unreadable cache is an inconvenience, never a failure: the reader waits for a fetch. Throwing would
    /// turn a faster copy of something we can ask for again into a reason the Models tab cannot render.
    /// </summary>
    [Fact]
    public void An_unreadable_cache_reads_as_empty_and_is_replaced_on_the_next_write()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "ai-model-listings.json"), "{ this is not json");

        var cache = Cache();
        Assert.Empty(cache.Get("groq"));

        cache.Put("groq", new[] { new AiCatalogModel("a", "A") });
        Assert.Single(Cache().Get("groq"));
    }
}
