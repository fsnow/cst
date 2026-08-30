using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.ViewModels;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// The commit policy of the final pre-shutdown position capture. (#891)
///
/// <para>
/// The stored anchor and #434 token must describe the SAME moment, because restore prefers the token: a
/// fresh anchor committed beside a staler token is silently ignored in the token's favour, and the reader
/// reopens "a bit off" (#551, #537). The original code captured anchor-then-token off an unhoisted
/// property, so a drag-teardown between the two awaits produced exactly that half-update — the NRE it
/// also threw was caught and merely logged. These tests pin the policy that replaces it:
/// token first, and never a fresh anchor beside a stale token.
/// </para>
///
/// <para>
/// Honest limit: the race itself — ControlRecycling nulling BookDisplayControl between two UI-thread
/// awaits — needs a live view and dispatcher and is NOT reproduced here. What is tested is the policy
/// core the instance method delegates to, whose delegates close over a control hoisted ONCE; the hoist
/// itself is three lines verified by reading, not by a test.
/// </para>
/// </summary>
public class FinalPositionCaptureTests
{
    private static readonly ReadingPositionToken FreshToken = new()
    {
        Above = "para42",
        Below = "para43",
        Fraction = 0.25,
    };

    private static Func<Task<ReadingPositionToken?>> Token(ReadingPositionToken? value)
        => () => Task.FromResult(value);

    private static Func<Task<string?>> Anchor(string? value)
        => () => Task.FromResult(value);

    // ---- the happy path ----

    [Theory]
    [InlineData(true)]    // hasStoredToken must not suppress a SUCCESSFUL capture
    [InlineData(false)]
    public async Task Both_captures_succeeding_commit_a_fresh_consistent_pair(bool hasStoredToken)
    {
        var (token, anchor) = await BookDisplayViewModel.CaptureFinalPositionCoreAsync(
            Token(FreshToken), Anchor("para42"), hasStoredToken);

        Assert.Same(FreshToken, token);
        Assert.Equal("para42", anchor);
    }

    // ---- the #891 half-update, both directions ----

    [Fact]
    public async Task Token_lost_with_a_stored_token_commits_nothing_and_never_asks_for_the_anchor()
    {
        // The stored pair is at most one 200ms tick stale and self-consistent. A fresh anchor would be
        // ignored on restore (token preferred) while desynchronizing the pair — the #891 drift.
        var anchorAsked = false;
        var (token, anchor) = await BookDisplayViewModel.CaptureFinalPositionCoreAsync(
            Token(null),
            () => { anchorAsked = true; return Task.FromResult<string?>("para42"); },
            hasStoredToken: true);

        Assert.Null(token);
        Assert.Null(anchor);
        Assert.False(anchorAsked);
    }

    [Fact]
    public async Task Token_captured_then_control_dies_still_commits_the_token()
    {
        // Interrupted between the two captures: "fresh token + stale anchor" is the CORRECT partial
        // state — the token wins on restore. This is why the token goes first.
        var (token, anchor) = await BookDisplayViewModel.CaptureFinalPositionCoreAsync(
            Token(FreshToken), Anchor(null), hasStoredToken: true);

        Assert.Same(FreshToken, token);
        Assert.Null(anchor);
    }

    [Fact]
    public async Task Token_is_captured_before_the_anchor()
    {
        var calls = new List<string>();
        await BookDisplayViewModel.CaptureFinalPositionCoreAsync(
            () => { calls.Add("token"); return Task.FromResult<ReadingPositionToken?>(FreshToken); },
            () => { calls.Add("anchor"); return Task.FromResult<string?>("para42"); },
            hasStoredToken: false);

        Assert.Equal(new[] { "token", "anchor" }, calls);
    }

    // ---- the no-token-yet book ----

    [Fact]
    public async Task With_no_stored_token_a_failed_token_capture_still_takes_the_anchor()
    {
        // A book closed within its first status tick has no token at all; the anchor is then the only
        // thing restore can use, so skipping it here would lose the position entirely.
        var (token, anchor) = await BookDisplayViewModel.CaptureFinalPositionCoreAsync(
            Token(null), Anchor("para7"), hasStoredToken: false);

        Assert.Null(token);
        Assert.Equal("para7", anchor);
    }

    // ---- anchor normalization ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]   // the JS side reports "no anchor" as the literal string
    public async Task Non_answers_from_the_anchor_capture_leave_the_stored_anchor_alone(string? raw)
    {
        var (token, anchor) = await BookDisplayViewModel.CaptureFinalPositionCoreAsync(
            Token(FreshToken), Anchor(raw), hasStoredToken: false);

        Assert.Same(FreshToken, token);
        Assert.Null(anchor);
    }

    [Fact]
    public async Task Genuinely_asynchronous_captures_complete_the_same_way()
    {
        // The awaits must survive delegates that actually yield — the real captures are CEF round trips,
        // never synchronously complete. Catches a rewrite that only works when awaits run inline.
        var (token, anchor) = await BookDisplayViewModel.CaptureFinalPositionCoreAsync(
            async () => { await Task.Yield(); return FreshToken; },
            async () => { await Task.Yield(); return "para42"; },
            hasStoredToken: true);

        Assert.Same(FreshToken, token);
        Assert.Equal("para42", anchor);
    }
}
