# Structural markup conventions (as found in the corpus)

This document describes the `<div>` markup that fsnow added by hand in 2007–08. Everything here is
**[observed]**: read from the downloaded corpus, with a file to check it against. The exceptions are the
[fsnow] decisions, which are quoted, and the [suggestion]s, which are marked. It records what the
existing markup *does*. It is the starting specification for resuming the work, but it is not a set of
rules until fsnow adopts it. Measurements come from `scripts/markup_profile.py`, run 2026-10-10.

> [fsnow, 2026-10-09] "The structural markup that is there was done by me in 2007 - 2008, manually, and
> stopped arbitrarily when I ran out of time to develop CST4. The app's downloaded copy *is* the master
> and I have no work on markup in progress." / "This would be the time that we prioritize that work"

## The pattern

`s0101m.mul.xml` (DN, Sīlakkhandhavagga mūla):

```
<p rend="centre">  namo tassa…                 outside every div
<p rend="nikaya">  दीघनिकायो                   outside every div
<div id="dn1" n="dn1" type="book">
  <head rend="book">सीलक्खन्धवग्गपाळि</head>
  <div id="dn1_1" n="dn1_1" type="sutta">
    <head rend="chapter">१. ब्रह्मजालसुत्तं</head>
    … subheads, numbered bodytext, colophons inside the sutta …
    <trailer rend="centre">ब्रह्मजालसुत्तं निट्ठितं पठमं।</trailer>
  </div>
  … dn1_2 … dn1_13 …
  <p rend="centre">सीलक्खन्धवग्गो निट्ठितो।</p>     the vagga's closing line and uddāna:
  तस्सुद्दानं – + gāthā lines                     inside the book, outside every sutta
  <trailer rend="centre">सीलक्खन्धवग्गपाळि निट्ठिता।</trailer>
</div>
```

The same file without markup would have the same headings and colophons as plain paragraphs:
`<p rend="book">`, `<p rend="chapter">`, `<p rend="centre">`. Compare `s0104t.nrf.xml`/`s0105t.nrf.xml`
(the DN Abhinavaṭīkā, unmarked) with `s0101t.tik.xml` (the Līnatthappakāsanā on the same suttas,
marked).

Marking a section up made three changes:
1. **The section is wrapped in a `<div>`** with `id`, `n` and `type` attributes.
2. **Its heading `<p rend="X">` became `<head rend="X">`.** The `rend` value is kept, and the XSL styles
   `p[@rend=X]` and `head[@rend=X]` identically, so the page looks the same.
3. **Its closing colophon `<p rend="centre">` became `<trailer rend="centre">`**, the last element
   before `</div>`.

Colophons of units that did *not* get a div stay as `<p rend="centre">`. Examples are bhāṇavāra ends
and the sīla sections inside a sutta: `s0101t` has nine of them inside `dn1_1`. `subhead` paragraphs are
never divs.

No unmarked file contains a `<head>` or a `<trailer>`.

## Attributes

- **`id`**: the parent's id + `_` + the section's ordinal (`dn1` ⊃ `dn1_1` … `dn1_13`). The prefix is
  the division and book number (`dn1`, `mn3`, `an8`, `kn10`). Every id in the corpus extends its
  parent's id.
  - The underscore count is the nesting depth. `ChapterListsService` indents the chapter list by it.
  - The same id names the same section at every commentary level: `dn1_2` is the Sāmaññaphala in
    `s0101m`, `s0101a` and `s0101t`.
- **`n`**: repeats `id`.
- **`type`**: the collection's own name for the unit.
  - Depth 0 is always `book` (93).
  - Depth 1: `vagga` 226, `samyutta` 170, `sutta` 104, `chapter` 93, `nipata` 63, `pannasaka` 48,
    `khandaka` 22, `peyyala` 17, `kanda` 9, `vimana` 2, `intro` 1, `subbook` 1.
  - Depth 2: `vagga` 239, `kanda` 7, `peyyala` 1.

  A book's chapter list shows exactly the types named in its `ChapterListTypes` in `Books.cs` (DN:
  `book,sutta`).

## Opening sections

> [fsnow, 2026-10-10] "this was a choice, since the top entry in the chapter list also takes you to the
> top". He said this about `dn1_0` in `s0101t`, a div with no `<head>`, which is missing from the chapter
> list.

There are seven opening sections in the corpus (ganthārambha / nidāna, with ids ending `_0` or with
`type="intro"`). **They are not all handled the same way** (`scripts/consistency.py`):

