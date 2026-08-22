using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// One bad property should cost that property, not the file. (#803)
///
/// <para><b>The incident.</b> A persisted type changed (#771) and a settings file written the previous day
/// threw on <c>$.Ai.Chat.Connections[0].Headers</c>. <c>Deserialize</c> is all-or-nothing, so that one
/// property cost the books directory, the fonts, the layout, every connection and every hand-built model
/// list. #785 made that survivable by restoring the previous save. This makes it proportionate.</para>
///
/// <para>The two answers are not interchangeable, which is why tolerance is tried FIRST: a backup is a
/// previous save, so recovering from one loses everything changed since, while keeping what parses loses only
/// the part genuinely unreadable.</para>
/// </summary>
public sealed class TolerantSettingsLoadTests : IDisposable
{
    private readonly string _dir;

    public TolerantSettingsLoadTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"tolerant-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    private async Task<Settings> Load(string json)
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "settings.json"), json);
        var svc = new SettingsService(_dir);
        await svc.LoadSettingsAsync();
        return svc.Settings;
    }

    // The incident, replayed at the level it should have cost.
    [Fact]
    public async Task An_unreadable_property_costs_that_property_and_nothing_else()
    {
        var settings = await Load("""
        {
          "Version": "1.0",
          "XmlBooksDirectory": "/books",
          "IndexDirectory": "/idx",
          "FontSettings": 12345,
          "Ai": { "Enabled": true, "Chat": { "Enabled": true } }
        }
        """);

        // Everything around it survives — this is the assertion that failed in the field.
        Assert.Equal("/books", settings.XmlBooksDirectory);
        Assert.Equal("/idx", settings.IndexDirectory);
        Assert.True(settings.Ai.Enabled);
        Assert.True(settings.Ai.Chat.Enabled);

        // And the unreadable one is its default, not null.
        Assert.NotNull(settings.FontSettings);
    }

    // A malformed ENTRY should cost that entry. Every real instance of this defect so far has been inside
    // Ai.Chat.Connections — a header shape, a null collection, a null entry.
    //
    // The element here is a bare string where an object belongs, chosen because it is genuinely unreadable:
    // an earlier version of this test used "Headers": 99, which the #784 converter absorbs into an empty
    // list — so the whole file parsed strictly and the test never entered the tolerant path at all. It
    // passed with per-element salvage deleted, which is the mutation it exists to catch.
    [Fact]
    public async Task One_unreadable_connection_does_not_take_the_others_with_it()
    {
        var settings = await Load("""
        {
          "XmlBooksDirectory": "/books",
          "Ai": { "Chat": { "Connections": [
            { "Id": "good-one", "Kind": "openai-compatible", "BaseUrl": "https://a.invalid/v1" },
            "this is not a connection",
            { "Id": "good-two", "Kind": "anthropic", "BaseUrl": "https://b.invalid" }
          ] } }
        }
        """);

        Assert.Equal("/books", settings.XmlBooksDirectory);
        Assert.Equal(
            new[] { "good-one", "good-two" },
            settings.Ai.Chat.Connections.Select(c => c.Id).OrderBy(id => id).ToArray());
        Assert.Equal("https://a.invalid/v1", settings.Ai.Chat.Connections.Single(c => c.Id == "good-one").BaseUrl);
    }

    // And a connection with one unreadable PROPERTY keeps its readable ones, rather than vanishing: its id
    // and base URL parsed perfectly well, and dropping the whole connection is the same error one level down.
    [Fact]
    public async Task A_connection_with_one_bad_property_keeps_the_rest_of_itself()
    {
        var settings = await Load("""
        {
          "XmlBooksDirectory": "/books",
          "Ai": { "Chat": { "Connections": [
            { "Id": "partly", "Kind": "openai-compatible", "BaseUrl": "https://a.invalid/v1",
              "Models": "this should be a list" }
          ] } }
        }
        """);

        var connection = settings.Ai.Chat.Connections.Single();
        Assert.Equal("partly", connection.Id);
        Assert.Equal("https://a.invalid/v1", connection.BaseUrl);
        Assert.NotNull(connection.Models);
        Assert.Empty(connection.Models);
    }

    // A section this build cannot read at all must not cost an unrelated one.
    [Fact]
    public async Task A_bad_section_does_not_cost_the_books_directory()
    {
        var settings = await Load("""
        {
          "XmlBooksDirectory": "/books",
          "FontSettings": { "DefaultFontSize": 14 },
          "Ai": "this used to be an object"
        }
        """);

        Assert.Equal("/books", settings.XmlBooksDirectory);
        Assert.NotNull(settings.Ai);
        Assert.False(settings.Ai.Enabled);       // defaulted, and off is the shipped default
    }

    // Tolerance runs only after a strict read has failed. An ordinary file must take the ordinary path —
    // tolerance on every load is tolerance that hides a shape change from everyone until it has hidden it
    // for a year.
    [Fact]
    public async Task An_ordinary_file_is_read_strictly_and_completely()
    {
        var settings = await Load("""
        {
          "Version": "1.0",
          "XmlBooksDirectory": "/books",
          "IndexDirectory": "/idx",
          "Ai": { "Enabled": true, "Chat": { "Connections": [
            { "Id": "c1", "Kind": "anthropic", "BaseUrl": "https://b.invalid" } ] } }
        }
        """);

        Assert.Equal("/books", settings.XmlBooksDirectory);
        Assert.Equal("/idx", settings.IndexDirectory);
        Assert.Equal("c1", settings.Ai.Chat.Connections.Single().Id);
    }

    // Not JSON at all is not something to be tolerant OF. A torn write has no salvageable structure, and the
    // honest answer is the backup path rather than a half-invented settings object.
    [Fact]
    public async Task A_file_that_is_not_json_falls_through_rather_than_being_salvaged()
    {
        var settings = await Load("{ this is not json");

        Assert.False(string.IsNullOrEmpty(settings.XmlBooksDirectory));   // first-run defaulting ran
        Assert.NotEmpty(Directory.GetFiles(_dir, "settings.unreadable-*.json"));
    }

    // The salvage is written back, so the next launch is an ordinary one rather than another salvage.
    [Fact]
    public async Task What_was_salvaged_is_written_back_as_the_primary_file()
    {
        await Load("""
        { "XmlBooksDirectory": "/books", "FontSettings": 12345 }
        """);

        // The rewritten file must itself parse strictly.
        var rewritten = await File.ReadAllTextAsync(Path.Combine(_dir, "settings.json"));
        var reread = System.Text.Json.JsonSerializer.Deserialize<Settings>(
            rewritten, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(reread);
        Assert.Equal("/books", reread!.XmlBooksDirectory);
    }

    // A salvage keeps the file, so the reader has not lost their configuration AND the cause is still
    // diagnosable — unlike the original behaviour, which overwrote it with defaults.
    [Fact]
    public async Task A_salvaged_file_is_not_destroyed()
    {
        await Load("""
        { "XmlBooksDirectory": "/books", "FontSettings": 12345 }
        """);

        // Nothing was preserved-aside, because nothing was lost: the file was READ, not discarded.
        Assert.Empty(Directory.GetFiles(_dir, "settings.unreadable-*.json"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
