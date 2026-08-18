using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.Models;
using CST.Avalonia.Models.Ai;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Ai;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// #689: the service the connections UI (#691/#692/#693) binds to. Settings-backed; no credential handling
/// yet, which is what lets the UI work start in parallel with the keychain re-keying.
/// </summary>
public class AiConnectionServiceTests
{
    private static (AiConnectionService Service, Settings Settings) Make()
    {
        var settings = new Settings();
        var svc = new Mock<ISettingsService>();
        svc.SetupGet(s => s.Settings).Returns(settings);
        return (new AiConnectionService(svc.Object), settings);
    }

    private static AiConnectionDraft Draft(string name = "My box", string url = "http://localhost:8000/v1") =>
        new(name, ChatProviderKind.OpenAiCompatible, url,
            new List<AiModelEntry>(), new Dictionary<string, string>(), new Dictionary<string, string>());

    // ---- ids ------------------------------------------------------------------------------------------

    /// <summary>The id becomes the credential's account name, so a duplicate would mean one connection
    /// quietly inheriting another's key. Refused explicitly rather than overwriting.</summary>
    [Fact]
    public void A_duplicate_id_is_refused()
    {
        var (service, _) = Make();
        Assert.True(service.Add("my-box", Draft()).Ok);

        var second = service.Add("my-box", Draft("Another"));

        Assert.False(second.Ok);
        Assert.Contains("already", second.Problem);
    }

    /// <summary>Preset ids are reserved: taking one by hand would collide with the built-in the reader might
    /// add later, and the collision would be a credential mix-up rather than a visible error.</summary>
    [Fact]
    public void A_preset_id_cannot_be_taken_by_a_custom_connection()
    {
        var (service, _) = Make();

        var result = service.Add("openrouter", Draft());

        Assert.False(result.Ok);
        Assert.Contains("built-in", result.Problem);
    }

    [Theory]
    [InlineData("My-Box")]      // uppercase
    [InlineData("my box")]      // space
    [InlineData("my.box")]      // dot
    [InlineData("-leading")]    // leading hyphen
    [InlineData("")]
    public void An_id_that_is_not_a_slug_is_refused(string id)
    {
        var (service, _) = Make();
        Assert.False(service.Add(id, Draft()).Ok);
    }

    // ---- presets --------------------------------------------------------------------------------------

    /// <summary>An added preset drops out of the available list, so the "add a provider" section always reads
    /// as what you could add next rather than a list where some rows are already spoken for.</summary>
    [Fact]
    public void An_added_preset_leaves_the_available_list()
    {
        var (service, _) = Make();
        Assert.Contains(service.AvailablePresets, p => p.Id == "openrouter");

        service.AddFromPreset("openrouter", new Dictionary<string, string>());

        Assert.DoesNotContain(service.AvailablePresets, p => p.Id == "openrouter");
        Assert.Contains(service.Connections, c => c.Id == "openrouter");
    }

    [Fact]
    public void A_preset_arrives_with_its_url_and_kind_filled_in()
    {
        var (service, _) = Make();

        var result = service.AddFromPreset("openrouter", new Dictionary<string, string>());

        Assert.True(result.Ok);
        Assert.Equal("https://openrouter.ai/api/v1", result.Connection!.BaseUrl);
        Assert.Equal(ChatProviderKind.OpenAiCompatible, result.Connection.Kind);
    }