| File | Div | `<head>`? | In the chapter list? |
|---|---|---|---|
| `s0101t.tik.xml` | `dn1_0` | no (title is `<p rend="subsubhead">`) | no |
| `s0201a.att.xml` | `mn1_0` | no | no |
| `s0101a.att.xml` | `dn1_0` | `<head rend="subsubhead">` | yes |
| `s0301a.att.xml` | `sn1_0` | `<head rend="subsubhead">` | yes |
| `s0401a.att.xml` | `an1_0`, `type="intro"` | `<head rend="subsubhead">` | yes (`intro` is in its `ChapterListTypes`) |
| `s0201t.tik.xml` | `mn1_0` | `<head rend="subsubhead">` | yes |
| `s0301t.tik.xml` | `sn1_0` | `<head rend="subsubhead">` | yes |

The "in the chapter list" column follows from `ChapterListsService`'s rule (listed when the div's type
is in `ChapterListTypes` and it has a `.//head`). It has not been checked in the app. Which of the two
treatments is intended is fsnow's call.

Every opening div keeps the title's `subsubhead` rend, whether it is a `<p>` or a `<head>`.

Either way, the div bounds the section for the app. `BookMarkers` stops AI passage context at it, and
the status bar takes a section's page from it (#542).

## Other divs that do not open with `<head>` (31 in all)

- **27 AN book divs open on the salutation** `<p rend="centre" n="0">`, which comes before the book's
  `<head>`.
  - Examples: `an2`–`an4` in `s0402m1`–`m3`, `s0402a` and `s0402t`, and the same pattern through
    `an11`.
  - In `s0402a`, a div opening exactly at the salutation is what separates the previous book's closing
    attribution (old page) from the new book (new page). `BookDisplayView` relies on this.
- **`kn11` in `s0510m2`** opens on `<p rend="centre">`.
- **`kn18` in `s0518m`** (Milindapañha) has no `<head>` of its own. Its first child is the div
  `kn18_1`, which carries `<head rend="chapter">मिलिन्दपञ्हपाळि</head>`.
- **`dn1_0` in `s0101t` and `mn1_0` in `s0201a`**, the two opening sections without a `<head>`.

## Page turns

In `s0101m`, **every sutta starts a new page in all four editions** (M, V, P, T). At each of the 12
boundaries, the previous sutta's last page number + 1 equals the new sutta's first. Every first page
marker of a sutta comes after the sutta's first word (`१५०. एवं <pb …/> मे सुतं`), so the div is where
the page turn actually is.

[suggestion] Check this on other files before relying on it generally.

## States of a file

| State | Files | What it looks like |
|---|---:|---|
| marked | 73 | Inner divs. Every `<trailer>` closes a div. |
| book-only | 4 | `s0401t`–`s0404t` (the AN ṭīkās). Only `<div type="book">` and `<head rend="book">`; the sections inside are still `<p rend="chapter|title">` (18–59 per file). |
| partial | 1 | `s0404a`. AN8 is fully marked. AN9–11 have their book divs, and their headings and colophons were converted to `<head>`/`<trailer>`, but the vagga and paṇṇāsaka divs were never added. This leaves runs of `</trailer><head rend="chapter">` with no `</div><div>` between them: 22 of the file's 36 trailers close nothing. |
| unmarked | 139 | Plain `<p>` throughout. |

[suggestion] The partial state suggests an order of work: first convert the headings and colophons,
then add the divs. `s0404a` is where that work stopped.

