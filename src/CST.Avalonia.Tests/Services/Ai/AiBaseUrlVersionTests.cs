using System.Collections.Generic;
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
/// #742: a bare-host base URL always gained a <c>/v1</c>, which is wrong for at least one real provider.
/// Perplexity documents <c>https://api.perplexity.ai</c> and serves <c>/chat/completions</c>, so every
/// request 404'd against a URL the reader could not see us rewriting.
///
/// <para>The answer is measured, not configured: the model listing asks once, and what it learns is recorded
/// on the connection so no later request has to guess. These tests pin the three parts of that — the guess,
/// the probe, and the memory.</para>
/// </summary>
public class AiBaseUrlVersionTests
{
    private static AiConnection Connection(string baseUrl, bool? usesVersionSegment = null) => new(
        Id: "custom",
        DisplayName: "Custom",
        Kind: ChatProviderKind.OpenAiCompatible,
        BaseUrl: baseUrl,
        Models: new List<AiModelEntry>(),
        Headers: new Dictionary<string, string>(),
        Inputs: new Dictionary<string, string>(),
        UsesVersionSegment: usesVersionSegment);

    private static (AiModelCatalog Catalog, List<string> Urls) Catalog(
        params (HttpStatusCode Status, string Body)[] responses)
    {
        var urls = new List<string>();
        var next = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            urls.Add(request.RequestUri!.ToString());
            var (status, body) = responses[next < responses.Length ? next : responses.Length - 1];
            next++;
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        });

        return (new AiModelCatalog(new HttpClient(handler), null, NullLogger<AiModelCatalog>.Instance), urls);
    }

    private const string OneModel = """{"data":[{"id":"sonar-pro"}]}""";

    // ---- the guess, unchanged where it was right ---------------------------------------------------------

    [Fact]
    public async Task A_bare_host_is_still_tried_with_the_version_segment_first()
    {
        var (catalog, urls) = Catalog((HttpStatusCode.OK, OneModel));

        var result = await catalog.FetchAsync(Connection("https://api.deepseek.com"));

        Assert.True(result.Ok);
        Assert.Equal("https://api.deepseek.com/v1/models", Assert.Single(urls));
        // Nothing was learned, because nothing needed to be: the guess was right on the first try.
        Assert.Null(result.UsesVersionSegment);
    }

    // ---- the probe ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_404_on_the_guessed_url_asks_the_other_way_once_and_reports_what_worked()
    {
        var (catalog, urls) = Catalog(
            (HttpStatusCode.NotFound, "not found"),
            (HttpStatusCode.OK, OneModel));

        var result = await catalog.FetchAsync(Connection("https://api.perplexity.ai"));

        Assert.True(result.Ok);
        Assert.Equal(
            new[] { "https://api.perplexity.ai/v1/models", "https://api.perplexity.ai/models" },
            urls);
        Assert.False(result.UsesVersionSegment);
    }

    /// <summary>
    /// The probe is for a URL we guessed at. A base that already carries its own version segment was not
    /// guessed at, so a 404 there is the endpoint's answer and must not cost a second request.
    /// </summary>
    [Fact]
    public async Task A_404_on_a_url_the_reader_fully_specified_is_not_second_guessed()
    {
        var (catalog, urls) = Catalog((HttpStatusCode.NotFound, "not found"));

        var result = await catalog.FetchAsync(Connection("https://openrouter.ai/api/v1"));

        Assert.False(result.Ok);
        Assert.Equal("https://openrouter.ai/api/v1/models", Assert.Single(urls));
    }

    /// <summary>Asked once, never again — a connection that already knows does not re-probe.</summary>
    [Fact]
    public async Task A_connection_that_already_knows_does_not_probe_again()
    {
        var (catalog, urls) = Catalog((HttpStatusCode.NotFound, "not found"));

        var result = await catalog.FetchAsync(
            Connection("https://api.perplexity.ai", usesVersionSegment: false));

        Assert.False(result.Ok);
        Assert.Equal("https://api.perplexity.ai/models", Assert.Single(urls));
    }

    // ---- the memory, applied to the path that actually matters -------------------------------------------

    /// <summary>
    /// The whole point: the chat request is what was 404-ing, and it is not the request that can afford to
    /// probe. A recorded answer moves the fix from the listing to the answer.
    /// </summary>
    [Fact]
    public void A_recorded_answer_changes_where_a_chat_request_goes()
    {
        Assert.Equal(
            "https://api.perplexity.ai/chat/completions",
            AiHttp.ResolveEndpoint(
                "https://api.perplexity.ai", "v1/chat/completions", "chat/completions",
                BaseUrlConvention.IncludesVersion, usesVersionSegment: false).ToString());

        Assert.Equal(
            "https://api.deepseek.com/v1/chat/completions",
            AiHttp.ResolveEndpoint(
                "https://api.deepseek.com", "v1/chat/completions", "chat/completions",
                BaseUrlConvention.IncludesVersion, usesVersionSegment: true).ToString());
    }

    /// <summary>
    /// A measured answer replaces a GUESS, never a fact about the URL in hand. A base that already names its
    /// version keeps it whatever was recorded, or a stale record would start mangling a correct URL.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_base_url_that_already_carries_a_version_is_left_alone_either_way(bool recorded)
    {
        Assert.Equal(
            "https://openrouter.ai/api/v1/chat/completions",
            AiHttp.ResolveEndpoint(
                "https://openrouter.ai/api/v1", "v1/chat/completions", "chat/completions",
                BaseUrlConvention.IncludesVersion, usesVersionSegment: recorded).ToString());
    }
}
