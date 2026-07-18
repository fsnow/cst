using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;

// =====================================================================================================
// dpd-cst-subset generator (the derived DPD subset for CST — form->lemma + report + optional sandhi)
// Builds the trimmed, CORPUS-AGNOSTIC asset (dpd-cst-subset.db) from a full DPD dpd.db release.
//
//   Inputs : a full DPD `dpd.db` (from digitalpalidictionary/dpd-db releases; has the `lookup` table).
//   Output : `dpd-cst-subset.db` — form->lemma resolution + lemma metadata, stripped of everything else.
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
// Usage: DpdLemmaBuilder [<dpd.db path> [<output dpd-cst-subset.db path>]] [--scope lean|mid|full]
// =====================================================================================================

const string ConverterVersion = "3";   // v3: gloss coalesces meaning_2 (blank-compound fix, #109)
const string SchemaVersion = "2";

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
string outPath = positional.Count > 1 ? positional[1] : "/Users/fsnow/dpd-poc/dpd-cst-subset.db";

bool includeForms = scope != "lean";      // the forms (grammar) table
bool includeDecon = scope == "full";      // the FULL sandhi deconstructor (every decomposable form)
bool includeReport = scope != "lean";     // report-grade per-lemma columns (etymology/example/…) + root table
bool deconColumn = includeReport;         // the forms.deconstructor column exists for mid + full

// Enclitic particles that attach to a fully-inflected base word. A form whose deconstructor is entirely
// "base + <enclitic>" (2-part) is a RESOLVABLE enclitic (e.g. pajānātīti = pajānāti + iti) — mid keeps its
// deconstructor (so the report resolves "base grammar, + iti") while dropping the multi-word sandhi bulk. (#247 Phase 2)
var enclitics = new HashSet<string>(StringComparer.Ordinal)
    { "iti", "ti", "ca", "pi", "api", "eva", "ceva", "va", "hi", "kho", "su", "nu", "no", "ve" };
bool IsResolvableEnclitic(string deconJson)
{
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(deconJson);
        if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            return false;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (el.ValueKind != System.Text.Json.JsonValueKind.String) return false;
            var parts = el.GetString()!.Split(" + ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !enclitics.Contains(parts[1])) return false;
        }
        return true;
    }
    catch { return false; }
}

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
Exec(outDb, includeReport
    ? @"CREATE TABLE lemma (
        id INTEGER PRIMARY KEY, lemma TEXT NOT NULL, pos TEXT, gloss TEXT, derived_from TEXT,
        root_key TEXT, construction TEXT, sanskrit TEXT, meaning_lit TEXT, pattern TEXT, ebt_count INTEGER,
        example_source TEXT, example_sutta TEXT, example TEXT, synonym TEXT, antonym TEXT );"
    : @"CREATE TABLE lemma (
        id INTEGER PRIMARY KEY, lemma TEXT NOT NULL, pos TEXT, gloss TEXT, derived_from TEXT );");
