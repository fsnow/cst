# Third-Party Dictionary Support (DPD + Import Format) — Research Spike (#109)

*Read-only spike. Backend-only; UI/layout explicitly out of scope. This is analysis for planning, not an implementation.*

## Executive summary

- The Digital Pāḷi Dictionary (DPD) is a large, actively maintained (roughly monthly) Pāli→English dictionary. Latest release at time of writing: **`v0.4.20260531`** with **~89,050 headwords** (~47k fully complete).
- **The data is licensed CC BY-NC-SA 4.0** — Attribution + **NonCommercial** + **ShareAlike**. The **NC** clause is **not a concern**: CST Reader is non-commercial by principle, and non-commercial use is itself one of the conditions under which the VRI texts are used — so DPD's NonCommercial term is consonant with the whole project. The live considerations are lighter: **BY** (display attribution) and **SA** (the converted DPD file must itself be marked CC BY-NC-SA and kept separable from the app's own code). Delivery still favors **on-demand download** — driven by size and monthly churn, not license risk.
- DPD ships many consumer formats plus **machine-readable sources**: the full SQLite `dpd.db` (177 MB compressed), a mobile SQLite db (141 MB), a **plain-text export (`dpd-txt.zip`, ~4.2 MB)**, and — inside the git repo — a canonical **50-column TSV backup** of the headwords table. The TSV/SQLite are the right programmatic import sources.
- DPD entries are **richly structured** (lemma, POS, grammar, meaning, root, construction, examples, inflection data, etc.), far beyond the current `headword \n definition` shape. Recommendation: import DPD as a **flattened HTML fragment per headword** into a slightly generalized version of the existing model, not DPD's full relational schema.
- Proposed: a **simple, documented external-dictionary import format** (a `manifest.json` + a 2-column TSV payload) that both a DPD converter and hand-made user dictionaries can target, with IPE normalization of headwords at import time.
- Recommended backend evolution: move from per-**language** selection to per-**source** selection, keep the existing EN/HI built-ins working unchanged, and keep `IDictionaryService` / `IDictionaryTool` format-agnostic.

## 1. Current backend (verified against the code)

The dictionary backend today is a faithful, UI-free port of CST4's `FormDictionary`:

- **Model** (`Models/DictionaryWord.cs`): `{ string Word /* IPE-normalized headword, the sort/search key */, string Meaning /* HTML fragment */ }`.
- **Index** (`Services/DictionaryIndex.cs`): an immutable list sorted by **ordinal** comparison of IPE headwords (the IPE collation invariant: codepoint order == Pāli alphabetical order). `Lookup` does binary search → exact hit + `StartsWith` prefix run, or on a miss the tied-longest-common-prefix nearest-neighbor run. Pure and unit-testable.
- **Service** (`Services/DictionaryService.cs` + `IDictionaryService.cs`): reads **every file** in `dictionaries/<lang>/`, parses **alternating lines** (headword, definition), IPE-normalizes each headword via `Any2Ipe.Convert`, and **merges duplicate headwords** by joining definitions with a `MeaningSeparator` (`<hr/>`). Data is loaded lazily per language and cached. First run seeds app-support from a bundled `dictionaries/` copy (`EnsureBundledDictionaries`).
- **On disk**: `~/Library/Application Support/CSTReader/dictionaries/en/…` and `…/hi/…`; repo source-of-truth mirror at `src/CST.Avalonia/dictionaries/`. Files are UTF-8/LF.
- **Selection unit is a *language***: `AvailableLanguages` returns two-letter codes; `LookupAsync(language, query)`. Multiple source files *within* a language dir merge transparently.
- **Agent contract** (`CST.Core/Tools/DictionaryToolContracts.cs`): `IDictionaryTool` with `Languages` and `LookupAsync(DictionaryRequest{ Language, Query, OutputScript, MaxEntries })` → `DictionaryEntry{ Headword, MeaningHtml }`. Explicitly documented as format-agnostic.

Design doc of record: `docs/features/in-progress/DICTIONARIES.md`.

