# Building & delivering the DPD-lemma asset

The lemma search, lemma report, sandhi decomposer (`/v1/deconstruct`), and the DPD dictionary source
(`language: "dpd"`) are all powered by one derived SQLite asset, **`dpd-lemma.db`**, built from a
[Digital Pāḷi Dictionary](https://www.dpdict.net/) (DPD) release. It is corpus-agnostic (no occurrence counts —
those come from the app's Lucene index) and ships separately from the app binary; the app opens it read-only at
`<app-support>/CSTReader/dpd-lemma/dpd-lemma.db` and degrades gracefully to "feature off" when it is absent.

## The builder

`tools/DpdLemmaBuilder` reads a full DPD `dpd.db` and emits `dpd-lemma.db`, keeping only the tables/columns the
features need (see [../architecture/LEMMA_EXPANSION.md](../architecture/LEMMA_EXPANSION.md)). Scope tiers:

| scope  | adds                                             | powers                                    | ~size (zst) |
|--------|--------------------------------------------------|-------------------------------------------|-------------|
| `lean` | `form_lemma` + `lemma`                            | resolver only                             | ~13 MB      |
| `mid`  | + per-form grammar + report columns + enclitic decon | lemma search, report, dictionary; enclitic-only sandhi | ~25 MB |
| `full` | + all sandhi deconstructors                       | **everything**, incl. compound `/v1/deconstruct` | ~51 MB |

`full` is the superset that makes every shipped feature work; `mid` halves the size but limits
`/v1/deconstruct` to enclitic splits. The builder's `gloss` column coalesces `meaning_2` when `meaning_1` is
empty (~30% of headwords, mostly compounds — #109), so DPD dictionary definitions are never blank.

```bash
dotnet run -c Release --project tools/DpdLemmaBuilder -- <dpd.db> <out/dpd-lemma.db> --scope full
```

The builder ends with a **validation pass** (lemma count + known back-lookups/families) and exits non-zero if
any check fails.

## CI build & publish

`.github/workflows/build-dpd-lemma-asset.yml` (manual `workflow_dispatch`) downloads a DPD
`dpd.db.tar.bz2` release, runs the builder at the chosen scope, compresses with `zstd -19`, and publishes
`dpd-lemma.db.zst` + a `.sha256` + a `dpd-lemma.manifest.json` (sizes, checksum, DPD tag, scope) as a GitHub
Release tagged `dpd-lemma-<dpdTag>-<scope>`. Inputs: `dpd_tag` (default the last-validated release), `scope`
(default `full`), and `publish` (uncheck to build + validate + upload an artifact only, without a release).

**Version tripwire:** the builder's validation is pinned to the current DPD release (exact lemma count + a few
known lemma ids). A *new* DPD tag can fail those checks by design — re-verify and bump the pinned facts in
`tools/DpdLemmaBuilder/Program.cs` deliberately; it is not a flaky build.

## App-side download (planned)

A `DpdUpdateService` — parallel to `XmlUpdateService` for the corpus XML — will fetch the latest published
`dpd-lemma.db.zst`, verify the checksum, and decompress it into app-support. Until then the asset is seeded
manually. See the memory note *dpd-derived-asset-delivery* for the delivery design.
