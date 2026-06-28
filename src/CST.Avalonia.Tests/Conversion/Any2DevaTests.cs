using CST.Conversion;
using Xunit;

namespace CST.Avalonia.Tests.Conversion;

/// <summary>
/// Any2Deva must mirror Any2Ipe's script detection. In particular, zero-width joiners (ZWJ/ZWNJ) are
/// classified as Unknown so the run-splitter keeps them with the surrounding script instead of treating
/// them as Latin and breaking a non-Latin run mid-conjunct. (Dormant in the current app - live callers
/// pass Latin - but exercised by the auto-detect path the dictionary feature (#25) will use.)
/// </summary>
public class Any2DevaTests
{
    [Theory]
    [InlineData(0x200C)] // ZWNJ
    [InlineData(0x200D)] // ZWJ
    public void GetScript_JoinersAreUnknown(int codepoint)
        => Assert.Equal(Script.Unknown, Any2Deva.GetScript((char)codepoint));

    [Theory]
    [InlineData(0x0915, Script.Devanagari)] // Devanagari ka
    [InlineData(0x0985, Script.Bengali)]    // Bengali a
    [InlineData(0x0D85, Script.Sinhala)]    // Sinhala a
    [InlineData('a', Script.Latin)]
    public void GetScript_ClassifiesScripts(int codepoint, Script expected)
        => Assert.Equal(expected, Any2Deva.GetScript((char)codepoint));
}
