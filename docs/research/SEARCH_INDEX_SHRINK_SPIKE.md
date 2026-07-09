# Search Index Shrink — Research Spike (#55)

*Read-only investigation of the Lucene.NET 4.8 index configuration and its consumers, plus on-disk measurement of the live index. No behavior change proposed here — this is the analysis that a later PR would implement.*

## Bottom line up front

The single biggest lever is **term vectors (~45% of the index, ~60 MB)**, which duplicate offset/position data the app *also* reads straight from postings. They are consumed by exactly **one** fallback code path (`BookDisplayViewModel`), which can be migrated to postings. **Norms** and **term-vector payloads** are provably dead weight (no scoring anywhere; no analyzer ever sets a payload) and are safe to drop with zero code changes — but they are small. Every change requires a full reindex.

Net effect of the recommended change: roughly **135 MB → ~75 MB** with no change to search results, counts, occurrences/KWIC, or highlight offsets.

## 1. Current field configuration (verified)

`src/CST.Lucene/BookIndexer.cs`, `IndexBook()`, lines 264–274 — the `"text"` field:

```csharp
FieldType ft = new FieldType(TextField.TYPE_NOT_STORED);   // not stored
ft.IsIndexed = true; ft.IsTokenized = true;
ft.OmitNorms = false;                    // norms ARE written
ft.StoreTermVectors = true;
ft.StoreTermVectorOffsets = true;
ft.StoreTermVectorPayloads = true;       // payloads in term vectors
ft.StoreTermVectorPositions = true;
ft.IndexOptions = IndexOptions.DOCS_AND_FREQS_AND_POSITIONS_AND_OFFSETS;  // postings carry offsets too
ft.Freeze();
```

Analyzer at index time is `DevaXmlAnalyzer` → `DevaXmlTokenizer` (BookIndexer.cs:187–188). Other fields (`file`, `matn`, `pitaka`) are `StringField`/`StoredField` and are not at issue. Compound files are enabled (`config.UseCompoundFile = true`), but Lucene keeps the large segments non-compound.

So the field pays simultaneously for postings offsets **and** term-vector offsets/positions/payloads **and** norms. The question is which are actually read.

## 2. What each stored component is used for

| Component | Where it lives | Consumed by / status |
|---|---|---|
| **Postings docs+freqs** (`.doc`) | postings | **USED.** `TermsEnum.DocFreq`/`TotalTermFreq` counts-only fast path (`SearchService.GetMatchingTermStats`, `SelectTermsCountsOnly`, 626–684); `Freq` during position walks. |
| **Postings positions** (`.pos`) | postings | **USED.** Multi-word/phrase/proximity matching reads `dape.NextPosition()` (`SearchMultiWordAsync` 361; `GetTermOccurrencesAsync` 566; `GetTermPositionsAsync` 921; `GetMultiWordPositionsAsync` 997). |
| **Postings offsets** (`.pay`) | postings | **USED — load-bearing.** `dape.StartOffset`/`EndOffset` drive every KWIC snippet and highlight (SearchService 362–363, 568–569, 923, 998); feeds `TeiSnippetExtractor` and `SearchTool.GetOccurrencesAsync`. Requires `IndexOptions` offsets. |
| **Norms** (`.nvd`/`.nvm`) | norms | **UNUSED.** No relevance scoring anywhere: no `IndexSearcher` / `TopDocs` / `BM25` / `Similarity` / `Scorer` / `.Search(` in app code. Search is pure term-enumeration + postings position matching. `OmitNorms=false` is dead weight. |
| **Term-vector offsets + positions** (`.tvd`/`.tvx`) | term vectors | **Used by ONE fallback only.** `BookDisplayViewModel.ApplySearchHighlightingToRawXml` reads `indexReader.GetTermVector(_docId,"text")` then `DocsAndPositions(..., OFFSETS)` (1010, 1037–1049). The primary branch `ApplyHighlightingFromPositions` (987–990) uses positions carried in from search (postings-derived). The KWIC/occurrences path never touches term vectors. |
| **Term-vector payloads** (inside `.tvd`) | term vectors | **UNUSED — pure waste.** No `PayloadAttribute` is set anywhere. `DevaXmlTokenizer.IncrementToken` sets only term/offset/positionIncrement/type (263–272). `IpeFilter` is entirely commented out. `IpeAnalyzer` is not used for the text field. |
| **Terms dict** (`.tim`/`.tip`) | terms | **USED** (term enumeration/expansion). Not a shrink target. |
| **Stored fields** (`.fdt`/`.fdx`) | stored | tiny (`file`/`matn`/`pitaka`); not at issue. |

