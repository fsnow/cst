using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using CST.Avalonia.ViewModels;
using CST.Conversion;

namespace CST.Avalonia.Converters;

/// <summary>
/// A session row's books, as the list shows them: the book's own name in Latin script, title-cased as the book
/// tabs show it, cut to the leaf the citation captions use (<see cref="AiAssistantViewModel.LeafBookName"/>).
/// (#997)
///
/// <para>[observed] <see cref="AiSessionRowViewModel.BookIds"/> carries XML file names (<c>s0101m.mul.xml</c>),
/// and nothing in the session summary carries a book name, so the name is looked up here. An id the book list
/// does not know is shown as itself rather than dropped: a row should not claim fewer books than it has.</para>
/// </summary>
public sealed class AiSessionBooksConverter : IValueConverter
{
    public static readonly AiSessionBooksConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is IEnumerable<string> ids ? Format(ids) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    internal static string Format(IEnumerable<string> ids) =>
        string.Join(", ", ids.Select(id =>
        {
            var path = LongNavPath(id);
            if (string.IsNullOrWhiteSpace(path)) return id;
            var leaf = AiAssistantViewModel.LeafBookName(
                ScriptConverter.Convert(path, Script.Devanagari, Script.Latin, toTitleCase: true));
            return leaf.Length == 0 ? id : leaf;
        }));

    private static string? LongNavPath(string id)
    {
        try { return Books.Inst[id].LongNavPath; }
        catch (KeyNotFoundException) { return null; }
    }
}

/// <summary>
/// When a session row was last active: the time alone for today, the date for anything older, and the year only
/// when it is not this one. (#997) [suggestion]
/// </summary>
public sealed class AiSessionTimeConverter : IValueConverter
{
    public static readonly AiSessionTimeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTimeOffset at ? Format(at, DateTimeOffset.Now, culture) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    internal static string Format(DateTimeOffset at, DateTimeOffset now, CultureInfo culture)
    {
        var local = at.ToOffset(now.Offset);
        if (local.Date == now.Date) return local.ToString("t", culture);
        return local.Year == now.Year
            ? local.ToString("MMM d", culture)
            : local.ToString("MMM d, yyyy", culture);
    }
}

/// <summary>"1 turn", "3 turns". (#997)</summary>
public sealed class AiSessionTurnCountConverter : IValueConverter
{
    public static readonly AiSessionTurnCountConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int n ? (n == 1 ? "1 turn" : $"{n} turns") : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
