using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// Can this build load what an EARLIER build wrote? (#787)
///
/// <para>Every persisted model here is round-tripped in some test — serialize the current shape, deserialize
/// it, assert it survives. Those tests pass the whole time a type change is breaking real installations,
/// because both halves speak the new shape. The test that fails is this one, and nobody writes it because
/// after a type change the old shape no longer exists in the code to write it against. So it has to be
/// captured as a literal, which is what the fixtures below are.</para>
///
/// <para><b>What went wrong without it.</b> <c>AiConnectionRecord.Headers</c> changed from
/// <c>Dictionary&lt;string, string&gt;</c> to <c>List&lt;AiHeaderRecord&gt;</c> (#771). A settings file
/// written the previous day threw <c>JsonException</c> on load — and <c>SettingsService</c> answers a failed
/// load by replacing the settings with defaults and SAVING them, so the file was not merely unreadable, it was
/// overwritten. One property cost a whole configuration: books directory, fonts, layout, every connection and
/// every hand-built model list. (#784 fixed the reading; #785 covers the blast radius.)</para>
///
/// <para><b>These assert the load path copes, not that it round-trips.</b> An old file legitimately lacks
/// fields that exist now, and a file from a NEWER build carries fields that do not exist yet. Neither is an
/// error; both must leave the rest of the file intact.</para>
///
/// <para>Adding a fixture here is the cheap half. The discipline worth keeping is capturing one BEFORE
/// changing a persisted type, while the old shape still exists to copy.</para>
/// </summary>
public sealed class OlderSettingsFilesLoadTests : IDisposable
{
    private readonly string _dir;

    public OlderSettingsFilesLoadTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"old-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    private async Task<Settings> Load(string json)
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "settings.json"), json);
        var svc = new SettingsService(_dir);
        await svc.LoadSettingsAsync();
        return svc.Settings;
    }

    // ---- the shape that actually broke ----

    // Headers as a JSON OBJECT, which is what every build before #771 wrote.
    [Fact]
    public async Task A_pre_771_file_with_object_headers_still_loads_and_keeps_them()
    {
        var settings = await Load("""
        {
          "Version": "1.0",
          "XmlBooksDirectory": "/books",
          "Ai": { "Enabled": true, "Chat": { "Enabled": true, "ActiveConnectionId": "c1",
            "Connections": [ {
              "Id": "c1", "DisplayName": "A gateway", "Kind": "openai-compatible",
              "BaseUrl": "https://example.invalid/v1",
              "Headers": { "X-Gateway": "token", "X-Title": "CST Reader" }
            } ] } }
        }
        """);

        // The whole file survives — this is the assertion that failed in the field, and it failed for
        // XmlBooksDirectory as much as for the connection.
        Assert.Equal("/books", settings.XmlBooksDirectory);
        var connection = Assert.Single(settings.Ai.Chat.Connections);
        Assert.Equal("A gateway", connection.DisplayName);
        Assert.Equal(2, connection.Headers.Count);
        Assert.Contains(connection.Headers, h => h.Name == "X-Gateway" && h.Value == "token");
    }

    // The second hole, found only by writing this kind of test: System.Text.Json does not consult a converter
    // for null unless it opts in via HandleNull, so an explicit null bypassed the shape handling entirely.
    [Fact]
    public async Task Null_headers_load_as_an_empty_list_not_as_null()
    {
        var settings = await Load("""
        {
          "XmlBooksDirectory": "/books",
          "Ai": { "Chat": { "Connections": [ {
            "Id": "c1", "Kind": "openai-compatible", "BaseUrl": "https://example.invalid/v1",
            "Headers": null } ] } }
        }
        """);

        var connection = Assert.Single(settings.Ai.Chat.Connections);
        Assert.NotNull(connection.Headers);          // the next thing to enumerate it must not throw
        Assert.Empty(connection.Headers);
    }

    // ---- files older than the AI work entirely ----

    // Beta 5 shipped before any of the AI configuration existed, so its file has no Ai section at all. A
    // missing section is an ordinary state, not a defect.
    [Fact]
    public async Task A_file_with_no_Ai_section_loads_and_defaults_it()
    {
        var settings = await Load("""
        {
          "Version": "1.0",
          "XmlBooksDirectory": "/books",
          "FontSettings": { },
          "XmlUpdateSettings": { }
        }
        """);

        Assert.Equal("/books", settings.XmlBooksDirectory);
        Assert.NotNull(settings.Ai);
        Assert.False(settings.Ai.Enabled);           // off is the shipped default
        Assert.Empty(settings.Ai.Chat.Connections);
    }

    [Fact]
    public async Task A_nearly_empty_file_loads_rather_than_being_discarded()
    {
        var settings = await Load("""{ "Version": "1.0" }""");

        Assert.NotNull(settings.FontSettings);
        Assert.NotNull(settings.Ai);
        // ApplyFirstRunDefaults must still have run: an empty XmlBooksDirectory changes update and indexing
        // behaviour, which is the whole point of STATE-3.
        Assert.False(string.IsNullOrEmpty(settings.XmlBooksDirectory));
    }

    // ---- files from a build NEWER than this one ----

    // A reader who runs a newer build, then goes back — or restores a backup from one. Unknown properties are
    // not an error, and treating them as one would discard a working configuration for containing something we
    // do not need. This is the likelier case in practice than genuine corruption.
    [Fact]
    public async Task Properties_this_build_does_not_know_are_ignored_not_fatal()
    {
        var settings = await Load("""
        {
          "Version": "9.9",
          "XmlBooksDirectory": "/books",
          "SomeFutureSetting": { "nested": [1, 2, 3] },
          "Ai": { "Enabled": true, "FutureAiKnob": "on",
            "Chat": { "Connections": [ {
              "Id": "c1", "Kind": "openai-compatible", "BaseUrl": "https://example.invalid/v1",
              "Headers": [ { "Name": "X-A", "Value": "1" } ],
              "FutureConnectionField": 42 } ] } }
        }
        """);

        Assert.Equal("/books", settings.XmlBooksDirectory);
        Assert.True(settings.Ai.Enabled);
        var connection = Assert.Single(settings.Ai.Chat.Connections);
        Assert.Equal("X-A", Assert.Single(connection.Headers).Name);
    }

    // ---- the current shape, so the fixtures above cannot silently stop being "old" ----

    [Fact]
    public async Task The_current_header_shape_still_loads()
    {
        var settings = await Load("""
        {
          "XmlBooksDirectory": "/books",
          "Ai": { "Chat": { "Connections": [ {
            "Id": "c1", "Kind": "openai-compatible", "BaseUrl": "https://example.invalid/v1",
            "Headers": [ { "Name": "X-A", "Value": "1", "Secret": false } ] } ] } }
        }
        """);

        var header = Assert.Single(Assert.Single(settings.Ai.Chat.Connections).Headers);
        Assert.Equal("X-A", header.Name);
        Assert.Equal("1", header.Value);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
