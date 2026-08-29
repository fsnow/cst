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

    public ReopenableLemmaProvider(string assetPath)
    {
        _assetPath = assetPath;
        _inner = new SqliteLemmaProvider(assetPath);
    }

    /// <summary>
    /// Rebuild the inner provider from the asset path — call after the asset has been installed and is live.
    /// Safe to call when the asset is still absent (the new inner is simply unavailable, as before).
    ///
    /// <para><b>The outgoing provider is disposed BEFORE the replacement is built, and the order is the fix.
    /// </b> An update replaces the asset in place, which on macOS and Linux is a rename that succeeds over
    /// open handles; the replacement provider then builds the same connection string, and Microsoft.Data.
    /// Sqlite pools by connection string — so its first <c>Open()</c> was handed a pooled handle still bound
    /// to the replaced file's old inode. The app logged the new asset active and went on serving the
    /// superseded one, in lemma search and the dictionary panel alike, until the next launch. Clearing the
    /// pool first means the replacement opens the file that is actually there. (#869)</para>
    ///
    /// <para>This used to retire the outgoing provider instead, holding it until the wrapper itself was
    /// disposed, on the reasoning that a caller mid-query would meet <c>ObjectDisposedException</c>. That
    /// does not apply to this provider: its <c>Dispose</c> only calls <c>SqliteConnection.ClearPool</c>,
    /// which by contract leaves connections that are in use alone — they are discarded when they close
    /// rather than returned to the pool — and the instance stays perfectly usable afterwards, merely
    /// unpooled. So there was nothing to protect, and the protection was what kept the stale handles
    /// alive.</para>
    /// </summary>
    public void Reopen()
    {
        ILemmaProvider outgoing;
        lock (_gate) outgoing = _inner;

        try { outgoing.Dispose(); } catch { /* best effort — a failed pool clear must not block the swap */ }

        var fresh = new SqliteLemmaProvider(_assetPath);
        lock (_gate) _inner = fresh;
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
        ILemmaProvider inner;
        lock (_gate) inner = _inner;
        try { inner.Dispose(); } catch { /* best effort — teardown */ }
    }
}
