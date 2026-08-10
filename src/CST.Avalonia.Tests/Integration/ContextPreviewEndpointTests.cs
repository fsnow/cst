using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Services.Ai;
using CST.Avalonia.Tests.TestSupport;
using Xunit;

namespace CST.Avalonia.Tests.Integration;

/// <summary>
/// <c>POST /v1/ai/context-preview</c> against the assembled server. The refusals carry the weight here: the
/// endpoint's whole safety argument is that it never answers when the app cannot say what the user is looking
/// at, and a preview that succeeded where the real invocation would fail would be showing a fiction. (#593)
/// </summary>
[Collection("LocalApiIntegration")]
public class ContextPreviewEndpointTests
{
    /// <summary>A reader that reports whatever the test wants — the real one reads the live dock.</summary>
    private sealed class FakeReaderState : IReaderStateService
    {
        private readonly ReaderStateResult _result;

        internal FakeReaderState(ReaderStateResult result) => _result = result;

        public Task<ReaderStateResult> GetCurrentAsync(CancellationToken ct = default) => Task.FromResult(_result);
    }

    /// <summary>Records what it was asked for; the bundler itself is tested directly elsewhere.</summary>
    private sealed class RecordingBundler : IAiContextBundler
    {
        internal AiContextRequest? LastRequest { get; private set; }

        public Task<AiContextBundle> BuildAsync(AiContextRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            throw new AiContextException("stub: not building a real bundle");
        }
    }

    private static ReaderStateResult Reading(string bookId, int paragraph, string? selection = null) =>
        ReaderStateResult.Ok(new ReaderState(bookId, paragraph, selection));

    private static async Task<HttpResponseMessage> PreviewAsync(
        LocalApiTestServer server, object body)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", server.Token);
        return await http.PostAsJsonAsync($"{server.BaseUrl}/v1/ai/context-preview", body);
    }

    [Fact]
    public async Task No_book_open_is_refused_rather_than_answered()
    {
        await using var server = await LocalApiTestServer.StartAsync(
            contextBundler: new RecordingBundler(),
            readerState: new FakeReaderState(ReaderStateResult.Fail(ReaderStateProblem.NoBookOpen)));

        var response = await PreviewAsync(server, new { task = "explain" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("no-book-open", body.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task An_unknown_reading_position_is_refused_rather_than_read_from_the_book_start()
    {
        // The dangerous one. AiContextRequest.Reference is nullable and a null reference reads from the START
        // of the book, so falling through here would produce a confident, app-cited answer about a passage the
        // user is not looking at — with nothing to indicate it.
        var bundler = new RecordingBundler();
        await using var server = await LocalApiTestServer.StartAsync(
            contextBundler: bundler,
            readerState: new FakeReaderState(ReaderStateResult.Fail(ReaderStateProblem.PositionUnknown)));

        var response = await PreviewAsync(server, new { task = "explain" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("position-unknown", body.RootElement.GetProperty("reason").GetString());

        // Nothing was even attempted — the refusal is upstream of the bundler.
        Assert.Null(bundler.LastRequest);
    }

    [Fact]
    public async Task An_unknown_task_is_a_bad_request()
    {
        await using var server = await LocalApiTestServer.StartAsync(
            contextBundler: new RecordingBundler(),
            readerState: new FakeReaderState(Reading("s0101m.mul.xml", 5)));

        var response = await PreviewAsync(server, new { task = "interpretive-dance" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unknown-task", body.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task The_bundle_is_built_from_live_reader_state_not_from_the_request_body()
    {
        // The design decision the endpoint exists to embody: the caller supplies only what the USER chooses.
        // Book, position and selection are read from the app, so the preview exercises the real input path.
        var bundler = new RecordingBundler();
        await using var server = await LocalApiTestServer.StartAsync(
            contextBundler: bundler,
            readerState: new FakeReaderState(Reading(server_book(), 271, "appamādo")));

        await PreviewAsync(server, new { task = "grammar", userQuestion = "how is this formed?" });

        var request = Assert.IsType<AiContextRequest>(bundler.LastRequest);
        Assert.Equal(AiTask.Grammar, request.Task);
        Assert.Equal("how is this formed?", request.UserQuestion);
        Assert.Equal(server_book(), request.BookId);
        Assert.Equal("appamādo", request.SelectionText);
        Assert.Equal(
            271,
            Assert.IsType<CST.Navigation.NavigationReference.Paragraph>(request.Reference).Number);

        static string server_book() => "s0101m.mul.xml";
    }

    [Fact]
    public async Task The_endpoint_requires_the_bearer_token_like_every_other_data_route()
    {
        await using var server = await LocalApiTestServer.StartAsync(
            contextBundler: new RecordingBundler(),
            readerState: new FakeReaderState(Reading("s0101m.mul.xml", 5)));

        using var http = new HttpClient();
        var response = await http.PostAsJsonAsync(
            $"{server.BaseUrl}/v1/ai/context-preview", new { task = "explain" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_route_is_absent_when_surface_B_is_not_wired()
    {
        // Consistent with /v1/passage and the lemma routes: an unwired capability has no endpoint rather than
        // an endpoint that errors.
        await using var server = await LocalApiTestServer.StartAsync();

        var response = await PreviewAsync(server, new { task = "explain" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
