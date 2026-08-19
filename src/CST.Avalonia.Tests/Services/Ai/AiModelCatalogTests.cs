using System.Linq;
using CST.Avalonia.Services.Ai;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// #674: reading a provider's model listing.
///
/// <para>The parser is the whole risk surface. One code path serves OpenRouter's richly annotated 414, the
/// three fields Anthropic publishes, and the bare ids a local Ollama returns — so every test here is really
/// asking the same question: does an absent field degrade, or break?</para>
/// </summary>
public class AiModelCatalogTests
{
    // ---- the three shapes one parser has to serve ------------------------------------------------------

    /// <summary>OpenRouter: everything published at once. The fields are pulled by name — never "whatever
    /// the source says" — because a listing can gain a ranking field in a release nobody read.</summary>
    [Fact]
    public void An_openrouter_entry_yields_every_published_field()
    {
        var models = AiModelCatalog.Parse("""
        {"data":[{
          "id":"nvidia/nemotron-nano-9b-v2",
          "name":"NVIDIA Nemotron Nano 9B V2",
          "context_length":131072,
          "pricing":{"prompt":"0.0000004","completion":"0.0000016"},
          "architecture":{"input_modalities":["text"],"output_modalities":["text"]},
          "supported_parameters":["temperature","reasoning"]
        }]}
        """);

        var model = Assert.Single(models);
        Assert.Equal("nvidia/nemotron-nano-9b-v2", model.Id);
        Assert.Equal("NVIDIA Nemotron Nano 9B V2", model.DisplayName);
        Assert.Equal(131072, model.ContextLength);
        Assert.Equal(0.4m, model.PromptPricePerMillion);
        Assert.Equal(1.6m, model.CompletionPricePerMillion);
        Assert.True(model.SupportsReasoning);
        Assert.True(model.CostsMoney);
    }

    /// <summary>Anthropic names the field differently and publishes nothing else.</summary>
    [Fact]
    public void An_anthropic_entry_uses_display_name()
    {
        var models = AiModelCatalog.Parse("""
        {"data":[{"id":"claude-sonnet-4-5","display_name":"Claude Sonnet 4.5","type":"model"}]}
        """);

        var model = Assert.Single(models);
        Assert.Equal("Claude Sonnet 4.5", model.DisplayName);
        Assert.Null(model.ContextLength);
        Assert.False(model.SupportsReasoning);
    }

    /// <summary>
    /// A local runner publishes an id and nothing else — the ordinary case, not a degraded one.
    ///
    /// <para>It must still be usable, and in particular must survive the capability filter: a model with no
    /// published modality has not said it cannot handle text, and filtering it away on a field the provider
    /// never sent would make every local endpoint look empty.</para>
    /// </summary>
    [Fact]
    public void An_ollama_entry_survives_on_its_id_alone()
    {
        var models = AiModelCatalog.Parse("""
        {"data":[{"id":"gemma4:12b-mlx","object":"model","owned_by":"library"}]}
        """);

        var model = Assert.Single(models);
        Assert.Equal("gemma4:12b-mlx", model.Id);
        Assert.Equal("gemma4:12b-mlx", model.DisplayName);
        Assert.False(model.CostsMoney);   // publishes no price; unknown is not costly
    }

    // ---- price -------------------------------------------------------------------------------------------

    /// <summary>
    /// What a provider charges is a fact it publishes, which is what makes filtering on it safe where a
    /// judgment about quality would not be (#670/#681).
    /// </summary>
    [Theory]
    [InlineData("""{"prompt":"0","completion":"0"}""", false)]
    [InlineData("""{"prompt":"0.0000004","completion":"0.0000016"}""", true)]
    [InlineData("""{"prompt":"0","completion":"0.0000016"}""", true)]
    public void Costing_money_is_read_from_the_published_price(string pricing, bool expected)
    {
        var models = AiModelCatalog.Parse($$"""{"data":[{"id":"m","pricing":{{pricing}}}]}""");

        Assert.Equal(expected, Assert.Single(models).CostsMoney);
    }

