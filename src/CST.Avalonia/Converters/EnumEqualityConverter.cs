using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CST.Avalonia.Converters
{
    /// <summary>
    /// Converts between an enum value and a boolean for radio button binding.
    /// Used to bind radio buttons to enum properties.
    /// </summary>
    public class EnumEqualityConverter : IValueConverter
    {
        public static readonly EnumEqualityConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            // Get the enum value as string
            var enumValue = value.ToString();
            var parameterValue = parameter.ToString();

            return string.Equals(enumValue, parameterValue, StringComparison.OrdinalIgnoreCase);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isChecked && isChecked && parameter != null)
            {
                // Parse the parameter string back to enum
                return Enum.Parse(targetType, parameter.ToString()!);
            }

            // Return unset value for two-way binding when not checked
            return global::Avalonia.AvaloniaProperty.UnsetValue;
        }
    }
}
