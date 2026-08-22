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

        if (element.ValueKind == JsonValueKind.Object && DictionaryValueType(type) is { } valueType)
            return SalvageDictionary(element, type, valueType, path, options, dropped);

        // A collection this cannot take apart entry-by-entry is DROPPED, never handed to SalvageObject.
        //
        // Dictionary and List both have parameterless constructors, so without this they were salvaged as
        // ordinary objects — iterating their CLR properties (Comparer, Count, Keys), none of which is
        // writable — and came back EMPTY with nothing recorded. A settings file whose Connections was written
        // as an object lost every connection, silently, and the mutilated result was then saved over the only
        // copy. That is the incident this whole feature exists to prevent, reproduced inside the mechanism
        // meant to prevent it. (fable)
        if (IsCollection(type))
        {
            dropped.Add(path);
            return null;
        }

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

    // Entry by entry, for the same reason a list is salvaged element by element: one unreadable value should
    // cost that value. Emptying the dictionary would take the reader's other inputs with it — and Azure's
    // resourceName lives in one of these. (fable)
    private static object SalvageDictionary(
        JsonElement element, Type dictionaryType, Type valueType, string path,
        JsonSerializerOptions options, List<string> dropped)
    {
        var dictionary = (IDictionary)Activator.CreateInstance(dictionaryType)!;
        foreach (var entry in element.EnumerateObject())
        {
            var value = Node(entry.Value, valueType, $"{path}.{entry.Name}", options, dropped);
            if (value is not null) dictionary[entry.Name] = value;
        }
        return dictionary;
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

            if (json.ValueKind == JsonValueKind.Null)
            {
                // An explicit null needs three answers, not one.
                //
                // For a COLLECTION, keeping the initializer is right — the same reasoning as #787's
                // null-coalescing from the other side: the model's `= new()` is what the reader gets, rather
                // than a null nothing downstream checks for.
                //
                // For anything else nullable, the null is a STORED VALUE and must be preserved. Skipping it
                // silently replaced AiConnectionRecord.AuthScheme's null — which means "a bare credential,
                // no scheme", and is exactly what a saved Azure connection holds — with its "Bearer"
                // initializer, then wrote that back. The connection would send `api-key: Bearer <key>` and
                // fail to authenticate with nothing on screen to explain it. (fable)
                //
                // For a non-nullable value type, a null is unreadable: record it rather than pretending the
                // initializer was what the file said.
                if (IsCollection(property.PropertyType)) continue;
                if (IsNullable(property.PropertyType)) { property.SetValue(instance, null); continue; }
                dropped.Add($"{path}.{property.Name}");
                continue;
            }

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

    private static Type? DictionaryValueType(Type type) =>
        type.IsGenericType
        && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)
        && type.GetGenericArguments()[0] == typeof(string)
            ? type.GetGenericArguments()[1]
            : null;

    private static bool IsCollection(Type type) =>
        type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

    private static bool IsNullable(Type type) =>
        !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    private static bool HasParameterlessConstructor(Type type) =>
        !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) is not null;
}
