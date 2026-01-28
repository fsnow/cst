// Utility to find the start page (where "namo tassa" appears) in Atthakatha PDFs
// Run with: dotnet run -- --find-start-pages

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using CST.Avalonia.Services;
using Serilog;
using UglyToad.PdfPig;

namespace CST.Avalonia
{
    public static class PdfStartPageFinder
    {
        // The "namo tassa bhagavato arahato sammāsambuddhassa" in Myanmar script
        // This appears on the first content page of each book
        private const string NamoTassaMyanmar = "နမော တဿ ဘဂဝတော အရဟတော သမ္မာသမ္ဗုဒ္ဓဿ";

        // Alternative: just search for "နမော" which is the first word
        private const string NamoMyanmar = "နမော";

        // List of Atthakatha PDF paths (relative to SharePoint root)
        private static readonly List<(string xmlFile, string pdfPath)> AtthakathaPdfs = new()
        {
            // Vinaya Atthakatha
            ("vin01a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Pārājikakaṇḍa-aṭṭhakathā-1.pdf"),
            ("vin02a1.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Pācittiyādi-aṭṭhakathā.pdf"),
            ("vin02a2.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Cūḷavagga-aṭṭhakathā.pdf"),

            // DN Atthakatha (already done - for verification)
            ("s0101a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Sīlakkhandhavagga-aṭṭhakathā.pdf"),
            ("s0102a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Mahāvagga-aṭṭhakathā.pdf"),
            ("s0103a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Pāthikavagga-aṭṭhakathā.pdf"),

            // MN Atthakatha
            ("s0201a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Mūlapaṇṇāsa-aṭṭhakathā-1.pdf"),
            ("s0202a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Majjhimapaṇṇāsa-aṭṭhakathā.pdf"),
            ("s0203a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Uparipaṇṇāsa-aṭṭhakathā.pdf"),

            // SN Atthakatha
            ("s0301a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Sagāthāvagga-aṭṭhakathā.pdf"),
            ("s0302a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Nidāna-Khandhavagga-aṭṭhakathā.pdf"),
            ("s0304a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Saḷāyatana-Mahāvagga-aṭṭhakathā.pdf"),

            // AN Atthakatha
            ("s0401a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Ekakanipāta-aṭṭhakathā.pdf"),
            ("s0402a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Duka-Tika-Catukkanipāta-aṭṭhakathā.pdf"),
            ("s0403a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Pañcakanipāta-aṭṭhakathā.pdf"),

            // KN Atthakatha (selected - there are many more)
            ("s0501a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Khuddakapāṭha-aṭṭhakathā.pdf"),
            ("s0502a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Dhammapada-aṭṭhakathā-1.pdf"),
            ("s0503a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Udāna-aṭṭhakathā.pdf"),
            ("s0504a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Itivuttaka-aṭṭhakathā.pdf"),
            ("s0505a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Suttanipāta-aṭṭhakathā-1.pdf"),
            ("s0506a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Vimānavatthu-aṭṭhakathā.pdf"),
            ("s0507a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Petavatthu-aṭṭhakathā.pdf"),
            ("s0508a1.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Theragāthā-aṭṭhakathā-1.pdf"),
            ("s0508a2.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Theragāthā-aṭṭhakathā-2.pdf"),
            ("s0509a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Therīgāthā-aṭṭhakathā.pdf"),
            ("s0510a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Apadāna-aṭṭhakathā-1.pdf"),
            ("s0511a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Buddhavaṃsa-aṭṭhakathā.pdf"),
            ("s0512a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Cariyāpiṭaka-aṭṭhakathā.pdf"),
            ("s0513a1.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Jātaka-aṭṭhakathā-1.pdf"),
            ("s0513a2.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Jātaka-aṭṭhakathā-2.pdf"),
            ("s0513a3.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Jātaka-aṭṭhakathā-3.pdf"),
            ("s0513a4.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Jātaka-aṭṭhakathā-4.pdf"),
            ("s0514a1.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Jātaka-aṭṭhakathā-5.pdf"),
            ("s0514a2.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Jātaka-aṭṭhakathā-6.pdf"),
            ("s0514a3.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Jātaka-aṭṭhakathā-7.pdf"),
            ("s0515a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Mahāniddesa-aṭṭhakathā.pdf"),
            ("s0516a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Cūḷaniddesa-Nettippakaraṇa-aṭṭhakathā.pdf"),
            ("s0517a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Paṭisambhidāmagga-aṭṭhakathā-1.pdf"),

            // Abhidhamma Atthakatha
            ("abh01a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Dhammasaṅgaṇī-aṭṭhakathā.pdf"),
            ("abh02a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Vibhaṅga-aṭṭhakathā.pdf"),
            ("abh03a.att.xml", "01 - Burmese-CST/1957 edition/4 - Atthakatha/Pañcappakaraṇa-aṭṭhakathā.pdf"),
        };

