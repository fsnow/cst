using System.Text;
using CST.Avalonia.Services.Ai;
using CST.Conversion;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// The selection pipeline. (#581, AI_SURFACE_B.md §3.1)
///
/// <para>The selection is the one bundler input scraped from the DOM: it arrives in the user's display script,
/// through a <c>document.title</c> round trip, with a timeout. Each of those is a way for the words the user
/// highlighted to be silently dropped, and each has a case here.</para>
/// </summary>
public class SelectionPipelineTests
{
    // Devanagari for "appamādo amatapadaṃ" — the opening of Dhp 21.
    private const string Devanagari = "अप्पमादो अमतपदं";
    private const string Latin = "appamādo amatapadaṃ";

    private const string Window =
        "Appamādo amatapadaṃ, pamādo maccuno padaṃ;\nappamattā na mīyanti, ye pamattā yathā matā.";

    [Fact]
    public void A_non_latin_selection_is_converted()
    {
        // Not a refinement. Unconverted, every dictionary and lemma lookup misses and the window match fails,
        // which makes the two grammatical presets work for Latin-script readers only — the opposite of the
        // reader who prompted surface B.
        Assert.Equal(Latin, SelectionPipeline.Normalize(Devanagari, Script.Devanagari));
    }

    [Fact]
    public void A_converted_selection_is_found_in_the_window()
    {
        // The end-to-end point of conversion: a Devanagari reader's selection locates in a Latin passage.
        var normalized = SelectionPipeline.Normalize(Devanagari, Script.Devanagari)!;

        Assert.True(SelectionPipeline.IsWithin(normalized, Window));
    }

    [Fact]
    public void A_decomposed_selection_still_matches_a_composed_window()
    {
        // ScriptConverter emits composed Latin, so both sides normally agree — but a decomposed selection
        // (a + U+0304 rather than U+0101) is ORDINALLY unequal, and the symptom would be a bundle reporting
        // "selection not found in the passage window": a false diagnostic from the component whose whole job is
        // faithful diagnostics.
        var decomposed = Latin.Normalize(NormalizationForm.FormD);
        Assert.NotEqual(Latin, decomposed);          // the failure this guards is real

        var normalized = SelectionPipeline.Normalize(decomposed, Script.Latin)!;

        Assert.Equal(Latin, normalized);
        Assert.True(SelectionPipeline.IsWithin(normalized, Window));
    }

    [Fact]
    public void A_selection_straddling_a_line_break_matches_across_it()
    {
        // Selecting across a paragraph or line boundary drags the break in with it. The window renders that
        // break differently from the DOM, so an un-collapsed comparison misses on a selection that is plainly
        // present.
        var straddling = "  maccuno   padaṃ;\n  appamattā \t na mīyanti  ";

        var normalized = SelectionPipeline.Normalize(straddling, Script.Latin)!;

        Assert.Equal("maccuno padaṃ; appamattā na mīyanti", normalized);
        Assert.True(SelectionPipeline.IsWithin(normalized, Window));
    }

    [Fact]
    public void Matching_ignores_case_because_the_corpus_capitalises_programmatically()
    {
        // "Appamādo" heads the window; the same word mid-sentence is lowercase. A reader selecting one and the
        // renderer showing the other must not count as a miss.
        var normalized = SelectionPipeline.Normalize("APPAMĀDO AMATAPADAṂ", Script.Latin)!;

        Assert.True(SelectionPipeline.IsWithin(normalized, Window));
    }

