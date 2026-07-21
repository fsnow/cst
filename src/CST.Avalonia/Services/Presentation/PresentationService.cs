using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CST.Avalonia.ViewModels;
using Serilog;
// Explicit alias: CST.Avalonia.Services has its own `Book`, which would otherwise shadow the catalog type.
using TermPosition = CST.Avalonia.Models.TermPosition;

namespace CST.Avalonia.Services.Presentation;

/// <summary>
/// The single internal "present the reader in context" command (#187): open the book, apply the search
/// highlights, and land on the requested target. Every surface is a thin adapter over this — the search UI
/// today, and next the agent present-tool (<c>POST /navigate</c>), App Intents, and the <c>cstreader://</c>
/// URL. Keeping one command means those surfaces can't drift apart in behaviour.
/// </summary>
public interface IPresentationService
{
    /// <summary>
    /// Present the reader. Safe to call from any thread — marshals to the Avalonia UI thread itself.
    /// Returns a failure RESULT (rather than throwing) for every expected condition: no reader to drive, an
    /// unpresentable request, or a duplicate open suppressed by the dock. Only cancellation throws, per the
    /// usual CancellationToken contract.
    /// </summary>
    Task<PresentationResult> PresentAsync(PresentationRequest request, CancellationToken ct = default);
}

public sealed class PresentationService : IPresentationService
{
    private readonly ILogger _logger = Log.ForContext<PresentationService>();

    public async Task<PresentationResult> PresentAsync(PresentationRequest request, CancellationToken ct = default)
    {
        var invalid = PresentationPlanner.Validate(request);
        if (invalid != null) return PresentationResult.Fail(invalid);
        ct.ThrowIfCancellationRequested();

        // Presentation mutates the dock/visual tree, so it must run on the UI thread regardless of who called
        // (the agent tool will call from a Kestrel request thread).
        return await Dispatcher.UIThread.InvokeAsync(() => Present(request));
    }

    private PresentationResult Present(PresentationRequest request)
    {
        try
        {
            // The dock factory isn't in DI — it belongs to the main window's layout, same lookup the search
            // panel uses. Absent = there is no reader to drive (not running / still starting).
            if ((App.MainWindow?.DataContext as LayoutViewModel)?.Factory is not CstDockFactory factory)
            {
                _logger.Warning("Presentation requested but no reader layout is available");
                return PresentationResult.Fail("CST Reader is not presentable (no reader window is available).");
            }

            var plan = PresentationPlanner.Plan(request);
            var terms = request.SearchTerms?.ToList() ?? new List<string>();
            var positions = request.Positions?.ToList() ?? new List<TermPosition>();

            bool opened;
            if (plan.UseSearchTab)
            {
                // Search-result semantics: a fresh tab per result, highlight plumbing, lands on the first hit.
                _logger.Information("Presenting {Book} in a search tab ({Terms} terms, {Positions} positions)",
                    request.Book.FileName, terms.Count, positions.Count);
                opened = factory.OpenBookInNewTab(request.Book, terms, positions);
            }
            else
            {
                _logger.Information("Presenting {Book} (anchor={Anchor}, hit={Hit}, token={HasToken})",
                    request.Book.FileName, plan.Anchor ?? "none", plan.HitIndex, plan.PositionToken != null);
                opened = factory.OpenBook(
                    request.Book,
                    plan.Anchor,
                    request.Script,
                    null,                       // windowId: null = fresh instance
                    terms.Count > 0 ? terms : null,
                    request.DocId ?? request.Book.DocId,
                    positions.Count > 0 ? positions : null,
                    plan.HitIndex,
                    request.ShowFootnotes,
                    request.ShowSearchTerms,
                    plan.PositionToken);
            }

            // The dock paths suppress a repeat open of the same book inside a short window and return silently.
            // Report that honestly instead of claiming success a caller can't verify. (fable §1)
            if (!opened)
            {
                _logger.Debug("Presentation suppressed as a duplicate open: {Book}", request.Book.FileName);
                return PresentationResult.Fail(
                    "Duplicate open suppressed — this book was just opened; retry in a moment.");
            }

            return PresentationResult.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Presentation failed for {Book}", request.Book?.FileName ?? "null");
            return PresentationResult.Fail($"Presentation failed: {ex.Message}");
        }
    }
}
