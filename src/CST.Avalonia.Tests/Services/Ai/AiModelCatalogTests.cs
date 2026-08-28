using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using CST.Avalonia.Models.Ai;
using CST.Avalonia.Services.Ai;
using CST.Avalonia.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
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

    /// <summary>
    /// A padded id is trimmed at the parse seam, so every later ordinal join agrees on it.
    ///
    /// <para>Untrimmed, a gateway that pads its ids gave the reader a stored model and its own listing entry
    /// that could never match each other: two rows for one model on the Models tab, published facts that
    /// never reattached to the stored row, and a completed fetch marking the model they had just enabled "no
    /// longer listed". (#870)</para>
    /// </summary>
    [Fact]
    public void A_padded_listing_id_is_trimmed()
    {
        var models = AiModelCatalog.Parse("""
        {"data":[{"id":"  gpt-4o  "},{"id":"   "}]}
        """);

        var model = Assert.Single(models);
        Assert.Equal("gpt-4o", model.Id);
        Assert.Equal("gpt-4o", model.DisplayName);   // the name falls back to the id, trimmed too
    }

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
        // Anthropic publishes no parameter list, which is not the same as publishing one without
        // reasoning in it — so this is unknown rather than false.
        Assert.Null(model.SupportsReasoning);
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

    // ---- telling "the provider sent few" from "we understood few" ------------------------------------------

    /// <summary>
    /// The listing's entry count is read separately from what we could parse.
    ///
    /// <para>Asked in earnest: a Cerebras connection offered two models and the maintainer knew the provider
    /// supported more. "Fetched 2 models" reads as a fact about the provider and is equally consistent with
    /// the provider having sent two and with our having understood two of nine — and nothing in the log could
    /// tell them apart.</para>
    /// </summary>
    [Fact]
    public void The_listing_count_is_independent_of_what_parsed()
    {
        const string json = """
        {"data":[{"id":"a"},{"no-id":"here"},{"id":"c"},"a string"]}
        """;

        Assert.Equal(4, AiModelCatalog.CountEntries(json));
        Assert.Equal(2, AiModelCatalog.Parse(json).Count);
    }

    /// <summary>When everything parses the two agree, which is what makes a disagreement worth logging.</summary>
    [Fact]
    public void A_listing_we_fully_understand_counts_the_same_both_ways()
    {
        const string json = """{"data":[{"id":"a"},{"id":"b"}]}""";

        Assert.Equal(AiModelCatalog.Parse(json).Count, AiModelCatalog.CountEntries(json));
    }

    /// <summary>A body that is not a listing counts as nothing rather than throwing — this runs on the path
    /// that is already reporting a problem.</summary>
    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"models":[{"id":"a"}]}""")]
    [InlineData("""{"data":"not an array"}""")]
    public void An_unreadable_listing_counts_as_none(string json) =>
        Assert.Equal(0, AiModelCatalog.CountEntries(json));

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

    // ---- whether a listing is everything (#728) -----------------------------------------------------------

    /// <summary>An ordinary listing we could read end to end is complete.</summary>
    [Fact]
    public void A_listing_read_end_to_end_is_complete()
    {
        Assert.False(AiModelCatalog.HasMore("""{"data":[{"id":"a"},{"id":"b"}]}"""));
    }

    /// <summary>
    /// An endpoint that says there is another page is not complete.
    ///
    /// <para>Anthropic's <c>GET /v1/models</c> pages at twenty by default. We do not follow the pages yet, but
    /// reading the flag is what stops a first page being mistaken for the whole catalogue — which would report
    /// every model after the twentieth as retired (#728).</para>
    /// </summary>
    [Fact]
    public void A_paged_listing_says_there_is_more()
    {
        Assert.True(AiModelCatalog.HasMore("""{"data":[{"id":"a"}],"has_more":true,"last_id":"a"}"""));
    }

    /// <summary>A body we cannot read is not evidence that there is nothing more. Guessing the other way is
    /// the half of the answer that produces false alarms.</summary>
    [Fact]
    public void An_unreadable_body_is_not_taken_as_the_end()
    {
        Assert.True(AiModelCatalog.HasMore("{ this is not json"));
    }

    // ---- the listing request must authenticate exactly as a chat request does ----------------------------

    /// <summary>
    /// The listing path and the chat path must not disagree about <c>anthropic-version</c>. The catalog added
    /// it unconditionally while the adapter added it only when absent, so a gateway pinning its own version
    /// got one value on a chat request and TWO on a model listing — <c>2026-01-01, 2023-06-01</c>, because
    /// TryAddWithoutValidation appends. That is the "list loads while answers fail" contradiction the shared
    /// auth helper exists to prevent, arriving through the one header the helper does not own.
    /// (Fable review of #764, finding 1)
    /// </summary>
    [Fact]
    public async Task A_listing_sends_one_protocol_version_when_the_connection_pins_its_own()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[{"id":"claude-opus-5"}]}"""),
        });

        var catalog = new AiModelCatalog(
            new HttpClient(handler), credentials: null, NullLogger<AiModelCatalog>.Instance);

        await catalog.FetchAsync(new AiConnection(
            Id: "gw",
            DisplayName: "Gateway",
            Kind: ChatProviderKind.Anthropic,
            BaseUrl: "https://gateway.example",
            Models: new List<AiModelEntry>(),
            Headers: new[] { new AiHeader("anthropic-version", "2026-01-01") },
            Inputs: new Dictionary<string, string>()));

        var sent = handler.LastRequest!;
        Assert.Equal(new[] { "2026-01-01" }, sent.Headers.GetValues("anthropic-version"));
    }

    /// <summary>And the default still goes out when the connection pins nothing.</summary>
    [Fact]
    public async Task A_listing_sends_the_default_protocol_version_when_the_connection_pins_none()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[]}"""),
        });

        var catalog = new AiModelCatalog(
            new HttpClient(handler), credentials: null, NullLogger<AiModelCatalog>.Instance);

        await catalog.FetchAsync(new AiConnection(
            Id: "anthropic",
            DisplayName: "Claude",
            Kind: ChatProviderKind.Anthropic,
            BaseUrl: "https://api.anthropic.com",
            Models: new List<AiModelEntry>(),
            Headers: Array.Empty<AiHeader>(),
            Inputs: new Dictionary<string, string>()));

        Assert.Equal(new[] { "2023-06-01" }, handler.LastRequest!.Headers.GetValues("anthropic-version"));
    }

    /// <summary>An in-memory store keyed by the joined account, so a test that asks for the wrong name misses
    /// rather than being handed the only secret there is.</summary>
    private sealed class NamedKeys : IAiCredentialStore
    {
        private readonly Dictionary<string, string> _byAccount = new(StringComparer.Ordinal);
        public bool IsAvailable => true;
        public string? Unavailable => null;
        public string? Get(string connectionId, string name) =>
            _byAccount.GetValueOrDefault(connectionId + ":" + name);
        public bool Set(string connectionId, string name, string secret)
        { _byAccount[connectionId + ":" + name] = secret; return true; }
        public bool Delete(string connectionId, string name) => _byAccount.Remove(connectionId + ":" + name);
    }

    /// <summary>
    /// The listing sends a secret header exactly as the chat path does. (#771)
    ///
    /// <para>The two surfaces send the same credentials, so they have to authenticate identically — the
    /// symptom of them diverging is a provider whose model list loads while its answers 401, which is the
    /// contradiction #673 exists to prevent and is close to undiagnosable from a bug report.</para>
    /// </summary>
    [Fact]
    public async Task A_listing_sends_a_secret_header_from_the_credential_store()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[{"id":"some-model"}]}"""),
        });

        var keys = new NamedKeys();
        keys.Set("gw", AiCredentialNames.Header("cf-aig-authorization"), "cf-token-abc");

        var catalog = new AiModelCatalog(
            new HttpClient(handler), keys, NullLogger<AiModelCatalog>.Instance);

        await catalog.FetchAsync(new AiConnection(
            Id: "gw",
            DisplayName: "Gateway",
            Kind: ChatProviderKind.OpenAiCompatible,
            BaseUrl: "https://gateway.example/v1",
            Models: new List<AiModelEntry>(),
            Headers: new[] { new AiHeader("cf-aig-authorization", null, Secret: true) },
            Inputs: new Dictionary<string, string>()));

        Assert.Equal(
            new[] { "cf-token-abc" },
            handler.LastRequest!.Headers.GetValues("cf-aig-authorization"));
    }

    /// <summary>
    /// A secret with nothing stored is refused here exactly as the chat path refuses it. A listing that loads
    /// while the chat refuses is the surface split #673 exists to prevent, pointing the other way — and a
    /// blank header dropped at the wire comes back as a 401 naming nothing.
    /// </summary>
    [Fact]
    public async Task A_listing_refuses_a_secret_header_with_nothing_stored()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[{"id":"some-model"}]}"""),
        });

        var catalog = new AiModelCatalog(
            new HttpClient(handler), new NamedKeys(), NullLogger<AiModelCatalog>.Instance);

        var result = await catalog.FetchAsync(new AiConnection(
            Id: "gw",
            DisplayName: "Gateway",
            Kind: ChatProviderKind.OpenAiCompatible,
            BaseUrl: "https://gateway.example/v1",
            Models: new List<AiModelEntry>(),
            Headers: new[] { new AiHeader("cf-aig-authorization", null, Secret: true) },
            Inputs: new Dictionary<string, string>()));

        Assert.False(result.Ok);
        Assert.Contains("cf-aig-authorization", result.Problem);
        Assert.Null(handler.LastRequest);   // refused before anything went out
    }

    /// <summary>
    /// The case that could not exist before #771: the ONLY credential is a secret header. Anthropic, no API
    /// key stored, authenticating entirely by a header whose value is in the credential store.
    ///
    /// <para>This is #689's "leave the key empty if you manage auth via headers" escape hatch, and it is the
    /// one most at risk from this change — <c>IsSendableHeader</c> requires a non-blank VALUE (that was #764's
    /// fix, after a cosmetic X-Title let a keyless connection through and its 401 surfaced as "the provider
    /// rejected the API key"). A secret header is blank in settings.json, so if the value were fetched after
    /// the credential check rather than before it, this connection would be refused by the very feature built
    /// to make it safe. (raised in review by the session that landed #711/#764)</para>
    /// </summary>
    [Fact]
    public async Task A_listing_authenticates_by_a_secret_header_alone()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[{"id":"claude-opus-5"}]}"""),
        });

        var keys = new NamedKeys();
        keys.Set("gw", AiCredentialNames.Header("x-api-key"), "secret-only-credential");

        var catalog = new AiModelCatalog(
            new HttpClient(handler), keys, NullLogger<AiModelCatalog>.Instance);

        var result = await catalog.FetchAsync(new AiConnection(
            Id: "gw",
            DisplayName: "Gateway",
            Kind: ChatProviderKind.Anthropic,
            BaseUrl: "https://gateway.example",
            Models: new List<AiModelEntry>(),
            Headers: new[] { new AiHeader("x-api-key", null, Secret: true) },
            Inputs: new Dictionary<string, string>()));

        Assert.True(result.Ok);
        Assert.Equal(
            new[] { "secret-only-credential" },
            handler.LastRequest!.Headers.GetValues("x-api-key"));
    }

    // ---- reasoning effort (#671) ------------------------------------------------------------------------

    /// <summary>
    /// The published levels and the provider's own default, read from the richer object OpenRouter carries
    /// beside the flat parameter list. That object is what answers "which values, and which is the default";
    /// the flat list only answers "is the knob there".
    /// </summary>
    [Fact]
    public void Published_effort_levels_and_default_are_read()
    {
        var models = AiModelCatalog.Parse("""
        {"data":[{"id":"m","name":"M","supported_parameters":["reasoning_effort"],
                  "reasoning":{"supported_efforts":["low","high","max"],"default_effort":"high","mandatory":false}}]}
        """);

        var model = Assert.Single(models);
        Assert.Equal(new[] { "low", "high", "max" }, model.ReasoningEfforts);
        Assert.Equal("high", model.DefaultReasoningEffort);
    }

    /// <summary>
    /// The correction this issue turned up. The old predicate matched any parameter containing "reasoning",
    /// which catches `reasoning` and `include_reasoning` — both meaning the model RETURNS reasoning content,
    /// not that it takes an effort knob. Measured against OpenRouter's live listing, 287 models matched and
    /// only 142 list reasoning_effort: 51% false positives. Gating the effort chip on the loose test would
    /// have put it on 145 models that publish no such parameter.
    /// </summary>
    [Fact]
    public void Emitting_reasoning_is_not_the_same_as_accepting_an_effort_parameter()
    {
        var models = AiModelCatalog.Parse("""
        {"data":[{"id":"m","name":"M","supported_parameters":["reasoning","include_reasoning"]}]}
        """);

        var model = Assert.Single(models);
        Assert.True(model.SupportsReasoning);        // it does emit reasoning
        Assert.False(model.AcceptsReasoningEffort);  // and it does NOT take the knob
    }

    [Fact]
    public void A_model_that_lists_reasoning_effort_accepts_it()
    {
        var models = AiModelCatalog.Parse("""
        {"data":[{"id":"m","name":"M","supported_parameters":["reasoning","reasoning_effort"]}]}
        """);

        Assert.True(Assert.Single(models).AcceptsReasoningEffort);
    }

    /// <summary>Silence stays silence. A provider that publishes no parameter list has not said no.</summary>
    [Fact]
    public void A_provider_that_publishes_no_parameters_says_nothing_about_effort()
    {
        var models = AiModelCatalog.Parse("""{"data":[{"id":"m","name":"M"}]}""");

        var model = Assert.Single(models);
        Assert.Null(model.AcceptsReasoningEffort);
        Assert.Null(model.ReasoningEfforts);
        Assert.Null(model.DefaultReasoningEffort);
    }

    /// <summary>
    /// #670/#681: the same OpenRouter objects carry benchmark scores on 229 of 420 models. Provider-published,
    /// so it passes the letter of "a published capability is fine" while being unambiguously a ranking. This
    /// pins that the parser does not pick it up as one more fact when someone widens it.
    /// </summary>
    [Fact]
    public void Published_benchmark_scores_are_not_read()
    {
        var models = AiModelCatalog.Parse("""
        {"data":[{"id":"m","name":"M",
                  "benchmarks":{"artificial_analysis":{"intelligence_index":59.5,"coding_index":74.8}}}]}
        """);

        var model = Assert.Single(models);
        var serialised = System.Text.Json.JsonSerializer.Serialize(model);
        Assert.DoesNotContain("59.5", serialised, StringComparison.Ordinal);
        Assert.DoesNotContain("intelligence", serialised, StringComparison.OrdinalIgnoreCase);
    }

    // ---- following the pages (#769) ---------------------------------------------------------------------

    /// <summary>An Anthropic-shaped connection, for the paging tests.</summary>
    private static AiConnection Paged() => new(
        Id: "anthropic",
        DisplayName: "Claude",
        Kind: ChatProviderKind.Anthropic,
        BaseUrl: "https://api.anthropic.com",
        Models: new List<AiModelEntry>(),
        Headers: System.Array.Empty<AiHeader>(),
        Inputs: new Dictionary<string, string>());

    private static AiModelCatalog CatalogOver(StubHttpMessageHandler handler) =>
        new(new HttpClient(handler), credentials: null, NullLogger<AiModelCatalog>.Instance);

    /// <summary>
    /// The listing is read to the end, not one page deep. Before #769 an Anthropic-protocol connection with
    /// more than twenty models showed the first twenty — and, because the flag said the listing was partial,
    /// #728's retired-model marking never ran for it at all.
    /// </summary>
    [Fact]
    public async Task A_paged_listing_is_followed_to_its_end()
    {
        var handler = new StubHttpMessageHandler(request =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    request.RequestUri!.Query.Contains("after_id=", StringComparison.Ordinal)
                        ? """{"data":[{"id":"claude-sonnet"}],"has_more":false}"""
                        : """{"data":[{"id":"claude-opus"}],"has_more":true,"last_id":"claude-opus"}"""),
            });

        var result = await CatalogOver(handler).FetchAsync(Paged());

        Assert.True(result.Ok);
        Assert.Equal(new[] { "claude-opus", "claude-sonnet" }, result.Models.Select(m => m.Id));

        // Complete, so #728's marking may run from it — the point of following the pages at all.
        Assert.True(result.Complete);
        Assert.Equal(2, handler.RequestedUrls.Count);
        Assert.Contains("after_id=claude-opus", handler.RequestedUrls[1].Query, StringComparison.Ordinal);
    }

    /// <summary>
    /// The cursor is the endpoint's LAST entry in document order, not the last of what Parse returns — Parse
    /// sorts alphabetically, so paging from its tail would ask the provider to continue after a model in the
    /// middle of the page and silently lose everything between.
    /// </summary>
    [Fact]
    public async Task Paging_continues_from_the_endpoints_own_last_entry_not_the_alphabetical_one()
    {
        var handler = new StubHttpMessageHandler(request =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    request.RequestUri!.Query.Contains("after_id=", StringComparison.Ordinal)
                        ? """{"data":[{"id":"m-last"}],"has_more":false}"""
                        // Document order zebra, alpha — and no last_id, so the walk has to find it.
                        : """{"data":[{"id":"zebra"},{"id":"alpha"}],"has_more":true}"""),
            });

        await CatalogOver(handler).FetchAsync(Paged());

        Assert.Contains("after_id=alpha", handler.RequestedUrls[1].Query, StringComparison.Ordinal);
    }

    /// <summary>
    /// A gateway that invents <c>has_more</c> but ignores <c>after_id</c> hands back the same page for ever.
    /// The walk stops the moment a page adds nothing, rather than looping until the timeout — and says the
    /// listing is partial, because the endpoint is still claiming there is more.
    /// </summary>
    [Fact]
    public async Task An_endpoint_that_promises_more_and_repeats_itself_is_not_followed_for_ever()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[{"id":"only"}],"has_more":true,"last_id":"only"}"""),
        });

        var result = await CatalogOver(handler).FetchAsync(Paged());

        Assert.True(result.Ok);
        Assert.Equal("only", Assert.Single(result.Models).Id);
        Assert.False(result.Complete);

        // Two: the first page, and one attempt to advance that came back with nothing new.
        Assert.Equal(2, handler.RequestedUrls.Count);
    }

    /// <summary>
    /// A later page failing costs the rest of the listing, not the part already in hand. Throwing twenty real
    /// models away because the twenty-first page answered 500 turns a partial answer into no answer.
    /// </summary>
    [Fact]
    public async Task A_page_that_fails_after_the_first_keeps_what_was_already_read()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.Query.Contains("after_id=", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"data":[{"id":"kept"}],"has_more":true,"last_id":"kept"}"""),
                });

        var result = await CatalogOver(handler).FetchAsync(Paged());

        Assert.True(result.Ok);
        Assert.Equal("kept", Assert.Single(result.Models).Id);
        Assert.False(result.Complete);
    }

    /// <summary>The FIRST page failing still produces its own named sentence, rather than an empty success.</summary>
    [Fact]
    public async Task A_first_page_that_fails_still_reports_the_failure()
    {
        var handler = new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await CatalogOver(handler).FetchAsync(Paged());

        Assert.False(result.Ok);
        Assert.NotNull(result.Problem);
        Assert.True(result.Reachable);
    }

    /// <summary>
    /// Entries we cannot read are counted, not merely logged. The observed case was a listing whose nine
    /// entries yielded two — and the reader saw "2 models" with no way to tell that from a provider that
    /// offers two.
    /// </summary>
    [Fact]
    public async Task Unreadable_entries_are_counted_for_the_reader()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"data":[{"id":"good"},{"name":"no id"},{"name":"nor here"}],"has_more":false}"""),
        });

        var result = await CatalogOver(handler).FetchAsync(Paged());

        Assert.True(result.Ok);
        Assert.Equal("good", Assert.Single(result.Models).Id);
        Assert.Equal(2, result.Skipped);

        // A hole in the listing still bars #728's marking: a skipped entry is precisely a model that is
        // absent from what we parsed without having been retired.
        Assert.False(result.Complete);
    }

    /// <summary>
    /// A gateway repeating a page must not have its entries counted twice as unreadable. Deriving the skipped
    /// count by subtracting at the end would do exactly that — the ids dedupe, the entry counts do not.
    /// </summary>
    [Fact]
    public async Task A_repeated_page_does_not_invent_unreadable_entries()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[{"id":"a"},{"id":"b"}],"has_more":true,"last_id":"b"}"""),
        });

        var result = await CatalogOver(handler).FetchAsync(Paged());

        Assert.Equal(2, result.Models.Count);
        Assert.Equal(0, result.Skipped);
        Assert.False(result.Complete);
    }

    /// <summary>Models stay alphabetical across a page boundary, not merely within each page.</summary>
    [Fact]
    public async Task Models_from_several_pages_are_sorted_together()
    {
        var handler = new StubHttpMessageHandler(request =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    request.RequestUri!.Query.Contains("after_id=", StringComparison.Ordinal)
                        ? """{"data":[{"id":"a-model"}],"has_more":false}"""
                        : """{"data":[{"id":"z-model"}],"has_more":true,"last_id":"z-model"}"""),
            });

        var result = await CatalogOver(handler).FetchAsync(Paged());

        Assert.Equal(new[] { "a-model", "z-model" }, result.Models.Select(m => m.Id));
    }

    /// <summary>
    /// The cursor is added to a base URL that already carries a query without producing a second '?' — an
    /// Azure-style endpoint pins api-version there, and the malformed URL would be rejected with a message
    /// about the wrong thing entirely.
    /// </summary>
    [Fact]
    public void The_cursor_is_added_to_a_url_that_already_has_a_query()
    {
        var next = AiModelCatalog.After(
            new System.Uri("https://x.example/openai/v1/models?api-version=2026-01-01"), "m/1");

        Assert.Equal("https", next.Scheme);
        Assert.Equal("/openai/v1/models", next.AbsolutePath);
        Assert.Contains("api-version=2026-01-01", next.Query, StringComparison.Ordinal);
        Assert.Contains("after_id=m%2F1", next.Query, StringComparison.Ordinal);
        Assert.Equal(1, next.Query.Count(c => c == '?'));
    }

    /// <summary>
    /// An endpoint that always says there is more AND always advances is bounded by the page cap rather than
    /// by the 20-second budget. Without the cap it would page until the timeout and then report nothing at
    /// all — the worst of both, since every page it did read was real.
    /// </summary>
    [Fact]
    public async Task An_endpoint_that_never_ends_is_stopped_by_the_page_cap()
    {
        var page = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            var id = $"m{page++}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"data":[{"id":"{{id}}"}],"has_more":true,"last_id":"{{id}}"}"""),
            };
        });

        var result = await CatalogOver(handler).FetchAsync(Paged());

        Assert.True(result.Ok);
        Assert.Equal(25, handler.RequestedUrls.Count);
        Assert.Equal(25, result.Models.Count);
        Assert.False(result.Complete);
        Assert.Equal(0, result.Skipped);
    }

    /// <summary>The published cursor wins over the walk, and the walk covers a gateway that publishes none.</summary>
    [Fact]
    public void The_last_entry_is_read_from_last_id_when_the_endpoint_publishes_one()
    {
        Assert.Equal("published", AiModelCatalog.LastEntryId(
            """{"data":[{"id":"a"},{"id":"b"}],"last_id":"published"}"""));

        Assert.Equal("b", AiModelCatalog.LastEntryId("""{"data":[{"id":"a"},{"id":"b"}]}"""));
        Assert.Null(AiModelCatalog.LastEntryId("""{"data":[]}"""));
        Assert.Null(AiModelCatalog.LastEntryId("not json"));
    }

    // #714: the listing authenticates exactly as chat does, INCLUDING from an adopted environment key.
    //
    // The first version of that work wired the environment into the chat resolver and left this path reading
    // the credential store alone. An adopted connection would answer a question and then fail to list its
    // models, reporting "the provider rejected the stored key" for a connection that has no stored key — the
    // two-surfaces-disagreeing failure #673 exists to prevent, and which Authenticate's own summary forbids
    // two lines above the code that had it. Untested is how it went missing. (fable)
    [Fact]
    public async Task An_adopted_environment_key_authenticates_the_listing_too()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[{"id":"claude-opus-5"}]}"""),
        });

        var catalog = new AiModelCatalog(
            new HttpClient(handler), credentials: null, NullLogger<AiModelCatalog>.Instance,
            new CST.Avalonia.Services.Ai.Credentials.AiEnvironmentKeys(
                n => n == "ANTHROPIC_API_KEY" ? "sk-from-env" : null));

        await catalog.FetchAsync(new AiConnection(
            Id: "claude",
            DisplayName: "Claude",
            Kind: ChatProviderKind.Anthropic,
            BaseUrl: "https://api.anthropic.com",
            Models: new List<AiModelEntry>(),
            Headers: System.Array.Empty<AiHeader>(),
            Inputs: new Dictionary<string, string>(),
            UsesEnvironmentKey: true,
            EnvironmentVariable: "ANTHROPIC_API_KEY"));

        Assert.Equal("sk-from-env", string.Join(",", handler.LastRequest!.Headers.GetValues("x-api-key")));
    }

    // And a connection that never opted in does not borrow it here either.
    [Fact]
    public async Task A_listing_does_not_use_an_environment_key_without_the_opt_in()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[{"id":"claude-opus-5"}]}"""),
        });

        var catalog = new AiModelCatalog(
            new HttpClient(handler), credentials: null, NullLogger<AiModelCatalog>.Instance,
            new CST.Avalonia.Services.Ai.Credentials.AiEnvironmentKeys(
                n => n == "ANTHROPIC_API_KEY" ? "sk-from-env" : null));

        await catalog.FetchAsync(new AiConnection(
            Id: "claude",
            DisplayName: "Claude",
            Kind: ChatProviderKind.Anthropic,
            BaseUrl: "https://api.anthropic.com",
            Models: new List<AiModelEntry>(),
            Headers: System.Array.Empty<AiHeader>(),
            Inputs: new Dictionary<string, string>(),
            UsesEnvironmentKey: false,
            EnvironmentVariable: "ANTHROPIC_API_KEY"));

        Assert.False(handler.LastRequest!.Headers.Contains("x-api-key"));
    }
}