    [Fact]
    public void A_selection_absent_from_the_window_is_reported_as_absent()
    {
        var normalized = SelectionPipeline.Normalize("dhammā manopubbaṅgamā", Script.Latin)!;

        Assert.False(SelectionPipeline.IsWithin(normalized, Window));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t ")]
    public void Nothing_selected_normalizes_to_nothing(string? raw)
    {
        // The caller distinguishes this from "could not read the selection"; the pipeline's job is only to say
        // there is no usable text.
        Assert.Null(SelectionPipeline.Normalize(raw, Script.Latin));
    }

    [Fact]
    public void Normalizing_is_idempotent()
    {
        // The bundler re-runs it rather than trusting its caller, which is only safe if it is.
        var once = SelectionPipeline.Normalize(Devanagari, Script.Devanagari)!;

        Assert.Equal(once, SelectionPipeline.Normalize(once, Script.Latin));
    }

    [Fact]
    public void An_empty_window_matches_nothing_rather_than_everything()
    {
        Assert.False(SelectionPipeline.IsWithin("appamādo", string.Empty));
    }

    // ---- Punctuation (#641) ----------------------------------------------------------------------

    [Fact]
    public void A_selection_spanning_a_sentence_break_is_found_across_the_danda()
    {
        // The reported case, and the one that made the panel unusable: the reader selected the first paragraph
        // of a book, got a correct translation, AND a notice saying the selection was not found. The window
        // carried the danda (U+0964) where the selection carried an ASCII full stop, so a selection covering
        // one sentence boundary — which is most of them — reported as absent.
        var window = "appamādo amatapadaṃ\u0964 pamādo maccuno padaṃ";
        var selection = SelectionPipeline.Normalize("appamādo amatapadaṃ. pamādo maccuno", Script.Latin)!;

        Assert.True(SelectionPipeline.IsWithin(selection, window));
    }

    [Theory]
    // Every script this app renders brings its own sentence and phrase marks. A table of known marks would
    // have fixed the danda and left each of these to be found the same way — by a reader being told that the
    // text in front of them is not there.
    // Escaped rather than pasted: these marks are visually near-identical to each other and to ASCII bars, so
    // a literal here would be unreviewable — the house rule for script-facing code.
    [InlineData("\u0964")]     // Devanagari danda
    [InlineData("\u0965")]     // Devanagari double danda
    [InlineData("\u104A")]     // Myanmar little section
    [InlineData("\u104B")]     // Myanmar section
    [InlineData("\u17D4")]     // Khmer khan
    [InlineData("\u0F0D")]     // Tibetan shad
    [InlineData("\u0E5A")]     // Thai angkhankhu
    [InlineData(".")]
    [InlineData(";")]
    [InlineData(",")]
    public void Sentence_marks_do_not_decide_whether_a_selection_is_present(string mark)
    {
        var window = $"appamādo amatapadaṃ{mark} pamādo maccuno padaṃ";
        var selection = SelectionPipeline.Normalize("amatapadaṃ, pamādo", Script.Latin)!;

        Assert.True(SelectionPipeline.IsWithin(selection, window));
    }

    [Fact]
    public void The_elision_apostrophe_matches_in_either_of_its_forms()
    {
        // Latin Pāli writes elision with an apostrophe, and the DOM and the passage reader do not agree on
        // which one — U+2019 from a copy, ASCII from the renderer, or neither.
        var window = "so\u2019haṃ vadāmi";
        var selection = SelectionPipeline.Normalize("so'haṃ vadāmi", Script.Latin)!;

        Assert.True(SelectionPipeline.IsWithin(selection, window));
    }

    [Fact]
    public void Folding_punctuation_does_not_make_an_absent_selection_present()
    {
        // The guard on the fix: dropping punctuation must not collapse the comparison into "close enough".
        // Different words are still different words.
        var selection = SelectionPipeline.Normalize("dhammā manopubbaṅgamā", Script.Latin)!;

        Assert.False(SelectionPipeline.IsWithin(selection, Window));
    }

    [Fact]
    public void A_selection_of_nothing_but_punctuation_is_not_found_everywhere()
    {
        // Folded, it is the empty string — which Contains reports as present in anything. A reader who
        // fumbles a drag and catches one danda must not be told the model saw their selection.
        var selection = SelectionPipeline.Normalize(".\u0964 ;", Script.Latin)!;

        Assert.False(SelectionPipeline.IsWithin(selection, Window));
    }

    [Fact]
    public void Diacritics_survive_the_fold()
    {
        // Folding keeps combining marks deliberately: a Latin form with no precomposed codepoint would
        // otherwise lose its diacritic and match a different word. "pāda" and "pada" are different words.
        Assert.False(SelectionPipeline.IsWithin(SelectionPipeline.Normalize("pādo", Script.Latin)!, "pado"));
        Assert.False(SelectionPipeline.IsWithin(SelectionPipeline.Normalize("pado", Script.Latin)!, "pādo"));
    }
}
