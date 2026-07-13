using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;

// =====================================================================================================
// DPD-lemma generator
// Builds the trimmed, CORPUS-AGNOSTIC form->lemma asset (dpd-lemma.db) from a full DPD dpd.db release.
//
//   Inputs : a full DPD `dpd.db` (from digitalpalidictionary/dpd-db releases; has the `lookup` table).
//   Output : `dpd-lemma.db` — form->lemma resolution + lemma metadata, stripped of everything else.
//
// Keeps (Layer A only): form -> lemma resolution, lemma metadata (lemma/pos/gloss/derived_from), and
// (mid/full) per-form grammar. DROPS: occurrence counts (Layer B = the app's Lucene index), roots,
// families, sutta_info, bold_definitions, inflection templates, the ~50 unused headword columns, and the
// generated multi-script inflections. Forms stored in IAST; the app converts to IPE at query time.
//
// --scope tiers (default mid):
//   lean : resolver only            (form_lemma + lemma)                         ~13 MB zst
//   mid  : + per-form grammar        (resolvable forms only; powers disambig UI)  ~20 MB zst   <-- v1
//   full : + sandhi deconstructions  (861k splits; for a future sandhi feature)   ~46 MB zst
//
// Usage: DpdLemmaBuilder [<dpd.db path> [<output dpd-lemma.db path>]] [--scope lean|mid|full]
// =====================================================================================================

const string ConverterVersion = "1";
const string SchemaVersion = "1";

// ---- args ----
var positional = new List<string>();
string scope = "mid";
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--scope" && i + 1 < args.Length) scope = args[++i];
    else if (args[i].StartsWith("--scope=")) scope = args[i]["--scope=".Length..];
    else positional.Add(args[i]);
}
scope = scope.ToLowerInvariant();
if (scope is not ("lean" or "mid" or "full"))
{
    Console.Error.WriteLine($"invalid --scope '{scope}' (expected lean|mid|full)");
    return 2;
}
string srcPath = positional.Count > 0 ? positional[0] : "/Users/fsnow/dpd-poc/dpd.db";
string outPath = positional.Count > 1 ? positional[1] : "/Users/fsnow/dpd-poc/dpd-lemma.db";

bool includeForms = scope != "lean";      // the forms (grammar) table
bool includeDecon = scope == "full";      // sandhi deconstructor column

if (!File.Exists(srcPath))
{
    Console.Error.WriteLine($"source DPD db not found: {srcPath}");
    return 2;
}

var sw = Stopwatch.StartNew();
Console.WriteLine($"DPD-lemma builder v{ConverterVersion} (schema v{SchemaVersion}, scope={scope})");
Console.WriteLine($"  source: {srcPath}");
Console.WriteLine($"  output: {outPath}");

using var src = new SqliteConnection($"Data Source={srcPath};Mode=ReadOnly");
src.Open();

// ---- provenance from db_info ----
string DbInfo(string key)
{
    using var c = src.CreateCommand();
    c.CommandText = "SELECT value FROM db_info WHERE key=$k";
    c.Parameters.AddWithValue("$k", key);
    return c.ExecuteScalar() as string ?? "";
}
string dpdVersion = DbInfo("dpd_release_version");
string dpdAuthor = DbInfo("author");
string dpdLicense = DbInfo("license");
string dpdWebsite = DbInfo("website");
Console.WriteLine($"  DPD release: {dpdVersion} ({dpdAuthor}, {dpdLicense})");

