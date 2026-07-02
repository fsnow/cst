using System;
using System.Globalization;
using CST.Conversion;
using Xunit;

namespace CST.Avalonia.Tests.Conversion;

/// <summary>
/// CORE-4: Pāli case folding on the conversion hot paths must be culture-invariant. Under the Turkish
/// (tr) / Azeri (az) locales the default ToLower/ToUpper map I and i through the dotted/dotless-i letters
/// (dotless small i U+0131 / dotted capital I U+0130), which would corrupt Latin to IPE conversion
/// (search silently fails) and Latin capitalization (wrong glyph). These tests force tr-TR and assert the
/// ASCII mapping still holds. Non-Latin characters are written as \uXXXX escapes per repo convention.
/// </summary>
public class CultureInvariantCasingTests
{
    private const char DotlessSmallI = '\u0131';
    private const char DottedCapitalI = '\u0130';

    private static void InTurkishCulture(Action body)
    {
        var prev = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        try { body(); }
        finally { CultureInfo.CurrentCulture = prev; }
    }

    [Fact]
    public void Latn2Ipe_Convert_CapitalI_FoldsToAscii_UnderTurkishCulture()
    {
        InTurkishCulture(() =>
        {
            // "Iti" must lower-case to ASCII "iti", not the dotless-i form (starts U+0131, no IPE mapping).
            Assert.Equal(Latn2Ipe.Convert("iti"), Latn2Ipe.Convert("Iti"));
            Assert.DoesNotContain(DotlessSmallI, Latn2Ipe.Convert("Iti"));
        });
    }

    [Fact]
    public void Latn2Ipe_Convert_MatchesFrozenReference_UnderTurkishCulture()
    {
        // The #86 oracle equivalence (Convert == ConvertReference) must hold even in a hostile locale.
        InTurkishCulture(() =>
        {
            foreach (var s in new[] { "Iti", "BUDDHA", "Dhammo", "Idani", "III" })
                Assert.Equal(Latn2Ipe.ConvertReference(s), Latn2Ipe.Convert(s));
        });
    }

    [Fact]
    public void ScriptConverter_ToTitleCase_UsesAsciiI_UnderTurkishCulture()
    {
        InTurkishCulture(() =>
        {
            Assert.Equal("Iti", ScriptConverter.ToTitleCase("iti"));
            Assert.DoesNotContain(DottedCapitalI, ScriptConverter.ToTitleCase("iti"));
        });
    }

    [Fact]
    public void Deva2Latn_ConvertBook_CapitalizesWithAsciiI_UnderTurkishCulture()
    {
        // Devanagari "iti" (independent-i U+0907 + ta U+0924 + i-sign U+093F) at paragraph start. The
        // capitalized first letter must be ASCII 'I', never the dotted '\u0130' the Turkish ToUpper produces.
        var devaIti = "\u0907\u0924\u093F";
        InTurkishCulture(() =>
        {
            var html = Deva2Latn.ConvertBook($"<p rend=\"bodytext\">{devaIti}</p>");
            Assert.Contains("Iti", html);
            Assert.DoesNotContain(DottedCapitalI.ToString(), html);
        });
    }
}
