using CST.Avalonia.Models;
using CST.Conversion;

namespace CST.Avalonia.Services.LocalApi;

/// <summary>
/// Request for <c>POST /v1/navigate</c> — the agent-facing "show me in context" verb (#187 surface E). Unlike
/// every other endpoint this one has a SIDE EFFECT on the user's window, so it is gated behind the
/// remote-control consent toggle (Settings → AI → allow agents to drive the reader).
/// </summary>
/// <param name="BookId">Corpus file name, e.g. "s0101m.mul.xml" (from /v1/books or a search result).</param>
/// <param name="Anchor">Optional reference to land on: a page anchor ("V1.0023"), paragraph ("para5"), or chapter id.</param>
/// <param name="Terms">Optional query to highlight — same syntax as /v1/search (phrases, multiple words).</param>
/// <param name="Hit">Optional 1-based hit to land on; requires <paramref name="Terms"/>.</param>
/// <param name="Mode">Search mode for <paramref name="Terms"/>: Exact (default), Wildcard, Regex.</param>
/// <param name="ProximityDistance">Word distance for multi-word proximity matching.</param>
/// <param name="OutputScript">
/// Script to display the book in; null keeps whatever the reader is currently showing.
/// <para>
/// <see cref="Mode"/> and this are TYPED, not strings: the registered converters reject an unknown name (and the
/// internal Ipe encoding) with the surface's standard 400. A string would have fallen back to Exact/Latin —
/// harmless on a read-only endpoint, but this one MUTATES the user's display, so a typo like "Devanagri" would
/// visibly flip their window to Latin while the agent was told it succeeded. (fable MED-3 / LOW-6)
/// </para>
/// </param>
public sealed record NavigateRequest(
    string? BookId,
    string? Anchor = null,
    string? Terms = null,
    int? Hit = null,
    SearchMode? Mode = null,
    int? ProximityDistance = null,
    Script? OutputScript = null);

/// <summary>
/// Result of a navigate. <paramref name="Presented"/> is false — with a reason — whenever the reader was not
/// actually driven (no reader window, an unpresentable request, or a duplicate open the dock suppressed), so an
/// agent never has to infer success.
/// </summary>
/// <param name="Highlights">
/// How many hit positions were APPLIED. Zero whenever <paramref name="Presented"/> is false, so it can never
/// read as partial success for something that did not happen. (fable MED-4)
/// </param>
/// <param name="Note">A non-fatal explanation, e.g. a query with no occurrences in the book.</param>
public sealed record NavigateResponse(
    bool Presented,
    string? Error,
    string BookId,
    int Highlights,
    string? Note);
