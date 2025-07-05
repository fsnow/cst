using System.Text.Json.Serialization;
using CST.Conversion;

namespace CST.Avalonia.Models;

/// <summary>
/// Represents a chapter or section entry in a book's table of contents.
/// Port of CST4's DivTag class with JSON serialization support.
/// </summary>
public class DivTag
{
    /// <summary>
    /// The unique identifier of the div element (e.g., "dn1_9")
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>
    /// The chapter heading text with indentation spaces (originally in Devanagari)
    /// </summary>
    [JsonPropertyName("heading")]
    public string Heading { get; set; } = "";

    /// <summary>
    /// The indentation level based on div nesting (calculated from ID underscores)
    /// </summary>
    [JsonPropertyName("indentLevel")]
    public int IndentLevel { get; set; }

    /// <summary>
    /// The script to use for display (not serialized, set at runtime)
    /// </summary>
    [JsonIgnore]
    public Script BookScript { get; set; } = Script.Latin;

    /// <summary>
    /// Default constructor for JSON deserialization
    /// </summary>
    public DivTag()
    {
    }

    /// <summary>
    /// Constructor with all properties
    /// </summary>
    public DivTag(string id, string heading, int indentLevel)
    {
        Id = id;
        Heading = heading;
        IndentLevel = indentLevel;
    }

    /// <summary>
    /// Returns the display text for the chapter, converted to the current BookScript.
    /// This method is called when the DivTag is displayed in a ComboBox.
    /// </summary>
    public override string ToString()
    {
        try
        {
            // Convert from Devanagari (source) to the target BookScript
            string converted = ScriptConverter.Convert(Heading, Script.Devanagari, BookScript, true);
            return converted;
        }
        catch
        {
            // Fallback to original heading if conversion fails
            return Heading;
        }
    }

    /// <summary>
    /// Gets the display text with proper indentation
    /// </summary>
    public string GetDisplayText()
    {
        return ToString();
    }
}