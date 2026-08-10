using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CST.Avalonia.Services;
using CST.Avalonia.ViewModels;
using Dock.Model.Core;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services.Ai;

/// <summary>Why the reader could not say what the user is looking at.</summary>
public enum ReaderStateProblem
{
    /// <summary>No reader window, or no book document in it.</summary>
    NoBookOpen,

    /// <summary>
    /// A book is open but its position is unknown. The reading position is derived from scroll, and reports
    /// <c>"*"</c> until the page settles — so this is a real, transient state, not a defect.
    /// </summary>
    PositionUnknown,
}

/// <summary>
/// What the user is looking at: the book, where in it, and what (if anything) they have selected. Or a reason
/// none of that could be established.
/// </summary>
/// <param name="BookId">The open book's file name — what the bundler and the passage tool key on.</param>
/// <param name="Paragraph">The paragraph the viewport is on.</param>
/// <param name="SelectionText">The user's selection, if any, <b>in the display script</b> — converting it is
/// the selection pipeline's job (#581), not this one's.</param>
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

/// <summary>
/// Reads what the reader is currently showing.
///
/// <para>The read-side counterpart to <see cref="Presentation.IPresentationService"/>, which can drive the
/// reader but cannot report on it. Surface B needs the opposite direction: the app must be able to say what the
/// user is looking at before it can ask a model about it. (#593)</para>
/// </summary>
public interface IReaderStateService
{
    /// <summary>
    /// Safe to call from any thread — marshals to the UI thread itself, as the presentation service does.
    /// Returns a failure RESULT rather than throwing for every expected condition.
    /// </summary>
    Task<ReaderStateResult> GetCurrentAsync(CancellationToken ct = default);
}

/// <summary>
/// Reads the active book document out of the main window's dock.
///
/// <para><b>Both refusals are refusals, never fallbacks.</b> In particular, an unknown position must not fall
/// back to the start of the book: <c>AiContextRequest.Reference</c> is nullable and a null reference reads from
/// the book start, so falling through would produce a confident, app-cited answer about a passage the user is
/// not looking at — with no signal that anything went wrong. That is the scope-mismatch hazard the grounding
/// fences exist to prevent (AI_SURFACE_B.md §6), arriving silently.</para>
/// </summary>
public sealed class ReaderStateService : IReaderStateService
{
    private readonly ILogger<ReaderStateService> _logger;

    public ReaderStateService(ILogger<ReaderStateService> logger) => _logger = logger;

    public async Task<ReaderStateResult> GetCurrentAsync(CancellationToken ct = default)
    {
        var result = await Dispatcher.UIThread.InvokeAsync(ReadActiveBook);
        if (result.State is null) return result;

        // The selection round-trips through the WebView and can legitimately come back null (nothing selected,
        // or the 700 ms channel timeout). It is optional context, so a miss is not a refusal.
        var selection = await ReadSelectionAsync().ConfigureAwait(false);

        return ReaderStateResult.Ok(result.State with { SelectionText = selection });
    }

    private ReaderStateResult ReadActiveBook()
    {
        // The dock factory is not in DI — it belongs to the main window's layout, the same lookup the
        // presentation service and the search panel use.
        if ((App.MainWindow?.DataContext as LayoutViewModel)?.Factory is not CstDockFactory factory)
        {
            _logger.LogDebug("Reader state requested but no reader layout is available");
            return ReaderStateResult.Fail(ReaderStateProblem.NoBookOpen);
        }

        if (factory.ActiveBookDocument is not { } book)
            return ReaderStateResult.Fail(ReaderStateProblem.NoBookOpen);

        // "*" is the view model's own "not known yet" — the position is derived from scroll and reports it
        // until the page settles.
        if (!int.TryParse(book.CurrentParagraph, out var paragraph) || paragraph <= 0)
        {
            _logger.LogDebug(
                "Reader state: {Book} is open but its paragraph is '{Paragraph}'",
                book.Book.FileName, book.CurrentParagraph);
            return ReaderStateResult.Fail(ReaderStateProblem.PositionUnknown);
        }

        return ReaderStateResult.Ok(new ReaderState(book.Book.FileName, paragraph, SelectionText: null));
    }

    private static async Task<string?> ReadSelectionAsync()
    {
        // The view model holds its own view; the selection has to be asked of the WebView.
        var control = await Dispatcher.UIThread
            .InvokeAsync(() => ((App.MainWindow?.DataContext as LayoutViewModel)?.Factory as CstDockFactory)
                ?.ActiveBookDocument?.BookDisplayControl);

        if (control is null) return null;

        var selection = await control.GetWebViewSelectionAsync().ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(selection) ? null : selection;
    }
}
