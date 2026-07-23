using System;
using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.Models;
using CST.Avalonia.Services;

namespace CST.Avalonia.Services.Dictionaries;

/// <summary>
/// The user-managed enable/order preference for the dictionary sources (#479). The dictionary panel's
/// picker shows only the ENABLED sources, in this order; a fresh install (a source with no stored
/// preference) is appended ENABLED so it appears without a settings hunt. First enabled = the default.
///
/// The preference is applied HERE (and consumed by <see cref="ViewModels.DictionaryViewModel"/>), never in
/// the registry or the /v1 API path — an agent still enumerates every installed source. Persisted in
/// <see cref="DictionaryDialogState.SourceOrder"/>, alongside the last-selected <c>SourceId</c>.
///
/// A single instance is shared by the Settings editor (which mutates it) and the panel VM (which consumes
/// it and rebuilds its picker on <see cref="Changed"/>).
/// </summary>
public sealed class DictionarySourcePreferenceService
{
    private readonly DictionarySourceRegistry _registry;
    private readonly IApplicationStateService _state;

    /// <summary>Raised after the enable/order preference changes so the live panel can rebuild its picker.</summary>
    public event EventHandler? Changed;

    public DictionarySourcePreferenceService(DictionarySourceRegistry registry, IApplicationStateService state)
    {
        _registry = registry;
        _state = state;
    }

    private List<DictionarySourcePreference> Stored => _state.Current.DictionaryDialog.SourceOrder;

    /// <summary>One editable row: an available source and whether it is enabled in the picker.</summary>
    public sealed record SourceRow(IDictionarySource Source, bool Enabled);

    /// <summary>
    /// Every AVAILABLE source paired with its enabled flag, in preference order: stored order first (by id),
    /// then any newly-installed source appended enabled. DISABLED sources are included — the Settings editor
    /// needs them so they can be re-enabled. Sources in the stored list that are no longer installed are
    /// skipped (their preference is retained in state for a later re-install, just not shown).
    /// </summary>
    public IReadOnlyList<SourceRow> GetRows()
    {
        var available = _registry.Available;
        var byId = available.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var rows = new List<SourceRow>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pref in Stored)
            if (byId.TryGetValue(pref.Id, out var src) && used.Add(src.Id))
                rows.Add(new SourceRow(src, pref.Enabled));
        foreach (var src in available)
            if (used.Add(src.Id))
                rows.Add(new SourceRow(src, true));   // newly installed → enabled, appended (#479)
        return rows;
    }

    /// <summary>The picker list: enabled available sources in preference order. First = the default source.</summary>
    public IReadOnlyList<IDictionarySource> GetEffectiveSources() =>
        GetRows().Where(r => r.Enabled).Select(r => r.Source).ToList();

    /// <summary>Enable or disable a source. Ignored if it would leave no enabled source (the picker must
    /// never be empty) or if the value is unchanged.</summary>
    public void SetEnabled(string id, bool enabled)
    {
        var list = Reconcile();
        var entry = list.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        if (entry == null || entry.Enabled == enabled)
            return;
        if (!enabled && !AnyOtherAvailableEnabled(list, id))
            return;   // keep at least one enabled AND installed source — the picker must never be empty
        entry.Enabled = enabled;
        Commit(list);
    }

    /// <summary>Move a source up (delta &lt; 0) or down (delta &gt; 0) among the shown (available) rows.</summary>
    public void Move(string id, int delta)
    {
        if (delta == 0)
            return;
        var list = Reconcile();
        // Reorder among the AVAILABLE (shown) entries only, stepping over any retained-but-uninstalled
        // placeholders so the visible order matches what the user sees.
        var availableIds = _registry.Available.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var shown = list.Select((p, i) => (pref: p, index: i))
                        .Where(t => availableIds.Contains(t.pref.Id))
                        .ToList();
        var pos = shown.FindIndex(t => string.Equals(t.pref.Id, id, StringComparison.OrdinalIgnoreCase));
        if (pos < 0)
            return;
        var targetPos = pos + Math.Sign(delta);
        if (targetPos < 0 || targetPos >= shown.Count)
            return;
        // Remove-and-reinsert next to the target (rather than swapping absolute slots), so a retained
        // placeholder for an uninstalled source keeps its anchor relative to its neighbours. (#479, Fable LOW-5)
        var fromIndex = shown[pos].index;
        var toIndex = shown[targetPos].index;
        var entry = list[fromIndex];
        list.RemoveAt(fromIndex);
        var targetAdjusted = toIndex > fromIndex ? toIndex - 1 : toIndex;   // target's index after removal
        var insertAt = delta > 0 ? targetAdjusted + 1 : targetAdjusted;
        list.Insert(insertAt, entry);
        Commit(list);
    }

    // True iff some source OTHER than exceptId is both enabled and currently installed — the picker draws
    // only from installed sources, so a retained-but-uninstalled enabled placeholder must not count. (Fable MEDIUM-3)
    private bool AnyOtherAvailableEnabled(List<DictionarySourcePreference> list, string exceptId)
    {
        var availableIds = _registry.Available.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return list.Any(p => p.Enabled
                             && availableIds.Contains(p.Id)
                             && !string.Equals(p.Id, exceptId, StringComparison.OrdinalIgnoreCase));
    }

    // Materialize the persisted order to hold an explicit entry for every available source (preserving the
    // stored entries and any uninstalled placeholders), so an edit captures a stable order + enable state
    // even the first time the user touches an all-defaults list.
    private List<DictionarySourcePreference> Reconcile()
    {
        var list = Stored.Select(p => new DictionarySourcePreference { Id = p.Id, Enabled = p.Enabled }).ToList();
        var have = new HashSet<string>(list.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var src in _registry.Available)
            if (have.Add(src.Id))
                list.Add(new DictionarySourcePreference { Id = src.Id, Enabled = true });
        return list;
    }

    private void Commit(List<DictionarySourcePreference> list)
    {
        _state.Current.DictionaryDialog.SourceOrder = list;
        _state.MarkDirty();   // persist via the state timer/shutdown save (STATE-2), like SourceId
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