Key structural fact: because postings already index `DOCS_AND_FREQS_AND_POSITIONS_AND_OFFSETS`, **term vectors are redundant** for this app's offset/position needs. The usual reason to keep them — Lucene's `FastVectorHighlighter` — does not apply; highlighting is done by manual offset insertion into the raw XML.

## 3. On-disk size breakdown (measured)

Live index: `~/Library/Application Support/CSTReader/index`, 10 segments, **~135.6 MB** (142,229,488 bytes). 3 large segments are non-compound; 6 small merged segments are packed in `.cfs`.

| Category | Ext(s) | Bytes | MB | % of index |
|---|---|---:|---:|---:|
| **Term vectors** | `.tvd` + `.tvx` | 59,050,234 | **56.3** | **41.5%** |
| Postings positions | `.pos` | 31,379,633 | 29.9 | 22.1% |
| Terms dictionary | `.tim` + `.tip` | 26,778,210 | 25.5 | 18.8% |
| Compound (mixed) | `.cfs` | 12,464,211 | 11.9 | 8.8% |
| Postings offsets | `.pay` | 9,925,066 | 9.5 | 7.0% |
| Postings docs+freqs | `.doc` | 2,624,968 | 2.5 | 1.8% |
| Norms | `.nvd` + `.nvm` | 486 | ~0 | 0.0003% |
| Stored/meta | `.fdt/.fdx/.fnm/.si` | 6,680 | ~0 | ~0% |

**Caveat on `.cfs` (8.8%):** the 6 small compound segments hide their internal per-type split. If their composition mirrors the large segments (term vectors ≈ 45% of a segment), then whole-index **term vectors ≈ 60–65 MB ≈ ~45%** and postings offsets ≈ ~10.5 MB. The measured non-compound numbers are exact; the `.cfs` allocation is an estimate.

## 4. Safe shrink levers, ordered (each requires a full reindex of 217 books)

### Lever A — Drop term vectors entirely *(biggest win; needs a one-line consumer migration first)*
```csharp
ft.StoreTermVectors = false;
ft.StoreTermVectorOffsets = false;
ft.StoreTermVectorPositions = false;
ft.StoreTermVectorPayloads = false;
```
- **Estimated saving:** ~**56 MB visible + ~5 MB inside `.cfs` ≈ ~60 MB ≈ ~45%** of the index.
- **Why it's safe *after* migration:** the only reader of term vectors is the fallback branch in `BookDisplayViewModel.ApplySearchHighlightingToRawXml` (999–1077). It needs only **offsets**, which are identical in postings. Rewrite that branch to read offsets from postings via `MultiFields.GetTermPositionsEnum(reader, liveDocs, "text", termBytes)` restricted to `_docId` — exactly the pattern `SearchService.GetTermPositionsAsync` (908–935) already uses. After that, no consumer reads term vectors.
- **Classification:** **RISKY as a pure config change** (would break the fallback highlight path); **SAFE and high-value once the fallback is migrated to postings.** Do the migration + drop together.

### Lever B — Omit norms *(safe, zero code change, tiny)*
`ft.OmitNorms = true;` — saving ~486 bytes (negligible; ~1 byte/doc/field × 217 docs). No scoring anywhere reads norms. **SAFE, high-confidence**; worth doing for correctness/clarity even though the byte win is trivial.

