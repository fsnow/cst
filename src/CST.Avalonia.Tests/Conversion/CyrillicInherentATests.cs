using System.Collections.Generic;
using CST.Conversion;
using Xunit;

namespace CST.Avalonia.Tests.Conversion;

/// <summary>
/// #1057: a consonant before niggahita or visarga keeps its inherent a in Cyrillic. From 2025-10-21 until the
/// fix, niggahita suppressed the a, so buddham was written "б̣уд̣̇д̇хм̣". The expected strings are what CST4 and
/// Tipitaka.org's Deva2Cyrl write. All non-ASCII text here is written as escapes.
/// </summary>
public class CyrillicInherentATests
{
    public static IEnumerable<object[]> Cases => new[]
    {
        // buddham
        new object[] { "बुद्धं",
                       "б̣уд̣̇д̇хам̣" },
        // dhammam
        new object[] { "धम्मं",
                       "д̇хаммам̣" },
        // sangham: niggahita after a consonant mid-word and at the end
        new object[] { "संघं",
                       "сам̣гхам̣" },
        // pindam
        new object[] { "पिण्डं",
                       "бин̣д̣ам̣" },
        // kah: visarga after a consonant (Cyrillic has no visarga letter yet, so it passes through)
        new object[] { "कः", "гаः" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void InherentA_BeforeNiggahitaAndVisarga(string deva, string expected)
    {
        Assert.Equal(expected, Deva2Cyrl.Convert(deva));
        Assert.Equal(expected, Deva2Cyrl.ConvertReference(deva));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ReadsBack(string deva, string expected)
    {
        Assert.Equal(deva, Cyrl2Deva.Convert(expected));
    }
}
