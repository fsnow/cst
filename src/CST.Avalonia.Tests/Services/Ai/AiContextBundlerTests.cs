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
        string.Concat(Enumerable.Repeat("appam\u0101do amatapada\u1E41\u0964 pam\u0101do maccuno pada\u1E41\u0964 ", 20));

    private static readonly string Xml =
        "<body><div id=\"dn1\" type=\"book\">" +
        "<pb ed=\"V\" n=\"1.0001\"/>" +
        "<p rend=\"bodytext\" n=\"5\">" + Verse + "</p>" +
        "<p rend=\"bodytext\" n=\"6\">appamatt\u0101 na m\u012Byanti\u0964 ye pamatt\u0101 yath\u0101 mat\u0101\u0964</p>" +
        "</div></body>";

    private readonly string _dir;

    public AiContextBundlerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cst-bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, BookId), Xml, Encoding.Unicode);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ISettingsService Settings(string? dir = null)
    {
        var m = new Mock<ISettingsService>();
        m.SetupGet(s => s.Settings).Returns(new Settings { XmlBooksDirectory = dir ?? _dir });
        return m.Object;
    }

    private sealed class StubLemmas : ILemmaSearchService
    {
        private readonly Dictionary<string, LemmaCandidate[]> _byForm;

        internal StubLemmas(bool available, Dictionary<string, LemmaCandidate[]>? byForm = null)
        {
            IsAvailable = available;
            _byForm = byForm ?? new Dictionary<string, LemmaCandidate[]>(StringComparer.OrdinalIgnoreCase);
        }

        public bool IsAvailable { get; }
        public DpdLemmaMeta? Meta => null;

        public FormResolution? ResolveWord(string word, Script sourceScript = Script.Ipe) =>
            _byForm.TryGetValue(word, out var candidates)
                ? new FormResolution(word, candidates, null, null)
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

    private AiContextBundler Bundler(ILemmaSearchService? lemmas = null, string? booksDir = null) =>
        new(new PassageTool(Settings(booksDir)),
            lemmas,
            appVersion: "6.0.0-test",
            NullLogger<AiContextBundler>.Instance);

    private static AiContextRequest Request(
        AiTask task = AiTask.Explain, string? selection = null, string language = "English",
        int paragraph = 5) =>
        new(task, BookId, language, new NavigationReference.Paragraph(paragraph), selection);

    private static BundlePart Part(AiContextBundle bundle, string name) =>
        bundle.Budget.Parts.Single(p => p.Name == name);

    [Fact]
    public async Task Gathers_the_passage_from_the_tool_layer_not_the_dom()
    {
        var bundle = await Bundler().BuildAsync(Request());

        Assert.Contains("appam\u0101do", bundle.Passage.Text);
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
    public async Task A_selection_absent_from_the_window_is_flagged_rather_than_hidden()
    {
        var bundle = await Bundler().BuildAsync(Request(selection: "nowhere in this book"));

        Assert.False(bundle.Selection!.FoundInWindow);
        Assert.Contains("not found", Part(bundle, BundlePartNames.Selection).Detail);
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
            ["appam\u0101do"] = new[] { new LemmaCandidate(1, "appam\u0101da", "masc", "heedfulness", null) },
        });

        var bundle = await Bundler(lemmas: lemmas).BuildAsync(Request(AiTask.Grammar, selection: "appam\u0101do"));

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
            ["appam\u0101do"] = new[] { new LemmaCandidate(1, "appam\u0101da", "masc", "heedfulness", null) },
        });

        var bundle = await Bundler(lemmas: lemmas).BuildAsync(Request(AiTask.Explain));

        Assert.Empty(bundle.Lemmas);
        Assert.DoesNotContain(bundle.Budget.Parts, p => p.Name == BundlePartNames.Lemmas);
    }

    [Fact]
    public async Task Stamps_provenance_so_an_eval_regression_is_attributable()
    {
        var bundle = await Bundler().BuildAsync(Request());

        Assert.Equal("6.0.0-test", bundle.Provenance.AppVersion);
        Assert.Null(bundle.Provenance.LemmaAssetVersion);   // no asset in this fixture
    }

    [Fact]
    public async Task An_unknown_book_fails_loudly_rather_than_bundling_an_empty_passage()
    {
        await Assert.ThrowsAsync<AiContextException>(
            () => Bundler().BuildAsync(new AiContextRequest(AiTask.Explain, "not-a-book.xml", "English")));
    }

    [Fact]
    public async Task The_bundle_is_data_and_survives_a_round_trip_to_json()
    {
        // Being inspectable data rather than a prompt string is what makes this testable and dumpable at all.
        // Deserializing rather than substring-matching, because the default encoder escapes the diacritics to
        // \uXXXX — which round-trips correctly but would make a text assertion test the encoder, not the bundle.
        var bundle = await Bundler().BuildAsync(Request());

        var json = System.Text.Json.JsonSerializer.Serialize(bundle);
        var back = System.Text.Json.JsonSerializer.Deserialize<AiContextBundle>(json)!;

        Assert.Equal(bundle.Passage.Text, back.Passage.Text);
        Assert.Contains("appam\u0101do", back.Passage.Text);
        Assert.Equal(bundle.Citation.NormalizedReference, back.Citation.NormalizedReference);
    }

    [Fact]
    public async Task A_catalogued_book_whose_xml_is_missing_fails_loudly()
    {
        // Reachable on a partial download. The passage tool signals it as EMPTY TEXT with the reason left in
        // NormalizedReference — so bundling it would put "book not available" in the citation the app renders
        // as authoritative.
        var empty = Path.Combine(Path.GetTempPath(), "cst-bundle-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            var error = await Assert.ThrowsAsync<AiContextException>(
                () => Bundler(booksDir: empty).BuildAsync(Request()));

            Assert.Contains("No passage text", error.Message);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [Fact]
    public async Task A_reference_the_book_does_not_have_fails_loudly()
    {
        var error = await Assert.ThrowsAsync<AiContextException>(
            () => Bundler().BuildAsync(Request(paragraph: 9999)));

        Assert.Contains("No passage text", error.Message);
    }

    [Fact]
    public async Task An_empty_apparatus_is_reported_as_empty_not_as_unavailable()
    {
        // Unavailable means "an asset is not installed". A window with no print notes is healthy — most windows
        // outside mula texts have none — and conflating the two would nag about a missing download.
        var bundle = await Bundler().BuildAsync(Request());

        Assert.Equal(BundlePartState.Empty, Part(bundle, BundlePartNames.Apparatus).State);
    }

    [Fact]
    public async Task The_window_reports_that_it_may_extend_past_the_cited_reference()
    {
        // The reader takes a character budget from the reference and flows on into what follows, so the text can
        // carry more than the citation names. That is what is actually known — there is deliberately no
        // "was truncated" flag, since NextCursor means end-of-FILE, not end-of-paragraph.
        var bundle = await Bundler().BuildAsync(Request(AiTask.WordByWord));

        Assert.True(bundle.Budget.WindowMayExtendPastReference);
    }

    [Fact]
    public async Task Diacritics_survive_the_round_trip_into_the_bundle()
    {
        var bundle = await Bundler().BuildAsync(Request());

        Assert.Contains("appam\u0101do", bundle.Passage.Text);
        Assert.DoesNotContain("\uFFFD", bundle.Passage.Text);
    }
}
