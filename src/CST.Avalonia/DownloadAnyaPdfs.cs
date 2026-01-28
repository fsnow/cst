// Utility to download Anya PDFs for View Source PDF feature
// Run with: dotnet run -- --download-anya-pdfs

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using CST.Avalonia.Services;
using Serilog;

namespace CST.Avalonia
{
    public static class AnyaPdfDownloader
    {
        // List of Anya PDF paths (relative to SharePoint _Source folder)
        private static readonly List<(string xmlFile, string pdfPath, string description)> AnyaPdfs = new()
        {
            // Visuddhimagga (in Anya folder)
            ("e0101n.mul.xml", "01 - Burmese-CST/Anya/1. Visuddhimagga/1. Visuddhimagga-1.pdf", "Visuddhimagga Mula 1"),
            ("e0102n.mul.xml", "01 - Burmese-CST/Anya/1. Visuddhimagga/2. Visuddhimagga-2.pdf", "Visuddhimagga Mula 2"),
            ("e0103n.att.xml", "01 - Burmese-CST/Anya/1. Visuddhimagga/3. Visuddhimagga-Mahāṭīkā-1.pdf", "Visuddhimagga Mahatika 1"),
            ("e0104n.att.xml", "01 - Burmese-CST/Anya/1. Visuddhimagga/4. Visuddhimagga-Mahāṭīkā-2.pdf", "Visuddhimagga Mahatika 2"),
            // Additional: Visuddhimagga Nidanakatha (introduction)
            ("", "01 - Burmese-CST/Anya/1. Visuddhimagga/5. Visuddhimagga-Nidānakathā.pdf", "Visuddhimagga Nidanakatha"),
        };

        public static async Task DownloadAndListPdfs()
        {
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            loggerFactory.AddSerilog(Log.Logger);
            var sharePointService = new SharePointService(loggerFactory.CreateLogger<SharePointService>());

            Console.WriteLine("=== Downloading Anya PDFs for View Source PDF Feature ===\n");

            var downloadedPaths = new List<(string xmlFile, string localPath, string description)>();

            foreach (var (xmlFile, pdfPath, description) in AnyaPdfs)
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
                        Console.WriteLine("FAILED - file not downloaded");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: {ex.Message}");
                }
            }

            Console.WriteLine($"\n=== Downloaded {downloadedPaths.Count} of {AnyaPdfs.Count} PDFs ===\n");

            // Print paths for pdftoppm extraction
            Console.WriteLine("To extract pages for start page verification:");
            Console.WriteLine("  mkdir -p /tmp/anya-pages\n");
            foreach (var (xmlFile, localPath, description) in downloadedPaths)
            {
                var shortName = Path.GetFileNameWithoutExtension(localPath).Replace(" ", "-").ToLower();
                Console.WriteLine($"  # {description} ({xmlFile})");
                Console.WriteLine($"  pdftoppm -jpeg -f 1 -l 20 -r 100 \"{localPath}\" /tmp/anya-pages/{shortName}\n");
            }
        }
    }
}
