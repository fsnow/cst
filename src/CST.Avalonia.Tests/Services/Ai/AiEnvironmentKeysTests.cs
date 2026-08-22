using System;
using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.Models.Ai;
using CST.Avalonia.Services.Ai;
using CST.Avalonia.Services.Ai.Credentials;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// Reading the vendor API keys a reader's environment already holds. (#714)
///
/// <para><b>Discovery is automatic; use is not.</b> Nothing here adopts a key — that is a click, recorded on
/// the connection. The separation is the point: OpenCode adopts silently, which produced a connected provider
/// the maintainer had not configured, from a variable he had forgotten was set on his own machine, and then
/// offered no way to disconnect it because an app cannot delete a credential it never stored.</para>
///
/// <para>Every test reads through an injected lookup, never the real environment — a suite that depended on
/// which variables happen to be exported on the machine running it would pass or fail for reasons that have
/// nothing to do with the code.</para>
/// </summary>
public sealed class AiEnvironmentKeysTests
{
    private static AiProviderPreset Preset(string id, params string[] envNames) =>
        new(id, id.ToUpperInvariant(), ChatProviderKind.OpenAiCompatible, "https://example.invalid/v1",
            new AiCredentialMethod[] { new AiCredentialMethod.Env(envNames) },
            Array.Empty<AiInputPrompt>());

    private static AiEnvironmentKeys Keys(params (string Name, string? Value)[] environment)
    {
        var map = environment.ToDictionary(e => e.Name, e => e.Value, StringComparer.Ordinal);
        return new AiEnvironmentKeys(name => map.TryGetValue(name, out var v) ? v : null);
    }

    [Fact]
    public void A_variable_that_is_set_is_found_by_name()
    {
        var keys = Keys(("OPENAI_API_KEY", "sk-test"));

        Assert.Equal("OPENAI_API_KEY", keys.VariableFor(Preset("openai", "OPENAI_API_KEY")));
    }

    [Fact]
    public void A_preset_with_no_variable_set_reports_nothing()
    {
        var keys = Keys(("SOMETHING_ELSE", "x"));

        Assert.Null(keys.VariableFor(Preset("openai", "OPENAI_API_KEY")));
        Assert.Null(keys.ValueFor(Preset("openai", "OPENAI_API_KEY")));
    }

    // The catalogue decides precedence, not us: Google alone answers to three names.
    [Fact]
    public void The_first_variable_the_preset_lists_wins()
    {
        var keys = Keys(
            ("GOOGLE_GENERATIVE_AI_API_KEY", "second"),
            ("GEMINI_API_KEY", "third"),
            ("GOOGLE_API_KEY", "first"));

        var google = Preset("google", "GOOGLE_API_KEY", "GOOGLE_GENERATIVE_AI_API_KEY", "GEMINI_API_KEY");

        Assert.Equal("GOOGLE_API_KEY", keys.VariableFor(google));
        Assert.Equal("first", keys.ValueFor(google));
    }

    [Fact]
    public void A_later_variable_is_used_when_the_earlier_ones_are_unset()
    {
        var keys = Keys(("GEMINI_API_KEY", "third"));
        var google = Preset("google", "GOOGLE_API_KEY", "GOOGLE_GENERATIVE_AI_API_KEY", "GEMINI_API_KEY");

        Assert.Equal("GEMINI_API_KEY", keys.VariableFor(google));
    }

    // `export OPENAI_API_KEY=` in a shell profile, or a CI runner defining every name it knows. Offering to
    // connect with one of these produces an authentication failure the reader cannot explain, from a variable
    // they did not know they had.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_variable_exported_empty_is_not_a_credential(string value)
    {
        var keys = Keys(("OPENAI_API_KEY", value));

        Assert.Null(keys.VariableFor(Preset("openai", "OPENAI_API_KEY")));
    }

    [Fact]
    public void Discovery_names_every_preset_whose_variable_is_present_and_no_others()
    {
        var keys = Keys(("OPENAI_API_KEY", "a"), ("GEMINI_API_KEY", "b"));

        var found = keys.Discover(new[]
        {
            Preset("openai", "OPENAI_API_KEY"),
            Preset("google", "GOOGLE_API_KEY", "GEMINI_API_KEY"),
            Preset("anthropic", "ANTHROPIC_API_KEY"),      // not set
            Preset("ollama"),                              // a local runner declares none
        });

        Assert.Equal(new[] { "google", "openai" }, found.Select(f => f.PresetId).OrderBy(id => id).ToArray());
        Assert.Equal("GEMINI_API_KEY", found.Single(f => f.PresetId == "google").VariableName);
    }

    // The value is read at the moment of use, never captured — so a variable the reader changes or unsets
    // takes effect on the next request rather than at the next launch. A copy in our keychain would outlive
    // the variable, and the row it came from offers no remove action to undo that with (#691).
    [Fact]
    public void The_value_is_read_afresh_each_time_rather_than_captured()
    {
        string? current = "before";
        var keys = new AiEnvironmentKeys(name => name == "OPENAI_API_KEY" ? current : null);
        var preset = Preset("openai", "OPENAI_API_KEY");

        Assert.Equal("before", keys.ValueFor(preset));
        current = "after";
        Assert.Equal("after", keys.ValueFor(preset));
        current = null;
        Assert.Null(keys.ValueFor(preset));
    }

    // A platform that refuses to read the environment is "no key", not a crash on the settings screen.
    [Fact]
    public void A_reader_that_throws_is_treated_as_no_key()
    {
        var keys = new AiEnvironmentKeys(_ => throw new InvalidOperationException("denied"));

        Assert.Null(keys.VariableFor(Preset("openai", "OPENAI_API_KEY")));
        Assert.Empty(keys.Discover(new[] { Preset("openai", "OPENAI_API_KEY") }));
    }
}
