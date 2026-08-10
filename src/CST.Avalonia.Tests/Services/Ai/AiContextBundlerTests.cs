using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Ai;
using CST.Avalonia.Services.Tools;
using CST.Conversion;
using CST.Lemma;
using CST.Navigation;
using CST.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// The context bundler against a real passage tool reading a real (temp) UTF-16 book — the format boundary is
/// the thing worth exercising, so the passage half is genuine rather than mocked. The dictionary and lemma
/// halves are stubbed, since what matters there is how their ABSENCE and their misses are reported. (#580)
/// </summary>
public class AiContextBundlerTests : IDisposable
{
    private const string BookId = "s0101m.mul.xml";

    // Paragraph 5 is deliberately long enough to overflow the smallest per-task window (word-by-word, 600
    // chars) while fitting the largest, so truncation can be asserted in both directions. `appamado` appears
    // so a gloss can be matched to a real word from the text.
    private static readonly string Verse =
        string.Concat(Enumerable.Repeat("appamado amatapadam। pamado maccuno padam। ", 20));

    private static readonly string Xml =
        "<body><div id=\"dn1\" type=\"book\">" +
        "<pb ed=\"V\" n=\"1.0001\"/>" +
        "<p rend=\"bodytext\" n=\"5\">" + Verse + "</p>" +
        "<p rend=\"bodytext\" n=\"6\">appamatta na miyanti। ye pamatta yatha mata।</p>" +
        "</div></body>";

    private readonly string _dir;

