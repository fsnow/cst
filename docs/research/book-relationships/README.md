# Book relationships: model and measurements (epic #422)

**Status:** research, started 2026-10-09. No code is to change until the model has been tested against the
corpus. When the model settles, its design moves to `docs/features/planned/`; the measurements stay here
as its evidence.

**Provenance.** Each load-bearing statement is marked:
- **[fsnow]**: the maintainer's decision, quoted;
- **[observed]**: a measurement or a fact from code, with the script or file that shows it;
- **[suggestion]**: an agent's reasoning. It is advisory and not a rule.

Unmarked text is context.

Related documents:
- [markup-conventions.md](markup-conventions.md): the 2007–08 structural markup, described from the files themselves.
- [../STRUCTURAL_MARKUP_INVENTORY.md](../STRUCTURAL_MARKUP_INVENTORY.md): the July 2026 per-file `<div>` coverage table. Its layer column comes from the file extension, which misclassifies .nrf files (see [Canonical and añña](#canonical-and-añña)).
- [scripts/](scripts/): the measurements. Each number below gives the script that reproduces it.

## Decisions so far

All from 2026-10-09 unless dated otherwise.

- **Goal.** [fsnow] "My goal in the short term is to come up with a model for representing the complexity of book relationships", "then test that model against the reality before we make any code changes".
- **Correlation is the guiding principle.** [fsnow, 2026-10-11] "Any work that has a related commentary has some system to correlate positions between the work and its commentary. At least through the 4 nikayas that is a monotonic paragraph number. We know that in other pitakas and in Khuddaka nikaya there may be a different way that they correlate. We need to determine how this works in every case."
- **The DN1 Abhinavaṭīkā.** [fsnow, 2026-10-11] "we will also include the DN1 Abhinavaṭīkā in linking, maybe with a "Tika 2" button (eventually)." This is `s0104t` + `s0105t`, which are `// unlinked` in `Books.cs` today.
- **Top-level division.** [fsnow] "we need to make a major division of the 217 files between canonical (61 + 47 + 41) and non-canonical / añña (68)".
- **Paragraph navigation.** [fsnow] "we need to assess which books don't have a consecutive paragraph numbering system and remove that from navigation (Go To and status bar)".
- **Visuddhimagga.** [fsnow] "the Visuddhimagga is an interesting case in Añña because it has its own consecutive para numbering and linking between the main text and the mahatika. It is an isolated microcosm of the canonical mul/attha/tika linking".
- **Abhidhammatthasaṅgaha.** [fsnow, 2026-10-12] "This may be a case where we want to treat it as an isolated pair, like the Visuddhimagga". He said "may be", so this is not settled. The pair is the Saṅgaha and the Vibhāvinī, both in `abh07t`. [observed] It differs from the Visuddhimagga in two ways: both works are in one file, and the locator needs the pariccheda (see Findings).
- **Abhidhammamātikā.** Asked whether `abh09t` should also be treated as an isolated pair, [fsnow, 2026-10-12] "yes, let's start with that for abbh09t". The pair is the Mātikāpāḷi and the Mohavicchedanī. Unlike `abh07t`, they correlate by section only, with no paragraph numbers. "Start with" leaves room to change this. The Mohavicchedanī's chapters also draw on the seven canonical books and their aṭṭhakathās (see Findings), and that is not modelled for now.
- **Khuddasikkhā-Mūlasikkhā.** I proposed treating `vin13t` as an isolated set. [fsnow, 2026-10-12] "yes, agree about vin13t." It is two roots and three ṭīkās in one file, each correlating with its own root by plain ¶ (see Findings).
- **The structural markup.**
  - [fsnow] "The structural markup that is there was done by me in 2007 - 2008, manually, and stopped arbitrarily when I ran out of time to develop CST4. The app's downloaded copy *is* the master and I have no work on markup in progress."
  - [fsnow] "This would be the time that we prioritize that work".
- **Opening sections and the chapter list.** [fsnow, 2026-10-10] "this was a choice, since the top entry in the chapter list also takes you to the top". He said this about `dn1_0` in `s0101t`, a div with no `<head>` that is missing from the chapter list. Five of the corpus's seven opening sections *do* have a `<head>` and are listed (see [markup-conventions.md](markup-conventions.md#opening-sections)), so which treatment is the convention is still open.
- **The six goals of #422.** They are set out in the epic's comments of 2026-07-31; fsnow confirmed on 2026-10-09 that they are his. In short:
  1. book linking;
  2. anchors unique within a file;
  3. Go To handles the citation styles people use, with DPPN as the test corpus (#486);
  4. View Source PDF correct;
  5. displayed numbering systems correct;
  6. chapter lists where relevant.

  The epic's comments are the authoritative wording.
- **Corpus XML changes.** These go through Brihas (VRI). See #452/#453.
- **Scope of the corpus.** [fsnow, 2026-10-10] "for our purposes here we only care about “deva master”". `deva master/` in `VipassanaTech/tipitaka-xml` is what CST Reader downloads (`Settings.XmlRepositoryPath`) [observed]. The 14 script folders beside it don't matter here.
- **Other consumers of the markup.** [fsnow, 2026-10-10] "Since the master files have a mix of markup styles, with div, head, trailer, or only p, so I assume that everything they have written handles both cases." This is his assumption about VRI's tipitaka.org code, which Len Aye recently rewrote. It has not been verified.

### Background: how the XML became the only master
These are fsnow's account (2026-10-10), not measurements:
- The XML used to have a parallel **text version**. That was essentially the original VRI-typed files, converted by fsnow in early 2007 from the Aalekh word-processor encoding to Devanagari Unicode. `aalekh2uni` in `VipassanaTech/tipitaka-xml`'s `conversion scripts/` is **not** that converter. [fsnow, 2026-10-12] "aalekh2uni.dpr is not my converter. It is one that was written by someone before me and was known to be wrong."
- VRI made each correction in both versions.
- One of Len Aye's goals for the site rewrite was to retire the text version, so that the XML would be the single master copy.


## The model (draft)

[suggestion] This is the candidate the measurements below are meant to test. None of it is decided.

| Entity | What it is | Where its data would come from |
|---|---|---|
| **File** | One of the 217 XML files | `Books.cs` |
| **Work** | An editorial text, independent of how it is split into files | The structural markup (`<div type="book">`) where it exists; curated elsewhere |
| **Volume** | A print volume, per edition | `<pb ed n="vol.page">` |
| **Numbering system** | Paragraph numbers, and page numbers per edition (V/M/P/T) | `<p n>`, `<pb ed>` |
| **Section** | A sutta, vagga, etc. inside a work | The structural markup |
| **Locator** | A paragraph number or page number, qualified as far as needed to be unique in its file | Derived |
| **Source segment** | A run of pages in one scanned PDF | `Sources.cs` |

Edges:
- **contains**: File ⊃ Work, Work ⊃ Section, and Work spans File (volume splits).
- **comments-on**: from a commentary's work or section to the text it comments on, through a **correlation system**: in the four nikāyas, a monotonic paragraph number shared by both texts [fsnow]; elsewhere, to be determined case by case (see "Correlation systems" under Findings).
- **scanned-in**: from a page to a source segment.
- **cited-as**: from a citation form, such as a DPPN PTS reference, to a locator.

Invariant (goal 2): **every locator the app offers resolves to exactly one place in its file.** Where paragraph numbers repeat, they need a section qualifier, which only the markup can supply. Otherwise they are withdrawn from navigation.

Proposed order for testing [suggestion]:
1. The Visuddhimagga, a closed añña case.
2. DN/MN/SN as the control.
3. AN, where volume splits create a work-level relationship.
4. Abhidhamma (5 works → 1 commentary).
5. Vinaya.
6. KN.

## Findings

### Canonical and añña
[observed] The `Matn` field in `Books.cs` gives Mūla 61, Aṭṭhakathā 47, Ṭīkā 41, Other 68. Other is exactly the 68 `e*` files.
- The file extension does **not** follow this division. 21 canonical files are `.nrf`: for example `s0104t`/`s0105t` are DN ṭīkās, and `s0518m`/`s0520m` are mūla.
- `scripts/corpus.py` therefore classifies by `Matn` and never by extension.

### Paragraph numbering, per file
[observed] `scripts/paranum.py`, measured 2026-10-10:

| Group | Files | consecutive | monotonic, with gaps | monotonic, with typos | restarting | no numbers |
|---|---:|---:|---:|---:|---:|---:|
| Canonical | 149 | 42 | 40 | 3 | 64 | 0 |
| — Mūla | 61 | 33 | 5 | 2 | 21 | 0 |
| — Aṭṭhakathā | 47 | 7 | 22 | 1 | 17 | 0 |
| — Ṭīkā | 41 | 2 | 13 | 0 | 26 | 0 |
| Añña | 68 | 15 | 7 | 4 | 27 | 15 |

Notes:
- Numbers are compared within each `<div type="book">` segment. A file without markup is one segment.
- "Gaps" in commentaries are expected, because a commentary comments only on some of the mūla's paragraphs.

[observed] `scripts/restarts.py` prints every restart. The restarts fall into groups:
1. **Introduction prefix.** A short introduction is numbered separately, then the main sequence starts at 1 (e.g. `s0506a` 7→1, `s0507a` 3→1, `abh02t` 1030→1).
2. **One file, several works or volumes.** For example, `vin02t` restarts 1228→1, 477→1, 457→1, and `vin07t` and `vin12t` repeat the same pattern. In `abh06t` the page numbers run backwards at the restarts (#452). `s0512m`/`s0512a` both go 143→1, 91→1.
3. **Numbering per section.** In most of KN and Abhidhamma, numbers restart at every vagga or chapter (`s0510m1` has 41 restarts, `abh03m10` 20).
4. **Commentaries following a restarting mūla.** `s0510a`, `s0511a` and `abh03a` restart where their mūla does.

[observed] **Where a mūla is marked up, its restarts fall on div boundaries.** In 8 marked mūla files (`s0501m`, `s0510m1`, `s0510m2`, `s0511m`, `s0512m`, `s0513m`, `s0514m`, `s0516m`), every restart comes right after a `<div>` opens. In `s0517m`, `s0519m` and `s0518m`, some do. No unmarked file has any marked boundary to fall on.

[suggestion] Two consequences follow:
- **Paragraph numbers restarting per section are not a defect.** A section-qualified locator (e.g. `kn10_3` ¶ 5) is unique. So whether a book keeps paragraph navigation may depend on its markup, not only on its numbering.
- **"Consecutive" should be tested per work segment, not per file.** A file that holds several works, each consecutive, is consecutive where it matters.

Which books lose paragraph navigation is fsnow's decision. This table is the input.

### The structural markup
[observed] `scripts/markup_profile.py`, 2026-10-10. Full details are in [markup-conventions.md](markup-conventions.md).

| State | Files |
|---|---:|
| marked: inner divs, with `<head>` and `<trailer>` converted | 73 |
| book-only: `<div type="book">` only, with inner headings still `<p>` (`s0401t`–`s0404t`, the four AN ṭīkās) | 4 |
| partial: headings and colophons converted, inner divs missing (`s0404a`, AN9–11) | 1 |
| unmarked | 139 |

- No unmarked file contains a `<head>` or a `<trailer>`.
- **The divs are uniform, but what goes inside them varies** (`consistency.py`). Eleven MN/SN commentary files close sections with `<p>` colophons instead of `<trailer>`. Opening sections are handled two ways. Closing matter sits sometimes inside the book div and sometimes after it. See [markup-conventions.md](markup-conventions.md#where-the-conventions-differ-between-files).
- `markup_profile.py`'s id check found three slips in the 2007 ids: two duplicate ids and one `n` that differs from its `id`. They are listed in [markup-conventions.md](markup-conventions.md#slips-in-the-existing-markup).
- The four book-only files are exactly the "AN ṭīkā, 4 books" that #422's goal 6 ranks first. Their books are delimited, but there's no chapter list below the book level.

### Correlation systems

How each commentary's positions correlate with the text it comments on, measured per the guiding principle above. Scripts: `correlate.py`, `content.py`, `drift.py`, `pairs.py` and `sections.py`.

**Method** [observed]. Paragraph numbers alone can't show a correlation: any increasing run of numbers is a subsequence of a long enough base, so a monotonic commentary "fits" any base (`correlate.py` scored s0104t at 100% against an unrelated DN+MN control). The evidence is content. A commentary block numbered *n* explains words from base block *n* and marks them `<hi rend="bold">`. `content.py` looks those lemmas up in the base block with the same number (**hit**). It looks them up again in block *n*+5 (**control**). A real correlation shows hit far above control.

**Case endings** [fsnow, 2026-10-11]: "sometimes the case ending differs from the LHS and the RHS bold words. This would cause a mismatch in your testing, most likely." [observed] It does. Exact matching misses those lemmas. Those misses lower hit and control alike, so **the figures below, all from exact matching, are lower bounds.** `content.py --stem` drops each lemma's last two characters and matches the stem instead.

| Pair | Exact hit / control | Stem hit / control |
|---|---|---|
| `s0101a` → `s0101m` | 92/17 | 98/22 |
| `s0514a3` → `s0514m` (Jātaka) | 69/3 | 88/4 |
| `vin02a1` → `vin02m1` | 72/9 | 94/16 |
| `abh02t` → `abh02a` | 63/7 | 72/10 |
| `vin11t` → `vin10t` | 81/6 | 90/6 |
| `abh08t` → `abh06t` | 85/15 | 93/15 |
| `s0505a` → `s0505m` (the numbering slip) | 43/5 | 46/6 |

No conclusion changes. The lower Jātaka and Vinaya figures were mostly endings.

**The four nikāyas** [observed]. It is the paragraph number, as fsnow said.
- Aṭṭhakathā → mūla: hits of 85–96%, against controls of 10–26%.
- Ṭīkā → aṭṭhakathā: 85–94% (s0101t, s0201t, s0401t, s0402t). A ṭīkā explains the aṭṭhakathā's words, so it scores lower against the mūla (33–83%). It reaches the mūla through the shared number.

**Vinaya** [observed]. Also the paragraph number. Aṭṭhakathā → mūla: vin01a 81%, vin02a1–a4 72–85%. Ṭīkā → aṭṭhakathā: vin01t2 92%, vin02t 66% (controls 7–15%).

**Abhidhamma** [observed]. Also the paragraph number, but in the Pañcapakaraṇa the locator is **(section, ¶)**.
- In abh03m the numbers restart for each section, such as each yamaka and each Paṭṭhāna section. The aṭṭhakathā restarts with them: Yamaka 1–99, 1–211, 1–254, 1–19, 1–170 on both sides.
- No Abhidhamma file has markup, so nothing names the section today.
- `abh02t` holds two works, the Vibhaṅga mūlaṭīkā and the anuṭīkā. Each runs ¶ 1–1030 against the Vibhaṅga's numbers.
- Ṭīkā → aṭṭhakathā: abh01t 82%, abh02t 63%, abh03t 73%.

**Khuddaka** [observed]. The aṭṭhakathās correlate through the mūla's numbers, but not always in a single pass.
- **Vimānavatthu and Petavatthu** (`s0506a`, `s0507a`): 89% hit, 2–4% control. For each story, the commentary first quotes the verses under the mūla's numbers (`hangnum`). It then explains them in body paragraphs that start again from the story's first verse number. So each number is visited twice, and the file is not monotonic.
- **Jātaka** (`s0513a*`, `s0514a*`): 69–92%.
- **Suttanipāta** (`s0505a`): 43% overall, against a 5% control, because of **a numbering slip in the mūla**.
  - `s0505m` numbers ¶274 twice. These are the Sūcilomasutta's verses, numbered 273, 274, 274, 275. The second 274 ("स्नेहजा अत्तसम्भूता …", Myanmar page 0.0321) should be 275.
  - From there to the end of the book, each mūla number is one lower than the number the commentary uses for the same verse. That is 882 paragraphs. There is no later jump or duplicate that brings them back into step.
  - With that offset, the commentary hits 24–30 of every 30 blocks (`drift.py`). Before ¶274 it hits 94% unshifted.
  - Both files end at ¶1156 only by coincidence. The last verse ("असंहीरं असङ्कुप्पं …") is mūla ¶1155 and commentary ¶1156. The mūla's ¶1156 is the closing summary verse naming the suttas, which the commentary does not number. Correctly numbered, the mūla would end at ¶1157.
  - The fix is one renumbering in `s0505m`: the second 274 becomes 275, and every later number goes up by one.
  - **Reported** [fsnow, 2026-10-12]: "I sent a short email to help@Tipitaka.org about the SN para numbering issue."
  - **Workaround, conditional on the reader's copy** [fsnow, 2026-10-12]: "in our implementation we will want to have a check where we look for the max para number in Suttanipata and only workaround the issue if it isn't fixed yet (in that user's text)."
    - [observed] Today the mūla's highest number is 1156. Correctly numbered, it would be 1157.
    - [suggestion] The workaround maps commentary ¶*n* ≥ 274 to mūla ¶*n*−1.
    - [fsnow, 2026-10-12] "Let's assume that they will fix the issue correctly by renumbering all after 274". So the check is max ¶ = 1156: if it is, the copy is unfixed and the workaround applies.
- **Udāna** (`s0503a`): confirmed. The *n*+5 control hits 89%, because each mūla paragraph is a whole sutta full of stock phrases, so `content.py --best` asks which base paragraph shares the most lemmas. For all 80 commentary paragraphs it is the one with the same number, where chance is 1.2%.
- A commentary can quote another text with that text's own numbers. `s0503a` quotes verses numbered from 1, which reads as a restart.

**Commentaries Books.cs leaves unlinked** [observed]. Several correlate clearly.

| Commentary | Correlates with | Hit / control |
|---|---|---|
| `s0104t` + `s0105t`, DN1 Abhinavaṭīkā (¶ 1–149, 150–559) | `s0101a` | 100/32, 95/20 |
| `s0519t` Netti-ṭīkā | `s0519a` | 91/19 |
| `abh01t` Dhammasaṅgaṇī mūlaṭīkā | `abh01a` | 82/10 |
| `abh04t` Dhammasaṅgaṇī anuṭīkā | `abh01t`, a ṭīkā on a ṭīkā | 90/15 |
| `abh05t` Pañcapakaraṇa anuṭīkā | `abh03t` | 86/32 |
| `vin06t` Vajirabuddhi-ṭīkā | `vin01a` + `vin02a1–4` | 59/9 |
| `vin07t` Vimativinodanī-ṭīkā | `vin01a` + `vin02a1–4` | 72/11 |
| `s0501t` Nettivibhāvinī | `s0519m` | 88/9 with lemmas of 10+ letters; 4–6 at *n*±20 |

**Commentary pairs outside the Books.cs links** [observed; confirmed by fsnow 2026-10-11, "I went through all 5 cases"]. Each pair has its own numbering, shared with its commentary.

| Commentary → base | Kind | Hit / control |
|---|---|---|
| `e0103n` → `e0101n`, `e0104n` → `e0102n` (Visuddhimagga) | añña | 96/24, 94/15 |
| `e0812n` → `e0811n` (Subodhālaṅkāra) | añña | 58/0 † |
| `vin11t` → `vin10t` (Vinayavinicchaya) | ṭīkā on a ṭīkā-collection work | 81/6 |
| `abh08t` → `abh06t` (Abhidhammāvatāra) | ṭīkā on a ṭīkā-collection work | 85/15 |
| `vin08t` → `vin05t` (Vinayasaṅgaha) | ṭīkā on a ṭīkā-collection work | 91/20 |

- **The last three are not añña.** [fsnow, 2026-10-11] "Of course 2, 3 and 4 above are really a tika and *its* tika*, just like the Abhinavatika in DN1." `Books.cs` files all six of their files as `Tika`, on the canonical side [observed].
- **The DN1 Abhinavaṭīkā reads both texts** [observed]. Some of its lemmas occur in only one of `s0101a` and `s0101t`. About 2,180 of those are aṭṭhakathā-only and about 1,560 are ṭīkā-only. So it comments on the old ṭīkā, as fsnow says, and also directly on the aṭṭhakathā.
- **The Subodhālaṅkāra-ṭīkā (`e0812n`) interleaves three texts, verse by verse.**
  - [fsnow, 2026-10-11] "In the subtika, first the para number is used to mark a complete duplication/quoting of the passage in the tika, then the para number recurs one or more times as it is explained and quoted word by word."
  - [observed] 365 of the base's 367 verses are quoted in full in a `hangnum` paragraph numbered *n*. For 341 of the 369 runs, two complete `bodytext` explanations follow, each also numbered *n*.
  - [observed] Every chapter closes with three colophons in a row: one for the root text ("इति सङ्घरक्खितमहासामिपादविरचिते सुबोधालङ्कारे …"), one for the ṭīkā called Mahāsāmi ("इति सुबोधालङ्कारे महासामिनामिकाटीकायं …") and one for the Nissaya ("इति सुबोधालङ्कारनिस्सये …").
  - [suggestion] So the two explanations are probably the Mahāsāmi-ṭīkā and the Nissaya, in that order. That rests on the colophons' order matching the paragraphs' order, and has not been confirmed. `pairs.py` cannot tell them apart, because both carry the base's numbers.

**The Kaṅkhāvitaraṇī family correlates by section alone** [observed, 2026-10-11].
- `vin04t` (Dvemātikāpāḷi) holds three works:
  - the Bhikkhupātimokkha, numbered ¶1–92;
  - the Bhikkhunīpātimokkha, numbered ¶1–166;
  - the Kaṅkhāvitaraṇī-aṭṭhakathā, which has **no paragraph numbers**.
- `vin09t` holds two ṭīkās on it, the purāṇaṭīkā and the abhinavaṭīkā, also unnumbered. Its only 18 numbers are the verses of the closing nigamana, which is why the paragraph test scored 0%.
- All three works are organised by rule. The headings are numbered within (part, kaṇḍa, vagga), e.g. "१. सुक्कविस्सट्ठिसिक्खापदवण्णना".
- Matched on that key, the purāṇaṭīkā's 89 rule sections all find the aṭṭhakathā section with the same heading, in order. The abhinavaṭīkā's sections find it in 206 of 219 cases. The heading differences seen are spellings of the same rule (पठमअनियत / पठमानियत).
- The purāṇaṭīkā skips rules: in the Saṅghādisesa it goes 9, 11.
- These counts come from a rough heading parser (no script yet); treat them as approximate.
- So here the section is the whole locator, not a qualifier on a paragraph number. No Vinaya file has markup, so the section exists only as heading text.
- How the Kaṅkhāvitaraṇī's rule sections correspond to the Pātimokkha's ¶ numbers is not yet measured.

**The Abhidhammamātikā (`abh09t`) is a base and its commentary in one file, correlated by section** [observed, 2026-10-11].
- The file holds two works. The first is the Abhidhammamātikāpāḷi (`book`, about 134K characters). The second is its commentary, the Mohavicchedanī (`book`, about 730K characters).
- **Neither work has paragraph numbers.** The file's only 29 numbers are the Mohavicchedanī's 10 opening verses and the 19 verses of its nigamana.
- Both works have the same seven `chapter`s, one per Abhidhamma book, in the same order: Dhammasaṅgaṇī, Vibhaṅga, Dhātukathā, Puggalapaññatti, Kathāvatthu, Yamaka, Paṭṭhāna.
- Inside a chapter, the commentary's headings name the base's sections, e.g. "सतिपट्ठानविभङ्गो" ↔ "सतिपट्ठानविभङ्गमातिकत्थवण्णना". One differs in name: Paṭiccasamuppādavibhaṅgo ↔ Paccayākāravibhaṅgamātikatthavaṇṇanā.
- Lemma test (stemmed, per heading section, scratch script, not saved): in the Vibhaṅga chapter, 11 of the commentary's 18 sections score highest on the base section of the same name. The misses are short sections.
- **The Mohavicchedanī also reads the canonical books and their aṭṭhakathās.** Per chapter, its lemmas occur more often in the matching canonical book than in the mātikā. Every chapter's best mūla file is the right book: Dhammasaṅgaṇī 57% in the mātikā vs 73% in `abh01m`; Puggalapaññatti 7% vs 76% in `abh03m2`; Kathāvatthu 25% vs 86% in `abh03m3`.
  - The aṭṭhakathās score higher still (`abh01a` 80%, `abh03a` 81–95%).
  - These figures have no control, and the canonical files are many times the size of the mātikā. So they show which book a chapter is about, not paragraph-level correlation.
- fsnow has decided to start by treating it as an isolated pair (see Decisions). Whether the commentary's links to the canonical books get modelled later is open.

**The Abhidhammatthasaṅgaha (`abh07t`) is also a base and its commentary in one file, correlated by section-qualified paragraph number** [observed, 2026-10-12].
- [fsnow, 2026-10-12] "My research says that Abhidhammatthasaṅgaha is a *summary* of the Abhidhamma, not in traditional commentary form where it parallels a higher-level text and quotes it."
- The file holds two works: the Abhidhammatthasaṅgaha (`book`) and its ṭīkā, the Abhidhammatthavibhāvinī (`book`).
- Both have the same nine paricchedas as `chapter`s, and paragraph numbers restart at 1 in each pariccheda in both works.
- Lemma test, keyed (pariccheda, ¶): with stemmed lemmas, the Vibhāvinī's block matches the Saṅgaha block with the same key in 205 of 245 blocks, against 7 at ¶+5. With exact lemmas the figures are 176 of 265 against 3. This was a scratch script, not saved.
- So the Saṅgaha is a root text in the sense that matters here.

**Khuddasikkhā-Mūlasikkhā (`vin13t`): five works in one file, three commentary pairs, by plain paragraph number** [observed, 2026-10-12].
- The file's five works are `chapter`s under one `book`. Numbering restarts at each work and runs straight through inside it:

  | Work | ¶ | Numbered blocks |
  |---|---|---|
  | Khuddasikkhā (verse) | 1–476 | 476 |
  | Khuddasikkhā-purāṇaṭīkā | 1–473 | 374 |
  | Khuddasikkhā-abhinavaṭīkā | 1–475 | 389 |
  | Mūlasikkhā | 1–128 | 128 |
  | Mūlasikkhā-ṭīkā | 1–116 | 93 |

- Lemma test, same ¶ in the base vs the control at ¶+5. This was a scratch script, not saved:

  | Commentary → base | Exact | Stemmed |
  |---|---|---|
  | purāṇaṭīkā → Khuddasikkhā | 141/260 vs 0 | 173/232 vs 0 |
  | abhinavaṭīkā → Khuddasikkhā | 163/335 vs 0 | 235/305 vs 1 |
  | Mūlasikkhā-ṭīkā → Mūlasikkhā | 50/68 vs 0 | 50/61 vs 0 |
  | abhinavaṭīkā → purāṇaṭīkā | 173/317 vs 1 | 204/290 vs 3 |
  | Mūlasikkhā-ṭīkā → Khuddasikkhā (wrong base, as a check) | 0/68 | 1/61 |

- The two Khuddasikkhā ṭīkās are parallel commentaries on the same verses, so each also correlates with the other through the shared number. This is the same effect seen with the Vinaya ṭīkās.
- The two root texts are themselves too poor in bold lemmas to test against the canonical Vinaya: 66 in the Khuddasikkhā, none in the Mūlasikkhā. Whether they correlate with anything above them is not measured.
- Treated as an isolated set, like the Visuddhimagga (see Decisions). There are two roots, one with two ṭīkās and one with one, all in a single file. Each work's own ¶ is enough within that work, but a locator in the file needs the work as well. It has a commentary that correlates with it, but it does not itself correlate with the Abhidhamma texts it summarises.

**Blind search** [observed]. `pairs.py` tests every file with bold lemmas against every other file. It uses lemmas of 10+ letters and a control at *n*+20, and keeps pairs with a hit of 50% or more, a control of 15% or less, and at least 20 blocks; figures marked † come from it. Across the whole corpus it returns 92 pairs. Every one is either a known link, a pair above, or one of these:
- **`vin12t` Pācityādiyojanā** ↔ `vin02a2`–`a4` (53–65/0). A yojanā construes the aṭṭhakathā, so it is keyed to the aṭṭhakathā's numbers.
- **Parallel ṭīkās correlate with each other** through the shared number. The Vimativinodanī (`vin07t`) correlates with the Sāratthadīpanī (`vin01t1`, `vin01t2`, `vin02t`: 60–87/0–4), and the Abhinavaṭīkā with `s0101t` (82–86/0).

**Numbering slips** [observed]. `drift.py` measures each window of 30 blocks with the base number shifted by −3…+3. It reports a window where a shift beats the unshifted number by 10 or more hits. Across every aṭṭhakathā → mūla pair, the 20 ṭīkā → aṭṭhakathā pairs and the candidates above, it finds only the Suttanipāta.

**One correlation system, sometimes section-qualified.** [fsnow, 2026-10-11] In the other piṭakas and the Khuddaka, the "different way that they correlate" is that "With some books it needs a section added". So every commentary pair measured here correlates by paragraph number, qualified by a section where the numbers restart. [observed] The Pañcapakaraṇa is the confirmed case. `sections.py` checks the other pairs whose numbers restart [observed]. It splits base and commentary into runs between restarts, and finds the best-matching base section for each commentary section.
- **Aṭṭhakathā → mūla:** every such pair lines up section for section, in order. That covers AN, Khuddakapāṭha, Apadāna, Buddhavaṃsa, Cariyāpiṭaka, Jātaka, Niddesa, Paṭisambhidāmagga, Netti, Dhammasaṅgaṇī and the Pañcapakaraṇa. Matched blocks are 64–97%.
- **Ṭīkā → aṭṭhakathā:** these line up the same way (AN, Vinaya, Netti, Dhammasaṅgaṇī, the Pañcapakaraṇa ṭīkā and anuṭīkā), at 64–90%.
- The two pairs flagged out of order (`s0511a`, the Pañcapakaraṇa ṭīkās) are short sections of 2–5 blocks, where one or two hits chose the wrong section. The long sections are all in order.
- **A commentary can skip a section of its base.** `abh03t` has no section for two of `abh03a`'s Yamaka sections. Counting sections stops pairing them correctly from that point, so the section part of a locator needs the section's identity, from markup or headings, not its ordinal. No Abhidhamma file has markup today.

**Coverage** [observed, 2026-10-11].
- **150 of 217 files** are linked or correlated with another file. 126 are linked in `Books.cs` today, and 24 more are correlated by measurement, the Kaṅkhāvitaraṇī family (`vin04t`, `vin09t`) among them.
- **67 remain unlinked:** 62 añña files and 5 on the canonical side. The five are Milindapañha (`s0518m`) and Peṭakopadesa (`s0520m`), which have no commentary in the corpus, then Khuddasikkhā-Mūlasikkhā (`vin13t`), Abhidhammatthasaṅgaha (`abh07t`) and Abhidhammamātikā (`abh09t`).
- `abh07t`, `abh09t` and `vin13t` are linked to nothing in `Books.cs`, but each holds bases and commentaries that correlate inside the file (above). None of the three is a file with no relationships. The only canonical-side files with no correlation at all are `s0518m` and `s0520m`, which have no commentary in the corpus.

**What this means for the model** [suggestion].
- A base can have more than one commentary at the same level, as with the DN1 Abhinavaṭīkā.
- A commentary can comment on a commentary: the anuṭīkās, and the three ṭīkā-on-ṭīkā pairs above.
- One file can interleave several works under the same numbers, as in `e0812n`.
- One file can hold two works that share a base (abh02t).
- One file can hold a base and its own commentary, so a relationship can run between two works in the same file (`abh07t`, `abh09t`, `vin13t`, and `vin04t`'s Pātimokkhas and Kaṅkhāvitaraṇī).
- A locator may need a section qualifier even where the numbering is right (abh03).
- The single-int `MulaIndex`/`AtthakathaIndex`/`TikaIndex` can express none of these.

### Visuddhimagga
[observed] `scripts/visuddhimagga.py`, 2026-10-10:
- **Main text:** `e0101n` ¶ 1–364, `e0102n` ¶ 365–896.
- **Mahāṭīkā:** `e0103n` ¶ 1–363, `e0104n` ¶ 365–892.
- **Same volume split** in both, at ¶ 364/365.
- **870 of the 871 Mahāṭīkā paragraph numbers exist in the main text.** The exception, ¶ 591, is a typo in `e0102n` (the sequence reads 590, 561, 592).
- **Neither has any markup.** Both are `// unlinked` in `Books.cs`, as they were in CST4.
- `e0105n` (Nidānakathā) is a separate work with numbering that restarts.

[suggestion] This is the smallest complete test of the comments-on edge plus a volume split, and it has no markup to depend on. That makes it a good first case for the model.

### Current encodings the model replaces
[observed] As of 2026-10-10:
- **`Book`** has single-int `MulaIndex`/`AtthakathaIndex`/`TikaIndex`. `BookType` (Whole/Multi/Split) is set on 108 of 217 books.
- **Unlinked books.** 91 entries are `// unlinked`. `TikaIndex = -1` is set on `abh03m3`, `abh03m7` and `abh03m10`; the comment there points to #174.
- **CST4 hard-coded Split-book cases in `OpenLinkedBook`** (`git show cst4-final:src/Cst4/FormBookDisplay.cs`):
  - Theragāthā paragraphs > 266 go to a second file;
  - books 43/100 paragraph > 23 go to book 128;
  - the AN Multi `bookCode` goes to mūla volumes.

  In the Avalonia port (`BookDisplayViewModel`), the Split case is a TODO.
- **Unused navigation code.** `NavigationResolver` and `IAnchorCatalog` (CST.Core/Navigation) exist but are not wired in, and there's no catalog implementation.

## Open questions
Each is fsnow's call unless marked otherwise.

1. Should a **Work** be curated data, or derived from markup wherever markup exists?
2. **Linking granularity:** paragraph, section (`dn1_2` ↔ `dn1_2` across levels), or both?
3. Should the model describe the corpus **as it is**, or as it will be once the markup is resumed?
4. ~~Does any work and commentary correlate by something other than paragraph number?~~ Answered: [fsnow, 2026-10-11] "as you said above, “With some books it needs a section added”. that's what I was referring to." The different way outside the four nikāyas is a section-qualified paragraph number, not a different system.
5. For the paragraph-navigation decision, should "consecutive" be judged **per file or per work segment**?
6. **How should markup be done:** by hand, as in 2007, or proposed by an agent for review? And does structural markup go through Brihas like text corrections (#452), or by another route?
7. Which **section levels** belong in the chapter list? It shows exactly the types in `ChapterListTypes`.
