using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;

namespace CST.Avalonia.Converters
{
    public static class FontHelper
    {
        // Define the attached property for FontFamily
        public static readonly AttachedProperty<string> DynamicFontFamilyProperty =
            AvaloniaProperty.RegisterAttached<Control, string>(
                "DynamicFontFamily", 
                typeof(FontHelper),
                defaultValue: string.Empty,
                inherits: true);

        // Define the attached property for FontSize
        public static readonly AttachedProperty<int> DynamicFontSizeProperty =
            AvaloniaProperty.RegisterAttached<Control, int>(
                "DynamicFontSize",
                typeof(FontHelper),
                defaultValue: 12,
                inherits: true);

        static FontHelper()
        {
            DynamicFontFamilyProperty.Changed.AddClassHandler<TextBlock>(OnDynamicFontFamilyChanged);
            DynamicFontSizeProperty.Changed.AddClassHandler<TextBlock>(OnDynamicFontSizeChanged);
        }

        // FontFamily Getter
        public static string GetDynamicFontFamily(Control element)
        {
            return element.GetValue(DynamicFontFamilyProperty);
        }

        // FontFamily Setter
        public static void SetDynamicFontFamily(Control element, string value)
        {
            element.SetValue(DynamicFontFamilyProperty, value);
        }

        // FontSize Getter
        public static int GetDynamicFontSize(Control element)
        {
            return element.GetValue(DynamicFontSizeProperty);
        }

        // FontSize Setter
        public static void SetDynamicFontSize(Control element, int value)
        {
            element.SetValue(DynamicFontSizeProperty, value);
        }

        // The property changed callback for FontFamily
        private static void OnDynamicFontFamilyChanged(TextBlock textBlock, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.NewValue is string fontFamilyName && !string.IsNullOrEmpty(fontFamilyName))
            {
                try
                {
                    // Create a FontFamily object from the string name and apply it
                    textBlock.FontFamily = new FontFamily(fontFamilyName);
                }
                catch (Exception)
                {
                    // Fallback to default if font is not found
                    textBlock.FontFamily = FontFamily.Default;
                }
            }
        }

        // The property changed callback for FontSize
        private static void OnDynamicFontSizeChanged(TextBlock textBlock, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.NewValue is int fontSize && fontSize > 0)
            {
                textBlock.FontSize = fontSize;
            }
        }
    }
}