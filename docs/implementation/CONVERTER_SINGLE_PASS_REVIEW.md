# Review — converter single-pass optimization

**Branch:** `converter-single-pass-optimization` · **Commit:** `fe9f579`
**Reviewer:** Claude (for Frank) · **Date:** 2026-06-28
**Scope:** `git diff main...fe9f579` — 26 converters + oracle/benchmark test additions (~1,965 insertions).

## Verdict

Strong, correct work — **mergeable after one blocker is fixed**, one test-coverage gap is
acknowledged/closed, and a mechanical merge reconciliation is done. The core claim (single-pass
`Convert` is byte-identical to the frozen readable `ConvertReference`) is **proven** for all 30
converters.

## What I verified (not just read)

- Built the test project on the branch — **green**.
- Ran the full oracle suite (`ConverterEquivalenceTests`) against the real corpus:
  **30/30 converters byte-identical across all 217 books (~118M chars each), exit 0.**
- Confirmed the branch is **complementary to main**: it builds on the four already-optimized
  converters (Deva2Ipe/Latn/Cyrl/Mymr) and does not touch them — no overlap.
- Every changed converter defines both `Convert` (fast) and `ConvertReference` (frozen oracle), and
  has a matching `[Fact]` in `ConverterEquivalenceTests` (30 facts total).
- Escape-rule scan: all converters are `\uXXXX`-only **except Cyrl2Deva** (see blocker).

## Findings

### 1. BLOCKER — escape-rule violation in `Cyrl2Deva.cs`

The hard rule (CLAUDE.md): *"Script-conversion code uses `\uXXXX` escapes — never paste literal/invisible
non-Latin characters into source."* `Cyrl2Deva.cs` is the one file that breaks it — and `fe9f579`'s
new single-pass `Convert` **added more** literal glyphs, not just the pre-existing dictionary keys:

- Dict keys mix literal Cyrillic with `\u` combining marks (introduced earlier in `d5a9953`, now on main):
  ```csharp
  cyrl2Dev["д̇х"] = "ध"; // literal д, х
  cyrl2Dev["м̣"]  = "ं";
  ```
- The optimized `Convert` (added by this branch) hard-codes literal **Cyrillic and Devanagari** glyphs:
  ```csharp
  sb.Append('्');                     // literal Devanagari virama
  bool isNiggahita = (devaOutput == "ं");
  if (twoChar == "аа" || twoChar == "ий" || twoChar == "уу") ...
  else if (twoChar == "аа") devaOutput = "ा";
  ```

Every other converter on the branch uses numeric `\uXXXX` literals throughout (the oracle scan found
zero non-ASCII in their code). `Cyrl2Deva` should match that convention.

**Fix:** replace all literal Cyrillic/Devanagari in `Cyrl2Deva.cs` (keys *and* the new `Convert` body)
with `\uXXXX`. The established workflow is: write with numeric hex literals, or do a final Python pass
that rewrites non-ASCII → `\uXXXX`. The oracle test guarantees the rewrite stays byte-identical, so
this is mechanical and safe.

> Note: `ConvertReference` is frozen on purpose, but "frozen" means *behaviorally* frozen — converting
> its glyphs to escapes is a representation change, not a behavior change, and the oracle proves it.

### 2. Test-coverage gap — reverse/cross converters are fed the wrong input script

`AssertEquivalentOverCorpus` feeds raw **Devanagari** book text to *every* converter:

```csharp
var deva = File.ReadAllText(f);
var a = fast(deva);  var b = reference(deva);
```

That's correct for the **Deva2X** converters (Deva in → Deva2X). But for **X2Deva**, **Latn2Ipe**,
**Latn2Deva**, **Ipe2Latn**, etc., the input is the wrong script — Devanagari text contains almost no
Cyrillic/Thai/Sinhala/Latin source characters, so their mapping tables are barely hit. For those
converters the oracle mostly proves the **pass-through path** is equivalent, not the real conversion.
"Byte-identical" is therefore a weak guarantee for ~half the converters.

**Suggestion:** for the non-Deva-input converters, drive them with input in their *own* script via a
round-trip — e.g. `Deva2Thai.Convert(deva)` (trusted forward) → feed result to `Thai2Deva.Convert`
vs `Thai2Deva.ConvertReference`. That actually exercises the tables the optimization changed. (Same
applies to the benchmark harness, which times X2Deva on DN1's Devanagari text.)

### 3. Merge reconciliation needed (not a defect, but will conflict)

The branch forked from main **before** main commit `de31f91`, which made the corpus-dependent tests
environment-robust. Both sides edited the same two files, so a merge will conflict in:

- `ConverterEquivalenceTests.cs` — branch still has `Assert.True(Directory.Exists(dir), …)` (hard-fail
  when the corpus is absent, e.g. CI). Main replaced this with an early `return` (no-op). **Keep main's
  version + the branch's 26 new facts.**
- `ScriptConverterPerformanceTests.cs` — same corpus-absent reconciliation.

Also: main now has a **hermetic** `OffsetConventionTests` (tokenizes directly, no index dependency).
The branch doesn't touch it, so no conflict there — just don't reintroduce the old index-reading version.

### 4. Minor — comment hygiene

Non-code, low risk, but worth a pass while you're in these files:
- `Mymr2Deva.cs` (≈ lines 238–240): stray replacement char `�` in comments (`// �a + ca`) — looks like
  copy/paste corruption; restore the intended glyph name or make it ASCII.
- `Khmr2Deva.cs` (≈ line 161): a literal Khmer glyph in a comment.
- `Deva2Thai.cs` / `Sinh2Deva.cs`: Latin-with-diacritic examples in comments (`iṃ`, `ē`, `ō`) — these
  are benign, but for strict consistency with the escape rule prefer code-point references in comments.

## Recommendation

Fix #1 (blocker) and reconcile #3 on merge; close #2 either now (round-trip inputs) or as a tracked
follow-up so we don't ship a false sense of coverage for the reverse converters. #4 is optional polish.
Everything else is solid — the frozen-oracle approach is exactly right and the byte-identical proof
across the full corpus is convincing.