// ---- create the output db ----
if (File.Exists(outPath)) File.Delete(outPath);
using var outDb = new SqliteConnection($"Data Source={outPath}");
outDb.Open();
Exec(outDb, "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA temp_store=MEMORY;");
Exec(outDb, @"
    CREATE TABLE lemma (
        id INTEGER PRIMARY KEY, lemma TEXT NOT NULL, pos TEXT, gloss TEXT, derived_from TEXT
    );
    CREATE TABLE form_lemma ( form TEXT NOT NULL, lemma_id INTEGER NOT NULL );
    CREATE TABLE meta ( key TEXT PRIMARY KEY, value TEXT );");
if (includeForms)
    Exec(outDb, includeDecon
        ? "CREATE TABLE forms ( form TEXT PRIMARY KEY, grammar TEXT, deconstructor TEXT );"
        : "CREATE TABLE forms ( form TEXT PRIMARY KEY, grammar TEXT );");

// ---- 1. lemma layer ----
long lemmaCount = 0;
using (var tx = outDb.BeginTransaction())
{
    using var ins = outDb.CreateCommand();
    ins.CommandText = "INSERT INTO lemma(id,lemma,pos,gloss,derived_from) VALUES($id,$lemma,$pos,$gloss,$df)";
    var pId = ins.Parameters.Add("$id", SqliteType.Integer);
    var pLemma = ins.Parameters.Add("$lemma", SqliteType.Text);
    var pPos = ins.Parameters.Add("$pos", SqliteType.Text);
    var pGloss = ins.Parameters.Add("$gloss", SqliteType.Text);
    var pDf = ins.Parameters.Add("$df", SqliteType.Text);

    using var read = src.CreateCommand();
    read.CommandText = "SELECT id, lemma_1, pos, meaning_1, derived_from FROM dpd_headwords";
    using var r = read.ExecuteReader();
    while (r.Read())
    {
        pId.Value = r.GetInt64(0);
        pLemma.Value = r.GetString(1);
        pPos.Value = NullIfEmpty(r.GetString(2));
        pGloss.Value = NullIfEmpty(r.GetString(3));
        pDf.Value = NullIfEmpty(r.GetString(4));
        ins.ExecuteNonQuery();
        lemmaCount++;
    }
    tx.Commit();
}
Console.WriteLine($"  lemma rows: {lemmaCount:N0}  ({sw.Elapsed.TotalSeconds:F1}s)");

// ---- 2. explode lookup -> form_lemma (+ forms) ----
long formCount = 0, edgeCount = 0, skipped = 0;
using (var tx = outDb.BeginTransaction())
{
    using var insEdge = outDb.CreateCommand();
    insEdge.CommandText = "INSERT INTO form_lemma(form,lemma_id) VALUES($f,$id)";
    var eF = insEdge.Parameters.Add("$f", SqliteType.Text);
    var eId = insEdge.Parameters.Add("$id", SqliteType.Integer);

    SqliteCommand? insForm = null;
    SqliteParameter fF = null!, fG = null!, fD = null!;
    if (includeForms)
    {
        insForm = outDb.CreateCommand();
        insForm.CommandText = includeDecon
            ? "INSERT OR IGNORE INTO forms(form,grammar,deconstructor) VALUES($f,$g,$d)"
            : "INSERT OR IGNORE INTO forms(form,grammar) VALUES($f,$g)";
        fF = insForm.Parameters.Add("$f", SqliteType.Text);
        fG = insForm.Parameters.Add("$g", SqliteType.Text);
        if (includeDecon) fD = insForm.Parameters.Add("$d", SqliteType.Text);
    }

    using var read = src.CreateCommand();
    read.CommandText = "SELECT lookup_key, headwords, grammar, deconstructor FROM lookup";
    using var r = read.ExecuteReader();
    while (r.Read())
    {
        string form = r.GetString(0);
        long[] ids = ParseIds(r.GetString(1));
        string grammar = r.GetString(2);
        string decon = r.GetString(3);
        bool hasDecon = !string.IsNullOrWhiteSpace(decon) && decon != "[]";

        // full keeps a form if it resolves OR decomposes; lean/mid keep only resolvable forms.
        bool keep = includeDecon ? (ids.Length > 0 || hasDecon) : ids.Length > 0;
        if (!keep) { skipped++; continue; }

        foreach (long id in ids) { eF.Value = form; eId.Value = id; insEdge.ExecuteNonQuery(); edgeCount++; }

        if (includeForms)
        {
            // mid: a forms row only when there's grammar to carry; full: whenever kept.
            bool wantForm = includeDecon || !string.IsNullOrWhiteSpace(grammar);
            if (wantForm)
            {
                fF.Value = form;
                fG.Value = NullIfEmpty(grammar);
                if (includeDecon) fD.Value = hasDecon ? decon : DBNull.Value;
                insForm!.ExecuteNonQuery();
                formCount++;
            }
        }
    }
    insForm?.Dispose();
    tx.Commit();
}
Console.WriteLine($"  form->lemma edges: {edgeCount:N0}  forms rows: {formCount:N0}  skipped: {skipped:N0}  ({sw.Elapsed.TotalSeconds:F1}s)");

// ---- 3. indexes ----
Exec(outDb, "CREATE INDEX idx_fl_form ON form_lemma(form); CREATE INDEX idx_fl_lemma ON form_lemma(lemma_id);");
Console.WriteLine($"  indexed  ({sw.Elapsed.TotalSeconds:F1}s)");

// ---- 4. meta (version axes + CC BY-NC-SA attribution) ----
void Meta(string k, string v)
{
    using var c = outDb.CreateCommand();
    c.CommandText = "INSERT OR REPLACE INTO meta(key,value) VALUES($k,$v)";
    c.Parameters.AddWithValue("$k", k);
    c.Parameters.AddWithValue("$v", v);
    c.ExecuteNonQuery();
}
Meta("asset", "dpd-lemma");
Meta("scope", scope);
Meta("dpd_version", dpdVersion);
Meta("converter_version", ConverterVersion);
Meta("schema_version", SchemaVersion);
Meta("built_at_utc", DateTime.UtcNow.ToString("O"));
Meta("source", "Digital Pāḷi Dictionary (DPD)");
Meta("author", dpdAuthor);
Meta("license", dpdLicense);
Meta("homepage", dpdWebsite);
Meta("attribution", $"Derived from the Digital Pāḷi Dictionary ({dpdVersion}) by {dpdAuthor} — {dpdLicense} — {dpdWebsite}");
Meta("lemma_count", lemmaCount.ToString());
Meta("form_count", formCount.ToString());
Meta("edge_count", edgeCount.ToString());

// ---- 5. optimize ----
Exec(outDb, "PRAGMA optimize; VACUUM;");
Console.WriteLine($"  vacuumed  ({sw.Elapsed.TotalSeconds:F1}s)");

// ---- 6. validate against known DPD facts (Task 1 ground truth) ----
Console.WriteLine("validation:");
int failures = 0;
void Check(string label, bool ok, string detail = "")
{
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {label}{(detail == "" ? "" : "  -> " + detail)}");
    if (!ok) failures++;
}
long Scalar(string sql)
{
    using var c = outDb.CreateCommand();
    c.CommandText = sql;
    var o = c.ExecuteScalar();
    return o is null or DBNull ? 0 : Convert.ToInt64(o);
}
List<long> LemmaIds(string form)
{
    var l = new List<long>();
    using var c = outDb.CreateCommand();
    c.CommandText = "SELECT lemma_id FROM form_lemma WHERE form=$f ORDER BY lemma_id";
    c.Parameters.AddWithValue("$f", form);
    using var rr = c.ExecuteReader();
    while (rr.Read()) l.Add(rr.GetInt64(0));
    return l;
}
string? DerivedFrom(long id)
{
    using var c = outDb.CreateCommand();
    c.CommandText = "SELECT derived_from FROM lemma WHERE id=$i";
    c.Parameters.AddWithValue("$i", id);
    return c.ExecuteScalar() as string;
}

Check("lemma count == 89050", lemmaCount == 89050, lemmaCount.ToString());
var paj = LemmaIds("pajānāti");
Check("back-lookup pajānāti -> [39702]", paj.Count == 1 && paj[0] == 39702, string.Join(",", paj));
var pannaya = LemmaIds("paññāya");
Check("paññāya is a homograph (>1 lemma)", pannaya.Count > 1, $"{pannaya.Count} candidates");
Check("paññāya includes the pajānāti gerund (40070)", pannaya.Contains(40070));
Check("lemma 40070 derived_from == pajānāti", DerivedFrom(40070) == "pajānāti", DerivedFrom(40070) ?? "null");
var napp = LemmaIds("nappajānāti");
Check("back-lookup nappajānāti -> [35708]", napp.Count == 1 && napp[0] == 35708, string.Join(",", napp));
Check("lemma 35708 derived_from == pajānāti", DerivedFrom(35708) == "pajānāti", DerivedFrom(35708) ?? "null");
long fwd = Scalar("SELECT COUNT(*) FROM form_lemma WHERE lemma_id=39702");
Check("forward expansion 39702 -> 34 forms", fwd == 34, fwd.ToString());
if (scope == "mid")
    Check("per-form grammar present (pajānāti)", Scalar("SELECT COUNT(*) FROM forms WHERE form='pajānāti' AND grammar IS NOT NULL") == 1);
if (scope == "full")
    Check("sandhi preserved (sammappajānāti)", Scalar("SELECT COUNT(*) FROM forms WHERE form='sammappajānāti' AND deconstructor IS NOT NULL") == 1);

var fi = new FileInfo(outPath);
Console.WriteLine($"output: {outPath}  {fi.Length / 1024.0 / 1024.0:F1} MB   built in {sw.Elapsed.TotalSeconds:F1}s");
Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;

// ---- helpers ----
static void Exec(SqliteConnection c, string sql)
{
    using var cmd = c.CreateCommand();
    cmd.CommandText = sql;
    cmd.ExecuteNonQuery();
}
static object NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? DBNull.Value : s;
static long[] ParseIds(string json)
{
    if (string.IsNullOrWhiteSpace(json)) return [];
    var t = json.AsSpan().Trim();
    if (t.Length < 2 || t[0] != '[') return [];
    try { return JsonSerializer.Deserialize<long[]>(json) ?? []; }
    catch { return []; }
}
