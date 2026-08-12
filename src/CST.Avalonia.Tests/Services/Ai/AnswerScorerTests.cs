using System;
using System.Linq;
using CST.Avalonia.Services.Ai;
using CST.Avalonia.Services.Ai.Eval;
using CST;
using CST.Navigation;
using CST.Search;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// The eval harness's scorers. (#587, AI_SURFACE_B.md §13)
///
/// <para>These run without a model or a network — which is the point. The live runs cost money, so what they
/// spend it on must be measurement whose behaviour is already pinned down here; a scorer debugged against live
/// output is debugged at several dollars a mistake.</para>
/// </summary>
public class AnswerScorerTests
{
    private const string Passage =
        "Appamādo amatapadaṃ, pamādo maccuno padaṃ;\nappamattā na mīyanti, ye pamattā yathā matā.";

    private static CitationRef Citation(string reference = "paragraph 21 (kn2)", int page = 17) =>
        new("s0502m.mul.xml", "Dhammapadapāḷi", reference,
            new[] { new SnippetPageRef(PageEdition.Vri, 1, page) });

    // ---- Marker discipline -------------------------------------------------------------------------------

    [Fact]
    public void Well_marked_pali_scores_clean()
    {
        var answer = "The phrase [[appamādo amatapadaṃ]] opens the chapter, and [[pamādo]] is its opposite.";

        var score = AnswerScorer.Score(answer, Passage, Citation());

        Assert.Equal(2, score.Quotes);
        Assert.Empty(score.UnmarkedPali);
        Assert.True(score.Clean);
    }

    [Fact]
    public void Inline_pali_left_outside_the_markers_is_caught()
    {
        // The measurement that decides whether v1.1 script conversion can be enabled per model: a model that
        // marks block quotes but leaves inline terms bare gets its verse converted and its vocabulary left in
        // Latin — the worse half. Observed exactly this on gemma4 before the prompt was fixed.
        var answer = "The verse [[appamādo amatapadaṃ]] turns on *pamādo*, the opposite of appamāda.";

        var score = AnswerScorer.Score(answer, Passage, Citation());

        Assert.Equal(new[] { "appamāda", "pamādo" }, score.UnmarkedPali);
        Assert.False(score.Clean);
    }

    [Fact]
    public void Marked_pali_is_not_reported_as_unmarked()
    {
        // The trap: after stripping, correctly marked Pāli is indistinguishable from unmarked Pāli, so a
        // scorer reading the rendered text would report every well-behaved model as failing.
        var score = AnswerScorer.Score("[[appamādo amatapadaṃ]]", Passage, Citation());

        Assert.Empty(score.UnmarkedPali);
    }

    [Fact]
    public void An_unbalanced_marker_is_counted()
    {
        var score = AnswerScorer.Score("He said [[appamādo and stopped.", Passage, Citation());

        Assert.Equal(1, score.UnbalancedMarkers);
        Assert.False(score.Clean);
    }

    // ---- Grounding ---------------------------------------------------------------------------------------

    [Fact]
    public void A_quote_the_passage_does_not_contain_is_flagged()
    {
        // The strongest mechanical signal for answering from training data: the model quoted Pāli it was never
        // given. §6's characteristic failure, caught without a human reading the answer.
        var answer = "Compare [[manopubbaṅgamā dhammā manoseṭṭhā manomayā]], which opens the collection.";

        var score = AnswerScorer.Score(answer, Passage, Citation());

        Assert.Single(score.QuotesNotInPassage);
        Assert.Contains("manopubbaṅgamā", score.QuotesNotInPassage[0]);
    }

    [Fact]
    public void A_quote_from_the_passage_is_not_flagged_over_whitespace()
    {
        // The passage carries a newline where the answer has a space; an un-normalized comparison would report
        // a quote that is plainly present.
        var answer = "It reads [[maccuno padaṃ; appamattā na mīyanti]].";

        Assert.Empty(AnswerScorer.Score(answer, Passage, Citation()).QuotesNotInPassage);
    }

    [Fact]
    public void A_single_word_stem_is_not_treated_as_an_ungrounded_quote()
    {
        // A model legitimately names the stem for an inflected form in the text. Flagging that would bury the
        // signal that matters — a multi-word quotation the passage does not contain.
        var answer = "The stem is [[appamāda]], appearing here as [[appamādo]].";

        Assert.Empty(AnswerScorer.Score(answer, Passage, Citation()).QuotesNotInPassage);
    }

    [Fact]
    public void A_quoted_variant_reading_from_the_apparatus_is_grounded()
    {
        // Found by a live run: gpt-oss quoted "amataṃ padaṃ", the sī/syā variant at Dhp 21, and the scorer
        // called it invention. The Translate preset explicitly asks the model to say which reading it followed,
        // so scoring the apparatus as ungrounded penalises doing what it was told.
        var bundle = BundleWithApparatus();
        var answer = "Some editions read [[amataṃ padaṃ]] as two words; I followed the base text.";

        var score = AnswerScorer.Score(answer, AnswerScorer.QuotableText(bundle), Citation());

        Assert.Empty(score.QuotesNotInPassage);
    }

