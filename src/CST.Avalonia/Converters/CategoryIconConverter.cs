using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CST.Avalonia.Converters;

/// <summary>
/// Multi-value converter that determines the visibility of folder icons based on IsCategory and IsExpanded states
/// </summary>
public class CategoryIconConverter : IMultiValueConverter
{
    public static readonly CategoryIconConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values?.Count >= 2 && 
            values[0] is bool isCategory && 
            values[1] is bool isExpanded &&
            parameter is string iconType)
        {
            if (!isCategory) return false; // Only show for categories
            
            return iconType.ToLowerInvariant() switch
            {
                "closed" => !isExpanded, // Show closed folder when not expanded
                "open" => isExpanded,    // Show open folder when expanded
                _ => false
            };
        }
        
        return false;
    }
}