Exec(outDb, @"
    CREATE TABLE form_lemma ( form TEXT NOT NULL, lemma_id INTEGER NOT NULL );
    CREATE TABLE meta ( key TEXT PRIMARY KEY, value TEXT );");
if (includeReport)
    Exec(outDb, @"CREATE TABLE root (
        root_key TEXT PRIMARY KEY, root_sign TEXT, root_meaning TEXT, root_group INTEGER,
        sanskrit_root TEXT, sanskrit_root_meaning TEXT, dhatupatha_pali TEXT, dhatupatha_english TEXT );");
if (includeForms)
    Exec(outDb, deconColumn
        ? "CREATE TABLE forms ( form TEXT PRIMARY KEY, grammar TEXT, deconstructor TEXT );"
        : "CREATE TABLE forms ( form TEXT PRIMARY KEY, grammar TEXT );");

// ---- 1. lemma layer ----
long lemmaCount = 0;
using (var tx = outDb.BeginTransaction())
{
    using var ins = outDb.CreateCommand();
    using var read = src.CreateCommand();
    if (includeReport)
    {
        ins.CommandText = @"INSERT INTO lemma
            (id,lemma,pos,gloss,derived_from,root_key,construction,sanskrit,meaning_lit,pattern,ebt_count,
             example_source,example_sutta,example,synonym,antonym)
            VALUES ($id,$lemma,$pos,$gloss,$df,$rk,$con,$skt,$lit,$pat,$ebt,$exs,$exu,$ex,$syn,$ant)";
        // gloss = COALESCE(meaning_1, meaning_2): ~30% of DPD headwords (26,782/89,050, mostly compounds) have
        // an EMPTY meaning_1 but a populated meaning_2, so a gloss keyed on meaning_1 alone would blank them —
        // which surfaces as blank dictionary definitions (#109). meaning_2 is the fallback, ~0.7 MB total. (#109)
        read.CommandText = @"SELECT id,lemma_1,pos,COALESCE(NULLIF(meaning_1,''),meaning_2),derived_from,root_key,construction,sanskrit,
            meaning_lit,pattern,ebt_count,source_1,sutta_1,example_1,synonym,antonym FROM dpd_headwords";
    }
    else
    {
        ins.CommandText = "INSERT INTO lemma(id,lemma,pos,gloss,derived_from) VALUES($id,$lemma,$pos,$gloss,$df)";
        read.CommandText = "SELECT id, lemma_1, pos, COALESCE(NULLIF(meaning_1,''),meaning_2), derived_from FROM dpd_headwords";
    }
    var pId = ins.Parameters.Add("$id", SqliteType.Integer);
    var pLemma = ins.Parameters.Add("$lemma", SqliteType.Text);
    var pPos = ins.Parameters.Add("$pos", SqliteType.Text);
    var pGloss = ins.Parameters.Add("$gloss", SqliteType.Text);
    var pDf = ins.Parameters.Add("$df", SqliteType.Text);
    SqliteParameter pRk = null!, pCon = null!, pSkt = null!, pLit = null!, pPat = null!, pEbt = null!,
                    pExs = null!, pExu = null!, pEx = null!, pSyn = null!, pAnt = null!;
    if (includeReport)
    {
        pRk = ins.Parameters.Add("$rk", SqliteType.Text);
        pCon = ins.Parameters.Add("$con", SqliteType.Text);
        pSkt = ins.Parameters.Add("$skt", SqliteType.Text);
        pLit = ins.Parameters.Add("$lit", SqliteType.Text);
        pPat = ins.Parameters.Add("$pat", SqliteType.Text);
        pEbt = ins.Parameters.Add("$ebt", SqliteType.Integer);
        pExs = ins.Parameters.Add("$exs", SqliteType.Text);
        pExu = ins.Parameters.Add("$exu", SqliteType.Text);
        pEx = ins.Parameters.Add("$ex", SqliteType.Text);
        pSyn = ins.Parameters.Add("$syn", SqliteType.Text);
        pAnt = ins.Parameters.Add("$ant", SqliteType.Text);
    }

    using var r = read.ExecuteReader();
    while (r.Read())
    {
        pId.Value = r.GetInt64(0);
        pLemma.Value = r.GetString(1);
        pPos.Value = NullIfEmpty(r.GetString(2));
        // gloss is now a COALESCE(NULLIF(meaning_1,''),meaning_2) expression — NULL-capable if BOTH are empty
        // (never happens in DPD, meaning_2 is NOT NULL, but guard so GetString can't throw on a future release).
        pGloss.Value = r.IsDBNull(3) ? DBNull.Value : NullIfEmpty(r.GetString(3));
        pDf.Value = NullIfEmpty(r.GetString(4));
        if (includeReport)
        {
            pRk.Value = NullIfEmpty(r.GetString(5));
            pCon.Value = NullIfEmpty(r.GetString(6));
            pSkt.Value = NullIfEmpty(r.GetString(7));
            pLit.Value = NullIfEmpty(r.GetString(8));
            pPat.Value = NullIfEmpty(r.GetString(9));
            pEbt.Value = r.IsDBNull(10) ? DBNull.Value : r.GetValue(10);
            pExs.Value = NullIfEmpty(r.GetString(11));
            pExu.Value = NullIfEmpty(r.GetString(12));
            pEx.Value = NullIfEmpty(r.GetString(13));
            pSyn.Value = NullIfEmpty(r.GetString(14));
            pAnt.Value = NullIfEmpty(r.GetString(15));
        }
        ins.ExecuteNonQuery();
        lemmaCount++;
    }
    tx.Commit();
}
Console.WriteLine($"  lemma rows: {lemmaCount:N0}  ({sw.Elapsed.TotalSeconds:F1}s)");

// ---- 1b. root layer (report scope) ----
if (includeReport)
{
    using var tx = outDb.BeginTransaction();
    using var ins = outDb.CreateCommand();
    ins.CommandText = @"INSERT OR IGNORE INTO root
        (root_key,root_sign,root_meaning,root_group,sanskrit_root,sanskrit_root_meaning,dhatupatha_pali,dhatupatha_english)
        VALUES ($rk,$rs,$rm,$rg,$sr,$srm,$dp,$de)";
    var a = ins.Parameters.Add("$rk", SqliteType.Text);
    var b = ins.Parameters.Add("$rs", SqliteType.Text);
    var c2 = ins.Parameters.Add("$rm", SqliteType.Text);
    var d = ins.Parameters.Add("$rg", SqliteType.Integer);
    var e = ins.Parameters.Add("$sr", SqliteType.Text);
    var f = ins.Parameters.Add("$srm", SqliteType.Text);
    var g = ins.Parameters.Add("$dp", SqliteType.Text);
    var h = ins.Parameters.Add("$de", SqliteType.Text);
    using var read = src.CreateCommand();
    read.CommandText = @"SELECT root,root_sign,root_meaning,root_group,sanskrit_root,sanskrit_root_meaning,
        dhatupatha_pali,dhatupatha_english FROM dpd_roots";
    using var r = read.ExecuteReader();
    long rootCount = 0;
    while (r.Read())
    {
        a.Value = r.GetString(0);
        b.Value = NullIfEmpty(r.GetString(1));
        c2.Value = NullIfEmpty(r.GetString(2));
        d.Value = r.IsDBNull(3) ? DBNull.Value : r.GetValue(3);
        e.Value = NullIfEmpty(r.GetString(4));
        f.Value = NullIfEmpty(r.GetString(5));
        g.Value = NullIfEmpty(r.GetString(6));
        h.Value = NullIfEmpty(r.GetString(7));
        ins.ExecuteNonQuery();
        rootCount++;
    }
    tx.Commit();
    Console.WriteLine($"  root rows: {rootCount:N0}  ({sw.Elapsed.TotalSeconds:F1}s)");
}

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
        insForm.CommandText = deconColumn
            ? "INSERT OR IGNORE INTO forms(form,grammar,deconstructor) VALUES($f,$g,$d)"
            : "INSERT OR IGNORE INTO forms(form,grammar) VALUES($f,$g)";
        fF = insForm.Parameters.Add("$f", SqliteType.Text);
        fG = insForm.Parameters.Add("$g", SqliteType.Text);
        if (deconColumn) fD = insForm.Parameters.Add("$d", SqliteType.Text);
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
            bool grammarPresent = !string.IsNullOrWhiteSpace(grammar);
            // A resolvable enclitic (ids>0, no direct grammar, decon entirely "base + <enclitic>") is kept so
            // the report can render "base grammar, + iti". FULL keeps every decon; MID keeps only enclitics.
            bool enclitic = !grammarPresent && hasDecon && ids.Length > 0 && IsResolvableEnclitic(decon);
            bool wantForm = includeDecon || grammarPresent || enclitic;
            if (wantForm)
            {
                fF.Value = form;
                fG.Value = NullIfEmpty(grammar);
                if (deconColumn)
                    fD.Value = includeDecon ? (hasDecon ? decon : (object)DBNull.Value)
                                            : (enclitic ? decon : (object)DBNull.Value);
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
Meta("asset", "dpd-cst-subset");
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
{
    Check("per-form grammar present (pajānāti)", Scalar("SELECT COUNT(*) FROM forms WHERE form='pajānāti' AND grammar IS NOT NULL") == 1);
    // enclitic decon kept (pajānātīti = pajānāti + iti), but NOT a non-enclitic sandhi decon (sammappajānāti).
    Check("enclitic decon retained (pajānātīti)", Scalar("SELECT COUNT(*) FROM forms WHERE form='pajānātīti' AND deconstructor IS NOT NULL") == 1);
    Check("non-enclitic decon dropped in mid (sammappajānāti)", Scalar("SELECT COUNT(*) FROM forms WHERE form='sammappajānāti' AND deconstructor IS NOT NULL") == 0);
}
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
