using System;
using System.Net;
using System.Net.Http;
using CST.Avalonia.Services.Ai;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// #673: telling a rate-limited reader how long the limit lasts.
///
/// <para>Reported from use. A free OpenRouter key allows 50 requests a day; on hitting that the assistant
/// said "Wait a moment and try again", so the reader retried, failed again, and had no way to tell a
/// per-minute cap from one that lasts until tomorrow.</para>
/// </summary>
public class AiRateLimitTests
{
    private static HttpResponseMessage Response(params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        foreach (var (name, value) in headers)
            response.Headers.TryAddWithoutValidation(name, value);
        return response;
    }

    private static string Unix(DateTimeOffset moment, bool milliseconds) =>
        (milliseconds ? moment.ToUnixTimeMilliseconds() : moment.ToUnixTimeSeconds()).ToString();

    // ---- reading the headers ---------------------------------------------------------------------------

    /// <summary>
    /// <c>X-RateLimit-Reset</c> is read when there is no <c>Retry-After</c>.
    ///
    /// <para>The case that produced the bad message: OpenRouter sends <c>Retry-After</c> only when every
    /// upstream provider returned a retry hint, so an account-level daily cap arrives with the reset header
    /// alone.</para>
    /// </summary>
    [Theory]
    [InlineData(true)]    // milliseconds
    [InlineData(false)]   // seconds
    public void The_reset_header_is_read_in_either_unit(bool milliseconds)
    {
        using var response = Response(
            ("x-ratelimit-reset", Unix(DateTimeOffset.UtcNow.AddHours(7), milliseconds)));

        var wait = AiHttp.RateLimitWait(response);

        Assert.NotNull(wait);
        Assert.InRange(wait!.Value.TotalHours, 6.9, 7.1);
    }

    /// <summary>
    /// Told apart by magnitude, not by trusting a convention.
    ///
    /// <para>Providers disagree about the unit, and reading a millisecond timestamp as seconds puts the reset
    /// tens of thousands of years out — which would be reported to the reader with a straight face.</para>
    /// </summary>
    [Fact]
    public void An_absurd_reset_is_not_believed()
    {
        using var response = Response(("x-ratelimit-reset", "99999999999999999"));

        Assert.Null(AiHttp.RateLimitWait(response));
    }

    /// <summary>A reset a moment ago reads as "now" rather than as nothing — clocks disagree by seconds.</summary>
    [Fact]
    public void A_reset_just_past_reads_as_now()
    {
        using var response = Response(
            ("x-ratelimit-reset", Unix(DateTimeOffset.UtcNow.AddSeconds(-20), true)));

        Assert.Equal(TimeSpan.Zero, AiHttp.RateLimitWait(response));
    }

    /// <summary>The standard header wins: it states a duration outright rather than a moment to subtract
    /// from.</summary>
    [Fact]
    public void Retry_after_is_preferred_over_the_reset_header()
    {
        using var response = Response(
            ("retry-after", "30"),
            ("x-ratelimit-reset", Unix(DateTimeOffset.UtcNow.AddHours(7), true)));

        Assert.Equal(TimeSpan.FromSeconds(30), AiHttp.RateLimitWait(response));
    }

    [Fact]
    public void No_headers_means_no_wait() => Assert.Null(AiHttp.RateLimitWait(Response()));

    // ---- what the reader is told -------------------------------------------------------------------------

    /// <summary>A short wait is a minute, and says so.</summary>
    [Fact]
    public void A_short_wait_says_a_minute() =>
        Assert.Contains("in a minute", AiHttp.RateLimitMessage(TimeSpan.FromSeconds(20)));

    [Fact]
    public void A_wait_of_minutes_counts_them() =>
        Assert.Contains("about 12 minutes", AiHttp.RateLimitMessage(TimeSpan.FromMinutes(11.2)));

    /// <summary>
    /// A long wait names the hour it lifts, not just a duration.
    ///
    /// <para>"About 7 hours" invites arithmetic; the reader wants to know whether to wait or to come back
    /// tomorrow, and a clock time answers that directly.</para>
    /// </summary>
    [Fact]
    public void A_long_wait_names_the_time_it_resets()
    {
        var message = AiHttp.RateLimitMessage(TimeSpan.FromHours(7));

        Assert.Contains("resets at", message);
        Assert.Contains("about 7 hours", message);
    }

    /// <summary>
    /// With no header the wording stays vague, because vague is what we know — but it no longer promises a
    /// moment, and it names the possibility the reader cannot otherwise guess at.
    /// </summary>
    [Fact]
    public void With_no_header_it_does_not_promise_a_moment()
    {
        var message = AiHttp.RateLimitMessage(null);

        Assert.DoesNotContain("moment", message);
        Assert.Contains("daily quota", message);
    }

    /// <summary>The message reaches the reader through the ordinary error path, not only from the helper.</summary>
    [Fact]
    public void The_rate_limit_message_is_what_a_429_reports() =>
        Assert.Contains(
            "in a minute",
            AiHttp.MessageFor(AiErrorKind.RateLimited, HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(30)));

    // ---- 402, which is not a rate limit and not a bad key --------------------------------------------------

    /// <summary>
    /// 402 is its own kind.
    ///
    /// <para>Observed against Cerebras: a valid key on an account with no credit produced HTTP 402, which
    /// fell through to the catch-all and was reported as "The provider rejected the request (HTTP 402)".</para>
    /// </summary>
    [Fact]
    public void A_402_is_payment_required() =>
        Assert.Equal(AiErrorKind.PaymentRequired, AiHttp.KindFor(HttpStatusCode.PaymentRequired));

    /// <summary>
    /// And it must not read like a rejected key.
    ///
    /// <para>Nothing is wrong with the key, so "check the key" sends the reader to re-paste a working one and
    /// conclude the app is broken when that changes nothing. Nor is waiting any use, which rules out the
    /// rate-limit wording.</para>
    /// </summary>
    [Fact]
    public void The_402_message_blames_the_balance_rather_than_the_key()
    {
        var message = AiHttp.MessageFor(AiErrorKind.PaymentRequired, HttpStatusCode.PaymentRequired);

        Assert.Contains("out of credit", message);
        Assert.DoesNotContain("key", message, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("try again", message, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Statuses we have no better word for still fall through to the generic sentence, which names
    /// the code so a reader can search for it.</summary>
    [Fact]
    public void An_unrecognised_status_still_names_its_code() =>
        Assert.Contains(
            "418",
            AiHttp.MessageFor(AiErrorKind.Provider, (HttpStatusCode)418));
}
