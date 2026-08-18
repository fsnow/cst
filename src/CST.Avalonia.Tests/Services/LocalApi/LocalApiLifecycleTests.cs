using System;
using System.IO;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services.LocalApi;
using Serilog;
using Xunit;

namespace CST.Avalonia.Tests.Services.LocalApi;

/// <summary>
/// #529: the AI settings used to be read once at startup, so toggling any of the three switches did nothing
/// until relaunch. These cover the transitions — including the ones the issue calls out as the hard part,
/// where one surface stays on while the other flips.
///
/// <para>These start a real Kestrel host on an ephemeral loopback port. That is deliberate: the defect being
/// fixed is about actually starting and stopping a server, and a mocked host would assert the diff logic while
/// leaving the thing that broke untested.</para>
/// </summary>
public class LocalApiLifecycleTests : IDisposable
{
    private readonly string _dir;
    private readonly AiSettings _settings = new();

    public LocalApiLifecycleTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cst-529-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private LocalApiLifecycle NewLifecycle() =>
        new(services: null, appVersion: "test", handshakeDirectory: _dir,
            logger: Serilog.Core.Logger.None, readSettings: () => _settings);

    private void Configure(bool master, bool rest, bool mcp)
    {
        _settings.Enabled = master;
        _settings.LocalApi.Enabled = rest;
        _settings.LocalApi.EnableMcpServer = mcp;
    }

    private bool HandshakeExists => File.Exists(LocalApiInfo.PathIn(_dir));

    // ---- the defect ------------------------------------------------------------------------------------

    /// <summary>The whole point: a toggle takes effect without a restart.</summary>
    [Fact]
    public async Task Turning_the_master_switch_on_starts_the_server()
    {
        Configure(master: false, rest: true, mcp: true);
        await using var life = NewLifecycle();
        await life.ApplyAsync();
        Assert.Null(life.Server);

        Configure(master: true, rest: true, mcp: true);
        Assert.True(await life.ApplyAsync());

        Assert.NotNull(life.Server);
        Assert.True(life.Server!.IsRunning);
        Assert.True(HandshakeExists);
    }

    [Fact]
    public async Task Turning_the_master_switch_off_stops_the_server_and_removes_the_handshake_file()
    {
        Configure(master: true, rest: true, mcp: false);
        await using var life = NewLifecycle();
        await life.ApplyAsync();
        Assert.True(HandshakeExists);

        Configure(master: false, rest: true, mcp: false);
        Assert.True(await life.ApplyAsync());

        Assert.Null(life.Server);
        Assert.False(HandshakeExists);
    }

    // ---- the four-valued surface space ------------------------------------------------------------------

    /// <summary>
    /// The transition the issue names as the hard one: MCP is added while REST stays on. The surfaces are fixed
    /// at construction, so this necessarily rebuilds the host — what must hold is that both surfaces are
    /// serving afterwards, not that the port survived.
    /// </summary>
    [Fact]
    public async Task Adding_the_second_surface_leaves_both_serving()
    {
        Configure(master: true, rest: true, mcp: false);
        await using var life = NewLifecycle();
        await life.ApplyAsync();
        Assert.Equal(new LocalApiLifecycle.Surfaces(Rest: true, Mcp: false), life.Running);

        Configure(master: true, rest: true, mcp: true);
        Assert.True(await life.ApplyAsync());

        Assert.Equal(new LocalApiLifecycle.Surfaces(Rest: true, Mcp: true), life.Running);
        Assert.True(life.Server!.IsRunning);
    }

    /// <summary>REST-only → MCP-only. Both flags change in one apply; the server must end up serving exactly
    /// the new pair rather than the union or the old one.</summary>
    [Fact]
    public async Task Swapping_which_surface_is_on_serves_only_the_new_one()
    {
        Configure(master: true, rest: true, mcp: false);
        await using var life = NewLifecycle();
        await life.ApplyAsync();

        Configure(master: true, rest: false, mcp: true);
        Assert.True(await life.ApplyAsync());

        Assert.Equal(new LocalApiLifecycle.Surfaces(Rest: false, Mcp: true), life.Running);
        Assert.True(life.Server!.IsRunning);
    }

