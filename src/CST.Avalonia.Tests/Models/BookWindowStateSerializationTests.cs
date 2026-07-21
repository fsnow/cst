using System.Text.Json;
using System.Text.Json.Serialization;
using CST.Avalonia.Models;
using Xunit;

namespace CST.Avalonia.Tests.Models;

/// <summary>
/// #434: BookWindowState.ReadingPosition (the reading-position token) must round-trip through JSON under the
/// app's serializer options so a saved reading position restores exactly on next launch. Also verifies the
/// absent-token case (old file / never captured) deserializes to null and falls back to CurrentAnchor.
/// </summary>
public class BookWindowStateSerializationTests
{
    // Mirror the options used by ApplicationStateService.
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new JsonStringEnumConverter() },
    };

    private static BookWindowState RoundTrip(BookWindowState s) =>
        JsonSerializer.Deserialize<BookWindowState>(JsonSerializer.Serialize(s, Options), Options)!;

    [Fact]
    public void ReadingPosition_token_survives_round_trip()
    {
        var s = new BookWindowState
        {
            BookFileName = "s0101m.mul.xml",
            CurrentAnchor = "V1.0023",
            ReadingPosition = new ReadingPositionToken { Above = "V1.0023", Below = "para145", Fraction = 0.42 }
        };

        var r = RoundTrip(s);

        Assert.NotNull(r.ReadingPosition);
        Assert.Equal("V1.0023", r.ReadingPosition!.Above);
        Assert.Equal("para145", r.ReadingPosition.Below);
        Assert.Equal(0.42, r.ReadingPosition.Fraction, 6);
        Assert.Equal("V1.0023", r.CurrentAnchor); // coarse fallback preserved alongside
    }

    [Fact]
    public void Document_start_token_survives_round_trip()
    {
        // Above=null with a Below is the genuine document-start token; null must survive (not become "").
        var s = new BookWindowState
        {
            BookFileName = "s0101m.mul.xml",
            ReadingPosition = new ReadingPositionToken { Above = null, Below = "V1.0001", Fraction = 0.0 }
        };

        var r = RoundTrip(s);

        Assert.NotNull(r.ReadingPosition);
        Assert.Null(r.ReadingPosition!.Above);
        Assert.Equal("V1.0001", r.ReadingPosition.Below);
    }

    [Fact]
    public void Absent_token_deserializes_null()
    {
        // An old/never-captured state has no reading position → null → restore falls back to CurrentAnchor.
        var r = RoundTrip(new BookWindowState { BookFileName = "s0101m.mul.xml", CurrentAnchor = "para5" });
        Assert.Null(r.ReadingPosition);
        Assert.Equal("para5", r.CurrentAnchor);
    }
}
