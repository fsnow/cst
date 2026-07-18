# Dictionary Feature

Pāli→English and Pāli→Hindi dictionary lookup. Users type a Pāli word in any
supported script and see matching headwords and their definitions.

> **Source of truth.** This spec is derived from the original CST4 WinForms
> implementation, which is the authoritative reference for a port:
> - `src/Cst4/FormDictionary.cs` — all load/search/display logic
> - `src/Cst4/DictionaryWord.cs` — the `(Word, Meaning)` model
> - `src/Cst4/DictionaryWordComparer.cs` — the collation used for sort **and** binary search
>
> The IPE converter is already ported and available to the Avalonia app at
> `src/CST.Core/Conversion/Any2Ipe.cs` (`Any2Ipe.Convert(string)`).
>
> An earlier version of this document described the feature inaccurately (see
> the "Corrections" section at the end). Implement from the CST4 source, not
> from prose paraphrases.

## 1. Overview

The dictionary loads flat word/definition text files, normalizes every headword
into **IPE** (the internal script-independent Pāli encoding), and answers
lookups with a binary search plus prefix / nearest-neighbor matching. Because
the user can type in any script, the query is IPE-normalized the same way before
matching. Definitions are HTML fragments; `<see>…</see>` cross-references become
clickable links that trigger a new lookup.

## 2. The IPE collation invariant (read this first)

Everything depends on one property: **IPE is designed so that ordinal
(codepoint) order equals Pāli alphabetical order.** IPE was created as an ideal
script-independent Pāli encoding specifically so Pāli text sorts correctly with
a trivial comparison — no locale, no collation table.

`DictionaryWordComparer` is therefore just an ordinal char-by-char comparison
with a length tiebreak (equivalent to `string.CompareOrdinal`):

```csharp
// compares dw1.Word vs dw2.Word
while (both have a char at position i) {
    if (x[i] < y[i]) return -1;
    if (x[i] > y[i]) return  1;
    i++;
}
return x.Length - y.Length;   // shorter string sorts first
```

The **same comparer** is used to `Sort()` the word list and to `BinarySearch()`
it. A port must keep using IPE ordinal order — **never** substitute
culture-aware / `StringComparer.CurrentCulture` comparison, which would break the
binary search.

## 3. Data model

```
DictionaryWord {
    string Word;      // IPE-normalized headword (the sort/search key)
    string Meaning;   // HTML fragment (definition)
}
```

`Word` is always stored in IPE. For display, CST4 converts it back to the user's
current script (`ScriptConverter.Convert(word, Script.Ipe, currentScript)`).

## 4. Data files and loading

Two independent, lazily-loaded lists: `enWords` (English) and `hiWords`
(Hindi). Each is loaded on first lookup for that language and cached; on load
error the list is reset to `null` so the next lookup retries.

**Avalonia layout (new design).** Dictionaries live under the CSTReader
app-support directory in a new `dictionaries/` tree, organized by **two-letter
language code**, with each source's files **prefixed by source id**:

```
~/Library/Application Support/CSTReader/dictionaries/
├── en/vri-pali-english-dictionary.txt   (VRI Pāli→English)
└── hi/vri-pali-hindi-dictionary.txt     (VRI Pāli→Hindi, Devanagari headwords)
```

- The `vri-` prefix marks the source (Vipassana Research Institute). This is
  deliberate: additional dictionaries from other open-source projects will drop
  into the same language dirs under different prefixes, and the loader reads
  **every file** in a language dir (per CST4's `DirectoryInfo.GetFiles()`
  behavior) — so multiple sources merge naturally per language.
- Files are UTF-8 (LF). The original VRI files were UTF-16-LE and have been
  converted.
- This fixes a CST4 inconsistency where English lived under `en/` but Hindi sat
  loose in `Reference/`; both are now under a language-code dir.

**Repo source-of-truth.** The bundled dictionary files live in the Avalonia
project, mirroring the runtime layout (and the `src/CST.Avalonia/Xsl/` precedent):

```
src/CST.Avalonia/dictionaries/
├── en/vri-pali-english-dictionary.txt
└── hi/vri-pali-hindi-dictionary.txt
```

These are UTF-8 (LF), converted from the legacy CST4 UTF-16-LE originals (which
are left untouched in `src/Cst4/` — that directory is not modified).

**Bundling / deployment** (not yet wired up) should mirror the XSL data pattern:
the repo `dictionaries/` source → copied into the macOS app bundle by
`package-macos.sh` → copied into the user's app-support `dictionaries/` dir on
first run if absent (cf. `BookDisplayViewModel.EnsureXslFilesInUserDirectory` /
`CopyXslFilesFromBundle`), then read from app-support at runtime.

