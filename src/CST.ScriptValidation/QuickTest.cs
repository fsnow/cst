using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CST.Conversion;

public class QuickTest
{
    public static void Run(bool useFullCorpus = false, string? outputFile = null)
    {
        Console.WriteLine("\n=== CST Script Validation Tool ===\n");

        // Select data source
        List<string> testWords;
        string dataSource;
        if (useFullCorpus)
        {
            Console.WriteLine("Data source: Full corpus (all words from 217 XML files)");
            dataSource = "Full corpus (all words from 217 XML files)";
            testWords = LoadFullCorpus();
        }
        else
        {
            Console.WriteLine("Data source: syllable-test-words.txt (curated test set)");
            dataSource = "syllable-test-words.txt (curated test set)";
            testWords = LoadSyllableTestWords();
        }

        Console.WriteLine($"Total words to test: {testWords.Count}\n");

        // Run validation
        var results = RunValidation(testWords);

        // Generate HTML report if requested
        if (outputFile != null)
        {
            GenerateHtmlReport(outputFile, dataSource, testWords.Count, results);
            Console.WriteLine($"\nHTML report saved to: {outputFile}");
        }
    }

    static List<string> LoadSyllableTestWords()
    {
        string syllableFile = "syllable-test-words.txt";

        if (!File.Exists(syllableFile))
        {
            Console.WriteLine($"ERROR: {syllableFile} not found");
            Environment.Exit(1);
        }

        string syllableText = File.ReadAllText(syllableFile);
        var words = syllableText.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .ToList();

        return words;
    }

    static List<string> LoadFullCorpus()
    {
        // Path to XML files directory
        string xmlDir = "../../../../data/cscd-xml";  // Adjust path as needed

        if (!Directory.Exists(xmlDir))
        {
            Console.WriteLine($"ERROR: XML directory not found: {xmlDir}");
            Console.WriteLine("Full corpus testing requires XML files.");
            Environment.Exit(1);
        }

        var allWords = new HashSet<string>();  // Use HashSet to avoid duplicates
        var xmlFiles = Directory.GetFiles(xmlDir, "*.xml", SearchOption.AllDirectories);

        Console.WriteLine($"Loading words from {xmlFiles.Length} XML files...");

        foreach (var xmlFile in xmlFiles)
        {
            try
            {
                string xmlContent = File.ReadAllText(xmlFile);
                // Extract words using simple regex (you may need more sophisticated extraction)
                var matches = System.Text.RegularExpressions.Regex.Matches(xmlContent, @"[\u0900-\u097F]+");
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    if (!string.IsNullOrWhiteSpace(match.Value))
                        allWords.Add(match.Value);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to read {xmlFile}: {ex.Message}");
            }
        }

        return allWords.ToList();
    }

    public class FailureDetails
    {
        public string Word { get; set; } = "";
        public string Ipe1 { get; set; } = "";
        public string Ipe2 { get; set; } = "";
        public string Target1 { get; set; } = "";
        public string Target2 { get; set; } = "";
        public bool IpeMatch { get; set; }
        public bool TargetMatch { get; set; }
    }

    public class ScriptValidationResult
    {
        public Script Script { get; set; }
        public int Successes { get; set; }
        public int Failures { get; set; }
        public int Total { get; set; }
        public double SuccessRate => (Successes * 100.0) / Total;
        public List<FailureDetails> FailedWords { get; set; } = new();
    }

