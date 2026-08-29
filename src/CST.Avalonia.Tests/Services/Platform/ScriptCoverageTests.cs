using System.Globalization;
using System.Linq;
using System.Text;
using CST.Avalonia.Services.Platform;
using CST.Conversion;
using Xunit;

namespace CST.Avalonia.Tests.Services.Platform;

/// <summary>
/// What "this font supports this script" is allowed to mean. (#29)
///
/// <para>The font enumeration itself needs a live Avalonia <c>FontManager</c> and so is exercised by running
/// the app; this half needs nothing, and it is the half where a mistake is invisible. Derive too few
/// codepoints and the filter admits fonts that render tofu - which is the bug being fixed - while every test
/// that merely counts fonts still passes.</para>
///
/// <para>Codepoints are written as integers rather than escaped string literals on purpose: a literal is at
/// the mercy of the file's encoding, and this file is the reference for what the diacritics ARE.</para>
/// </summary>
public class ScriptCoverageTests
{
    // The Pāli diacritics in the probe phrase. A font that lacks these cannot set Pāli in Latin script, which
    // is the single most likely wrong choice a user can make on a machine full of ASCII-only fonts.
    private const int ALongA = 0x0101;      // a with macron
    private const int TUnderdot = 0x1E6D;   // t with dot below
    private const int MUnderdot = 0x1E43;   // m with dot below

    // Sinhala's al-lakuna and the two joiners that can follow it. Deva2Sinh maps every virama to al-lakuna +
    // ZWNJ, then rewrites that joiner to ZWJ before ya, before ra, and between kk - so which joiner appears in
    // any given phrase is a property of the phrase.
    private const int SinhalaAlLakuna = 0x0DCA;
    private const int Zwnj = 0x200C;
    private const int Zwj = 0x200D;

    [Theory]
    [InlineData(Script.Latin)]
    [InlineData(Script.Devanagari)]
    [InlineData(Script.Bengali)]
    [InlineData(Script.Cyrillic)]
    [InlineData(Script.Gujarati)]
    [InlineData(Script.Gurmukhi)]
    [InlineData(Script.Kannada)]
    [InlineData(Script.Khmer)]
    [InlineData(Script.Malayalam)]
    [InlineData(Script.Myanmar)]
    [InlineData(Script.Sinhala)]
    [InlineData(Script.Telugu)]
    [InlineData(Script.Thai)]
    [InlineData(Script.Tibetan)]
    public void Every_readable_script_demands_something_of_a_font(Script script)
    {
        // An empty list means "offer every font", so a script that silently produced one would regress to
        // exactly the unfiltered behaviour this exists to remove - without failing anything else.
        Assert.NotEmpty(ScriptCoverage.CodepointsFor(script));
    }

    [Theory]
    [InlineData(Script.Unknown)]
    [InlineData(Script.Ipe)]
    public void Scripts_that_are_not_scripts_impose_no_requirement(Script script)
    {
        // Unknown is not a script and Ipe is the internal search encoding, never shown to a reader. Filtering
        // a font list for either would be inventing a requirement out of nothing.
        Assert.Empty(ScriptCoverage.CodepointsFor(script));
    }

    [Theory]
    [InlineData(Script.Latin)]
    [InlineData(Script.Devanagari)]
    [InlineData(Script.Bengali)]
    [InlineData(Script.Cyrillic)]
    [InlineData(Script.Gujarati)]
    [InlineData(Script.Gurmukhi)]
    [InlineData(Script.Kannada)]
    [InlineData(Script.Khmer)]
    [InlineData(Script.Malayalam)]
    [InlineData(Script.Myanmar)]
    [InlineData(Script.Sinhala)]
    [InlineData(Script.Telugu)]
    [InlineData(Script.Thai)]
    [InlineData(Script.Tibetan)]
    public void No_font_is_asked_to_draw_a_formatting_character(Script script)
    {
        // A joiner instructs the shaper; it is not a glyph, and a font that omits it from its cmap can still
        // render the text. Requiring one could only exclude a font that works.
        //
        // Written over every script rather than over Sinhala alone because the requirement is derived from the
        // probe phrase: change PaliSample to something containing a virama before ya or ra, and Sinhala would
        // start demanding ZWJ instead of ZWNJ - a codepoint no survey has ever checked fonts for. This fails
        // when that happens, which is the point of testing the category rather than the two known joiners.
        Assert.All(ScriptCoverage.CodepointsFor(script), cp =>
            Assert.False(Rune.GetUnicodeCategory(new Rune(cp)) == UnicodeCategory.Format,
                $"{script} would require U+{cp:X4}, a formatting character no font need draw."));
    }

