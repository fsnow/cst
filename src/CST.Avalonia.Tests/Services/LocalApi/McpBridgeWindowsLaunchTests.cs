using System;
using System.IO;
using CST.Avalonia.Services.LocalApi.Mcp;
using Xunit;

namespace CST.Avalonia.Tests.Services.LocalApi;

/// <summary>
/// Deciding what to launch when an MCP client connects and CST Reader is not running. (#507)
///
/// <para>The relay is CST Reader itself - it runs as <c>CST.Avalonia.exe --mcp-bridge</c> - so the GUI is the
/// same executable without that flag. That makes the decision a question about this process's own path, which
/// is testable without launching anything.</para>
///
/// <para>The case that matters is the one where it must DECLINE. Under <c>dotnet run</c> the process path is
/// the dotnet host, and starting that with no arguments prints help rather than launching CST Reader - so a
/// naive "just start ProcessPath" would spawn something useless and report success.</para>
/// </summary>
public class McpBridgeWindowsLaunchTests
{
    [Theory]
    [InlineData(@"C:\Program Files\CST Reader\CST.Avalonia.exe")]
    [InlineData(@"C:\Users\me\source\cst\src\CST.Avalonia\bin\Debug\net10.0\win-arm64\CST.Avalonia.exe")]
    [InlineData(@"D:\portable\cst.avalonia.EXE")]
    public void The_app_executable_is_recognised(string path)
    {
        // Installed, developer-built and oddly-cased all name the same program.
        Assert.Equal(path, McpBridge.WindowsAppExecutable(path));
    }

    [Theory]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"C:\Program Files\CST Reader\CST.Avalonia.Tests.exe")]
    public void Anything_else_is_declined_rather_than_launched(string path)
    {
        // dotnet.exe is the real one: `dotnet run -- --mcp-bridge` lands here, and launching it with no
        // arguments prints help. Declining produces an honest "start CST Reader manually" instead of a
        // spawned process that never becomes the app.
        //
        // CST.Avalonia.Tests.exe is included because a prefix match would wrongly accept it.
        Assert.Null(McpBridge.WindowsAppExecutable(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_unknown_process_path_is_declined(string? path)
    {
        // Environment.ProcessPath is documented as possibly null.
        Assert.Null(McpBridge.WindowsAppExecutable(path));
    }

    [Fact]
    public void The_two_platforms_decline_in_the_same_situation()
    {
        // Consistency between the halves: on macOS a `dotnet run` bridge has no enclosing .app bundle and
        // declines; on Windows the same bridge has a dotnet host path and declines. Neither invents a launch
        // it cannot deliver, so the "start CST Reader manually" message means the same thing on both.
        Assert.Null(McpBridge.AppBundleFromExecutablePath(
            "/usr/local/share/dotnet/dotnet"));
        Assert.Null(McpBridge.WindowsAppExecutable(
            @"C:\Program Files\dotnet\dotnet.exe"));
    }

    [Fact]
    public void A_real_bundle_and_a_real_executable_are_both_accepted()
    {
        // The positive counterpart, so the previous test cannot pass merely because both always return null.
        //
        // Separators are normalised before comparing: Path.GetDirectoryName rewrites forward slashes as backslashes when the
        // suite runs on Windows, which would fail this assertion for a reason that has nothing to do with the
        // behaviour under test - AppBundleFromExecutablePath only ever runs on macOS.
        var bundle = McpBridge.AppBundleFromExecutablePath("/Applications/CST Reader.app/Contents/MacOS/CST.Avalonia");
        Assert.Equal("/Applications/CST Reader.app", bundle?.Replace('\\', '/'));

        Assert.Equal(
            @"C:\Program Files\CST Reader\CST.Avalonia.exe",
            McpBridge.WindowsAppExecutable(@"C:\Program Files\CST Reader\CST.Avalonia.exe"));
    }
}
