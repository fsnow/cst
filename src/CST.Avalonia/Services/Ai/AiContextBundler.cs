using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CST;
using CST.Avalonia.Services.Tools;
using CST.Search;
using CST.Conversion;
using CST.Tools;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services.Ai;

/// <summary>Assembles the context surface B sends a model. See <see cref="AiContextBundle"/>.</summary>
public interface IAiContextBundler
{
    Task<AiContextBundle> BuildAsync(AiContextRequest request, CancellationToken ct = default);
}

/// <summary>
/// Thrown when the context cannot be assembled. Loud on purpose: every alternative — an empty passage, a
/// citation carrying an error string — produces a bundle that looks healthy and is not.
/// </summary>
public sealed class AiContextException : Exception
{
    public AiContextException(string message) : base(message) { }
}

/// <summary>
/// Gathers what the model needs about the passage the user is reading, in-process. (#580)
///
/// <para><b>No dictionary glosses.</b> An earlier version injected them. The reason for dropping them is not
/// that they are hard to gather but that they are <b>context clutter that is unlikely to help the model discern
/// meaning</b> (fsnow, 2026-08-10): the app must choose which words to look up BEFORE anything has read the
/// passage, so the choice is a heuristic guess, and the entries it produces then compete for context with the
/// passage itself — the one thing certain to be relevant. A confident but irrelevant gloss is worse than
/// silence, because it hands the model a plausible authority. Lookups belong to the tool-calling tier, where
/// the model asks for the word it has decided it needs. (AI_SURFACE_B.md §4, §11)</para>
///
/// <para><b>A defect that outlived that code, recorded so it is not rediscovered.</b> DPD distinguishes
/// homographs with numeric suffixes on the lemma (<c>mata 1.1</c>), and those suffixed strings do not appear in
/// the form index — roughly a quarter of its ~89,000 lemmas. So resolving a lemma back to a dictionary entry by
/// HEADWORD STRING fails for every homograph, which is precisely the case such a lookup exists to serve. The
/// correct join is on <c>LemmaId</c>, which both <c>LemmaCandidate</c> and <c>DictionaryEntry</c> carry. This
/// matters for the tool-calling tier and for any future gloss surface — not for the code below, which no longer
/// does dictionary lookups at all.</para>
///
/// <para><b>On the tool layer this consumes.</b> <c>ICorpusTools</c> advertised itself as the facade both the
/// local API and surface B would call, but nothing implemented or consumed it, and one of its four members
/// (<c>INavigationTool</c>) has no implementation at all. Surface C resolves the focused interfaces
/// individually; so does this, and book classification comes from <see cref="Books"/> — the stable fact.</para>
/// </summary>
public sealed class AiContextBundler : IAiContextBundler
{
    /// <summary>
    /// The window when there is NO selection to build around — the reader asked about the passage they have
    /// open rather than something they highlighted, so there is no anchor and no sentence to count from.
    ///
    /// <para>This is the one place a character figure survives #672. It is the old Translate/Ask budget,
    /// carried over deliberately rather than re-guessed: with no selection there is nothing better to derive
    /// it from, and its scope has shrunk from every request to this fallback alone. Where there IS a selection
    /// the window is two sentences either side of it, bounded by the enclosing section — see
    /// <see cref="TeiPassageReader.DefaultContextSentences"/>.</para>
    /// </summary>
    private const int NoSelectionPassageChars = 2400;

    /// <summary>How many distinct words get a lemma resolution — the grammatical presets only.</summary>
    private static readonly Dictionary<AiTask, int> LemmaBudget = new()
    {
        [AiTask.Grammar] = 24,
        [AiTask.WordByWord] = 60,
    };

    /// <summary>Words shorter than this are particles and inflectional debris.</summary>
    private const int MinimumWordLength = 3;

