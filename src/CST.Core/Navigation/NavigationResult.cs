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
    public sealed record ResolvedTarget(
        string BookFileName,
        int BookIndex,
        int BookDocId,
        string Anchor,
        string NormalizedReference,
        IReadOnlyList<string> SearchTerms);

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
        public bool IsResolved => Status == NavigationStatus.Resolved;

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
