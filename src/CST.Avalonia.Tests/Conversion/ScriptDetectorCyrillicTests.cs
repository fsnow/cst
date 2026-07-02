using CST.Conversion;
using Xunit;

namespace CST.Avalonia.Tests.Conversion;

/// <summary>
/// CORE-3: Cyrillic input must be script-detected (U+0400–04FF) and its combining diacritical marks
/// (U+0300–036F, used by the Cyrillic Pāli scheme) must join the surrounding run rather than split it —
/// otherwise Cyrillic search/dictionary input misroutes to Latin and yields zero results. Includes a Thai
/// regression confirming the rewrite's four new input parsers (Thai/Telugu/Tibetan/Khmer) already work.
/// Non-Latin characters are written as \uXXXX escapes per repo convention.
/// </summary>
public class ScriptDetectorCyrillicTests
{
    [Theory]
    [InlineData(0x0400)] // start of the Cyrillic block
    [InlineData(0x0434)] // Cyrillic 'de'
    [InlineData(0x04FF)] // end of the Cyrillic block
    public void GetScript_ClassifiesCyrillic(int codepoint)
        => Assert.Equal(Script.Cyrillic, ScriptDetector.GetScript((char)codepoint));

    [Theory]
    [InlineData(0x0300)] // start of Combining Diacritical Marks
    [InlineData(0x0307)] // combining dot above (used by the Cyrillic scheme)
    [InlineData(0x0303)] // combining tilde
    [InlineData(0x036F)] // end of the block
    public void GetScript_CombiningMarksAreUnknown_SoTheyJoinTheRun(int codepoint)
        => Assert.Equal(Script.Unknown, ScriptDetector.GetScript((char)codepoint));

    [Fact]
    public void Any2Deva_ConvertsCyrillicSyllable()
    {
        // Cyrl2Deva maps U+0434 (Cyrillic de) + U+0307 (combining dot-above) to Devanagari 'ta' (U+0924).
        Assert.Equal("\u0924", Any2Deva.Convert("\u0434\u0307"));
    }

    [Fact]
    public void Any2Ipe_CyrillicMatchesDevanagari()
    {
        // The Cyrillic 'ta' syllable and the Devanagari 'ta' must produce the same IPE term (so a Cyrillic
        // query would hit the same index entries). Before the fix the Cyrillic form misrouted to Latin.
        Assert.Equal(Any2Ipe.Convert("\u0924"), Any2Ipe.Convert("\u0434\u0307"));
    }

    [Fact]
    public void Any2Ipe_ThaiMatchesDevanagari_Regression()
    {
        // Thai 'a' (U+0E2D) maps to Devanagari 'a' (U+0905); both must yield the same IPE. Confirms the
        // Thai input parser (added in the rewrite, not in CST4) is wired — the SRCH-9 concern was stale.
        Assert.Equal(Any2Ipe.Convert("\u0905"), Any2Ipe.Convert("\u0E2D"));
    }
}
