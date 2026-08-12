using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CST;
using CST.Avalonia.Services.Tools;
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
    /// <summary>Rendered characters of passage per task. Translation earns a wider window than a quick gloss.</summary>
    private static readonly Dictionary<AiTask, int> PassageBudget = new()
    {
        [AiTask.Explain] = 1600,
        [AiTask.Translate] = 2400,
        [AiTask.Grammar] = 900,
        [AiTask.WordByWord] = 600,
    };

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
        if (!PassageBudget.TryGetValue(request.Task, out var budget))
            throw new AiContextException($"No passage budget is defined for task '{request.Task}'.");

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
                MaxChars: budget,
                OutputScript: Script.Latin,
                IncludeFootnotes: false,
                StructuredNotes: true),
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

        parts.Add(new BundlePart(
            BundlePartNames.Passage,
            BundlePartState.Included,
            $"reading window of up to {budget} characters from {passage.NormalizedReference}"));

        parts.Add(new BundlePart(
            BundlePartNames.Apparatus,
            passage.NoteCount > 0 ? BundlePartState.Included : BundlePartState.Empty,
            passage.NoteCount > 0 ? $"{passage.NoteCount} print note(s)" : "no apparatus in this window"));

        var selection = BuildSelection(request.SelectionText, passage.Text, parts);
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
    /// Normalize the selection and locate it in the window. A selection the window does not contain is still
    /// passed to the model — it is what the user pointed at — but the mismatch is recorded rather than hidden,
    /// because it means the surrounding passage may not support what is being asked about.
    /// </summary>
    private static SelectionContext? BuildSelection(string? raw, string windowText, List<BundlePart> parts)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var text = Whitespace.Replace(raw.Trim(), " ");
        var found = Whitespace.Replace(windowText, " ").Contains(text, StringComparison.OrdinalIgnoreCase);

        parts.Add(new BundlePart(
            BundlePartNames.Selection,
            BundlePartState.Included,
            found ? null : "selection not found in the passage window"));

        return new SelectionContext(text, found);
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
    /// A rough size for the budget report. <b>A heuristic, and knowingly a poor one for this corpus</b> — there
    /// is no local tokenizer, and romanized Pāli with diacritics tokenizes considerably worse than English, so
    /// treat it as an order of magnitude rather than a figure. Never build a hard limit on it.
    /// </summary>
    private static int ApproximateTokens(string passage, IReadOnlyList<LemmaEntry> lemmas)
    {
        var characters = passage.Length
            + lemmas.Sum(l => l.Form.Length + l.Lemma.Length + (l.Gloss?.Length ?? 0));
        return characters / 4;
    }
}