    private static AiContextBundle BundleWithApparatus()
    {
        var notes = new[] { new ApparatusNote(12, "amataṃ padaṃ (sī, syā)", "amataṃ padaṃ", "sī, syā") };
        var pages = new[] { new SnippetPageRef(PageEdition.Vri, 1, 17) };
        var passage = new CST.Tools.PassageResult(
            "s0502m.mul.xml", "paragraph 21 (kn2)", Passage, pages, 21, "kn2", null, null, notes.Length, notes);

        return new AiContextBundle(
            AiTask.Translate, "English", null, passage, null, Array.Empty<LemmaEntry>(),
            new BookContext("s0502m.mul.xml", "Dhammapadapāḷi", Pitaka.Sutta, CommentaryLevel.Mula),
            Citation(), new Provenance("test", null),
            new BudgetReport(Array.Empty<BundlePart>(), 10, 1));
    }

    // ---- Invented references -----------------------------------------------------------------------------

    [Fact]
    public void A_reference_the_citation_does_not_support_is_flagged()
    {
        var answer = "This echoes paragraph 183, the summary of the teaching.";

        var score = AnswerScorer.Score(answer, Passage, Citation());

        Assert.Contains("paragraph 183", score.UnsupportedReferences[0]);
    }

    [Fact]
    public void A_reference_matching_the_citation_is_accepted_whatever_it_is_called()
    {
        // The model writes "verse 21" where the citation says "paragraph 21". Same thing; comparing by number
        // rather than by phrasing is what stops the check crying wolf.
        var score = AnswerScorer.Score("As verse 21 has it...", Passage, Citation());

        Assert.Empty(score.UnsupportedReferences);
    }

    [Fact]
    public void Everything_inside_a_cited_range_is_supported()
    {
        // Since #602 the citation names a range when the window spans one. A model citing paragraph 33 of
        // "paragraphs 21-45" is citing what it was actually given.
        var score = AnswerScorer.Score(
            "Paragraph 33 develops the point.", Passage, Citation("paragraphs 21-45 (kn2)"));

        Assert.Empty(score.UnsupportedReferences);
    }

    [Fact]
    public void A_page_number_from_the_citation_is_supported()
    {
        Assert.Empty(AnswerScorer.Score("See p. 17.", Passage, Citation()).UnsupportedReferences);
    }

    [Fact]
    public void Ordinary_numbers_in_prose_do_not_trip_the_reference_check()
    {
        // A check that fires on "four noble truths" or "the 12 links" gets switched off, and then it protects
        // nothing. Narrowness is the feature.
        var answer = "There are 4 lines here, and 12 syllables in the first.";

        Assert.Empty(AnswerScorer.Score(answer, Passage, Citation()).UnsupportedReferences);
    }

    // ---- Terminology and case signatures -----------------------------------------------------------------

    [Fact]
    public void Discouraged_terms_are_counted_never_rewritten()
    {
        // §10: string surgery on model prose produces worse artifacts than the term it removes, cannot handle
        // inflected or compounded forms, and rewrites third-party text the app already labels as generated.
        var answer = "A widespread notion in the tradition, and a widespread teaching.";

        var score = AnswerScorer.Score(answer, Passage, Citation(), discouragedTerms: new[] { "widespread" });

        Assert.Equal("widespread (2)", Assert.Single(score.TerminologyLapses));
    }

    [Fact]
    public void A_term_does_not_fire_inside_an_unrelated_word()
    {
        var score = AnswerScorer.Score(
            "The pathway is clear.", Passage, Citation(), discouragedTerms: new[] { "path" });

        // "pathway" IS a path-prefixed word, so the suffix rule catches inflections; "footpath" must not.
        Assert.Empty(AnswerScorer.Score("A footpath.", Passage, Citation(),
            discouragedTerms: new[] { "path" }).TerminologyLapses);
        Assert.Single(score.TerminologyLapses);
    }

    [Fact]
    public void A_case_failure_signature_is_reported_when_it_matches()
    {
        // Case 4: matā (dead) misread as mātā (mother) — two models did this independently.
        var answer = "those who are heedless are as a mother";

        var score = AnswerScorer.Score(
            answer, Passage, Citation(), failureSignatures: new[] { @"as (a|the) mother", "as they think" });

        Assert.Equal(@"as (a|the) mother", Assert.Single(score.FailureSignatures));
    }

    [Fact]
    public void An_invalid_signature_is_surfaced_rather_than_swallowed()
    {
        // Silently skipping a broken pattern turns a case into one that can never fail — which looks exactly
        // like a case every model passes.
        var score = AnswerScorer.Score("anything", Passage, Citation(), failureSignatures: new[] { "([unclosed" });

        Assert.Contains("[invalid pattern]", Assert.Single(score.FailureSignatures));
    }

    // ---- Degenerate input --------------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_empty_answer_scores_without_throwing(string? answer)
    {
        // #601's case reaches the harness as an empty string; it must produce a row, not an exception.
        var score = AnswerScorer.Score(answer, Passage, Citation());

        Assert.Equal(0, score.Quotes);
        Assert.True(score.Clean);   // nothing to flag — the empty-answer failure is the orchestrator's to name
    }
}
