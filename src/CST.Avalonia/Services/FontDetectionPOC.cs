using System;
using System.Reflection;
using Avalonia.Media;
using Serilog;

namespace CST.Avalonia.Services
{
    /// <summary>
    /// Proof of Concept for detecting actual fonts used by Avalonia
    /// </summary>
    public class FontDetectionPOC
    {
        private readonly ILogger _logger;

        public FontDetectionPOC(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Attempts to detect the actual font that would be used to render text
        /// </summary>
        public string? DetectActualFont(string sampleText, string? requestedFontFamily)
        {
            try
            {
                _logger.Information("Attempting to detect font for text: '{Text}' with requested font: '{Font}'", 
                    sampleText, requestedFontFamily ?? "system default");

                // Step 1: Create a Typeface with the requested font
                var fontFamily = string.IsNullOrWhiteSpace(requestedFontFamily) 
                    ? FontFamily.Default 
                    : new FontFamily(requestedFontFamily);
                
                var typeface = new Typeface(fontFamily);
                _logger.Debug("Created typeface with font family: {FontFamily}", typeface.FontFamily);

                // Step 2: Try to get GlyphTypeface
                if (!FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphTypeface))
                {
                    _logger.Warning("Failed to get GlyphTypeface for font: {Font}", requestedFontFamily);
                    return null;
                }

                _logger.Debug("Got GlyphTypeface: {GlyphTypeface}", glyphTypeface);

                // Step 3: Use reflection to access internal SKTypeface
                // Try different potential field names
                string[] potentialFieldNames = { "_typeface", "typeface", "Typeface", "_skTypeface" };
                
                foreach (var fieldName in potentialFieldNames)
                {
                    var field = glyphTypeface.GetType().GetField(
                        fieldName, 
                        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                    
                    if (field != null)
                    {
                        _logger.Debug("Found field '{FieldName}' of type {FieldType}", 
                            fieldName, field.FieldType.Name);
                        
                        var fieldValue = field.GetValue(glyphTypeface);
                        if (fieldValue != null)
                        {
                            // Try to get FamilyName property
                            var familyNameProp = fieldValue.GetType().GetProperty("FamilyName");
                            if (familyNameProp != null)
                            {
                                var actualFontName = familyNameProp.GetValue(fieldValue) as string;
                                _logger.Information("Successfully detected actual font: '{ActualFont}'", actualFontName);
                                return actualFontName;
                            }
                        }
                    }
                }

                // Step 4: Alternative - try to get font info from GlyphTypeface directly
                // Check if GlyphTypeface has any useful properties
                var typefaceType = glyphTypeface.GetType();
                _logger.Debug("GlyphTypeface type: {Type}", typefaceType.FullName);
                
                // Log all properties for debugging
                foreach (var prop in typefaceType.GetProperties())
                {
                    try
                    {
                        var value = prop.GetValue(glyphTypeface);
                        _logger.Debug("Property {Name}: {Value}", prop.Name, value);
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug("Could not read property {Name}: {Error}", prop.Name, ex.Message);
                    }
                }

                _logger.Warning("Could not find SKTypeface field via reflection");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error detecting font");
                return null;
            }
        }

        /// <summary>
        /// Test the POC with various scripts
        /// </summary>
        public void RunTests()
        {
            _logger.Information("=== Starting Font Detection POC Tests ===");

            // Test cases with different scripts
            var testCases = new[]
            {
                ("Hello World", "Arial"),
                ("Hello World", null), // System default
                ("नमस्ते", null), // Devanagari
                ("မင်္ဂလာပါ", null), // Myanmar
                ("สวัสดี", null), // Thai
                ("བཀྲ་ཤིས་བདེ་ལེགས", null), // Tibetan
                ("ආයුබෝවන්", null), // Sinhala
                ("ನಮಸ್ಕಾರ", null), // Kannada
            };

            foreach (var (text, font) in testCases)
            {
                _logger.Information("\nTest: Text='{Text}', RequestedFont='{Font}'", text, font ?? "default");
                var detected = DetectActualFont(text, font);
                _logger.Information("Result: {Result}", detected ?? "DETECTION FAILED");
            }

            _logger.Information("=== Font Detection POC Tests Complete ===");
        }
    }
}