Nine files with divs have no `<trailer>` at all. `s0401t` is book-only. The other eight are among the eleven files whose colophons stayed `<p>` (see [below](#section-colophons-trailer-in-most-files-p-rendcentre-in-eleven)): the colophons are there, and were not converted.

## Where the conventions differ between files

From `scripts/consistency.py` (2026-10-10). The divs themselves are uniform:
- every id extends its parent's;
- `n` repeats `id` (with the one exception below);
- the attributes always come in the same order;
- each type occurs only at its usual depth.

The differences are in the elements inside the divs.

### Section colophons: `<trailer>` in most files, `<p rend="centre">` in eleven
In most marked files, every section div ends in a `<trailer>`. Eleven commentary files instead close
almost every section with a colophon left as `<p rend="centre">`, even though the colophon is the last
element before `</div>`:

| | Files that close sections with `<p>` colophons |
|---|---|
| MN aṭṭhakathā | `s0201a`, `s0202a`, `s0203a` |
| MN ṭīkā | `s0201t`, `s0202t`, `s0203t` |
| SN aṭṭhakathā | `s0302a`, `s0303a` |
| SN ṭīkā | `s0301t`, `s0302t`, `s0303t` |

The neighbouring files use `<trailer>`: `s0301a`, `s0304a`, `s0305a`, `s0304t`, `s0305t`, the DN
aṭṭhakathās and ṭīkās, the AN aṭṭhakathās, and every mūla file. Within the mūla files, the few
sections that end in something other than a trailer are ones whose last element is a verse or a
numbered paragraph. These look like texts with no colophon at that point.

Nothing in the app reads `<trailer>` differently from `<p rend="centre">` today. It matters only as a
convention for the markup work to follow.

Two trailers have `rend="bodytext"` instead of `centre`: `an1_6` in `s0401m` (अच्छरासङ्घातवग्गो छट्ठो।)
and `kn20_5` in `s0520m` (पञ्चमा भूमि।).

### Book-level `<head>` rend
Book divs use `<head rend="book">`, with two exceptions:
- `vin5` in `vin02m4`: `rend="title"` (१. भिक्खुविभङ्गो);
- `vin2_5` in `vin02m1`: a `type="subbook"` div whose head is `rend="book"` (भिक्खुनीविभङ्गो).

### Closing matter of a nikāya or book
Material after the last section sits in different places:
- Inside the book div, after the last section: the DN Sīlakkhandhavagga mūla's closing line and
  uddāna (`s0101m`), and the SN and AN commentaries' final colophons (e.g. `s0305a`, `s0404a`).
- After the book div closes: in `s0103a` and `s0103t`, the whole nigamanakathā (23 and 12 elements),
  plus "दीघनिकायो समत्तो" in `s0103m`, "अङ्गुत्तरनिकायो समत्तो" in `s0404m4`, the Apadāna's closing
  (`s0510m2`), and "दुतियो भागो निट्ठितो" in `s0402t`.

[suggestion] One reading is consistent: colophons for the nikāya or for the whole commentary sit
outside the book, and the book's own closing matter sits inside it. But `s0404a` ends with
"अङ्गुत्तरनिकाय-अट्ठकथा सब्बाकारेन निट्ठिता" as the trailer *inside* `an11`, while the DN nigamana in
`s0103a` is outside. This needs fsnow's reading.

### Text inside a book div but before its first section
- **Heading only**, a `<p rend="title">` naming a group: `s0510m1`, `s0510m2`, `s0513m`, `s0514m`,
  `s0515m`.
- **An introductory gāthā block:** `s0508m`.
- **`s0516m` (Cūḷaniddesa):** 174 numbered verse paragraphs (the Pārāyanavagga text, under
  `<p rend="subsubhead">पारायनवग्गो</p>`) sit in the book div before `kn16_1`
  (पारायनवग्गनिद्देसो). They belong to no section and have no chapter-list entry.

## Slips in the existing markup

These were found by the id check in `scripts/markup_profile.py`:

| File | Div | Problem | Probably meant |
|---|---|---|---|
| `s0510m1.mul.xml` | `kn10_2` (२३. आलम्बणदायकवग्गो) | Duplicates the id of `kn10_2` (२. सीहासनियवग्गो) | `kn10_23` |
| `s0203a.att.xml` | `mn3_4` (५. सळायतनवग्गो) | Duplicates the id of `mn3_4` (४. विभङ्गवग्गो) | `mn3_5` |
| `s0502m.mul.xml` | `kn2_9` (९. पापवग्गो) | `n="kn2_2"` | `n="kn2_9"` |

[suggestion] A duplicate id gives two HTML anchors with the same name, so choosing the second entry in
the chapter list probably lands on the first. This has not been checked in the app. Correcting these
is a corpus change, and so goes through the same route as other XML changes (#452).

## What the app does with the markup

| Consumer | Uses |
|---|---|
| `ChapterListsService` | One chapter-list entry per div whose `type` is in `ChapterListTypes`. Heading text comes from its `.//head`, and the indent from the id's underscore count. A div with no `<head>` is skipped. |
| `tipitaka.xsl` | `<a name="{id}">` for each div. Every numbered paragraph also gets `para{n}_{bookId}` from its enclosing `type="book"` div, which keeps anchors unique when numbers restart per book. |
| `BookDisplayView` (#542) | Div starts are section starts, so the status bar shows a section heading's own page. |
| `BookMarkers` / `TeiPassageReader` / `PassageTool` | The innermost div bounds AI passage context. A file without markup is one section. |
| `BookMarkers.CurrentBook` | The `type="book"` div a paragraph belongs to. |