    /// <summary>Unknown is not costly. Every local runner publishes no price at all, and treating silence as
    /// expensive would hide the models of a reader spending nothing.</summary>
    [Fact]
    public void A_model_with_no_published_price_does_not_count_as_costing_money() =>
        Assert.False(Assert.Single(AiModelCatalog.Parse("""{"data":[{"id":"m"}]}""")).CostsMoney);

    /// <summary>Modalities are still parsed and kept — they are provider-published facts worth showing, even
    /// though they no longer drive a filter: on OpenRouter every one of 415 models can answer in text, so the
    /// modality filter they used to drive excluded nothing.</summary>
    [Fact]
    public void Modalities_are_still_read()
    {
        var models = AiModelCatalog.Parse(
            """{"data":[{"id":"m","architecture":{"input_modalities":["text","image"],"output_modalities":["text"]}}]}""");

        var model = Assert.Single(models);
        Assert.Equal(new[] { "text", "image" }, model.InputModalities);
        Assert.Equal(new[] { "text" }, model.OutputModalities);
    }

    // ---- ordering and robustness -----------------------------------------------------------------------

    /// <summary>
    /// Alphabetical, which is mechanical. The ordering upstream uses — newest first, computed from release
    /// dates — is an editorial claim however arithmetically it is arrived at, since a newer point release can
    /// be worse at Pāli (#689).
    /// </summary>
    [Fact]
    public void Models_come_back_in_alphabetical_order()
    {
        var models = AiModelCatalog.Parse("""
        {"data":[{"id":"c","name":"Zephyr"},{"id":"a","name":"Apollo"},{"id":"b","name":"mercury"}]}
        """);

        Assert.Equal(new[] { "Apollo", "mercury", "Zephyr" }, models.Select(m => m.DisplayName));
    }

    /// <summary>A price of zero is free and may be said so; an absent price is unknown and must not be
    /// rendered as free.</summary>
    [Fact]
    public void An_absent_price_is_unknown_rather_than_free()
    {
        var models = AiModelCatalog.Parse("""{"data":[{"id":"m"}]}""");

        Assert.Null(Assert.Single(models).PromptPricePerMillion);
    }

    [Fact]
    public void A_zero_price_is_kept_as_zero()
    {
        var models = AiModelCatalog.Parse("""
        {"data":[{"id":"m","pricing":{"prompt":"0","completion":"0"}}]}
        """);

        Assert.Equal(0m, Assert.Single(models).PromptPricePerMillion);
    }

    /// <summary>OpenRouter nests it under <c>top_provider</c> for some entries; the same list mixes both
    /// shapes.</summary>
    [Fact]
    public void Context_length_is_read_from_either_place()
    {
        var models = AiModelCatalog.Parse("""
        {"data":[{"id":"m","top_provider":{"context_length":8192}}]}
        """);

        Assert.Equal(8192, Assert.Single(models).ContextLength);
    }

    /// <summary>An entry with no id is not a model. Skipped rather than admitted as an empty row that would
    /// send an empty string on the wire.</summary>
    [Fact]
    public void Entries_without_an_id_are_skipped()
    {
        var models = AiModelCatalog.Parse("""
        {"data":[{"name":"No id here"},{"id":"real"},"a string",42]}
        """);

        Assert.Equal("real", Assert.Single(models).Id);
    }

    [Fact]
    public void A_response_without_a_data_array_yields_nothing() =>
        Assert.Empty(AiModelCatalog.Parse("""{"models":[{"id":"m"}]}"""));

    /// <summary>Reasoning support is read from the provider's published parameters, never guessed from a
    /// name — "thinking" in a model's title is marketing, `supported_parameters` is a fact.</summary>
    [Theory]
    [InlineData("""["reasoning"]""", true)]
    [InlineData("""["include_reasoning"]""", true)]
    [InlineData("""["thinking"]""", true)]
    [InlineData("""["temperature","top_p"]""", false)]
    public void Reasoning_support_comes_from_supported_parameters(string parameters, bool expected)
    {
        var models = AiModelCatalog.Parse($$"""{"data":[{"id":"m","supported_parameters":{{parameters}}}]}""");

        Assert.Equal(expected, Assert.Single(models).SupportsReasoning);
    }
}
