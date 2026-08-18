using System;
using CST.Avalonia.Services.Ai;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// #673: one first-event timeout served two situations with nothing in common. Ten minutes was earned by a
/// local runner doing prompt evaluation on modest hardware; applied to a hosted endpoint it means a reader
/// clicks a preset and the app is prepared to wait ten minutes before saying anything.
/// </summary>
public class AiEndpointTests
{
    [Theory]
    [InlineData("http://localhost:11434/v1")]
    [InlineData("http://127.0.0.1:1234/v1")]
    [InlineData("http://[::1]:8080/v1")]
    [InlineData("http://mac-studio.local:11434/v1")]
    [InlineData("http://192.168.1.50:8000/v1")]
    [InlineData("http://10.0.0.5:8000/v1")]
    [InlineData("http://172.16.4.2:8000/v1")]
    public void A_machine_on_this_desk_or_network_counts_as_local(string url)
    {
        Assert.True(AiEndpoint.IsLocal(url), url);
    }

    [Theory]
    [InlineData("https://openrouter.ai/api/v1")]
    [InlineData("https://api.openai.com/v1")]
    [InlineData("https://api.anthropic.com/v1")]
    [InlineData("https://acme.openai.azure.com/openai/v1")]
    public void A_hosted_endpoint_does_not(string url)
    {
        Assert.False(AiEndpoint.IsLocal(url), url);
    }

    /// <summary>172.x is only private in 16–31; 172.15 and 172.32 are ordinary public space, and treating them
    /// as local would hand a hosted endpoint the ten-minute allowance.</summary>
    [Theory]
    [InlineData("http://172.15.0.1:8000/v1")]
    [InlineData("http://172.32.0.1:8000/v1")]
    [InlineData("http://172.200.0.1:8000/v1")]
    public void The_172_range_is_only_private_between_16_and_31(string url)
    {
        Assert.False(AiEndpoint.IsLocal(url), url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("{resourceName}.example.com")]   // an unresolved template is not a usable address
    public void Anything_unparseable_is_treated_as_hosted(string? url)
    {
        // Hosted is the safe default: it gives the SHORTER wait, so a misjudgement costs a premature timeout
        // rather than ten minutes of silence.
        Assert.False(AiEndpoint.IsLocal(url));
    }

    [Fact]
    public void Local_keeps_the_long_allowance_it_earned()
    {
        Assert.Equal(SseReader.DefaultFirstEventTimeout,
            AiEndpoint.FirstEventTimeoutFor("http://localhost:11434/v1"));
    }

    [Fact]
    public void Hosted_gets_an_interactive_ceiling_instead()
    {
        var hosted = AiEndpoint.FirstEventTimeoutFor("https://openrouter.ai/api/v1");

        Assert.Equal(AiEndpoint.HostedFirstEventTimeout, hosted);
        Assert.True(hosted < SseReader.DefaultFirstEventTimeout);
        Assert.True(hosted >= TimeSpan.FromMinutes(1), "too short to cover a slow hosted first token");
    }
}
