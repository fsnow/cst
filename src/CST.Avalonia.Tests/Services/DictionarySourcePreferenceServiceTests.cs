using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Dictionaries;
using CST.Tools;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// The dictionary source enable/order preference service (#479): the picker shows only enabled sources in the
/// user's order, a fresh install is appended enabled, and edits persist to <see cref="DictionaryDialogState"/>.
/// </summary>
public sealed class DictionarySourcePreferenceServiceTests
{
    // A stand-in installed source. IsAvailable is settable so "uninstalled" can be simulated.
    private sealed class FakeSource : IDictionarySource
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string DefinitionLanguage => "en";
        public DictionarySourceKind Kind => DictionarySourceKind.General;
        public bool IsAvailable { get; set; } = true;
        public DictionarySourceInfo? Attribution => null;
        public FakeSource(string id, string? displayName = null) { Id = id; DisplayName = displayName ?? id.ToUpperInvariant(); }
        public Task<IReadOnlyList<DictionaryEntry>> LookupAsync(DictionaryRequest request, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DictionaryEntry>>(new List<DictionaryEntry>());
    }

    // Registry over the given sources, plus a state-backed preference service. The Mock state exposes a real
    // ApplicationState so SourceOrder round-trips, and records MarkDirty calls.
    private static (DictionarySourcePreferenceService svc, ApplicationState state, Mock<IApplicationStateService> stateMock)
        Build(params IDictionarySource[] sources)
    {
        var registry = new DictionarySourceRegistry(sources);
        var state = new ApplicationState();
        var mock = new Mock<IApplicationStateService>();
        mock.SetupGet(s => s.Current).Returns(state);
        var svc = new DictionarySourcePreferenceService(registry, mock.Object);
        return (svc, state, mock);
    }

    [Fact]
    public void With_no_stored_preference_all_available_sources_are_enabled_in_registry_order()
    {
        var (svc, _, _) = Build(new FakeSource("en"), new FakeSource("hi"), new FakeSource("dpd"));

        var rows = svc.GetRows();
        Assert.Equal(new[] { "en", "hi", "dpd" }, rows.Select(r => r.Source.Id));
        Assert.All(rows, r => Assert.True(r.Enabled));
        Assert.Equal(new[] { "en", "hi", "dpd" }, svc.GetEffectiveSources().Select(s => s.Id));
    }

    [Fact]
    public void Disabling_a_source_removes_it_from_the_effective_picker_list_but_keeps_it_in_rows()
    {
        var (svc, state, _) = Build(new FakeSource("en"), new FakeSource("hi"), new FakeSource("dpd"));

        svc.SetEnabled("hi", false);

        Assert.Equal(new[] { "en", "dpd" }, svc.GetEffectiveSources().Select(s => s.Id));
        // Still shown (disabled) in the editor rows so it can be re-enabled.
        var hi = svc.GetRows().Single(r => r.Source.Id == "hi");
        Assert.False(hi.Enabled);
        // Persisted with an explicit entry for every source.
        Assert.Contains(state.DictionaryDialog.SourceOrder, p => p.Id == "hi" && !p.Enabled);
    }

    [Fact]
    public void Cannot_disable_the_last_enabled_source()
    {
        var (svc, _, _) = Build(new FakeSource("en"), new FakeSource("hi"));

        svc.SetEnabled("hi", false);
        svc.SetEnabled("en", false);   // would empty the picker — rejected

        Assert.Equal(new[] { "en" }, svc.GetEffectiveSources().Select(s => s.Id));
    }

    [Fact]
    public void The_last_enabled_guard_ignores_a_retained_but_uninstalled_enabled_source()
    {
        // en + dppn both enabled, dppn then uninstalled (its enabled pref is retained). Disabling en would
        // leave only the uninstalled dppn "enabled" — which draws nothing — so it must be rejected. (Fable MEDIUM-3)
        var en = new FakeSource("en");
        var dppn = new FakeSource("dppn");
        var (svc, _, _) = Build(en, dppn);
        svc.Move("dppn", -1);       // establish a stored pref that lists dppn, enabled
        dppn.IsAvailable = false;   // uninstalled

        svc.SetEnabled("en", false);

        Assert.Equal(new[] { "en" }, svc.GetEffectiveSources().Select(s => s.Id));   // still non-empty
    }

