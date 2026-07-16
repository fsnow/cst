using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using CST.Conversion;

namespace CST.Avalonia.Services;

/// <summary>
/// Renders a (script-neutral, IPE) <see cref="LemmaReport"/> to a self-contained HTML document in a target
/// script — the "dossier" shown in a WebView. Pāli fields are converted IPE → <paramref name="script"/>;
/// English fields (glosses, pos labels, section chrome) are verbatim. pos abbreviations are expanded.
/// </summary>
public static class LemmaReportRenderer
{
    public static string Render(LemmaReport r, Script script)
    {
        // Pāli IPE -> target script, HTML-escaped for text nodes.
        string P(string? ipe) => string.IsNullOrEmpty(ipe) ? "" : Esc(ScriptConverter.Convert(ipe!, Script.Ipe, script));
        // Pāli carrying trusted DPD markup (the example's <b>…</b>): convert + HTML-escape ONLY the text runs
        // to the target script; re-emit the <b>/</b> tags literally (converting a tag would mangle it to
        // e.g. <ब्> in Devanagari; escaping the text runs also closes the sole raw-output path).
        string PMarkup(string? ipe)
        {
            if (string.IsNullOrEmpty(ipe)) return "";
            var o = new StringBuilder(ipe!.Length);
            foreach (var part in System.Text.RegularExpressions.Regex.Split(ipe!, "(</?b>)"))
            {
                if (part is "<b>" or "</b>") o.Append(part);
                else if (part.Length > 0) o.Append(Esc(ScriptConverter.Convert(part, Script.Ipe, script)));
            }
            return o.ToString();
        }

        var sb = new StringBuilder(8192);
        sb.Append("<style>").Append(Css).Append("</style>");
        sb.Append("<div class=\"wrap\">");

        // ---- header ----
        sb.Append("<header><div>")
          .Append($"<div class=\"lemma pali\">{P(r.LemmaPali)}<small>{r.LemmaId}</small>")
          .Append($"<span class=\"pos-badge\">{Esc(PosFull(r.Pos))}</span></div>");
        if (!string.IsNullOrEmpty(r.Gloss)) sb.Append($"<div class=\"gloss\">{Esc(r.Gloss!)}</div>");
        sb.Append("</div>");
        sb.Append("<div class=\"script-ctl\"><label>Script</label>")
          .Append("<select onchange=\"location.search='?script='+encodeURIComponent(this.value)\">");
        foreach (var s in Scripts)
            sb.Append($"<option{(s == script.ToString() ? " selected" : "")}>{s}</option>");
        sb.Append("</select></div></header>");

        // ---- etymology band ----
        if (r.Root is { } root || !string.IsNullOrEmpty(r.ConstructionPali) || !string.IsNullOrEmpty(r.Sanskrit)
            || !string.IsNullOrEmpty(r.Pattern))
        {
            sb.Append("<div class=\"etym\">");
            if (r.Root?.RootPali is { } rp) sb.Append($"<span class=\"root pali\">{P(rp)}</span>");
            if (r.Root?.RootMeaning is { } rm)
            {
                string dp = r.Root.DhatupathaPali is { } d ? $" — <span class=\"pali\">{P(d)}</span>" : "";
                string de = r.Root.DhatupathaEnglish is { } e ? $" “{Esc(e)}”" : "";
                string grp = r.Root.RootGroup is { } g ? $" (class {g})" : "";
                sb.Append($"<div class=\"kv\"><span class=\"k\">Root sense</span><span class=\"v\">{Esc(rm)}{dp}{de}{grp}</span></div>");
            }
            if (r.ConstructionPali is { } con) sb.Append($"<div class=\"kv\"><span class=\"k\">Construction</span><span class=\"v pali\">{P(con)}</span></div>");
            if (r.Sanskrit is { } skt) sb.Append($"<div class=\"kv\"><span class=\"k\">Sanskrit</span><span class=\"v pali\">{Esc(skt)}</span></div>");
            if (r.Pattern is { } pat) sb.Append($"<div class=\"kv\"><span class=\"k\">Pattern</span><span class=\"v\">{Esc(pat)}</span></div>");
            sb.Append("</div>");
        }

        sb.Append("<div class=\"grid\">");

        // ---- paradigm ----
        sb.Append("<section class=\"card\"><div class=\"card-h\"><h2>In this corpus — attested paradigm</h2>")
          .Append($"<div class=\"meta\"><b>{r.TotalOccurrences:N0}</b> occurrences · <b>{r.AttestedFormCount}</b>/{r.CandidateFormCount} forms</div></div>");
        sb.Append("<div class=\"tbl-scroll\"><table><thead><tr><th>Form</th><th>Analysis</th><th></th><th style=\"text-align:right\">Count</th><th style=\"text-align:right\">Books</th></tr></thead><tbody>");
        int max = r.Forms.Count > 0 ? r.Forms.Max(f => f.Count) : 1;
        foreach (var f in r.Forms.OrderByDescending(f => f.Count))
        {
            string homo = f.Homograph ? " <span class=\"tag\">homograph</span>" : "";
            sb.Append($"<tr><td class=\"form pali\">{P(f.FormPali)}{homo}</td>")
              .Append($"<td class=\"gram\">{Esc(f.Grammar ?? "")}</td>")
              .Append($"<td class=\"barcell\"><div class=\"bar\"><i style=\"width:{(max > 0 ? f.Count * 100.0 / max : 0):F1}%\"></i></div></td>")
              .Append($"<td class=\"c\">{f.Count:N0}</td><td class=\"books\">{f.BookCount}</td></tr>");
        }
        sb.Append("</tbody></table></div>");
        int synthetic = r.CandidateFormCount - r.AttestedFormCount;
        sb.Append("<div class=\"stat-row\">")
          .Append($"<span class=\"stat\"><b>{r.AttestedFormCount}</b> attested</span>")
          .Append($"<span class=\"stat syn\"><b>{synthetic}</b> synthetic · omitted</span>")
          .Append($"<span class=\"stat\"><b>{r.CandidateFormCount}</b> DPD candidates</span></div>");
        sb.Append($"<div class=\"foot-note\">Forms of this lemma (<b>{Esc(PosFull(r.Pos))}</b>) only — its participle, absolutive &amp; deverbal-noun forms are separate lemmas → see <b>Word family</b>.</div>");
        sb.Append("</section>");

        // ---- word family ----
        sb.Append("<section class=\"card\"><div class=\"card-h\"><h2>Word family</h2></div><div class=\"fam\">");
        foreach (var grp in r.Family.GroupBy(m => m.Group))
        {
            sb.Append($"<div class=\"fam-group\"><h3>{Esc(grp.Key)}</h3>");
            foreach (var m in grp)
                sb.Append($"<div class=\"fam-row\"><span class=\"fam-lemma pali\">{P(m.LemmaPali)}</span>")
                  .Append($"<span class=\"fam-gloss\">{Esc(m.Gloss ?? "")} ({Esc(PosFull(m.Pos))})</span>")
                  .Append($"<span class=\"fam-count\">{m.TotalOccurrences:N0}</span></div>");
            sb.Append("</div>");
        }
        sb.Append("</div>");
        if (r.FamilyTotalOccurrences > 0)
            sb.Append($"<div class=\"foot-note\"><b>Whole family</b> (de-duplicated union): <b class=\"num\">{r.FamilyTotalOccurrences:N0}</b> across {r.FamilyFormCount} forms — broader than the conjugation.</div>");
        sb.Append("</section></div>");

        // ---- lower row: example + homograph ----
        sb.Append("<div class=\"lower\">");
        if (!string.IsNullOrEmpty(r.ExamplePali))
        {
            sb.Append("<section class=\"card\" style=\"padding:18px 20px 20px\"><div class=\"card-h\" style=\"padding:0 0 12px\"><h2>Attested in the canon</h2><div class=\"meta\">DPD-cited</div></div>")
              .Append($"<blockquote class=\"pali\">{PMarkup(r.ExamplePali)}</blockquote>");
            string cite = string.Join(" · ", new[] { r.ExampleSource, r.ExampleSutta }.Where(x => !string.IsNullOrEmpty(x)).Select(Esc!));
            if (cite.Length > 0) sb.Append($"<div class=\"ex-cite\">{cite}</div>");
            sb.Append("</section>");
        }
        if (r.Homographs.Count > 0)
        {
            int n = r.Homographs.Count;
            sb.Append($"<section class=\"card homo\"><div class=\"card-h\" style=\"padding:0 0 10px\"><h2>Homographs — {n} form{(n == 1 ? "" : "s")} shared with other words</h2></div>")
              .Append("<p>These paradigm forms are spelled identically to other lemmas; the index tallies surface strings, so a form’s count can’t be split between them.</p>");
            foreach (var h in r.Homographs)
            {
                sb.Append($"<div class=\"homo-item\"><div class=\"homo-head\"><span class=\"hform pali\">{P(h.FormPali)}</span><span class=\"homo-count\">{h.Count:N0}</span></div><table><tbody>");
                foreach (var s in h.Senses)
                    sb.Append($"<tr><td class=\"pali\">{P(s.LemmaPali)} <span class=\"tag\">{Esc(PosFull(s.Pos))}</span></td><td>{Esc(s.Gloss ?? "")}</td></tr>");
                sb.Append("</tbody></table></div>");
            }
            sb.Append("<div class=\"warn\"><span>⚠</span><span>Their counts are included in the paradigm above but overlap these other lemmas — a surface-string index can’t separate them.</span></div></section>");
        }
        sb.Append("</div>");

        // ---- footer ----
        sb.Append($"<footer><span>{Esc(r.Attribution)}</span><span class=\"num\">dpd-lemma · {Esc(r.DpdVersion)}</span></footer>");
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string Esc(string s) => WebUtility.HtmlEncode(s);

    private static readonly string[] Scripts = { "Devanagari", "Latin", "Sinhala", "Thai", "Myanmar" };

    private static readonly Dictionary<string, string> PosLabels = new()
    {
        ["pr"] = "present", ["aor"] = "aorist", ["fut"] = "future", ["opt"] = "optative", ["imp"] = "imperative",
        ["cond"] = "conditional", ["prp"] = "present participle", ["app"] = "active participle", ["abs"] = "absolutive",
        ["ger"] = "gerund", ["inf"] = "infinitive", ["pp"] = "past participle", ["ptp"] = "gerundive",
        ["fpp"] = "future passive participle", ["caus"] = "causative", ["pass"] = "passive", ["denom"] = "denominative",
        ["masc"] = "masculine", ["fem"] = "feminine", ["nt"] = "neuter", ["adj"] = "adjective", ["adv"] = "adverb",
        ["ind"] = "indeclinable", ["pron"] = "pronoun", ["card"] = "cardinal", ["ordin"] = "ordinal",
        ["prefix"] = "prefix", ["suffix"] = "suffix", ["idiom"] = "idiom",
    };
    private static string PosFull(string? pos) => pos is null ? "" : (PosLabels.TryGetValue(pos, out var v) ? v : pos);

    private const string Css = @"
:root{--ground:#FBF9F4;--surface:#FFFFFF;--surface-2:#F5F2EA;--ink:#242019;--muted:#6A6357;--faint:#948C7C;--hair:#E5DFD2;--jade:#1F6F5C;--jade-soft:rgba(31,111,92,.10);--jade-line:rgba(31,111,92,.28);--amber:#A6712A;--amber-soft:rgba(166,113,42,.10);--pos-noun:#8A5A9E;--pali:'Iowan Old Style','Palatino Linotype','Book Antiqua',Palatino,Georgia,serif;--ui:system-ui,-apple-system,'Segoe UI',Roboto,sans-serif;--mono:'SF Mono','JetBrains Mono',ui-monospace,Menlo,monospace;}
@media(prefers-color-scheme:dark){:root{--ground:#171410;--surface:#211D16;--surface-2:#2A2519;--ink:#EEE8DC;--muted:#A79E8C;--faint:#7C7362;--hair:#352F22;--jade:#5FBBA1;--jade-soft:rgba(95,187,161,.12);--jade-line:rgba(95,187,161,.30);--amber:#D6A24E;--amber-soft:rgba(214,162,78,.12);--pos-noun:#C79BDA;}}
*{box-sizing:border-box}body{margin:0;background:var(--ground);color:var(--ink);font-family:var(--ui);line-height:1.5}
.pali{font-family:var(--pali)}.wrap{max-width:1140px;margin:0 auto;padding:24px 28px 40px}.num{font-family:var(--mono);font-variant-numeric:tabular-nums}
header{display:flex;align-items:flex-end;justify-content:space-between;gap:24px;flex-wrap:wrap;padding-bottom:16px;border-bottom:1px solid var(--hair)}
.lemma{font-family:var(--pali);font-size:2.8rem;line-height:1;font-weight:600}.lemma small{font-size:1rem;color:var(--faint);font-weight:400;vertical-align:.55em;margin-left:.3em}
.gloss{margin-top:8px;font-size:1.02rem;color:var(--muted);max-width:60ch}
.pos-badge{display:inline-block;font-size:.68rem;font-weight:700;text-transform:uppercase;letter-spacing:.08em;color:var(--jade);border:1px solid currentColor;border-radius:999px;padding:2px 9px;vertical-align:.55em;margin-left:.5em}
.script-ctl{display:flex;align-items:center;gap:8px;font-size:.8rem;color:var(--muted)}.script-ctl label{text-transform:uppercase;letter-spacing:.07em;font-size:.66rem;font-weight:700;color:var(--faint)}
.script-ctl select{font-family:var(--ui);font-size:.85rem;color:var(--ink);background:var(--surface);border:1px solid var(--hair);border-radius:8px;padding:6px 10px}
.etym{margin-top:18px;background:var(--amber-soft);border:1px solid color-mix(in srgb,var(--amber) 24%,transparent);border-radius:12px;padding:14px 18px;display:flex;flex-wrap:wrap;gap:10px 26px;align-items:baseline}
.etym .root{font-family:var(--pali);font-size:1.3rem;color:var(--amber);font-weight:600}.etym .kv{display:flex;flex-direction:column;gap:1px}.etym .k{font-size:.62rem;text-transform:uppercase;letter-spacing:.08em;color:var(--faint);font-weight:700}.etym .v{font-size:.95rem}
.grid{display:grid;grid-template-columns:1.35fr 1fr;gap:20px;margin-top:22px}@media(max-width:820px){.grid{grid-template-columns:1fr}}
.card{background:var(--surface);border:1px solid var(--hair);border-radius:14px;overflow:hidden}
.card-h{display:flex;align-items:baseline;justify-content:space-between;gap:12px;padding:13px 18px 9px;border-bottom:1px solid var(--hair)}.card-h h2{margin:0;font-size:.76rem;text-transform:uppercase;letter-spacing:.1em;color:var(--muted);font-weight:700}.card-h .meta{font-size:.8rem;color:var(--faint)}.card-h .meta b{color:var(--jade);font-family:var(--mono)}
table{width:100%;border-collapse:collapse;font-size:.92rem}.tbl-scroll{overflow-x:auto}tbody tr{border-top:1px solid var(--hair)}td,th{padding:6px 18px;text-align:left}th{font-size:.64rem;text-transform:uppercase;letter-spacing:.06em;color:var(--faint);font-weight:700}
td.form{font-family:var(--pali);font-size:1rem}td.c{text-align:right;font-family:var(--mono);font-variant-numeric:tabular-nums}td.books{text-align:right;font-family:var(--mono);color:var(--muted)}
td.gram{font-size:.8rem;color:var(--muted)}
.barcell{width:22%}.bar{height:7px;border-radius:4px;background:var(--jade-soft)}.bar>i{display:block;height:7px;border-radius:4px;background:var(--jade);opacity:.85}
.tag{font-size:.58rem;font-weight:700;text-transform:uppercase;letter-spacing:.04em;padding:1px 6px;border-radius:999px;margin-left:8px;color:var(--pos-noun);background:color-mix(in srgb,var(--pos-noun) 12%,transparent)}
.foot-note{padding:9px 18px 13px;font-size:.8rem;color:var(--muted);border-top:1px solid var(--hair)}.foot-note b{color:var(--ink)}
.stat-row{display:flex;gap:16px;flex-wrap:wrap;padding:9px 18px 2px;font-size:.8rem;color:var(--muted)}.stat b{font-family:var(--mono);color:var(--ink)}.stat.syn b{color:var(--amber)}
.fam{padding:4px 6px 10px}.fam-group{padding:9px 12px 3px}.fam-group>h3{margin:0 0 5px;font-size:.64rem;text-transform:uppercase;letter-spacing:.08em;color:var(--faint);font-weight:700}
.fam-row{display:flex;align-items:baseline;gap:10px;padding:4px 6px;border-radius:8px}.fam-lemma{font-family:var(--pali);font-size:1rem;min-width:8em}.fam-gloss{flex:1;font-size:.8rem;color:var(--muted);overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.fam-count{font-family:var(--mono);font-variant-numeric:tabular-nums;font-size:.8rem;color:var(--jade)}
.lower{display:grid;grid-template-columns:1.35fr 1fr;gap:20px;margin-top:20px}@media(max-width:820px){.lower{grid-template-columns:1fr}}
blockquote{margin:0;font-family:var(--pali);font-size:1.1rem;line-height:1.6}blockquote b{color:var(--jade)}
.ex-cite{margin-top:10px;font-size:.8rem;color:var(--faint)}.homo{padding:16px 18px}.homo p{margin:0 0 10px;font-size:.85rem;color:var(--muted)}.homo .hform{font-size:1.15rem}.homo td{padding:4px 0;font-size:.85rem}.homo td:first-child{padding-right:14px}
.homo-item{padding:8px 0;border-top:1px solid var(--hair)}.homo-item:first-of-type{border-top:none}.homo-head{display:flex;justify-content:space-between;align-items:baseline}.homo-count{font-family:var(--mono);font-variant-numeric:tabular-nums;color:var(--faint);font-size:.8rem}
.warn{margin-top:10px;font-size:.78rem;color:var(--amber);display:flex;gap:7px}
footer{margin-top:28px;padding-top:14px;border-top:1px solid var(--hair);display:flex;justify-content:space-between;flex-wrap:wrap;gap:10px;font-size:.74rem;color:var(--faint)}";
}
