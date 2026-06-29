# Task: Optimize the CST script converters (GitHub issue #86)

> Self-contained task brief. Assumes no prior context — safe to hand to a fresh session.
> Status: in progress (4 of ~30 converters done). Issue stays **open** until all are complete.

## Objective
Make the script converters in `src/CST.Core/Conversion/` faster by replacing their many
sequential whole-string `string.Replace()` / regex passes with a **single-pass** implementation —
while proving the new code is **byte-for-byte identical** to the original across the entire
217-book corpus. This is performance work with a correctness oracle, not a behavior change.

Issue: https://github.com/fsnow/cst/issues/86 (umbrella task — reference `#86` in commits, do
**not** close it until every converter is done).

## Why this is safe
Each converter's original (readable, 2007-era) implementation is frozen verbatim as a reference
oracle, and a differential test asserts the optimized version produces identical output over all
217 Devanagari books (~118M chars). If the test passes, the optimization is provably correct.

## The proven pattern — read these first
Four converters were already done this way; **read them as your template before writing any code**:

- Example files: `src/CST.Core/Conversion/Deva2Latn.cs` and `Deva2Ipe.cs` (cleanest examples).
- The exact diffs that established the pattern (read with `git show <sha>`):
  - `73e7c1c` — Deva2Ipe (the template + the oracle test harness)
  - `b5e5d87` — Deva2Latn (generalized the oracle test)
  - `6ec8737` — Deva2Cyrl
  - `e1acf59` — Deva2Mymr (positional ligature rules folded into the pass)
- Test harness: `src/CST.Avalonia.Tests/Conversion/ConverterEquivalenceTests.cs` and
  `src/CST.Avalonia.Tests/Performance/ScriptConverterPerformanceTests.cs`.

## Recipe, per converter
1. **Freeze the oracle.** Copy the existing `Convert` body verbatim into a new public method
   `ConvertReference` (add the XML-doc comment: "FROZEN reference … do NOT change … tests assert
   Convert == ConvertReference"). Never edit `ConvertReference` again.
2. **Rewrite `Convert` as a single pass.** Build a `string[]` lookup table indexed by char code
   (built once in the static ctor from the same dictionary the original uses), scan the input once
   into a `char[]`/`StringBuilder` buffer, and fold any positional rules (inherent-'a' insertion,
   ligature/vowel reordering, etc.) into that one pass. Guard `string.IsNullOrEmpty(input)`.
3. **Add the oracle test.** Add one `[Fact]` to `ConverterEquivalenceTests` calling the shared
   `AssertEquivalentOverCorpus("<Name>", X.Convert, X.ConvertReference)` helper.
4. **Record the benchmark.** Add a before/after timing entry in `ScriptConverterPerformanceTests`.
5. **Verify** (see below). The converter is done only when its equivalence `[Fact]` is green and
   the full suite stays green.

### Direction matters for the test input
- **`Deva2X` converters** take Devanagari input → feed the corpus directly (the harness already
  does this).
- **`X2Deva` converters** take script-X input. The raw corpus is Devanagari, so generate realistic
  script-X input by running the corresponding (already-trusted) `Deva2X.Convert` over the corpus,
  then feed that into `X2Deva`. Both `Convert` and `ConvertReference` get the same input, so the
  equivalence check is valid regardless — using `Deva2X(corpus)` just guarantees the input
  exercises the real character space.

## Verification
The corpus is 217 **UTF-16-LE** TEI XML files (Devanagari). Make sure it's present and that the
tests can find it:

- Tests read `CST_XML_DIR`, falling back to `~/Library/Application Support/CSTReader/xml`. If the
  corpus isn't at that default, `export CST_XML_DIR=/path/to/deva/xml`. Confirm it contains
  **217** `*.xml` files. (To provision a corpus from scratch, see
  [docs/development/CLOUD_SESSION_SETUP.md](../../development/CLOUD_SESSION_SETUP.md).)

```bash
cd src/CST.Avalonia
dotnet build
dotnet test --filter "FullyQualifiedName~ConverterEquivalenceTests"        # byte-identical oracle
dotnet test --filter "FullyQualifiedName~ScriptConverterPerformanceTests"  # timings
dotnet test                                                                 # full suite must stay green
```

## Remaining converters (4 done, ~26 to go)
Done: Deva2Ipe, Deva2Latn, Deva2Cyrl, Deva2Mymr.

To do — number = count of `.Replace(` in the current file (rough size signal):

- **Deva→X:** Deva2Thai (18), Deva2Sinh (17), Deva2Mlym (15), Deva2Telu (14), Deva2Khmr (14),
  Deva2Gujr (14), Deva2Tibt (9), Deva2Beng (1), Deva2Guru (1), Deva2Knda (1)
- **X→Deva:** Mymr2Deva (14), Beng2Deva (12), Sinh2Deva (11), Mlym2Deva (11), Guru2Deva (11),
  Tibt2Deva (6), Thai2Deva (3), plus Cyrl2Deva, Gujr2Deva, Khmr2Deva, Knda2Deva, Telu2Deva
- **Ipe/Latn family:** Ipe2Deva (11), Ipe2Latn, Latn2Deva, Latn2Ipe
- The low-`Replace` ones (Deva2Beng/Guru/Knda = 1) may already be near-single-pass dictionary
  converters — assess; they may only need the `ConvertReference` freeze + table.

`VriDevToUni.cs` (81 `.Replace`s) is a legacy VRI-encoding→Unicode converter, structurally
different from the transliterators — treat it as a **separate, later** item, not part of this batch.

## Hard project rules (from CLAUDE.md — non-negotiable)
- **Never use the word "Buddhist"** anywhere in code or docs. Use "Pāli", "Tipiṭaka", "VRI texts".
- **Script-conversion code uses `\uXXXX` escapes** — never paste literal/invisible non-Latin
  characters into source. Conversion *logic* uses numeric code-point literals (ASCII source);
  *data tables* keep `\uXXXX`.
- **The corpus XML is UTF-16-LE** — decode before any byte-level grep/sed. (Repo source files
  are UTF-8 + LF.)
- **Commit/push only when explicitly asked** — the human reviews first.

## Suggested workflow
- One converter per commit, message referencing `#86` (e.g.
  `"Optimize Deva2Thai: single-pass converter (#86)"`), summarizing the win (e.g.
  `DN1: X ms → Y ms`) and confirming byte-identical across all 217 books + full suite green.
  Mirror the style of the four reference commits above.
- This task is embarrassingly parallel. If you fan out subagents/worktrees, have each agent touch
  **only its own converter file**, and add all the `[Fact]`/benchmark entries to the two shared
  test files in a single coordinated step — otherwise parallel edits to those two files collide.

**Definition of done (per converter):** `ConvertReference` frozen, `Convert` single-pass,
equivalence `[Fact]` green over 217 books, benchmark recorded, full `dotnet test` green.
