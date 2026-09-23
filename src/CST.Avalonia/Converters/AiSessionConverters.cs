using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Ai;

namespace CST.Avalonia.Converters;

/// <summary>
/// A session row's books, as the list shows them: each book's name as its tab and the Recent Books menu show
/// it (<see cref="RecentBooksService.DisplayName"/>, Latin script). (#997)
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
        string.Join(", ", ids.Select(id => Lookup(id) is { } book ? RecentBooksService.DisplayName(book) : id));

    private static Book? Lookup(string id)
    {
        try { return Books.Inst[id]; }
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

/// <summary>
/// A compaction summary for display, as answer blocks: the <c>[[…]]</c> Pāli markers kept in the stored summary
/// (it is replayed to the model) are stripped, and the Markdown the model writes is parsed as an answer's is.
/// (#998)
/// </summary>
public sealed class AiCompactionSummaryConverter : IValueConverter
{
    public static readonly AiCompactionSummaryConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        AnswerMarkup.Parse(value is string text ? PaliQuoteMarkers.Strip(text) : null);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
