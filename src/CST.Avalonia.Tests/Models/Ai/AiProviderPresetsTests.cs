using System;
using System.Collections.Generic;
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
            // A base URL may be a TEMPLATE (Azure's resource name, Cloudflare's account id), so substitute
            // a value for every placeholder before parsing - otherwise this test would forbid the very shape
            // that lets those providers exist at all.
            var filled = AiTemplate.Expand(
                p.BaseUrl,
                AiTemplate.PlaceholdersIn(p.BaseUrl).ToDictionary(k => k, _ => "x"));
            Assert.True(Uri.TryCreate(filled, UriKind.Absolute, out _), $"{p.Id} has a non-absolute base URL");

            // Every placeholder must have a prompt to collect it, or the connection can never be completed
            // and the reader is given no way to say what is missing.
            foreach (var key in AiTemplate.PlaceholdersIn(p.BaseUrl))
                Assert.Contains(key, (p.Prompts ?? new List<AiInputPrompt>()).Select(pr => pr.Key));
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
        // Asserted of the HAND-KEPT entries only. Build treats the catalogue's `env` as optional, so a
        // future snapshot carrying an env-less provider would break this suite rather than the app.
        foreach (var p in AiProviderPresets.HandKept.Where(p => p.RequiresKey))
            Assert.NotEmpty(p.EnvironmentVariables);
    }

    /// <summary>Local runners need no key. Guards the case that makes "no credential" a valid state rather
    /// than an error — the single most likely first configuration for a reader trying this out.</summary>
    [Fact]
    public void Local_runners_do_not_require_a_key()
    {
        // Keyed off OUR local presets rather than "any URL containing localhost": the catalogue lists at
        // least one hosted provider (privatemode-ai) that advertises a loopback address, and it is not a
        // local runner in the sense this rule is about.
        var local = AiProviderPresets.LocalOnly;

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

    // ---- the credential-method union (revised after reading opencode's Integration.Method) ---------------

    /// <summary>
    /// A local runner declares NO credential method at all — which is a different statement from "a key that
    /// happens to be empty", and is what makes an absent credential a valid configuration rather than an
    /// error the reader has to dismiss.
    /// </summary>
    [Fact]
    public void Local_runners_declare_no_credential_method()
    {
        foreach (var p in AiProviderPresets.LocalOnly)
        {
            Assert.Empty(p.Methods);
            Assert.False(p.RequiresKey);
        }
    }

    /// <summary>Env is a place a key MIGHT be, never a decision to use one. A preset that offers env
    /// discovery must also offer the ordinary paste-a-key path, or a reader without the variable set has no
    /// way in at all.</summary>
    [Fact]
    public void Env_discovery_never_stands_alone()
    {
        foreach (var p in AiProviderPresets.All.Where(p => p.Methods.OfType<AiCredentialMethod.Env>().Any()))
            Assert.Contains(p.Methods, m => m is AiCredentialMethod.Key);
    }

    /// <summary>Azure is the case that forced the auth header to be configurable: it expects the credential in
    /// `api-key` and expects `Authorization` to be ABSENT, so adding a header would not have been enough.</summary>
    [Fact]
    public void Azure_sends_its_credential_in_its_own_header_without_a_scheme()
    {
        var azure = AiProviderPresets.ById("azure");

        Assert.NotNull(azure);
        Assert.Equal("api-key", azure!.AuthHeaderName);
        Assert.Null(azure.AuthScheme);
    }

    /// <summary>Everything else is an ordinary bearer token; the default must stay that way so a new preset
    /// needs no auth ceremony.</summary>
    [Fact]
    public void Bearer_is_the_default_everywhere_else()
    {
        foreach (var p in AiProviderPresets.All.Where(p => p.Id != "azure"))
        {
            Assert.Equal("Authorization", p.AuthHeaderName);
            Assert.Equal("Bearer", p.AuthScheme);
        }
    }

    /// <summary>A prompt that no template consumes is dead weight the reader is asked to fill in for nothing;
    /// the reverse (a placeholder with no prompt) is covered by the well-formed test.</summary>
    [Fact]
    public void Every_prompt_feeds_a_template()
    {
        foreach (var p in AiProviderPresets.All.Where(p => p.Prompts is { Count: > 0 }))
        {
            var used = AiTemplate.PlaceholdersIn(p.BaseUrl)
                .Concat((p.Headers ?? new Dictionary<string, string>())
                    .Values.SelectMany(AiTemplate.PlaceholdersIn))
                .ToHashSet();

            foreach (var prompt in p.Prompts!)
                Assert.Contains(prompt.Key, used);
        }
    }

    /// <summary>Conditional prompts: absent means empty, so a NotEquals condition is satisfied before the
    /// reader has typed anything — which is what makes a dependent field appear rather than hide.</summary>
    [Fact]
    public void A_condition_treats_an_unanswered_input_as_empty()
    {
        var whenBlank = new AiPromptCondition("baseUrl", AiConditionOperator.NotEquals, "x");

        Assert.True(whenBlank.IsSatisfiedBy(new Dictionary<string, string>()));
        Assert.False(whenBlank.IsSatisfiedBy(new Dictionary<string, string> { ["baseUrl"] = "x" }));
    }
}
