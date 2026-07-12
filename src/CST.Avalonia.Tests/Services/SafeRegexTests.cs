using System;
using System.Diagnostics;
using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services
{
    /// <summary>
    /// #309 A4-1: client-supplied regexes run against ~500K index terms must not catastrophically backtrack.
    /// SafeRegex compiles NonBacktracking (linear time), falls back to a timed backtracking engine for
    /// constructs the linear engine can't express, and still throws on a genuinely invalid pattern.
    /// </summary>
    public class SafeRegexTests
    {
        [Fact]
        public void Does_not_hang_on_a_catastrophic_backtracking_pattern()
        {
            var regex = SafeRegex.Compile("(a+)+b");   // classic ReDoS
            var evil = new string('a', 50);            // ~2^50 steps on a backtracking engine; linear here

            var sw = Stopwatch.StartNew();
            bool matched = SafeRegex.IsMatch(regex, evil);
            sw.Stop();

            Assert.False(matched);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"took {sw.Elapsed} — should be ~linear, not backtracking");
        }

        [Fact]
        public void Falls_back_for_an_unsupported_construct_and_still_matches()
        {
            // A backreference is unsupported by NonBacktracking -> timed backtracking fallback, still correct.
            var regex = SafeRegex.Compile(@"(a)\1");
            Assert.True(SafeRegex.IsMatch(regex, "aa"));
            Assert.False(SafeRegex.IsMatch(regex, "ab"));
        }

        [Fact]
        public void Reports_timeout_on_a_fallback_catastrophic_pattern()
        {
            // #309 fallback residual: a pattern NonBacktracking can't express (backreference) drops to the timed
            // backtracking engine; a catastrophic core then times out per-term, and IsMatch must REPORT it so an
            // expansion scan can abort a pattern that repeatedly times out instead of pinning a core over 500K terms.
            var regex = SafeRegex.Compile(@"(a)\1(a+)+$");   // backreference → fallback; (a+)+$ → catastrophic
            var evil = new string('a', 40) + "b";            // no match → catastrophic backtracking → 200ms timeout

            var sw = Stopwatch.StartNew();
            bool matched = SafeRegex.IsMatch(regex, evil, out bool timedOut);
            sw.Stop();

            Assert.False(matched);
            Assert.True(timedOut);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"took {sw.Elapsed} — should be bounded by the 200ms MatchTimeout");
        }

        [Fact]
        public void Throws_on_a_genuinely_invalid_pattern()
        {
            // A syntax error still surfaces as a parse exception (upstream "Invalid regex pattern" hint), #59.
            Assert.ThrowsAny<ArgumentException>(() => SafeRegex.Compile("(unclosed"));
        }
    }
}
