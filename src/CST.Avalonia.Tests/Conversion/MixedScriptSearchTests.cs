using CST.Conversion;
using Xunit;

namespace CST.Avalonia.Tests.Conversion;

/// <summary>
/// Any2Ipe / Any2Deva are designed to accept a MIXED-script string (e.g. a search query mixing Latin and
/// Devanagari) and convert each script run independently to a common normal form. This is what lets a
/// mixed-script search query be handled uniformly - the search path runs queries through Any2Ipe
/// (SearchService.Convert). These tests confirm that behavior still works and guard it. (Frank's
/// Any2Ipe/Any2Deva design intent.)
/// </summary>
public class MixedScriptSearchTests
{
    private const string Word = "dhammacakka";

    [Fact]
    public void Any2Ipe_SameWordInDifferentScripts_NormalizesToSameIpe()
    {
        var deva = ScriptConverter.Convert(Word, Script.Latin, Script.Devanagari);
        var ipe = Latn2Ipe.Convert(Word);

        Assert.NotEqual(Word, deva);                  // genuinely different scripts
        Assert.Equal(ipe, Any2Ipe.Convert(Word));     // Latin run
        Assert.Equal(ipe, Any2Ipe.Convert(deva));     // Devanagari run -> identical IPE
    }

    [Fact]
    public void Any2Ipe_MixedScriptString_SplitsRunsAndConvertsEach()
    {
        var deva = ScriptConverter.Convert(Word, Script.Latin, Script.Devanagari);
        var ipe = Latn2Ipe.Convert(Word);

        // A Latin run followed by a Devanagari run in one string: each is detected and converted, then
        // joined. Same word both ways, so the result is the same IPE twice.
        Assert.Equal(ipe + ipe, Any2Ipe.Convert(Word + deva));
    }

    [Fact]
    public void Any2Ipe_ThreeScriptsInOneString_AllConverted()
    {
        var deva = ScriptConverter.Convert(Word, Script.Latin, Script.Devanagari);
        var thai = ScriptConverter.Convert(Word, Script.Latin, Script.Thai);
        var ipe = Latn2Ipe.Convert(Word);

        // Latin + Devanagari + Thai, same word -> the same IPE three times.
        Assert.Equal(ipe + ipe + ipe, Any2Ipe.Convert(Word + deva + thai));
    }

    [Fact]
    public void Any2Deva_MixedScriptString_ConvertsEachRunToDevanagari()
    {
        var deva = Latn2Deva.Convert(Word);
        var beng = ScriptConverter.Convert(deva, Script.Devanagari, Script.Bengali);

        Assert.NotEqual(deva, beng);
        Assert.Equal(deva, Any2Deva.Convert(Word));   // Latin run -> Devanagari
        Assert.Equal(deva, Any2Deva.Convert(beng));   // Bengali run -> same Devanagari
        Assert.Equal(deva + deva, Any2Deva.Convert(Word + beng));
    }
}
