using System;
using System.IO;
using System.Linq;
using CST.Conversion;
using Xunit;
using Xunit.Abstractions;

namespace CST.Avalonia.Tests.Conversion;

/// <summary>
/// Oracle test for #86: the optimized <see cref="Deva2Ipe.Convert"/> must be byte-identical to the frozen
/// readable <see cref="Deva2Ipe.ConvertReference"/> across the real corpus (all 217 Devanagari books).
/// Reads book text from disk so no Devanagari glyphs appear in this source file.
/// </summary>
public class Deva2IpeEquivalenceTests
{
    private readonly ITestOutputHelper _out;
    public Deva2IpeEquivalenceTests(ITestOutputHelper o) => _out = o;

    private static string XmlDir =>
        Environment.GetEnvironmentVariable("CST_XML_DIR")
        ?? Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "/Users/fsnow",
                        "Library/Application Support/CSTReader/xml");

    [Fact]
    public void FastConvert_MatchesReference_OverAllBooks()
    {
        var dir = XmlDir;
        Assert.True(Directory.Exists(dir), $"xml dir not found: {dir}");
        var files = Directory.GetFiles(dir, "*.xml").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Assert.True(files.Length > 0, "no book xml files found");

        int checked_ = 0;
        long chars = 0;
        foreach (var f in files)
        {
            var deva = File.ReadAllText(f); // UTF-16-LE corpus; BOM honored
            var fast = Deva2Ipe.Convert(deva);
            var reference = Deva2Ipe.ConvertReference(deva);
            if (!string.Equals(fast, reference, StringComparison.Ordinal))
            {
                int at = FirstDiff(fast, reference);
                Assert.Fail(
                    $"Mismatch in {Path.GetFileName(f)} at index {at} (lenF={fast.Length}, lenR={reference.Length})\n" +
                    $"  fast: [{Snippet(fast, at)}]\n" +
                    $"  ref : [{Snippet(reference, at)}]");
            }
            checked_++;
            chars += deva.Length;
        }
        _out.WriteLine($"Deva2Ipe.Convert == ConvertReference across {checked_} books ({chars:N0} chars), byte-identical.");
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