    [Fact]
    public void Sinhala_requires_its_virama_but_not_the_joiner_that_follows_it()
    {
        // Sinhala is the only script whose conversion emits a joiner at all, so it is the only one where the
        // Format filter does any work today. Both halves matter: dropping the joiner must not also drop the
        // al-lakuna, which is a real mark that a Sinhala font genuinely has to draw.
        var codepoints = ScriptCoverage.CodepointsFor(Script.Sinhala);

        Assert.Contains(SinhalaAlLakuna, codepoints);
        Assert.DoesNotContain(Zwnj, codepoints);
        Assert.DoesNotContain(Zwj, codepoints);
    }

    [Fact]
    public void Latin_requires_the_Pali_diacritics_not_merely_the_alphabet()
    {
        // The whole reason Latin is filtered at all. Most fonts on any machine draw "mahasatipatthanasuttam";
        // far fewer draw it with the diacritics, and only the latter can set Pāli.
        var codepoints = ScriptCoverage.CodepointsFor(Script.Latin);

        Assert.Contains(ALongA, codepoints);
        Assert.Contains(TUnderdot, codepoints);
        Assert.Contains(MUnderdot, codepoints);
    }

    [Theory]
    [InlineData(Script.Devanagari)]
    [InlineData(Script.Myanmar)]
    [InlineData(Script.Sinhala)]
    [InlineData(Script.Tibetan)]
    [InlineData(Script.Thai)]
    [InlineData(Script.Khmer)]
    public void A_converted_script_asks_for_its_own_characters_not_ASCII(Script script)
    {
        // Guards the conversion actually happening. Were it to return the Latin text unchanged, every font
        // with a Latin alphabet would "support" Myanmar and the picker would be as useless as before.
        var codepoints = ScriptCoverage.CodepointsFor(script);

        Assert.All(codepoints, cp => Assert.True(cp >= 0x80,
            $"{script} asked for U+{cp:X4}, which is ASCII - the sample was not converted."));
    }

    [Theory]
    [InlineData(Script.Latin)]
    [InlineData(Script.Devanagari)]
    [InlineData(Script.Myanmar)]
    [InlineData(Script.Tibetan)]
    [InlineData(Script.Sinhala)]
    public void Nothing_is_asked_for_twice(Script script)
    {
        // The phrase repeats letters. Testing a font for the same codepoint several times is wasted work on
        // a path that runs across every installed font, for every script, at startup.
        var codepoints = ScriptCoverage.CodepointsFor(script);

        Assert.Equal(codepoints.Distinct().Count(), codepoints.Count);
    }

    [Theory]
    [InlineData(Script.Devanagari)]
    [InlineData(Script.Myanmar)]
    [InlineData(Script.Tibetan)]
    [InlineData(Script.Sinhala)]
    [InlineData(Script.Khmer)]
    public void The_character_used_to_ask_the_platform_is_one_that_identifies_the_script(Script script)
    {
        // This codepoint is handed to the platform's font fallback to ask what IT would use. An ASCII letter
        // would match almost every font installed and answer nothing; a combining mark asks about a character
        // that never stands alone.
        var representative = ScriptCoverage.RepresentativeCodepoint(script);

        Assert.NotNull(representative);
        Assert.True(representative >= 0x80,
            $"{script} would ask the platform about U+{representative:X4}, which is ASCII.");
        Assert.Contains(representative!.Value, ScriptCoverage.CodepointsFor(script));
    }

    [Fact]
    public void A_script_with_no_requirements_nominates_no_character_either()
    {
        // Consistency between the two halves: nothing to require means nothing to ask about.
        Assert.Null(ScriptCoverage.RepresentativeCodepoint(Script.Unknown));
        Assert.Null(ScriptCoverage.RepresentativeCodepoint(Script.Ipe));
    }

    [Fact]
    public void Latin_asks_the_platform_about_a_diacritic()
    {
        // Not "m". The point of asking is to find a font that handles Pāli, and every font handles "m".
        Assert.Equal(ALongA, ScriptCoverage.RepresentativeCodepoint(Script.Latin));
    }
}
