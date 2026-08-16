using System.Collections.Generic;
using System.Linq;
using CST;
using CST.Search;
using CST.Tools;

namespace CST.Avalonia.Services.Ai;

/// <summary>Which preset the bundle was assembled for. Determines what gets gathered and how it is budgeted.</summary>
public enum AiTask
{
    Explain,
    Translate,
    Grammar,
    WordByWord,

    /// <summary>
    /// The reader's own question, with the passage as its context. The only task that requires a question —
    /// the other four are complete without one, and this one IS one.
    /// </summary>
    Ask,
}

/// <summary>
/// What the caller is looking at, and what it wants said about it. The bundler's entire input — everything
/// else is looked up.
/// </summary>
/// <param name="BookId">The open book's file name, as <c>Book.FileName</c> spells it.</param>
/// <param name="Reference">Where in the book. Null reads from the start.</param>
/// <param name="SelectionText">The user's selection, ALREADY put through <c>SelectionPipeline.Normalize</c> —
/// Latin script, composed, whitespace-collapsed. This type does not convert: a raw display-script selection
/// passed here would silently fail every dictionary and lemma lookup, which is the failure #581 exists to
/// prevent. Null means nothing was selected.</param>
/// <param name="SelectionUnavailable">The reader could not determine the selection at all. Distinct from a null
/// <paramref name="SelectionText"/>, which means the user genuinely selected nothing — see
/// <see cref="SelectionState.Unavailable"/>.</param>
/// <param name="OutputLanguage">Language the ANSWER should be in — a separate axis from the script of quoted
/// Pāli. Not optional: translate into what?</param>
/// <param name="UserQuestion">Free-form question, where the preset allows one.</param>
public sealed record AiContextRequest(
    AiTask Task,
    string BookId,
    string OutputLanguage,
    CST.Navigation.NavigationReference? Reference = null,
    string? SelectionText = null,
    string? UserQuestion = null,
    bool SelectionUnavailable = false);

/// <summary>What became of the user's selection.</summary>
public enum SelectionState
{
    /// <summary>The reader selected text and it reached the model. The window is built around it, so there
    /// is no separate "and it was found" state to be in — see AiContextBundler.</summary>
    Located,

    /// <summary>
    /// The reader could not say what was selected — the WebView was not ready, or the round trip through the
    /// <c>document.title</c> channel timed out. <b>Deliberately distinct from "nothing was selected"</b>: those
    /// two were the same null before #581, and the difference is the difference between an answer about the
    /// whole passage (correct) and an answer that quietly ignored the words the user highlighted (which the
    /// user experiences as "the AI ignored my selection").
    /// </summary>
    Unavailable,
}

/// <summary>The user's selection, once the selection pipeline has been through it.</summary>
/// <param name="Text">Latin-script, composed, whitespace-collapsed. Null when <see cref="State"/> is
/// <see cref="SelectionState.Unavailable"/> — there is no text to carry.</param>
public sealed record SelectionContext(string? Text, SelectionState State);

/// <summary>A word's stem and grammatical analysis, from the optional DPD-lemma asset.</summary>
public sealed record LemmaEntry(
    string Form,
    string Lemma,
    string? PartOfSpeech,
    string? Gloss);

/// <summary>
/// Where the passage sits in the canon. Cheap to supply and it prevents category errors — a model told it is
/// reading a ṭīkā will not gloss it as the word of the Buddha.
/// </summary>
public sealed record BookContext(
    string BookId,
    string Name,
    Pitaka Pitaka,
    CommentaryLevel CommentaryLevel);

/// <summary>
/// Everything needed to cite the passage. <b>Rendered by the app, never parsed back out of model output</b> —
/// which is what makes it impossible for a garbled answer to produce a false citation on screen.
/// </summary>
public sealed record CitationRef(
    string BookId,
    string BookName,
    string NormalizedReference,
    IReadOnlyList<SnippetPageRef> Pages);

/// <summary>
/// What produced this bundle. Stamped so a cross-release evaluation regression (#587) is attributable rather
/// than mysterious — the corpus is corrected over time and the lemma data is a separately versioned asset.
/// </summary>
/// <param name="LemmaAssetVersion">Null when no DPD-lemma asset is installed.</param>
public sealed record Provenance(
    string AppVersion,
    string? LemmaAssetVersion);

