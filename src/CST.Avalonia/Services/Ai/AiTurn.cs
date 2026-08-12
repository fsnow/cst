using System.Collections.Generic;
using CST.Navigation;

namespace CST.Avalonia.Services.Ai;

/// <summary>
/// What the user asked for. The orchestrator's whole input — everything else it looks up.
/// </summary>
/// <param name="Reference">Where in the book. Null reads from the START of the book, so a caller that does not
/// know the reading position must refuse rather than pass null (see <see cref="ReaderStateService"/>).</param>
public sealed record AiTurnRequest(
    AiTask Task,
    string BookId,
    NavigationReference? Reference = null,
    string? SelectionText = null,
    string? UserQuestion = null);

/// <summary>What kind of thing a <see cref="AiTurnEvent"/> carries.</summary>
public enum AiTurnEventKind
{
    /// <summary>
    /// The context is assembled and the request is away. Carries the citation the app renders beside the
    /// answer and any degradation notices. <b>Always the first event on a successful turn</b>, and it arrives
    /// before any text — the panel can draw its chrome while the model is still thinking.
    /// </summary>
    Started,

    /// <summary>Answer text, marker-stripped and ready to render.</summary>
    Text,

    /// <summary>Model reasoning, segregated. A caller may show it deliberately or drop it; never concatenate it
    /// into the answer.</summary>
    Reasoning,

    /// <summary>Token accounting, merged across the stream. Emitted once, before the terminal event.</summary>
    Usage,

    /// <summary>
    /// The turn failed. Terminal. Any <see cref="Text"/> already emitted stands — a mid-stream failure keeps
    /// the partial answer rather than discarding it.
    /// </summary>
    Error,

    /// <summary>The turn finished normally. Terminal.</summary>
    Completed,
}

/// <summary>What the app renders beside the answer, and what it must tell the user about how it was built.</summary>
/// <param name="Citation">Rendered by the app from bundle data, never parsed out of model output — which is
/// what makes it impossible for a garbled answer to produce a false citation on screen.</param>
/// <param name="Notices">Degradations worth showing: a missing asset, a trimmed part, a selection the window
/// does not contain, a prompt edit that was rejected. Empty on a clean turn.</param>
public sealed record AiTurnContext(
    AiTask Task,
    string OutputLanguage,
    CitationRef Citation,
    BookContext Book,
    IReadOnlyList<string> Notices);

/// <summary>
/// How the model quoted Pāli, counted rather than merely repaired. (#587)
/// </summary>
/// <param name="Quotes">Properly opened and closed quotes.</param>
/// <param name="UnbalancedMarkers">Markers with no partner. Stripped from the answer regardless — see
/// <see cref="PaliQuoteFilter"/> — but recorded, because marker discipline is what decides whether script
/// conversion can be enabled for a given model (AI_SURFACE_B.md §9).</param>
public sealed record PaliMarkerReport(int Quotes, int UnbalancedMarkers);

/// <summary>Tokens as the provider reported them. Null where a provider does not report that half.</summary>
/// <remarks>
/// There is no cost field. Neither wire format reports a price, so a figure here would have to come from a
/// rate table — which is #584's registry, not this layer's to invent. §10's "show what the call cost" is
/// satisfied by tokens until that lands.
/// </remarks>
public sealed record AiUsageReport(int? InputTokens, int? OutputTokens);

/// <summary>One event in a turn. See <see cref="AiTurnEventKind"/> for which field each kind populates.</summary>
public sealed record AiTurnEvent(
    AiTurnEventKind Kind,
    string? Text = null,
    AiTurnContext? Context = null,
    AiUsageReport? Usage = null,
    AiError? Error = null,
    PaliMarkerReport? Markers = null)
{
    public static AiTurnEvent ForStarted(AiTurnContext context) => new(AiTurnEventKind.Started, Context: context);
    public static AiTurnEvent ForText(string text) => new(AiTurnEventKind.Text, Text: text);
    public static AiTurnEvent ForReasoning(string text) => new(AiTurnEventKind.Reasoning, Text: text);
    public static AiTurnEvent ForUsage(AiUsageReport usage) => new(AiTurnEventKind.Usage, Usage: usage);
    public static AiTurnEvent ForError(AiError error) => new(AiTurnEventKind.Error, Error: error);

    public static AiTurnEvent ForCompleted(PaliMarkerReport markers) =>
        new(AiTurnEventKind.Completed, Markers: markers);
}
