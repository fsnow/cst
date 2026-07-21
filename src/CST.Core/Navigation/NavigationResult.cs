using System.Collections.Generic;

namespace CST.Navigation
{
    /// <summary>Outcome of resolving a <see cref="NavigationRequest"/>.</summary>
    public enum NavigationStatus
    {
        /// <summary>Resolved to a concrete, in-range target (<see cref="NavigationResult.Target"/>).</summary>
        Resolved,
        /// <summary>No book matched <see cref="NavigationRequest.BookId"/>.</summary>
        UnknownBook,
        /// <summary>The reference was malformed for its kind (e.g. non-positive paragraph, empty chapter id).</summary>
        InvalidReference,
        /// <summary>The reference maps to more than one target; see <see cref="NavigationResult.Candidates"/>.</summary>
        AmbiguousReference,
        /// <summary>The reference is well-formed but no such anchor exists in the book (catalog-validated).</summary>
        ReferenceOutOfRange,
        /// <summary>The corpus/index isn't ready (e.g. still (re)indexing); retry later.</summary>
        NotReady
    }

    /// <summary>A resolved, normalized navigation target ready to hand to the reader's anchor navigation.</summary>
    /// <param name="BookFileName">The resolved book's file name.</param>
    /// <param name="BookIndex">The resolved book's catalog index.</param>
    /// <param name="BookDocId">The resolved book's Lucene doc id (-1 if not yet indexed).</param>
    /// <param name="Anchor">The normalized anchor string (empty = start of book).</param>
    /// <param name="NormalizedReference">A short human-readable description (e.g. "paragraph 123 (an5)").</param>
    /// <param name="SearchTerms">Search terms to highlight, carried through from the request.</param>
    /// <param name="Validated">
    /// True only when an <see cref="IAnchorCatalog"/> confirmed the anchor EXISTS in the book. False means the
    /// reference was merely well-formed and normalized — the anchor may be dead, and navigating to it will
    /// silently not scroll. Callers that report success to a user or an agent must surface this rather than
    /// treating <see cref="NavigationStatus.Resolved"/> alone as "found it". (#314)
    /// </param>
    public sealed record ResolvedTarget(
        string BookFileName,
        int BookIndex,
        int BookDocId,
        string Anchor,
        string NormalizedReference,
        IReadOnlyList<string> SearchTerms,
        bool Validated);

    /// <summary>One option offered when a reference is ambiguous.</summary>
    public sealed record NavigationCandidate(string Anchor, string Description);

    /// <summary>
    /// The structured outcome of navigation resolution. Exactly one of <see cref="Target"/> /
    /// <see cref="Candidates"/> is populated depending on <see cref="Status"/>; <see cref="Message"/> is a
    /// human- and machine-readable explanation. Callers (HTTP API, App Intents bridge, in-app) inspect
    /// <see cref="Status"/> and never guess.
    /// </summary>
    public sealed record NavigationResult(
        NavigationStatus Status,
        ResolvedTarget? Target = null,
        IReadOnlyList<NavigationCandidate>? Candidates = null,
        string? Message = null)
    {
        // Enforce the documented invariant at construction so a caller that follows the contract — reading
        // Target! after seeing Resolved — cannot hit a NullReferenceException instead of a clear error. Both
        // halves of "exactly one" are checked, so the doc comment above is not an overstatement. (#314, fable)
        // NOTE: `with` copies fields directly and bypasses this, so don't reshape a result that way.
        public NavigationStatus Status { get; } = Check(Status, Target, Candidates);

        private static NavigationStatus Check(
            NavigationStatus status, ResolvedTarget? target, IReadOnlyList<NavigationCandidate>? candidates)
        {
            bool wantsTarget = status == NavigationStatus.Resolved;
            bool wantsCandidates = status == NavigationStatus.AmbiguousReference;

            if (wantsTarget && target is null)
                throw new System.ArgumentException("A Resolved result must carry a Target.", nameof(status));
            if (!wantsTarget && target is not null)
                throw new System.ArgumentException(
                    $"Only a Resolved result may carry a Target (got {status}).", nameof(status));
            if (wantsCandidates && (candidates is null || candidates.Count == 0))
                throw new System.ArgumentException(
                    "An AmbiguousReference result must carry Candidates.", nameof(status));
            if (!wantsCandidates && candidates is { Count: > 0 })
                throw new System.ArgumentException(
                    $"Only an AmbiguousReference result may carry Candidates (got {status}).", nameof(status));
            return status;
        }

        public bool IsResolved => Status == NavigationStatus.Resolved;

        /// <summary>Resolved AND confirmed to exist by an anchor catalog. <see cref="IsResolved"/> alone does
        /// not imply the anchor is real — see <see cref="ResolvedTarget.Validated"/>. (#314)</summary>
        public bool IsValidated => Status == NavigationStatus.Resolved && Target?.Validated == true;

        public static NavigationResult Resolved(ResolvedTarget target) =>
            new(NavigationStatus.Resolved, Target: target);

        public static NavigationResult UnknownBook(string bookId) =>
            new(NavigationStatus.UnknownBook, Message: $"No book with identifier '{bookId}'.");

        public static NavigationResult InvalidReference(string message) =>
            new(NavigationStatus.InvalidReference, Message: message);

        public static NavigationResult Ambiguous(IReadOnlyList<NavigationCandidate> candidates, string message) =>
            new(NavigationStatus.AmbiguousReference, Candidates: candidates, Message: message);

        public static NavigationResult OutOfRange(string message) =>
            new(NavigationStatus.ReferenceOutOfRange, Message: message);

        public static NavigationResult NotReady(string message) =>
            new(NavigationStatus.NotReady, Message: message);
    }
}
