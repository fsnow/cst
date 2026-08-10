using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CST.Avalonia.Services.Ai;

/// <summary>Shared HTTP plumbing for the chat providers.</summary>
internal static class AiHttp
{
    /// <summary>Plenty for classification; a provider that sends more is not telling us anything we use.</summary>
    private const int MaxErrorBodyBytes = 8 * 1024;

    /// <summary>Provider codes are short tokens. Anything longer is not a code, whatever the provider calls it.</summary>
    private const int MaxProviderCodeLength = 64;

    /// <summary>
    /// An <see cref="HttpClient"/> configured for streaming. <b>Providers must be given a client built here</b>
    /// (see <see cref="EnsureStreamable"/>).
    ///
    /// <para><b>The infinite timeout is deliberate and load-bearing.</b> <see cref="HttpClient.Timeout"/> is a
    /// deadline for the whole request including the response body, so on a streamed response the default 100
    /// seconds silently truncates any generation that runs longer — which for a translation at high effort is
    /// routine. Worse, it surfaces as a <see cref="TaskCanceledException"/> with the caller's token NOT
    /// cancelled, so it is indistinguishable from the user pressing stop. Liveness is enforced instead by
    /// <see cref="SseReader"/>'s idle timeout, which is the property we actually want: kill a stream that has
    /// stopped producing, not one that is merely long.</para>
    /// </summary>
    internal static HttpClient CreateClient() =>
        new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    /// <summary>
    /// Guard the invariant above at construction, because a finite timeout does not fail loudly — it truncates a
    /// long answer and reports it as a cancellation, which is close to undiagnosable from a bug report.
    /// </summary>
    internal static HttpClient EnsureStreamable(HttpClient http)
    {
        if (http.Timeout != System.Threading.Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentException(
                "A chat provider needs an HttpClient with an infinite timeout (see AiHttp.CreateClient): a finite " +
                "HttpClient.Timeout truncates long streamed responses and reports it as a cancellation. " +
                "Liveness is enforced by the SSE reader's idle timeout instead.",
                nameof(http));
        }

        return http;
    }

    /// <summary>
    /// Resolve the request URL from a user-supplied base URL.
    ///
    /// <para>Users paste whatever their provider's docs showed them, and the variants are all legitimate:
    /// <c>https://api.deepseek.com</c>, <c>https://api.deepseek.com/v1</c>,
    /// <c>http://localhost:11434/v1</c>, <c>https://openrouter.ai/api/v1</c>. Getting this wrong produces a 404
    /// that looks like a broken provider rather than a mistyped setting, so be forgiving: a bare host gains the
    /// version segment, a base that already carries one does not, and a URL that already names the endpoint is
    /// left alone. Any query string is preserved — Azure-style bases carry <c>?api-version=</c>, and appending
    /// the path as raw text would bury it inside the query.</para>
    /// </summary>
    /// <param name="baseUrl">The configured base URL.</param>
    /// <param name="versionedPath">Path to use when the base URL is a bare host, e.g. <c>v1/chat/completions</c>.</param>
    /// <param name="path">Path to use when the base URL already carries a path, e.g. <c>chat/completions</c>.</param>
    internal static Uri ResolveEndpoint(string baseUrl, string versionedPath, string path)
    {
        var trimmed = (baseUrl ?? string.Empty).Trim();
        if (trimmed.Length == 0 || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new AiException(new AiError(
                AiErrorKind.NotConfigured,
                "The endpoint URL is not a valid http(s) address."));
        }

        var builder = new UriBuilder(uri);
        var existing = builder.Path.Trim('/');

        if (existing.EndsWith(path, StringComparison.OrdinalIgnoreCase) &&
            (existing.Length == path.Length || existing[^(path.Length + 1)] == '/'))
        {
            return builder.Uri;   // already names the endpoint
        }

        // Does the base already carry a version segment? The OpenAI convention is that a "base URL" ends in
        // one (OPENAI_BASE_URL=https://api.openai.com/v1), so a path WITHOUT one is a mount point that still
        // needs it — `https://openrouter.ai/api` is the docs' URL with the `/v1` dropped, and appending only
        // `chat/completions` there yields a 404 that reads as a broken provider rather than a mistyped setting.
        var lastSegment = existing.Length == 0
            ? string.Empty
            : existing[(existing.LastIndexOf('/') + 1)..];
        var versioned = lastSegment.Length > 1 && (lastSegment[0] is 'v' or 'V') &&
                        lastSegment[1..].All(char.IsAsciiDigit);

        var suffix = versioned ? path : versionedPath;
        builder.Path = existing.Length == 0 ? suffix : existing + "/" + suffix;
        return builder.Uri;
    }

    /// <summary>The provider's requested backoff, when it sent one. Seconds and HTTP-date forms are both legal.</summary>
    internal static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header is not null)
        {
            if (header.Delta is { } delta) return delta;
            if (header.Date is { } date)
            {
                var wait = date - DateTimeOffset.UtcNow;
                return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
            }
        }

        // A header HttpClient could not parse stays in the raw collection.
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

    /// <summary>
    /// Clamp a provider-supplied error code to something genuinely safe to log.
    ///
    /// <para>The rest of the design keeps provider prose out of logs and out of <see cref="AiError.Message"/>,
    /// but <c>error.type</c> / <c>error.code</c> are provider-controlled strings and "OpenAI-compatible" means an
    /// arbitrary user-pasted endpoint — including, on a mistyped setting, an entirely different server. Nothing
    /// stops such a server putting a paragraph of echoed request material where a short token belongs, and that
    /// paragraph would land in the log. A length cap plus a token charset makes the "bounded vocabulary" claim
    /// true instead of merely intended.</para>
    /// </summary>
    internal static string? SanitizeProviderCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var code = raw.Trim();
        if (code.Length > MaxProviderCodeLength) return null;
        return code.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or ':') ? code : null;
    }

    /// <summary>
    /// Read at most <see cref="MaxErrorBodyBytes"/> of an error body, for classification only.
    ///
    /// <para>Bounded because the client runs without a timeout: a server that sends error headers and then stalls
    /// the body would otherwise hang until the user cancels, and a very large body would be buffered whole. The
    /// content is never logged or retained — see <see cref="AiError"/>.</para>
    /// </summary>
    internal static async Task<string> ReadBoundedBodyAsync(
        HttpContent content, TimeSpan timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);

        try
        {
            await using var stream = await content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            var buffer = new byte[MaxErrorBodyBytes];
            var filled = 0;

            while (filled < buffer.Length)
            {
                var read = await stream
                    .ReadAsync(buffer.AsMemory(filled, buffer.Length - filled), deadline.Token)
                    .ConfigureAwait(false);
                if (read == 0) break;
                filled += read;
            }

            return Encoding.UTF8.GetString(buffer, 0, filled);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return string.Empty;   // no body is simply no extra information; the status already classified it
        }
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

    /// <summary>The error for a 200 that carried nothing a provider stream could possibly be made of.</summary>
    internal static AiError EmptyResponse() => new(
        AiErrorKind.Provider,
        "The provider accepted the request but returned no response. Check the endpoint URL and the model name.");
}
