using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace CST.ScriptValidation;

/// <summary>
/// Wrapper for pnfo/pali-script-converter JavaScript library
/// </summary>
public class PnfoConverter
{
    private static string? _nodeScriptPath;
    private static bool _initialized = false;

    public static async Task<bool> InitializeAsync()
    {
        if (_initialized)
            return true;

        Console.WriteLine("Initializing pnfo/pali-script-converter...");

        try
        {
            // Create a temporary directory for the Node.js script
            string tempDir = Path.Combine(Path.GetTempPath(), "cst-pnfo-converter");
            Directory.CreateDirectory(tempDir);

            // Create package.json
            string packageJson = @"{
  ""type"": ""module"",
  ""dependencies"": {
    ""@pathnirvanafoundation/pali-script-converter"": ""https://github.com/Path-Nirvana-Foundation/pali-script-converter""
  }
}";
            await File.WriteAllTextAsync(Path.Combine(tempDir, "package.json"), packageJson);

            // Create the Node.js converter script
            string converterScript = @"import paliConverter from '@pathnirvanafoundation/pali-script-converter';

// Read input from command line arguments
const [inputScript, outputScript, text] = process.argv.slice(2);

try {
  const result = paliConverter.convert(text, inputScript, outputScript);
  console.log(result);
} catch (error) {
  console.error('ERROR:', error.message);
  process.exit(1);
}
";
            _nodeScriptPath = Path.Combine(tempDir, "convert.js");
            await File.WriteAllTextAsync(_nodeScriptPath, converterScript);

            // Check if npm is available
            var npmCheck = new ProcessStartInfo
            {
                FileName = "npm",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = Process.Start(npmCheck))
            {
                if (process == null)
                {
                    Console.WriteLine("  ❌ npm not found. Please install Node.js and npm.");
                    return false;
                }
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                {
                    Console.WriteLine("  ❌ npm not working correctly.");
                    return false;
                }
            }

            Console.WriteLine("  Installing pnfo package (this may take a minute)...");

            // Install the package using npm
            var npmInstall = new ProcessStartInfo
            {
                FileName = "npm",
                Arguments = "install --force",
                WorkingDirectory = tempDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = Process.Start(npmInstall))
            {
                if (process == null)
                {
                    Console.WriteLine("  ❌ Failed to start npm install");
                    return false;
                }

                await process.WaitForExitAsync();
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                if (process.ExitCode != 0)
                {
                    Console.WriteLine($"  ❌ npm install failed:");
                    Console.WriteLine(error);
                    return false;
                }
            }

            _initialized = true;
            Console.WriteLine("  ✅ pnfo/pali-script-converter initialized successfully");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Initialization error: {ex.Message}");
            return false;
        }
    }

    public static async Task<string?> ConvertAsync(string text, string inputScript, string outputScript)
    {
        if (!_initialized)
        {
            bool success = await InitializeAsync();
            if (!success)
                return null;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = $"\"{_nodeScriptPath}\" \"{inputScript}\" \"{outputScript}\" \"{EscapeForShell(text)}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return null;

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                Console.WriteLine($"pnfo conversion error: {error}");
                return null;
            }

            return output.Trim();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"pnfo conversion exception: {ex.Message}");
            return null;
        }
    }

    private static string EscapeForShell(string input)
    {
        // Escape quotes for shell argument
        return input.Replace("\"", "\\\"");
    }

    /// <summary>
    /// Script name mapping from CST to pnfo format (ISO 15924 codes)
    /// </summary>
    public static string? MapScriptName(CST.Conversion.Script script)
    {
        return script switch
        {
            CST.Conversion.Script.Devanagari => "Deva",
            CST.Conversion.Script.Latin => "Latn",
            CST.Conversion.Script.Bengali => "Beng",
            CST.Conversion.Script.Gujarati => "Gujr",
            CST.Conversion.Script.Gurmukhi => "Guru",
            CST.Conversion.Script.Kannada => "Knda",
            CST.Conversion.Script.Malayalam => "Mlym",
            CST.Conversion.Script.Myanmar => "Mymr",
            CST.Conversion.Script.Sinhala => "Sinh",
            CST.Conversion.Script.Thai => "Thai",
            CST.Conversion.Script.Tibetan => "Tibt",
            CST.Conversion.Script.Telugu => "Telu",
            CST.Conversion.Script.Cyrillic => "Cyrl",
            CST.Conversion.Script.Khmer => "Khmr",
            _ => null
        };
    }
}
