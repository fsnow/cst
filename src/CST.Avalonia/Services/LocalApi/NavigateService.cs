using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services.Presentation;
using TermPosition = CST.Avalonia.Models.TermPosition;
using SearchMode = CST.Avalonia.Models.SearchMode;

namespace CST.Avalonia.Services.LocalApi
{
    /// <summary>Why a navigate ended the way it did — lets the REST route pick a status code while MCP, which
    /// has none, reports the same thing through <see cref="NavigateResponse.Presented"/> + Error.</summary>
    internal enum NavigateOutcome
    {
        Presented,
        ConsentDenied,
        UnknownBook,
        /// <summary>The ARGUMENTS can't be satisfied (a hit with nothing to land on, a malformed pattern).
        /// Distinct from <see cref="NotPresented"/> because retrying this unchanged will never work.</summary>
        InvalidRequest,
        /// <summary>The request was fine but the app's STATE prevented it — no reader window, duplicate open.
        /// Retrying later is exactly the right response.</summary>
        NotPresented
    }

    /// <summary>
    /// The one implementation behind both <c>POST /v1/navigate</c> and the MCP <c>navigate</c> tool (#187):
    /// check remote-control consent, resolve the book and any highlight positions, then hand off to the shared
    /// <see cref="IPresentationService"/> that the search UI also drives. Keeping it here (rather than in each
    /// transport) is what stops the two surfaces from drifting apart in behaviour.
    /// </summary>
    internal sealed class NavigateService
    {
        // Matches the search UI's default word distance for multi-word proximity (SearchQuery.ProximityDistance).
        private const int DefaultProximityDistance = 10;

        private readonly IPresentationService _presentation;
        private readonly ISearchService? _search;
        private readonly Func<bool> _isRemoteControlAllowed;

        public NavigateService(IPresentationService presentation, ISearchService? search,
            Func<bool> isRemoteControlAllowed)
        {
            _presentation = presentation;
            _search = search;
            // A PREDICATE, not a captured bool: the user can revoke remote control in Settings while the server
            // is running, and that must take effect on the very next call.
            _isRemoteControlAllowed = isRemoteControlAllowed;
        }

        // The quoted text must stay EXACTLY the SettingsWindow checkbox label — an agent relays this to the
        // user, and a paraphrase sends them looking for a control that doesn't exist by that name.
        public const string ConsentDeniedMessage =
            "Remote control is disabled. The user must enable Settings → AI → \"Allow agents to drive the " +
            "reader (navigate and highlight)\" before an agent can navigate; this is the user's decision, " +
            "not something to work around.";

