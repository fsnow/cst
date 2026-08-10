using System.Globalization;
using System.Text.Json;

namespace CST.Avalonia.Services.Ai;

/// <summary>
/// Shape-safe accessors for provider JSON.
///
/// <para><b>Why these exist rather than plain <c>GetString()</c>.</b> A provider's payload is untrusted input,
/// and <see cref="JsonElement"/> throws <see cref="System.InvalidOperationException"/> the moment a value is not
/// the kind you assumed — <c>GetString()</c> on a number, or <c>TryGetProperty</c> on a root that is not an
/// object. Inside a streaming iterator that exception escapes to the consumer as an unclassified crash
/// mid-answer, which is exactly the outcome the Error-delta contract exists to prevent. Real traffic hits this:
/// OpenRouter reports mid-stream failures with a NUMERIC <c>code</c>, and a bare <c>data: null</c> chunk parses
/// successfully and then faults on the first property read.</para>
/// </summary>
internal static class AiJson
{
    /// <summary>True when the element is an object and can safely be probed for properties.</summary>
    internal static bool IsObject(JsonElement element) => element.ValueKind == JsonValueKind.Object;

    /// <summary>A string property, or null if absent or of any other kind.</summary>
    internal static string? String(JsonElement element, string name) =>
        IsObject(element) && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>An int property, or null if absent or not a number that fits.</summary>
    internal static int? Int(JsonElement element, string name) =>
        IsObject(element) && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    /// <summary>An object property, or null if absent or of another kind.</summary>
    internal static JsonElement? Object(JsonElement element, string name) =>
        IsObject(element) && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    /// <summary>An array property, or null if absent or of another kind.</summary>
    internal static JsonElement? Array(JsonElement element, string name) =>
        IsObject(element) && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value
            : null;

    /// <summary>
    /// An error code, which providers spell as either a string or a number — OpenRouter uses the HTTP status as
    /// a bare number. Numbers are rendered invariantly so the result is always a short token.
    /// </summary>
    internal static string? Code(JsonElement element, string name)
    {
        if (!IsObject(element) || !element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.TryGetInt64(out var number)
                ? number.ToString(CultureInfo.InvariantCulture)
                : value.GetRawText(),
            _ => null,
        };
    }
}
