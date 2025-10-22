using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace CST.ScriptValidation;

/// <summary>
/// Wrapper for Aksharamukha Python library
/// </summary>
public class AksharamukhaConverter
{
    private static string? _pythonScriptPath;
    private static bool _initialized = false;

    public static async Task<bool> InitializeAsync()
    {
        if (_initialized)
            return true;

        Console.WriteLine("Initializing Aksharamukha...");

        try
        {
            // Check if Python is available
            var pythonCheck = new ProcessStartInfo
            {
                FileName = "python3",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = Process.Start(pythonCheck))
            {
                if (process == null)
                {
                    Console.WriteLine("  ❌ python3 not found. Please install Python 3.");
                    return false;
                }
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                {
                    Console.WriteLine("  ❌ python3 not working correctly.");
                    return false;
                }
            }

            // Check if aksharamukha package is installed
            var pipCheck = new ProcessStartInfo
            {
                FileName = "python3",
                Arguments = "-c \"import aksharamukha\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = Process.Start(pipCheck))
            {
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    if (process.ExitCode != 0)
                    {
                        Console.WriteLine("  Installing aksharamukha package (this may take a minute)...");

                        // Install aksharamukha
                        var pipInstall = new ProcessStartInfo
                        {
                            FileName = "python3",
                            Arguments = "-m pip install aksharamukha --quiet",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using var installProcess = Process.Start(pipInstall);
                        if (installProcess == null)
                        {
                            Console.WriteLine("  ❌ Failed to install aksharamukha");
                            return false;
                        }

                        await installProcess.WaitForExitAsync();
                        if (installProcess.ExitCode != 0)
                        {
                            string error = await installProcess.StandardError.ReadToEndAsync();
                            Console.WriteLine($"  ❌ pip install failed: {error}");
                            return false;
                        }
                    }
                }
            }

            // Create a temporary directory for the Python script
            string tempDir = Path.Combine(Path.GetTempPath(), "cst-aksharamukha-converter");
            Directory.CreateDirectory(tempDir);

            // Create the Python converter script
            string converterScript = @"#!/usr/bin/env python3
import sys
from aksharamukha import transliterate

# Read input from command line arguments
if len(sys.argv) != 4:
    print('ERROR: Usage: convert.py <input_script> <output_script> <text>', file=sys.stderr)
    sys.exit(1)

input_script = sys.argv[1]
output_script = sys.argv[2]
text = sys.argv[3]

try:
    result = transliterate.process(input_script, output_script, text)
    print(result, end='')
except Exception as e:
    print(f'ERROR: {str(e)}', file=sys.stderr)
    sys.exit(1)
";
            _pythonScriptPath = Path.Combine(tempDir, "convert.py");
            await File.WriteAllTextAsync(_pythonScriptPath, converterScript);

            _initialized = true;
            Console.WriteLine("  ✅ Aksharamukha initialized successfully");
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
                FileName = "python3",
                Arguments = $"\"{_pythonScriptPath}\" \"{inputScript}\" \"{outputScript}\" \"{EscapeForShell(text)}\"",
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
                Console.WriteLine($"Aksharamukha conversion error: {error}");
                return null;
            }

            return output.Trim();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Aksharamukha conversion exception: {ex.Message}");
            return null;
        }
    }

    private static string EscapeForShell(string input)
    {
        // Escape quotes for shell argument
        return input.Replace("\"", "\\\"");
    }

    /// <summary>
    /// Script name mapping from CST to Aksharamukha format
    /// </summary>
    public static string? MapScriptName(CST.Conversion.Script script)
    {
        return script switch
        {
            CST.Conversion.Script.Devanagari => "Devanagari",
            CST.Conversion.Script.Latin => "IAST", // or "ISO" or "IPA" depending on preference
            CST.Conversion.Script.Bengali => "Bengali",
            CST.Conversion.Script.Gujarati => "Gujarati",
            CST.Conversion.Script.Gurmukhi => "Gurmukhi",
            CST.Conversion.Script.Kannada => "Kannada",
            CST.Conversion.Script.Malayalam => "Malayalam",
            CST.Conversion.Script.Myanmar => "Burmese",
            CST.Conversion.Script.Sinhala => "Sinhala",
            CST.Conversion.Script.Thai => "Thai",
            CST.Conversion.Script.Tibetan => "Tibetan",
            CST.Conversion.Script.Telugu => "Telugu",
            CST.Conversion.Script.Cyrillic => "ISOCyrillic",
            _ => null
        };
    }
}
