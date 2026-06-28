using CST.Conversion;
using Xunit;

namespace CST.Avalonia.Tests.Conversion;

/// <summary>
/// #75: ScriptConverter.Convert now dispatches through (from,to) tables - a direct map plus a
/// Devanagari-pivot fallback - instead of a 167-line if/else. These guard that the dispatch routes
/// exactly as before (including the subtlety that Latin/Unknown pivot via the auto-detecting Any2Deva).
/// </summary>
public class ScriptConverterDispatchTests
{
    private const string Word = "dhammacakkappavattana";

    [Fact]
    public void Identity_ReturnsInputUnchanged()
        => Assert.Equal("abc123", ScriptConverter.Convert("abc123", Script.Thai, Script.Thai));

    [Fact]
    public void Direct_MatchesUnderlyingConverter()
    {
        Assert.Equal(Latn2Ipe.Convert(Word), ScriptConverter.Convert(Word, Script.Latin, Script.Ipe));
        var ipe = Latn2Ipe.Convert(Word);
        Assert.Equal(Ipe2Deva.Convert(ipe), ScriptConverter.Convert(ipe, Script.Ipe, Script.Devanagari));
    }

    [Fact]
    public void IpePivot_ToNonDevaNonLatin_GoesViaDevanagari()
    {
        var ipe = Latn2Ipe.Convert(Word);
        Assert.Equal(Deva2Thai.Convert(Ipe2Deva.Convert(ipe)),
                     ScriptConverter.Convert(ipe, Script.Ipe, Script.Thai));
    }

    [Fact]
    public void LatinPivot_ToOther_UsesAny2DevaNotLatn2Deva()
    {
        // Latin -> Bengali pivots via the auto-detecting Any2Deva (original dispatch's behavior),
        // not the direct Latn2Deva.
        Assert.Equal(Deva2Beng.Convert(Any2Deva.Convert(Word)),
                     ScriptConverter.Convert(Word, Script.Latin, Script.Bengali));
    }

    [Fact]
    public void ReversePivot_BengaliToThai_GoesViaDevanagari()
    {
        var beng = ScriptConverter.Convert(Latn2Deva.Convert(Word), Script.Devanagari, Script.Bengali);
        Assert.Equal(Deva2Thai.Convert(Beng2Deva.Convert(beng)),
                     ScriptConverter.Convert(beng, Script.Bengali, Script.Thai));
    }

    [Fact]
    public void ToTitleCase_AppliedOnlyForLatinOutput()
    {
        var ipe = Latn2Ipe.Convert("dhamma sangha");
        Assert.Equal(ScriptConverter.ToTitleCase(Ipe2Latn.Convert(ipe)),
                     ScriptConverter.Convert(ipe, Script.Ipe, Script.Latin, true));
        // non-Latin output ignores toTitleCase
        Assert.Equal(ScriptConverter.Convert(ipe, Script.Ipe, Script.Devanagari, false),
                     ScriptConverter.Convert(ipe, Script.Ipe, Script.Devanagari, true));
    }

    [Fact]
    public void UnsupportedOutput_ReturnsEmpty_PreservingOriginalBehavior()
        => Assert.Equal("", ScriptConverter.Convert(Latn2Deva.Convert(Word), Script.Devanagari, Script.Unknown));
}
