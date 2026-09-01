using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CST.Avalonia.Services;
using CST.Avalonia.ViewModels;
using CST.Conversion;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services.Ai;

/// <summary>Why the reader could not say — unambiguously — what the user is looking at.</summary>
public enum ReaderStateProblem
{
    /// <summary>No reader window, or no book document in it.</summary>
    NoBookOpen,

    /// <summary>
    /// A book is open but its position is unknown. The reading position is derived from scroll and reports
    /// <c>"*"</c> until the page settles — a real transient state, not a defect.
    /// </summary>
    PositionUnknown,

    /// <summary>
    /// The book is a Multi volume, where a paragraph number needs a sub-book code to be unambiguous, and the
    /// reader does not report one. See <see cref="ReaderStateService"/> for why this refuses rather than guesses.
    /// </summary>
    AmbiguousInMultiBook,

    /// <summary>More than one book window could be the one in use, and none can be shown to be.</summary>
    AmbiguousBookWindow,
}

/// <summary>What the user is looking at: the book, where in it, and what (if anything) they have selected.</summary>
/// <param name="BookId">The open book's file name — what the bundler and the passage tool key on.</param>
/// <param name="Paragraph">The paragraph the viewport is on.</param>
/// <param name="SelectionText">The user's selection, already through <c>SelectionPipeline.Normalize</c> —
/// Latin, composed, whitespace-collapsed. Null means <i>nothing was selected</i>.</param>
/// <param name="SelectionUnavailable">The selection could not be read at all — the WebView was not ready, or
/// the <c>document.title</c> round trip timed out. <b>Not the same as a null
/// <paramref name="SelectionText"/></b>: conflating them is what makes a dropped selection look to the user
/// like "the AI ignored my selection". (#581)</param>
/// <param name="SelectionParagraph">
/// The paragraph the SELECTION sits in, resolved from its own position in the document — not from
/// <paramref name="Paragraph"/>, which is derived from scroll.
///
/// <para>The distinction is the whole of #649. The passage window used to be built around the scroll
/// position, so a selection near the bottom of the viewport landed outside the window meant to explain it,
/// and the app reported that as a caveat instead of as the defect it was. Null when there is no selection,
/// or when the anchor cache could not place it — the caller then falls back to
/// <paramref name="Paragraph"/>.</para>
/// </param>
public sealed record ReaderState(
    string BookId,
    int Paragraph,
    string? SelectionText,
    bool SelectionUnavailable = false,
    int? SelectionParagraph = null);

/// <summary>
/// Whether the caller can say which book window the reader means. (#938)
///
/// <para><b>The two consumers differ, and only one of them is blind.</b> The Assistant panel is driven by a
/// person clicking in this app, so the app knows which book they were last in. An HTTP or MCP caller is an
/// outside agent with no such history — for it, several open book windows genuinely are ambiguous.</para>
///
/// <para>Applying the blind caller's rule to the sighted one is #938: with a book floated beside a docked
/// one, the Assistant refused every question, having never asked the question it could have answered.</para>
/// </summary>
public enum ReaderFocusSignal
{
    /// <summary>
    /// The caller is the reader, in this app. Resolve to <b>the last book they were in</b> — [fsnow]'s rule.
    ///
    /// <para>The last <i>book</i>, not the last window: reaching the Assistant means clicking into the main
    /// window, so "last window focused" would answer with the main window's book while the reader meant the
    /// floated one they had just selected in.</para>
    /// </summary>
    LastFocusedBook,

    /// <summary>
    /// The caller is outside the app and carries no focus history. More than one active book window is an
    /// honest <see cref="ReaderStateProblem.AmbiguousBookWindow"/>.
    /// </summary>
    None,
}

/// <summary>Success or a named refusal. Never a partial answer.</summary>
public readonly record struct ReaderStateResult(ReaderState? State, ReaderStateProblem? Problem)
{
    public static ReaderStateResult Ok(ReaderState state) => new(state, null);
    public static ReaderStateResult Fail(ReaderStateProblem problem) => new(null, problem);
}

/// <summary>Reads what the reader is currently showing. See <see cref="ReaderStateService"/>.</summary>
public interface IReaderStateService
{
    /// <summary>
    /// Safe to call from any thread — marshals to the UI thread itself, as the presentation service does.
    /// Returns a failure RESULT rather than throwing for every expected condition.
    ///
    /// <para><b>Requires a running Avalonia dispatcher</b>, which the app always has (the local API only runs
    /// while the app does). In a headless process nothing pumps it and the call would simply not complete, so
    /// tests should substitute this interface rather than exercise the live implementation.</para>
    /// </summary>
    /// <param name="focus">
    /// What the caller knows about which window the reader means (#938). Defaulted to
    /// <see cref="ReaderFocusSignal.None"/> so a caller has to SAY it can resolve — the failure being fixed
    /// was an interactive caller silently inheriting an external agent's blindness, and a default that
    /// resolves would hide the same mistake in the other direction.
    /// </param>
    Task<ReaderStateResult> GetCurrentAsync(
        ReaderFocusSignal focus = ReaderFocusSignal.None, CancellationToken ct = default);
}

