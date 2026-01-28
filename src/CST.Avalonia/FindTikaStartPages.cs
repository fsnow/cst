// Utility to download Tika PDFs for manual inspection
// Run with: dotnet run -- --download-tika-pdfs
// Then use: pdftoppm -jpeg -f 1 -l 30 <pdf> <output> to extract pages

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using CST.Avalonia.Services;
using Serilog;

namespace CST.Avalonia
{
    public static class TikaPdfDownloader
    {
        // List of Tika PDF paths (relative to SharePoint _Source folder)
        private static readonly List<(string xmlFile, string pdfPath, string description)> TikaPdfs = new()
        {
            // DN Tika
            ("s0101t.tik.xml", "01 - Burmese-CST/1957 edition/5 - Tika_/Sīlakkhandhavagga-ṭīkā.pdf", "DN Silakkhandhavagga Tika"),
            ("s0102t.tik.xml", "01 - Burmese-CST/1957 edition/5 - Tika_/Mahāvagga-ṭīkā.pdf", "DN Mahavagga Tika"),
            ("s0103t.tik.xml", "01 - Burmese-CST/1957 edition/5 - Tika_/Pāthikavagga-ṭīkā.pdf", "DN Pathikavagga Tika"),

            // MN Tika
            ("s0201t.tik.xml", "01 - Burmese-CST/1957 edition/5 - Tika_/Mūlapaṇṇāsa-ṭīkā-1.pdf", "MN Mulapannasa Tika 1"),
            ("s0201t.tik.xml (vol2)", "01 - Burmese-CST/1957 edition/5 - Tika_/Mūlapaṇṇāsa-ṭīkā-2.pdf", "MN Mulapannasa Tika 2"),
            ("s0202t.tik.xml", "01 - Burmese-CST/1957 edition/5 - Tika_/Majjhimapaṇṇāsa-Uparipaṇṇāsa-ṭīkā.pdf", "MN Majjhima+Upari Tika"),

            // SN Tika
            ("s0301t.tik.xml", "01 - Burmese-CST/1957 edition/5 - Tika_/Saṃyuttaṭīkā -1.pdf", "SN Tika 1"),
            ("s0303t.tik.xml", "01 - Burmese-CST/1957 edition/5 - Tika_/Saṃyuttaṭīkā -2.pdf", "SN Tika 2"),

            // AN Tika
            ("s0401t.tik.xml", "01 - Burmese-CST/1957 edition/5 - Tika_/Ekakanipāta-ṭīkā.pdf", "AN Ekakanipata Tika"),
            ("s0402t.tik.xml", "01 - Burmese-CST/1957 edition/5 - Tika_/Duka-tika-catukkanipāta-ṭīkā.pdf", "AN Duka-Tika-Catukka Tika"),
            ("s0403t.tik.xml", "01 - Burmese-CST/1957 edition/5 - Tika_/Pañcakanipātādi-ṭīkā.pdf", "AN Pancakanipata Tika"),

            // KN Tika
            ("s0519t.tik.xml", "01 - Burmese-CST/1957 edition/5 - Tika_/Nettiṭīkā - Nettivibhāvinī.pdf", "KN Netti Tika"),

            // Vinaya Tika (Saratthadipani)
            ("vin01t1.tik.xml", "01 - Burmese-CST/1957 edition/5 - Tika_/Sāratthadīpanī-ṭīkā-1.pdf", "Vinaya Saratthadipani 1"),
            ("vin01t2.tik.xml", "01 - Burmese-CST/1957 edition/5 - Tika_/Sāratthadīpanī-ṭīkā-2.pdf", "Vinaya Saratthadipani 2"),
            ("vin02t.tik.xml", "01 - Burmese-CST/1957 edition/5 - Tika_/Sāratthadīpanī-ṭīkā-3.pdf", "Vinaya Saratthadipani 3"),

            // Abhidhamma Tika
            ("abh01t.tik.xml", "01 - Burmese-CST/1957 edition/5 - Tika_/Dhammasaṅgaṇī-mūlaṭīkā-anuṭīkā.pdf", "Abh Dhammasangani Tika"),
            ("abh02t.tik.xml", "01 - Burmese-CST/1957 edition/5 - Tika_/Vibhaṅga-mūlaṭīkā-anuṭīkā.pdf", "Abh Vibhanga Tika"),
            ("abh03t.tik.xml", "01 - Burmese-CST/1957 edition/5 - Tika_/Pañcappakaraṇa-mūlaṭīkā-anuṭīkā.pdf", "Abh Pancappakarana Tika"),
        };

        public static async Task DownloadAndListPdfs()
        {
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            loggerFactory.AddSerilog(Log.Logger);
            var sharePointService = new SharePointService(loggerFactory.CreateLogger<SharePointService>());

            var pdfCacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CSTReader", "pdfs");

            Console.WriteLine("=== Downloading Tika PDFs for Manual Start Page Inspection ===\n");
            Console.WriteLine("After download, use pdftoppm to extract pages:");
            Console.WriteLine("  pdftoppm -jpeg -f 1 -l 30 <pdf_file> page\n");
            Console.WriteLine("Look for the page with \"နမော တဿ\" (namo tassa)\n");

            var downloadedPaths = new List<(string xmlFile, string localPath, string description)>();

            foreach (var (xmlFile, pdfPath, description) in TikaPdfs)
            {
                Console.Write($"[{description}] ");

                try
                {
                    var downloadedPath = await sharePointService.DownloadPdfAsync(pdfPath);
                    if (downloadedPath != null && File.Exists(downloadedPath))
                    {
                        var fileInfo = new FileInfo(downloadedPath);
                        Console.WriteLine($"OK ({fileInfo.Length:N0} bytes)");
                        downloadedPaths.Add((xmlFile, downloadedPath, description));
                    }
                    else
                    {
                        Console.WriteLine("FAILED - null or missing");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FAILED - {ex.Message}");
                }
            }

            Console.WriteLine($"\n=== Downloaded {downloadedPaths.Count} PDFs ===\n");
            Console.WriteLine("PDF files for inspection:\n");

            foreach (var (xmlFile, localPath, description) in downloadedPaths)
            {
                Console.WriteLine($"# {description} ({xmlFile})");
                Console.WriteLine($"pdftoppm -jpeg -f 1 -l 30 \"{localPath}\" /tmp/tika-{Path.GetFileNameWithoutExtension(localPath)}");
                Console.WriteLine();
            }
        }
    }
}
