using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CST.Avalonia.Services.Ai;

/// <summary>
/// Which convention a provider's documented "base URL" follows — they differ, and guessing wrong turns a
/// correctly-pasted setting into a 404.
/// </summary>
internal enum BaseUrlConvention
{
    /// <summary>
    /// The base URL already carries the version segment: <c>OPENAI_BASE_URL=https://api.openai.com/v1</c>. The
    /// documented value is the string you append <c>/chat/completions</c> to, so a pathed base is taken at its
    /// word — Gemini's <c>/v1beta/openai</c>, Azure's <c>/openai/deployments/{d}</c> and Cloudflare's gateway
    /// paths are all correct as given and must not be second-guessed.
    /// </summary>
    IncludesVersion,

    /// <summary>
    /// The base URL excludes the version segment: Anthropic's own SDK takes <c>https://api.anthropic.com</c> and
    /// appends <c>/v1/messages</c> itself, so a gateway mounted at <c>/anthropic</c> serves
    /// <c>/anthropic/v1/messages</c>.
    /// </summary>
    ExcludesVersion,
}

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
    /// Attaches a connection's credential in whatever shape its endpoint expects, plus its extra headers.
    /// (#689, #674)
    ///
    /// <para><b>Naming a non-standard auth header replaces <c>Authorization</c>; it does not add a second
    /// one.</b> Azure rejects a request carrying both, which is why this cannot be expressed as an ordinary
    /// extra header — an extra header is additive by definition, and the requirement there is an
    /// absence.</para>
    ///
    /// <para>Extra headers are applied first so they can never overwrite the credential: a header named
    /// <c>Authorization</c> mistyped into settings would otherwise silently replace the real key with whatever
    /// the reader pasted.</para>
    ///
    /// <para><b>Shared deliberately.</b> Chat requests and model-listing requests go to the same endpoint with
    /// the same credential, so they must authenticate identically — two implementations would eventually
    /// disagree, and the symptom would be a provider whose model list loads while its answers 401, which is
    /// exactly the sort of contradiction between two surfaces that #673 exists to prevent.</para>
    /// </summary>
    internal static void ApplyAuth(
        HttpRequestMessage message,
        string? apiKey,
        string? authHeaderName,
        string? authScheme,
        IReadOnlyDictionary<string, string>? extraHeaders)
    {
        var header = string.IsNullOrWhiteSpace(authHeaderName) ? "Authorization" : authHeaderName;

        if (extraHeaders is { Count: > 0 })
        {
            foreach (var extra in extraHeaders)
            {
                if (string.IsNullOrWhiteSpace(extra.Key)) continue;
                if (extra.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)) continue;
                if (extra.Key.Equals(header, StringComparison.OrdinalIgnoreCase)) continue;
                message.Headers.TryAddWithoutValidation(extra.Key, extra.Value);
            }
        }

        if (string.IsNullOrWhiteSpace(apiKey)) return;   // a local runner needs none

        message.Headers.TryAddWithoutValidation(
            header,
            string.IsNullOrWhiteSpace(authScheme) ? apiKey! : $"{authScheme} {apiKey}");
    }

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
    /// <c>https://api.deepseek.com</c>, <c>https://api.deepseek.com/v1</c>, <c>http://localhost:11434/v1</c>,
    /// <c>https://openrouter.ai/api/v1</c>. Getting this wrong produces a 404 that looks like a broken provider
    /// rather than a mistyped setting, so: a bare host gains the version segment, a base that already carries one
    /// does not, and a URL that already names the endpoint is left alone. Any query string is preserved — Azure
    /// bases carry <c>?api-version=</c>, and appending the path as text would bury it inside the query.</para>
    ///
    /// <para><b>The one guess, and its limit.</b> A single-segment path with no version — <c>openrouter.ai/api</c>,
    /// <c>api.groq.com/openai</c> — is the docs' URL with the <c>/v1</c> dropped, so under
    /// <see cref="BaseUrlConvention.IncludesVersion"/> the version is added back. That rescue stops at ONE
    /// segment on purpose: a longer path is somebody's documented base (Gemini's <c>/v1beta/openai</c>, Azure's
    /// <c>/openai/deployments/{id}</c>, a Cloudflare gateway path), and rescuing a typo is not worth 404-ing a
    /// setting that was pasted correctly.</para>
    /// </summary>
    /// <param name="baseUrl">The configured base URL.</param>
    /// <param name="versionedPath">Path used when the version segment must be added, e.g. <c>v1/chat/completions</c>.</param>
    /// <param name="path">Path used when the base already carries the version, e.g. <c>chat/completions</c>.</param>
    /// <param name="convention">Which convention the provider's own documentation follows.</param>
    internal static Uri ResolveEndpoint(
        string baseUrl, string versionedPath, string path, BaseUrlConvention convention)
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

        var segments = existing.Length == 0 ? Array.Empty<string>() : existing.Split('/');
        var addVersion = segments.Length switch
        {
            0 => true,                                   // bare host always needs the version segment
            _ when IsVersionSegment(segments[^1]) => false,   // already versioned
            _ => convention == BaseUrlConvention.ExcludesVersion || segments.Length == 1,
        };

        var suffix = addVersion ? versionedPath : path;
        builder.Path = existing.Length == 0 ? suffix : existing + "/" + suffix;
        return builder.Uri;
    }

    /// <summary>
    /// A path segment that names an API version — <c>v1</c>, <c>v2</c>, and also <c>v1beta</c> / <c>v1.0</c>,
    /// which Gemini and others use. A leading <c>v</c> followed by a digit is the whole test; anything more
    /// elaborate would start rejecting real version segments.
    /// </summary>
    private static bool IsVersionSegment(string segment) =>
        segment.Length > 1 && segment[0] is 'v' or 'V' && char.IsAsciiDigit(segment[1]);

    /// <summary>The provider's requested backoff, when it sent one. Seconds and HTTP-date forms are both legal.</summary>
    /// <summary>
    /// How long until the limit lifts, from whichever header the provider sent. (#673)
    ///
    /// <para><c>Retry-After</c> first, because it is the standard and states a duration outright. OpenRouter
    /// sends it only when <i>every</i> upstream provider returned a retry hint, so on a plain account-level
    /// cap there is no <c>Retry-After</c> at all and <c>X-RateLimit-Reset</c> is the only thing available —
    /// which is exactly the case that produced "wait a moment" for a limit that lasts until tomorrow.</para>
    /// </summary>
    internal static TimeSpan? RateLimitWait(HttpResponseMessage response) =>
        RetryAfter(response) ?? RateLimitReset(response);

    /// <summary>
    /// <c>X-RateLimit-Reset</c> as a wait, or null.
    ///
    /// <para><b>Seconds and milliseconds are both accepted</b>, told apart by magnitude rather than by
    /// trusting a convention: providers disagree about the unit, and reading a millisecond timestamp as
    /// seconds puts the reset tens of thousands of years out, which would be reported to the reader with a
    /// straight face. Anything that lands more than a day and a bit away is treated as not understood.</para>
    /// </summary>
    internal static TimeSpan? RateLimitReset(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("x-ratelimit-reset", out var values)) return null;

        foreach (var value in values)
        {
            if (!long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw))
                continue;

            var epochMs = raw > 100_000_000_000 ? raw : raw * 1000;

            // FromUnixTimeMilliseconds throws outside its range, and a header is somebody else's input: a
            // malformed value must produce no advice, never an exception on the error path.
            if (epochMs < 0 || epochMs > 253_402_300_799_000) continue;

            var moment = DateTimeOffset.FromUnixTimeMilliseconds(epochMs);
            var wait = moment - DateTimeOffset.UtcNow;

            // Just past is "now" - clocks disagree by seconds and a reset a moment ago is not a reason to
            // say nothing.
            if (wait <= TimeSpan.Zero) return wait > TimeSpan.FromMinutes(-5) ? TimeSpan.Zero : null;
            if (wait < TimeSpan.FromHours(26)) return wait;
        }

        return null;
    }

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
    /// <param name="wait">How long until the limit lifts, where the provider said. Turns "wait a moment" —
    /// which is wrong by many hours for a daily cap — into something the reader can act on.</param>
    internal static string MessageFor(AiErrorKind kind, HttpStatusCode status, TimeSpan? wait = null) => kind switch
    {
        AiErrorKind.Unauthorized =>
            "The provider rejected the API key. Check the key and that it has access to this model.",
        AiErrorKind.RateLimited => RateLimitMessage(wait),
        AiErrorKind.ContextTooLong =>
            "The request was longer than the model's context window. Try a smaller passage or fewer glosses.",
        _ => $"The provider rejected the request (HTTP {(int)status}).",
    };

    /// <summary>
    /// What to tell a reader who has been rate-limited.
    ///
    /// <para>The old wording was "wait a moment and try again" for every 429. That is right for a
    /// per-minute cap and badly wrong for a daily one — a free OpenRouter key allows 50 requests a day, and a
    /// reader told to wait a moment will retry, fail, and reasonably conclude the app is broken.</para>
    ///
    /// <para>Where the provider said nothing the wording stays vague, because vague is what we know. It no
    /// longer promises a moment.</para>
    /// </summary>
    internal static string RateLimitMessage(TimeSpan? wait)
    {
        const string prefix = "The provider is rate-limiting this key.";

        if (wait is not { } left) return $"{prefix} It may be a per-minute limit or a daily quota — the provider did not say which.";
        if (left <= TimeSpan.FromSeconds(90)) return $"{prefix} Try again in a minute.";
        if (left < TimeSpan.FromHours(1)) return $"{prefix} Try again in about {Math.Ceiling(left.TotalMinutes):N0} minutes.";

        // Beyond an hour, a duration alone is hard to act on - "about 7 hours" invites arithmetic, and the
        // reader wants to know whether it is worth waiting or worth coming back tomorrow.
        var hours = Math.Round(left.TotalHours, MidpointRounding.AwayFromZero);
        var at = DateTime.Now.Add(left).ToString("t", CultureInfo.CurrentCulture);
        return $"{prefix} The quota resets at {at}, about {hours:N0} hours from now.";
    }

    /// <summary>The error for a 200 that carried nothing a provider stream could possibly be made of.</summary>
    internal static AiError EmptyResponse() => new(
        AiErrorKind.Provider,
        "The provider accepted the request but returned no response. Check the endpoint URL and the model name.");

    /// <summary>
    /// The error for a turn the model ended at its output limit (#601). The wording here is the generic one:
    /// the orchestrator replaces it once it knows whether any answer text was written, because "cut off
    /// mid-answer" and "spent the whole budget reasoning" want different advice.
    /// </summary>
    internal static AiError Truncated(string providerCode) => new(
        AiErrorKind.Truncated,
        "The model reached its output limit and stopped before finishing.",
        ProviderCode: providerCode);
}
