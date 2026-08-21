using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CST.Avalonia.Models;
using Xunit;

namespace CST.Avalonia.Tests.Models;

/// <summary>
/// Reading a connection's headers from either shape. (#771, #784)
///
/// <para>These exist because the shape change shipped without them and cost a real settings file. The lesson
/// is not "add a converter": it is that <c>SettingsService</c> reverts the ENTIRE file to defaults on any
/// parse failure, so a type change in one property discards fonts, layout, the book directory and every AI
/// connection. A round-trip test on the new shape would have passed throughout.</para>
/// </summary>
public class AiHeaderRecordListConverterTests
{
    private static Settings Load(string json) =>
        JsonSerializer.Deserialize<Settings>(json)!;

    /// <summary>The shape written before #771. This is the exact failure that discarded a settings file.</summary>
    [Fact]
    public void The_old_object_shape_still_loads()
    {
        var settings = Load("""
        {"Ai":{"Chat":{"Connections":[
            {"Id":"gw","Headers":{"X-Title":"CST Reader","cf-aig-authorization":"Bearer token"}}
        ]}}}
        """);

        var headers = settings.Ai.Chat.Connections.Single().Headers;
        Assert.Equal(2, headers.Count);
        Assert.Equal("X-Title", headers[0].Name);
        Assert.Equal("CST Reader", headers[0].Value);
        Assert.Equal("Bearer token", headers[1].Value);
    }

    /// <summary>Nothing in the old shape could have been marked secret, because the mark did not exist.</summary>
    [Fact]
    public void Headers_from_the_old_shape_are_never_marked_secret()
    {
        var settings = Load("""
        {"Ai":{"Chat":{"Connections":[{"Id":"gw","Headers":{"cf-aig-authorization":"Bearer token"}}]}}}
        """);

        Assert.All(settings.Ai.Chat.Connections.Single().Headers, h => Assert.False(h.Secret));
    }

    [Fact]
    public void The_current_array_shape_round_trips_with_the_secret_mark()
    {
        var settings = Load("""
        {"Ai":{"Chat":{"Connections":[{"Id":"gw","Headers":[
            {"Name":"X-Title","Value":"CST Reader","Secret":false},
            {"Name":"cf-aig-authorization","Value":null,"Secret":true}
        ]}]}}}
        """);

        var headers = settings.Ai.Chat.Connections.Single().Headers;
        Assert.Equal("CST Reader", headers[0].Value);
        Assert.True(headers[1].Secret);
        Assert.Null(headers[1].Value);

        var again = Load(JsonSerializer.Serialize(settings));
        var round = again.Ai.Chat.Connections.Single().Headers;
        Assert.Equal("cf-aig-authorization", round[1].Name);
        Assert.True(round[1].Secret);
    }

    [Fact]
    public void An_absent_or_null_headers_property_is_an_empty_list_not_a_failure()
    {
        Assert.Empty(Load("""{"Ai":{"Chat":{"Connections":[{"Id":"gw"}]}}}""")
            .Ai.Chat.Connections.Single().Headers);
        Assert.Empty(Load("""{"Ai":{"Chat":{"Connections":[{"Id":"gw","Headers":null}]}}}""")
            .Ai.Chat.Connections.Single().Headers);
    }

    /// <summary>
    /// The point of the whole class: an unreadable headers value costs the headers, not the settings file.
    /// Throwing here reverts fonts, layout, the book directory and every connection to defaults.
    /// </summary>
    [Fact]
    public void An_unreadable_headers_value_does_not_take_the_whole_file_with_it()
    {
        var settings = Load("""
        {"XmlBooksDirectory":"/books","Ai":{"Chat":{"Connections":[
            {"Id":"gw","Headers":"nonsense"}
        ]}}}
        """);

        Assert.Empty(settings.Ai.Chat.Connections.Single().Headers);
        Assert.Equal("/books", settings.XmlBooksDirectory);   // the rest of the file survived
        Assert.Equal("gw", settings.Ai.Chat.Connections.Single().Id);
    }

