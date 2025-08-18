using System;
using System.Collections.Generic;
using Xunit;
using CST.Conversion;

namespace CST.Avalonia.Tests.Conversion
{
    /// <summary>
    /// Tests for script converter round-trip functionality.
    /// These converters have been in production with CST4 for 17 years,
    /// so we're testing to ensure proper round-trip conversion.
    /// </summary>
    public class ScriptConverterTests
    {
        [Theory]
        [InlineData("ṭh", "Latin -> IPE -> Latin round-trip for problematic 'ṭh'")]
        [InlineData("a", "Latin 'a' -> IPE (should be hex C1)")]
        [InlineData("bhikkhusaṅghena", "Common Pali word with diacritics")]
        [InlineData("bhikkhusaṅghañca", "Common Pali word with ñ character")]
        [InlineData("ṭ", "Single character with underdot")]
        [InlineData("ṇ", "Retroflex n")]
        [InlineData("ñ", "Palatal n")]
        [InlineData("ḷ", "Retroflex l")]
        [InlineData("ṃ", "Anusvara")]
        [InlineData("ā", "Long a")]
        [InlineData("ī", "Long i")]
        [InlineData("ū", "Long u")]
        public void TestLatinToIpeToLatinRoundTrip(string input, string description)
        {
            // Act
            string ipe = Latn2Ipe.Convert(input);
            string result = Ipe2Latn.Convert(ipe);
            
            // Assert
            Assert.Equal(input, result, ignoreCase: false, ignoreLineEndingDifferences: false, ignoreWhiteSpaceDifferences: false);
            
            // Log the intermediate IPE for debugging
            Console.WriteLine($"Test: {description}");
            Console.WriteLine($"Input: '{input}'");
            Console.WriteLine($"IPE: '{ipe}'");
            Console.WriteLine($"Result: '{result}'");
        }

        [Theory]
        [InlineData("ṭh")]
        [InlineData("bhikkhusaṅghena")]
        [InlineData("bhikkhusaṅghañca")]
        [InlineData("aṭṭhakathā")]
        [InlineData("paṭicca")]
        [InlineData("saṃyutta")]
        public void TestLatinToIpeToDevanagariToIpeToLatinRoundTrip(string input)
        {
            // Act - Full round trip through all scripts
            // Note: Based on ScriptConverter.cs, IPE -> Devanagari goes through Latin
            string ipe1 = Latn2Ipe.Convert(input);
            
            // IPE -> Latin -> Devanagari (this is how ScriptConverter does it)
            string latinFromIpe = Ipe2Latn.Convert(ipe1);
            string deva = Latn2Deva.Convert(latinFromIpe);
            
            string ipe2 = Deva2Ipe.Convert(deva);
            string result = Ipe2Latn.Convert(ipe2);
            
            // Assert
            Assert.Equal(input, result);
            Assert.Equal(ipe1, ipe2); // IPE should be identical both times
            
            // Log for debugging
            Console.WriteLine($"Latin: '{input}' -> IPE: '{ipe1}' -> Deva: '{deva}' -> IPE: '{ipe2}' -> Latin: '{result}'");
        }

        [Theory]
        [InlineData("a*", "Wildcard pattern in Latin")]
        [InlineData("bhikkhu*", "Wildcard with stem")]
        [InlineData("*saṅgha", "Wildcard at beginning")]
        [InlineData("*ṭh*", "Wildcard with problematic character")]
        public void TestWildcardPatternConversion(string pattern, string description)
        {
            // Test that wildcard patterns convert properly to IPE
            string ipePattern = Any2Ipe.Convert(pattern);
            
            // Wildcards should remain in the output
            Assert.Contains("*", ipePattern);
            
            Console.WriteLine($"Pattern test: {description}");
            Console.WriteLine($"Input pattern: '{pattern}'");
            Console.WriteLine($"IPE pattern: '{ipePattern}'");
        }

