using System;
using System.Collections.Generic;
using CST.Lemma;

namespace CST.Avalonia.Services;

/// <summary>
/// An <see cref="ILemmaProvider"/> that can be re-opened when the underlying asset appears or changes.
///
/// <para><see cref="SqliteLemmaProvider"/> binds availability in its constructor — it returns early when the
/// file is missing, so <c>IsAvailable</c> stays false for the life of the instance. Since it is registered as
/// a DI singleton, a first run that downloads <c>dpd-cst-subset.db</c> AFTER startup left DPD permanently
/// unavailable for that session: the dictionary only appeared after a restart. (#536)</para>
///
/// <para>This wrapper keeps the singleton reference that <c>LemmaSearchService</c>, <c>LemmaReportService</c>
/// and <c>DpdDictionarySource</c> already hold, and swaps the inner provider underneath them on
/// <see cref="Reopen"/> — so an asset that lands mid-session goes live with no restart and no re-wiring.</para>
/// </summary>
public sealed class ReopenableLemmaProvider : ILemmaProvider
{
    private readonly string _assetPath;
    private readonly object _gate = new();
    private ILemmaProvider _inner;

    /// <summary>
    /// Superseded inner providers, disposed only when this wrapper is.
    ///
    /// A caller can be mid-query on the previous instance when <see cref="Reopen"/> swaps it — every member
    /// below reads the reference under the lock and then calls OUTSIDE it, so disposing eagerly would race a
    /// live query into <c>ObjectDisposedException</c>. Retiring instead of disposing costs one idle SQLite
    /// connection per reopen, and a reopen happens at most once or twice per session (an asset install).
    /// </summary>
    private readonly List<ILemmaProvider> _retired = new();

    public ReopenableLemmaProvider(string assetPath)
    {
        _assetPath = assetPath;
        _inner = new SqliteLemmaProvider(assetPath);
    }

    /// <summary>
    /// Rebuild the inner provider from the asset path — call after the asset has been installed and is live.
    /// Safe to call when the asset is still absent (the new inner is simply unavailable, as before).
    /// </summary>
    public void Reopen()
    {
        var fresh = new SqliteLemmaProvider(_assetPath);
        lock (_gate)
        {
            _retired.Add(_inner);
            _inner = fresh;
        }
    }

    private ILemmaProvider Current
    {
        get { lock (_gate) return _inner; }
    }

    public bool IsAvailable => Current.IsAvailable;
    public DpdLemmaMeta? Meta => Current.Meta;
    public FormResolution? ResolveForm(string form) => Current.ResolveForm(form);
    public LemmaExpansion? ExpandLemma(long lemmaId, bool includeFamily = false) => Current.ExpandLemma(lemmaId, includeFamily);
    public FormDeconstruction? Deconstruct(string form) => Current.Deconstruct(form);
    public LemmaCandidate? GetLemma(long lemmaId) => Current.GetLemma(lemmaId);
    public LemmaDetail? GetDetail(long lemmaId) => Current.GetDetail(lemmaId);

    public void Dispose()
    {
        List<ILemmaProvider> toDispose;
        lock (_gate)
        {
            toDispose = new List<ILemmaProvider>(_retired) { _inner };
            _retired.Clear();
        }
        foreach (var p in toDispose)
        {
            try { p.Dispose(); } catch { /* best effort — teardown */ }
        }
    }
}
