using System;
using System.Globalization;
using System.Net;
using System.Net.Http;

namespace CST.Avalonia.Services.Ai;

/// <summary>Shared HTTP plumbing for the chat providers.</summary>
internal static class AiHttp
{
    /// <summary>
    /// An <see cref="HttpClient"/> configured for streaming.
    ///
    /// <para><b>The infinite timeout is deliberate and load-bearing.</b> <see cref="HttpClient.Timeout"/> is a
    /// deadline for the whole request including the response body, so on a streamed response the default 100
    /// seconds silently truncates any generation that runs longer — which for a translation at high effort is
    /// routine. Liveness is enforced instead by <see cref="SseReader"/>'s idle timeout, which is the property we
    /// actually want: kill a stream that has stopped producing, not one that is merely long.</para>
    /// </summary>
    internal static HttpClient CreateClient() =>
        new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    /// <summary>
    /// Resolve the request URL from a user-supplied base URL.
    ///
    /// <para>Users paste whatever their provider's docs showed them, and the variants are all legitimate:
    /// <c>https://api.deepseek.com</c>, <c>https://api.deepseek.com/v1</c>,
    /// <c>http://localhost:11434/v1</c>, <c>https://openrouter.ai/api/v1</c>. Getting this wrong produces a 404
    /// that looks like a broken provider rather than a mistyped setting, so be forgiving here:
    /// a bare host gains the version segment, a base that already carries one does not, and a URL that already
    /// names the endpoint is left alone.</para>
    /// </summary>
    /// <param name="baseUrl">The configured base URL.</param>
    /// <param name="versionedPath">Path to use when the base URL is a bare host, e.g. <c>v1/chat/completions</c>.</param>
    /// <param name="path">Path to use when the base URL already carries a path, e.g. <c>chat/completions</c>.</param>
    internal static Uri ResolveEndpoint(string baseUrl, string versionedPath, string path)
    {
        var trimmed = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (trimmed.Length == 0 || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new AiException(new AiError(
                AiErrorKind.NotConfigured,
                "The endpoint URL is not a valid http(s) address."));
        }

        if (trimmed.EndsWith("/" + path, StringComparison.OrdinalIgnoreCase))
            return uri;

        var hasPath = uri.AbsolutePath.Trim('/').Length > 0;
        return new Uri(trimmed + "/" + (hasPath ? path : versionedPath));
    }

    /// <summary>The provider's requested backoff, when it sent one. Seconds and HTTP-date forms are both legal.</summary>
    internal static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header is null) return null;
        if (header.Delta is { } delta) return delta;
        if (header.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        // HttpClient only surfaces a parsed header; a malformed one lands in the raw collection.
        if (response.Headers.TryGetValues("retry-after", out var raw))
        {
            foreach (var value in raw)
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
                    seconds >= 0)
                {
                    return TimeSpan.FromSeconds(seconds);
                }
            }
        }

        return null;
    }

    /// <summary>Map a status code to its kind, before any provider-specific refinement.</summary>
    internal static AiErrorKind KindFor(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AiErrorKind.Unauthorized,
        HttpStatusCode.TooManyRequests => AiErrorKind.RateLimited,
        _ => AiErrorKind.Provider,
    };

    /// <summary>The user-facing sentence for a kind. Never contains provider text — see <see cref="AiError"/>.</summary>
    internal static string MessageFor(AiErrorKind kind, HttpStatusCode status) => kind switch
    {
        AiErrorKind.Unauthorized =>
            "The provider rejected the API key. Check the key and that it has access to this model.",
        AiErrorKind.RateLimited =>
            "The provider is rate-limiting this key. Wait a moment and try again.",
        AiErrorKind.ContextTooLong =>
            "The request was longer than the model's context window. Try a smaller passage or fewer glosses.",
        _ => $"The provider rejected the request (HTTP {(int)status}).",
    };
}