        [Fact]
        public void TestLatinAToIpeIsHexC1()
        {
            // User mentioned that Latin 'a' should be hex C1 in IPE
            string result = Latn2Ipe.Convert("a");
            
            // Check if the result contains the expected byte
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(result);
            
            Console.WriteLine($"Latin 'a' -> IPE: '{result}'");
            Console.WriteLine($"IPE bytes: {BitConverter.ToString(bytes)}");
            Console.WriteLine($"IPE char code: {(int)result[0]:X}");
            
            // The IPE encoding should produce a specific character
            // Based on user's comment, 'a' in IPE is hex C1
            Assert.Equal(0xC1, (int)result[0]);
        }

        [Theory]
        [InlineData("ṭh", "Problematic character from search")]
        [InlineData("ṭ", "Single retroflex t")]
        [InlineData("h", "Single h")]
        [InlineData("th", "Regular th")]
        [InlineData("ṭha", "With following vowel")]
        public void TestProblematicCharacterConversions(string input, string description)
        {
            try
            {
                // Test each conversion step separately to identify where it might fail
                string ipe = Latn2Ipe.Convert(input);
                Console.WriteLine($"{description}: Latin '{input}' -> IPE '{ipe}' (bytes: {GetByteString(ipe)})");
                
                string latinBack = Ipe2Latn.Convert(ipe);
                Assert.Equal(input, latinBack);
                
                // Also test through Devanagari using ScriptConverter
                string deva = ScriptConverter.Convert(ipe, Script.Ipe, Script.Devanagari);
                Console.WriteLine($"  IPE -> Devanagari: '{deva}'");
                
                string ipeFromDeva = Deva2Ipe.Convert(deva);
                Console.WriteLine($"  Devanagari -> IPE: '{ipeFromDeva}' (bytes: {GetByteString(ipeFromDeva)})");
                
                Assert.Equal(ipe, ipeFromDeva);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in {description}: {ex.Message}");
                throw;
            }
        }

        [Fact]
        public void TestScriptConverterHighLevel()
        {
            // Test the high-level ScriptConverter that SearchService uses
            string input = "ṭh";
            
            // Test Latin -> IPE -> Devanagari (what happens when current script is Devanagari)
            string ipe = ScriptConverter.Convert(input, Script.Latin, Script.Ipe);
            string deva = ScriptConverter.Convert(ipe, Script.Ipe, Script.Devanagari);
            
            Console.WriteLine($"ScriptConverter: Latin '{input}' -> IPE '{ipe}' -> Devanagari '{deva}'");
            
            // Test reverse
            string ipeBack = ScriptConverter.Convert(deva, Script.Devanagari, Script.Ipe);
            string latinBack = ScriptConverter.Convert(ipeBack, Script.Ipe, Script.Latin);
            
            Assert.Equal(input, latinBack);
        }

        [Theory]
        [InlineData("saṅgha", "Regular word")]
        [InlineData("SAṄGHA", "Uppercase word")]
        [InlineData("Saṅgha", "Title case word")]
        [InlineData("123", "Numbers")]
        [InlineData("test-123", "Mixed alphanumeric")]
        [InlineData("", "Empty string")]
        [InlineData(" ", "Space")]
        [InlineData("\t\n", "Whitespace")]
        public void TestEdgeCases(string input, string description)
        {
            // These should all handle gracefully
            string ipe = Any2Ipe.Convert(input);
            Assert.NotNull(ipe);
            
            Console.WriteLine($"Edge case - {description}: '{input}' -> '{ipe}'");
        }

        private string GetByteString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "empty";
            
            var bytes = System.Text.Encoding.UTF8.GetBytes(s);
            var hexBytes = BitConverter.ToString(bytes);
            var charCodes = string.Join(",", s.ToCharArray().Select(c => $"U+{((int)c):X4}"));
            return $"UTF8:{hexBytes} Chars:{charCodes}";
        }
    }
}