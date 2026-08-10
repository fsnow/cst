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
}

/// <summary>
/// What the caller is looking at, and what it wants said about it. The bundler's entire input — everything
/// else is looked up.
/// </summary>
/// <param name="BookId">The open book's file name, as <c>Book.FileName</c> spells it.</param>
/// <param name="Reference">Where in the book. Null reads from the start.</param>
/// <param name="SelectionText">The user's selection, ALREADY normalized to Latin script by the selection
/// pipeline (#581). This type does not convert: a raw display-script selection passed here would silently fail
/// every dictionary and lemma lookup, which is the failure #581 exists to prevent.</param>
/// <param name="OutputLanguage">Language the ANSWER should be in — a separate axis from the script of quoted
/// Pāli. Not optional: translate into what?</param>
/// <param name="UserQuestion">Free-form question, where the preset allows one.</param>
public sealed record AiContextRequest(
    AiTask Task,
    string BookId,
    CST.Navigation.NavigationReference? Reference = null,
    string? SelectionText = null,
    string OutputLanguage = "English",
    string? UserQuestion = null);

/// <summary>The user's selection, once the selection pipeline has normalized it.</summary>
/// <param name="Text">Latin-script, whitespace-normalized.</param>
/// <param name="FoundInWindow">Whether the selection was located within the fetched passage window. False
/// means the model is being shown a selection the surrounding passage may not contain — worth knowing, and
/// recorded in the budget report rather than silently ignored.</param>
public sealed record SelectionContext(string Text, bool FoundInWindow);

/// <summary>
/// A dictionary gloss, projected to PLAIN TEXT.
///
/// <para><c>DictionaryEntry.MeaningHtml</c> is an HTML fragment. Injecting markup wastes tokens on tags the
/// model must ignore and invites it to echo them back into the answer, so the projection happens here rather
/// than being left to the prompt template.</para>
/// </summary>
/// <param name="Attribution">The dictionary's own recorded citation, so a gloss the model repeats can be
/// attributed honestly. Null when the dictionary records none — never inferred.</param>
public sealed record GlossEntry(
    string Headword,
    string Meaning,
    string? Attribution,
    long? LemmaId);

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
/// than mysterious — the corpus is corrected over time and dictionaries are separately versioned assets.
/// </summary>
public sealed record Provenance(
    string AppVersion,
    IReadOnlyList<string> DictionarySources);

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
}

/// <summary>One gathered part and how it fared.</summary>
public sealed record BundlePart(string Name, BundlePartState State, string? Detail = null);

/// <summary>
/// What was included, what was cut, and what was missing. Drives the "partial passage" badge (#582): a
/// translation presented as being of a passage that was silently truncated is a fidelity hazard specific to
/// this corpus, so trimming must be visible rather than merely logged.
/// </summary>
public sealed record BudgetReport(IReadOnlyList<BundlePart> Parts, int ApproximateTokens)
{
    public bool PassageWasTrimmed =>
        Parts.Any(p => p.Name == BundlePartNames.Passage && p.State == BundlePartState.TrimmedForBudget);
}

/// <summary>Stable part names, so consumers match on a constant rather than a string literal.</summary>
public static class BundlePartNames
{
    public const string Passage = "passage";
    public const string Selection = "selection";
    public const string Glosses = "glosses";
    public const string Lemmas = "lemmas";
    public const string Apparatus = "apparatus";
}

/// <summary>
/// Everything surface B sends a model about what the user is looking at, as DATA rather than a prompt string.
///
/// <para>Keeping it data is what makes the feature testable: a bundle can be asserted against the real corpus
/// with no API key, no network and no spend, and it can be dumped and read by a human deciding whether the
/// model was given a fair chance. A prompt string can only be eyeballed. (#580, AI_SURFACE_B.md §3)</para>
/// </summary>
public sealed record AiContextBundle(
    AiTask Task,
    string OutputLanguage,
    string? UserQuestion,
    PassageResult Passage,
    SelectionContext? Selection,
    IReadOnlyList<GlossEntry> Glosses,
    IReadOnlyList<LemmaEntry> Lemmas,
    BookContext Book,
    CitationRef Citation,
    Provenance Provenance,
    BudgetReport Budget);