    /// <summary>Turning off the last remaining surface stops the host — the master switch is not the only way
    /// to reach "off".</summary>
    [Fact]
    public async Task Turning_off_the_last_surface_stops_the_host()
    {
        Configure(master: true, rest: false, mcp: true);
        await using var life = NewLifecycle();
        await life.ApplyAsync();
        Assert.NotNull(life.Server);

        Configure(master: true, rest: false, mcp: false);
        Assert.True(await life.ApplyAsync());

        Assert.Null(life.Server);
        Assert.False(HandshakeExists);
    }

    // ---- not disturbing a healthy server ----------------------------------------------------------------

    /// <summary>
    /// An apply that changes nothing must NOT bounce the server. Applies fire on every AI settings change, and
    /// most of them — the API key, the model, remote-control consent — have nothing to do with which surfaces
    /// are mounted. Restarting on those would hand every connected client a new port and token for no reason.
    /// </summary>
    [Fact]
    public async Task An_apply_that_changes_nothing_leaves_the_server_untouched()
    {
        Configure(master: true, rest: true, mcp: true);
        await using var life = NewLifecycle();
        await life.ApplyAsync();

        var before = life.Server;
        var urlBefore = life.Server!.BaseUrl;
        var tokenBefore = life.Server!.Token;

        Assert.False(await life.ApplyAsync());   // reports "no transition"

        Assert.Same(before, life.Server);
        Assert.Equal(urlBefore, life.Server!.BaseUrl);
        Assert.Equal(tokenBefore, life.Server!.Token);
    }

    /// <summary>Changing an unrelated AI setting must not restart the server either — the same guarantee, from
    /// the direction a user would actually hit it.</summary>
    [Fact]
    public async Task Changing_an_unrelated_ai_setting_does_not_restart_the_server()
    {
        Configure(master: true, rest: true, mcp: false);
        await using var life = NewLifecycle();
        await life.ApplyAsync();
        var tokenBefore = life.Server!.Token;

        _settings.LocalApi.AllowRemoteControl = !_settings.LocalApi.AllowRemoteControl;
        Assert.False(await life.ApplyAsync());

        Assert.Equal(tokenBefore, life.Server!.Token);
    }

    // ---- rapid toggling --------------------------------------------------------------------------------

    /// <summary>
    /// The trigger is a checkbox, and a user can flip one faster than Kestrel can bind and release a port.
    /// Overlapping applies must serialise and settle on whatever the settings say LAST — and in particular must
    /// not leave a server running after the reader switched AI off, which is the wrong direction to fail in.
    /// </summary>
    [Fact]
    public async Task Rapid_toggling_settles_on_the_final_setting()
    {
        await using var life = NewLifecycle();

        for (int i = 0; i < 6; i++)
        {
            Configure(master: i % 2 == 0, rest: true, mcp: i % 3 == 0);
            _ = life.ApplyAsync();   // deliberately not awaited: overlap the transitions
        }

        Configure(master: false, rest: true, mcp: true);
        await life.ApplyAsync();

        Assert.Null(life.Server);
        Assert.False(HandshakeExists);
    }

    // ---- start failure ---------------------------------------------------------------------------------

    /// <summary>A stopped server must clear the failure flag: a stale one would keep Settings warning about a
    /// server the reader has since switched off.</summary>
    [Fact]
    public async Task Stopping_clears_the_start_failure_flag()
    {
        Configure(master: true, rest: true, mcp: false);
        await using var life = NewLifecycle();
        await life.ApplyAsync();
        Assert.False(life.StartFailed);

        Configure(master: false, rest: false, mcp: false);
        await life.ApplyAsync();

        Assert.False(life.StartFailed);
    }
}
