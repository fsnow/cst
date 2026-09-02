using System;
using System.Collections.Generic;
using CST.Avalonia.Models.Ai;
using CST.Avalonia.Services.Ai;
using Xunit;

namespace CST.Avalonia.Tests.Models;

/// <summary>
/// Which catalogue preset a connection belongs to. (R5-6, #766)
///
/// <para>#766 stopped the connections row guessing this from the connection id, because a reader's own slug
/// can collide with a provider a later catalogue adds — the preset table then answers about a provider the
/// connection has nothing to do with. Four sibling lookups kept guessing: two doc links, the models group's
/// logo, and the model picker's usability verdict, which turns a wrong answer into a <b>disabled row</b> for
/// a keyless endpoint that works.</para>
/// </summary>
public class PresetIdDerivationTests
{
    private static AiConnection With(string id, string? presetId) =>
        new(id, id, ChatProviderKind.OpenAiCompatible, "https://example.invalid/v1",
            new List<AiModelEntry>(), Array.Empty<AiHeader>(), new Dictionary<string, string>(),
            PresetId: presetId);

    /// <summary>Added from the provider list: the recorded preset, so a connection the reader renamed keeps
    /// its identity.</summary>
    [Fact]
    public void A_recorded_preset_is_used()
    {
        Assert.Equal("groq", With("my-groq", "groq").PresetIdOrLegacyId);
    }

    /// <summary>
    /// The case with teeth. Recorded as custom — empty, not null — means we KNOW there is no provider
    /// behind it, so no lookup may be attempted even though the slug would match one.
    /// </summary>
    [Fact]
    public void A_custom_endpoint_resolves_to_no_preset_even_when_its_slug_would_match()
    {
        Assert.Null(With("groq", "").PresetIdOrLegacyId);
    }

    /// <summary>A settings file older than the field records nothing, and the id is the same answer it
    /// always was — right for every connection such a file can hold.</summary>
    [Fact]
    public void A_file_older_than_the_field_falls_back_to_the_id()
    {
        Assert.Equal("openai", With("openai", null).PresetIdOrLegacyId);
    }
}
