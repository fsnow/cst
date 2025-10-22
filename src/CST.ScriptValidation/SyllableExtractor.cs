using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using CST;
using CST.Conversion;

namespace CST.ScriptValidation
{
    /// <summary>
    /// Extracts a minimal set of words that cover all unique syllable patterns
    /// in the CST corpus. This dramatically speeds up validation testing.
    /// </summary>
    public class SyllableExtractor
    {
        // Devanagari Unicode ranges
        private const int DEVA_CONSONANT_START = 0x0915; // क
        private const int DEVA_CONSONANT_END = 0x0939;   // ह
        private const int DEVA_INDEPENDENT_VOWEL_START = 0x0905; // अ
        private const int DEVA_INDEPENDENT_VOWEL_END = 0x0914;   // औ
        private const int DEVA_DEPENDENT_VOWEL_START = 0x093E;   // ा
        private const int DEVA_DEPENDENT_VOWEL_END = 0x094C;     // ौ
        private const int DEVA_VIRAMA = 0x094D;                  // ्
        private const int DEVA_ANUSVARA = 0x0902;                // ं
        private const int DEVA_CANDRABINDU = 0x0901;             // ँ

        private class SyllableInfo
        {
            public int Count { get; set; }
            public string FirstFile { get; set; }
        }

        private HashSet<string> initialSyllables = new HashSet<string>();
        private HashSet<string> medialFinalSyllables = new HashSet<string>();
        private Dictionary<string, SyllableInfo> syllableStats = new Dictionary<string, SyllableInfo>();
        private List<string> selectedWords = new List<string>();
        private HashSet<string> medialIndependentVowelPatterns = new HashSet<string>();

        public void ProcessCorpus(string xmlDirectory, string outputFile, string csvFile = null)
        {
            Console.WriteLine("Extracting minimal word set covering all syllable patterns...\n");

            var books = new Books();
            Console.WriteLine($"Found {books.Count()} books\n");

            int booksProcessed = 0;
            int wordsProcessed = 0;
            int wordsSelected = 0;

            foreach (var book in books)
            {
                booksProcessed++;
                if (booksProcessed % 20 == 0)
                {
                    Console.WriteLine($"Processing {booksProcessed}/{books.Count()}: {book.FileName}");
                }

                string xmlFile = Path.Combine(xmlDirectory, book.FileName);
                if (!File.Exists(xmlFile))
                {
                    Console.WriteLine($"Warning: File not found: {book.FileName}");
                    continue;
                }

                var words = ExtractWordsFromXml(xmlFile);
                foreach (var word in words)
                {
                    wordsProcessed++;
                    if (ProcessWord(word, book.FileName))
                    {
                        wordsSelected++;
                        selectedWords.Add(word);
                    }
                }
            }

            Console.WriteLine($"\nProcessed all {books.Count()} books");
            Console.WriteLine($"Total words processed: {wordsProcessed:N0}");
            Console.WriteLine($"Words selected: {wordsSelected:N0}");
            Console.WriteLine($"Unique initial syllables: {initialSyllables.Count}");
            Console.WriteLine($"Unique medial/final syllables: {medialFinalSyllables.Count}");
            Console.WriteLine($"Total unique syllables: {initialSyllables.Count + medialFinalSyllables.Count}");
            Console.WriteLine($"Total syllables (including duplicates): {syllableStats.Values.Sum(s => s.Count):N0}");
            Console.WriteLine($"Unique medial independent vowel patterns: {medialIndependentVowelPatterns.Count}");

            // Write selected words to file
            File.WriteAllText(outputFile, string.Join(" ", selectedWords));
            Console.WriteLine($"\nWrote {wordsSelected:N0} words to: {outputFile}");

            // Write CSV file if specified
            if (!string.IsNullOrEmpty(csvFile))
            {
                WriteCsvOutput(csvFile);
                Console.WriteLine($"Wrote syllable statistics ({syllableStats.Count:N0} unique syllables) to: {csvFile}");
            }
        }

