using CST.Avalonia.Services.Tools;
using CST.Conversion;
using CST.Tools;
using Xunit;

namespace CST.Avalonia.Tests.Tools
{
    /// <summary>
    /// The stateless script-conversion tool (#240): auto-detects input (mixed-script safe) and converts to the
    /// requested script. Devanagari fixtures use \uXXXX escapes.
    /// </summary>
    public class ScriptToolTests
    {
        [Fact]
        public void Scripts_lists_output_scripts_without_the_autodetect_sentinel()
        {
            var scripts = new ScriptTool().Scripts;

            Assert.Contains("Latin", scripts);
            Assert.Contains("Devanagari", scripts);
            Assert.DoesNotContain("Unknown", scripts);   // Unknown is the auto-detect input sentinel, not an output
            Assert.DoesNotContain("Ipe", scripts);       // Ipe is a legacy non-Unicode font encoding, useless to an agent
        }

        [Fact]
        public void Convert_devanagari_to_latin_produces_romanized_text()
        {
            // Devanagari "dhamma": dha + ma + virama + ma
            var result = new ScriptTool().Convert(new ConvertRequest("\u0927\u092e\u094d\u092e", Script.Latin));

            Assert.Equal(Script.Latin, result.OutputScript);
            Assert.NotEqual("", result.Text);
            Assert.DoesNotContain(result.Text, c => c >= '\u0900' && c <= '\u097F'); // no Devanagari left
        }

        [Fact]
        public void Convert_defaults_to_latin_and_auto_detects_input()
        {
            var result = new ScriptTool().Convert(new ConvertRequest("\u0927\u092e\u094d\u092e"));   // no OutputScript -> Latin
            Assert.Equal(Script.Latin, result.OutputScript);
            Assert.DoesNotContain(result.Text, c => c >= '\u0900' && c <= '\u097F');
        }
    }
}
