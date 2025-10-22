using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CST;
using CST.Conversion;

namespace CST.ScriptValidation
{
    /// <summary>
    /// Analyzes a single Devanagari word for script conversion round-trip failures.
    /// If the word fails, breaks it into syllables and tests each syllable individually,
    /// then tests consecutive syllable pairs to identify context-sensitive failures.
    /// </summary>
    public class SingleWordAnalyzer
    {
        // Devanagari Unicode ranges (same as SyllableExtractor)
        private const int DEVA_CONSONANT_START = 0x0915; // क
        private const int DEVA_CONSONANT_END = 0x0939;   // ह
        private const int DEVA_INDEPENDENT_VOWEL_START = 0x0905; // अ
        private const int DEVA_INDEPENDENT_VOWEL_END = 0x0914;   // औ
        private const int DEVA_DEPENDENT_VOWEL_START = 0x093E;   // ा
        private const int DEVA_DEPENDENT_VOWEL_END = 0x094C;     // ौ
        private const int DEVA_VIRAMA = 0x094D;                  // ्
        private const int DEVA_ANUSVARA = 0x0902;                // ं
        private const int DEVA_CANDRABINDU = 0x0901;             // ँ

        private static readonly Script[] InputScripts = new[]
        {
            Script.Bengali,
            Script.Cyrillic,
            Script.Gujarati,
            Script.Gurmukhi,
            Script.Kannada,
            Script.Khmer,
            Script.Latin,
            Script.Malayalam,
            Script.Myanmar,
            Script.Sinhala,
            Script.Telugu,
            Script.Thai,
            Script.Tibetan
        };