/// <summary>
/// Reads what the reader is currently showing.
///
/// <para>The read-side counterpart to <see cref="Presentation.IPresentationService"/>, which can drive the
/// reader but cannot report on it. Surface B needs the opposite direction: the app must be able to say what the
/// user is looking at before it can ask a model about it. (#593)</para>
///
/// <para><b>Every uncertainty is a refusal, never a guess.</b> The charter is that a preview must never describe
/// a passage the user is not looking at, so anything this cannot establish unambiguously becomes a named
/// problem. Three are load-bearing:</para>
/// <list type="bullet">
/// <item><b>Unknown position.</b> <c>AiContextRequest.Reference</c> is nullable and a null reference reads from
/// the BOOK START, so falling through would answer confidently about the wrong passage, with an app-rendered
/// citation vouching for it.</item>
/// <item><b>Multi volumes.</b> Paragraph numbering restarts per sub-book, so a bare number is ambiguous — and
/// <c>BookMarkers.PositionOfParagraph(n, null)</c> returns the FIRST match across all of them rather than
/// reporting the ambiguity. The reader's status pipeline strips the <c>_bookcode</c> suffix before it reaches
/// here, so the disambiguating code is genuinely unavailable; reviving that plumbing is follow-up work. Until
/// then a refusal is right and a plausible wrong answer is not.</item>
/// <item><b>Which window.</b> A floated book can be the one being read, so the main dock alone is not the
/// answer.</item>
/// </list>
/// </summary>
public sealed class ReaderStateService : IReaderStateService
{
    private readonly ILogger<ReaderStateService> _logger;

    public ReaderStateService(ILogger<ReaderStateService> logger) => _logger = logger;

    public async Task<ReaderStateResult> GetCurrentAsync(
        ReaderFocusSignal focus = ReaderFocusSignal.None, CancellationToken ct = default)
    {
        // ONE resolution of the active document. An earlier version read the book and then re-resolved it to
        // fetch the selection, which let a tab switch during the selection round-trip (up to 700 ms) attribute
        // one book's selection to another book's paragraph.
        var (result, document) = await Dispatcher.UIThread.InvokeAsync(() => ReadActiveBook(focus));
        if (result.State is null || document is null) return result;

        ct.ThrowIfCancellationRequested();

        var (selection, unavailable, selectionParagraph) = await ReadSelectionAsync(document);

        // Logged at Information because the difference between these two numbers is the whole of #649: the
        // scroll paragraph is where the viewport is, the selection paragraph is where the reader pointed, and
        // a context built from the first is the defect. If they differ and the window still follows the
        // scroll, the anchor is not arriving.
        _logger.LogInformation(
            "Reader state: scroll paragraph {ScrollPara}, selection paragraph {SelectionPara}, " +
            "selection {Length} char(s), unavailable {Unavailable}",
            result.State.Paragraph, selectionParagraph?.ToString() ?? "(none)",
            selection?.Length ?? 0, unavailable);

        return ReaderStateResult.Ok(
            result.State with
            {
                SelectionText = selection,
                SelectionUnavailable = unavailable,
                SelectionParagraph = selectionParagraph,
            });
    }

    /// <summary>
    /// The last book the reader was in, from the app's own interaction history. (#938)
    ///
    /// <para><b>The last BOOK, not the last window.</b> Reaching the Assistant means clicking into the main
    /// window, so a window-level answer would name the main window's book while the reader meant the floated
    /// one they had just selected in. Walking a history of dockables and keeping only the books skips the
    /// panel they clicked to ask the question.</para>
    ///
    /// <para><b>Selecting text is what puts a book at the head of this list.</b> Selecting requires clicking
    /// into the book's WebView, which raises the CEF focus callback that feeds the tracker
    /// (<c>BookDisplayView.OnBrowserGotFocus</c>) — the feed that exists because Avalonia focus goes blind
    /// inside CEF (#621). So the book whose selection the reader means is already at the front, which is why
    /// this resolves the reported case without consulting selections at all. Selections carry no timestamp
    /// and cannot be compared; the act of making one can be.</para>
    ///
    /// <para><b>Only among the candidates.</b> The history is app-wide and outlives tabs, so an entry may be
    /// a book that is no longer any window's active document. Intersecting with the live set keeps this from
    /// resurrecting one.</para>
    /// </summary>
    private static BookDisplayViewModel? LastFocusedBook(IReadOnlyList<BookDisplayViewModel> candidates)
    {
        var recent = App.TryGetService<ActiveDocumentTracker>()?.Recent;
        if (recent is null) return null;

        foreach (var dockable in recent)
        {
            if (dockable is BookDisplayViewModel book && candidates.Contains(book))
                return book;
        }

        return null;
    }

