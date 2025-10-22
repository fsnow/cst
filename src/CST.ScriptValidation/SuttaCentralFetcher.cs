using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;

namespace CST.ScriptValidation;

public class SuttaCentralFetcher
{
    private static readonly HttpClient client = new HttpClient();

    static SuttaCentralFetcher()
    {
        client.DefaultRequestHeaders.Add("User-Agent", "CST-ScriptValidation/1.0");
    }

    public static async Task InvestigateApiAsync()
    {
        Console.WriteLine("SuttaCentral API Investigation Tool");
        Console.WriteLine("====================================\n");

        string outputDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Desktop",
            "SC-API-Investigation"
        );
        Directory.CreateDirectory(outputDir);
        Console.WriteLine($"Output directory: {outputDir}\n");

        // Test 1: Official API documentation
        await TestEndpoint("API Documentation",
            "https://suttacentral.net/api/docs",
            outputDir);

        // Test 2: Bilara API for root Pali text
        await TestEndpoint("Bilara API - DN1 Root Text",
            "https://suttacentral.net/api/bilarasuttas/dn1/pli-ms",
            outputDir);

        // Test 3: Web page with script parameter (Myanmar)
        await TestEndpoint("Web Page - DN1 Myanmar Script",
            "https://suttacentral.net/dn1/pli/ms?lang=en&layout=plain&reference=none&notes=asterisk&highlight=false&script=Burmese",
            outputDir);

        // Test 4: Web page with script parameter (Thai)
        await TestEndpoint("Web Page - DN1 Thai Script",
            "https://suttacentral.net/dn1/pli/ms?lang=en&layout=plain&reference=none&notes=asterisk&highlight=false&script=Thai",
            outputDir);

        // Test 5: Web page with script parameter (Devanagari)
        await TestEndpoint("Web Page - DN1 Devanagari Script",
            "https://suttacentral.net/dn1/pli/ms?lang=en&layout=plain&reference=none&notes=asterisk&highlight=false&script=Devanagari",
            outputDir);

        // Test 6: Web page with script parameter (Sinhala)
        await TestEndpoint("Web Page - DN1 Sinhala Script",
            "https://suttacentral.net/dn1/pli/ms?lang=en&layout=plain&reference=none&notes=asterisk&highlight=false&script=Sinhala",
            outputDir);

        // Test 7: Downloads page
        await TestEndpoint("Downloads Page",
            "https://suttacentral.net/downloads",
            outputDir);

        // Test 8: Offline page
        await TestEndpoint("Offline Page",
            "https://suttacentral.net/offline?lang=en",
            outputDir);

        // Test 9: Try API endpoint with script parameter (speculative)
        await TestEndpoint("API - DN1 with script param (speculative)",
            "https://suttacentral.net/api/suttas/dn1?script=Burmese",
            outputDir);

        Console.WriteLine($"\n✅ Investigation complete. Check files in: {outputDir}");
    }

    private static async Task TestEndpoint(string testName, string url, string outputDir)
    {
        Console.WriteLine($"Testing: {testName}");
        Console.WriteLine($"URL: {url}");

        try
        {
            HttpResponseMessage response = await client.GetAsync(url);
            string statusCode = $"{(int)response.StatusCode} {response.StatusCode}";
            string contentType = response.Content.Headers.ContentType?.ToString() ?? "unknown";

            Console.WriteLine($"  Status: {statusCode}");
            Console.WriteLine($"  Content-Type: {contentType}");

            if (response.IsSuccessStatusCode)
            {
                string content = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"  Content Length: {content.Length:N0} characters");

                // Save to file
                string safeFileName = MakeSafeFileName(testName);
                string extension = contentType.Contains("json") ? "json" :
                                  contentType.Contains("html") ? "html" : "txt";
                string filePath = Path.Combine(outputDir, $"{safeFileName}.{extension}");

                await File.WriteAllTextAsync(filePath, content);
                Console.WriteLine($"  ✅ Saved to: {safeFileName}.{extension}");

                // If JSON, try to pretty-print
                if (extension == "json")
                {
                    try
                    {
                        var jsonDoc = JsonDocument.Parse(content);
                        string prettyJson = JsonSerializer.Serialize(jsonDoc, new JsonSerializerOptions
                        {
                            WriteIndented = true
                        });
                        await File.WriteAllTextAsync(filePath, prettyJson);
                        Console.WriteLine($"  📝 Formatted as pretty JSON");
                    }
                    catch
                    {
                        // If pretty-print fails, keep original
                    }
                }

                // Show first 200 chars as preview
                string preview = content.Length > 200 ? content.Substring(0, 200) + "..." : content;
                Console.WriteLine($"  Preview: {preview.Replace("\n", " ").Replace("\r", "")}");
            }
            else
            {
                Console.WriteLine($"  ❌ Failed: {statusCode}");
                string errorContent = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrEmpty(errorContent) && errorContent.Length < 500)
                {
                    Console.WriteLine($"  Error: {errorContent}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Exception: {ex.Message}");
        }

        Console.WriteLine();
    }

    private static string MakeSafeFileName(string input)
    {
        string safe = input;
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(c, '-');
        }
        safe = safe.Replace(" ", "-");
        safe = safe.Replace("--", "-");
        return safe;
    }

    public static async Task FetchSuttaInScript(string suttaId, string script, string outputPath)
    {
        string url = $"https://suttacentral.net/{suttaId}/pli/ms?lang=en&layout=plain&reference=none&notes=asterisk&highlight=false&script={script}";

        Console.WriteLine($"Fetching {suttaId} in {script} script...");
        Console.WriteLine($"URL: {url}");

        try
        {
            HttpResponseMessage response = await client.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                string content = await response.Content.ReadAsStringAsync();
                await File.WriteAllTextAsync(outputPath, content);
                Console.WriteLine($"✅ Saved to: {outputPath}");
                Console.WriteLine($"   Content length: {content.Length:N0} characters");
            }
            else
            {
                Console.WriteLine($"❌ Failed: {(int)response.StatusCode} {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Exception: {ex.Message}");
        }
    }
}