/// <summary>Why a part of the bundle is or is not present.</summary>
public enum BundlePartState
{
    /// <summary>Gathered in full.</summary>
    Included,

    /// <summary>Present but cut to fit the budget.</summary>
    TrimmedForBudget,

    /// <summary>
    /// Could not be gathered at all — most often an optional asset that is not installed. <b>Deliberately
    /// distinct from <see cref="TrimmedForBudget"/></b>: conflating them makes a missing DPD-lemma download
    /// look like a budget problem, and sends whoever is debugging it to the wrong place entirely.
    /// </summary>
    Unavailable,

    /// <summary>
    /// Gathered successfully; there was simply nothing to gather. <b>Also distinct from
    /// <see cref="Unavailable"/></b>, for the same reason: a window with no print apparatus is healthy — most
    /// windows outside mūla texts have none — and reporting that as "unavailable" would have consumers nagging
    /// about a missing asset on perfectly good bundles.
    /// </summary>
    Empty,
}

/// <summary>One gathered part and how it fared.</summary>
public sealed record BundlePart(string Name, BundlePartState State, string? Detail = null);

/// <summary>
/// What was included, what was cut, and how far the window actually reached.
///
/// <para><b>There is deliberately no "the passage was truncated" flag.</b> An earlier version derived one from
/// <c>PassageResult.NextCursor</c>, which was wrong: that cursor is non-null whenever the window ends before the
/// end of the BOOK FILE, not before the end of the requested paragraph. On the real corpus it is set for almost
/// every request, so the badge it drove would have fired always — and a fidelity signal that always fires is one
/// users learn to ignore.</para>
///
/// <para><see cref="ParagraphsCovered"/> replaced it, and unlike its predecessor it is <b>measured</b>: the
/// passage reader now reports the paragraph in effect where the window ended, so this says how many paragraphs
/// the returned text actually spans rather than guessing. That is what makes §6's partial-passage badge
/// implementable at last, and what caught #602 — a Translate window budgeted at 2,400 characters covering
/// roughly thirty Dhammapada verses beside a citation naming one.</para>
/// </summary>
/// <param name="ParagraphsCovered">How many numbered paragraphs the window spans; 1 when it stayed within the
/// one asked for. Null when the book has no paragraph numbering at that point.</param>
public sealed record BudgetReport(
    IReadOnlyList<BundlePart> Parts,
    int ApproximateTokens,
    int? ParagraphsCovered)
{
    /// <summary>True when the window ran past the paragraph the citation names.</summary>
    public bool WindowExtendsPastReference => ParagraphsCovered > 1;
}

/// <summary>Stable part names, so consumers match on a constant rather than a string literal.</summary>
public static class BundlePartNames
{
    public const string Passage = "passage";
    public const string Selection = "selection";
    public const string Lemmas = "lemmas";
    public const string Apparatus = "apparatus";
}

/// <summary>
/// Everything surface B sends a model about what the user is looking at, as DATA rather than a prompt string.
///
/// <para>Keeping it data is what makes the feature testable: a bundle can be asserted against the real corpus
/// with no API key, no network and no spend, and it can be dumped and read by a human deciding whether the
/// model was given a fair chance. A prompt string can only be eyeballed. (#580, AI_SURFACE_B.md §3)</para>
///
/// <para><b>Everything here is best-effort, and until tool calling arrives it cannot be otherwise.</b> The app
/// assembles this BEFORE the model has seen the passage, so its choice of which words to gloss is a heuristic
/// over the text — length, distinctness, a cap — not a response to what the model actually needs. Under tool
/// calling the model reads first and then asks for the lookups it wants; under injection we are guessing on its
/// behalf. Two things follow, and <b>#582's system prompt must say both</b>: no entry here is authoritative, and
/// <b>absence is not evidence</b> — a word with no gloss was missed by the heuristic, not found to be undefined
/// or unimportant. A model told otherwise will reasonably read the injected set as the relevant set.</para>
/// </summary>
public sealed record AiContextBundle(
    AiTask Task,
    string OutputLanguage,
    string? UserQuestion,
    PassageResult Passage,
    SelectionContext? Selection,
    IReadOnlyList<LemmaEntry> Lemmas,
    BookContext Book,
    CitationRef Citation,
    Provenance Provenance,
    BudgetReport Budget);
