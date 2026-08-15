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

    [Fact]
    public void Normalizing_is_idempotent()
    {
        // The bundler re-runs it rather than trusting its caller, which is only safe if it is.
        var once = SelectionPipeline.Normalize(Devanagari, Script.Devanagari)!;

        Assert.Equal(once, SelectionPipeline.Normalize(once, Script.Latin));
    }

}