    static List<ScriptValidationResult> RunValidation(List<string> testWords)
    {
        // Test scripts
        var scriptsToTest = new[] { Script.Gujarati };
        // Script.Bengali, Script.Cyrillic, Script.Gujarati, Script.Gurmukhi, Script.Kannada, Script.Khmer,
        // Script.Latin, Script.Malayalam, Script.Myanmar, Script.Sinhala, Script.Telugu, Script.Thai, Script.Tibetan

        var results = new List<ScriptValidationResult>();

        foreach (var targetScript in scriptsToTest)
        {
            Console.WriteLine($"\n=== Testing {targetScript} Round-Trip Conversion ===\n");

            var result = new ScriptValidationResult
            {
                Script = targetScript,
                Total = testWords.Count
            };

            foreach (var word in testWords)
            {
                // Test round-trip: Deva → IPE → TargetScript → IPE → TargetScript
                try
                {
                    string ipe1 = ScriptConverter.Convert(word, Script.Devanagari, Script.Ipe);
                    string target1 = ScriptConverter.Convert(ipe1, Script.Ipe, targetScript);
                    string ipe2 = ScriptConverter.Convert(target1, targetScript, Script.Ipe);
                    string target2 = ScriptConverter.Convert(ipe2, Script.Ipe, targetScript);

                    bool ipeMatch = ipe1 == ipe2;
                    bool targetMatch = target1 == target2;

                    if (ipeMatch && targetMatch)
                    {
                        result.Successes++;
                    }
                    else
                    {
                        result.Failures++;
                        result.FailedWords.Add(new FailureDetails
                        {
                            Word = word,
                            Ipe1 = ipe1,
                            Ipe2 = ipe2,
                            Target1 = target1,
                            Target2 = target2,
                            IpeMatch = ipeMatch,
                            TargetMatch = targetMatch
                        });
                    }
                }
                catch (Exception ex)
                {
                    result.Failures++;
                    // For exceptions, we don't have the details
                    result.FailedWords.Add(new FailureDetails
                    {
                        Word = word,
                        Ipe1 = $"Exception: {ex.Message}",
                        Ipe2 = "",
                        Target1 = "",
                        Target2 = "",
                        IpeMatch = false,
                        TargetMatch = false
                    });
                }
            }

            // Print results
            Console.WriteLine($"Results:");
            Console.WriteLine($"  Successes: {result.Successes:N0}");
            Console.WriteLine($"  Failures:  {result.Failures:N0}");
            Console.WriteLine($"  Success rate: {result.SuccessRate:F2}%");

            if (result.Failures > 0)
            {
                Console.WriteLine($"\nFirst 10 failing words:");
                foreach (var failure in result.FailedWords.Take(10))
                {
                    Console.WriteLine($"  {failure.Word}: IPE1={failure.Ipe1}, IPE2={failure.Ipe2}");
                }
            }

            Console.WriteLine();
            results.Add(result);
        }

        return results;
    }

