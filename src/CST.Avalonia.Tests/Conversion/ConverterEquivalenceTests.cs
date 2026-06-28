using System;
using System.IO;
using System.Linq;
using CST.Conversion;
using Xunit;
using Xunit.Abstractions;

namespace CST.Avalonia.Tests.Conversion;

/// <summary>
/// Oracle tests for #86: each optimized converter's <c>Convert</c> must be byte-identical to its frozen
/// readable <c>ConvertReference</c> across the real corpus (all 217 Devanagari books). Reads book text
/// from disk, so no Devanagari glyphs appear in this source file. Add a [Fact] per converter as the
/// optimization rolls out.
/// </summary>
public class ConverterEquivalenceTests
{
    private readonly ITestOutputHelper _out;
    public ConverterEquivalenceTests(ITestOutputHelper o) => _out = o;

    private static string XmlDir =>
        Environment.GetEnvironmentVariable("CST_XML_DIR")
        ?? Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "/Users/fsnow",
                        "Library/Application Support/CSTReader/xml");

    [Fact]
    public void Deva2Ipe_FastMatchesReference()
        => AssertEquivalentOverCorpus("Deva2Ipe", Deva2Ipe.Convert, Deva2Ipe.ConvertReference);

    [Fact]
    public void Deva2Latn_FastMatchesReference()
        => AssertEquivalentOverCorpus("Deva2Latn", Deva2Latn.Convert, Deva2Latn.ConvertReference);

    [Fact]
    public void Deva2Cyrl_FastMatchesReference()
        => AssertEquivalentOverCorpus("Deva2Cyrl", Deva2Cyrl.Convert, Deva2Cyrl.ConvertReference);

    [Fact]
    public void Deva2Mymr_FastMatchesReference()
        => AssertEquivalentOverCorpus("Deva2Mymr", Deva2Mymr.Convert, Deva2Mymr.ConvertReference);

    private void AssertEquivalentOverCorpus(string name, Func<string, string> fast, Func<string, string> reference)
    {
        var dir = XmlDir;
        if (!Directory.Exists(dir))
            Assert.Skip($"XML corpus not found at {dir} (set CST_XML_DIR). Integration test.");
        var files = Directory.GetFiles(dir, "*.xml").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        if (files.Length == 0)
            Assert.Skip($"No book XML files in {dir}. Integration test.");

        int checked_ = 0;
        long chars = 0;
        foreach (var f in files)
        {
            var deva = File.ReadAllText(f); // UTF-16-LE corpus; BOM honored
            var a = fast(deva);
            var b = reference(deva);
            if (!string.Equals(a, b, StringComparison.Ordinal))
            {
                int at = FirstDiff(a, b);
                Assert.Fail(
                    $"{name} mismatch in {Path.GetFileName(f)} at index {at} (lenFast={a.Length}, lenRef={b.Length})\n" +
                    $"  fast: [{Snippet(a, at)}]\n" +
                    $"  ref : [{Snippet(b, at)}]");
            }
            checked_++;
            chars += deva.Length;
        }
        _out.WriteLine($"{name}.Convert == ConvertReference across {checked_} books ({chars:N0} chars), byte-identical.");
    }

    private static int FirstDiff(string a, string b)
    {
        int m = Math.Min(a.Length, b.Length);
        for (int i = 0; i < m; i++)
            if (a[i] != b[i]) return i;
        return m;
    }

    private static string Snippet(string s, int at)
    {
        int start = Math.Max(0, at - 12);
        int len = Math.Min(24, s.Length - start);
        return len > 0 ? s.Substring(start, len) : "(end)";
    }
}
