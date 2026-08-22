using System.Collections.Generic;
using CST.Avalonia.Models;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// Which launches run a login shell, and which do not. (#817)
///
/// <para>CST Reader opens Pāli texts. The AI master switch ships off, so the great majority of launches must
/// not spawn a shell at all — both because it costs everyone for a feature few use, and because a reader
/// looking at their process list should not find a <c>zsh -il</c> a text reader cannot account for.</para>
/// </summary>
public sealed class ShellEnvironmentGateTests
{
    private static Settings WithConnection(bool aiEnabled, bool usesEnvironmentKey, string? variable)
    {
        var settings = new Settings();
        settings.Ai.Enabled = aiEnabled;
        settings.Ai.Chat.Connections = new List<AiConnectionRecord>
        {
            new() { Id = "one", UsesEnvironmentKey = usesEnvironmentKey, EnvironmentVariable = variable },
        };
        return settings;
    }

    [Fact]
    public void An_ordinary_launch_does_not_probe()
    {
        Assert.False(CST.Avalonia.App.ShouldPrimeShellEnvironment(new Settings()));
    }

    [Fact]
    public void No_settings_at_all_does_not_probe()
    {
        Assert.False(CST.Avalonia.App.ShouldPrimeShellEnvironment(null));
    }

    [Fact]
    public void The_ai_master_switch_being_on_is_enough()
    {
        var settings = new Settings();
        settings.Ai.Enabled = true;
        Assert.True(CST.Avalonia.App.ShouldPrimeShellEnvironment(settings));
    }

    [Fact]
    public void A_stored_connection_that_uses_an_environment_key_probes_even_with_the_master_switch_off()
    {
        // The arm that is easy to leave out and expensive to omit. A reader can hold an adopted connection
        // while the chat surface is off — Surface C answers under the same master switch — and without this
        // their first request of the session races the probe and loses.
        Assert.True(CST.Avalonia.App.ShouldPrimeShellEnvironment(
            WithConnection(aiEnabled: false, usesEnvironmentKey: true, variable: "OPENAI_API_KEY")));
    }

    [Fact]
    public void A_connection_that_stores_its_own_key_does_not_probe()
    {
        Assert.False(CST.Avalonia.App.ShouldPrimeShellEnvironment(
            WithConnection(aiEnabled: false, usesEnvironmentKey: false, variable: null)));
    }

    [Fact]
    public void An_adopted_connection_with_no_variable_recorded_does_not_probe()
    {
        // There is nothing to look up, so there is nothing a shell could tell us.
        Assert.False(CST.Avalonia.App.ShouldPrimeShellEnvironment(
            WithConnection(aiEnabled: false, usesEnvironmentKey: true, variable: "  ")));
    }
}
