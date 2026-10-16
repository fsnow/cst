using System;
using System.Collections.Generic;
using System.Linq;
using CST.Conversion;
using Xunit;

namespace CST.Avalonia.Tests.Conversion;

/// <summary>
/// Odia (Oriya): Deva2Orya and Orya2Deva. The letter choices are Tipitaka.org's (VipassanaTech/tipitaka-xml#2728),
/// which follow the Odia transliteration of #2707 there. These tests pin the code points; the shared tables
/// (ConverterEquivalenceTests, SanskritLettersTests, VisibleHalantTests, AutoDetectRoundTripTests) cover the
/// rest. All non-ASCII text here is written as escapes.
/// </summary>
public class OdiaConverterTests
{
    private static readonly Func<string, string>[] ToOrya = { Deva2Orya.Convert, Deva2Orya.ConvertReference };
    private static readonly Func<string, string>[] ToDeva = { Orya2Deva.Convert, Orya2Deva.ConvertReference };

    [Fact]
    public void Ya_IsYya_EverywhereIncludingAfterAVirama()
    {
        foreach (var conv in ToOrya)
        {
            Assert.Equal("\u0B5F", conv("\u092F"));           // ya
            Assert.Equal("\u0B15\u0B4D\u0B5F", conv("\u0915\u094D\u092F"));        // kya: still YYA after the virama
        }
    }

    [Fact]
    public void Va_IsWa()
    {
        foreach (var conv in ToOrya)
            Assert.Equal("\u0B71", conv("\u0935"));
    }

    [Fact]
    public void Joiners_AreRemoved()
    {
        // kka with ZWJ, as the corpus writes it, and a ZWNJ
        foreach (var conv in ToOrya)
        {
            Assert.Equal("\u0B15\u0B4D\u0B15", conv("\u0915\u094D\u200D\u0915"));
            Assert.Equal("\u0B15\u0B4D\u0B15", conv("\u0915\u094D\u200C\u0915"));
        }
    }

    [Fact]
    public void Dandas_AbbreviationSign_AndPunctuation_PassThrough()
    {
        foreach (var conv in ToOrya)
            Assert.Equal("\u0B15, \u0964\u0965\u0970'\u2018.", conv("\u0915, \u0964\u0965\u0970'\u2018."));
    }

    [Fact]
    public void Digits_AreOdia()
    {
        foreach (var conv in ToOrya)
            Assert.Equal("\u0B66\u0B67\u0B68\u0B69\u0B6A\u0B6B\u0B6C\u0B6D\u0B6E\u0B6F", conv("\u0966\u0967\u0968\u0969\u096A\u096B\u096C\u096D\u096E\u096F"));
        foreach (var conv in ToDeva)
            Assert.Equal("\u0966\u0967\u0968\u0969\u096A\u096B\u096C\u096D\u096E\u096F", conv("\u0B66\u0B67\u0B68\u0B69\u0B6A\u0B6B\u0B6C\u0B6D\u0B6E\u0B6F"));
    }

    [Fact]
    public void Reader_AcceptsYaAndVa_AsOtherOdiaTextWritesThem()
    {
        // U+0B2F (YA) and U+0B35 (VA) are not written by Deva2Orya, but Odia typed elsewhere uses them.
        foreach (var conv in ToDeva)
        {
            Assert.Equal("\u092F", conv("\u0B2F"));
            Assert.Equal("\u0935", conv("\u0B35"));
        }
    }

    [Fact]
    public void EveryDevanagariLetter_IsMapped_AndReadsBack()
    {
        // Every letter, sign and digit the converters handle: nothing may pass through as Devanagari.
        foreach (var deva in AllMapped)
        {
            var orya = Deva2Orya.Convert(deva.ToString());
            Assert.True(orya.Length == 1 && orya[0] >= '\u0B00' && orya[0] <= '\u0B7F',
                $"U+{(int)deva:X4} -> {string.Concat(orya.Select(c => $"U+{(int)c:X4} "))}");
            foreach (var conv in ToDeva)
                Assert.Equal(deva.ToString(), conv(orya));
        }
    }

    [Fact]
    public void Sutta_Opening_ExactCodePoints()
    {
        // evam me sutam ekam samayam bhagava ...: niggahita, ya, va, a long vowel
        foreach (var conv in ToOrya)
            Assert.Equal("\u0B0F\u0B71\u0B02 \u0B2E\u0B47 \u0B38\u0B41\u0B24\u0B02 \u0B0F\u0B15\u0B02 \u0B38\u0B2E\u0B5F\u0B02 \u0B2D\u0B17\u0B71\u0B3E", conv("\u090F\u0935\u0902 \u092E\u0947 \u0938\u0941\u0924\u0902 \u090F\u0915\u0902 \u0938\u092E\u092F\u0902 \u092D\u0917\u0935\u093E"));
    }

    // U+0901-U+0903, the vowels, consonants, signs, virama and digits that Deva2Orya maps.
    private static readonly char[] AllMapped =
        new[] { 0x0901, 0x0902, 0x0903 }
        .Concat(new[] { 0x0905, 0x0906, 0x0907, 0x0908, 0x0909, 0x090A, 0x090B, 0x090C, 0x090F, 0x0910, 0x0913, 0x0914 })
        .Concat(Enumerable.Range(0x0915, 0x0939 - 0x0915 + 1).Where(c => c != 0x0929 && c != 0x0931 && c != 0x0934))
        .Concat(new[] { 0x093E, 0x093F, 0x0940, 0x0941, 0x0942, 0x0943, 0x0947, 0x0948, 0x094B, 0x094C, 0x094D })
        .Concat(Enumerable.Range(0x0966, 10))
        .Select(c => (char)c).ToArray();
}