        public static async Task FindAndPrintStartPages()
        {
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            loggerFactory.AddSerilog(Log.Logger);
            var sharePointService = new SharePointService(loggerFactory.CreateLogger<SharePointService>());

            var pdfCacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CSTReader", "pdfs");

            Console.WriteLine("=== Finding Start Pages for Atthakatha PDFs ===\n");
            Console.WriteLine($"Searching for: \"{NamoMyanmar}\" (first word of namo tassa)\n");

            var results = new List<(string xmlFile, int startPage, string pdfName)>();

            foreach (var (xmlFile, pdfPath) in AtthakathaPdfs)
            {
                var localPath = Path.Combine(pdfCacheDir, pdfPath);
                var pdfName = Path.GetFileName(pdfPath);

                Console.Write($"Processing {pdfName}... ");

                // Download if not cached (DownloadPdfAsync handles caching internally)
                if (!File.Exists(localPath))
                {
                    Console.Write("downloading... ");
                    try
                    {
                        var downloadedPath = await sharePointService.DownloadPdfAsync(pdfPath);
                        if (downloadedPath == null)
                        {
                            Console.WriteLine("FAILED to download");
                            continue;
                        }
                        localPath = downloadedPath;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"FAILED to download: {ex.Message}");
                        continue;
                    }
                }

                // Search for namo tassa
                try
                {
                    var startPage = FindNamoTassaPage(localPath);
                    if (startPage > 0)
                    {
                        Console.WriteLine($"page {startPage}");
                        results.Add((xmlFile, startPage, pdfName));
                    }
                    else
                    {
                        Console.WriteLine("NOT FOUND - manual check needed");
                        results.Add((xmlFile, -1, pdfName));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: {ex.Message}");
                    results.Add((xmlFile, -1, pdfName));
                }
            }

            // Print summary for Sources.cs
            Console.WriteLine("\n=== Results for Sources.cs ===\n");
            foreach (var (xmlFile, startPage, pdfName) in results)
            {
                if (startPage > 0)
                {
                    Console.WriteLine($"// {pdfName}");
                    Console.WriteLine($"addSource(\"{xmlFile}\", SourceType.Burmese1957, {startPage}, ...);");
                }
                else
                {
                    Console.WriteLine($"// {pdfName} - NEEDS MANUAL CHECK");
                    Console.WriteLine($"addSource(\"{xmlFile}\", SourceType.Burmese1957, 1, ...); // TODO");
                }
                Console.WriteLine();
            }
        }

        private static int FindNamoTassaPage(string pdfPath)
        {
            using var document = PdfDocument.Open(pdfPath);

            // Search first 50 pages (should be within first 20 typically)
            var maxPages = Math.Min(50, document.NumberOfPages);

            for (int pageNum = 1; pageNum <= maxPages; pageNum++)
            {
                var page = document.GetPage(pageNum);
                var text = string.Join("", page.Letters.Select(l => l.Value));

                // Remove spaces for more reliable matching
                var textNoSpaces = text.Replace(" ", "").Replace("\n", "").Replace("\r", "");
                var searchNoSpaces = NamoMyanmar.Replace(" ", "");

                if (textNoSpaces.Contains(searchNoSpaces))
                {
                    return pageNum;
                }

                // Also try with the full phrase
                if (textNoSpaces.Contains(NamoTassaMyanmar.Replace(" ", "")))
                {
                    return pageNum;
                }
            }

            return -1; // Not found
        }
    }
}
