using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;

namespace CST.Avalonia.Services;

/// <summary>
/// Reads a settings file that <see cref="JsonSerializer"/> refuses, keeping every part it can. (#803)
///
/// <para><b>Why this exists.</b> A persisted type changed (#771) and a file written the previous day threw on
/// one property — <c>$.Ai.Chat.Connections[0].Headers</c>. `Deserialize` is all-or-nothing, so that one
/// property cost the books directory, the fonts, the layout, every connection and every hand-built model
/// list. #785 made that survivable by restoring the previous save; this makes it *proportionate*: the same
/// incident should have cost a header.</para>
///
/// <para><b>It runs only after a strict read has failed.</b> An ordinary file takes the ordinary path and
/// this code never executes, which is deliberate — tolerance that runs on every load is tolerance that hides
/// a shape change from everyone until it has hidden it for a year.</para>
///
/// <para><b>What is dropped is reported, never silent.</b> A section quietly replaced by its defaults is the
/// same defect one level down: the file loaded, nothing was said, and the reader's connections are gone. Every
/// node this cannot read is named in <c>dropped</c>, by path.</para>
/// </summary>
internal static class TolerantSettingsReader
{
    /// <summary>
    /// Rebuild <typeparamref name="T"/> from <paramref name="json"/>, keeping what parses.
    /// Returns null only when the document is not a JSON object at all — nothing to salvage.
    /// </summary>
    internal static T? Read<T>(string json, JsonSerializerOptions options, out IReadOnlyList<string> dropped)
        where T : class, new()
    {
        var lost = new List<string>();
        dropped = lost;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            return (T?)Node(document.RootElement, typeof(T), "$", options, lost);
        }
        catch (JsonException)
        {
            // Not even well-formed JSON — a torn write, or a file that is not settings at all. There is
            // nothing here to be tolerant OF, and the caller falls through to its backups.
            return null;
        }
    }

    /// <summary>
    /// One node: try it whole, and only on failure take it apart.
    ///
    /// <para>Trying whole first is what keeps this honest. A list of a hundred connections with one bad entry
    /// deserializes ninety-nine of them through the ordinary path, with the converters, naming policy and
    /// attributes the real load uses — this only steps in where that path has already refused.</para>
    /// </summary>
    private static object? Node(
        JsonElement element, Type type, string path, JsonSerializerOptions options, List<string> dropped)
    {
        try
        {
            return element.Deserialize(type, options);
        }
        catch (JsonException)
        {
            // fall through and salvage
        }
        catch (NotSupportedException)
        {
            // A converter refusing the shape outright — same treatment.
        }

        if (element.ValueKind == JsonValueKind.Array && ListElementType(type) is { } itemType)
            return SalvageList(element, type, itemType, path, options, dropped);

        if (element.ValueKind == JsonValueKind.Object && HasParameterlessConstructor(type))
            return SalvageObject(element, type, path, options, dropped);

        // A leaf we cannot read — a number where a string belongs. The caller keeps its default and says so.
        dropped.Add(path);
        return null;
    }

    // Keep every element that reads; name the ones that do not. Dropping one connection is a loss the reader
    // can see and repair; dropping the file is not.
    private static object SalvageList(
        JsonElement element, Type listType, Type itemType, string path,
        JsonSerializerOptions options, List<string> dropped)
    {
        var list = (IList)Activator.CreateInstance(listType)!;
        int index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var value = Node(item, itemType, $"{path}[{index}]", options, dropped);
            if (value is not null) list.Add(value);
            index++;
        }
        return list;
    }

    // Property by property, so a bad Ai section costs the AI settings and not the books directory.
    private static object SalvageObject(
        JsonElement element, Type type, string path, JsonSerializerOptions options, List<string> dropped)
    {
        var instance = Activator.CreateInstance(type)!;
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite || property.GetIndexParameters().Length > 0) continue;
            if (!TryGetProperty(element, property.Name, out var json)) continue;

            // An explicit null is a value, and leaving the initializer in place is the RIGHT answer for it —
            // the same reasoning as #787's null-coalescing, from the other side: the model's `= new()` is
            // what the reader gets, rather than a null nothing downstream checks for.
            if (json.ValueKind == JsonValueKind.Null) continue;

            var value = Node(json, property.PropertyType, $"{path}.{property.Name}", options, dropped);
            if (value is not null) property.SetValue(instance, value);
        }
        return instance;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        // Case-insensitive, matching the reader's own PropertyNameCaseInsensitive. Hand-edited files are the
        // common case here and their casing is whatever the person typed.
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static Type? ListElementType(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)
            ? type.GetGenericArguments()[0]
            : null;

    private static bool HasParameterlessConstructor(Type type) =>
        !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) is not null;
}
