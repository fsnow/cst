using System;
using System.Globalization;
using Avalonia.Data.Converters;
using CST.Conversion;

namespace CST.Avalonia.Converters;

/// <summary>
/// Converter to display script names using CST4 compatible names
/// </summary>
public class ScriptDisplayNameConverter : IValueConverter
{
    public static readonly ScriptDisplayNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Script script)
        {
            // Use the same names as CST4 FormMain resx resources (tscbPaliScript.Items)
            return script switch
            {
                Script.Bengali => "Bengali",
                Script.Cyrillic => "Cyrillic",
                Script.Devanagari => "Devanagari",
                Script.Gujarati => "Gujarati", 
                Script.Gurmukhi => "Gurmukhi",
                Script.Kannada => "Kannada",
                Script.Khmer => "Khmer",
                Script.Malayalam => "Malayalam",
                Script.Myanmar => "Myanmar",
                Script.Latin => "Roman", // CST4 uses "Roman" instead of "Latin"
                Script.Sinhala => "Sinhala",
                Script.Telugu => "Telugu",
                Script.Thai => "Thai",
                Script.Tibetan => "Tibetan",
                _ => script.ToString()
            };
        }
        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Not needed for one-way binding
        throw new NotImplementedException();
    }
}