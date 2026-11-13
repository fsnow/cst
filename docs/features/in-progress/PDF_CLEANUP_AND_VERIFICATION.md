# PDF Cleanup and Page Marker Verification

**Status**: Planning
**Date**: March 5, 2026

## Goal

Ensure that each Burmese 1957 source PDF has exactly one PDF page per original printed book page, then verify that the Myanmar page markers (`<pb ed="M">`) in the 217 TEI XML book files correspond to the correct pages in the source PDFs.

## Background

### What Are the Source PDFs?

The Chaṭṭha Saṅgāyana Tipiṭaka (CST) texts are published as TEI XML files with embedded page markers referencing multiple printed editions. The Burmese 1957 edition PDFs are scanned/digitized versions of the original printed books, stored on SharePoint and downloaded on demand.

There are currently **156+ XML books mapped** to their corresponding PDFs in `src/CST.Core/Sources.cs`. The PDFs are organized by category:

```
01 - Burmese-CST/
├── 1957 edition/
│   ├── 1 - Mula - Vinaya/        (5 PDFs)
│   ├── 2 - Mula - Sutta/         (21 PDFs — some are combined volumes)
│   ├── 3 - Mula - Abhidhamma/    (7 PDFs)
│   ├── 4 - Atthakatha/           (22 PDFs)
│   └── 5 - Tika_/                (13 PDFs)
├── 2010 edition/
│   └── Mula/                     (40 PDFs)
└── Anya/
    └── 1. Visuddhimagga/          (5 PDFs)
```

### How Page Navigation Works

The XML files contain page break markers like:

```xml
<pb ed="M" n="1.0005"/>
```

Where `ed="M"` indicates the Myanmar/Burmese edition and `n="1.0005"` means volume 1, page 5. The application navigates to the corresponding PDF page using:

```
pdfPage = startPage + (myanmarPage - 1)
```

Each XML-to-PDF mapping in `Sources.cs` stores a `startPage` value — the PDF page number where Myanmar page 1 begins. This offset accounts for front matter (title pages, prefaces, table of contents) that precedes the actual text.

### The Problem

The source PDFs have inconsistencies that break the simple page formula:

1. **Blank pages removed**: Some PDFs have had blank pages stripped out, so the PDF page count no longer matches the original printed page count. This means `startPage` must decrease for each book within a combined PDF to compensate — a fragile workaround.

2. **Duplicate pages**: At least one PDF (the KN combined volume containing Khuddakapāṭha through Suttanipāta) repeats a run of pages: after print 219 it starts over at print 120, adding 99 PDF pages and shifting all subsequent page numbers.

3. **Combined PDFs spanning multiple books**: Several PDFs contain multiple logical books end-to-end with continuous Myanmar page numbering. For example, one PDF contains Khuddakapāṭha, Dhammapada, Udāna, Itivuttaka, and Suttanipāta. Each book has its own `startPage` in `Sources.cs`.

4. **Two zero-byte PDFs**: Dhammasaṅgaṇī and Yamaka-3 in the 1957 Abhidhamma folder are 0 bytes.

## Project Scope

### Phase 1: PDF Surgery

For each source PDF, ensure a 1:1 correspondence between PDF pages and original printed book pages:

- **Remove duplicate pages** (e.g., the 99 duplicates in the KN combined PDF)
- **Restore blank pages** where they existed in the original print. This makes the formula `pdfPage = startPage + (myanmarPage - 1)` work with a consistent `startPage` across all books in a combined PDF, because the page offset never drifts.
- **Document the changes** made to each PDF

After surgery, the `startPage` values in `Sources.cs` will need to be recalculated and updated.

### Phase 2: Page Marker Verification

Systematically verify that each Myanmar page marker in the XML files points to the correct page in the corresponding PDF:

- For each mapped XML file, extract all `<pb ed="M" n="..."/>` markers
- Calculate the expected PDF page using the formula
- Extract or render that PDF page and verify the Myanmar page number appears on it
- Report any mismatches

This could be done by:
- Extracting page images from PDFs and visually/OCR checking Myanmar numerals
- Spot-checking a sample of pages per book (first, last, and a few in the middle)
- Comparing total Myanmar page count in XML against total content pages in PDF

## Key Files

