using System;
using System.IO;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

public class SettingsServiceTests
{
    [Fact]
    public void UpdateSetting_UnknownProperty_ThrowsArgumentException()
    {
        var svc = new SettingsService();
        // A typo'd / non-existent property name must fail fast, not silently no-op. (#63)
        Assert.Throws<ArgumentException>(() => svc.UpdateSetting("NotARealProperty", "x"));
    }

    [Fact]
    public void UpdateSetting_KnownProperty_Applies()
    {
        var svc = new SettingsService();
        const string val = "/tmp/cst-settings-service-test";
        svc.UpdateSetting(nameof(Settings.XmlBooksDirectory), val);
        Assert.Equal(val, svc.Settings.XmlBooksDirectory);
    }

    // STATE-3: a corrupt/torn settings.json must fall through to first-run defaulting, not leave the
    // app running with an empty XmlBooksDirectory (the default used to be set only in the no-file branch).
    [Fact]
    public async Task LoadSettingsAsync_CorruptFile_AppliesXmlDirectoryDefault()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "settings.json"), "{ this is not valid json ]]");

        var svc = new SettingsService(dir.Path);
        await svc.LoadSettingsAsync();

        Assert.False(string.IsNullOrEmpty(svc.Settings.XmlBooksDirectory));
        Assert.Equal(Path.Combine(dir.Path, "xml"), svc.Settings.XmlBooksDirectory);
    }

    // STATE-3: an empty/whitespace file deserializes to null; that path must default too.
    [Fact]
    public async Task LoadSettingsAsync_EmptyFile_AppliesXmlDirectoryDefault()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "settings.json"), "   ");

        var svc = new SettingsService(dir.Path);
        await svc.LoadSettingsAsync();

        Assert.Equal(Path.Combine(dir.Path, "xml"), svc.Settings.XmlBooksDirectory);
    }

    // STATE-3: the atomic write round-trips and leaves no stray .tmp file behind.
    [Fact]
    public async Task SaveSettingsAsync_IsAtomic_RoundTripsAndLeavesNoTempFile()
    {
        using var dir = new TempDir();
        var svc = new SettingsService(dir.Path);
        svc.UpdateSetting(nameof(Settings.XmlBooksDirectory), "/some/books");
        await svc.SaveSettingsAsync();

        Assert.True(File.Exists(Path.Combine(dir.Path, "settings.json")));
        Assert.False(File.Exists(Path.Combine(dir.Path, "settings.json.tmp")));

        var reloaded = new SettingsService(dir.Path);
        await reloaded.LoadSettingsAsync();
        Assert.Equal("/some/books", reloaded.Settings.XmlBooksDirectory);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cst-settings-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
        }
    }
}
