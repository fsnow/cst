using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.Models.Ai;
using CST.Avalonia.Services.Ai;
using Xunit;

namespace CST.Avalonia.Tests.Models.Ai;

/// <summary>
/// #689: several providers do not have a base URL so much as a shape — Azure wants the reader's resource
/// name in the host, Cloudflare its account id in the path. Without substitution each is a special case in
/// code, which is why they were absent from the first version of the catalogue.
/// </summary>
public class AiTemplateTests
{
    private static Dictionary<string, string> Inputs(params (string K, string V)[] pairs) =>
        pairs.ToDictionary(p => p.K, p => p.V);

    [Fact]
    public void A_plain_url_is_returned_unchanged()
    {
        Assert.Equal("https://api.openai.com/v1",
            AiTemplate.Expand("https://api.openai.com/v1", Inputs()));
    }

    [Fact]
    public void Placeholders_are_replaced_from_the_inputs()
    {
        Assert.Equal("https://acme.openai.azure.com/openai/v1",
            AiTemplate.Expand("https://{resourceName}.openai.azure.com/openai/v1",
                Inputs(("resourceName", "acme"))));
    }

    [Fact]
    public void Several_placeholders_are_all_replaced()
    {
        Assert.Equal("https://gateway.ai.cloudflare.com/v1/acct1/gw2/compat",
            AiTemplate.Expand("https://gateway.ai.cloudflare.com/v1/{accountId}/{gatewayId}/compat",
                Inputs(("accountId", "acct1"), ("gatewayId", "gw2"))));
    }

    /// <summary>
    /// The important one. An unsupplied value must NOT collapse to an empty string: that yields
    /// <c>https://.openai.azure.com/…</c>, which looks like a URL, passes a naive parse, and fails at request
    /// time as a DNS error naming nothing. Left visible, the connection can be refused before anything is
    /// sent, with a message that says which field is missing.
    /// </summary>
    [Fact]
    public void A_missing_input_is_left_visible_rather_than_emptied()
    {
        var expanded = AiTemplate.Expand("https://{resourceName}.openai.azure.com/openai/v1", Inputs());

        Assert.Equal("https://{resourceName}.openai.azure.com/openai/v1", expanded);
        Assert.True(AiTemplate.HasUnresolvedPlaceholders(expanded));
        Assert.DoesNotContain("https://.", expanded);
    }

    /// <summary>An input present but blank is the same failure as an absent one — a half-filled form.</summary>
    [Fact]
    public void A_blank_input_counts_as_missing()
    {
        var expanded = AiTemplate.Expand("https://{resourceName}.example.com",
            Inputs(("resourceName", "")));

        Assert.True(AiTemplate.HasUnresolvedPlaceholders(expanded));
    }

    [Fact]
    public void A_fully_resolved_url_reports_no_unresolved_placeholders()
    {
        Assert.False(AiTemplate.HasUnresolvedPlaceholders("https://acme.openai.azure.com/openai/v1"));
        Assert.False(AiTemplate.HasUnresolvedPlaceholders("https://api.openai.com/v1"));
    }

    [Fact]
    public void Placeholders_are_listed_in_order_without_duplicates()
    {
        Assert.Equal(new[] { "accountId", "gatewayId" },
            AiTemplate.PlaceholdersIn("https://x/{accountId}/{gatewayId}/{accountId}/compat"));
    }

    /// <summary>A connection is incomplete until every input its URL needs has been supplied — checked before
    /// a request rather than discovered as a connection failure.</summary>
    [Fact]
    public void A_connection_knows_when_it_is_not_yet_usable()
    {
        var incomplete = new AiConnection(
            "azure-prod", "Azure", ChatProviderKind.OpenAiCompatible,
            "https://{resourceName}.openai.azure.com/openai/v1",
            new List<AiModelEntry>(), new Dictionary<string, string>(), new Dictionary<string, string>());

        Assert.True(incomplete.IsIncomplete);

        var complete = incomplete with { Inputs = Inputs(("resourceName", "acme")) };

        Assert.False(complete.IsIncomplete);
        Assert.Equal("https://acme.openai.azure.com/openai/v1", complete.ResolvedBaseUrl);
    }
}
