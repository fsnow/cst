using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CST.Conversion;
using CST.ScriptValidation;

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            ShowHelp();
            return 0;
        }

        string command = args[0].ToLower();

        try
        {
            switch (command)
            {
                case "validate":
                case "v":
                    return RunValidation(args.Skip(1).ToArray());

                case "compare":
                case "c":
                    return await RunComparison(args.Skip(1).ToArray());

                case "analyze":
                case "a":
                    return RunAnalyze(args.Skip(1).ToArray());

                case "extract":
                case "extract-syllables":
                case "e":
                    return RunExtract(args.Skip(1).ToArray());

                default:
                    Console.WriteLine($"Unknown command: {command}");
                    Console.WriteLine("Run 'dotnet run --help' for usage information.");
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    static void ShowHelp()
    {
        Console.WriteLine(@"
=== CST Script Validation Tool ===

Usage: dotnet run <command> [options]

Commands:
  validate, v              Run round-trip validation tests
  compare, c               Compare CST converter with external converters
  analyze, a               Analyze a single word in detail
  extract, e               Extract syllable test words from corpus

Options for 'validate':
  -f, --full-corpus        Use full corpus instead of syllable-test-words.txt
  -h, --help               Show help for validate command

Options for 'compare':
  <script>                 Script to test (e.g., thai, cyrillic, myanmar)
  --word <word>            Test a specific word instead of corpus
  --file <path>            Load words from file
  -o, --output <path>      Save detailed results to markdown file
  -h, --help               Show help for compare command

Options for 'analyze':
  <word>                   Devanagari word to analyze
  -o, --output <path>      Save analysis to markdown file
  -h, --help               Show help for analyze command

Options for 'extract':
  --xml-dir <path>         Path to XML directory (default: ../../../../data/cscd-xml)
  -o, --output <path>      Output file (default: syllable-test-words.txt)
  --csv <path>             Also generate CSV statistics
  -h, --help               Show help for extract command

Examples:
  dotnet run validate
  dotnet run validate --full-corpus
  dotnet run compare thai
  dotnet run compare myanmar --word ကမ္မ
  dotnet run analyze छेदनवधबन्धनविपरामोसआलोपसहसाकारा
  dotnet run analyze वंअ -o reports/word-analysis.md
  dotnet run extract --csv syllable-stats.csv
");
    }

    static int RunValidation(string[] args)
    {
        bool useFullCorpus = false;
        bool showHelp = false;
        string? outputFile = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--full-corpus" || args[i] == "-f")
                useFullCorpus = true;
            else if (args[i] == "--help" || args[i] == "-h")
                showHelp = true;
            else if ((args[i] == "-o" || args[i] == "--output") && i + 1 < args.Length)
            {
                outputFile = args[i + 1];
                i++;
            }
        }

        if (showHelp)
        {
            Console.WriteLine(@"
Usage: dotnet run validate [OPTIONS]

Options:
  -f, --full-corpus    Test using full corpus (all words from 217 XML files)
                       Default: Use syllable-test-words.txt only
  -o, --output <path>  Save HTML report to file
                       Default: reports/validation-report-YYYYMMDD-HHMMSS.html
  -h, --help           Show this help message

Examples:
  dotnet run validate                          # Test using syllable-test-words.txt
  dotnet run validate -f                       # Test using full corpus
  dotnet run validate -o reports/report.html   # Save report to custom file
");
            return 0;
        }

        // Set default output file if not specified
        if (outputFile == null)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            outputFile = $"reports/validation-report-{timestamp}.html";
        }

        QuickTest.Run(useFullCorpus, outputFile);
        return 0;
    }

    static async Task<int> RunComparison(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            Console.WriteLine(@"
Usage: dotnet run compare <script> [OPTIONS]

Arguments:
  <script>             Script to test: bengali, cyrillic, gujarati, gurmukhi,
                       kannada, khmer, latin, malayalam, myanmar, sinhala,
                       telugu, thai, tibetan

Options:
  --word <word>        Test a specific Devanagari word
  --file <path>        Load words from file (one per line or space-separated)
  -o, --output <path>  Save detailed results to markdown file
  -h, --help           Show this help message

Examples:
  dotnet run compare thai
  dotnet run compare myanmar --word ကမ္မ
  dotnet run compare cyrillic --file test-words.txt -o results.md
");
            return 0;
        }

        string scriptName = args[0].ToLower();
        Script targetScript;

        // Map script name to Script enum
        switch (scriptName)
        {
            case "bengali": targetScript = Script.Bengali; break;
            case "cyrillic": targetScript = Script.Cyrillic; break;
            case "gujarati": targetScript = Script.Gujarati; break;
            case "gurmukhi": targetScript = Script.Gurmukhi; break;
            case "kannada": targetScript = Script.Kannada; break;
            case "khmer": targetScript = Script.Khmer; break;
            case "latin": targetScript = Script.Latin; break;
            case "malayalam": targetScript = Script.Malayalam; break;
            case "myanmar": targetScript = Script.Myanmar; break;
            case "sinhala": targetScript = Script.Sinhala; break;
            case "telugu": targetScript = Script.Telugu; break;
            case "thai": targetScript = Script.Thai; break;
            case "tibetan": targetScript = Script.Tibetan; break;
            default:
                Console.WriteLine($"Unknown script: {scriptName}");
                Console.WriteLine("Run 'dotnet run compare --help' for valid script names.");
                return 1;
        }

        string? singleWord = null;
        string? wordsFile = null;
        string? outputFile = null;

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--word" && i + 1 < args.Length)
            {
                singleWord = args[i + 1];
                i++;
            }
            else if (args[i] == "--file" && i + 1 < args.Length)
            {
                wordsFile = args[i + 1];
                i++;
            }
            else if ((args[i] == "-o" || args[i] == "--output") && i + 1 < args.Length)
            {
                outputFile = args[i + 1];
                i++;
            }
        }

        // Get test words
        List<string> testWords;
        if (singleWord != null)
        {
            testWords = new List<string> { singleWord };
        }
        else if (wordsFile != null)
        {
            if (!File.Exists(wordsFile))
            {
                Console.WriteLine($"Error: File not found: {wordsFile}");
                return 1;
            }
            string content = File.ReadAllText(wordsFile);
            testWords = content.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .ToList();
        }
        else
        {
            // Use syllable-test-words.txt by default
            string syllableFile = "syllable-test-words.txt";
            if (!File.Exists(syllableFile))
            {
                Console.WriteLine($"Error: {syllableFile} not found");
                Console.WriteLine("Specify --word <word> or --file <path> to test specific words");
                return 1;
            }
            string content = File.ReadAllText(syllableFile);
            testWords = content.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .ToList();
        }

        Console.WriteLine($"\n=== Comparing CST vs External Converters for {targetScript} ===\n");
        Console.WriteLine($"Testing {testWords.Count} words...\n");

        var results = new List<ConverterComparison.ComparisonResult>();

        int count = 0;
        foreach (var word in testWords)
        {
            count++;
            if (count % 100 == 0)
            {
                Console.Write($"\rProcessed {count}/{testWords.Count} words...");
            }

            var result = await ConverterComparison.CompareConvertersAsync(
                word,
                "syllable-test-words.txt",
                Script.Devanagari,
                targetScript);
            results.Add(result);
        }
        Console.WriteLine($"\rProcessed {count}/{testWords.Count} words.   ");

        // Analyze results
        var stats = ConverterComparison.AnalyzeResults(results);

        Console.WriteLine("\n=== Results Summary ===\n");
        Console.WriteLine($"Total words tested: {stats.TotalWords}");
        Console.WriteLine();

        foreach (var category in stats.CategoryCounts.OrderByDescending(kvp => kvp.Value))
        {
            string icon = ConverterComparison.GetCategoryIcon(category.Key);
            string desc = ConverterComparison.GetCategoryDescription(category.Key);
            double percentage = (category.Value * 100.0) / stats.TotalWords;
            Console.WriteLine($"{icon} {category.Key,-25} {category.Value,6} ({percentage:F1}%)");
            Console.WriteLine($"   {desc}");
            Console.WriteLine();
        }

        if (stats.Failures.Count > 0)
        {
            Console.WriteLine($"\n=== First 10 Disagreements ===\n");
            foreach (var failure in stats.Failures.Take(10))
            {
                Console.WriteLine($"Word: {failure.OriginalWord}");
                Console.WriteLine($"  CST:          {failure.CstOutput ?? "(failed)"}");
                Console.WriteLine($"  pnfo:         {failure.PnfoOutput ?? "(skipped/failed)"}");
                Console.WriteLine($"  Aksharamukha: {failure.AksharamukhaOutput ?? "(failed)"}");
                Console.WriteLine($"  Category:     {ConverterComparison.GetCategoryIcon(failure.Category)} {failure.Category}");
                if (!string.IsNullOrEmpty(failure.Notes))
                    Console.WriteLine($"  Notes:        {failure.Notes}");
                Console.WriteLine();
            }
        }

        // Save detailed results if requested
        if (outputFile != null)
        {
            SaveComparisonResults(outputFile, targetScript, stats);
            Console.WriteLine($"\nDetailed results saved to: {outputFile}");
        }

        return 0;
    }

    static void SaveComparisonResults(string outputFile, Script targetScript, ConverterComparison.ComparisonStats stats)
    {
        using (var writer = new StreamWriter(outputFile))
        {
            writer.WriteLine($"# Converter Comparison Results: {targetScript}");
            writer.WriteLine();
            writer.WriteLine($"**Generated:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine($"**Total words tested:** {stats.TotalWords}");
            writer.WriteLine();

            writer.WriteLine("## Summary");
            writer.WriteLine();
            writer.WriteLine("| Category | Count | Percentage | Description |");
            writer.WriteLine("|----------|-------|------------|-------------|");
            foreach (var category in stats.CategoryCounts.OrderByDescending(kvp => kvp.Value))
            {
                string icon = ConverterComparison.GetCategoryIcon(category.Key);
                string desc = ConverterComparison.GetCategoryDescription(category.Key);
                double percentage = (category.Value * 100.0) / stats.TotalWords;
                writer.WriteLine($"| {icon} {category.Key} | {category.Value} | {percentage:F1}% | {desc} |");
            }
            writer.WriteLine();

            if (stats.Failures.Count > 0)
            {
                writer.WriteLine("## Disagreements");
                writer.WriteLine();
                foreach (var failure in stats.Failures)
                {
                    writer.WriteLine($"### {failure.OriginalWord}");
                    writer.WriteLine();
                    writer.WriteLine($"- **CST:** `{failure.CstOutput ?? "(failed)"}`");
                    writer.WriteLine($"- **pnfo:** `{failure.PnfoOutput ?? "(skipped/failed)"}`");
                    writer.WriteLine($"- **Aksharamukha:** `{failure.AksharamukhaOutput ?? "(failed)"}`");
                    writer.WriteLine($"- **Category:** {ConverterComparison.GetCategoryIcon(failure.Category)} {failure.Category}");
                    if (!string.IsNullOrEmpty(failure.Notes))
                        writer.WriteLine($"- **Notes:** {failure.Notes}");
                    writer.WriteLine();
                }
            }
        }
    }

    static int RunAnalyze(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            Console.WriteLine(@"
Usage: dotnet run analyze <word> [OPTIONS]

Arguments:
  <word>               Devanagari word to analyze

Options:
  -o, --output <path>  Save analysis to markdown file
  -h, --help           Show this help message

Examples:
  dotnet run analyze छेदनवधबन्धनविपरामोसआलोपसहसाकारा
  dotnet run analyze वंअ -o reports/word-analysis.md
");
            return 0;
        }

        string word = args[0];
        string? outputFile = null;

        for (int i = 1; i < args.Length; i++)
        {
            if ((args[i] == "-o" || args[i] == "--output") && i + 1 < args.Length)
            {
                outputFile = args[i + 1];
                i++;
            }
        }

        var analyzer = new SingleWordAnalyzer();
        analyzer.AnalyzeWord(word, outputFile);

        return 0;
    }

    static int RunExtract(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h"))
        {
            Console.WriteLine(@"
Usage: dotnet run extract [OPTIONS]

Options:
  --xml-dir <path>     Path to XML directory (default: ../../../../data/cscd-xml)
  -o, --output <path>  Output file (default: syllable-test-words.txt)
  --csv <path>         Also generate CSV statistics
  -h, --help           Show this help message

Examples:
  dotnet run extract
  dotnet run extract --xml-dir /path/to/xml -o custom-words.txt
  dotnet run extract --csv syllable-stats.csv
");
            return 0;
        }

        string xmlDir = "/Users/fsnow/Library/Application Support/CSTReader/xml";
        string outputFile = "syllable-test-words.txt";
        string? csvFile = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--xml-dir" && i + 1 < args.Length)
            {
                xmlDir = args[i + 1];
                i++;
            }
            else if ((args[i] == "-o" || args[i] == "--output") && i + 1 < args.Length)
            {
                outputFile = args[i + 1];
                i++;
            }
            else if (args[i] == "--csv" && i + 1 < args.Length)
            {
                csvFile = args[i + 1];
                i++;
            }
        }

        if (!Directory.Exists(xmlDir))
        {
            Console.WriteLine($"Error: XML directory not found: {xmlDir}");
            Console.WriteLine("Specify --xml-dir <path> to set the correct path");
            return 1;
        }

        var extractor = new SyllableExtractor();
        extractor.ProcessCorpus(xmlDir, outputFile, csvFile);

        return 0;
    }
}
