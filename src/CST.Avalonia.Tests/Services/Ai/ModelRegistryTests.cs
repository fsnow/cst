using System;
using System.Linq;
using CST.Avalonia.Services.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// The fidelity advisory's data and lookup. (#584, AI_SURFACE_B.md §7)
///
/// <para>The shipped registry is asserted here as well as the matching rules: it is data, so nothing about it
/// is checked at compile time, and an entry that silently fails to match is an advisory that never fires.</para>
/// </summary>
public class ModelRegistryTests
{
    private static readonly ModelRegistry Registry = new(NullLogger<ModelRegistry>.Instance);

    // ---- Policy ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("claude-opus-5")]
    [InlineData("claude-fable-5")]
    [InlineData("claude-sonnet-5")]
    [InlineData("claude-opus-4-8")]
    public void Claude_frontier_models_are_recommended(string id)
    {
        // Claude-first is the standing policy (AI_INTEGRATION.md §11.1), not a measurement.
        Assert.Equal(ModelTier.Recommended, Registry.Rate(id).Tier);
        Assert.Null(Registry.Advisory(AiTask.Translate, id));
    }

    [Fact]
    public void An_unknown_model_is_unrated_rather_than_discouraged()
    {
        // The whole design. Frontier models appear constantly; a registry that treated anything it had not
        // heard of as suspect would have flagged half of today's good models a year ago, and would decay into
        // noise users learn to dismiss — including on the entries that matter.
        var rating = Registry.Rate("some-model-released-next-tuesday");

        Assert.Equal(ModelTier.Unrated, rating.Tier);
        Assert.NotEqual(ModelTier.DiscouragedForTranslation, rating.Tier);
    }

    [Fact]
    public void An_unrated_model_gets_a_softer_advisory_than_a_discouraged_one()
    {
        // "We have not tested this" and "this got it wrong" are different statements; conflating them is what
        // turns advisories into noise.
        var unrated = Registry.Advisory(AiTask.Translate, "some-model-released-next-tuesday")!;
        var discouraged = Registry.Advisory(AiTask.Translate, "gpt-oss:20b")!;

        Assert.Contains("has not been evaluated", unrated);
        Assert.Contains("not recommended", discouraged);
        Assert.NotEqual(unrated, discouraged);
    }

    [Fact]
    public void A_discouraged_model_says_why_in_its_advisory()
    {
        // A registry that says a model is not recommended has to be able to answer why — otherwise it is an
        // opinion with a version number.
        var advisory = Registry.Advisory(AiTask.Translate, "gpt-oss:20b")!;

        Assert.Contains("appamāda", advisory);
        Assert.Contains("Check its output against the Pāli", advisory);
    }

    [Fact]
    public void Every_non_recommended_entry_cites_evidence_or_says_it_is_untested()
    {
        // Guards the data, not the code: an entry demoting a model on no stated grounds is an opinion, and the
        // one place that must not happen is the file that tells users which models to distrust.
        foreach (var id in new[] { "gpt-oss:120b", "gpt-oss:20b", "nemotron-3-nano:30b" })
        {
            var rating = Registry.Rate(id);
            Assert.Equal(ModelTier.DiscouragedForTranslation, rating.Tier);
            Assert.False(string.IsNullOrWhiteSpace(rating.Note), id);
            Assert.False(string.IsNullOrWhiteSpace(rating.Evidence), id);
        }
    }

    [Fact]
    public void The_advisory_is_scoped_to_translation()
    {
        // An advisory attached to everything is one nobody reads. Explaining a passage the model can see is a
        // far smaller fidelity surface than producing English a reader takes as the meaning of the Pāli.
        foreach (var task in new[] { AiTask.Explain, AiTask.Grammar, AiTask.WordByWord })
            Assert.Null(Registry.Advisory(task, "gpt-oss:20b"));

        Assert.NotNull(Registry.Advisory(AiTask.Translate, "gpt-oss:20b"));
    }

    [Fact]
    public void Nothing_is_ever_blocked()
    {
        // Curate, advise, never block. The advisory is a string; there is no path that refuses a model, and a
        // reader who wants a local model for privacy has a reason we do not get to override.
        var discouraged = Registry.Rate("gpt-oss:20b");

        Assert.Equal(ModelTier.DiscouragedForTranslation, discouraged.Tier);
        Assert.NotNull(Registry.Advisory(AiTask.Translate, "gpt-oss:20b"));
        // And no API exists to reject one: every member returns a rating or advice, never a verdict.
        Assert.DoesNotContain(typeof(IModelRegistry).GetMethods(), m => m.ReturnType == typeof(bool));
    }

    // ---- Id matching -------------------------------------------------------------------------------------

    [Theory]
    [InlineData("gpt-oss:120b-cloud", "gpt-oss:120b")]
    [InlineData("  GPT-OSS:120B-CLOUD  ", "gpt-oss:120b")]
    [InlineData("anthropic/claude-opus-5", "claude-opus-5")]
    [InlineData("gemma4:cloud", "gemma4")]
    public void Ids_normalize_across_the_spellings_a_model_arrives_in(string typed, string expected)
    {
        // The same model reaches us several ways: an aggregator prefixes the vendor, Ollama suffixes the
        // deployment. Neither changes which model it is.
        Assert.Equal(expected, ModelRegistry.NormalizeId(typed));
    }

    [Fact]
    public void Size_is_never_normalized_away()
    {
        // gpt-oss:20b and gpt-oss:120b are different models rated differently — folding them together would
        // put the warning on the wrong one.
        Assert.Equal(ModelTier.DiscouragedForTranslation, Registry.Rate("gpt-oss:20b").Tier);
        Assert.NotEqual(Registry.Rate("gpt-oss:20b").Note, Registry.Rate("gpt-oss:120b").Note);
    }

    [Fact]
    public void A_dated_snapshot_resolves_to_its_family()
    {
        Assert.Equal(ModelTier.Permitted, Registry.Rate("claude-haiku-4-5-20251001").Tier);
    }

    [Fact]
    public void A_prefix_only_matches_at_a_separator()
    {
        // Otherwise "gpt-oss:120b" would swallow a hypothetical "gpt-oss:1200b" it says nothing about.
        Assert.True(ModelRegistry.Matches("gpt-oss:120b", "gpt-oss:120b-turbo"));
        Assert.False(ModelRegistry.Matches("gpt-oss:120b", "gpt-oss:1200b"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_model_id_is_unrated_rather_than_an_error(string? id)
    {
        Assert.Equal(ModelTier.Unrated, Registry.Rate(id).Tier);
    }

    [Fact]
    public void The_shipped_registry_actually_loaded()
    {
        // Without this, a broken resource would show up only as every model silently reporting unrated —
        // an advisory that never fires looks exactly like a well-behaved one.
        Assert.Equal(ModelTier.Recommended, Registry.Rate("claude-opus-5").Tier);
        Assert.False(string.IsNullOrWhiteSpace(ModelRegistry.Updated));
    }

    [Theory]
    [InlineData("recommended", ModelTier.Recommended)]
    [InlineData("permitted", ModelTier.Permitted)]
    [InlineData("discouraged-for-translation", ModelTier.DiscouragedForTranslation)]
    public void Tier_names_in_the_data_match_the_enum(string name, ModelTier expected)
    {
        Assert.True(ModelRegistry.TryParseTier(name, out var tier));
        Assert.Equal(expected, tier);
    }

    [Fact]
    public void An_unknown_tier_name_is_refused_rather_than_guessed()
    {
        Assert.False(ModelRegistry.TryParseTier("banned", out _));
    }
}