        public async Task<(NavigateOutcome Outcome, NavigateResponse Response)> NavigateAsync(
            NavigateRequest req, CancellationToken ct = default)
        {
            var bookId = req.BookId ?? string.Empty;

            // Highlights is always 0 on a failure: nothing was applied, so reporting a count would read as a
            // partial success for something that did not happen. (fable MED-4)
            NavigateResponse Failed(string error, string? note = null) => new(false, error, bookId, 0, note);

            if (!_isRemoteControlAllowed())
                return (NavigateOutcome.ConsentDenied, Failed(ConsentDeniedMessage));

            var book = string.IsNullOrWhiteSpace(req.BookId)
                ? null
                : Books.Inst.FirstOrDefault(b =>
                    string.Equals(b.FileName, req.BookId, StringComparison.OrdinalIgnoreCase));
            if (book == null)
                return (NavigateOutcome.UnknownBook,
                    Failed($"Unknown book '{req.BookId}'. Use the books tool to get valid book ids."));

            // From here on report the CANONICAL file name rather than echoing the caller's casing. (fable)
            bookId = book.FileName;

            // JsonStringEnumConverter also accepts INTEGERS, so {"mode": 99} binds to an undefined SearchMode
            // that the search stack would silently treat as Exact — the same #304 ordinal hazard McpSearchMode
            // guards on the MCP side, and the silent-Exact failure the typed field closed for strings. (fable)
            if (req.Mode is { } mode && !Enum.IsDefined(mode))
                return (NavigateOutcome.InvalidRequest,
                    Failed($"Unknown mode '{(int)mode}'. Use Exact, Wildcard, or Regex."));

            List<TermPosition> positions;
            List<string> terms;
            string? note;
            try
            {
                (positions, terms, note) = await ResolveHighlightsAsync(req, ct);
            }
            // Scoped to the modes that actually carry a pattern: in Exact mode there is nothing for the caller to
            // have malformed, so an ArgumentException there is an app fault and must NOT be reported as their
            // mistake. (fable)
            catch (Exception ex) when (req.Mode is SearchMode.Wildcard or SearchMode.Regex
                                       && ex is RegexParseException or ArgumentException)
            {
                // A malformed wildcard/regex is the CALLER's error, not an app fault: answer it as one instead of
                // letting it escape as the bodyless 500 an agent has no story for. (fable MED-2)
                return (NavigateOutcome.InvalidRequest,
                    Failed($"The {req.Mode ?? SearchMode.Exact} pattern '{req.Terms}' is not valid: {ex.Message}"));
            }

            PresentationTarget? target =
                req.Hit is int hit ? new PresentationTarget.Hit(hit)
                : !string.IsNullOrWhiteSpace(req.Anchor) ? new PresentationTarget.Anchor(req.Anchor!)
                : null;

            var request = new PresentationRequest
            {
                Book = book,
                Target = target,
                SearchTerms = terms,
                Positions = positions,
                // Only override the reader's current script when the caller actually asked for one.
                Script = req.OutputScript
            };

            // Ask the SAME validator the presentation service uses, before calling it, purely so an argument
            // problem (a hit with no highlights to land on) is reported as one rather than as the retryable
            // "app state" failure a 409 implies. No duplicated rules — it is the shared planner. (fable MED-4)
            if (PresentationPlanner.Validate(request) is { } invalid)
                return (NavigateOutcome.InvalidRequest, Failed(invalid, note));

            var result = await _presentation.PresentAsync(request, ct);
            if (!result.Presented)
                return (NavigateOutcome.NotPresented, Failed(result.Error ?? "The reader was not presented.", note));

            return (NavigateOutcome.Presented,
                new NavigateResponse(true, null, book.FileName, positions.Count, note));
        }

        /// <summary>
        /// Resolve the request's <c>terms</c> into the hit positions the reader highlights, so an agent only has
        /// to send a query string — the same index lookup the search UI does before opening a book. Returns an
        /// explanatory note rather than failing when there is simply nothing to highlight; a malformed PATTERN
        /// throws, and the caller turns that into an InvalidRequest.
        /// </summary>
        private async Task<(List<TermPosition> Positions, List<string> Terms, string? Note)>
            ResolveHighlightsAsync(NavigateRequest req, CancellationToken ct)
        {
            var noPositions = new List<TermPosition>();
            var noTerms = new List<string>();

            if (string.IsNullOrWhiteSpace(req.Terms))
                return (noPositions, noTerms, null);
            if (_search is not { } search)
                // Phrased so it does not assert an open that may not happen — the note travels with failures too.
                return (noPositions, noTerms, "Search is unavailable; no highlights were resolved.");

            // The multi-word path handles every case uniformly — single word, phrase, wildcard/regex expansion —
            // converts the query from any script to IPE, and stamps each position's matched Word.
            var hits = await search.GetMultiWordPositionsAsync(
                req.BookId!, req.Terms!, req.Mode ?? SearchMode.Exact,
                req.ProximityDistance ?? DefaultProximityDistance, ct);

            // Overlapping hits SHARE word positions, so the flattened list can hold several entries at one source
            // offset. They must be collapsed before highlighting — the same step the search UI does — or the
            // highlighter's back-to-front rewrite deletes the markup it just inserted. (fable HIGH-1)
            var positions = HighlightPositions.Dedupe(hits.SelectMany(h => h));

            if (positions.Count == 0)
                return (noPositions, noTerms,
                    $"No occurrences of '{req.Terms}' in this book; no highlights were applied.");

            var terms = positions
                .Select(p => p.Word)
                .Where(w => !string.IsNullOrEmpty(w))
                .Distinct(StringComparer.Ordinal)
                .Select(w => w!)
                .ToList();
            return (positions, terms, null);
        }
    }
}