**Legacy CST4 source (for provenance).** The canonical VRI files originate from
`src/Cst4/Reference/en/pali-english-dictionary.txt` and
`src/Cst4/Reference/pali-hindi-dictionary.txt` — confirmed by CST4 `Config` and
both installers (`src/CST4.wxs`, `src/Cst4/Cst4.nsi`), which shipped exactly
those two. The repo's `*-vri-dev*`, `*-before-editing`, and `.zi` files are
dev-only intermediates that were never installed — do not use them.

**File format:** plain text, alternating lines — headword, then its HTML
definition, repeating. A pair is skipped if either the word or the meaning line
is empty. Reading stops at EOF (a `null` from `ReadLine`).

### 4a. English load (`LoadEnglishDictionary`)

1. For each file in the English directory, read word/meaning pairs.
2. IPE-normalize the word: `word = Any2Ipe.Convert(word)`.
3. Accumulate into a temporary `Dictionary<string,string>` keyed by IPE word,
   **merging duplicates**: if the word already exists, the new definition is
   appended to the existing one with a separator. (Merging spans all files and
   repeats within a file.)
4. Copy the merged entries into `enWords` and `Sort()` with
   `DictionaryWordComparer`.

> **Known CST4 bug — do not replicate verbatim.** The merge separator in the
> source is the malformed string `"</p><hr/<p>"` (missing `>` on the `<hr`, and
> unbalanced `<p>`/`</p>`). A port should emit correct markup, e.g. wrap each
> definition in `<p>…</p>` and join with `<hr/>`.

### 4b. Hindi load (`LoadHindiDictionary`)

Same read loop and IPE normalization, but **no duplicate merging** — every pair
is appended to `hiWords` as-is, then the list is sorted. (This asymmetry with
English is intentional to preserve; revisit only deliberately.)

## 5. Search algorithm (`Search`)

Given the raw query text:

1. If empty → clear the definition pane and return.
2. IPE-normalize: `word = Any2Ipe.Convert(query)`. (CST4 also lowercases the
   query text before this.)
3. `index = words.BinarySearch(new DictionaryWord(word, ""), comparer)`.

**Exact match (`index >= 0`):**
- Add `words[index]`.
- Scan **forward** adding every subsequent word where
  `words[i].Word.StartsWith(word)`; stop at the first that doesn't.
- Select the first result (unless suppressed).

**No exact match (`index < 0`):** `index = ~index` is the insertion point.
Find the best candidates by shared leading characters, using
`CountCommonStartLetters(a, b)` = length of the common prefix of two strings:

- `commonBehind = CountCommonStartLetters(word, words[index-1].Word)` (neighbor
  just before the insertion point, if any).
- `commonAhead  = CountCommonStartLetters(word, words[index].Word)` (neighbor at
  the insertion point, if any).
- **Look behind** only if `commonBehind >= commonAhead && commonBehind > 0`:
  walk backward collecting every consecutive word whose common-prefix length
  with the query **equals `commonBehind`**; stop otherwise. (Collected via a
  stack so they end up in ascending order.)
- **Look ahead** only if `commonAhead >= commonBehind && commonAhead > 0`: walk
  forward from the insertion point collecting every consecutive word whose
  common-prefix length **equals `commonAhead`**; stop otherwise.
- On a tie (`commonBehind == commonAhead > 0`) **both** sides are collected.
- If **neither** neighbor shares any leading character (both counts `0`), there
  are **no** results → clear the definition pane.
- Otherwise select the first result (unless suppressed).

So a miss returns the run of headwords that share the *longest achievable*
common prefix with the query — the "best guess" behavior — not merely the two
adjacent words.

## 6. Display and navigation (UI-coupled — not part of the non-UI core)

These parts touch the WebView/UI and are called out separately; the port's UI
work owns them.

- **Definition rendering (`DisplayMeaning`).** Wrap the meaning HTML in a small
  document, inject a `<style>` body rule per language (English: `Tahoma`
  9.75pt; Hindi: `CDAC-GISTSurekh` 11pt), and render it.
- **`<see>` cross-references.** Regex `"<see>(.+?)</see>"` →
  `<a onclick="window.external.SeeAlso('$1')" href="#">$1</a>`. Clicking calls
  back into the app to look up that word.
- **See-Also back-stack.** `SeeAlso(word)` pushes the current
  `(queryText, selectedIpeWord, selectedIndex)` and navigates to `word`; a
  "Back to …" link (shown when the stack is non-empty) pops it via
  `SeeAlsoBack`. The stack is **cleared** on: a keypress in the query box, a
  definition-language change, and selecting a non-first result
  (`SelectedIndex > 0`).
