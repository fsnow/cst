using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CST.Avalonia.Services.Presentation;
using CST.Avalonia.Tests.TestSupport;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace CST.Avalonia.Tests.Integration
{
    /// <summary>
    /// End-to-end coverage of <c>POST /v1/navigate</c> (#187) over real HTTP against the assembled stack, with a
    /// recording stand-in for the reader window. The load-bearing assertions are about CONSENT and HONESTY: a
    /// navigate must not reach the reader unless the user granted remote control, and the response must never say
    /// "presented" for something that did not happen. Shares the serial "LocalApiIntegration" collection
    /// (fixture indexing mutates the global Books singleton).
    /// </summary>
    [Collection("LocalApiIntegration")]
    public class NavigateEndpointTests : IAsyncLifetime
    {
        private LocalApiTestServer _api = null!;
        private RecordingPresentationService _reader = null!;
        private bool _consent;

        public async Task InitializeAsync()
        {
            _reader = new RecordingPresentationService();
            _consent = true;
            // The predicate is read PER REQUEST, so a test can revoke consent mid-life (see the toggle test).
            _api = await LocalApiTestServer.StartAsync(
                presentation: _reader, isRemoteControlAllowed: () => _consent);
        }

        public async Task DisposeAsync() => await _api.DisposeAsync();

        private async Task<(HttpStatusCode Status, NavigateBody? Body)> PostAsync(object request)
        {
            using var http = _api.Http();
            var resp = await http.PostAsJsonAsync("/v1/navigate", request);
            var body = resp.Content.Headers.ContentLength == 0
                ? null
                : await resp.Content.ReadFromJsonAsync<NavigateBody>();
            return (resp.StatusCode, body);
        }

        // Mirrors the wire shape of NavigateResponse (camelCase over the Web JSON defaults).
        private sealed record NavigateBody(bool Presented, string? Error, string? BookId, int Highlights, string? Note);

        [Fact]
        public async Task Denies_and_does_not_touch_the_reader_when_consent_is_off()
        {
            _consent = false;

            var (status, body) = await PostAsync(new { bookId = _api.MulaBook });

            Assert.Equal(HttpStatusCode.Forbidden, status);
            Assert.False(body!.Presented);
            Assert.Contains("Settings", body.Error);
            // The docs tell agents to check `presented` on every response, so failures must carry the full
            // shape rather than a bare { error }. (fable MED-4)
            Assert.Equal(_api.MulaBook, body.BookId);
            Assert.Equal(0, body.Highlights);
            // The point of the gate: the reader was never driven, not merely told about it afterwards.
            Assert.Empty(_reader.Requests);
        }

        [Fact]
        public async Task Consent_is_re_read_per_request_so_revoking_takes_effect_immediately()
        {
            var (first, _) = await PostAsync(new { bookId = _api.MulaBook });
            Assert.Equal(HttpStatusCode.OK, first);
            Assert.Single(_reader.Requests);

            // The user revokes permission while the server is running.
            _consent = false;

            var (second, _) = await PostAsync(new { bookId = _api.MulaBook });
            Assert.Equal(HttpStatusCode.Forbidden, second);
            Assert.Single(_reader.Requests);   // still just the first one
        }

        [Fact]
        public async Task Unknown_book_is_404_and_never_reaches_the_reader()
        {
            var (status, body) = await PostAsync(new { bookId = "not-a-real-book.xml" });

            Assert.Equal(HttpStatusCode.NotFound, status);
            Assert.Empty(_reader.Requests);
            Assert.Contains("not-a-real-book.xml", body!.Error);
        }

        [Fact]
        public async Task Missing_bookId_is_404_rather_than_an_unhandled_error()
        {
            var (status, _) = await PostAsync(new { anchor = "para1" });

            Assert.Equal(HttpStatusCode.NotFound, status);
            Assert.Empty(_reader.Requests);
        }

        [Fact]
        public async Task Opens_a_book_with_no_target_and_no_highlights()
        {
            var (status, body) = await PostAsync(new { bookId = _api.MulaBook });

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.True(body!.Presented);
            Assert.Equal(0, body.Highlights);
            Assert.Null(body.Note);

            var req = Assert.Single(_reader.Requests);
            Assert.Equal(_api.MulaBook, req.Book.FileName);
            Assert.Null(req.Target);
            // Script must stay null so the reader keeps whatever the USER has selected. (#187)
            Assert.Null(req.Script);
        }

        [Fact]
        public async Task Anchor_becomes_an_anchor_target()
        {
            var (status, _) = await PostAsync(new { bookId = _api.MulaBook, anchor = "para1" });

            Assert.Equal(HttpStatusCode.OK, status);
            var target = Assert.IsType<PresentationTarget.Anchor>(_reader.Last!.Target);
            Assert.Equal("para1", target.Name);
        }

        [Fact]
        public async Task Terms_are_resolved_to_real_hit_positions_against_the_index()
        {
            // 'dhamma' is in the mula fixture; the endpoint must do the index lookup so an agent only sends words.
            var (status, body) = await PostAsync(new { bookId = _api.MulaBook, terms = "dhamma" });

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.True(body!.Presented);
            Assert.True(body.Highlights > 0);
            Assert.Null(body.Note);

            var req = _reader.Last!;
            Assert.NotEmpty(req.Positions!);
            Assert.Equal(body.Highlights, req.Positions!.Count);
            // Terms handed to the reader are the matched IPE forms, which is what the highlighter expects.
            Assert.NotEmpty(req.SearchTerms!);
        }

        [Fact]
        public async Task A_hit_target_rides_along_with_the_resolved_positions()
        {
            var (status, body) = await PostAsync(new { bookId = _api.MulaBook, terms = "dhamma", hit = 1 });

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.True(body!.Presented);
            var target = Assert.IsType<PresentationTarget.Hit>(_reader.Last!.Target);
            Assert.Equal(1, target.Index);
        }

        [Fact]
        public async Task Terms_with_no_occurrences_still_open_the_book_but_say_so()
        {
            var (status, body) = await PostAsync(new { bookId = _api.MulaBook, terms = "nibbana" });

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.True(body!.Presented);          // the book still opens...
            Assert.Equal(0, body.Highlights);
            Assert.Contains("No occurrences", body.Note);   // ...with an honest reason for the missing highlights
            Assert.Empty(_reader.Last!.Positions!);
        }

        [Fact]
        public async Task A_hit_with_no_matching_terms_is_a_400_not_a_retryable_409()
        {
            // Asking for hit 2 of a word that isn't in the book cannot land anywhere. The planner rejects it and
            // the caller must be told, rather than being given a silent open at the top of the book. 400 because
            // resending the SAME request will never succeed — unlike a 409, which invites a retry. (fable MED-4)
            var (status, body) = await PostAsync(new { bookId = _api.MulaBook, terms = "nibbana", hit = 2 });

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.False(body!.Presented);
            Assert.NotNull(body.Error);
            Assert.Empty(_reader.Requests);          // nothing was opened...
            Assert.Equal(0, body.Highlights);        // ...so nothing can be claimed as applied
        }

        [Fact]
        public async Task Wildcard_mode_expands_the_query()
        {
            var (status, body) = await PostAsync(new { bookId = _api.MulaBook, terms = "dhamm*", mode = "Wildcard" });

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.True(body!.Highlights > 0);
        }

        [Fact]
        public async Task An_explicit_outputScript_is_passed_through_but_ipe_is_never_accepted()
        {
            var (status, _) = await PostAsync(new { bookId = _api.MulaBook, outputScript = "Devanagari" });
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(CST.Conversion.Script.Devanagari, _reader.Last!.Script);

            // The internal font encoding must never be selectable from outside. It is now REFUSED rather than
            // degraded to Latin: on an endpoint that changes the user's display, silently showing a different
            // script than the one asked for is itself a false success. (fable MED-3)
            var before = _reader.Requests.Count;
            var (ipeStatus, _) = await PostAsync(new { bookId = _api.MulaBook, outputScript = "Ipe" });
            Assert.Equal(HttpStatusCode.BadRequest, ipeStatus);
            Assert.Equal(before, _reader.Requests.Count);
        }

        [Fact]
        public async Task A_reader_that_cannot_present_is_a_409_not_a_false_success()
        {
            _reader.Result = PresentationResult.Fail("CST Reader is not presentable (no reader window is available).");

            var (status, body) = await PostAsync(new { bookId = _api.MulaBook });

            // 409, not 400: the arguments were fine, the app's STATE wasn't — an agent should retry, not "fix"
            // its request.
            Assert.Equal(HttpStatusCode.Conflict, status);
            Assert.False(body!.Presented);
            Assert.Contains("not presentable", body.Error);
        }

        [Fact]
        public async Task Overlapping_phrase_hits_do_not_produce_duplicate_positions()
        {
            // Overlapping proximity/phrase windows SHARE word positions, so the flattened hit list can hold
            // several entries at one source offset. Those must be collapsed before they reach the highlighter,
            // which rewrites back-to-front and would delete markup it had just inserted — corrupting the book in
            // the user's window. The search UI has always deduped; navigate must too. (fable HIGH-1)
            var (status, body) = await PostAsync(new { bookId = _api.MulaBook, terms = "dhamma citta" });

            Assert.Equal(HttpStatusCode.OK, status);
            var positions = _reader.Last!.Positions!;
            Assert.NotEmpty(positions);
            Assert.Equal(positions.Count, positions.Select(p => p.StartOffset).Distinct().Count());
            Assert.Equal(positions.Count, body!.Highlights);
            // And they must arrive in document order, as the highlighter expects.
            Assert.Equal(positions.OrderBy(p => p.Position).Select(p => p.Position), positions.Select(p => p.Position));
        }

        [Fact]
        public async Task A_malformed_regex_is_a_400_not_an_unhandled_500()
        {
            // A bad pattern is the CALLER's error. Before the fix this escaped as a bodyless 500, which the docs
            // give an agent no story for. (fable MED-2)
            var (status, body) = await PostAsync(new { bookId = _api.MulaBook, terms = "[", mode = "Regex" });

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.False(body!.Presented);
            Assert.Contains("not valid", body.Error);
            Assert.Empty(_reader.Requests);
        }

        [Fact]
        public async Task A_misspelled_outputScript_is_refused_rather_than_silently_flipping_to_Latin()
        {
            // This endpoint MUTATES the user's display, so a typo must not quietly switch their window to Latin
            // while reporting success — the typed field gets the surface's standard 400. (fable MED-3)
            var (status, _) = await PostAsync(new { bookId = _api.MulaBook, outputScript = "Devanagri" });

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Empty(_reader.Requests);
        }

        [Fact]
        public async Task A_misspelled_mode_is_refused_rather_than_silently_meaning_Exact()
        {
            // Falling back to Exact would search the literal "dhamm*" and report "no occurrences", misleading the
            // agent about WHY it found nothing. (fable LOW-6)
            var (status, _) = await PostAsync(new { bookId = _api.MulaBook, terms = "dhamm*", mode = "Wildcards" });

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Empty(_reader.Requests);
        }

        [Fact]
        public async Task An_integer_mode_ordinal_is_refused_too()
        {
            // The typed field alone isn't enough: JsonStringEnumConverter also accepts integers, so an
            // out-of-range ordinal would bind to an undefined SearchMode and be silently treated as Exact —
            // reintroducing the very failure the typed field closed for strings. (fable, #304 hazard)
            var (status, body) = await PostAsync(new { bookId = _api.MulaBook, terms = "dhamm*", mode = 99 });

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Contains("Unknown mode", body!.Error);
            Assert.Empty(_reader.Requests);
        }

        [Fact]
        public async Task A_failed_presentation_reports_zero_highlights_despite_resolving_some()
        {
            // 42 resolved positions that were never applied must not read as partial success. (fable MED-4)
            _reader.Result = PresentationResult.Fail("Duplicate open suppressed — this book was just opened.");

            var (status, body) = await PostAsync(new { bookId = _api.MulaBook, terms = "dhamma" });

            Assert.Equal(HttpStatusCode.Conflict, status);
            Assert.False(body!.Presented);
            Assert.Equal(0, body.Highlights);
        }

        [Fact]
        public async Task Hit_wins_over_anchor_when_both_are_sent()
        {
            // Documented precedence — an explicit hit is the more specific intent. (fable LOW-7)
            var (status, _) = await PostAsync(
                new { bookId = _api.MulaBook, terms = "dhamma", hit = 1, anchor = "para1" });

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.IsType<PresentationTarget.Hit>(_reader.Last!.Target);
        }

        // ---- MCP surface ----
        // The REST route and the MCP tool share NavigateService; these assert that the sharing is real, so the
        // two surfaces cannot drift in consent or behaviour. (fable: "one implementation" was untested)

        private async Task<McpClient> ConnectMcpAsync()
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new System.Uri(_api.BaseUrl + "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new System.Collections.Generic.Dictionary<string, string>
                    { ["Authorization"] = "Bearer " + _api.Token },
            });
            return await McpClient.CreateAsync(transport);
        }

        [Fact]
        public async Task Mcp_navigate_tool_is_registered_and_drives_the_same_reader()
        {
            await using var client = await ConnectMcpAsync();

            var tools = await client.ListToolsAsync();
            Assert.Contains(tools, t => t.Name == "navigate");

            var result = await client.CallToolAsync("navigate",
                new System.Collections.Generic.Dictionary<string, object?>
                    { ["bookId"] = _api.MulaBook, ["terms"] = "dhamma" });

            Assert.NotEqual(true, result.IsError);
            var req = Assert.Single(_reader.Requests);
            Assert.Equal(_api.MulaBook, req.Book.FileName);
            Assert.NotEmpty(req.Positions!);
        }

        [Fact]
        public async Task Mcp_navigate_honors_the_same_consent_gate_as_rest()
        {
            _consent = false;
            await using var client = await ConnectMcpAsync();

            var result = await client.CallToolAsync("navigate",
                new System.Collections.Generic.Dictionary<string, object?> { ["bookId"] = _api.MulaBook });

            // MCP has no status codes, so the refusal must come through in the payload — and, as with REST, the
            // reader must never have been touched.
            var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text))
                       + (result.StructuredContent?.GetRawText() ?? string.Empty);
            Assert.Contains("Remote control is disabled", text);
            Assert.Empty(_reader.Requests);
        }


        [Fact]
        public async Task Navigate_is_documented_so_an_agent_can_tell_the_user_how_to_enable_it()
        {
            using var http = _api.Http();
            var full = await http.GetStringAsync("/llms-full.txt");
            Assert.Contains("/v1/navigate", full);
            Assert.Contains("presented", full);

            // Documented even while consent is OFF — a hidden endpoint can't teach the user how to grant it.
            _consent = false;
            var slice = await http.GetStringAsync("/docs/navigate.md");
            Assert.Contains("/v1/navigate", slice);
            // The docs must name the checkbox EXACTLY as the Settings window labels it. (see SettingsWindow.axaml)
            Assert.Contains("Allow agents to drive the reader (navigate and highlight)", slice);
        }
    }
}
