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
        // Connection ids now, not provider kinds (#678): keys are keyed per endpoint, so the cleanup list is
        // whatever ids the tests in this class use rather than an enum's members.
        if (_store.IsAvailable)
            foreach (var id in new[] { "anthropic", "openai-compatible", "openrouter-box", "local-ollama" })
                _store.DeleteApiKey(id);
    }

    // ---- The acceptance test ------------------------------------------------------------------------------

    [Fact]
    public void The_key_never_appears_in_log_output_at_any_level()
    {
        // §6's stated acceptance criterion. Asserted across store, read AND delete, and against the structured
        // values as well as the formatted message — a leak through a log property would be just as public, and
        // is the easier one to introduce by accident.
        if (!_store.IsAvailable) return;

        _store.SetApiKey("anthropic", Secret);
        _store.GetApiKey("anthropic");
        _store.DeleteApiKey("anthropic");

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

        Assert.True(_store.SetApiKey("anthropic", Secret));
        Assert.Equal(Secret, _store.GetApiKey("anthropic"));
    }

    [Fact]
    public void Storing_twice_replaces_rather_than_failing()
    {
        // SecItemAdd refuses a duplicate, so the second write must update in place. Delete-then-add would leave
        // a window with no key at all if the add failed.
        if (!_store.IsAvailable) return;

        _store.SetApiKey("anthropic", Secret);
        Assert.True(_store.SetApiKey("anthropic", "sk-ant-second-value"));

        Assert.Equal("sk-ant-second-value", _store.GetApiKey("anthropic"));
    }

    [Fact]
    public void Providers_keep_separate_keys()
    {
        // The ordinary case for someone comparing a hosted model against a local one.
        if (!_store.IsAvailable) return;

        _store.SetApiKey("anthropic", "sk-ant-aaa");
        _store.SetApiKey("openai-compatible", "sk-oai-bbb");

        Assert.Equal("sk-ant-aaa", _store.GetApiKey("anthropic"));
        Assert.Equal("sk-oai-bbb", _store.GetApiKey("openai-compatible"));
    }

    [Fact]
    public void A_key_that_was_never_stored_reads_as_null()
    {
        if (!_store.IsAvailable) return;

        Assert.Null(_store.GetApiKey("anthropic"));
    }

    [Fact]
    public void Deleting_a_key_that_is_not_there_is_success_not_failure()
    {
        // Idempotent: Settings should be able to offer "forget this key" without first checking.
        if (!_store.IsAvailable) return;

        Assert.True(_store.DeleteApiKey("anthropic"));
    }

    [Fact]
    public void A_deleted_key_is_gone()
    {
        if (!_store.IsAvailable) return;

        _store.SetApiKey("anthropic", Secret);
        Assert.True(_store.DeleteApiKey("anthropic"));

        Assert.Null(_store.GetApiKey("anthropic"));
    }

    [Fact]
    public void A_key_with_diacritics_and_whitespace_survives_the_round_trip()
    {
        // UTF-8 through CFData, and the trim on the way in: a key pasted from a web page routinely arrives with
        // a trailing newline, and storing that verbatim produces a 401 nobody can explain.
        if (!_store.IsAvailable) return;

        _store.SetApiKey("anthropic", "  sk-ānt-ṃixed-42\n");

        Assert.Equal("sk-ānt-ṃixed-42", _store.GetApiKey("anthropic"));
    }

    [Fact]
    public void An_empty_key_is_refused_rather_than_stored()
    {
        if (!_store.IsAvailable) return;

        Assert.False(_store.SetApiKey("anthropic", "   "));
        Assert.Null(_store.GetApiKey("anthropic"));
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
            AiCredentialStore.AccountFor("anthropic"),
            AiCredentialStore.AccountFor("openai-compatible"));
    }

    [Fact]
    public void The_service_name_is_stable()
    {
        // Changing it orphans every key already stored, silently: the user is simply told they have not
        // configured one. Pinned so that only a deliberate edit can do it.
        Assert.Equal("CST Reader — AI provider", AiCredentialStore.ServiceName);
    }
}