- **Script changes** re-render the visible headword list (each
  `DictionaryWord.ToString()` converts IPE → current script on the fly).

## 7. Porting notes

**Non-UI core (verifiable with `dotnet test`):** data model, file loading
(English merge / Hindi no-merge), IPE normalization via
`CST.Core`'s `Any2Ipe.Convert`, ordinal sort, and the binary-search
exact/prefix/tied-nearest-neighbor matching. This is the recommended first
deliverable — a `DictionaryService` (+ model + comparer) with unit tests that
pin the matching semantics above (exact hit, prefix run, behind-only,
ahead-only, tie-both-sides, and no-common-prefix → empty).

**UI layer (owned by the app's UI work):** the results panel, WebView rendering,
`<see>` link handling, back-stack, and per-script fonts (§6).

## 8. Third-party & structured dictionaries (#109)

The flat-file loader above is only one **backend**. As of #109 the dictionary
tool (`IDictionaryTool`, surface C) unions two backends behind one contract, so a
caller looks a word up the same way regardless of source:

- **Flat-file dictionaries** — the `dictionaries/<lang>/` tree of §4. This is also
  the **general user-import format**: drop a `<lang>/` folder with one or more
  UTF-8 files of alternating `headword` / `definition-HTML` lines (§4 "File
  format") plus an optional `source.json`, and it loads with no code change. A
  Pāli↔Pāli or bilingual flat dictionary imports today; a manifest-aware v2 loader
  (`manifest.json` with id/headwordScript/meaningFormat/languages) is a later
  option, not required.
- **Digital Pāḷi Dictionary (DPD)** — the reserved language code **`dpd`**, present
  only when the dpd-cst-subset asset (`dpd-cst-subset.db`) is installed. DPD ships **no
  rendered-HTML definitions**, so entries are **composed on the fly** from the
  asset's structured columns (pos + gloss + literal meaning + construction) by
  `CompositeDictionaryTool`. The word→entry key reuses the same `form_lemma`
  index the lemma endpoints use, so an **inflected** word resolves and a homograph
  returns several entries. Each entry carries a `lemmaId` that chains to the lemma
  report (`/v1/lemma-report/{lemmaId}`) for the full dossier — the dictionary
  meaning is deliberately compact; depth lives in the report.

**Attribution (`source.json`, #268).** Each flat `<lang>/` dir may carry a
`source.json` — the authoritative citation `{ title, compiler, edition, year,
publisher, license, url }`, read **verbatim, never inferred**; a wholly-blank file
reports `null` (unattributed), not a guess. DPD's citation is built the same shape
from the asset's own `meta` table (so it tracks the shipped release), never
hard-coded. `/v1/dictionary/languages` returns each dictionary's source.

**The `meaning_2` coalesce (`DpdLemmaBuilder`, in the [`dpd-cst-subset`](https://github.com/fsnow/dpd-cst-subset) repo, #109).** ~30% of DPD headwords
(26,782/89,050, mostly compounds) have an empty `meaning_1` but a populated
`meaning_2`; the builder now sets `gloss = COALESCE(NULLIF(meaning_1,''),
meaning_2)` so the composed DPD definition is not blank for those. Requires a
rebuilt asset (converter v3).

**Deferred (UI, Frank's lane):** a dictionary/source picker (today's picker only
chooses a *language*) and a multi-source results panel with per-source sections.
The API-first backend above lands the "look a word up in DPD and cite it" value
without that UI.

## Corrections vs. the previous version of this document

The earlier draft (written by a much weaker model) contained errors now fixed
above:

- Claimed IPE "is a variant of IAST (International Alphabet of Sanskrit
  Transliteration)." **False.** IPE is a purpose-built script-independent Pāli
  encoding whose codepoints sort correctly by ordinal comparison; it is not
  derived from IAST. This is the reason the collation is trivial.
- Never mentioned `DictionaryWordComparer` or the ordinal-collation invariant —
  the single most important detail for correctness.
- Said duplicate definitions are joined with `<hr/>`. The actual separator is
  the malformed `</p><hr/<p>` (a bug), and only **English** merges — Hindi does
  not.
- Described the miss/nearest-neighbor path as simply "closest words before and
  after." The real rule is *tied-maximum shared-prefix*: it scans the winning
  side(s) only, and returns empty when no leading character is shared.
- Used stale paths (`en-dict/`, `pali-hindi.dat`). Actual: `Reference/en/`
  (directory) and `Reference/pali-hindi-dictionary.txt`.