    public AiContextBundlerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cst-bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, BookId), Xml, Encoding.Unicode);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ISettingsService Settings()
    {
        var m = new Mock<ISettingsService>();
        m.SetupGet(s => s.Settings).Returns(new Settings { XmlBooksDirectory = _dir });
        return m.Object;
    }

    /// <summary>A dictionary with a fixed set of headwords; anything else is a miss.</summary>
    private sealed class StubDictionary : IDictionaryTool
    {
        private readonly Dictionary<string, string> _entries;

        internal StubDictionary(Dictionary<string, string>? entries = null, bool anyInstalled = true)
        {
            _entries = entries ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Languages = anyInstalled
                ? new[] { new DictionaryLanguageInfo("dpd", new DictionarySourceInfo("Digital Pāḷi Dictionary",
                    null, null, null, null, null, null)) }
                : Array.Empty<DictionaryLanguageInfo>();
        }

        public IReadOnlyList<DictionaryLanguageInfo> Languages { get; }

        public Task<IReadOnlyList<DictionaryEntry>> LookupAsync(
            DictionaryRequest request, CancellationToken ct = default)
        {
            // Mirrors the real tool: an exact hit, otherwise the NEAREST headword rather than nothing.
            if (_entries.TryGetValue(request.Query, out var meaning))
                return Task.FromResult<IReadOnlyList<DictionaryEntry>>(
                    new[] { new DictionaryEntry(request.Query, meaning, "Digital Pāḷi Dictionary", 42L) });

            var neighbour = _entries.Keys.FirstOrDefault();
            return Task.FromResult<IReadOnlyList<DictionaryEntry>>(neighbour is null
                ? Array.Empty<DictionaryEntry>()
                : new[] { new DictionaryEntry(neighbour, _entries[neighbour], "Digital Pāḷi Dictionary", 7L) });
        }
    }

    private sealed class StubLemmas : ILemmaSearchService
    {
        private readonly Dictionary<string, LemmaCandidate> _byForm;

        internal StubLemmas(bool available, Dictionary<string, LemmaCandidate>? byForm = null)
        {
            IsAvailable = available;
            _byForm = byForm ?? new Dictionary<string, LemmaCandidate>(StringComparer.OrdinalIgnoreCase);
        }

        public bool IsAvailable { get; }
        public DpdLemmaMeta? Meta => null;

        public FormResolution? ResolveWord(string word, Script sourceScript = Script.Ipe) =>
            _byForm.TryGetValue(word, out var candidate)
                ? new FormResolution(word, new[] { candidate }, null, null)
                : null;

        public WordDeconstruction? Deconstruct(string word, Script sourceScript = Script.Ipe) => null;

        public Task<LemmaSearchResult?> ExpandAndSearchAsync(
            long lemmaId, bool includeFamily = false, BookFilter? filter = null,
            Script outputScript = Script.Latin, CancellationToken ct = default, bool includeRelated = true) =>
            Task.FromResult<LemmaSearchResult?>(null);

        public Task<LemmaSearchResult?> ExpandAndSearchSetAsync(
            IReadOnlyList<long> lemmaIds, Script outputScript = Script.Latin, CancellationToken ct = default) =>
            Task.FromResult<LemmaSearchResult?>(null);
    }

    private AiContextBundler Bundler(IDictionaryTool? dictionary = null, ILemmaSearchService? lemmas = null) =>
        new(new PassageTool(Settings()),
            dictionary ?? new StubDictionary(),
            lemmas,
            appVersion: "6.0.0-test",
            NullLogger<AiContextBundler>.Instance);

    private static AiContextRequest Request(
        AiTask task = AiTask.Explain, string? selection = null, string language = "English") =>
        new(task, BookId, new NavigationReference.Paragraph(5), selection, language);

    private static BundlePart Part(AiContextBundle bundle, string name) =>
        bundle.Budget.Parts.Single(p => p.Name == name);

    [Fact]
    public async Task Gathers_the_passage_from_the_tool_layer_not_the_dom()
    {
        var bundle = await Bundler().BuildAsync(Request());

        Assert.Contains("appamado", bundle.Passage.Text);
        Assert.Equal(5, bundle.Passage.ParagraphNumber);
    }

    [Fact]
    public async Task Renders_the_citation_from_data_so_a_garbled_answer_cannot_forge_one()
    {
        var bundle = await Bundler().BuildAsync(Request());

        Assert.Equal(BookId, bundle.Citation.BookId);
        Assert.Equal("paragraph 5 (dn1)", bundle.Citation.NormalizedReference);
        Assert.Contains(bundle.Citation.Pages, p => p.Edition == PageEdition.Vri && p.Number == 1);
    }

    [Fact]
    public async Task Carries_the_output_language_so_translate_into_what_has_an_answer()
    {
        var bundle = await Bundler().BuildAsync(Request(language: "Burmese"));

        Assert.Equal("Burmese", bundle.OutputLanguage);
    }

    [Fact]
    public async Task Classifies_the_book_so_the_model_knows_what_it_is_reading()
    {
        var bundle = await Bundler().BuildAsync(Request());

        Assert.Equal(BookId, bundle.Book.BookId);
        Assert.False(string.IsNullOrWhiteSpace(bundle.Book.Name));
    }

    [Fact]
    public async Task Projects_gloss_html_to_plain_text()
    {
        // MeaningHtml is an HTML fragment; markup in the prompt wastes tokens and gets echoed into answers.
        var dictionary = new StubDictionary(new(StringComparer.OrdinalIgnoreCase)
        {
            ["appamado"] = "<b>heedfulness</b>, diligence &amp; vigilance<br/>",
        });

        var bundle = await Bundler(dictionary).BuildAsync(Request());

        var gloss = Assert.Single(bundle.Glosses);
        Assert.Equal("heedfulness, diligence & vigilance", gloss.Meaning);
        Assert.Equal("Digital Pāḷi Dictionary", gloss.Attribution);
    }

    [Fact]
    public async Task A_near_miss_is_not_passed_off_as_a_definition()
    {
        // The lookup falls back to the nearest headword on a miss — right for a person browsing, and dangerous
        // here: injecting a neighbouring entry as though it defined the word invents a gloss the model trusts.
        var dictionary = new StubDictionary(new(StringComparer.OrdinalIgnoreCase)
        {
            ["something-else-entirely"] = "not a definition of anything in the passage",
        });

        var bundle = await Bundler(dictionary).BuildAsync(Request());

        Assert.Empty(bundle.Glosses);
    }

    [Fact]
    public async Task Glosses_key_off_the_selection_when_there_is_one()
    {
        var dictionary = new StubDictionary(new(StringComparer.OrdinalIgnoreCase)
        {
            ["appamado"] = "heedfulness",
            ["maccuno"] = "of death",
        });

        var bundle = await Bundler(dictionary).BuildAsync(Request(selection: "appamado"));

        Assert.Equal("appamado", Assert.Single(bundle.Glosses).Headword);
    }

    [Fact]
    public async Task A_selection_absent_from_the_window_is_flagged_rather_than_hidden()
    {
        var bundle = await Bundler().BuildAsync(Request(selection: "nowhere in this book"));

        Assert.False(bundle.Selection!.FoundInWindow);
        Assert.Contains("not found", Part(bundle, BundlePartNames.Selection).Detail);
    }

    [Fact]
    public async Task A_truncated_passage_is_reported_so_the_answer_can_be_badged()
    {
        // Word-by-word has the smallest window and the fixture overflows it. A translation labelled as being
        // of this passage, silently truncated, is the fidelity hazard this flag exists to surface.
        var bundle = await Bundler().BuildAsync(Request(AiTask.WordByWord));

        Assert.True(bundle.Budget.PassageWasTrimmed);
        Assert.Equal(BundlePartState.TrimmedForBudget, Part(bundle, BundlePartNames.Passage).State);
    }

    [Fact]
    public async Task A_passage_that_fits_is_not_reported_as_trimmed()
    {
        var bundle = await Bundler().BuildAsync(Request(AiTask.Translate));

        Assert.False(bundle.Budget.PassageWasTrimmed);
        Assert.Equal(BundlePartState.Included, Part(bundle, BundlePartNames.Passage).State);
    }

    [Fact]
    public async Task A_missing_lemma_asset_reads_as_unavailable_not_as_a_budget_problem()
    {
        // The distinction is the point: conflating them sends whoever is debugging a thin grammar answer to
        // the budget instead of to the missing download.
        var bundle = await Bundler(lemmas: null).BuildAsync(Request(AiTask.Grammar));

        var part = Part(bundle, BundlePartNames.Lemmas);
        Assert.Equal(BundlePartState.Unavailable, part.State);
        Assert.Contains("not installed", part.Detail);
        Assert.Empty(bundle.Lemmas);
    }

    [Fact]
    public async Task An_installed_but_empty_lemma_service_is_also_unavailable()
    {
        var bundle = await Bundler(lemmas: new StubLemmas(available: false)).BuildAsync(Request(AiTask.Grammar));

        Assert.Equal(BundlePartState.Unavailable, Part(bundle, BundlePartNames.Lemmas).State);
    }

    [Fact]
    public async Task Lemmas_are_gathered_for_the_grammatical_presets()
    {
        var lemmas = new StubLemmas(available: true, new(StringComparer.OrdinalIgnoreCase)
        {
            ["appamado"] = new LemmaCandidate(1, "appamāda", "masc", "heedfulness", null),
        });

        var bundle = await Bundler(lemmas: lemmas).BuildAsync(Request(AiTask.Grammar, selection: "appamado"));

        var entry = Assert.Single(bundle.Lemmas);
        Assert.Equal("appamāda", entry.Lemma);
        Assert.Equal("masc", entry.PartOfSpeech);
    }

    [Fact]
    public async Task Explain_does_not_spend_on_lemmas()
    {
        // A paradigm is not what "explain this passage" needs, and every lookup costs.
        var lemmas = new StubLemmas(available: true, new(StringComparer.OrdinalIgnoreCase)
        {
            ["appamado"] = new LemmaCandidate(1, "appamāda", "masc", "heedfulness", null),
        });

        var bundle = await Bundler(lemmas: lemmas).BuildAsync(Request(AiTask.Explain));

        Assert.Empty(bundle.Lemmas);
        Assert.DoesNotContain(bundle.Budget.Parts, p => p.Name == BundlePartNames.Lemmas);
    }

    [Fact]
    public async Task No_dictionaries_installed_is_reported_as_unavailable()
    {
        var bundle = await Bundler(new StubDictionary(anyInstalled: false)).BuildAsync(Request());

        Assert.Equal(BundlePartState.Unavailable, Part(bundle, BundlePartNames.Glosses).State);
        Assert.Empty(bundle.Glosses);
    }

    [Fact]
    public async Task Stamps_provenance_so_an_eval_regression_is_attributable()
    {
        var bundle = await Bundler().BuildAsync(Request());

        Assert.Equal("6.0.0-test", bundle.Provenance.AppVersion);
        Assert.Contains("Digital Pāḷi Dictionary", bundle.Provenance.DictionarySources);
    }

    [Fact]
    public async Task An_unknown_book_fails_loudly_rather_than_bundling_an_empty_passage()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Bundler().BuildAsync(new AiContextRequest(AiTask.Explain, "not-a-book.xml")));
    }

    [Fact]
    public async Task The_bundle_is_data_and_survives_a_round_trip_to_json()
    {
        // Being inspectable data rather than a prompt string is what makes this testable and dumpable at all.
        var bundle = await Bundler().BuildAsync(Request());

        var json = System.Text.Json.JsonSerializer.Serialize(bundle);

        Assert.Contains("appamado", json);
        Assert.Contains("paragraph 5", json);
    }
}