    [Fact]
    public void Disabled_state_survives_a_serialization_round_trip()
    {
        // The state writer uses WhenWritingDefault, which would omit Enabled==false (a bool default) unless
        // the property forces serialization. Round-trip through the EXACT writer options. (Fable DO-NOT-SHIP-1)
        var (svc, state, _) = Build(new FakeSource("en"), new FakeSource("hi"));
        svc.SetEnabled("hi", false);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            Converters = { new JsonStringEnumConverter() }
        };
        var json = JsonSerializer.Serialize(state.DictionaryDialog, options);
        var restored = JsonSerializer.Deserialize<DictionaryDialogState>(json, options)!;

        var hi = Assert.Single(restored.SourceOrder, p => p.Id == "hi");
        Assert.False(hi.Enabled);   // regression guard: must NOT silently re-enable on reload
    }

    [Fact]
    public void Move_reorders_the_effective_list_and_first_enabled_is_the_default()
    {
        var (svc, _, _) = Build(new FakeSource("en"), new FakeSource("hi"), new FakeSource("dpd"));

        svc.Move("dpd", -1);   // dpd up one → en, dpd, hi
        Assert.Equal(new[] { "en", "dpd", "hi" }, svc.GetEffectiveSources().Select(s => s.Id));

        svc.Move("dpd", -1);   // dpd up again → dpd, en, hi (dpd now the default)
        Assert.Equal(new[] { "dpd", "en", "hi" }, svc.GetEffectiveSources().Select(s => s.Id));

        svc.Move("dpd", -1);   // already first → no-op
        Assert.Equal(new[] { "dpd", "en", "hi" }, svc.GetEffectiveSources().Select(s => s.Id));
    }

    [Fact]
    public void A_newly_installed_source_is_appended_enabled_after_the_stored_order()
    {
        var en = new FakeSource("en");
        var hi = new FakeSource("hi");
        var (svc, state, _) = Build(en, hi);

        // Establish a custom order/enable so a stored preference exists.
        svc.Move("hi", -1);            // hi, en
        svc.SetEnabled("en", false);   // hi enabled, en disabled

        // Simulate a fresh DPD install by rebuilding the service over an expanded registry, reusing state.
        var registry = new DictionarySourceRegistry(new IDictionarySource[] { en, hi, new FakeSource("dpd") });
        var mock = new Mock<IApplicationStateService>();
        mock.SetupGet(s => s.Current).Returns(state);
        var svc2 = new DictionarySourcePreferenceService(registry, mock.Object);

        // Stored order preserved (hi, en) with en still disabled; dpd appended enabled.
        Assert.Equal(new[] { "hi", "en", "dpd" }, svc2.GetRows().Select(r => r.Source.Id));
        Assert.Equal(new[] { "hi", "dpd" }, svc2.GetEffectiveSources().Select(s => s.Id));
    }

    [Fact]
    public void An_uninstalled_source_is_hidden_but_its_preference_is_retained()
    {
        var en = new FakeSource("en");
        var dppn = new FakeSource("dppn");
        var (svc, state, _) = Build(en, dppn);

        svc.Move("dppn", -1);   // dppn, en — establish a stored order that mentions dppn

        dppn.IsAvailable = false;   // uninstalled

        Assert.Equal(new[] { "en" }, svc.GetRows().Select(r => r.Source.Id));
        Assert.Equal(new[] { "en" }, svc.GetEffectiveSources().Select(s => s.Id));
        // The dppn preference survives in state for a later re-install.
        Assert.Contains(state.DictionaryDialog.SourceOrder, p => p.Id == "dppn");
    }

    [Fact]
    public void Every_edit_marks_state_dirty_and_raises_Changed()
    {
        var (svc, _, mock) = Build(new FakeSource("en"), new FakeSource("hi"));
        var changed = 0;
        svc.Changed += (_, _) => changed++;

        svc.SetEnabled("hi", false);
        svc.Move("hi", -1);

        Assert.Equal(2, changed);
        mock.Verify(s => s.MarkDirty(), Times.Exactly(2));
    }

    [Fact]
    public void A_no_op_edit_does_not_mark_dirty_or_raise_Changed()
    {
        var (svc, _, mock) = Build(new FakeSource("en"), new FakeSource("hi"));
        var changed = 0;
        svc.Changed += (_, _) => changed++;

        svc.SetEnabled("en", true);   // already enabled
        svc.Move("en", -1);           // already first
        svc.Move("hi", +1);           // already last

        Assert.Equal(0, changed);
        mock.Verify(s => s.MarkDirty(), Times.Never);
    }
}
