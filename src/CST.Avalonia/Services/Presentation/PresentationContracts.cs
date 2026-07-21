using System.Collections.Generic;
using CST.Conversion;
// Explicit aliases: CST.Avalonia.Services has its own `Book`, which would otherwise shadow the catalog type.
using Book = CST.Book;
using ReadingPositionToken = CST.Avalonia.Models.ReadingPositionToken;
using TermPosition = CST.Avalonia.Models.TermPosition;

namespace CST.Avalonia.Services.Presentation;

/// <summary>
/// Where a presentation should land in the book. Exactly one of these, or none (just open the book).
/// Precedence when a caller could supply more than one is resolved by <see cref="PresentationPlanner"/>:
/// an explicit hit/anchor (deliberate agent or user intent) outranks a restored reading position — the same
/// rule <c>ExecutePendingRestoration</c> enforces (#36 / #434 fable §6).
/// </summary>
public abstract record PresentationTarget
{
    private PresentationTarget() { }

    /// <summary>Restore an exact reading position (#434 token) — robust to reflow and window size.</summary>
    public sealed record Position(ReadingPositionToken Token) : PresentationTarget;

    /// <summary>Go to a named anchor / reference: page ("V1.0023"), paragraph ("para5"), or chapter id.</summary>
    public sealed record Anchor(string Name) : PresentationTarget;

    /// <summary>Land on a 1-based search hit (requires the search context below to have been supplied).</summary>
    public sealed record Hit(int Index) : PresentationTarget;
}

/// <summary>
/// One "present the reader in context" request — the single internal command behind every surface (the
/// search UI today; the agent present-tool, App Intents and the <c>cstreader://</c> URL next). (#187)
/// </summary>
public sealed record PresentationRequest
{
    public required global::CST.Book Book { get; init; }

    /// <summary>Where to land. Null = just open the book at its natural start.</summary>
    public PresentationTarget? Target { get; init; }

    /// <summary>IPE search terms to highlight.</summary>
    public IReadOnlyList<string>? SearchTerms { get; init; }

    /// <summary>Source-offset hit positions (with IsFirstTerm flags) driving the two-colour highlighting.</summary>
    public IReadOnlyList<TermPosition>? Positions { get; init; }

    public Script? Script { get; init; }
    public int? DocId { get; init; }
    public bool ShowFootnotes { get; init; } = true;
    public bool ShowSearchTerms { get; init; } = true;
}

/// <summary>Outcome of a presentation, so a non-UI caller (the agent tool) can report a clean failure.</summary>
public sealed record PresentationResult(bool Presented, string? Error = null)
{
    public static PresentationResult Ok() => new(true);
    public static PresentationResult Fail(string error) => new(false, error);
}

/// <summary>
/// The resolved dock-call shape for a request: which open path to use and which landing parameters to pass.
/// Kept separate from the service so the mapping + precedence are pure and unit-testable.
/// </summary>
public sealed record PresentationPlan(
    bool UseSearchTab,
    string? Anchor,
    int? HitIndex,
    ReadingPositionToken? PositionToken);

/// <summary>
/// Pure mapping from a <see cref="PresentationRequest"/> to the parameters the dock open paths take.
/// No UI, no DI — directly unit-testable.
/// </summary>
public static class PresentationPlanner
{
    /// <summary>
    /// Pure precondition check. Returns null when the request is presentable, else the reason — so a caller
    /// can't be told "presented" for a request that could only ever be a silent no-op. (fable §4)
    /// </summary>
    public static string? Validate(PresentationRequest request)
    {
        if (request?.Book == null) return "No book specified.";
        if (request.Target is PresentationTarget.Hit && !HasHighlights(request))
            return "A Hit target requires searchTerms and positions — there are no highlights to land on.";
        return null;
    }

    private static bool HasHighlights(PresentationRequest r) =>
        r.SearchTerms is { Count: > 0 } && r.Positions is { Count: > 0 };

    public static PresentationPlan Plan(PresentationRequest request)
    {
        string? anchor = null;
        int? hit = null;
        ReadingPositionToken? token = null;

        switch (request.Target)
        {
            case PresentationTarget.Hit h:
                // Explicit hit wins outright (deliberate intent).
                hit = h.Index >= 1 ? h.Index : 1;
                break;
            case PresentationTarget.Anchor a when !string.IsNullOrWhiteSpace(a.Name):
                anchor = a.Name;
                break;
            case PresentationTarget.Position p:
                token = p.Token;
                break;
        }

        // The search-tab path exists to allow MULTIPLE independent instances of the same book (one per result)
        // and owns the highlight plumbing. Use it when we have real highlight context AND no explicit landing
        // target; an explicit target needs the general path, which can carry an anchor/hit/token.
        //
        // It also CANNOT carry script / docId / the per-book view toggles (it hardcodes the current script and
        // the ViewModel defaults), so any request that sets those must take the general path — otherwise those
        // fields would be silently dropped while the caller was told it succeeded. (fable §2)
        bool hasHighlights = HasHighlights(request);
        bool needsGeneralOnlyOptions = request.Script != null || request.DocId != null
                                       || !request.ShowFootnotes || !request.ShowSearchTerms;
        bool useSearchTab = hasHighlights && anchor == null && hit == null && token == null && !needsGeneralOnlyOptions;

        return new PresentationPlan(useSearchTab, anchor, hit, token);
    }
}