    static string ToUnicodeString(string text)
    {
        var sb = new StringBuilder();
        foreach (char c in text)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append($"U+{((int)c):X4}");
        }
        return sb.ToString();
    }

    static void GenerateHtmlReport(string outputFile, string dataSource, int totalWords, List<ScriptValidationResult> results)
    {
        var html = new StringBuilder();

        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html>");
        html.AppendLine("<head>");
        html.AppendLine("    <meta charset='utf-8'>");
        html.AppendLine("    <title>CST Script Validation Report</title>");
        html.AppendLine("    <style>");
        html.AppendLine("        body { font-family: Arial, sans-serif; margin: 20px; background: #f5f5f5; }");
        html.AppendLine("        h1 { color: #333; }");
        html.AppendLine("        h2 { color: #666; margin-top: 30px; }");
        html.AppendLine("        .summary { background: white; padding: 15px; margin-bottom: 20px; border-radius: 5px; }");
        html.AppendLine("        .success { color: #4CAF50; font-weight: bold; }");
        html.AppendLine("        .failure { color: #f44336; font-weight: bold; }");
        html.AppendLine("        table { width: 100%; border-collapse: collapse; background: white; margin-bottom: 30px; }");
        html.AppendLine("        th { background: #4CAF50; color: white; padding: 12px; text-align: left; }");
        html.AppendLine("        td { padding: 10px; border-bottom: 1px solid #ddd; }");
        html.AppendLine("        tr:hover { background: #f5f5f5; }");
        html.AppendLine("        .word { font-family: 'Noto Sans Devanagari', Arial; font-size: 18px; font-weight: bold; }");
        html.AppendLine("        .match { color: #4CAF50; }");
        html.AppendLine("        .mismatch { color: #f44336; }");
        html.AppendLine("        .code { font-family: monospace; font-size: 12px; color: #666; }");
        html.AppendLine("    </style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine("    <h1>CST Script Validation Report</h1>");
        html.AppendLine("    <div class='summary'>");
        html.AppendLine($"        <p><strong>Total words tested:</strong> {totalWords:N0}</p>");

        int totalFailures = results.Sum(r => r.Failures);
        html.AppendLine($"        <p><strong>Total failures:</strong> <span class='{(totalFailures == 0 ? "success" : "failure")}'>{totalFailures}</span></p>");
        html.AppendLine($"        <p><strong>Generated:</strong> {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}</p>");
        html.AppendLine("    </div>");

        foreach (var result in results)
        {
            html.AppendLine($"    <h2>Cross-Script Failures: {result.Script}</h2>");

            if (result.FailedWords.Count > 0)
            {
                html.AppendLine($"    <p style='margin-left: 20px;'>Showing first 50 failures with detailed Unicode analysis</p>");
                html.AppendLine("    <table>");
                html.AppendLine("        <tr>");
                html.AppendLine("            <th>Original Word (Deva)</th>");
                html.AppendLine("            <th>Failure Type</th>");
                html.AppendLine("            <th>Details</th>");
                html.AppendLine("            <th>File</th>");
                html.AppendLine("        </tr>");

                foreach (var failure in result.FailedWords.Take(50))
                {
                    string failureType;
                    if (!failure.IpeMatch && !failure.TargetMatch)
                        failureType = "<span class='mismatch'>Both IPE and Script</span>";
                    else if (!failure.IpeMatch)
                        failureType = "<span class='mismatch'>IPE mismatch</span>";
                    else
                        failureType = "<span class='mismatch'>Script mismatch</span>";

                    html.AppendLine("        <tr>");
                    html.AppendLine($"            <td class='word'>{System.Web.HttpUtility.HtmlEncode(failure.Word)}</td>");
                    html.AppendLine($"            <td>{failureType}</td>");
                    html.AppendLine("            <td class='code' style='font-size: 11px;'>");

                    // Show script comparison if they don't match
                    if (!failure.TargetMatch && !string.IsNullOrEmpty(failure.Target1))
                    {
                        html.AppendLine("<div style='margin-bottom: 10px;'>");
                        html.AppendLine($"<strong>{result.Script} Comparison:</strong><br>");
                        html.AppendLine($"Script1: {System.Web.HttpUtility.HtmlEncode(failure.Target1)} [{ToUnicodeString(failure.Target1)}]<br>");
                        html.AppendLine($"Script2: {System.Web.HttpUtility.HtmlEncode(failure.Target2)} [{ToUnicodeString(failure.Target2)}]");
                        html.AppendLine("</div>");
                    }

                    // Show IPE comparison if they don't match
                    if (!failure.IpeMatch && !string.IsNullOrEmpty(failure.Ipe1))
                    {
                        html.AppendLine("<div>");
                        html.AppendLine("<strong>IPE Comparison:</strong><br>");
                        html.AppendLine($"IPE1: {System.Web.HttpUtility.HtmlEncode(failure.Ipe1)} [{ToUnicodeString(failure.Ipe1)}]<br>");
                        html.AppendLine($"IPE2: {System.Web.HttpUtility.HtmlEncode(failure.Ipe2)} [{ToUnicodeString(failure.Ipe2)}]");
                        html.AppendLine("</div>");
                    }

                    html.AppendLine("</td>");
                    html.AppendLine($"            <td class='code'>{dataSource}</td>");
                    html.AppendLine("        </tr>");
                }

                html.AppendLine("    </table>");
            }
            else
            {
                html.AppendLine($"    <p class='success' style='margin-left: 20px;'>✓ All words passed round-trip conversion!</p>");
            }
        }

        html.AppendLine("</body>");
        html.AppendLine("</html>");

        File.WriteAllText(outputFile, html.ToString());
    }
}