    /// <summary>
    /// A whole settings file in the pre-#771 shape loads with everything else intact — the regression as the
    /// reader met it, rather than a fragment.
    /// </summary>
    [Fact]
    public void A_whole_settings_file_in_the_old_shape_keeps_its_models_and_its_other_settings()
    {
        var settings = Load("""
        {"XmlBooksDirectory":"/books",
         "Ai":{"Chat":{"ActiveConnectionId":"groq","ActiveModelId":"openai/gpt-oss-120b","Connections":[
            {"Id":"groq","DisplayName":"Groq","BaseUrl":"https://api.groq.com/openai/v1",
             "Headers":{"X-Title":"CST Reader"},
             "Models":[{"Id":"openai/gpt-oss-120b","DisplayName":"GPT-OSS 120B","Enabled":true},
                       {"Id":"llama-3.3-70b","DisplayName":"Llama 3.3 70B","Enabled":true}]}
        ]}}}
        """);

        var connection = settings.Ai.Chat.Connections.Single();
        Assert.Equal(2, connection.Models.Count);
        Assert.Equal("groq", settings.Ai.Chat.ActiveConnectionId);
        Assert.Equal("/books", settings.XmlBooksDirectory);
        Assert.Equal("CST Reader", connection.Headers.Single().Value);
    }

    /// <summary>
    /// The regression as the reader met it: a whole pre-#771 settings file, with two connections, headers in
    /// the object shape and hand-built model lists. Before the converter this threw, SettingsService caught
    /// it, and the ENTIRE file — book directory, both connections, every enabled model — was replaced with
    /// defaults. (#784)
    /// </summary>
    [Fact]
    public void The_file_that_was_discarded_now_loads_with_its_models_intact()
    {
        // Inlined rather than read from disk: a fixture behind a machine path is a test that passes here and
        // fails everywhere else, which is its own kind of false coverage.
        var settings = Load("""
        {
          "XmlBooksDirectory": "/books/xml",
          "Ai": { "Chat": {
            "ActiveConnectionId": "groq",
            "ActiveModelId": "openai/gpt-oss-120b",
            "Connections": [
              { "Id": "openrouter", "DisplayName": "OpenRouter", "Kind": "openai-compatible",
                "BaseUrl": "https://openrouter.ai/api/v1",
                "Headers": { "HTTP-Referer": "https://cst.example", "X-Title": "CST Reader" },
                "Inputs": {},
                "Models": [ { "Id": "nvidia/nemotron", "DisplayName": "Nemotron", "Enabled": true } ] },
              { "Id": "groq", "DisplayName": "Groq", "Kind": "openai-compatible",
                "BaseUrl": "https://api.groq.com/openai/v1",
                "Headers": {},
                "Inputs": {},
                "Models": [ { "Id": "openai/gpt-oss-120b", "DisplayName": "GPT-OSS 120B", "Enabled": true },
                            { "Id": "llama-3.3-70b-versatile", "DisplayName": "Llama 3.3 70B", "Enabled": true } ] }
            ] } }
        }
        """);

        Assert.Equal(2, settings.Ai.Chat.Connections.Count);

        var groq = settings.Ai.Chat.Connections.Single(c => c.Id == "groq");
        Assert.Equal(2, groq.Models.Count);
        Assert.Contains(groq.Models, m => m.Id == "openai/gpt-oss-120b" && m.Enabled);

        var openrouter = settings.Ai.Chat.Connections.Single(c => c.Id == "openrouter");
        Assert.Equal(2, openrouter.Headers.Count);
        Assert.Equal("CST Reader", openrouter.Headers.Single(h => h.Name == "X-Title").Value);
        Assert.All(openrouter.Headers, h => Assert.False(h.Secret));

        Assert.Equal("groq", settings.Ai.Chat.ActiveConnectionId);
        Assert.Equal("/books/xml", settings.XmlBooksDirectory);
    }
}