        /// <summary>
        /// Get medial position independent vowel patterns in a word.
        /// These are non-initial independent Devanagari vowels preceded by different dependent vowels,
        /// with or without anusvara. These patterns are critical for round-trip conversion testing.
        /// Returns pattern descriptions for any such patterns found.
        /// </summary>
        private List<string> GetMedialPositionIndependentVowelPatterns(string word)
        {
            var patterns = new List<string>();

            for (int i = 0; i < word.Length - 1; i++)
            {
                char c = word[i];
                int code = (int)c;

                // Pattern 1: Dependent vowel + independent vowel
                if (code >= DEVA_DEPENDENT_VOWEL_START && code <= DEVA_DEPENDENT_VOWEL_END)
                {
                    char next = word[i + 1];
                    int nextCode = (int)next;

                    if (nextCode >= DEVA_INDEPENDENT_VOWEL_START && nextCode <= DEVA_INDEPENDENT_VOWEL_END)
                    {
                        // Record this specific combination
                        string pattern = $"dep_{c:X4}_indep_{next:X4}";
                        patterns.Add(pattern);
                    }

                    // Pattern 2: Dependent vowel + niggahita + independent vowel
                    if (nextCode == DEVA_ANUSVARA && i + 2 < word.Length)
                    {
                        char nextNext = word[i + 2];
                        int nextNextCode = (int)nextNext;

                        if (nextNextCode >= DEVA_INDEPENDENT_VOWEL_START && nextNextCode <= DEVA_INDEPENDENT_VOWEL_END)
                        {
                            string pattern = $"dep_{c:X4}_nigg_indep_{nextNext:X4}";
                            patterns.Add(pattern);
                        }
                    }
                }

                // Pattern 3: Consonant + niggahita + independent vowel
                if (code >= DEVA_CONSONANT_START && code <= DEVA_CONSONANT_END)
                {
                    if (i + 2 < word.Length)
                    {
                        char next = word[i + 1];
                        int nextCode = (int)next;

                        if (nextCode == DEVA_ANUSVARA)
                        {
                            char nextNext = word[i + 2];
                            int nextNextCode = (int)nextNext;

                            if (nextNextCode >= DEVA_INDEPENDENT_VOWEL_START && nextNextCode <= DEVA_INDEPENDENT_VOWEL_END)
                            {
                                string pattern = $"cons_{c:X4}_nigg_indep_{nextNext:X4}";
                                patterns.Add(pattern);
                            }
                        }
                    }
                }
            }

            return patterns;
        }

        /// <summary>
        /// Process a word and determine if it contains any new syllables.
        /// Returns true if the word should be added to the selected set.
        /// </summary>
        private bool ProcessWord(string word, string fileName)
        {
            if (string.IsNullOrWhiteSpace(word))
                return false;

            var syllables = ParseSyllables(word);
            if (syllables.Count == 0)
                return false;

            bool hasNewSyllable = false;
            bool hasNewMedialVowelPattern = false;

            // Check for medial independent vowel patterns and track new ones
            var vowelPatterns = GetMedialPositionIndependentVowelPatterns(word);
            foreach (var pattern in vowelPatterns)
            {
                if (!this.medialIndependentVowelPatterns.Contains(pattern))
                {
                    this.medialIndependentVowelPatterns.Add(pattern);
                    hasNewMedialVowelPattern = true;
                }
            }

            // Process all syllables and track statistics
            foreach (var syllable in syllables)
            {
                // Track statistics for all syllables (regardless of position)
                if (!syllableStats.ContainsKey(syllable))
                {
                    syllableStats[syllable] = new SyllableInfo
                    {
                        Count = 1,
                        FirstFile = fileName
                    };
                }
                else
                {
                    syllableStats[syllable].Count++;
                }
            }

            // Check initial syllable
            if (!initialSyllables.Contains(syllables[0]))
            {
                initialSyllables.Add(syllables[0]);
                hasNewSyllable = true;
            }

            // Check medial/final syllables
            for (int i = 1; i < syllables.Count; i++)
            {
                if (!medialFinalSyllables.Contains(syllables[i]))
                {
                    medialFinalSyllables.Add(syllables[i]);
                    hasNewSyllable = true;
                }
            }

            // Include word if it has new syllables OR new medial independent vowel patterns
            return hasNewSyllable || hasNewMedialVowelPattern;
        }

