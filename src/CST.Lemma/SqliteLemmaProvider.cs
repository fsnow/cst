using Microsoft.Data.Sqlite;

namespace CST.Lemma;

/// <summary>
/// <see cref="ILemmaProvider"/> over a DPD-lemma SQLite asset. Opens the file read-only; uses a fresh
/// pooled connection per call so concurrent callers (GUI + API) are safe. If the asset is missing or
/// unreadable, <see cref="IsAvailable"/> is false and every query returns null.
/// </summary>
public sealed class SqliteLemmaProvider : ILemmaProvider
{
    private readonly string? _connString;
    private readonly bool _hasFormsTable;
    private readonly bool _hasDecon;    // forms.deconstructor column (enclitic +iti resolution, #247 Phase 2)
    private readonly bool _hasReport;   // report-grade columns (root_key on lemma + a root table)

    public bool IsAvailable { get; }
    public DpdLemmaMeta? Meta { get; }

    public SqliteLemmaProvider(string? assetPath)
    {
        if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath))
            return; // IsAvailable stays false

        var connString = new SqliteConnectionStringBuilder
        {
            DataSource = assetPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();

        try
        {
            using var c = new SqliteConnection(connString);
            c.Open();
            // Minimal sanity check: the core tables must exist.
            if (!TableExists(c, "lemma") || !TableExists(c, "form_lemma"))
                return;
            _hasFormsTable = TableExists(c, "forms");
            _hasDecon = _hasFormsTable && ColumnExists(c, "forms", "deconstructor");
            _hasReport = TableExists(c, "root") && ColumnExists(c, "lemma", "root_key");
            Meta = LoadMeta(c);
            _connString = connString;
            IsAvailable = true;
        }
        catch
        {
            // corrupt / unreadable → stays unavailable
        }
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connString);
        c.Open();
        return c;
    }

    public FormResolution? ResolveForm(string form)
    {
        if (!IsAvailable || string.IsNullOrEmpty(form)) return null;
        using var c = Open();

        var candidates = new List<LemmaCandidate>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText =
                @"SELECT fl.lemma_id, l.lemma, l.pos, l.gloss, l.derived_from
                  FROM form_lemma fl JOIN lemma l ON l.id = fl.lemma_id
                  WHERE fl.form = $f ORDER BY fl.lemma_id";
            cmd.Parameters.AddWithValue("$f", form);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                candidates.Add(new LemmaCandidate(r.GetInt64(0), r.GetString(1), Str(r, 2), Str(r, 3), Str(r, 4)));
        }
        if (candidates.Count == 0) return null;

        string? grammar = null, deconstructor = null;
        if (_hasFormsTable)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = _hasDecon
                ? "SELECT grammar, deconstructor FROM forms WHERE form = $f"
                : "SELECT grammar FROM forms WHERE form = $f";
            cmd.Parameters.AddWithValue("$f", form);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                grammar = Str(r, 0);
                if (_hasDecon) deconstructor = Str(r, 1);
            }
        }
        return new FormResolution(form, candidates, grammar, deconstructor);
    }

    public LemmaExpansion? ExpandLemma(long lemmaId, bool includeFamily = false)
    {
        if (!IsAvailable) return null;
        using var c = Open();

        var head = ReadLemma(c, lemmaId);
        if (head is null) return null;

        var forms = new List<string>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT form FROM form_lemma WHERE lemma_id = $id ORDER BY form";
            cmd.Parameters.AddWithValue("$id", lemmaId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) forms.Add(r.GetString(0));
        }

        List<LemmaCandidate>? family = null;
        if (includeFamily)
        {
            family = new List<LemmaCandidate>();
            // The whole word-family (cluster), keyed on the family BASE — not just the focus's own
            // children. The base is the focus's derived_from when the focus is itself a derived member,
            // else the focus's own (homonym-stripped) headword. This makes the family SYMMETRIC: building
            // it from the base verb or from any derived member (participle, absolutive, deverbal noun)
            // yields the same cluster. So a form the finite verb shares with its own participle is
            // correctly in-family (not a homograph), regardless of which member the report focuses on.
            // The family is the DERIVATIONAL cluster around the focus. `id = $id` always keeps the focus,
            // and `derived_from = $self` (self = the focus's own headword) pulls in the focus's own children
            // (e.g. from a deverbal noun, down to its declensional derivations).
            //
            // When the focus is ITSELF derived, we also climb to its parent cluster, keyed on the focus's
            // derived_from ($base): `derived_from = $base` gets the co-derived siblings, and the parent
            // headword is `lemma = $base` OR — because DPD stores homonymous headwords numbered ('paññā 1')
            // while derived_from is unnumbered ('paññā') — `lemma GLOB 'base [0-9]*'`. This homonym GLOB is
            // used ONLY for the parent lookup: derived_from cannot say which numbered homonym is the parent,
            // so we (coarsely) include them all. It is deliberately NOT applied to a base lemma's own name,
            // so genuinely different words that merely share a spelling ('dhamma 1' / 'dhamma 2', both with
            // derived_from NULL) stay distinct and their shared forms remain true homographs.
            string? df = string.IsNullOrEmpty(head.DerivedFrom) ? null : head.DerivedFrom;
            var self = StripHomonym(head.Lemma);
            using var cmd = c.CreateCommand();
            cmd.Parameters.AddWithValue("$id", lemmaId);
            cmd.Parameters.AddWithValue("$self", self);
            if (df is null)
            {
                cmd.CommandText =
                    @"SELECT id, lemma, pos, gloss, derived_from FROM lemma
                      WHERE id = $id OR derived_from = $self ORDER BY id";
            }
            else
            {
                var parentBase = StripHomonym(df);
                cmd.CommandText =
                    @"SELECT id, lemma, pos, gloss, derived_from FROM lemma
                      WHERE id = $id OR derived_from = $self
                         OR derived_from = $base OR lemma = $base OR lemma GLOB $baseGlob
                      ORDER BY id";
                cmd.Parameters.AddWithValue("$base", parentBase);
                cmd.Parameters.AddWithValue("$baseGlob", parentBase + " [0-9]*");
            }
            using var r = cmd.ExecuteReader();
            while (r.Read())
                family.Add(new LemmaCandidate(r.GetInt64(0), r.GetString(1), Str(r, 2), Str(r, 3), Str(r, 4)));
        }

        return new LemmaExpansion(head.LemmaId, head.Lemma, head.Pos, head.Gloss, head.DerivedFrom, forms, family);
    }

    public LemmaCandidate? GetLemma(long lemmaId)
    {
        if (!IsAvailable) return null;
        using var c = Open();
        return ReadLemma(c, lemmaId);
    }

    public LemmaDetail? GetDetail(long lemmaId)
    {
        if (!IsAvailable) return null;
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = _hasReport
            ? @"SELECT id,lemma,pos,gloss,derived_from,meaning_lit,construction,sanskrit,pattern,ebt_count,
                  example_source,example_sutta,example,synonym,antonym,root_key FROM lemma WHERE id=$id"
            : "SELECT id,lemma,pos,gloss,derived_from FROM lemma WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", lemmaId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        long id = r.GetInt64(0);
        string lemma = r.GetString(1);
        string? pos = Str(r, 2), gloss = Str(r, 3), df = Str(r, 4);
        if (!_hasReport)
            return new LemmaDetail(id, lemma, pos, gloss, df, null, null, null, null, null, null, null, null, null, null, null);

        long? ebt = r.IsDBNull(9) ? null : r.GetInt64(9);
        string? rootKey = Str(r, 15);
        RootDetail? root = rootKey is null ? null : ReadRoot(c, rootKey);
        return new LemmaDetail(id, lemma, pos, gloss, df,
            Str(r, 5), Str(r, 6), Str(r, 7), Str(r, 8), ebt,
            Str(r, 10), Str(r, 11), Str(r, 12), Str(r, 13), Str(r, 14), root);
    }

    private static RootDetail? ReadRoot(SqliteConnection c, string rootKey)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT root_key,root_meaning,root_group,sanskrit_root,sanskrit_root_meaning,
            dhatupatha_pali,dhatupatha_english FROM root WHERE root_key=$rk";
        cmd.Parameters.AddWithValue("$rk", rootKey);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        long? grp = r.IsDBNull(2) ? null : r.GetInt64(2);
        return new RootDetail(r.GetString(0), Str(r, 1), grp, Str(r, 3), Str(r, 4), Str(r, 5), Str(r, 6));
    }

    private static LemmaCandidate? ReadLemma(SqliteConnection c, long id)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, lemma, pos, gloss, derived_from FROM lemma WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new LemmaCandidate(r.GetInt64(0), r.GetString(1), Str(r, 2), Str(r, 3), Str(r, 4)) : null;
    }

    private static DpdLemmaMeta LoadMeta(SqliteConnection c)
    {
        var m = new Dictionary<string, string>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT key, value FROM meta";
            using var r = cmd.ExecuteReader();
            while (r.Read()) m[r.GetString(0)] = r.GetString(1);
        }
        string G(string k) => m.TryGetValue(k, out var v) ? v : string.Empty;
        return new DpdLemmaMeta(G("scope"), G("dpd_version"), G("converter_version"),
                                G("schema_version"), G("license"), G("attribution"));
    }

    private static bool TableExists(SqliteConnection c, string name)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name = $n LIMIT 1";
        cmd.Parameters.AddWithValue("$n", name);
        return cmd.ExecuteScalar() is not null;
    }

    // table is a fixed literal (no injection); pragma_table_info can't be parameterized on the table name.
    private static bool ColumnExists(SqliteConnection c, string table, string column)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT 1 FROM pragma_table_info('{table}') WHERE name = $n LIMIT 1";
        cmd.Parameters.AddWithValue("$n", column);
        return cmd.ExecuteScalar() is not null;
    }

    private static string? Str(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    /// <summary>"paññāya 1" → "paññāya"; DPD's dotted sub-numbering "dhamma 1.01" → "dhamma";
    /// "pajānāti" → "pajānāti". A trailing token of only digits and dots is a homonym marker.</summary>
    internal static string StripHomonym(string lemma)
    {
        int sp = lemma.LastIndexOf(' ');
        if (sp <= 0 || sp + 1 >= lemma.Length) return lemma;
        for (int i = sp + 1; i < lemma.Length; i++)
            if (!char.IsDigit(lemma[i]) && lemma[i] != '.') return lemma;
        return lemma[..sp];
    }

    public void Dispose()
    {
        if (_connString != null)
            SqliteConnection.ClearPool(new SqliteConnection(_connString));
    }
}
