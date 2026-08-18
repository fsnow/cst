using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using CST.Avalonia.Services.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// #689: not every OpenAI-compatible endpoint authenticates with a bearer token. Azure sends the credential
/// in <c>api-key</c> and rejects a request that also carries <c>Authorization</c> — so the auth header has to
/// be REPLACEABLE, not merely supplementable, which is why this cannot be expressed as an extra header.
///
/// <para>Exercises the header construction directly. Sending a real request would need a live endpoint, and
/// the thing worth pinning is which headers are attached — the part a wrong guess makes invisible until a
/// provider returns 401 naming nothing.</para>
/// </summary>
public class OpenAiCompatibleAuthTests
{
    private static HttpRequestMessage Build(OpenAiCompatibleOptions options)
    {
        var provider = new OpenAiCompatibleProvider(
            // The provider refuses a finite-timeout client: liveness is the SSE reader's idle window, and a
            // finite HttpClient.Timeout would truncate a long stream and report it as a cancellation.
            new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan },
            options,
            NullLogger<OpenAiCompatibleProvider>.Instance);

        var message = new HttpRequestMessage(HttpMethod.Post, "https://example.invalid/chat/completions");

        typeof(OpenAiCompatibleProvider)
            .GetMethod("ApplyAuth", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(provider, new object[] { message });

        return message;
    }

    private static string? Header(HttpRequestMessage m, string name) =>
        m.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;

    [Fact]
    public void The_ordinary_case_is_a_bearer_token_in_authorization()
    {
        var m = Build(new OpenAiCompatibleOptions("https://api.openai.com/v1", "sk-test"));

        Assert.Equal("Bearer sk-test", Header(m, "Authorization"));
    }

    /// <summary>
    /// The Azure case, and the reason this mechanism exists. The credential goes in <c>api-key</c> with no
    /// scheme prefix, and <c>Authorization</c> must NOT be present — Azure rejects a request carrying both.
    /// </summary>
    [Fact]
    public void A_named_auth_header_replaces_authorization_rather_than_joining_it()
    {
        var m = Build(new OpenAiCompatibleOptions(
            "https://acme.openai.azure.com/openai/v1", "azure-key",
            AuthHeaderName: "api-key", AuthScheme: null));

        Assert.Equal("azure-key", Header(m, "api-key"));
        Assert.Null(Header(m, "Authorization"));
    }

    /// <summary>A local runner needs no credential, and an absent key must produce no auth header at all
    /// rather than an empty one — which some servers reject outright.</summary>
    [Fact]
    public void No_key_means_no_auth_header()
    {
        var m = Build(new OpenAiCompatibleOptions("http://localhost:11434/v1"));

        Assert.Null(Header(m, "Authorization"));
        Assert.Empty(m.Headers);
    }

    [Fact]
    public void Extra_headers_are_attached_alongside_the_credential()
    {
        var m = Build(new OpenAiCompatibleOptions(
            "https://openrouter.ai/api/v1", "sk-or",
            ExtraHeaders: new Dictionary<string, string> { ["X-Title"] = "CST Reader" }));

        Assert.Equal("Bearer sk-or", Header(m, "Authorization"));
        Assert.Equal("CST Reader", Header(m, "X-Title"));
    }

    /// <summary>
    /// An extra header may never overwrite the credential. A stray "Authorization" typed into a settings field
    /// would otherwise silently replace the stored key with whatever the reader pasted — a failure that
    /// presents as a bad key while the key is fine.
    /// </summary>
    [Fact]
    public void An_extra_header_cannot_overwrite_the_credential()
    {
        var m = Build(new OpenAiCompatibleOptions(
            "https://api.openai.com/v1", "sk-real",
            ExtraHeaders: new Dictionary<string, string> { ["Authorization"] = "Bearer sk-typed-by-hand" }));

        Assert.Equal("Bearer sk-real", Header(m, "Authorization"));
    }

    /// <summary>Same guarantee under the endpoint's own auth header name, not just the standard one.</summary>
    [Fact]
    public void An_extra_header_cannot_overwrite_a_named_credential_either()
    {
        var m = Build(new OpenAiCompatibleOptions(
            "https://acme.openai.azure.com/openai/v1", "azure-key",
            AuthHeaderName: "api-key", AuthScheme: null,
            ExtraHeaders: new Dictionary<string, string> { ["api-key"] = "wrong" }));

        Assert.Equal("azure-key", Header(m, "api-key"));
    }

    /// <summary>Defensive: a blank header name falls back to the standard one rather than producing a request
    /// with the credential attached to nothing.</summary>
    [Fact]
    public void A_blank_auth_header_name_falls_back_to_authorization()
    {
        var m = Build(new OpenAiCompatibleOptions(
            "https://api.openai.com/v1", "sk-test", AuthHeaderName: "  "));

        Assert.Equal("Bearer sk-test", Header(m, "Authorization"));
    }
}
