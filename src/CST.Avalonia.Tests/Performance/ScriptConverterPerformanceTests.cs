using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using CST.Conversion;
using Xunit;
using Xunit.Abstractions;

namespace CST.Avalonia.Tests.Performance;

/// <summary>
/// Baseline timings for the script converters (#86). Converts a real large book (DN1, ~442K Deva chars)
/// through each hot Deva-&gt;script converter and reports throughput. Run before/after the single-pass
/// optimization to measure the win. Not an assertion test — it always "passes" and just records timings.
/// </summary>
public class ScriptConverterPerformanceTests
{
    private readonly ITestOutputHelper _out;
    public ScriptConverterPerformanceTests(ITestOutputHelper o) => _out = o;

    private static string XmlDir =>
        Environment.GetEnvironmentVariable("CST_XML_DIR")
        ?? Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "/Users/fsnow",
                        "Library/Application Support/CSTReader/xml");

    [Fact]
    public void Baseline_ConvertDN1_PerScript()
    {
        var path = Path.Combine(XmlDir, "s0101m.mul.xml"); // DN1 mula
        Assert.True(File.Exists(path), $"DN1 not found at {path}");

        // UTF-16-LE corpus; File.ReadAllText honors the BOM.
        var deva = File.ReadAllText(path);
        int chars = deva.Length;

        var converters = new (string Name, Func<string, string> Fn)[]
        {
            ("Deva2Ipe-ref", Deva2Ipe.ConvertReference), // frozen readable oracle (baseline)
            ("Deva2Ipe",  Deva2Ipe.Convert),             // optimized single-pass (#86)
            ("Deva2Latn-ref", Deva2Latn.ConvertReference),
            ("Deva2Latn", Deva2Latn.Convert),
            ("Deva2Cyrl-ref", Deva2Cyrl.ConvertReference),
            ("Deva2Cyrl", Deva2Cyrl.Convert),
            ("Deva2Sinh", Deva2Sinh.Convert),
            ("Deva2Thai", Deva2Thai.Convert),
            ("Deva2Mymr-ref", Deva2Mymr.ConvertReference),
            ("Deva2Mymr", Deva2Mymr.Convert),
        };

        const int iterations = 10;
        var results = new StringBuilder();
        results.AppendLine($"# Script converter baseline — DN1 ({chars:N0} chars), {iterations} iters, avg");
        results.AppendLine($"{"converter",-12} {"avg ms",8} {"MB/s",8}");

        foreach (var (name, fn) in converters)
        {
            var outLen = fn(deva).Length; // warm up + JIT
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++) fn(deva);
            sw.Stop();
            double avgMs = sw.Elapsed.TotalMilliseconds / iterations;
            double mbPerSec = (chars * 2.0 / 1_000_000.0) / (avgMs / 1000.0); // chars*2 ~ UTF-16 bytes
            results.AppendLine($"{name,-12} {avgMs,8:F1} {mbPerSec,8:F1}   (out {outLen:N0})");
        }

        var report = results.ToString();
        _out.WriteLine(report);
        var outFile = Path.Combine(Path.GetTempPath(), "cst-converter-baseline.txt");
        File.WriteAllText(outFile, report);
        _out.WriteLine($"(written to {outFile})");
    }
}
