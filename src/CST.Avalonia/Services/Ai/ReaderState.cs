using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
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
/// <param name="SelectionText">The user's selection, if any, <b>already converted to Latin</b> — see
/// <see cref="ReaderStateService"/>.</param>
public sealed record ReaderState(
    string BookId,
    int Paragraph,
    string? SelectionText);

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
    Task<ReaderStateResult> GetCurrentAsync(CancellationToken ct = default);
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

    public async Task<ReaderStateResult> GetCurrentAsync(CancellationToken ct = default)
    {
        // ONE resolution of the active document. An earlier version read the book and then re-resolved it to
        // fetch the selection, which let a tab switch during the selection round-trip (up to 700 ms) attribute
        // one book's selection to another book's paragraph.
        var (result, document) = await Dispatcher.UIThread.InvokeAsync(ReadActiveBook);
        if (result.State is null || document is null) return result;

        ct.ThrowIfCancellationRequested();

        var selection = await ReadSelectionAsync(document);
        return ReaderStateResult.Ok(result.State with { SelectionText = selection });
    }

    private (ReaderStateResult Result, BookDisplayViewModel? Document) ReadActiveBook()
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

        // An API caller carries no focus signal, so with a docked book AND a floated one both active there is
        // no honest way to say which the user is reading. Refuse rather than pick.
        if (candidates.Count > 1)
        {
            _logger.LogDebug("Reader state is ambiguous: {Count} active book windows", candidates.Count);
            return (ReaderStateResult.Fail(ReaderStateProblem.AmbiguousBookWindow), null);
        }

        var document = candidates[0];

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
    /// Ask the WebView for the selection and convert it to Latin.
    ///
    /// <para><b>The conversion happens here because the display script is only known here.</b> The bundler's
    /// <c>SelectionContext</c> documents its input as Latin and matches the selection against a Latin passage
    /// window — hand it Devanagari and every bundle reports "selection not found", a false diagnostic from a
    /// tool whose whole purpose is faithful diagnostics. (Richer normalization remains #581's job.)</para>
    ///
    /// <para>Run entirely on the UI thread: every other caller of the selection channel is a UI-thread command
    /// handler, it drives CEF, and it writes a single-slot completion source a concurrent Cmd+D would
    /// otherwise clobber.</para>
    /// </summary>
    private static async Task<string?> ReadSelectionAsync(BookDisplayViewModel document)
    {
        var raw = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var control = document.BookDisplayControl;
            return control is null ? null : await control.GetWebViewSelectionAsync();
        });

        if (string.IsNullOrWhiteSpace(raw)) return null;

        var script = document.BookScript;
        return script == Script.Latin ? raw : ScriptConverter.Convert(raw, script, Script.Latin);
    }
}
