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

    /// <summary>
    /// A selection must not carry Devanāgarī punctuation into otherwise-Latin text. (#942)
    ///
    /// <para><b>[fsnow]</b>, running the Beta 6 runbook: <i>"I opened Apadanapali-1 and selected the third
    /// verse. I see in the context the single danda and double danda in the Latin-script Pali, not semicolon
    /// and period."</i></para>
    ///
    /// <para>Both marks flatten to a period. That loses the gāthā distinction the passage window keeps — a
    /// single daṇḍa reads ";" there, because it ends a pada — and that is a deliberate call:
    /// <i>"I am fine with both single and double danda being converted to period in this case. I don't think
    /// it will materially affect the LLMs ability to understand the text."</i></para></summary>
    [Theory]
    [InlineData("gacchati\u0964", "gacchati.")]
    [InlineData("gacchati\u0965", "gacchati.")]
    [InlineData("eka\u0964 dve\u0965", "eka. dve.")]
    public void A_danda_reaches_the_model_as_a_period(string raw, string expected)
    {
        Assert.Equal(expected, SelectionPipeline.Normalize(raw, Script.Latin));
    }

    /// <summary>
    /// The reported path: reading in a non-Latin script, where the selection arrives as source text and only
    /// its LETTERS were being converted.
    ///
    /// <para>This is why the check passes in Latin and fails everywhere else — a Latin reader's selection is
    /// copied from what the reader itself rendered, through <c>ConvertBook</c>, which applies the
    /// markup-driven daṇḍa rules.</para></summary>
    [Fact]
    public void A_selection_read_in_Devanagari_carries_no_Devanagari_punctuation()
    {
        // गच्छति। — "gacchati" followed by a single daṇḍa.
        const string deva = "\u0917\u091A\u094D\u091B\u0924\u093F\u0964";

        var normalized = SelectionPipeline.Normalize(deva, Script.Devanagari)!;

        Assert.DoesNotContain('\u0964', normalized);
        Assert.DoesNotContain('\u0965', normalized);
        Assert.EndsWith(".", normalized);
    }

    [Fact]
    public void Normalizing_is_idempotent()
    {
        // The bundler re-runs it rather than trusting its caller, which is only safe if it is.
        var once = SelectionPipeline.Normalize(Devanagari, Script.Devanagari)!;

        Assert.Equal(once, SelectionPipeline.Normalize(once, Script.Latin));
    }

}