    /// <summary>
    /// A preset whose URL is a template must not be addable without its inputs — the connection would keep its
    /// {placeholders} and could never send anything, failing later as a DNS error naming nothing.
    /// </summary>
    [Fact]
    public void A_templated_preset_is_refused_without_its_inputs()
    {
        var (service, _) = Make();

        var result = service.AddFromPreset("azure", new Dictionary<string, string>());

        Assert.False(result.Ok);
        Assert.Contains("resource name", result.Problem, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_templated_preset_resolves_its_url_once_the_inputs_are_given()
    {
        var (service, _) = Make();

        var result = service.AddFromPreset("azure", new Dictionary<string, string> { ["resourceName"] = "acme" });

        Assert.True(result.Ok);
        Assert.False(result.Connection!.IsIncomplete);
        Assert.Equal("https://acme.openai.azure.com/openai/v1", result.Connection.ResolvedBaseUrl);
    }

    // ---- active selection ------------------------------------------------------------------------------

    [Fact]
    public void The_first_connection_added_becomes_active()
    {
        var (service, _) = Make();
        service.Add("first", Draft());
        service.Add("second", Draft());

        Assert.Equal("first", service.Active!.Id);
    }

    /// <summary>Selecting a model that the connection does not offer must fail rather than store a value that
    /// resolves to nothing at send time.</summary>
    [Fact]
    public void An_unknown_model_cannot_be_made_active()
    {
        var (service, _) = Make();
        service.Add("box", Draft());

        Assert.False(service.SetActive("box", "no-such-model").Ok);
    }

    [Fact]
    public void A_model_the_connection_offers_can_be_made_active()
    {
        var (service, _) = Make();
        service.Add("box", Draft() with { Models = new[] { new AiModelEntry("llama-3", "Llama 3") } });

        Assert.True(service.SetActive("box", "llama-3").Ok);
        Assert.Equal("llama-3", service.ActiveModelId);
    }

    /// <summary>
    /// Removing the active connection must not leave the pointer dangling. A stale id reads as "configured"
    /// to anything that only checks for null, which would present as a request going nowhere.
    /// </summary>
    [Fact]
    public void Removing_the_active_connection_moves_the_pointer()
    {
        var (service, settings) = Make();
        service.Add("first", Draft());
        service.Add("second", Draft());

        Assert.True(service.Remove("first").Ok);

        Assert.Equal("second", settings.Ai.Chat.ActiveConnectionId);
        Assert.Null(settings.Ai.Chat.ActiveModelId);
    }

    [Fact]
    public void Removing_the_last_connection_leaves_nothing_active()
    {
        var (service, settings) = Make();
        service.Add("only", Draft());

        service.Remove("only");

        Assert.Null(settings.Ai.Chat.ActiveConnectionId);
        Assert.Null(service.Active);
    }

    // ---- models ---------------------------------------------------------------------------------------

    /// <summary>Models arrive enabled. All-on is neutral; a pre-selected subset would be a quality verdict,
    /// which is the registry removed in #670/#681 wearing a toggle.</summary>
    [Fact]
    public void Models_are_enabled_by_default()
    {
        var (service, _) = Make();
        var result = service.Add("box", Draft() with
        {
            Models = new[] { new AiModelEntry("a", "A"), new AiModelEntry("b", "B") }
        });

        Assert.All(result.Connection!.Models, m => Assert.True(m.Enabled));
    }

    [Fact]
    public void A_model_can_be_switched_off_without_removing_it()
    {
        var (service, _) = Make();
        service.Add("box", Draft() with { Models = new[] { new AiModelEntry("a", "A") } });

        Assert.True(service.SetModelEnabled("box", "a", false).Ok);

        var model = Assert.Single(service.Connections.Single().Models);
        Assert.False(model.Enabled);
        Assert.Equal("a", model.Id);
    }

    // ---- change notification ---------------------------------------------------------------------------

    /// <summary>The UI rebinds on this; a mutation that does not raise it presents as a screen that has
    /// silently stopped matching what is stored.</summary>
    [Fact]
    public void Every_mutation_raises_the_change_event()
    {
        var (service, _) = Make();
        int raised = 0;
        service.ConnectionsChanged += (_, _) => raised++;

        service.Add("box", Draft() with { Models = new[] { new AiModelEntry("a", "A") } });
        service.Update("box", Draft("Renamed"));
        service.Add("other", Draft() with { Models = new[] { new AiModelEntry("a", "A") } });
        service.SetModelEnabled("other", "a", false);
        service.SetActive("other", "a");
        service.Remove("other");

        Assert.Equal(6, raised);
    }

    /// <summary>The id is immutable because the credential is filed under it; an update must not silently
    /// change it even if a draft carried one.</summary>
    [Fact]
    public void Updating_edits_everything_except_the_id()
    {
        var (service, _) = Make();
        service.Add("box", Draft("Original", "http://a/v1"));

        var result = service.Update("box", Draft("Renamed", "http://b/v1"));

        Assert.True(result.Ok);
        Assert.Equal("box", result.Connection!.Id);
        Assert.Equal("Renamed", result.Connection.DisplayName);
        Assert.Equal("http://b/v1", result.Connection.BaseUrl);
    }
}