        public void AnalyzeWord(string devaWord, string outputFile = null)
        {
            var markdown = new StringBuilder();

            // If generating markdown, add header
            if (outputFile != null)
            {
                markdown.AppendLine($"# Single Word Analysis: {devaWord}");
                markdown.AppendLine();
                markdown.AppendLine($"**Generated:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                markdown.AppendLine();
            }

            Console.WriteLine("================================================================================");
            Console.WriteLine($"SINGLE WORD ANALYSIS: {devaWord}");
            Console.WriteLine("================================================================================\n");

            // Convert to IPE and show Latin representation
            string ipe = ScriptConverter.Convert(devaWord, Script.Devanagari, Script.Ipe);
            string latin = ScriptConverter.Convert(ipe, Script.Ipe, Script.Latin);
            Console.WriteLine($"IPE (Latin): {latin}");
            Console.WriteLine($"IPE (raw):   {ipe}");
            Console.WriteLine();

            if (outputFile != null)
            {
                markdown.AppendLine("## IPE Representation");
                markdown.AppendLine();
                markdown.AppendLine($"- **IPE (Latin):** `{latin}`");
                markdown.AppendLine($"- **IPE (raw):** `{ipe}`");
                markdown.AppendLine();
            }

            // Test full word round-trips
            Console.WriteLine("FULL WORD ROUND-TRIP TESTS:");
            Console.WriteLine("----------------------------");
            var failedScripts = new List<string>();
            var testResults = new List<(Script script, bool passed, string errorMsg)>();

            foreach (var script in InputScripts)
            {
                bool passed = TestRoundTrip(devaWord, script, out string errorMsg);
                string status = passed ? "✓ PASS" : "✗ FAIL";
                Console.WriteLine($"{status} {script,-12} {errorMsg}");

                testResults.Add((script, passed, errorMsg));
                if (!passed)
                {
                    failedScripts.Add(script.ToString());
                }
            }

            if (outputFile != null)
            {
                markdown.AppendLine("## Full Word Round-Trip Tests");
                markdown.AppendLine();
                markdown.AppendLine("| Script | Status | Error Details |");
                markdown.AppendLine("|--------|--------|---------------|");
                foreach (var (script, passed, errorMsg) in testResults)
                {
                    string status = passed ? "✓ PASS" : "✗ FAIL";
                    markdown.AppendLine($"| {script} | {status} | {errorMsg} |");
                }
                markdown.AppendLine();
            }

            if (failedScripts.Count == 0)
            {
                Console.WriteLine("\n✓ All scripts passed! Word conversion is perfect.");

                if (outputFile != null)
                {
                    markdown.AppendLine("**Result:** ✓ All scripts passed! Word conversion is perfect.");
                    markdown.AppendLine();
                    File.WriteAllText(outputFile, markdown.ToString());
                    Console.WriteLine($"\nMarkdown report saved to: {outputFile}");
                }
                return;
            }

            Console.WriteLine($"\n✗ {failedScripts.Count} script(s) failed: {string.Join(", ", failedScripts)}");
            Console.WriteLine("\nBreaking word into syllables for detailed analysis...\n");

            if (outputFile != null)
            {
                markdown.AppendLine($"**Result:** ✗ {failedScripts.Count} script(s) failed: {string.Join(", ", failedScripts)}");
                markdown.AppendLine();
            }

            // Break into syllables
            var syllables = ParseSyllables(devaWord);
            Console.WriteLine($"SYLLABLES ({syllables.Count}):");
            for (int i = 0; i < syllables.Count; i++)
            {
                string sylIpe = ScriptConverter.Convert(syllables[i], Script.Devanagari, Script.Ipe);
                string sylLatin = ScriptConverter.Convert(sylIpe, Script.Ipe, Script.Latin);
                Console.WriteLine($"  [{i}] {syllables[i]} ({sylLatin})");
            }
            Console.WriteLine();

            if (outputFile != null)
            {
                markdown.AppendLine($"## Syllable Breakdown ({syllables.Count} syllables)");
                markdown.AppendLine();
                markdown.AppendLine("| Index | Devanagari | Latin |");
                markdown.AppendLine("|-------|------------|-------|");
                for (int i = 0; i < syllables.Count; i++)
                {
                    string sylIpe = ScriptConverter.Convert(syllables[i], Script.Devanagari, Script.Ipe);
                    string sylLatin = ScriptConverter.Convert(sylIpe, Script.Ipe, Script.Latin);
                    markdown.AppendLine($"| [{i}] | {syllables[i]} | {sylLatin} |");
                }
                markdown.AppendLine();
            }

            // Test each syllable individually
            Console.WriteLine("INDIVIDUAL SYLLABLE TESTS:");
            Console.WriteLine("--------------------------");
            var syllableFailures = new Dictionary<int, List<string>>();
            var syllableResults = new List<(int index, string syl, string latin, List<string> failedScripts)>();

            for (int i = 0; i < syllables.Count; i++)
            {
                string syl = syllables[i];
                string sylIpe = ScriptConverter.Convert(syl, Script.Devanagari, Script.Ipe);
                string sylLatin = ScriptConverter.Convert(sylIpe, Script.Ipe, Script.Latin);
                Console.WriteLine($"\nSyllable [{i}]: {syl} ({sylLatin})");

                var sylFailedScripts = new List<string>();
                foreach (var script in InputScripts)
                {
                    bool passed = TestRoundTrip(syl, script, out string errorMsg);
                    if (!passed)
                    {
                        Console.WriteLine($"  ✗ {script,-12} {errorMsg}");
                        sylFailedScripts.Add(script.ToString());
                    }
                }

                if (sylFailedScripts.Count == 0)
                {
                    Console.WriteLine($"  ✓ All scripts passed");
                }
                else
                {
                    syllableFailures[i] = sylFailedScripts;
                }

                syllableResults.Add((i, syl, sylLatin, new List<string>(sylFailedScripts)));
            }

            if (outputFile != null)
            {
                markdown.AppendLine("## Individual Syllable Tests");
                markdown.AppendLine();
                foreach (var (index, syl, sylLatinStr, sylFailedScripts) in syllableResults)
                {
                    string status = sylFailedScripts.Count == 0 ? "✓ PASS" : $"✗ FAIL ({sylFailedScripts.Count} scripts)";
                    markdown.AppendLine($"### Syllable [{index}]: {syl} ({sylLatinStr})");
                    if (sylFailedScripts.Count == 0)
                    {
                        markdown.AppendLine("✓ All scripts passed");
                    }
                    else
                    {
                        markdown.AppendLine($"✗ Failed scripts: {string.Join(", ", sylFailedScripts)}");
                    }
                    markdown.AppendLine();
                }
            }

            // Check if all individual syllables passed
            bool allSyllablesPassed = syllableFailures.Count == 0;

            if (allSyllablesPassed && syllables.Count > 1)
            {
                Console.WriteLine("\n✓ All individual syllables passed!");
                Console.WriteLine("Finding smallest consecutive sequence that reproduces each bug...\n");

                // Track which scripts have been found to fail at each tile size
                var failedScriptsFound = new HashSet<string>();

                // Test progressively larger tiles: 2, 3, 4, ... syllables
                for (int tileSize = 2; tileSize <= syllables.Count; tileSize++)
                {
                    if (failedScriptsFound.Count == failedScripts.Count)
                    {
                        // All failing scripts have been identified
                        break;
                    }

                    Console.WriteLine($"TESTING TILES OF SIZE {tileSize}:");
                    Console.WriteLine($"{"".PadRight(30, '-')}");

                    var tileResults = new List<(string tile, string latin, List<string> failedScriptsInTile)>();
                    bool anyFailuresThisSize = false;

                    for (int i = 0; i <= syllables.Count - tileSize; i++)
                    {
                        // Build tile from consecutive syllables
                        string tile = string.Join("", syllables.Skip(i).Take(tileSize));
                        string tileIpe = ScriptConverter.Convert(tile, Script.Devanagari, Script.Ipe);
                        string tileLatin = ScriptConverter.Convert(tileIpe, Script.Ipe, Script.Latin);

                        // Build range string (e.g., "[0-2]" for syllables 0, 1, 2)
                        string range = tileSize == 1
                            ? $"[{i}]"
                            : $"[{i}-{i+tileSize-1}]";

                        Console.WriteLine($"\nTile {range}: {tile} ({tileLatin})");

                        var tileFailedScripts = new List<string>();
                        foreach (var script in InputScripts)
                        {
                            // Skip scripts already found in smaller tiles
                            if (failedScriptsFound.Contains(script.ToString()))
                                continue;

                            bool passed = TestRoundTrip(tile, script, out string errorMsg);
                            if (!passed)
                            {
                                Console.WriteLine($"  ✗ {script,-12} {errorMsg}");
                                tileFailedScripts.Add(script.ToString());
                                failedScriptsFound.Add(script.ToString());
                                anyFailuresThisSize = true;
                            }
                        }

                        if (tileFailedScripts.Count == 0)
                        {
                            Console.WriteLine($"  ✓ All scripts passed");
                        }

                        tileResults.Add((tile, tileLatin, new List<string>(tileFailedScripts)));
                    }

                    if (outputFile != null && anyFailuresThisSize)
                    {
                        markdown.AppendLine($"## Consecutive Syllable Sequences (Size {tileSize})");
                        markdown.AppendLine();
                        foreach (var (tile, tileLatin, tileFailedScriptsInTile) in tileResults)
                        {
                            if (tileFailedScriptsInTile.Count > 0)
                            {
                                string status = $"✗ FAIL ({tileFailedScriptsInTile.Count} scripts)";
                                markdown.AppendLine($"### Tile: {tile} ({tileLatin})");
                                markdown.AppendLine($"✗ Failed scripts: {string.Join(", ", tileFailedScriptsInTile)}");
                                markdown.AppendLine();
                            }
                        }
                    }

                    if (!anyFailuresThisSize)
                    {
                        Console.WriteLine($"  (All tiles of size {tileSize} passed)\n");
                    }
                }

                if (outputFile != null && failedScriptsFound.Count == 0)
                {
                    markdown.AppendLine("## Consecutive Syllable Tests");
                    markdown.AppendLine();
                    markdown.AppendLine("✓ All consecutive syllable sequences passed! The bug requires the full word context.");
                    markdown.AppendLine();
                }
            }
            else if (!allSyllablesPassed)
            {
                Console.WriteLine($"\n✗ {syllableFailures.Count} syllable(s) have failures - context-free syllable bugs detected");

                if (outputFile != null)
                {
                    markdown.AppendLine("## Conclusion");
                    markdown.AppendLine();
                    markdown.AppendLine($"✗ {syllableFailures.Count} syllable(s) have failures - context-free syllable bugs detected");
                    markdown.AppendLine();
                }
            }

            Console.WriteLine("\n================================================================================");
            Console.WriteLine("ANALYSIS COMPLETE");
            Console.WriteLine("================================================================================");

            // Write Markdown file if output path was specified
            if (outputFile != null)
            {
                File.WriteAllText(outputFile, markdown.ToString());
                Console.WriteLine($"\nMarkdown report saved to: {outputFile}");
            }
        }

        private bool TestRoundTrip(string devaWord, Script script, out string errorMsg)
        {
            try
            {
                // Deva → IPE
                string ipe1 = ScriptConverter.Convert(devaWord, Script.Devanagari, Script.Ipe);

                // IPE → Script
                string script1 = ScriptConverter.Convert(ipe1, Script.Ipe, script);

                // Script → IPE
                string ipe2 = ScriptConverter.Convert(script1, script, Script.Ipe);

                // IPE → Script (2nd round)
                string script2 = ScriptConverter.Convert(ipe2, Script.Ipe, script);

                // Check if IPE matches
                bool ipeMatch = ipe1 == ipe2;
                bool scriptMatch = script1 == script2;

                if (ipeMatch && scriptMatch)
                {
                    errorMsg = "";
                    return true;
                }
                else
                {
                    string latin1 = ScriptConverter.Convert(ipe1, Script.Ipe, Script.Latin);
                    string latin2 = ScriptConverter.Convert(ipe2, Script.Ipe, Script.Latin);
                    errorMsg = $"IPE1={latin1} → IPE2={latin2}";
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMsg = $"Exception: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Parse a Devanagari word into syllables.
        /// Copied from SyllableExtractor.ParseSyllables()
        /// </summary>
        private List<string> ParseSyllables(string word)
        {
            var syllables = new List<string>();
            var currentSyllable = new StringBuilder();
            int i = 0;

            while (i < word.Length)
            {
                char c = word[i];
                int code = (int)c;

                // Check for independent vowel - this is a complete syllable by itself
                if (code >= DEVA_INDEPENDENT_VOWEL_START && code <= DEVA_INDEPENDENT_VOWEL_END)
                {
                    if (currentSyllable.Length > 0)
                    {
                        syllables.Add(currentSyllable.ToString());
                        currentSyllable.Clear();
                    }
                    syllables.Add(c.ToString());
                    i++;
                    continue;
                }

                // Check for consonant - start of a syllable
                if (code >= DEVA_CONSONANT_START && code <= DEVA_CONSONANT_END)
                {
                    // If we already have a syllable building and we hit a new consonant
                    // without a virama before it, the previous syllable is complete
                    if (currentSyllable.Length > 0 &&
                        (i == 0 || word[i - 1] != (char)DEVA_VIRAMA))
                    {
                        syllables.Add(currentSyllable.ToString());
                        currentSyllable.Clear();
                    }

                    currentSyllable.Append(c);
                    i++;

                    // Look ahead for virama, dependent vowels, or anusvara
                    while (i < word.Length)
                    {
                        char next = word[i];
                        int nextCode = (int)next;

                        if (nextCode == DEVA_VIRAMA)
                        {
                            // Virama - add it and continue to next consonant
                            currentSyllable.Append(next);
                            i++;
                            // Next should be a consonant in the cluster
                            if (i < word.Length)
                            {
                                char nextCons = word[i];
                                int nextConsCode = (int)nextCons;
                                if (nextConsCode >= DEVA_CONSONANT_START && nextConsCode <= DEVA_CONSONANT_END)
                                {
                                    currentSyllable.Append(nextCons);
                                    i++;
                                }
                            }
                        }
                        else if (nextCode >= DEVA_DEPENDENT_VOWEL_START && nextCode <= DEVA_DEPENDENT_VOWEL_END)
                        {
                            // Dependent vowel - add it and continue
                            currentSyllable.Append(next);
                            i++;
                        }
                        else if (nextCode == DEVA_ANUSVARA || nextCode == DEVA_CANDRABINDU)
                        {
                            // Anusvara or candrabindu - add it and syllable is complete
                            currentSyllable.Append(next);
                            i++;
                            break;
                        }
                        else
                        {
                            // Something else - syllable is complete
                            break;
                        }
                    }

                    // Add the completed syllable
                    if (currentSyllable.Length > 0)
                    {
                        syllables.Add(currentSyllable.ToString());
                        currentSyllable.Clear();
                    }
                }
                else
                {
                    // Non-Devanagari character (punctuation, space, etc.) - skip it
                    i++;
                }
            }

            // Add any remaining syllable
            if (currentSyllable.Length > 0)
            {
                syllables.Add(currentSyllable.ToString());
            }

            return syllables;
        }
    }
}