### Lever C — Drop term-vector payloads *(safe; subsumed by Lever A)*
`ft.StoreTermVectorPayloads = false;` (only relevant if term vectors are otherwise kept). No analyzer sets payloads. **SAFE**; if Lever A is taken, term vectors and their payloads disappear entirely.

### What must NOT change (load-bearing)
- `IndexOptions = DOCS_AND_FREQS_AND_POSITIONS_AND_OFFSETS` — postings offsets (`.pay`) and positions (`.pos`) drive all snippets/highlights/proximity.
- `.doc`/`.tim`/`.tip` — counts and term expansion.

## 5. Verification plan (parity test before shipping)

Mirror the existing integration style (`IndexStructureTests`, `LocalApiIntegrationTests`, `SearchToolTests`). Build a small fixture (prose + `rend="gatha"` verse + `<note>` footnotes), index it **twice** (today's `FieldType` vs proposed), and assert parity across:

1. **Term expansion** — identical ordered term lists for exact/wildcard/regex.
2. **Counts** — identical `DocFreq`/`TotalTermFreq` per term (counts-only fast path).
3. **Postings offsets** — identical `(docId, position, StartOffset, EndOffset)` tuples per term.
4. **KWIC / occurrences** — identical `Snippet`, `HitStart`/`HitLength`, `Refs`, `Highlights` for single-term, phrase, and proximity queries.
5. **In-book highlighting** — identical inserted-tag offsets for both the positions branch and the migrated-fallback branch (include a case that exercises the fallback: search terms present, `searchPositions` empty).

Also invert `IndexStructureTests` assertions that currently expect `GetTermVectors` non-null (112–116, 205–206) — an intentional test update, not a regression.

## 6. Recommendation

**First PR (safe, high-confidence):**
- **Lever A done properly** — migrate the `BookDisplayViewModel` fallback highlight branch (999–1077) from term vectors to postings offsets (reuse the `GetTermPositionsAsync` pattern), then set all four `StoreTermVector*` to `false`. Captures ~45% (~60 MB) with a small, well-understood change under full parity coverage.
- **Lever B (omit norms)** — bundle in; one line, zero risk.

Gated on a **full reindex** — issue a version bump / index-format marker so existing users rebuild.

**Do second / fold in with review:** confirm whether the term-vector fallback branch is still *reachable* (opening from search always passes positions; the fallback seems to matter only for restored sessions with empty `SearchPositions`). If provably dead, Lever A becomes a pure config change + branch **deletion** — even cleaner. Worth a focused reachability check first.

## 7. Risks & open questions

- **Reindex required & user impact** — all levers change the index format; needs an index-version guard so stale indexes rebuild, not silently mis-read.
- **`.cfs` attribution is estimated** — re-measure after the change to confirm realized saving.
- **Fallback reachability** — could not definitively prove the term-vector branch is dead; treated conservatively as live (migrate before drop).
- **Norms saving is negligible** here (217 docs) — included for correctness, not size.
- **Offsets are the crux and must stay in postings** — the parity test (§5 steps 3–5) is the guardrail; do not merge without it.
- **`.pos` (30 MB) and `.tim` (25 MB)** are the next-largest categories but are genuinely used (proximity, term expansion) — no safe lever there without changing search behavior.

---

*Provenance: research spike for [issue #55](https://github.com/fsnow/cst/issues/55). Files cited: `src/CST.Lucene/BookIndexer.cs` (264–274), `DevaXmlTokenizer.cs` (263–272), `DevaXmlAnalyzer.cs`, `IpeFilter.cs` (commented out); `src/CST.Avalonia/Services/SearchService.cs` (362–363, 566–569, 626–684, 908–935, 997–998), `Services/Tools/SearchTool.cs` (97–174); `src/CST.Core/Search/TeiSnippetExtractor.cs`; `src/CST.Avalonia/ViewModels/BookDisplayViewModel.cs` (963–1099), `SearchViewModel.cs` (785–809); `src/CST.Avalonia.Tests/Integration/IndexStructureTests.cs` (70–116, 175–206).*
