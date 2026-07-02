using System.Net.Http;
using System.Reflection;
using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// NET-8: a WelcomeUpdateService is created per welcome document (and each layout reset). It must not
/// new up a fresh HttpClient each time (they were never disposed) — instances share one long-lived
/// client. An explicitly injected client is still honored (for tests).
/// </summary>
public class WelcomeUpdateHttpClientTests
{
    private static HttpClient? ClientOf(WelcomeUpdateService svc) =>
        (HttpClient?)typeof(WelcomeUpdateService)
            .GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(svc);

    [Fact]
    public void Instances_ShareOneHttpClient_WhenNoneInjected()
    {
        var a = new WelcomeUpdateService();
        var b = new WelcomeUpdateService();

        Assert.NotNull(ClientOf(a));
        Assert.Same(ClientOf(a), ClientOf(b)); // shared, not a leaked new client per instance
    }

    [Fact]
    public void InjectedHttpClient_IsUsed()
    {
        using var injected = new HttpClient();

        var svc = new WelcomeUpdateService(injected);

        Assert.Same(injected, ClientOf(svc));
    }
}