**Key takeaway:** the backend already supports *multiple merged files per language* and a *format-agnostic* tool contract. What it does **not** have is the concept of a *named source* with its own metadata/attribution, or per-source selection — which is exactly what #109 needs.

## 2. DPD data landscape

### 2.1 What DPD is
DPD is a feature-rich Pāḷi-English dictionary by **Bhikkhu Bodhirāsa**, running on the web (dpdict.net) and in GoldenDict/MDict/DictTango/Kindle/Kobo/etc. Data + build code live in [`digitalpalidictionary/dpd-db`](https://github.com/digitalpalidictionary/dpd-db).

### 2.2 Distribution formats (release `v0.4.20260531`, verified via GitHub API)

| Asset | Size | Notes |
|---|---|---|
| `dpd.db.tar.bz2` | **177 MB** | **Full SQLite database** (all tables) — the canonical machine-readable source |
| `dpd-mobile-db.zip` | **141 MB** | Trimmed **SQLite** db for mobile apps |
| `dpd-txt.zip` | **4.2 MB** | **Plain-text** rendered export |
| `dpd-mdict.zip` | 318 MB | MDict |
| `dpd-goldendict.zip` | 273 MB | GoldenDict |
| `dpd-slob.zip` | 302 MB | Aard2 Slob |
| `dpd-kindle.mobi` / `.epub` | 74 MB / 19 MB | Kindle |
| `dpd-kobo.zip` | 14 MB | Kobo |
| `dpd-apple.dictionary.zip` | 34 MB | Apple Dictionary.app |
| `dpd-anki.apkg` | 13 MB | Anki deck |
| `dpd-pdf.zip` | 36 MB | PDF |

Additionally, **inside the git repo** (not a release asset) there is a canonical **TSV backup** of the source data: `db/backup_tsv/dpd_headwords_part_001..003.tsv` (+ `dpd_roots_part_001.tsv`, `sutta_info.tsv`). The headwords TSV has **50 quoted, tab-separated columns**, header row first.

**Best programmatic import source:** the **full SQLite `dpd.db`** (query exactly the fields you want) or the **repo TSV backup** (no DB engine needed, versioned in git). `dpd-txt.zip` is pre-rendered prose — convenient but lossy. GoldenDict/MDict/Slob are consumer bundles, awkward to parse. **Recommendation: convert from SQLite (preferred) or TSV.**

### 2.3 Entry structure (the `DpdHeadword` table)
Verified from `db/models.py` and the TSV header. Core fields relevant to import:

- **Identity / headword**: `id`, `lemma_1` (headword *with* homonym disambiguation, e.g. `dhamma 1`), `lemma_2`.
- **Grammar**: `pos`, `grammar`, `neg`, `verb`, `trans`, `plus_case`, `derived_from`.
- **Meaning**: `meaning_1` (primary), `meaning_lit` (literal), `meaning_2` (secondary/legacy); computed `meaning_combo`, `degree_of_completion`.
- **Etymology / morphology**: `root_key`, `root_sign`, `root_base`, `family_root`, `family_word`, `family_compound`, `construction`, `derivative`, `suffix`, `phonetic`, `compound_type`, `sanskrit`, `non_ia`.
- **Attestation**: `source_1/sutta_1/example_1`, `source_2/sutta_2/example_2`.
- **Relations / notes**: `antonym`, `synonym`, `variant`, `commentary`, `notes`, `cognate`, `link`, `origin`.
- **Inflection**: `stem`, `pattern`, `inflections*` (incl. Sinhala/Devanagari/Thai variants), `inflections_html`, plus frequency data.

**Two "layers".** DPD is effectively two related datasets: (a) the **headword/word layer** (`DpdHeadword`) and (b) a **root layer** (`DpdRoot`: `root`, `root_meaning`, dhātupāṭha references, etc.). There is also a critical (c) **`Lookup` table** mapping any **inflected/looked-up form → headword id(s)** — this is how DPD resolves an inflected form the user typed to the right lemma. See §9 (a functional gap for our IPE-prefix approach).

Reported statistics (release `v0.3.20260202`): 88,350 headwords (47,535 complete / 11,472 partial / 29,343 incomplete), 754 roots, 3,350 root families, **1,477,220** inflected forms, **858,681** compound deconstructions. Latest release: **89,050 headwords**.

### 2.4 Update cadence & versioning
- **Cadence:** roughly **monthly** releases.
- **Version tags:** `v[MAJOR].[MINOR].[YYYYMMDD]` (e.g. `v0.4.20260531`). The embedded date makes "is my copy stale?" a trivial comparison; `DbInfo` inside the db carries version metadata.

## 3. Licensing & attribution

**The DPD data is licensed [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/)** (the repo build code carries its own separate open-source terms; the *content* is CC BY-NC-SA 4.0). From `docs/license.md`:

> Digital Pāḷi Dictionary is made available under a **CC BY-NC-SA 4.0** license.
> — **CC**: free to share and adapt … **BY**: attribute the source, **NC**: don't use it commercially, **SA**: share under the same conditions.

Implications for CST Reader (a free reader for VRI texts):

1. **BY (Attribution) — required.** Any shipped/derived DPD data must display attribution: creator **Bhikkhu Bodhirāsa**, title **Digital Pāḷi Dictionary**, the **CC BY-NC-SA 4.0** license (with link), the **version** used, and the homepage **https://dpdict.net**. This belongs in the import manifest and somewhere user-visible (credits — a UI concern, flagged for the maintainer). ([citation guidance](https://buddhistuniversity.net/content/reference/dpd))

2. **NC (NonCommercial) — satisfied by the project's non-commercial principle.** DPD may not be used "primarily intended for or directed toward commercial advantage." CST Reader is non-commercial by design, and non-commercial use is one of the few conditions under which the VRI texts themselves are used — so DPD's NC term aligns with the project rather than constraining it. The only standing implication is that the project remains non-commercial (no paid tier / monetization of DPD), which is already the governing principle. Not a blocker.

3. **SA (ShareAlike) — applies to *adaptations*.** A converted/flattened DPD file **is an adaptation** and must be offered under CC BY-NC-SA 4.0 (or compatible). SA attaches to the **adapted DPD data**, *not* to CST Reader's own source code: bundling a separately-licensed data file alongside independently-licensed application code is a **"mere aggregation / collection,"** which SA does not reach. So the app's own license is unaffected, **but** the shipped DPD-derived file must itself be marked CC BY-NC-SA 4.0.

4. **Bundle vs. download.** The **SA** clause travels with the DPD data (any converted copy must stay CC BY-NC-SA), so the cleanest posture is to keep that data **separable** from the app's own code: download it **on demand** into the user's asset store, tagged with a manifest carrying its license, rather than embedding it in the signed app bundle. This keeps the adapted-data license explicit per source, avoids shipping 130–180 MB per release, and makes versioning/refresh simple. (NC is satisfied by the project's non-commercial principle — item 2 — so it is not a driver here.) See §6.

> **Context:** the DPD author has a collegial relationship with the VRI Tipiṭaka Project, so any attribution or coordination question around DPD integration likely has a direct channel — a further reason the licensing side is low-risk.

## 4. Mapping DPD → CST

Two options: **(a)** flatten to the existing `headword \n definition (HTML)` shape, rendering the useful fields (meaning / literal / grammar / root / construction / trimmed example) into a small HTML fragment; or **(b)** adopt a richer structured entry model.

**Recommendation: (a) flattened HTML, for v1.** Rationale:
- Reuses the entire existing pipeline (IPE normalization, ordinal index, binary-search lookup, duplicate-merge, WebView rendering, `DictionaryEntry.MeaningHtml`) with **zero changes to the matching core**.
- The agent-facing `IDictionaryTool` is *already* defined around `MeaningHtml` and documented as format-agnostic — a structured model forces a contract change for no v1 benefit.
- DPD's own consumer exports are themselves flattened HTML; nothing is lost vs GoldenDict/MDict users.
- Homonyms (`dhamma 1`, `dhamma 2`, …) collapse to one IPE headword and merge with the existing `<hr/>` separator — which the current loader already does for free.
- A richer model is a **later** option (§8) if the UI later wants collapsible grammar/inflection sections.

Conversion notes:
- **Headword source:** use DPD's clean lemma (strip the trailing homonym number from `lemma_1`); feed to `Any2Ipe.Convert`. Verify `Any2Ipe` handles the full diacritic set (esp. `ṃ`/`ṁ` niggahita variants).
- **NFC:** normalize to NFC before IPE conversion (the service already does this for queries; do the same at import).
- **HTML safety:** sanitize/whitelist the rendered fragment; keep it a self-contained fragment as the contract expects.
- **Completeness filter:** consider importing only entries with `meaning_1` (or including `degree_of_completion`) to avoid ~29k "incomplete" stubs — a maintainer toggle.

## 5. Proposed external-dictionary import format

Design goals: **one format** that a DPD converter emits *and* a user can hand-author for a small dictionary; expressive enough to carry license/attribution/version; trivial to parse with the existing loader.

### 5.1 On-disk layout (per **source**, not per language)
```
dictionaries/
  vri-en/         (legacy built-in, keeps working; see §7 back-compat)
  vri-hi/
  dpd/
    manifest.json
    entries.tsv
  my-glossary/
    manifest.json
    entries.tsv
```

### 5.2 Manifest (`manifest.json`)
```json
{
  "formatVersion": 1,
  "id": "dpd",
  "name": "Digital Pāḷi Dictionary",
  "shortName": "DPD",
  "languages": ["en"],
  "sourceLanguage": "pi",
  "headwordScript": "latin",
  "meaningFormat": "html",
  "version": "0.4.20260531",
  "homepage": "https://dpdict.net",
  "license": "CC-BY-NC-SA-4.0",
  "licenseUrl": "https://creativecommons.org/licenses/by-nc-sa/4.0/",
  "attribution": "Digital Pāḷi Dictionary by Bhikkhu Bodhirāsa — CC BY-NC-SA 4.0 — https://dpdict.net",
  "entriesFile": "entries.tsv",
  "entryCount": 89050
}
```
- `headwordScript` tells the importer which script the headwords are in **before** IPE conversion (`latin` for DPD, `devanagari` for VRI Hindi). The importer runs `Any2Ipe.Convert` accordingly, so headwords are stored in IPE regardless of source script — preserving the collation invariant.
- `languages` allows a single source to serve more than one target language.
- `meaningFormat` = `html` (default) or `text` (importer wraps in `<p>`).

### 5.3 Entry payload (`entries.tsv`)
Two tab-separated columns, header row, one entry per line — **more robust than the legacy alternating-line format** (no off-by-one corruption, greppable, spreadsheet-editable):
```
headword	meaning_html
dhamma	<p><b>dhamma</b>, <i>masc.</i> the teaching; nature; a mental state…</p><hr/><p><b>dhamma</b> (2)…</p>
akkhi	<p><b>akkhi</b>, <i>nt.</i> the eye.</p>
```
- Headword is authored in the source script (`headwordScript`); the importer IPE-normalizes it. Multiple rows with the same headword merge as today (`<hr/>`).
- Embedded newlines within a definition are encoded (`\n`) or the cell is HTML with `<br/>`.

**Hand-authoring a tiny dictionary:** a user drops a folder with a 6-line `manifest.json` and a 2-column TSV. That is the whole contract.

**Backward compatibility:** a source directory with **no `manifest.json`** is treated as a *legacy language source* (current alternating-line `.txt` behavior, id/name from the dir name, `meaningFormat=html`, `headwordScript` inferred from language). So `en/` and `hi/` keep working untouched.

> Optional richer variant (deferred): allow `entries.jsonl` (one JSON object per line) with optional structured fields (`pos`, `grammar`, `examples[]`) for sources that want a structured model later. v1 does not require it.

## 6. Delivery

- **Built-in EN/HI:** stay **bundled** (small, VRI-owned, no NC issue), seeded to app-support on first run as today.
- **DPD:** **on-demand download**, not bundled — size (130–180 MB machine-readable; even flattened, tens of MB), the ShareAlike posture (§3 — keep the CC BY-NC-SA data separable), and monthly churn. Fetch the SQLite/TSV from `releases/latest`, convert locally to the import format, write into the user's dictionary store.
- **Where converted dictionaries live:** under app-support `dictionaries/<sourceId>/`. Treat downloaded/converted DPD data as a **preservation asset** (consistent with the project's rule that downloaded source PDFs are a preservation store, not an evictable cache) — never auto-delete.
- **Conversion location:** ideally a small offline/CI converter (Python or C#) produces a redistributable `dpd/` folder; the app can also download-and-convert at runtime. Converting **outside** the signed app keeps the CC BY-NC-SA data cleanly separable from the app's own code and avoids adding a SQLite dependency if TSV is used.
- **Versioning / refresh:** the manifest `version` mirrors DPD's `v[MAJOR].[MINOR].[YYYYMMDD]` tag; a refresh check compares stored vs `releases/latest` (GitHub API).

## 7. Backend model changes (BACKEND ONLY — UI/layout out of scope)

Move from per-**language** to per-**source** selection, keeping everything additive and back-compatible.

1. **Source metadata type** (new): `DictionarySourceInfo { string Id, string Name, IReadOnlyList<string> Languages, string HeadwordScript, string Version, string Attribution, string LicenseId, string Homepage }`, populated from `manifest.json` (or synthesized for legacy dirs).

2. **`IDictionaryService`** — add source-aware members, keep language members:
   - `IReadOnlyList<DictionarySourceInfo> AvailableSources { get; }`
   - `Task<IReadOnlyList<DictionaryWord>> LookupAsync(string sourceId, string query)` — per-source.
   - `Task<IReadOnlyList<DictionaryLookupResult>> LookupAllAsync(string query, IEnumerable<string>? sourceIds = null)` where `DictionaryLookupResult { DictionarySourceInfo Source, IReadOnlyList<DictionaryWord> Entries }` — **combined, sectioned** results with per-source attribution.
   - Keep `AvailableLanguages` / `LookupAsync(language, …)` as thin wrappers over sources grouped by language, so nothing existing breaks.

3. **Loader** (`DictionaryService`): detect `manifest.json`; if present, parse the 2-column TSV honoring `headwordScript`/`meaningFormat`; else fall back to the legacy alternating-line reader. Cache per **source**. Each `DictionaryIndex` stays as-is (IPE-ordinal, dup-merge).

4. **Agent contract** (`IDictionaryTool` / `DictionaryToolContracts.cs`): stays format-agnostic; extend additively — `Sources { get; }` (keep `Languages`); optional `SourceId`/`SourceIds[]` on `DictionaryRequest` (omitted ⇒ behave as today); optional `SourceId`/`SourceName`/`Attribution` on `DictionaryEntry` so combined results are attributable (existing 2-field construction still compiles).

5. **Combined-search rendering** (sectioned display with source headers) is a UI concern — **out of scope**; the service returns grouped, attributed results.

All additive and unit-testable with `dotnet test` (new tests: manifest parsing, per-source cache, combined lookup ordering, legacy fallback).

## 8. Phasing / proposed child issues

- **v1 — Import format + per-source backend + one importable dictionary (no DPD yet):** define & document `manifest.json` + `entries.tsv`; loader manifest detection + TSV + legacy fallback + per-source caching; additive source-aware API with back-compat wrappers; ship/convert one small importable dictionary end-to-end; tests.
- **v2 — DPD on-demand:** offline/CI converter (DPD SQLite/TSV → flattened `dpd/` folder, IPE headwords, HTML meanings, completeness filter, stamped with version + CC BY-NC-SA 4.0 attribution); in-app download + refresh check; store as preservation asset; attribution surfaced (UI: maintainer).
- **v3 — Multi-dictionary combined search:** `LookupAllAsync` sectioned/attributed results; agent-tool multi-source support.
- **v4 (optional) — Richer entry model + inflected-form resolution:** optional `entries.jsonl`; import DPD's `Lookup` table so inflected forms resolve to lemmas (see §9).

**Suggested #109 breakdown:** #109a "External dictionary import format + per-source backend (v1)"; #109b "DPD converter + on-demand download (v2)"; #109c "Combined multi-source lookup (v3)"; #109d "Structured entries + DPD inflection lookup (v4)".

## 9. Risks & open questions

1. **NC — resolved (not a blocker).** CST Reader is non-commercial by principle, and non-commercial use is one of the conditions on the VRI texts, so DPD's NonCommercial clause aligns with the project. Standing implication: keep the project non-commercial (no monetization of DPD) — already the case. The DPD author's collegial relationship with the VRI Tipiṭaka Project further de-risks the licensing/attribution side.
2. **SA on the converted file.** The DPD-derived import folder must itself be marked CC BY-NC-SA 4.0. Confirm comfort shipping/hosting a CC-BY-NC-SA data artifact (the app's own code license is unaffected — mere aggregation).
3. **Inflected-form lookup gap.** DPD's core value includes resolving *inflected forms* (1.4M) and *compound deconstructions* to lemmas via its `Lookup` table. Our IPE prefix/nearest-neighbor search only matches **lemma prefixes**, so a user typing an inflected form may miss. v1–v3 ignore this; v4 should import DPD's `Lookup` mappings. **Biggest UX gap vs. using DPD natively.**
4. **IPE conversion fidelity for DPD lemmas.** Verify `Any2Ipe.Convert` round-trips all DPD diacritics and niggahita variants; decide homonym-number handling (strip → merge, as recommended).
5. **Size/perf.** Even flattened, DPD is large (~89k headwords). Validate load time/memory of a single large `DictionaryIndex`; ordinal binary search scales fine, but startup/seed cost and merge behavior should be measured.
6. **HTML sanitization.** DPD fields contain markup, links, cross-references; ensure the rendered fragment is safe and self-contained for the WebView; decide whether/how to map DPD cross-references to the existing SeeAlso mechanism.
7. **Converter maintenance.** DPD releases monthly and its schema evolves; the converter must track `DpdHeadword` field changes. Pin to a known-good DPD version and re-verify on bump.
8. **Language coverage.** DPD is Pāli→English only today; the per-source model handles that, but don't assume future sources are English.

## 10. Sources

- DPD home / docs: https://digitalpalidictionary.github.io/
- Data + build repo: https://github.com/digitalpalidictionary/dpd-db
- Releases (formats, sizes, cadence, version tags): https://github.com/digitalpalidictionary/dpd-db/releases
- License (CC BY-NC-SA 4.0): https://raw.githubusercontent.com/digitalpalidictionary/dpd-db/main/docs/license.md
- CC BY-NC-SA 4.0 legal text: https://creativecommons.org/licenses/by-nc-sa/4.0/
- `DpdHeadword` schema (`db/models.py`): https://raw.githubusercontent.com/digitalpalidictionary/dpd-db/main/db/models.py
- TSV backup (canonical source columns): https://github.com/digitalpalidictionary/dpd-db/tree/main/db/backup_tsv
- Plain-text exporter (`exporter/txt/`): https://github.com/digitalpalidictionary/dpd-db/tree/main/exporter/txt
- Web app: https://www.dpdict.net/
- Attribution/citation guidance (creator Bhikkhu Bodhirāsa): https://buddhistuniversity.net/content/reference/dpd
- Release stats example (`v0.3.20260202`): https://github.com/digitalpalidictionary/dpd-db/releases/tag/v0.3.20260202

*Internal ground truth (this repo):* `docs/features/in-progress/DICTIONARIES.md`; `src/CST.Avalonia/Services/{DictionaryService,IDictionaryService,DictionaryIndex}.cs`; `src/CST.Avalonia/Models/DictionaryWord.cs`; `src/CST.Core/Tools/DictionaryToolContracts.cs`.

---

*Provenance: research spike for [issue #109](https://github.com/fsnow/cst/issues/109).*
