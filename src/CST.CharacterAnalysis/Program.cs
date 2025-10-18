using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CST.CharacterAnalysis;

class Program
{
    // Standard Pali characters from Deva2Ipe (excluding C1/\u0933 which we want to include)
    private static readonly HashSet<char> StandardPaliChars = new HashSet<char>
    {
        '\u0902', // niggahita
        '\u0905', '\u0906', '\u0907', '\u0908', '\u0909', '\u090A', '\u090F', '\u0913', // independent vowels
        '\u0915', '\u0916', '\u0917', '\u0918', '\u0919', // velar stops
        '\u091A', '\u091B', '\u091C', '\u091D', '\u091E', // palatal stops
        '\u091F', '\u0920', '\u0921', '\u0922', '\u0923', // retroflex stops
        '\u0924', '\u0925', '\u0926', '\u0927', '\u0928', // dental stops
        '\u092A', '\u092B', '\u092C', '\u092D', '\u092E', // labial stops
        '\u092F', '\u0930', '\u0932', '\u0933', '\u0935', '\u0938', '\u0939', // liquids, fricatives
        '\u093E', '\u093F', '\u0940', '\u0941', '\u0942', '\u0947', '\u094B', // dependent vowel signs
        '\u094D', // virama
        '\u200C', '\u200D', // ZWNJ, ZWJ
        // Common characters to ignore
        ' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '-', '—', '–',
        '(', ')', '[', ']', '{', '}', '"', '\'', '`', '\u201C', '\u201D', '\u2018', '\u2019',
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
        // Devanagari digits
        '\u0966', '\u0967', '\u0968', '\u0969', '\u096A', '\u096B', '\u096C', '\u096D', '\u096E', '\u096F',
        // Devanagari punctuation
        '\u0964', '\u0965', // danda, double danda
    };

    static void Main(string[] args)
    {
        Console.WriteLine("CST Character Analysis Tool");
        Console.WriteLine("===========================\n");

        string xmlPath = "/Users/fsnow/Library/Application Support/CSTReader/xml";

        if (!Directory.Exists(xmlPath))
        {
            Console.WriteLine($"Error: XML directory not found: {xmlPath}");
            return;
        }

        var xmlFiles = Directory.GetFiles(xmlPath, "*.xml").OrderBy(f => f).ToArray();
        Console.WriteLine($"Found {xmlFiles.Length} XML files\n");

        var charData = new Dictionary<char, CharacterInfo>();

        int filesProcessed = 0;
        foreach (var file in xmlFiles)
        {
            string fileName = Path.GetFileName(file);
            filesProcessed++;

            if (filesProcessed % 20 == 0)
                Console.WriteLine($"Processing {filesProcessed}/{xmlFiles.Length}: {fileName}");

            ProcessFile(file, fileName, charData);
        }

        Console.WriteLine($"\nProcessed all {filesProcessed} files");
        Console.WriteLine($"Found {charData.Count} non-standard characters\n");

        // Generate HTML report
        string reportPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Desktop",
            "CST-Character-Analysis.html"
        );

        GenerateHtmlReport(charData, reportPath);

