using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// SRCH-12: the page budget must be spent on terms that actually occur in the selected books. Truncating
/// the matched-term list before the book filter dropped legitimate terms past the page size when the
/// filter was narrow. These test <see cref="SearchService.SelectTermsWithinBudgetAsync"/> in isolation.
/// </summary>
public class SearchTermBudgetTests
{
    // Terms in the given "survivor" set return a single occurrence with that count; all others return an
    // empty list, standing in for "not present in the selected books".
    private static Func<string, Task<List<BookOccurrence>>> Occurrences(params (string term, int count)[] survivors)
    {
        var map = survivors.ToDictionary(s => s.term, s => s.count);
        return term => Task.FromResult(
            map.TryGetValue(term, out var c)
                ? new List<BookOccurrence> { new BookOccurrence { Count = c } }
                : new List<BookOccurrence>());
    }

    [Fact]
    public async Task Budget_CountsOnlyFilterSurvivors_NotTermsMissingFromSelectedBooks()
    {
        // t1/t3/t5 aren't in the selected books; survivors in order are t2, t4, t6. The old code took the
        // first pageSize (2) terms and THEN filtered -> only t2; t4 and t6 were lost. Now t4 is included.
        var terms = new[] { "t1", "t2", "t3", "t4", "t5", "t6" };

        var (selected, total, truncated) = await SearchService.SelectTermsWithinBudgetAsync(
            terms, pageSize: 2, Occurrences(("t2", 1), ("t4", 1), ("t6", 1)), s => s, CancellationToken.None);

        Assert.Equal(new[] { "t2", "t4" }, selected.Select(x => x.Term));
        Assert.True(truncated); // t6 survives the filter beyond the page
        Assert.Equal(2, total);
    }

    [Fact]
    public async Task Budget_NoTruncation_WhenSurvivorsFitWithinPage_AndDisplayApplied()
    {
        var terms = new[] { "a", "b", "c" };

        var (selected, total, truncated) = await SearchService.SelectTermsWithinBudgetAsync(
            terms, pageSize: 10, Occurrences(("a", 3), ("c", 4)), s => s.ToUpperInvariant(), CancellationToken.None);

        Assert.Equal(new[] { "a", "c" }, selected.Select(x => x.Term));           // "b" missing -> skipped
        Assert.Equal(new[] { "A", "C" }, selected.Select(x => x.DisplayTerm));    // toDisplay applied
        Assert.False(truncated);
        Assert.Equal(7, total);
    }

    [Fact]
    public async Task Budget_ReturnsEmpty_WhenNoTermSurvives()
    {
        var (selected, total, truncated) = await SearchService.SelectTermsWithinBudgetAsync(
            new[] { "x", "y" }, pageSize: 5, Occurrences(), s => s, CancellationToken.None);

        Assert.Empty(selected);
        Assert.Equal(0, total);
        Assert.False(truncated);
    }
}
