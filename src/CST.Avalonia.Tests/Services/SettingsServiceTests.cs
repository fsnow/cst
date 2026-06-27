using System;
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
}
