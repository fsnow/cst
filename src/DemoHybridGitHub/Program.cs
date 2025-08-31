using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Octokit;

class Program
{
    // GitHub repository details
    const string OWNER = "VipassanaTech";
    const string REPO = "tipitaka-xml";
    const string BRANCH = "main";
    const string TARGET_PATH = "deva master";
    
    // Sample book files from CST.Core Books collection
    static readonly string[] SAMPLE_BOOKS = new[]
    {
        "s0101m.mul.xml",
        "s0101t.tik.xml",
        "s0102m.mul.xml", 
        "s0103m.mul.xml",
        "s0401m.mul.xml",
        "s0501m.mul.xml"
    };
    
    static async Task Main(string[] args)
    {
        Console.WriteLine("GitHub Hybrid Approach POC");
        Console.WriteLine("===========================");
        Console.WriteLine($"Repository: {OWNER}/{REPO}");
        Console.WriteLine($"Branch: {BRANCH}");
        Console.WriteLine($"Target Path: {TARGET_PATH}");
        Console.WriteLine($"Testing with {SAMPLE_BOOKS.Length} sample book files");
        Console.WriteLine();
        
        // Test the hybrid approach
        await TestHybridApproach();
    }
    
    static async Task TestHybridApproach()
    {
        try
        {
            // Step 1: Use GitHub API to get tree (single API call)
            Console.WriteLine("STEP 1: Getting repository tree via GitHub API");
            Console.WriteLine("-----------------------------------------------");
            
            var gitHubClient = new GitHubClient(new ProductHeaderValue("CST-Hybrid-POC"));
            
            // Get the latest commit for the branch
            var branch = await gitHubClient.Repository.Branch.Get(OWNER, REPO, BRANCH);
            var commitSha = branch.Commit.Sha;
            Console.WriteLine($"Latest commit SHA: {commitSha.Substring(0, 7)}");
            
            // Get the tree with all files (recursive)
            Console.WriteLine("Fetching tree (single API call)...");
            var tree = await gitHubClient.Git.Tree.GetRecursive(OWNER, REPO, commitSha);
            Console.WriteLine($"✓ Tree retrieved with {tree.Tree.Count} total items");
            
            // Filter to our target path
            var targetFiles = tree.Tree
                .Where(item => item.Type == TreeType.Blob && 
                               item.Path.StartsWith(TARGET_PATH + "/") &&
                               item.Path.EndsWith(".xml"))
                .ToList();
            
            Console.WriteLine($"✓ Found {targetFiles.Count} XML files in '{TARGET_PATH}'");
            Console.WriteLine();
            
            // Step 2: Simulate local file comparison
            Console.WriteLine("STEP 2: SHA Comparison (simulated)");
            Console.WriteLine("-----------------------------------");
            
            // Load or create local file-dates.json equivalent
            var localFileDates = LoadLocalFileDates();
            
            // Find files that need updating
            var filesToUpdate = new List<TreeItem>();
            foreach (var book in SAMPLE_BOOKS)
            {
                var remoteFile = targetFiles.FirstOrDefault(f => f.Path.EndsWith(book));
                if (remoteFile != null)
                {
                    if (!localFileDates.ContainsKey(book) || localFileDates[book] != remoteFile.Sha)
                    {
                        filesToUpdate.Add(remoteFile);
                        Console.WriteLine($"✓ {book}: Needs update (SHA: {remoteFile.Sha.Substring(0, 7)})");
                    }
                    else
                    {
                        Console.WriteLine($"  {book}: Up to date");
                    }
                }
                else
                {
                    Console.WriteLine($"✗ {book}: Not found in repository");
                }
            }
            
            Console.WriteLine($"\nFiles needing update: {filesToUpdate.Count}/{SAMPLE_BOOKS.Length}");
            Console.WriteLine();
            
            // Step 3: Direct download via raw.githubusercontent.com (no API calls)
            Console.WriteLine("STEP 3: Direct HTTPS Downloads");
            Console.WriteLine("-------------------------------");
            
            if (filesToUpdate.Count > 0)
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "CST-Hybrid-POC");
                
                // Download first file as example
                var fileToDownload = filesToUpdate.First();
                var fileName = Path.GetFileName(fileToDownload.Path);
                
                // Construct direct download URL (no API, no rate limit)
                var downloadUrl = $"https://raw.githubusercontent.com/{OWNER}/{REPO}/{BRANCH}/{Uri.EscapeDataString(fileToDownload.Path)}";
                Console.WriteLine($"Downloading: {fileName}");
                Console.WriteLine($"URL: {downloadUrl}");
                
                var response = await httpClient.GetAsync(downloadUrl);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsByteArrayAsync();
                    var outputPath = $"downloaded-{fileName}";
                    await File.WriteAllBytesAsync(outputPath, content);
                    
                    Console.WriteLine($"✓ Downloaded successfully ({content.Length:N0} bytes)");
                    Console.WriteLine($"✓ Saved to: {outputPath}");
                    
                    // Update local tracking
                    localFileDates[fileName] = fileToDownload.Sha;
                    SaveLocalFileDates(localFileDates);
                }
                else
                {
                    Console.WriteLine($"✗ Download failed: {response.StatusCode}");
                }
            }
            
            Console.WriteLine();
            
            // Step 4: Summary
            Console.WriteLine("SUMMARY");
            Console.WriteLine("-------");
            Console.WriteLine("API Calls Used:");
            Console.WriteLine("  - Get branch: 1 call");
            Console.WriteLine("  - Get tree: 1 call");
            Console.WriteLine("  - TOTAL: 2 API calls");
            Console.WriteLine();
            Console.WriteLine("Direct Downloads:");
            Console.WriteLine($"  - {filesToUpdate.Count} files via raw.githubusercontent.com (no API limit)");
            Console.WriteLine();
            
            // Check rate limit
            var rateLimit = gitHubClient.GetLastApiInfo()?.RateLimit;
            if (rateLimit != null)
            {
                Console.WriteLine("GitHub API Rate Limit Status:");
                Console.WriteLine($"  - Remaining: {rateLimit.Remaining}/{rateLimit.Limit}");
                Console.WriteLine($"  - Resets at: {rateLimit.Reset.ToLocalTime()}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"Inner: {ex.InnerException.Message}");
        }
    }
    
    static Dictionary<string, string> LoadLocalFileDates()
    {
        var filePath = "local-file-dates.json";
        if (File.Exists(filePath))
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        return new Dictionary<string, string>();
    }
    
    static void SaveLocalFileDates(Dictionary<string, string> fileDates)
    {
        var json = JsonSerializer.Serialize(fileDates, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText("local-file-dates.json", json);
    }
}