        /// <summary>
        /// Write syllable statistics to CSV file.
        /// </summary>
        private void WriteCsvOutput(string csvFile)
        {
            using (var writer = new StreamWriter(csvFile, false, Encoding.UTF8))
            {
                // Write CSV header
                writer.WriteLine("syllable,latin,count,first_file");

                // Sort syllables by count descending (most frequent first)
                var sortedSyllables = syllableStats
                    .OrderByDescending(kvp => kvp.Value.Count)
                    .ThenBy(kvp => kvp.Key);

                // Write syllable data
                foreach (var kvp in sortedSyllables)
                {
                    // Escape syllable if it contains special characters
                    string syllable = kvp.Key;
                    if (syllable.Contains(',') || syllable.Contains('"') || syllable.Contains('\n'))
                    {
                        syllable = $"\"{syllable.Replace("\"", "\"\"")}\"";
                    }

                    // Convert syllable to Latin transliteration
                    string latin = "";
                    try
                    {
                        string ipe = ScriptConverter.Convert(kvp.Key, Script.Devanagari, Script.Ipe);
                        latin = ScriptConverter.Convert(ipe, Script.Ipe, Script.Latin);
                    }
                    catch
                    {
                        latin = "(conversion error)";
                    }

                    writer.WriteLine($"{syllable},{latin},{kvp.Value.Count},{kvp.Value.FirstFile}");
                }
            }
        }

        /// <summary>
        /// Parse a Devanagari word into syllables.
        ///
        /// A syllable is:
        /// - An independent vowel (अ, आ, etc.)
        /// - OR: One or more consonants (with viramas between) + optional dependent vowel + optional anusvara
        ///
        /// Examples:
        /// - "अद्धानमग्गप्पटिपन्नो" => ["अ", "द्धा", "न", "म", "ग्ग", "प्प", "टि", "प", "न्नो"]
        /// - "संखित्तेन" => ["सं", "खि", "त्ते", "न"]
        /// - "सङ्खित्तेन" => ["स", "ङ्खि", "त्ते", "न"]
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

        /// <summary>
        /// Extract all Devanagari words from an XML file.
        /// </summary>
        private List<string> ExtractWordsFromXml(string xmlFile)
        {
            var words = new List<string>();

            try
            {
                var doc = new XmlDocument();
                doc.Load(xmlFile);

                // Extract text from all text nodes
                var textNodes = doc.SelectNodes("//text()");
                if (textNodes != null)
                {
                    foreach (XmlNode node in textNodes)
                    {
                        if (node.Value != null)
                        {
                            // Split on whitespace and punctuation
                            var nodeWords = Regex.Split(node.Value, @"[\s\p{P}]+")
                                .Where(w => !string.IsNullOrWhiteSpace(w) && ContainsDevanagari(w))
                                .ToList();
                            words.AddRange(nodeWords);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Error processing {Path.GetFileName(xmlFile)}: {ex.Message}");
            }

            return words;
        }

        /// <summary>
        /// Check if a string contains any Devanagari characters.
        /// </summary>
        private bool ContainsDevanagari(string text)
        {
            foreach (char c in text)
            {
                int code = (int)c;
                if ((code >= DEVA_CONSONANT_START && code <= DEVA_CONSONANT_END) ||
                    (code >= DEVA_INDEPENDENT_VOWEL_START && code <= DEVA_INDEPENDENT_VOWEL_END))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