    private (ReaderStateResult Result, BookDisplayViewModel? Document) ReadActiveBook(
        ReaderFocusSignal focus)
    {
        // The dock factory is not in DI — it belongs to the main window's layout, the same lookup the
        // presentation service and the search panel use.
        if ((App.MainWindow?.DataContext as LayoutViewModel)?.Factory is not CstDockFactory factory)
        {
            _logger.LogDebug("Reader state requested but no reader layout is available");
            return (ReaderStateResult.Fail(ReaderStateProblem.NoBookOpen), null);
        }

        var candidates = factory.ActiveBookDocuments.ToList();
        if (candidates.Count == 0)
            return (ReaderStateResult.Fail(ReaderStateProblem.NoBookOpen), null);

        // Several active book windows - a docked book and a floated one. Whether that is ambiguous depends
        // entirely on who is asking (#938).
        //
        // Resolved here rather than returned early on purpose: the chosen book still has to pass the Multi
        // volume and position checks below. Answering about a floated book with an unknown paragraph would
        // trade one wrong answer for another.
        BookDisplayViewModel document;
        if (candidates.Count == 1)
        {
            document = candidates[0];
        }
        else if (focus == ReaderFocusSignal.LastFocusedBook && LastFocusedBook(candidates) is { } chosen)
        {
            _logger.LogDebug(
                "Reader state: {Count} active book windows, resolved to the last one focused ({Book})",
                candidates.Count, chosen.Book?.FileName);
            document = chosen;
        }
        else
        {
            // No history to go on, or a caller that never had any. Refuse rather than pick: an outside agent
            // has no click to remember, and the charter here is that a preview must never describe a passage
            // the user is not looking at.
            _logger.LogDebug("Reader state is ambiguous: {Count} active book windows", candidates.Count);
            return (ReaderStateResult.Fail(ReaderStateProblem.AmbiguousBookWindow), null);
        }

        if (document.Book.BookType == BookType.Multi)
        {
            _logger.LogDebug(
                "Reader state: {Book} is a Multi volume; a bare paragraph number is ambiguous",
                document.Book.FileName);
            return (ReaderStateResult.Fail(ReaderStateProblem.AmbiguousInMultiBook), null);
        }

        // "*" is the view model's own "not known yet" — the position is derived from scroll and reports that
        // until the page settles.
        if (!int.TryParse(document.CurrentParagraph, out var paragraph) || paragraph <= 0)
        {
            _logger.LogDebug(
                "Reader state: {Book} is open but its paragraph is '{Paragraph}'",
                document.Book.FileName, document.CurrentParagraph);
            return (ReaderStateResult.Fail(ReaderStateProblem.PositionUnknown), null);
        }

        return (ReaderStateResult.Ok(new ReaderState(document.Book.FileName, paragraph, SelectionText: null)),
                document);
    }

    /// <summary>
    /// Ask the WebView for the selection and put it through <see cref="SelectionPipeline"/>.
    ///
    /// <para><b>The channel distinguishes two failures and so does this.</b>
    /// <c>GetWebViewSelectionAsync</c> returns an empty string when the user genuinely selected nothing, and
    /// <c>null</c> when it could not find out — the browser was not initialised, the script threw, or the
    /// <c>document.title</c> round trip exceeded its 700 ms budget. Collapsing both to "no selection" is what
    /// produces the report "the AI ignored my selection", so the distinction is carried upward. (#581)</para>
    ///
    /// <para><b>Conversion happens here because the display script is only known here</b>, and it is not a
    /// refinement: hand the bundler Devanagari and every lookup misses and every window match fails, which makes
    /// the two grammatical presets Latin-readers-only — the opposite of the user who prompted surface B.</para>
    ///
    /// <para>Run entirely on the UI thread: every other caller of the selection channel is a UI-thread command
    /// handler, it drives CEF, and it writes a single-slot completion source a concurrent Cmd+D would
    /// otherwise clobber.</para>
    /// </summary>
    private static async Task<(string? Text, bool Unavailable, int? Paragraph)> ReadSelectionAsync(
        BookDisplayViewModel document)
    {
        var (raw, paragraph) = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var control = document.BookDisplayControl;
            return control is null
                ? ((string?)null, (int?)null)
                : await control.GetWebViewSelectionWithParagraphAsync();
        });

        // null = could not read it; "" = read it, and there was nothing to read.
        if (raw is null) return (null, true, null);

        return (SelectionPipeline.Normalize(raw, document.BookScript), false, paragraph);
    }
}
