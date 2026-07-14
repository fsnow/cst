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

        string? grammar = null;
        if (_hasFormsTable)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT grammar FROM forms WHERE form = $f";
            cmd.Parameters.AddWithValue("$f", form);
            grammar = cmd.ExecuteScalar() as string;
        }
        return new FormResolution(form, candidates, grammar);
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
            var baseName = StripHomonym(head.Lemma);
            using var cmd = c.CreateCommand();
            cmd.CommandText =
                @"SELECT id, lemma, pos, gloss, derived_from FROM lemma
                  WHERE id = $id OR derived_from = $base ORDER BY id";
            cmd.Parameters.AddWithValue("$id", lemmaId);
            cmd.Parameters.AddWithValue("$base", baseName);
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

    /// <summary>"paññāya 1" → "paññāya"; "pajānāti" → "pajānāti" (strips a trailing homonym number).</summary>
    internal static string StripHomonym(string lemma)
    {
        int sp = lemma.LastIndexOf(' ');
        return sp > 0 && int.TryParse(lemma.AsSpan(sp + 1), out _) ? lemma[..sp] : lemma;
    }

    public void Dispose()
    {
        if (_connString != null)
            SqliteConnection.ClearPool(new SqliteConnection(_connString));
    }
}
