# Source PDF Pagination — missing pages and two-page spreads

Findings from the #540 investigation (2026-07-31). Covers two separable problems:

1. **Navigation** — pages missing from the scans throw off which PDF page we open. *Fixed* (#540).
2. **Spreads** — recto/verso reverse in two-page view. *Not fixed, and deliberately so* — the defect is
   in our viewer, and correcting the files would break them in standards-respecting viewers.

Both trace to the same fact: **the scans are not page-complete.** The PDFs were produced by a different
group within VRI from the Tipitaka.org team that produced the texts, and blank pages were passed over.

---

## 1. Missing pages (navigation) — FIXED

### The defect

CST4's formula assumed the scan holds every printed page:

```csharp
pdfPage = source.PageStart + (printPage - 1);
```

One skipped page throws off everything after it, and the error **accumulates**. In AN 2-3-4 Aṭṭha
(`s0402a`, `PageStart` 19) blanks at print pages 68 and 248 made Tikanipāta land one page late and
Catukkanipāta two.

### The fix

`Sources.Source.PdfPageFor()` subtracts the skips recorded in a per-PDF `MissingPages` array:

```
pdfPage = PageStart + (printPage - 1) - |{ missing pages < printPage }|
```

### How the seed values were derived

**A blank page leaves two traces, and we hold one of them.** It has no text, so it produces no `<pb>`
marker in the XML *and* nothing for the scanner to capture. Holes in a book's Myanmar page sequence
therefore predict pages absent from the PDF. `s0402a` runs 1–397 with exactly two holes: 68 and 248.

Seeded **45 pages across 22 canonical books**; 16 have a PDF mapped today (21 `addSource` entries once
the paired 2010 editions are counted).

Two categories were excluded deliberately:

| excluded | why |
|---|---|
| `e*` books — 3,344 gap pages | They come in **runs** of consecutive missing markers: unmarked regions, not blank pages. Subtracting them would be badly wrong. No canonical book has a run — every canonical gap is isolated, which is what a single blank leaf looks like. |
| `vin01a` (1.223, 1.329), `vin07t` (2.66, 2.82, 2.270), `vin11t` (2.128, 2.400) | Their Myanmar numbering restarts per volume, so a bare page number is ambiguous until source entries carry volume segments (#546). |

### Caveats

- **The derivation is a correlation, not proof.** An XML gap could be a *dropped marker* rather than a
  blank leaf, in which case we subtract where we shouldn't. That failure looks like a page that was
  right before and is now one *early*.
- **The 2010 arrays are unverified.** They carry the same seed as 1957 on the assumption that both
  scans skipped the same pages — which is exactly what may not hold, given they came from different
  groups. Five books have both editions: `abh03m10`, `abh03m11`, `abh03m4`, `s0201m`, `s0510m2`.
- Arrays are **per PDF, not per book**. Correcting one edition must not be taken to imply the other.
- `PdfPageFor` stops counting at the first entry ≥ the target, so an array must stay ascending. A test
  guards this against hand-edits during QA.

---

## 2. Two-page spreads — NOT fixed, by choice

### The symptom

In two-page view the recto and verso reverse, putting the page numbers in the **gutter** instead of the
fore-edge. Each missing leaf also *flips* parity for the remainder of the volume.

### Pairing conventions are viewer-specific, and opposite

For a book, print page 1 is a recto (right-hand); spreads run (2,3), (4,5) with **even left, odd right**.

| viewer | pairing | correct `PageStart` | our mapped entries |
|---|---|---|---|
| Preview / Acrobat "Two-Up (Cover Page)" | page 1 alone, then (2,3), (4,5) | **odd** | 125 |
| Chrome / CEF (our viewer) | (1,2), (3,4) | **even** | 69 |

Confirmed empirically: `Duka-tika-catukkanipāta-aṭṭhakathā.pdf` (`PageStart` 19, odd) reads **correctly
in Preview** — fore-edge numbers through the TOC and body — until the genuine missing leaf at page 68.

### Chrome's viewer cannot be configured

`pdf/document_layout.cc` hard-codes the pairing on the 0-based page index:

```cpp
if (i % 2 == 0)  page_rect = draw_utils::GetLeftRectForTwoUpView(...);
else             page_rect = draw_utils::GetRightRectForTwoUpView(...);
```

- The only spread settings are `PageSpread::kOneUp` and `kTwoUpOdd`. There is no `kTwoUpEven`.
- **Nothing in the layout path reads the document catalog**, so `/PageLayout /TwoColumnRight` is
  ignored. (Neither VRI PDF sets it in any case — verified by inflating the object streams.)
- The URL fragment supports only `page=` and `nameddest=` (plus partial `zoom=`); there is no spread
  parameter. [crbug 64309](https://issues.chromium.org/issues/40483153) is the standing request.

**The page count is the only thing that changes pairing.**

### Why we are not "fixing" the PDFs

Inserting a leading blank leaf to satisfy our viewer would **break the same file in Preview** — the 125
books that read correctly today would start reading wrong. Chrome is the outlier; deforming the corpus
to compensate for one viewer's non-standard pairing makes the data worse everywhere else.

If in-app spreads ever become a goal, the only safe shape is a **derived copy that the app renders, with
the downloaded original untouched** (the originals are a preservation mechanism). The cheapest technique
is an *incremental update* appending one blank page object plus a new xref — a few KB, no re-encoding of
the 57 MB of scanned images — rather than a full rewrite through `PdfPig`'s builder.

### Front matter

Front matter carries its own numbered sequence (the TOC numbers from `[1]` in square brackets), so
parity must come out right more than once per volume, independently. The general invariant is:

> **Every sequence start — title page, TOC `[1]`, body `1` — must land on a recto.**

Blank-verso insertions adjust the *distances* between sequence starts so that one global parity can
satisfy all of them at once. Nothing we hold predicts where those belong; the XML says nothing about
front matter, so it would be per-file inspection. In practice this is near-moot: under Preview's
convention the sample book needs only a blank before the title page, a cosmetic nit.

Note that any insertion **before** print page 1 shifts `PageStart`, so a patched PDF and the
`Sources.cs` value must move together — an argument for deriving `PageStart` from an insertion list
rather than storing both, should this ever be built.

---

## QA notes

- **Parity transitions localize missing leaves.** Absolute parity says nothing (it depends on the viewer
  and on the front matter), but each missing leaf *flips* it — so the point where numbers jump to the
  gutter marks a missing page. Sequence, not state.
- Books worth exercising: `s0402a` (blanks at 68, 248 — accumulation), `s0404a` (256, 286, 342 — three
  steps), `abh03m11` (11 blanks, the densest), `s0403a` / `s0514a2` / `vin02t` (2 each).
- A wrong page in a book *not* on the seeded list is a different defect — most likely #546 if the status
  bar shows a `2.NNNN` page in one of the twelve volume-restart books.

## Related

- #540 — missing pages (this fix)
- #546 — 12 books whose Myanmar numbering restarts per volume; `MissingPages` should move to a segment
- #76 — one-to-many / many-to-many source model. **Its upstream blocker is resolved**: it was deferred
  waiting on VRI to guarantee page-complete scans, and recording the skips on our side removes that
  dependency.