| File | Purpose |
|------|---------|
| `src/CST.Core/Sources.cs` | All XML-to-PDF mappings with `startPage` values |
| `src/CST.Core/SourceMappings.md` | Human-readable documentation of all mappings |
| XML books directory | `~/Library/Application Support/CSTReader/xml/` (217 files) |
| Local PDF cache | `~/Library/Application Support/CSTReader/pdfs/` |

## Known Issues to Address

| Issue | PDF | Details |
|-------|-----|---------|
| 99 duplicate pages, a mislabelled page, and the end of the Itivuttaka missing | KN combined 1957 (Khuddakapāṭha–Suttanipāta) | Checked by hand 2026-11-13. After print 219 (PDF 241) the scan starts again at print 120 (PDF 242); print 219 recurs at PDF 340 and print 220 is PDF 341, so the repeat is 99 PDF pages. Print 275 is labelled "279". Print 276–277 (the end of the Itivuttaka, from ¶112) and the blank 278 are not in the scan. Recorded as `ExtraPagesAfter` (99 × 219) and `MissingPages` `{ 276, 277, 278 }` for `s0504m` and `s0505m`, both now at `startPage` 23; `s0505m`'s old workaround of 117 opened the Suttanipāta two pages early. [suggestion] Remove PDF 242–340 and insert the two missing pages |
| Blank pages removed | Multiple PDFs | Causes decreasing `startPage` pattern across books in combined PDFs |
| Zero-byte files | Dhammasaṅgaṇī, Yamaka-3 | 0 bytes in SharePoint; need replacement |
| Text pages missing from the scan | Netti-Peṭakopadesa 1957 (`s0519m`) | Print pages 156–157 are not in the PDF and are not blank. `MissingPages` `{ 156, 157 }` keeps later pages right (#1101); `s0520m` shares the PDF and its `startPage` 5 already absorbs the gap. [fsnow: VRI "probably have the physical book but not a better scan"] [suggestion] Scan the two pages from the volume and insert them; then `s0519m`'s `MissingPages` entry comes out and `s0520m`'s `startPage` goes from 5 to 7. |
| Two-volume span | MN Tika 1 (`s0201t.tik.xml`) | Spans two PDF files; needs volume-based PDF selection |
| Truncated scan | 1957 Sagāthāvagga-Nidānavagga | Holds only the first 6 printed pages of the Nidānavagga (`s0302m`, 1.0243–1.0248); the SharePoint copy is the same size, so it is short at the source (#1099) |
| Text pages missing | 1957 Netti-Peṭakopadesa | Print pages 156–157 are absent, and they are text pages, not blanks; listed in `MissingPages` for `s0519m` (#1101) |
| Pages scanned twice | Paṭṭhāna-2 1957 (`abh03m8`) | The scan runs 93, 94, 95, 94, 95, 96. `ExtraPagesAfter` `{ 95, 95 }` keeps print page 96 onward right. [suggestion] Remove the two duplicate PDF pages; then the entry comes out |
| Renamed on SharePoint | 2010 Apadāna-2 / Buddhavaṃsa / Cariyāpiṭaka (`s0510m2`, `s0511m`, `s0512m`) | The mapped filename no longer existed, so the download failed. Repointed at the `(2010)` printing (#1111); a `(2005)` printing is also there. Start page 19 is still to be checked (#1110) |
| No Myanmar pages | Sīhaḷa: e1201, e1203, e1205, e1209 | The XML has only `ed="O"` pages, so these are mapped by O page (#1100). The O numbers are not the scans' pagination: e1201 drifts by up to +11 pages. e1209's PDF is a Burmese edition with translations and commentary that omits the XML's verse 1. The fix is Myanmar page markers in the XML, deferred |

## Page Marker Format Reference

Myanmar page markers in the XML use this pattern:

```xml
<pb ed="M" n="VOLUME.PAGE"/>
```

- Volume is typically `1` but can be `2`, `3`, etc. for multi-volume works
- Page is zero-padded to 4 digits: `0001`, `0002`, etc.
- Other editions use different `ed` codes: `V` (VRI), `T` (Thai), `P` (PTS)
- A single location may have markers for multiple editions

Example with multiple edition markers:

```xml
<pb ed="M" n="1.0042"/><pb ed="V" n="1.0030"/><pb ed="T" n="1.0025"/>
```
