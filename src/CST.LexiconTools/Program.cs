using System;
using CST.LexiconTools;

// Build-time CLI. Usage:
//   cst-lexicon-tools dppn <DPPN.json> <out.db> [sourceVersion]
if (args.Length >= 1 && args[0].Equals("dppn", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("usage: cst-lexicon-tools dppn <DPPN.json> <out.db> [sourceVersion]");
        return 2;
    }
    string json = args[1], db = args[2], ver = args.Length >= 4 ? args[3] : "unknown";
    try
    {
        int n = DppnConverter.BuildLexicon(json, db, ver);
        Console.WriteLine($"DPPN → {db}: {n} entries (source {ver}).");
        return 0;
    }
    catch (Exception ex) when (ex is System.IO.IOException or System.Text.Json.JsonException or InvalidOperationException)
    {
        Console.Error.WriteLine($"DPPN conversion failed: {ex.Message}");
        return 1;
    }
}

Console.Error.WriteLine("usage: cst-lexicon-tools dppn <DPPN.json> <out.db> [sourceVersion]");
return 2;