        Console.WriteLine($"Report saved to: {reportPath}");
    }

    static void ProcessFile(string filePath, string fileName, Dictionary<char, CharacterInfo> charData)
    {
        try
        {
            var doc = XDocument.Load(filePath);
            var textContent = ExtractTextContent(doc.Root);

            foreach (char c in textContent)
            {
                // Skip standard Pali characters and common punctuation
                if (StandardPaliChars.Contains(c))
                    continue;

                // Skip basic Latin letters (a-z, A-Z)
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                    continue;

                if (!charData.ContainsKey(c))
                {
                    charData[c] = new CharacterInfo
                    {
                        Character = c,
                        Count = 0,
                        Books = new HashSet<string>(),
                        Examples = new List<string>()
                    };
                }

                var info = charData[c];
                info.Count++;
                info.Books.Add(fileName);

                // Collect examples (up to 10 per character)
                if (info.Examples.Count < 10)
                {
                    string context = GetContext(textContent, textContent.IndexOf(c), 20);
                    if (!info.Examples.Contains(context))
                        info.Examples.Add(context);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing {fileName}: {ex.Message}");
        }
    }

    static string ExtractTextContent(XElement element)
    {
        if (element == null)
            return "";

        var sb = new StringBuilder();

        foreach (var node in element.DescendantNodes())
        {
            if (node is XText textNode)
            {
                sb.Append(textNode.Value);
            }
        }

        return sb.ToString();
    }

    static string GetContext(string text, int index, int contextLength)
    {
        int start = Math.Max(0, index - contextLength);
        int end = Math.Min(text.Length, index + contextLength + 1);

        string context = text.Substring(start, end - start);

        // Clean up whitespace
        context = System.Text.RegularExpressions.Regex.Replace(context, @"\s+", " ");

        return context.Trim();
    }

    static void GenerateHtmlReport(Dictionary<char, CharacterInfo> charData, string outputPath)
    {
        var sortedData = charData.Values.OrderByDescending(c => c.Count).ToList();

        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html>");
        html.AppendLine("<head>");
        html.AppendLine("    <meta charset='utf-8'>");
        html.AppendLine("    <title>CST Character Analysis Report</title>");
        html.AppendLine("    <style>");
        html.AppendLine("        body { font-family: Arial, sans-serif; margin: 20px; background: #f5f5f5; }");
        html.AppendLine("        h1 { color: #333; }");
        html.AppendLine("        .summary { background: white; padding: 15px; margin-bottom: 20px; border-radius: 5px; }");
        html.AppendLine("        table { width: 100%; border-collapse: collapse; background: white; }");
        html.AppendLine("        th { background: #4CAF50; color: white; padding: 12px; text-align: left; }");
        html.AppendLine("        td { padding: 10px; border-bottom: 1px solid #ddd; }");
        html.AppendLine("        tr:hover { background: #f5f5f5; }");
        html.AppendLine("        .char { font-size: 24px; font-weight: bold; }");
        html.AppendLine("        .unicode { font-family: monospace; color: #666; }");
        html.AppendLine("        .examples { font-family: 'Noto Sans Devanagari', Arial; color: #444; font-size: 14px; }");
        html.AppendLine("        .book-list { font-size: 11px; color: #666; max-height: 100px; overflow-y: auto; }");
        html.AppendLine("    </style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine("    <h1>CST Character Analysis Report</h1>");
        html.AppendLine($"    <div class='summary'>");
        html.AppendLine($"        <p><strong>Total non-standard characters found:</strong> {charData.Count}</p>");
        html.AppendLine($"        <p><strong>Total occurrences:</strong> {sortedData.Sum(c => c.Count):N0}</p>");
        html.AppendLine($"        <p><strong>Generated:</strong> {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
        html.AppendLine($"    </div>");
        html.AppendLine("    <table>");
        html.AppendLine("        <tr>");
        html.AppendLine("            <th>Character</th>");
        html.AppendLine("            <th>Unicode</th>");
        html.AppendLine("            <th>Unicode Name</th>");
        html.AppendLine("            <th>Count</th>");
        html.AppendLine("            <th>Books</th>");
        html.AppendLine("            <th>Examples</th>");
        html.AppendLine("        </tr>");

        foreach (var info in sortedData)
        {
            string unicodeName = GetUnicodeName(info.Character);
            string unicodeValue = $"U+{((int)info.Character):X4}";
            string bookList = string.Join(", ", info.Books.OrderBy(b => b).Take(20));
            if (info.Books.Count > 20)
                bookList += $" ... (+{info.Books.Count - 20} more)";

            string examples = string.Join("<br>", info.Examples.Select(e => System.Net.WebUtility.HtmlEncode(e)));

            html.AppendLine("        <tr>");
            html.AppendLine($"            <td class='char'>{System.Net.WebUtility.HtmlEncode(info.Character.ToString())}</td>");
            html.AppendLine($"            <td class='unicode'>{unicodeValue}</td>");
            html.AppendLine($"            <td>{unicodeName}</td>");
            html.AppendLine($"            <td>{info.Count:N0}</td>");
            html.AppendLine($"            <td class='book-list' title='{System.Net.WebUtility.HtmlEncode(string.Join(", ", info.Books.OrderBy(b => b)))}'>{bookList}</td>");
            html.AppendLine($"            <td class='examples'>{examples}</td>");
            html.AppendLine("        </tr>");
        }

        html.AppendLine("    </table>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");

        File.WriteAllText(outputPath, html.ToString(), Encoding.UTF8);
    }

    static string GetUnicodeName(char c)
    {
        // Basic Unicode name lookup for common Devanagari and Sanskrit characters
        var names = new Dictionary<int, string>
        {
            { 0x0900, "DEVANAGARI SIGN INVERTED CANDRABINDU" },
            { 0x0901, "DEVANAGARI SIGN CANDRABINDU" },
            { 0x0903, "DEVANAGARI SIGN VISARGA" },
            { 0x0904, "DEVANAGARI LETTER SHORT A" },
            { 0x090B, "DEVANAGARI LETTER VOCALIC R" },
            { 0x090C, "DEVANAGARI LETTER VOCALIC L" },
            { 0x090D, "DEVANAGARI LETTER CANDRA E" },
            { 0x090E, "DEVANAGARI LETTER SHORT E" },
            { 0x0910, "DEVANAGARI LETTER AI" },
            { 0x0911, "DEVANAGARI LETTER CANDRA O" },
            { 0x0912, "DEVANAGARI LETTER SHORT O" },
            { 0x0914, "DEVANAGARI LETTER AU" },
            { 0x0929, "DEVANAGARI LETTER NNNA" },
            { 0x0931, "DEVANAGARI LETTER RRA" },
            { 0x0934, "DEVANAGARI LETTER LLLA" },
            { 0x0933, "DEVANAGARI LETTER LLA" },
            { 0x0936, "DEVANAGARI LETTER SHA" },
            { 0x0937, "DEVANAGARI LETTER SSA" },
            { 0x0943, "DEVANAGARI VOWEL SIGN VOCALIC R" },
            { 0x0944, "DEVANAGARI VOWEL SIGN VOCALIC RR" },
            { 0x0945, "DEVANAGARI VOWEL SIGN CANDRA E" },
            { 0x0946, "DEVANAGARI VOWEL SIGN SHORT E" },
            { 0x0948, "DEVANAGARI VOWEL SIGN AI" },
            { 0x0949, "DEVANAGARI VOWEL SIGN CANDRA O" },
            { 0x094A, "DEVANAGARI VOWEL SIGN SHORT O" },
            { 0x094C, "DEVANAGARI VOWEL SIGN AU" },
            { 0x094E, "DEVANAGARI VOWEL SIGN PRISHTHAMATRA E" },
            { 0x094F, "DEVANAGARI VOWEL SIGN AW" },
            { 0x0950, "DEVANAGARI OM" },
            { 0x0951, "DEVANAGARI STRESS SIGN UDATTA" },
            { 0x0952, "DEVANAGARI STRESS SIGN ANUDATTA" },
            { 0x0953, "DEVANAGARI GRAVE ACCENT" },
            { 0x0954, "DEVANAGARI ACUTE ACCENT" },
            { 0x0958, "DEVANAGARI LETTER QA" },
            { 0x0959, "DEVANAGARI LETTER KHHA" },
            { 0x095A, "DEVANAGARI LETTER GHHA" },
            { 0x095B, "DEVANAGARI LETTER ZA" },
            { 0x095C, "DEVANAGARI LETTER DDDHA" },
            { 0x095D, "DEVANAGARI LETTER RHA" },
            { 0x095E, "DEVANAGARI LETTER FA" },
            { 0x095F, "DEVANAGARI LETTER YYA" },
            { 0x0960, "DEVANAGARI LETTER VOCALIC RR" },
            { 0x0961, "DEVANAGARI LETTER VOCALIC LL" },
            { 0x0962, "DEVANAGARI VOWEL SIGN VOCALIC L" },
            { 0x0963, "DEVANAGARI VOWEL SIGN VOCALIC LL" },
            { 0x0964, "DEVANAGARI DANDA" },
            { 0x0965, "DEVANAGARI DOUBLE DANDA" },
            { 0x0970, "DEVANAGARI ABBREVIATION SIGN" },
        };

        int codepoint = (int)c;
        if (names.TryGetValue(codepoint, out string name))
            return name;

        return $"U+{codepoint:X4}";
    }
}

class CharacterInfo
{
    public char Character { get; set; }
    public int Count { get; set; }
    public HashSet<string> Books { get; set; }
    public List<string> Examples { get; set; }
}
