using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// The settings half of the durability pass: concurrent saves, the salvage path's converter gap, a script
/// key that went missing, and what an older build does to a newer build's file. (#878, #880, #881, #883)
/// </summary>
public sealed class SettingsDurabilityTests : IDisposable
{
    private readonly string _dir;

    public SettingsDurabilityTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"settings-durability-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    // ---- concurrent saves (#878) -----------------------------------------------------------------------

    /// <summary>
    /// Saves fired at once still leave a file that parses.
    ///
    /// <para>They shared one <c>settings.json.tmp</c>: A could finish writing the temp file, B truncate and
    /// start rewriting it, and A's <c>File.Replace</c> promote B's half-written temp over the real file.
    /// Concurrent callers are ordinary — the 750ms debounce flushes on a pool thread while
    /// <c>XmlUpdateService</c> and <c>IndexingService</c> save directly from background flows.</para>
    ///
    /// <para>A torn write is a race, so this cannot prove the lock by failing reliably without it — what it
    /// pins is that the serialized path is correct under concurrency and stays that way. The lock's absence
    /// is the mechanism; the reviewer traced it.</para>
    /// </summary>
    [Fact]
    public async Task Concurrent_saves_leave_a_file_that_parses()
    {
        var service = new SettingsService(_dir);
        service.Settings.XmlBooksDirectory = Path.Combine(_dir, "books");

        await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => service.SaveSettingsAsync()));

        var reloaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(reloaded);
        Assert.Equal(service.Settings.XmlBooksDirectory, reloaded!.XmlBooksDirectory);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    // ---- the salvage path's converter gap (#880) -------------------------------------------------------

    /// <summary>
    /// A property with its own converter is read by that converter on the salvage path too.
    ///
    /// <para><c>AiConnectionRecord.Headers</c> declares <c>AiHeaderRecordListConverter</c>, which reads the
    /// legacy dict-shaped form. Taken apart property-by-property the node is an object where a List is
    /// expected, so it landed in the drop-the-collection branch and the headers were discarded — the salvage
    /// path being strictly weaker than the strict path, in the one mechanism whose job is to lose as little
    /// as possible.</para>
    ///
    /// <para>The connection below carries a number where <c>displayName</c> expects a string. That is what
    /// forces this record to be taken apart at all — <see cref="TolerantSettingsReader"/> tries a whole-node
    /// read at every level first, precisely so the ordinary converters apply wherever they can, and the
    /// converter gap only shows once something ELSE on the same object has failed. A fixture broken further
    /// up the tree does not reach it: the <c>ai</c> node deserializes whole and the headers were never at
    /// risk.</para>
    /// </summary>
    [Fact]
    public void A_property_with_its_own_converter_survives_the_salvage()
    {
        const string json = """
        {
          "version": "1.0",
          "ai": { "chat": { "connections": [ {
              "id": "mine",
              "displayName": 12345,
              "headers": { "X-Org": "acme", "X-Team": "pali" }
          } ] } }
        }
        """;

        var salvaged = TolerantSettingsReader.Read<Settings>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, out var dropped);

        Assert.NotNull(salvaged);
        var connection = Assert.Single(salvaged!.Ai.Chat.Connections);
        Assert.Equal(2, connection.Headers.Count);
        Assert.Contains(connection.Headers, h => h.Name == "X-Org" && h.Value == "acme");
        Assert.DoesNotContain(dropped, d => d.Contains("Headers", StringComparison.OrdinalIgnoreCase));
    }

    // ---- a script key that went missing (#881) ---------------------------------------------------------

    /// <summary>
    /// Sanitize puts back a canonical script key that is absent.
    ///
    /// <para>The Appearance panel builds its rows by enumerating this dictionary, so a key dropped by a
    /// salvage or a hand-edit left that script with no font control at all — permanently, since nothing ever
    /// re-seeded it, and invisibly, since rendering falls back safely.</para>
    /// </summary>
    [Fact]
    public void Sanitize_restores_a_missing_script_font_key()
    {
        var settings = new Settings();
        settings.FontSettings.ScriptFonts.Remove("Myanmar");
        settings.FontSettings.ScriptFonts.Remove("Sinhala");

        var fixes = SettingsValidator.Sanitize(settings);

        Assert.Contains("Myanmar", settings.FontSettings.ScriptFonts.Keys);
        Assert.Contains("Sinhala", settings.FontSettings.ScriptFonts.Keys);
        Assert.Contains(fixes, f => f.Contains("restored missing script-font key 'Myanmar'"));
    }

    /// <summary>Every canonical script is covered, and a reader's own choice is never overwritten by the
    /// re-seed — otherwise this would reset fonts on every launch.</summary>
    [Fact]
    public void The_reseed_covers_every_script_and_keeps_what_the_reader_chose()
    {
        var settings = new Settings();
        settings.FontSettings.ScriptFonts["Devanagari"] =
            new ScriptFontSetting { FontFamily = "Sanskrit 2003", FontSize = 22 };
        settings.FontSettings.ScriptFonts.Clear();
        settings.FontSettings.ScriptFonts["Devanagari"] =
            new ScriptFontSetting { FontFamily = "Sanskrit 2003", FontSize = 22 };

        SettingsValidator.Sanitize(settings);

        Assert.Equal(FontSettings.DefaultScriptFonts().Count, settings.FontSettings.ScriptFonts.Count);
        Assert.Equal("Sanskrit 2003", settings.FontSettings.ScriptFonts["Devanagari"].FontFamily);
        Assert.Equal(22, settings.FontSettings.ScriptFonts["Devanagari"].FontSize);
    }

    /// <summary>Sanitize is idempotent — a second pass must report nothing, or every launch would log a
    /// repair it did not make.</summary>
    [Fact]
    public void The_reseed_is_idempotent()
    {
        var settings = new Settings();
        settings.FontSettings.ScriptFonts.Remove("Khmer");

        SettingsValidator.Sanitize(settings);

        Assert.DoesNotContain(SettingsValidator.Sanitize(settings), f => f.Contains("restored missing"));
    }

    // ---- what an older build does to a newer build's file (#883) ---------------------------------------

    /// <summary>
    /// A settings file from a newer build keeps its unknown top-level properties across a round-trip.
    ///
    /// <para>An unknown property is not an error to System.Text.Json — it is dropped, silently. So a reader
    /// who launches an older build once loses whatever the newer one had added, the next time anything
    /// saves.</para>
    /// </summary>
    [Fact]
    public void Unknown_top_level_properties_survive_a_round_trip()
    {
        const string fromNewerBuild = """
        { "version": "9.9", "xmlBooksDirectory": "/books", "somethingNewerBuildsHave": { "keep": true } }
        """;
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var loaded = JsonSerializer.Deserialize<Settings>(fromNewerBuild, options)!;
        var written = JsonSerializer.Serialize(loaded, options);

        Assert.Contains("somethingNewerBuildsHave", written);
        Assert.Contains("keep", written);
    }

    /// <summary>The same for the other store.</summary>
    [Fact]
    public void Unknown_top_level_state_properties_survive_a_round_trip()
    {
        const string fromNewerBuild = """
        { "version": "9.9", "somethingNewerBuildsHave": [1, 2, 3] }
        """;

        var loaded = JsonSerializer.Deserialize<ApplicationState>(
            fromNewerBuild, ApplicationStateService.JsonOptions)!;
        var written = JsonSerializer.Serialize(loaded, ApplicationStateService.JsonOptions);

        Assert.Contains("somethingNewerBuildsHave", written);
    }

    /// <summary>An ordinary file gains nothing — no stray member in the output, or every settings.json in
    /// the wild grows a property that means nothing.</summary>
    [Fact]
    public void An_ordinary_file_gains_no_extra_member()
    {
        var written = JsonSerializer.Serialize(new Settings(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.DoesNotContain("unknownProperties", written, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Launching an older build over a newer build's file does not rewrite it.
    ///
    /// <para>"Reading as-is" was true of the read and false of what followed: the note itself counted toward
    /// "something changed", so merely launching rewrote the file in the older shape before the reader touched
    /// anything.</para>
    /// </summary>
    [Fact]
    public async Task A_newer_settings_file_is_not_rewritten_on_load()
    {
        const string fromNewerBuild = """
        { "version": "9.9", "xmlBooksDirectory": "/books", "developerSettings": { "logLevel": "Verbose" } }
        """;
        File.WriteAllText(SettingsPath, fromNewerBuild);
        var before = File.GetLastWriteTimeUtc(SettingsPath);

        var service = new SettingsService(_dir);
        await service.LoadSettingsAsync();
        await service.FlushPendingSaveAsync();

        // Sanitize DID repair the bad log level in memory (so the app runs), and that repair is exactly what
        // used to trigger the save.
        Assert.Equal("Information", service.Settings.DeveloperSettings.LogLevel);
        Assert.Equal(before, File.GetLastWriteTimeUtc(SettingsPath));
        Assert.Contains("9.9", File.ReadAllText(SettingsPath));
    }

    /// <summary>
    /// A file this build DOES understand is still written back when it needed repairing — the guard must
    /// only cover the newer-than-supported case, or it would strand every genuine sanitize fix on disk.
    ///
    /// <para>Same repairable defect as the test above (an unknown log level), so the pair differs in exactly
    /// one thing: the version.</para>
    /// </summary>
    [Fact]
    public async Task A_repairable_file_this_build_understands_is_still_written_back()
    {
        File.WriteAllText(SettingsPath, """
        { "version": "1.0", "xmlBooksDirectory": "/books", "developerSettings": { "logLevel": "Verbose" } }
        """);

        var service = new SettingsService(_dir);
        await service.LoadSettingsAsync();
        await service.FlushPendingSaveAsync();

        Assert.Contains("Information", File.ReadAllText(SettingsPath));
        Assert.DoesNotContain("Verbose", File.ReadAllText(SettingsPath));
    }

    // ---- the logger swap (#882) ------------------------------------------------------------------------

    /// <summary>A sink that records its own disposal, and what the global logger was at that moment.</summary>
    private sealed class DisposeSpySink : Serilog.Core.ILogEventSink, IDisposable
    {
        public bool Disposed { get; private set; }
        public Serilog.ILogger? GlobalLoggerWhenDisposed { get; private set; }

        public void Emit(Serilog.Events.LogEvent logEvent) { }

        public void Dispose()
        {
            Disposed = true;
            GlobalLoggerWhenDisposed = Serilog.Log.Logger;
        }
    }

    /// <summary>
    /// Replacing the global logger disposes the one it replaces, and does so AFTER the swap.
    ///
    /// <para>The outgoing pipeline's rolling file sink keeps today's log file open, and Serilog's file sink
    /// defaults to exclusive access — so on Windows the incoming logger's file sink cannot open the same
    /// file and file logging dies silently for the rest of the session, the moment the reader changes the
    /// log level. Disposing releases the handle. This runs on macOS, where the lock does not exist, so what
    /// it pins is the disposal itself rather than the Windows symptom.</para>
    ///
    /// <para>The ordering is the second half and is not decoration: disposing first leaves a window with no
    /// logger at all, and Serilog answers that with a silent no-op logger rather than an error.</para>
    /// </summary>
    [Fact]
    public void Swapping_the_global_logger_disposes_the_one_it_replaces_after_the_swap()
    {
        var spy = new DisposeSpySink();
        var outgoing = new Serilog.LoggerConfiguration().WriteTo.Sink(spy).CreateLogger();
        var incoming = new Serilog.LoggerConfiguration().CreateLogger();
        var saved = Serilog.Log.Logger;

        try
        {
            Serilog.Log.Logger = outgoing;

            CST.Avalonia.ViewModels.DeveloperSettingsViewModel.SwapGlobalLogger(incoming);

            Assert.True(spy.Disposed);
            Assert.Same(incoming, Serilog.Log.Logger);
            Assert.Same(incoming, spy.GlobalLoggerWhenDisposed);   // swapped first, then disposed
        }
        finally
        {
            Serilog.Log.Logger = saved;
            incoming.Dispose();
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
