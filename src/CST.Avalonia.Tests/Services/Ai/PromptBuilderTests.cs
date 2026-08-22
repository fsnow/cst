using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CST;
using CST.Avalonia.Services.Ai;
using CST.Navigation;
using CST.Search;
using CST.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// Bundle to prompt. (#582)
///
/// <para>What is actually under test is the grounding contract: that the prompt states its own scope, that an
/// absent part says it is absent rather than rendering as an empty heading, and that the same facts reach the
/// user through <see cref="RenderedPrompt.Notices"/>. A degradation the model is warned about and the user is
/// not is still silent from where the user is sitting.</para>
/// </summary>
public class PromptBuilderTests : IDisposable
{
    private readonly string _dir;
    private readonly PromptTemplateStore _store;
    private readonly PromptBuilder _builder;

    public PromptBuilderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cst-prompt-build-" + Guid.NewGuid().ToString("N"));
        _store = new PromptTemplateStore(_dir, NullLogger<PromptTemplateStore>.Instance);
        _builder = new PromptBuilder(_store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    /// <summary>The smallest override that validates: every required placeholder, no prose.</summary>
    private const string Minimal = "{{citation}}\n{{passage}}\n{{selection}}\n{{userQuestion}}";

    private static AiContextBundle Bundle(
        AiTask task = AiTask.Explain,
        string passage = "appamādo amatapadaṃ, pamādo maccuno padaṃ.",
        SelectionContext? selection = null,
        IReadOnlyList<LemmaEntry>? lemmas = null,
        IReadOnlyList<BundlePart>? parts = null,
        IReadOnlyList<ApparatusNote>? notes = null,
        int? paragraphsCovered = 3,
        string? userQuestion = null,
        string outputLanguage = "English",
        CommentaryLevel level = CommentaryLevel.Mula)
    {
        var pages = new[] { new SnippetPageRef(PageEdition.Vri, 1, 17) };
        var result = new PassageResult(
            "s0502m.mul.xml", "paragraph 21 (dhp)", passage, pages, 21, "dhp", null,
            paragraphsCovered > 1 ? 4200 : null,
            notes?.Count ?? 0, notes ?? Array.Empty<ApparatusNote>());

        return new AiContextBundle(
            task, outputLanguage, userQuestion, result, selection,
            lemmas ?? Array.Empty<LemmaEntry>(),
            new BookContext("s0502m.mul.xml", "Dhammapadapāḷi", Pitaka.Sutta, level),
            new CitationRef("s0502m.mul.xml", "Dhammapadapāḷi", "paragraph 21 (dhp)", pages),
            new Provenance("6.0.0-test", null),
            new BudgetReport(
                parts ?? new[] { new BundlePart(BundlePartNames.Passage, BundlePartState.Included, "a window") },
                500, paragraphsCovered));
    }

    [Fact]
    public void The_system_prompt_states_the_scope_from_bundle_data()
    {
        // The fence against injection's characteristic failure: a truthful answer over a narrower scope than the
        // user assumed. The model is told what the excerpt IS before it is told what to do with it.
        var prompt = _builder.Build(Bundle());

        Assert.Contains("Dhammapadapāḷi", prompt.System);
        Assert.Contains("paragraph 21 (dhp)", prompt.System);
        Assert.Contains("covering 3 numbered paragraphs", prompt.System);
    }

    [Fact]
    public void A_window_that_reached_the_end_of_the_file_says_so_instead()
    {
        var prompt = _builder.Build(Bundle(paragraphsCovered: 1));

        Assert.Contains("exactly the paragraph named", prompt.System);
        Assert.DoesNotContain("numbered paragraphs", prompt.System);
    }

    [Fact]
    public void The_answer_language_reaches_the_prompt()
    {
        var prompt = _builder.Build(Bundle(outputLanguage: "Burmese"));

        Assert.Contains("Write in Burmese", prompt.System);
    }

    [Fact]
    public void The_quote_markers_come_from_the_constant_not_from_the_template_text()
    {
        var prompt = _builder.Build(Bundle());

        Assert.Contains(PaliQuoteMarkers.Open + "appamādo amatapadaṃ" + PaliQuoteMarkers.Close, prompt.System);
    }

    [Fact]
    public void The_user_turn_carries_the_passage_and_the_citation()
    {
        var prompt = _builder.Build(Bundle());

        Assert.Contains("appamādo amatapadaṃ", prompt.UserContent);
        Assert.Contains("paragraph 21 (dhp)", prompt.UserContent);
        Assert.Contains("VRI vol. 1 p. 17", prompt.UserContent);
    }

    [Fact]
    public void The_commentary_level_is_stated_so_a_tika_is_not_read_as_the_canonical_text()
    {
        var prompt = _builder.Build(Bundle(level: CommentaryLevel.Tika));

        Assert.Contains("ṭīkā", prompt.UserContent);
        Assert.Contains("Sutta piṭaka", prompt.UserContent);
    }

    [Fact]
    public void No_placeholder_survives_into_a_rendered_prompt()
    {
        // A literal "{{lemmas}}" reaching the model reads as a section that should have been filled and was not.
        foreach (var task in Enum.GetValues<AiTask>())
        {
            var prompt = _builder.Build(Bundle(task));

            Assert.DoesNotContain("{{", prompt.System);
            Assert.DoesNotContain("{{", prompt.UserContent);
        }
    }

    [Fact]
    public void No_preset_imposes_an_output_cap()
    {
        // The cap is deliberately unset: it would have to predict output length, and on a reasoning model it
        // cannot — reasoning and answer share the budget, so the cap truncates mid-answer or yields a blank
        // panel (#601). The per-preset table survives as the seam #584/#585 fills in from Settings.
        foreach (var task in Enum.GetValues<AiTask>())
            Assert.Null(_builder.Build(Bundle(task)).MaxOutputTokens);
    }

    [Fact]
    public void A_task_with_no_budget_entry_fails_loudly_rather_than_rendering()
    {
        // What the all-null table still buys at runtime: membership says the task is renderable at all.
        Assert.Throws<ArgumentOutOfRangeException>(() => _builder.Build(Bundle((AiTask)999)));
    }

    [Fact]
    public void An_absent_selection_is_a_sentence_not_an_empty_heading()
    {
        var prompt = _builder.Build(Bundle());

        Assert.Contains("has not selected anything", prompt.UserContent);
    }

    [Fact]
    public void An_asked_question_leads_the_prompt_rather_than_riding_on_a_preset()
    {
        // Before this task existed, a reader with a question had to pick a preset to carry it -- Explain, in
        // practice -- so the model was told to explain the passage AND answer a question, and the preset's
        // instructions competed with the question for what the answer should be about.
        var prompt = _builder.Build(Bundle(task: AiTask.Ask, userQuestion: "what governs bhavissanti?"));

        Assert.Contains("what governs bhavissanti?", prompt.UserContent);
        Assert.Contains("Answer the reader's question", prompt.UserContent);

        // No preset instruction survives to compete with it.
        Assert.DoesNotContain("Explain this passage", prompt.UserContent);
        Assert.DoesNotContain("Translate this passage", prompt.UserContent);

        // And the question comes before the passage it is asked about.
        var question = prompt.UserContent.IndexOf("what governs bhavissanti?", StringComparison.Ordinal);
        var passage = prompt.UserContent.IndexOf("appamādo", StringComparison.Ordinal);
        Assert.True(question < passage, "the question must lead the prompt");
    }

    [Fact]
    public void A_question_about_a_selection_makes_the_selection_the_subject()
    {
        var prompt = _builder.Build(Bundle(
            task: AiTask.Ask,
            userQuestion: "which word is the verb?",
            selection: new SelectionContext("appamādo amatapadaṃ", SelectionState.Located)));

        Assert.Contains("which word is the verb?", prompt.UserContent);
        Assert.Contains("about the text they have selected", prompt.UserContent);
        Assert.Contains("for context only", prompt.UserContent);
    }

    [Fact]
    public void A_clean_request_produces_no_notices_however_many_paragraphs_it_covers()
    {
        // A window spanning several paragraphs used to raise "The passage window covers N paragraphs, not
        // just the one cited". It fired on almost every request, so the notice list read as a list of
        // problems on requests that had none -- and since #649 the window is built around the reader's own
        // selection, making its extent a consequence of what they asked about rather than something that
        // happened to them. Asserted as "no notices at all" rather than as the absence of one string, so a
        // differently-worded reintroduction fails too.
        var prompt = _builder.Build(Bundle(paragraphsCovered: 29));

        Assert.Empty(prompt.Notices);
    }

    [Fact]
    public void A_selection_carries_no_caveat_about_its_own_context()
    {
        // There used to be a "this selection was not found in the passage above" branch here, because the
        // window came from scroll position and might genuinely not contain the selection. The window is now
        // built around the selection, so the caveat has nothing to warn about -- and a prompt that tells the
        // model its own context might be the wrong context degrades every answer it touches. (#649)
        var prompt = _builder.Build(Bundle(
            selection: new SelectionContext("appamādo amatapadaṃ", SelectionState.Located)));

        Assert.Contains("appamādo amatapadaṃ", prompt.UserContent);
        Assert.DoesNotContain("not found in the passage", prompt.UserContent);
        Assert.DoesNotContain("may not be", prompt.UserContent);
        Assert.DoesNotContain(prompt.Notices, n => n.Contains("not found in the passage window"));
    }

    [Fact]
    public void An_unreadable_selection_produces_exactly_one_notice()
    {
        // #831: the reader saw "2 notes about this request" for a single fact — a generic one from the
        // parts loop and the specific one from the switch below it. Two notices read as two things having
        // gone wrong, and the generic one was the worse of the pair: it said "could not be gathered", which
        // is the bundler's vocabulary, and named a cause ("the page was not ready, or the request timed
        // out") that #824 and #827 both showed was not what had happened.
        //
        // Asserted as a COUNT plus the surviving wording. Asserting only that the good one is present would
        // pass with both, which is the state being fixed.
        var prompt = _builder.Build(Bundle(
            selection: new SelectionContext(null, SelectionState.Unavailable),
            parts: new[]
            {
                new BundlePart(BundlePartNames.Passage, BundlePartState.Included, "a window"),
                new BundlePart(BundlePartNames.Selection, BundlePartState.Unavailable,
                    "the reader could not read the selection"),
            }));

        Assert.Single(prompt.Notices);
        Assert.Contains("the answer covers the whole passage", prompt.Notices[0]);
        Assert.DoesNotContain("could not be gathered", prompt.Notices[0]);
    }

    [Fact]
    public void Another_unavailable_part_still_gets_the_generic_notice()
    {
        // The exclusion in #831 is for the selection alone. Word analysis and the rest have no sentence of
        // their own, so silencing the generic notice for everything would trade two notices for none — and
        // a missing part nobody is told about is where this whole family of defects started.
        var prompt = _builder.Build(Bundle(
            task: AiTask.WordByWord,
            parts: new[]
            {
                new BundlePart(BundlePartNames.Passage, BundlePartState.Included, "a window"),
                new BundlePart(BundlePartNames.Lemmas, BundlePartState.Unavailable, "the dictionary is still downloading"),
            }));

        Assert.Contains(prompt.Notices, n => n.Contains("could not be gathered"));
    }

    // ---- The selection is the subject of the request -----------------------------------------------

    [Fact]
    public void A_selection_makes_the_instruction_about_the_selection_and_not_the_passage()
    {
        // The reported defect, at its source. "If I select text and click Translate, I expect that that is
        // what is translated." The old single template opened its instructions with "Translate this passage",
        // spent five rules elaborating the passage, and appended the selection as a trailing conditional --
        // so the model translated the passage. Nothing about the wording was wrong in isolation; the DOCUMENT
        // was about the passage.
        var prompt = _builder.Build(Bundle(
            task: AiTask.Translate,
            selection: new SelectionContext("appamādo amatapadaṃ", SelectionState.Located)));

        Assert.Contains("Translate the selected text.", prompt.UserContent);
        Assert.DoesNotContain("Translate this passage.", prompt.UserContent);

        // And no conditional survives for a weak model to mis-weight: every instruction it receives is
        // unconditional and true of this request.
        Assert.DoesNotContain("If a selection is given", prompt.UserContent);
    }

    [Theory]
    [InlineData(AiTask.Explain)]
    [InlineData(AiTask.Translate)]
    [InlineData(AiTask.Grammar)]
    [InlineData(AiTask.WordByWord)]
    [InlineData(AiTask.Ask)]
    public void Every_preset_demotes_the_passage_to_context_when_something_is_selected(AiTask task)
    {
        // Pins the whole set rather than the one preset that was reported. All four had the same shape, so
        // all four had the same defect, and a fix applied to Translate alone would have looked complete.
        var prompt = _builder.Build(Bundle(
            task: task,
            selection: new SelectionContext("appamādo amatapadaṃ", SelectionState.Located)));

        Assert.Contains("for context only", prompt.UserContent);
        // Each preset disclaims the passage in its own words -- "not what you were asked to translate", "not
        // what the question is about" -- so the assertion is on the disclaimer, not on one phrasing of it.
        Assert.Contains("It is not what", prompt.UserContent);

        // The selection has to be present in full, above the context, or the heading is a lie.
        var subject = prompt.UserContent.IndexOf("appamādo amatapadaṃ", StringComparison.Ordinal);
        var context = prompt.UserContent.IndexOf("for context only", StringComparison.Ordinal);
        Assert.True(subject >= 0 && subject < context, "the selection must come before the context section");
    }

    [Fact]
    public void The_system_prompt_agrees_with_the_preset_about_what_is_being_asked()
    {
        // The half that is easy to miss: {{scope}} is rendered by the builder, not by the preset template, so
        // splitting the presets alone would leave the system prompt still saying "the whole of what follows is
        // in view, say which part you are answering about" -- the passage-as-subject argument, one layer up,
        // contradicting the preset directly beneath it.
        var withSelection = _builder.Build(Bundle(
            selection: new SelectionContext("appamādo amatapadaṃ", SelectionState.Located)));
        var without = _builder.Build(Bundle());

        Assert.Contains("The selection is the subject of this request", withSelection.System);
        Assert.DoesNotContain("The selection is the subject", without.System);
    }

    [Fact]
    public void A_selection_that_could_not_be_read_keeps_the_passage_as_the_subject()
    {
        // Three outcomes, not two (#581). "The reader selected something we could not read" has no text to
        // make the subject, so it routes to the base preset -- but the model must still be told a selection
        // was dropped, or the answer silently ignores what the user watched themselves supply.
        var prompt = _builder.Build(Bundle(
            task: AiTask.Translate,
            selection: new SelectionContext(null, SelectionState.Unavailable)));

        Assert.Contains("Translate this passage.", prompt.UserContent);
        Assert.Contains("could not be read", prompt.UserContent);
    }

    [Fact]
    public void A_selection_that_could_not_be_read_is_distinguished_from_none()
    {
        // The two were the same null before #581. The difference is an answer about the whole passage
        // (correct) versus one that quietly dropped the words the user highlighted.
        var prompt = _builder.Build(Bundle(
            selection: new SelectionContext(null, SelectionState.Unavailable)));

        Assert.Contains("could not be read", prompt.UserContent);
        Assert.DoesNotContain("has not selected anything", prompt.UserContent);
        Assert.Contains(prompt.Notices, n => n.Contains("could not be read"));
    }

    [Fact]
    public void A_missing_lemma_asset_is_named_as_a_missing_download_not_as_a_fact_about_the_words()
    {
        // The plan requires the lemma presets to degrade VISIBLY. The distinction carried here is the one that
        // matters: "we could not look this up" versus "these words have no analysis".
        var prompt = _builder.Build(Bundle(
            AiTask.Grammar,
            parts: new[]
            {
                new BundlePart(BundlePartNames.Passage, BundlePartState.Included, "a window"),
                new BundlePart(BundlePartNames.Lemmas, BundlePartState.Unavailable, "the DPD-lemma asset is not installed"),
            }));

        Assert.Contains("not installed", prompt.UserContent);
        Assert.Contains("say that the analysis was unavailable", prompt.UserContent);
        Assert.Contains(prompt.Notices, n => n.Contains("word analysis") && n.Contains("not installed"));
    }

    [Fact]
    public void An_empty_lemma_result_reads_as_a_gap_in_the_lookup_rather_than_a_missing_asset()
    {
        var prompt = _builder.Build(Bundle(
            AiTask.Grammar,
            parts: new[]
            {
                new BundlePart(BundlePartNames.Passage, BundlePartState.Included, "a window"),
                new BundlePart(BundlePartNames.Lemmas, BundlePartState.Included, "0 candidate lemma(s)"),
            }));

        Assert.Contains("gap in the lookup", prompt.UserContent);
        Assert.DoesNotContain("not installed", prompt.UserContent);
    }

    [Fact]
    public void A_preset_that_gathers_no_lemmas_says_so_rather_than_reporting_an_empty_lookup()
    {
        // Reachable by adding {{lemmas}} to a preset that does not gather them — the bundler records a lemmas
        // part only for Grammar and WordByWord. Saying "the lookup returned no candidates" here would describe
        // a lookup that never ran, which is the same class of false diagnostic as an empty section.
        _store.Save(PromptTemplateNames.Explain, Minimal + "\n{{lemmas}}");

        var prompt = _builder.Build(Bundle());

        Assert.Contains("this preset does not use it", prompt.UserContent);
        Assert.DoesNotContain("gap in the lookup", prompt.UserContent);
    }

    [Fact]
    public void Lemmas_render_as_a_table_and_a_pipe_in_a_gloss_does_not_break_a_row()
    {
        var prompt = _builder.Build(Bundle(
            AiTask.WordByWord,
            lemmas: new[]
            {
                new LemmaEntry("appamādo", "appamāda", "masc", "heedfulness"),
                new LemmaEntry("padaṃ", "pada", "nt", "foot | word | state"),
            }));

        Assert.Contains("| appamādo | appamāda | masc | heedfulness |", prompt.UserContent);
        Assert.Contains(@"foot \| word \| state", prompt.UserContent);
    }

    [Fact]
    public void A_trimmed_lemma_list_tells_the_model_that_absence_means_nothing()
    {
        var prompt = _builder.Build(Bundle(
            AiTask.WordByWord,
            lemmas: new[] { new LemmaEntry("appamādo", "appamāda", "masc", null) },
            parts: new[]
            {
                new BundlePart(BundlePartNames.Passage, BundlePartState.Included, "a window"),
                new BundlePart(BundlePartNames.Lemmas, BundlePartState.TrimmedForBudget, "60 of 140 words"),
            }));

        Assert.Contains("no conclusion follows from their absence", prompt.UserContent);
        Assert.Contains(prompt.Notices, n => n.Contains("cut to fit"));
    }

    [Fact]
    public void Apparatus_notes_are_labelled_as_evidence_about_the_text_not_as_the_text()
    {
        var prompt = _builder.Build(Bundle(
            AiTask.Translate,
            notes: new[] { new ApparatusNote(12, "appamādo", "appamādā", "sī, syā") }));

        Assert.Contains("reading *appamādā* in sī, syā", prompt.UserContent);
        Assert.Contains("evidence about the text, not part of it", prompt.UserContent);
    }

    [Fact]
    public void A_window_with_no_apparatus_says_so_rather_than_leaving_the_section_blank()
    {
        var prompt = _builder.Build(Bundle(AiTask.Translate));

        Assert.Contains("no print notes in this window", prompt.UserContent);
    }

    [Fact]
    public void The_users_question_lands_verbatim_and_last()
    {
        var prompt = _builder.Build(Bundle(userQuestion: "why the instrumental here?"));

        Assert.Contains("why the instrumental here?", prompt.UserContent);
        Assert.EndsWith("why the instrumental here?", prompt.UserContent.TrimEnd());
    }

    [Fact]
    public void No_question_leaves_no_empty_heading_behind()
    {
        var prompt = _builder.Build(Bundle());

        Assert.DoesNotContain("The reader asks", prompt.UserContent);
    }

    [Fact]
    public void The_provisions_inventory_names_what_was_and_was_not_gathered()
    {
        var prompt = _builder.Build(Bundle(
            parts: new[]
            {
                new BundlePart(BundlePartNames.Passage, BundlePartState.Included, "a window"),
                new BundlePart(BundlePartNames.Apparatus, BundlePartState.Empty, "no apparatus in this window"),
            }));

        Assert.Contains("**passage** — given", prompt.UserContent);
        Assert.Contains("**apparatus** — nothing to give", prompt.UserContent);
    }

    [Fact]
    public void The_inventory_cannot_claim_a_part_the_template_never_shows()
    {
        // Found by reading a dumped prompt rather than by a test: the inventory said "apparatus — given" while
        // only the translate template rendered the notes, so on every other preset the model was told it had
        // been given something it could not see. Deriving the inventory from the template's own placeholders
        // makes the two agree by construction, including after a user edit.
        _store.Save(PromptTemplateNames.Explain, Minimal + "\n{{provisions}}");

        var prompt = _builder.Build(Bundle(
            parts: new[]
            {
                new BundlePart(BundlePartNames.Passage, BundlePartState.Included, "a window"),
                new BundlePart(BundlePartNames.Apparatus, BundlePartState.Included, "3 print note(s)"),
            }));

        Assert.Contains("**passage** — given", prompt.UserContent);
        Assert.DoesNotContain("apparatus", prompt.UserContent);
    }

    [Fact]
    public void A_degradation_in_a_part_the_preset_does_not_use_is_not_reported_to_the_user()
    {
        // An Explain answer is not worse for the word-analysis download being absent — it never asked for one.
        _store.Save(PromptTemplateNames.Explain, Minimal + "\n{{provisions}}");

        var prompt = _builder.Build(Bundle(
            parts: new[]
            {
                new BundlePart(BundlePartNames.Passage, BundlePartState.Included, "a window"),
                new BundlePart(BundlePartNames.Lemmas, BundlePartState.Unavailable, "asset not installed"),
            },
            paragraphsCovered: 1));

        Assert.Empty(prompt.Notices);
    }

    [Fact]
    public void An_empty_part_is_not_reported_to_the_user_as_a_problem()
    {
        // "Nothing to give" is healthy — most windows outside mūla texts carry no apparatus. A notice here would
        // nag about a missing download on a perfectly good bundle.
        var prompt = _builder.Build(Bundle(
            parts: new[] { new BundlePart(BundlePartNames.Apparatus, BundlePartState.Empty, "none") },
            paragraphsCovered: 1));

        Assert.Empty(prompt.Notices);
    }

    [Fact]
    public void A_rejected_prompt_edit_is_reported_to_the_user_who_made_it()
    {
        // The one degradation the model cannot be told about: the template that would have carried the news is
        // the one that failed to load. Without a notice the user reads answers from a prompt they think they
        // replaced.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "explain.md"), "just do it");

        var prompt = _builder.Build(Bundle());

        Assert.Contains(prompt.Notices, n => n.Contains("explain") && n.Contains("not used"));
        Assert.Contains("appamādo amatapadaṃ", prompt.UserContent);   // the built-in still ran
    }

    [Fact]
    public void A_valid_prompt_edit_is_used_and_reported_as_nothing()
    {
        _store.Save(PromptTemplateNames.Explain, "MY TEMPLATE\n" + Minimal);

        var prompt = _builder.Build(Bundle(paragraphsCovered: 1));

        Assert.StartsWith("MY TEMPLATE", prompt.UserContent);
        Assert.Empty(prompt.Notices);
    }
}
