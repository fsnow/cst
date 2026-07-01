# CST4 Dictionary — Oracle Capture Plan

Screenshots to capture from **CST4 running on Caracara** (Windows 11 / Parallels)
so we have real-CST4 oracles for the CST Reader (Avalonia) dictionary backend
(#25). Each case exercises a specific branch of CST4's lookup algorithm
(`FormDictionary.Search`), so the captures pin behavior we can regression-test.

Two oracles are already captured (`samayaṃ`, English + Hindi — see
`docs/features/planned/`) and are locked in as regression tests
(`DictionaryOracleTests`). This plan fills in the remaining branches.

## How to capture

1. Launch CST4 on Caracara and open **Dictionary**.
2. Set the app's **display script to Roman (Latin)** so the *Words* list shows
   Latin headwords — that matches the "CST Reader output" column below and makes
   diffing easy. (The definition-language dropdown is separate from the display
   script; leave definitions in their own script.)
3. Make the Dictionary window **tall enough to show the whole Words list** — some
   cases return 10–23 entries. If a list still overflows, capture top **and**
   bottom.
4. For each row: pick the **Definition Language**, clear the box, type the
   **exact** query (including diacritics/niggahita), and screenshot the whole
   dialog (Words list **and** Meaning pane).
5. Suggested filenames (ASCII only): `dict-<lang>-<label>.png`, e.g.
   `dict-en-anga.png`, `dict-hi-samayam.png`.

**Typing diacritics:** the queries use Pāli diacritics (`ā ṅ ṃ ā ī` …) and one
niggahita (`ṃ` in `samayaṃ`). Type them as shown (CST4 auto-detects the script).
If any diacritic is hard to enter, note it on the screenshot and we'll adapt.

The **"CST Reader output"** column is what our new backend already returns
(headword list, in order, count in parens). The capture's job is to confirm CST4
produces the same — or reveal a difference.

## English (Definition Language = English)

| # | Type this | Branch exercised | CST Reader output (headwords · count) |
|---|-----------|------------------|----------------------------------------|
| E1 | `-gū` | Exact match — **first** headword (binary-search lower bound) | `-gū` · 1 |
| E2 | `akaṭo` | Exact match, **no** prefix followers | `akaṭo` · 1 |
| E3 | `aṅga` | Exact match **+ prefix run** | `aṅga, aṅgaṃ, aṅgajātaṃ, aṅgaṇaṃ, aṅgati, aṅgadaṃ, aṅganā, aṅgavikkhepo, aṅgavijjā, aṅgahāro` · 10 |
| E4 | `aṅg` | **Miss** → ahead "best-guess" prefix run | `aṅga … aṅgī, aṅgīraso, …` · 23 |
| E5 | `samayaṃ` | **Miss** → nearest-neighbor, single side | `samayo` · 1 *(already captured)* |
| E6 | `abbhuto` | Exact match, **duplicate headword → merged definitions** | `abbhuto` · 1 |
| E7 | `homo` | Exact match — **last** headword (binary-search upper bound) | `homo` · 1 |
| E8 | `homoz` | **Miss** past the last headword → behind-only nearest-neighbor | `homo` · 1 |
| E9 | `zzzz` | **Miss**, no shared leading char → **empty** list + empty meaning | *(0)* |
| E10 | *(clear the box)* | **Empty query** → empty list + empty meaning | *(0)* |

**E6 is the important one for the Meaning pane:** `abbhuto` appears twice in the
VRI file, so CST4 concatenates both definitions —
*"Mysterious; wonderful, portentous"* **and** *"The Marvellous, one of the
nāṭyarasas; a gambler's stake"*. Capture the Meaning pane carefully; it documents
how CST4 renders the join (its separator in code is a malformed `</p><hr/<p>`).
Our backend uses a clean `<hr/>` instead.

## Hindi (Definition Language = Hindi)

| # | Type this | Branch exercised | CST Reader output (headwords · count) |
|---|-----------|------------------|----------------------------------------|
| H1 | `aṃgada` | Exact match, **no** prefix followers | `aṃgada` · 1 |
| H2 | `akkha` | Exact match **+ prefix run** | `akkha, akkhaka, akkhaggakīla, akkhaṇa, akkhaṇa-vedhī, akkhata, akkhadassa, akkhadevī, akkhadhutta, akkhaya, akkhara, akkharamālā, akkharāvayava` · 13 |
| H3 | `samaya` | Exact match **+ prefix run** | `samaya, samayantara` · 2 |
| H4 | `samayaṃ` | **Miss** → nearest-neighbor, **tied both sides** | `samaya, samayantara` · 2 *(already captured)* |
| H5 | `horā-yanta` | Exact match — **last** headword (upper bound) | `horā-yanta` · 1 |
| H6 | `horā-yantaz` | **Miss** past the last headword → behind-only | `horā-yanta` · 1 |
| H7 | `zzzz` | **Miss**, no shared leading char → **empty** | *(0)* |
| H8 | *(clear the box)* | **Empty query** → empty | *(0)* |

Note the pair **H3 vs H4**: typing `samaya` (a real headword) is an *exact match*
whose prefix run happens to be `{samaya, samayantara}`, while `samayaṃ` (not a
headword) is a *miss* whose tied nearest-neighbors are the same two — different
code paths, same result. Both are worth capturing.

## Optional / stretch

- **Cross-script input into the English dictionary.** English headwords are
  stored in Latin; the Hindi captures already prove Latin-typed queries match
  Devanagari-stored headwords (script-independent normalization to IPE). The
  untested direction is typing a **Devanagari** word while Definition Language =
  English. If you have a Devanagari IME, type the Devanagari form of `dhammo`
  and confirm it still finds the English entry.
- **Display-script variants.** Re-capturing a case or two with the app's display
  script set to Devanagari (instead of Roman) shows the Words list rendered in
  Devanagari — useful reference for the UI's IPE→script headword rendering, but
  not needed for backend validation.

## After capturing

Drop the PNGs somewhere on Caracara, then `scp` a representative subset to egret
(same pattern used for the `samaya` pair). We'll diff each against the CST Reader
output above; any mismatch is a bug in either the port or this table, and each
confirmed case can graduate into `DictionaryOracleTests`.
