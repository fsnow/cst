using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CST;
using CST.Conversion;
using CST.Tools;
using Microsoft.Extensions.Logging;

// CST.Avalonia.Services.Book (the view model) shadows the corpus Book from the enclosing namespace.
using CoreBook = CST.Book;

namespace CST.Avalonia.Services.Ai;

/// <summary>Assembles the context surface B sends a model. See <see cref="AiContextBundle"/>.</summary>
public interface IAiContextBundler
{
    Task<AiContextBundle> BuildAsync(AiContextRequest request, CancellationToken ct = default);
}

/// <summary>
/// Gathers everything the model needs about the passage the user is reading, in-process. (#580)
///
/// <para><b>On the tool layer this consumes.</b> <c>ICorpusTools</c> advertised itself as the facade both the
/// local API and surface B would call, but nothing ever implemented or consumed it, and one of its four members
/// (<c>INavigationTool</c>) has no implementation at all. Surface C resolves the focused interfaces
/// individually; this does the same, and takes book classification straight from <see cref="Books"/> — the
/// stable fact — rather than waiting on a navigation tool that does not exist. The dead facade was removed
/// rather than left asserting it was load-bearing.</para>
///
/// <para><b>Optional assets degrade visibly.</b> The DPD-lemma asset and the good dictionaries are separate
/// downloads, so the services behind them are legitimately absent. Every such gap is reported as
/// <see cref="BundlePartState.Unavailable"/>, distinct from budget trimming, so "grammar was thin" can be
/// traced to a missing download instead of being mistaken for a truncation.</para>
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

    /// <summary>How many distinct words get a dictionary lookup. Bounded: each is a real query, and a wall of
    /// glosses crowds out the passage it is supposed to support.</summary>
    private static readonly Dictionary<AiTask, int> GlossBudget = new()
    {
        [AiTask.Explain] = 12,
        [AiTask.Translate] = 40,
        [AiTask.Grammar] = 24,
        [AiTask.WordByWord] = 60,
    };

    /// <summary>Words shorter than this are particles and inflectional debris; glossing them wastes the budget.</summary>
    private const int MinimumWordLength = 3;

    private static readonly Regex WordSplitter = new(@"[^\p{L}Ā-ſḀ-ỿ]+", RegexOptions.Compiled);
    private static readonly Regex HtmlTag = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex SpaceBeforePunctuation = new(@"\s+([,.;:!?])", RegexOptions.Compiled);

    private readonly IPassageTool _passage;
    private readonly IDictionaryTool _dictionary;
    private readonly ILemmaSearchService? _lemmas;
    private readonly string _appVersion;
    private readonly ILogger<AiContextBundler> _logger;

    /// <param name="lemmas">Null when the DPD-lemma asset is not installed — a supported configuration, not an
    /// error. Resolved with <c>GetService</c> in the app, exactly as the local API does.</param>
    public AiContextBundler(
        IPassageTool passage,
        IDictionaryTool dictionary,
        ILemmaSearchService? lemmas,
        string appVersion,
        ILogger<AiContextBundler> logger)
    {
        _passage = passage;
        _dictionary = dictionary;
        _lemmas = lemmas;
        _appVersion = appVersion;
        _logger = logger;
    }

    public async Task<AiContextBundle> BuildAsync(AiContextRequest request, CancellationToken ct = default)
    {
        var book = ResolveBook(request.BookId)
            ?? throw new ArgumentException($"Unknown book id '{request.BookId}'.", nameof(request));

        var parts = new List<BundlePart>();
        var budget = PassageBudget[request.Task];

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

        // NextCursor set means the window stopped short of the paragraph's end. The caller must be able to say
        // so: a translation labelled as being of this passage, silently truncated, is a fidelity hazard.
        parts.Add(new BundlePart(
            BundlePartNames.Passage,
            passage.NextCursor is not null ? BundlePartState.TrimmedForBudget : BundlePartState.Included,
            passage.NextCursor is not null ? $"window capped at {budget} characters" : null));

        parts.Add(new BundlePart(
            BundlePartNames.Apparatus,
            passage.NoteCount > 0 ? BundlePartState.Included : BundlePartState.Unavailable,
            passage.NoteCount > 0 ? $"{passage.NoteCount} print note(s)" : "no apparatus in this window"));

        var selection = BuildSelection(request.SelectionText, passage.Text, parts);

        // Glosses key off the selection when there is one — that is what the user pointed at.
        var glossSource = selection?.Text ?? passage.Text;
        var words = DistinctWords(glossSource, GlossBudget[request.Task], out var truncatedWords);

        var glosses = await GatherGlossesAsync(words, parts, truncatedWords, ct).ConfigureAwait(false);
        var lemmas = GatherLemmas(words, request.Task, parts);

        var citation = new CitationRef(
            book.FileName,
            ScriptConverter.Convert(book.LongNavPath, Script.Devanagari, Script.Latin),
            passage.NormalizedReference,
            passage.Pages);

        var provenance = new Provenance(
            _appVersion,
            _dictionary.Languages
                .Select(l => l.Source?.Title ?? l.Language)
                .ToList());

        var bundle = new AiContextBundle(
            Task: request.Task,
            OutputLanguage: request.OutputLanguage,
            UserQuestion: request.UserQuestion,
            Passage: passage,
            Selection: selection,
            Glosses: glosses,
            Lemmas: lemmas,
            Book: new BookContext(
                book.FileName,
                ScriptConverter.Convert(book.LongNavPath, Script.Devanagari, Script.Latin),
                book.Pitaka,
                book.Matn),
            Citation: citation,
            Provenance: provenance,
            Budget: new BudgetReport(parts, ApproximateTokens(passage.Text, glosses, lemmas)));

        _logger.LogDebug(
            "Built {Task} bundle for {BookId}: {Glosses} gloss(es), {Lemmas} lemma(s), ~{Tokens} tokens",
            request.Task, request.BookId, glosses.Count, lemmas.Count, bundle.Budget.ApproximateTokens);

        return bundle;
    }

    private static CoreBook? ResolveBook(string bookId) =>
        Books.Inst.FirstOrDefault(b => string.Equals(b.FileName, bookId, StringComparison.OrdinalIgnoreCase));

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

    /// <summary>Distinct words worth glossing, longest first so the budget buys the substantial ones.</summary>
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

    private async Task<IReadOnlyList<GlossEntry>> GatherGlossesAsync(
        IReadOnlyList<string> words, List<BundlePart> parts, bool truncatedWords, CancellationToken ct)
    {
        var languages = _dictionary.Languages;
        if (languages.Count == 0)
        {
            parts.Add(new BundlePart(
                BundlePartNames.Glosses, BundlePartState.Unavailable, "no dictionaries are installed"));
            return Array.Empty<GlossEntry>();
        }

        // One dictionary, deliberately: the point is a gloss per word, not every dictionary's take on each.
        // Which one is a data-quality question for the settings layer (#585) — DPD where present, since it is
        // the strong one, else whatever is installed.
        var language = languages.FirstOrDefault(l => l.Language.Contains("dpd", StringComparison.OrdinalIgnoreCase))
                       ?? languages[0];

        var glosses = new List<GlossEntry>();
        foreach (var word in words)
        {
            ct.ThrowIfCancellationRequested();

            var entries = await _dictionary
                .LookupAsync(new DictionaryRequest(language.Language, word, Script.Latin, MaxEntries: 1), ct)
                .ConfigureAwait(false);

            // Only an EXACT headword match is a gloss for this word. The lookup falls back to the nearest
            // prefix run on a miss, which is right for a person browsing and wrong here: injecting a
            // neighbouring headword as though it defined the word invents a definition the model will trust.
            var hit = entries.FirstOrDefault(e =>
                string.Equals(e.Headword, word, StringComparison.OrdinalIgnoreCase));
            if (hit is null) continue;

            glosses.Add(new GlossEntry(
                hit.Headword,
                PlainText(hit.MeaningHtml),
                hit.Source ?? language.Source?.Title,
                hit.LemmaId));
        }

        parts.Add(new BundlePart(
            BundlePartNames.Glosses,
            truncatedWords ? BundlePartState.TrimmedForBudget : BundlePartState.Included,
            $"{glosses.Count} of {words.Count} word(s) matched a headword in '{language.Language}'"));

        return glosses;
    }

    private IReadOnlyList<LemmaEntry> GatherLemmas(
        IReadOnlyList<string> words, AiTask task, List<BundlePart> parts)
    {
        // Only the grammatical presets spend on this; explain and translate do not need a paradigm.
        if (task is not (AiTask.Grammar or AiTask.WordByWord))
            return Array.Empty<LemmaEntry>();

        if (_lemmas is null || !_lemmas.IsAvailable)
        {
            parts.Add(new BundlePart(
                BundlePartNames.Lemmas,
                BundlePartState.Unavailable,
                "the DPD-lemma asset is not installed — grammar answers will be weaker"));
            return Array.Empty<LemmaEntry>();
        }

        var lemmas = new List<LemmaEntry>();
        foreach (var word in words)
        {
            var resolution = _lemmas.ResolveWord(word, Script.Latin);
            if (resolution is null) continue;

            foreach (var candidate in resolution.Candidates)
                lemmas.Add(new LemmaEntry(word, candidate.Lemma, candidate.Pos, candidate.Gloss));
        }

        parts.Add(new BundlePart(
            BundlePartNames.Lemmas,
            BundlePartState.Included,
            $"{lemmas.Count} candidate lemma(s) for {words.Count} word(s)"));

        return lemmas;
    }

    /// <summary>
    /// HTML fragment to plain text: strip tags, decode entities, collapse whitespace.
    ///
    /// <para>Tags become a space rather than nothing, so <c>a&lt;br/&gt;b</c> does not become <c>ab</c> — but an
    /// inline tag hugging punctuation (<c>&lt;b&gt;heedfulness&lt;/b&gt;,</c>) would then leave a space before
    /// the comma, so that is closed up afterwards. Small, but it is the difference between a gloss that reads
    /// like a dictionary and one that reads like scraped markup.</para>
    /// </summary>
    private static string PlainText(string html)
    {
        var text = Whitespace.Replace(WebUtility.HtmlDecode(HtmlTag.Replace(html, " ")), " ");
        return SpaceBeforePunctuation.Replace(text, "$1").Trim();
    }

    /// <summary>
    /// A rough size for the budget report. <b>A heuristic, and knowingly a poor one for this corpus</b> — there
    /// is no local tokenizer, and romanized Pāli with diacritics tokenizes considerably worse than English, so
    /// treat it as an order of magnitude rather than a figure. Never build a hard limit on it.
    /// </summary>
    private static int ApproximateTokens(
        string passage, IReadOnlyList<GlossEntry> glosses, IReadOnlyList<LemmaEntry> lemmas)
    {
        var characters = passage.Length
            + glosses.Sum(g => g.Headword.Length + g.Meaning.Length)
            + lemmas.Sum(l => l.Form.Length + l.Lemma.Length + (l.Gloss?.Length ?? 0));
        return characters / 4;
    }
}
