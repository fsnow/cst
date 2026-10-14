using System.Collections.Generic;
using CST.Conversion;
using Xunit;

namespace CST.Avalonia.Tests.Conversion;

/// <summary>
/// #1057: a consonant before niggahita or visarga keeps its inherent a in Cyrillic. From 2025-10-21 until the
/// fix, niggahita suppressed the a, so buddham was written without the a before its niggahita. The expected
/// strings are what CST4 and Tipitaka.org's Deva2Cyrl write. All non-ASCII text here is written as escapes.
/// </summary>
public class CyrillicInherentATests
{
    public static IEnumerable<object[]> Cases => new[]
    {
        // buddham
        new object[] { "\u092C\u0941\u0926\u094D\u0927\u0902",
                       "\u0431\u0323\u0443\u0434\u0307\u0323\u0434\u0307\u0445\u0430\u043C\u0323" },
        // dhammam
        new object[] { "\u0927\u092E\u094D\u092E\u0902",
                       "\u0434\u0307\u0445\u0430\u043C\u043C\u0430\u043C\u0323" },
        // sangham: niggahita after a consonant mid-word and at the end
        new object[] { "\u0938\u0902\u0918\u0902",
                       "\u0441\u0430\u043C\u0323\u0433\u0445\u0430\u043C\u0323" },
        // pindam
        new object[] { "\u092A\u093F\u0923\u094D\u0921\u0902",
                       "\u0431\u0438\u043D\u0323\u0434\u0323\u0430\u043C\u0323" },
        // kah: visarga after a consonant (#1049 writes visarga as kh with dot below)
        new object[] { "\u0915\u0903", "\u0433\u0430\u0445\u0323" },
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
