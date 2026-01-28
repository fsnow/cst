// Temporary file to browse SharePoint and find 2010 PDFs
// Run with: dotnet run -- --browse-sharepoint

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using CST.Avalonia.Services;
using Serilog;

namespace CST.Avalonia
{
    public static class SharePointBrowser
    {
        public static async Task BrowseAndPrint()
        {
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            loggerFactory.AddSerilog(Log.Logger);
            var service = new SharePointService(loggerFactory.CreateLogger<SharePointService>());

            Console.WriteLine("=== SharePoint Directory Browser ===\n");

            // Start from root
            await ListRecursive(service, "", 0, 4); // Max depth of 4
        }

        private static string ExtractName(string item)
        {
            // Item format: "[DIR] Name (size bytes)" or "[FILE] Name (size bytes)"
            // Extract just the name part
            var withoutPrefix = item.Replace("[DIR] ", "").Replace("[FILE] ", "").Trim();

            // Remove the size suffix if present: " (123 bytes)"
            var parenIndex = withoutPrefix.LastIndexOf(" (");
            if (parenIndex > 0 && withoutPrefix.EndsWith(" bytes)"))
            {
                return withoutPrefix.Substring(0, parenIndex);
            }
            return withoutPrefix;
        }

        private static async Task ListRecursive(SharePointService service, string path, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;

            var indent = new string(' ', depth * 2);
            var displayPath = string.IsNullOrEmpty(path) ? "(root)" : path;

            Console.WriteLine($"{indent}📁 {displayPath}");

            var items = await service.ListFilesAsync(path);

            foreach (var item in items)
            {
                Console.WriteLine($"{indent}  {item}");

                // If it's a directory, potentially recurse
                if (item.StartsWith("[DIR]"))
                {
                    var dirName = ExtractName(item);
                    var subPath = string.IsNullOrEmpty(path) ? dirName : $"{path}/{dirName}";

                    // Recurse into Burmese directories and anything containing edition info
                    if (dirName.Contains("Burmese") || dirName.Contains("2010") ||
                        dirName.Contains("1957") || dirName.Contains("edition") ||
                        dirName.Contains("Mula") || dirName.Contains("Atthakatha") ||
                        dirName.Contains("Tika") || dirName.Contains("Anya") ||
                        dirName.Contains("Visuddhimagga") || depth < 2)
                    {
                        await ListRecursive(service, subPath, depth + 1, maxDepth);
                    }
                }
            }
        }
    }
}