    // \p{L} already covers the Latin-script diacritics romanized Pāli uses (ā ī ū ṅ ñ ṭ ḍ ṇ ḷ ṃ ṁ).
    private static readonly Regex WordSplitter = new(@"[^\p{L}]+", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private readonly IPassageTool _passage;
    private readonly ILemmaSearchService? _lemmas;
    private readonly string _appVersion;
    private readonly ILogger<AiContextBundler> _logger;

    /// <param name="lemmas">Null when the DPD-lemma asset is not installed. Resolved with <c>GetService</c> in
    /// the app, exactly as the local API does. Surface B treats DPD as a prerequisite (AI_SURFACE_B.md §4), but
    /// enforcing that belongs to the orchestrator (#583) — this reports the absence rather than assuming it away.</param>
    public AiContextBundler(
        IPassageTool passage,
        ILemmaSearchService? lemmas,
        string appVersion,
        ILogger<AiContextBundler> logger)
    {
        _passage = passage;
        _lemmas = lemmas;
        _appVersion = appVersion;
        _logger = logger;
    }

    public async Task<AiContextBundle> BuildAsync(AiContextRequest request, CancellationToken ct = default)
    {
        // The same catalog guard the passage tool applies, reused rather than re-implemented so the two cannot
        // drift apart (#301 made that guard the defence against reading arbitrary files).
        if (!PassageTool.IsCatalogBook(request.BookId))
            throw new AiContextException($"Unknown book id '{request.BookId}'.");

        var book = Books.Inst.First(b =>
            string.Equals(b.FileName, request.BookId, StringComparison.OrdinalIgnoreCase));

        var parts = new List<BundlePart>();

        // Latin to the model (AI_INTEGRATION §11); StructuredNotes so the Pāli comes back brace-free and
        // quotable with the print apparatus as separate data — which is what you want a model translating.
        var passage = await _passage.FetchPassageAsync(
            new PassageRequest(
                BookId: request.BookId,
                Reference: request.Reference,
                // A CAP, not a target. With a selection the window is two sentences either side of it,
                // bounded by the section; this only bites on the no-selection fallback. (#672)
                MaxChars: NoSelectionPassageChars,
                OutputScript: Script.Latin,
                IncludeFootnotes: false,
                StructuredNotes: true,
                // The window is built AROUND this, so the selection is inside the context by construction
                // rather than by luck. (#649)
                SelectionText: request.SelectionUnavailable ? null : request.SelectionText),
            ct).ConfigureAwait(false);

        // The passage tool signals every failure the same way — empty text, with the reason left in
        // NormalizedReference ("book not available" when the XML is absent, "reference not found", or an
        // unsupported reference kind). Unchecked, those become a healthy-looking bundle whose CITATION reads
        // "book not available", since that field is what the app renders as authoritative. Fail loudly.
        if (string.IsNullOrWhiteSpace(passage.Text))
        {
            throw new AiContextException(
                $"No passage text for '{request.BookId}' at the requested reference " +
                $"({passage.NormalizedReference}).");
        }

        // TrimmedForBudget, not Included, when the reader's selection was cut. This is what raises the
        // partial-passage badge (AI_SURFACE_B.md §6) — and until #672 nothing ever wrote it, so the badge
        // could not fire at all: the passage part was hardcoded Included on every path. An answer about part
        // of a selection, captioned as being about the whole of it, is the one outcome this bundle exists to
        // prevent. (#672, fable)
        parts.Add(new BundlePart(
            BundlePartNames.Passage,
            passage.SelectionTruncated ? BundlePartState.TrimmedForBudget : BundlePartState.Included,
            passage.SelectionTruncated
                ? $"reading window from {passage.NormalizedReference} \u2014 your selection was longer than "
                  + $"{TeiPassageReader.MaxSelectionChars:N0} characters and was cut to fit"
                : $"reading window from {passage.NormalizedReference}"));

        parts.Add(new BundlePart(
            BundlePartNames.Apparatus,
            passage.NoteCount > 0 ? BundlePartState.Included : BundlePartState.Empty,
            passage.NoteCount > 0 ? $"{passage.NoteCount} print note(s)" : "no apparatus in this window"));

        var selection = BuildSelection(request, parts);
        var lemmas = GatherLemmas(request.Task, selection?.Text ?? passage.Text, parts);

        var bookName = ScriptConverter.Convert(book.LongNavPath, Script.Devanagari, Script.Latin);

        var bundle = new AiContextBundle(
            Task: request.Task,
            OutputLanguage: request.OutputLanguage,
            UserQuestion: request.UserQuestion,
            Passage: passage,
            Selection: selection,
            Lemmas: lemmas,
            Book: new BookContext(book.FileName, bookName, book.Pitaka, book.Matn),
            Citation: new CitationRef(
                book.FileName, bookName, passage.NormalizedReference, passage.Pages),
            Provenance: new Provenance(_appVersion, _lemmas?.Meta?.DpdVersion),
            Budget: new BudgetReport(
                parts,
                ApproximateTokens(passage.Text, lemmas),
                // Measured, not guessed: the reader reports the paragraph in effect where the window ended, so
                // this is the window's real span. The predecessor derived it from NextCursor and was therefore
                // set on almost every request — see BudgetReport. (#602)
                ParagraphsCovered: ParagraphsCovered(passage)));

        _logger.LogDebug(
            "Built {Task} bundle for {BookId}: {Lemmas} lemma(s), ~{Tokens} tokens",
            request.Task, request.BookId, lemmas.Count, bundle.Budget.ApproximateTokens);

        return bundle;
    }

    /// <summary>
    /// Record what the user selected.
    ///
    /// <para><b>There is no longer a "not found in the window" outcome, by construction.</b> The window is
    /// built around the selection, so it contains it; the state, its notice and the caution it put in the
    /// prompt are all gone. A context that could fail to contain the thing it was context for was not a
    /// context. (#649)</para>
    ///
    /// <para><b>Two outcomes remain, and they are not the same.</b> "Nothing selected" and "we could not tell
    /// what was selected" were the same null before #581: the first means the whole passage is legitimately in
    /// view, the second means the words the user highlighted were dropped on the floor. Only the second is
    /// something to tell them about. (§3.1)</para>
    /// </summary>
    private static SelectionContext? BuildSelection(AiContextRequest request, List<BundlePart> parts)
    {
        if (request.SelectionUnavailable)
        {
            parts.Add(new BundlePart(
                BundlePartNames.Selection,
                BundlePartState.Unavailable,
                // No speculative cause. This reaches the model, and the two causes it used to name were
                // guesses that #824 and #827 both found to be wrong in the case at hand; a third (the title
                // cap) was not among them. What the model needs is that the selection is absent. (#827)
                "the reader could not read the selection"));
            return new SelectionContext(null, SelectionState.Unavailable);
        }

        // Already Latin, composed and whitespace-collapsed by SelectionPipeline.Normalize; run it again rather
        // than trust the caller, since it is idempotent.
        var text = SelectionPipeline.Normalize(request.SelectionText, Script.Latin);
        if (text is null) return null;

        parts.Add(new BundlePart(BundlePartNames.Selection, BundlePartState.Included, null));

        return new SelectionContext(text, SelectionState.Located);
    }

    /// <summary>
    /// Stem and grammatical analysis for the words in play — the grammatical presets only, since a paradigm is
    /// not what "explain this passage" needs and every resolution is a real query.
    ///
    /// <para>This is the only lexical material surface B injects, and it is defensible where a gloss set was
    /// not: it is scoped to what the USER selected, so the relevance signal comes from the user rather than
    /// from a heuristic of ours, and it is grammatical analysis rather than a definition to be believed.</para>
    /// </summary>
    private IReadOnlyList<LemmaEntry> GatherLemmas(AiTask task, string source, List<BundlePart> parts)
    {
        if (!LemmaBudget.TryGetValue(task, out var max))
            return Array.Empty<LemmaEntry>();

        if (_lemmas is not { IsAvailable: true })
        {
            parts.Add(new BundlePart(
                BundlePartNames.Lemmas,
                BundlePartState.Unavailable,
                "the DPD-lemma asset is not installed — grammar answers will be weaker"));
            return Array.Empty<LemmaEntry>();
        }

        var words = DistinctWords(source, max, out var truncated);
        var lemmas = new List<LemmaEntry>();

        foreach (var word in words)
        {
            // A homograph resolves to several candidates and DPD says the caller disambiguates; all of them are
            // emitted, because which reading the passage supports is a judgement only the context can settle.
            if (_lemmas.ResolveWord(word, Script.Latin) is not { } resolution) continue;

            foreach (var candidate in resolution.Candidates)
                lemmas.Add(new LemmaEntry(word, candidate.Lemma, candidate.Pos, candidate.Gloss));
        }

        parts.Add(new BundlePart(
            BundlePartNames.Lemmas,
            truncated ? BundlePartState.TrimmedForBudget : BundlePartState.Included,
            $"{lemmas.Count} candidate lemma(s) for {words.Count} word(s)"));

        return lemmas;
    }

    /// <summary>
    /// How many numbered paragraphs the window spans. Null when the book carries no paragraph numbering at that
    /// point, which is not the same as 1 — "unknown" must not be reported as "stayed put".
    /// </summary>
    private static int? ParagraphsCovered(PassageResult passage)
    {
        if (passage.ParagraphNumber is not int first) return null;
        if (passage.EndParagraphNumber is not int last) return 1;

        // Numbering restarts per sub-book in a Multi volume, so a raw subtraction across a sub-book boundary is
        // meaningless — and can go negative. Refuse rather than report a number that looks measured and isn't.
        if (passage.EndParagraphBookCode != passage.ParagraphBookCode) return null;

        return last >= first ? last - first + 1 : null;
    }

    /// <summary>Distinct words worth resolving, longest first so the budget buys the substantial ones.</summary>
    private static IReadOnlyList<string> DistinctWords(string text, int max, out bool truncated)
    {
        var all = WordSplitter.Split(text)
            .Where(w => w.Length >= MinimumWordLength)
            .Select(w => w.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(w => w.Length)
            .ToList();

        truncated = all.Count > max;
        return all.Take(max).ToList();
    }

    /// <summary>
    /// The BUNDLE's own approximate size — the passage and the lemma entries, which is what a bundle contains.
    ///
    /// <para>Deliberately not what the assistant reports as "estimated context": that is the rendered prompt,
    /// which also carries the system prompt, the preset's template and the reader's own question, and it is
    /// estimated where those exist rather than here where they do not. Reporting this figure as the context
    /// sent was one of the two things wrong with the estimate before #672; the ratio was the other.</para>
    /// </summary>
    private static int ApproximateTokens(string passage, IReadOnlyList<LemmaEntry> lemmas) =>
        AiTokens.Estimate(
            new[] { passage }.Concat(
                lemmas.SelectMany(l => new[] { l.Form, l.Lemma, l.Gloss })));
}
