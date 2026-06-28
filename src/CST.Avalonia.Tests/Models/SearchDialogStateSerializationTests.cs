using System.Text.Json;
using System.Text.Json.Serialization;
using CST.Avalonia.Models;
using Xunit;

namespace CST.Avalonia.Tests.Models;

/// <summary>
/// #87: SearchDialogState must round-trip through JSON exactly under the serializer options the app uses.
/// The app sets DefaultIgnoreCondition = WhenWritingDefault, which drops default-valued (false) bools; a
/// false (unchecked) book-type filter would then be omitted and revert to the model's `= true` default on
/// load. The [JsonIgnore(Condition = Never)] attributes on the model prevent that - these tests guard it.
/// </summary>
public class SearchDialogStateSerializationTests
{
    // Mirror the options used by ApplicationStateService.
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new JsonStringEnumConverter() },
    };

    private static SearchDialogState RoundTrip(SearchDialogState s) =>
        JsonSerializer.Deserialize<SearchDialogState>(JsonSerializer.Serialize(s, Options), Options)!;

    [Fact]
    public void FalseFilters_SurviveRoundTrip()
    {
        var s = new SearchDialogState
        {
            SearchText = "dhamma",
            IncludeVinaya = false,
            IncludeSutta = true,
            IncludeAbhidhamma = false,
            IncludeMula = true,
            IncludeAttha = false,
            IncludeTika = false,
            IncludeOther = false,
        };

        var back = RoundTrip(s);

        Assert.False(back.IncludeVinaya);
        Assert.True(back.IncludeSutta);
        Assert.False(back.IncludeAbhidhamma);
        Assert.True(back.IncludeMula);
        Assert.False(back.IncludeAttha);
        Assert.False(back.IncludeTika);
        Assert.False(back.IncludeOther);
        Assert.Equal("dhamma", back.SearchText);
    }

    [Fact]
    public void DefaultValues_AlsoRoundTrip()
    {
        var back = RoundTrip(new SearchDialogState()); // all defaults
        Assert.True(back.IncludeVinaya);
        Assert.True(back.IncludeOther);
        Assert.Equal(SearchMode.Wildcard, back.SearchMode);
        Assert.Equal(10, back.ProximityDistance);
        Assert.Equal(string.Empty, back.SearchText);
        Assert.False(back.IsTextTypesExpanded);
        Assert.Empty(back.SelectedTerms);
    }

    [Fact]
    public void ExpanderAndSelectedTerms_RoundTrip()
    {
        var s = new SearchDialogState
        {
            IsTextTypesExpanded = true,
            SelectedTerms = { "buddho", "dhammo", "sangho" },
        };

        var back = RoundTrip(s);

        Assert.True(back.IsTextTypesExpanded);
        Assert.Equal(new[] { "buddho", "dhammo", "sangho" }, back.SelectedTerms);
    }

    [Fact]
    public void FalseFilters_AreActuallyWrittenToJson()
    {
        // Directly assert the JSON contains the false filter (the bug was that it was omitted).
        var json = JsonSerializer.Serialize(new SearchDialogState { IncludeVinaya = false }, Options);
        Assert.Contains("\"includeVinaya\": false", json);
    }
}
