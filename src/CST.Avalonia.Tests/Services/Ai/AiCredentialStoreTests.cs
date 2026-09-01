using System;
using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.Services.Ai;
using CST.Avalonia.Services.Ai.Credentials;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// Key storage. (#579, AI_SURFACE_B.md §6)
///
/// <para>Every test uses a <b>unique service name</b>, so the suite can never read, overwrite or delete the key
/// a developer has actually stored. Sharing the real one would make running the tests a way to lose a
/// credential.</para>
///
/// <para>The Keychain tests no-op off macOS rather than failing: this lands macOS-first by design, and a red
/// suite on Windows would say "broken" where the truth is "not built yet".</para>
/// </summary>
public class AiCredentialStoreTests : IDisposable
{
    /// <summary>Captures everything written at every level, so a leak has nowhere to hide.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        internal readonly List<string> Lines = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Lines.Add(formatter(state, exception));
            // The formatted message is not the only channel — a structured sink writes the raw values too.
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
                Lines.AddRange(pairs.Select(p => $"{p.Key}={p.Value}"));
        }
    }

    private const string Secret = "sk-ant-do-not-log-me-4f9a2c";

    private readonly string _service = "CST Reader test " + Guid.NewGuid().ToString("N");
    private readonly CapturingLogger<AiCredentialStore> _log = new();
    private readonly AiCredentialStore _store;

    public AiCredentialStoreTests() => _store = new AiCredentialStore(_log, _service);

    public void Dispose()
    {
        // Connection ids, not provider kinds (#678), and every NAME each id is used with (#759): an account is
        // the pair, so sweeping only the primary one would leave a test's second secret behind in the
        // developer's own keychain.
        if (!_store.IsAvailable) return;

        foreach (var id in new[] { "anthropic", "openai-compatible", "openrouter-box", "local-ollama", "gw", "gw-header" })
            foreach (var name in new[] { AiCredentialNames.Primary, "gateway", "header-x", "x" })
                _store.Delete(id, name);
    }

    // ---- The acceptance test ------------------------------------------------------------------------------

    [Fact]
    public void The_key_never_appears_in_log_output_at_any_level()
    {
        // §6's stated acceptance criterion. Asserted across store, read AND delete, and against the structured
        // values as well as the formatted message — a leak through a log property would be just as public, and
        // is the easier one to introduce by accident.
        if (!_store.IsAvailable) return;

        _store.Set("anthropic", AiCredentialNames.Primary, Secret);
        _store.Get("anthropic", AiCredentialNames.Primary);
        _store.Delete("anthropic", AiCredentialNames.Primary);

        Assert.NotEmpty(_log.Lines);   // the logger really was wired, so absence means absence
        foreach (var line in _log.Lines)
        {
            Assert.DoesNotContain(Secret, line, StringComparison.Ordinal);
            // Not even a fragment: a logged prefix narrows a guess, and a logged length narrows it further.
            Assert.DoesNotContain(Secret[..12], line, StringComparison.Ordinal);
            Assert.DoesNotContain(Secret.Length.ToString(), line, StringComparison.Ordinal);
        }
    }

    // ---- Round trip ---------------------------------------------------------------------------------------

    [Fact]
    public void A_stored_key_comes_back()
    {
        if (!_store.IsAvailable) return;

        Assert.True(_store.Set("anthropic", AiCredentialNames.Primary, Secret));
        Assert.Equal(Secret, _store.Get("anthropic", AiCredentialNames.Primary));
    }

    [Fact]
    public void Storing_twice_replaces_rather_than_failing()
    {
        // SecItemAdd refuses a duplicate, so the second write must update in place. Delete-then-add would leave
        // a window with no key at all if the add failed.
        if (!_store.IsAvailable) return;

        _store.Set("anthropic", AiCredentialNames.Primary, Secret);
        Assert.True(_store.Set("anthropic", AiCredentialNames.Primary, "sk-ant-second-value"));

        Assert.Equal("sk-ant-second-value", _store.Get("anthropic", AiCredentialNames.Primary));
    }

    [Fact]
    public void Providers_keep_separate_keys()
    {
        // The ordinary case for someone comparing a hosted model against a local one.
        if (!_store.IsAvailable) return;

        _store.Set("anthropic", AiCredentialNames.Primary, "sk-ant-aaa");
        _store.Set("openai-compatible", AiCredentialNames.Primary, "sk-oai-bbb");

        Assert.Equal("sk-ant-aaa", _store.Get("anthropic", AiCredentialNames.Primary));
        Assert.Equal("sk-oai-bbb", _store.Get("openai-compatible", AiCredentialNames.Primary));
    }

    [Fact]
    public void A_key_that_was_never_stored_reads_as_null()
    {
        if (!_store.IsAvailable) return;

        Assert.Null(_store.Get("anthropic", AiCredentialNames.Primary));
    }

    [Fact]
    public void Deleting_a_key_that_is_not_there_is_success_not_failure()
    {
        // Idempotent: Settings should be able to offer "forget this key" without first checking.
        if (!_store.IsAvailable) return;

        Assert.True(_store.Delete("anthropic", AiCredentialNames.Primary));
    }

    [Fact]
    public void A_deleted_key_is_gone()
    {
        if (!_store.IsAvailable) return;

        _store.Set("anthropic", AiCredentialNames.Primary, Secret);
        Assert.True(_store.Delete("anthropic", AiCredentialNames.Primary));

        Assert.Null(_store.Get("anthropic", AiCredentialNames.Primary));
    }

    [Fact]
    public void A_key_with_diacritics_and_whitespace_survives_the_round_trip()
    {
        // UTF-8 through CFData, and the trim on the way in: a key pasted from a web page routinely arrives with
        // a trailing newline, and storing that verbatim produces a 401 nobody can explain.
        if (!_store.IsAvailable) return;

        _store.Set("anthropic", AiCredentialNames.Primary, "  sk-ānt-ṃixed-42\n");

        Assert.Equal("sk-ānt-ṃixed-42", _store.Get("anthropic", AiCredentialNames.Primary));
    }

    [Fact]
    public void An_empty_key_is_refused_rather_than_stored()
    {
        if (!_store.IsAvailable) return;

        Assert.False(_store.Set("anthropic", AiCredentialNames.Primary, "   "));
        Assert.Null(_store.Get("anthropic", AiCredentialNames.Primary));
    }

    // ---- Named credentials (#759) -------------------------------------------------------------------------

    [Fact]
    public void Two_names_on_one_connection_are_two_secrets()
    {
        // The case the whole reshape exists for: Cloudflare's gateway wants a gateway token beside the
        // upstream key (#701), and one opaque string per connection had nowhere to put the second.
        if (!_store.IsAvailable) return;

        _store.Set("gw", AiCredentialNames.Primary, "sk-upstream");
        _store.Set("gw", "gateway", "cf-gateway-token");

        Assert.Equal("sk-upstream", _store.Get("gw", AiCredentialNames.Primary));
        Assert.Equal("cf-gateway-token", _store.Get("gw", "gateway"));
    }

    [Fact]
    public void Deleting_one_name_leaves_the_others()
    {
        // Degrading one credential at a time is the reason for N accounts rather than one JSON blob per
        // connection: losing the gateway token must not lose the upstream key with it.
        if (!_store.IsAvailable) return;

        _store.Set("gw", AiCredentialNames.Primary, "sk-upstream");
        _store.Set("gw", "gateway", "cf-gateway-token");

        Assert.True(_store.Delete("gw", "gateway"));

        Assert.Null(_store.Get("gw", "gateway"));
        Assert.Equal("sk-upstream", _store.Get("gw", AiCredentialNames.Primary));
    }

    [Fact]
    public void A_name_that_was_never_stored_reads_as_null_even_when_another_name_was()
    {
        // Absence of one secret must not be answered with a different one — that would present as a 401 the
        // reader cannot attribute, which is #678's symptom exactly.
        if (!_store.IsAvailable) return;

        _store.Set("gw", AiCredentialNames.Primary, "sk-upstream");

        Assert.Null(_store.Get("gw", "gateway"));
    }

    [Fact]
    public void An_id_ending_in_a_name_does_not_share_an_account_with_it()
    {
        // The regression this design exists to make unreachable. Sanitising the JOINED string folds the
        // separator into '-', which ids may contain, so ("gw", "header-x") and ("gw-header", "x") both become
        // "gw-header-x" and silently overwrite each other. Sanitising each part and joining afterwards cannot:
        // the separator never occurs inside a part, so the split is unambiguous.
        //
        // Invisible if it regresses — the symptom is one connection answering with another's credential, i.e.
        // a 401 naming neither cause — which is why it is pinned rather than left to the doc comment.
        if (!_store.IsAvailable) return;

        _store.Set("gw", "header-x", "secret-for-gw");
        _store.Set("gw-header", "x", "secret-for-gw-header");

        Assert.Equal("secret-for-gw", _store.Get("gw", "header-x"));
        Assert.Equal("secret-for-gw-header", _store.Get("gw-header", "x"));
    }

    // ---- Platform behaviour -------------------------------------------------------------------------------

    [Fact]
    public void Availability_matches_the_platform_this_ships_for()
    {
        // Both shipping platforms now have a store: Keychain on macOS (#608), DPAPI on Windows (#579). Linux
        // remains deliberately false while it is unshipped.
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsWindows())
        {
            Assert.True(_store.IsAvailable);
            Assert.Null(_store.Unavailable);
        }
        else
        {
            Assert.False(_store.IsAvailable);
            Assert.False(string.IsNullOrWhiteSpace(_store.Unavailable));
        }
    }

    [Fact]
    public void An_unavailable_platform_explains_that_a_keyless_endpoint_still_works()
    {
        // The privacy-first configuration — a local runner on loopback — needs no key at all, so "no secure
        // storage" must not read as "no assistant". Only reachable where storage is genuinely absent, which
        // after #579 means Linux (or a Windows profile whose data folder cannot be written).
        if (_store.IsAvailable) return;

        Assert.Contains("no API key still works", _store.Unavailable);
    }

    [Fact]
    public void Each_provider_gets_its_own_account_name()
    {
        Assert.NotEqual(
            AiCredentialStore.AccountFor("anthropic", AiCredentialNames.Primary),
            AiCredentialStore.AccountFor("openai-compatible", AiCredentialNames.Primary));
    }

    [Fact]
    public void Each_name_gets_its_own_account_name()
    {
        Assert.NotEqual(
            AiCredentialStore.AccountFor("gw", AiCredentialNames.Primary),
            AiCredentialStore.AccountFor("gw", "gateway"));
    }

    [Fact]
    public void The_separator_cannot_occur_inside_either_part()
    {
        // What makes the account string unambiguously splittable, and therefore collision-free. Asserted on the
        // sanitiser's OUTPUT rather than on its allowed set, so widening that set trips this rather than
        // silently reopening the collision An_id_ending_in_a_name_does_not_share_an_account_with_it pins.
        //
        // Deliberately does not name the separator: it differs by platform (':' on macOS, '.' on Windows,
        // because a DPAPI account is a filename), and a test that named one would be vacuous on the other.
        // What must hold on both is that exactly ONE character of the account lies outside what Sanitize
        // emits — the join — so the parts cannot bleed into each other.
        var account = AiCredentialStore.AccountFor("a:b.c", "d.e:f");

        Assert.Equal(1, account.Count(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')));
    }

    [Fact]
    public void The_account_spelling_is_stable()
    {
        // The same property ServiceName has, and for the same reason: change it and every credential already
        // stored reads as "not configured", with no error anywhere. The round-trip tests above cannot hold
        // this line - save and find share the value in-process, so they stay green across any reformatting of
        // the account - and The_separator_cannot_occur_inside_either_part deliberately does not name the
        // separator. So the exact spelling is pinned here, per platform.
        //
        // The concrete regression: someone "unifies" the two separators to one character (they look like an
        // irregularity, and '.' is legal in a Keychain account too). Every macOS credential moves from
        // anthropic:primary to anthropic.primary, every stored key silently disappears, and the whole suite
        // passes. (fable review)
        if (OperatingSystem.IsWindows())
            Assert.Equal("anthropic.primary", AiCredentialStore.AccountFor("anthropic", AiCredentialNames.Primary));
        else
            Assert.Equal("anthropic:primary", AiCredentialStore.AccountFor("anthropic", AiCredentialNames.Primary));
    }

    /// <summary>
    /// The two collision tests are load-bearing AS A PAIR, which is worth saying before someone deletes the
    /// "redundant" one: a '_' separator leaves ("gw","header-x") and ("gw-header","x") distinct, so
    /// An_id_ending_in_a_name_does_not_share_an_account_with_it stays green - and only
    /// The_separator_cannot_occur_inside_either_part catches it, because '_' is a character Sanitize emits.
    /// (fable review)
    /// </summary>
    // ---- the state channel, on the REAL store (#926) ---------------------------------------------------

    /// <summary>
    /// <c>Read</c> on the real store, not on a test double.
    ///
    /// <para><b>Why this needed writing.</b> #926 added four outcomes and seven test fakes that implement
    /// them — so every test of the new states was testing the fakes' model of the store rather than the
    /// store. <c>Get</c> was covered throughout this file and <c>Read</c> nowhere, which left the real
    /// dispatch, the <c>!IsAvailable</c> branch and the log wiring exercised only in production. (fable)</para>
    /// </summary>
    [Fact]
    public void Read_reports_not_stored_before_anything_is_stored_and_found_after()
    {
        Assert.Equal(CredentialState.NotStored, _store.Read("anthropic", AiCredentialNames.Primary).State);

        Assert.True(_store.Set("anthropic", AiCredentialNames.Primary, Secret));

        var read = _store.Read("anthropic", AiCredentialNames.Primary);
        Assert.Equal(CredentialState.Found, read.State);
        Assert.Equal(Secret, read.Secret);
        Assert.True(read.Exists);
    }

    [Fact]
    public void Read_reports_not_stored_again_after_a_delete()
    {
        _store.Set("anthropic", AiCredentialNames.Primary, Secret);
        Assert.True(_store.Delete("anthropic", AiCredentialNames.Primary));

        var read = _store.Read("anthropic", AiCredentialNames.Primary);
        Assert.Equal(CredentialState.NotStored, read.State);
        Assert.False(read.Exists);
    }

    /// <summary>Get is Read's value and nothing else, so the two can never disagree about the same item.</summary>
    [Fact]
    public void Get_agrees_with_Read_on_the_real_store()
    {
        Assert.Null(_store.Get("anthropic", AiCredentialNames.Primary));
        Assert.Null(_store.Read("anthropic", AiCredentialNames.Primary).Secret);

        _store.Set("anthropic", AiCredentialNames.Primary, Secret);

        Assert.Equal(
            _store.Read("anthropic", AiCredentialNames.Primary).Secret,
            _store.Get("anthropic", AiCredentialNames.Primary));
    }

    /// <summary>
    /// The outcome word reaches the log, and the secret still does not. (#926)
    ///
    /// <para>The log line is the only place three of the four states are visible at all today, so a change
    /// that stopped <c>Read</c> calling <see cref="CredentialRead.Describe"/> would otherwise be silent.</para>
    /// </summary>
    [Fact]
    public void Read_logs_the_outcome_and_never_the_secret()
    {
        _store.Read("anthropic", AiCredentialNames.Primary);
        Assert.Contains(_log.Lines, l => l.Contains("none stored", StringComparison.Ordinal));

        _log.Lines.Clear();
        _store.Set("anthropic", AiCredentialNames.Primary, Secret);
        _store.Read("anthropic", AiCredentialNames.Primary);

        Assert.Contains(_log.Lines, l => l.Contains("found", StringComparison.Ordinal));
        Assert.All(_log.Lines, l => Assert.DoesNotContain(Secret, l, StringComparison.Ordinal));
        Assert.All(_log.Lines, l => Assert.DoesNotContain(Secret[..12], l, StringComparison.Ordinal));
    }

    [Fact]
    public void The_service_name_is_stable()
    {
        // Changing it orphans every key already stored, silently: the user is simply told they have not
        // configured one. Pinned so that only a deliberate edit can do it.
        Assert.Equal("CST Reader — AI provider", AiCredentialStore.ServiceName);
    }
}
