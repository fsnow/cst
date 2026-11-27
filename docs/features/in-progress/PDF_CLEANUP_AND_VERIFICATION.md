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

## Project Scope

### Phase 1: PDF Surgery

For each source PDF, ensure a 1:1 correspondence between PDF pages and original printed book pages:

- **Remove duplicate pages** (e.g., the 99 duplicates in the KN combined PDF)
- **Restore blank pages** where they existed in the original print. This makes the formula `pdfPage = startPage + (myanmarPage - 1)` work with a consistent `startPage` across all books in a combined PDF, because the page offset never drifts.
- **Document the changes** made to each PDF

After surgery, the `startPage` values in `Sources.cs` will need to be recalculated and updated.

### When a corrected PDF replaces one on SharePoint (#1136)

[observed, `SharePointService.GetPdfAsync`] Each time a PDF is opened while online, the app compares SharePoint's content hash (`quickXorHash`) with the cached copy's. If they differ, it downloads the current file and replaces the cached copy [fsnow, 2026-11-26: "Replace it"]. Offline, the cached copy is used as it is.

A book's page mapping can differ between versions of its PDF. [fsnow, 2026-11-26] "Let's change the data structure so that we can support mappings for multiple versions", and on a file with no mapping of its own: "If there is a newer file we don't have a mapping for, we will use the latest." So, in `Sources.cs`:

- **A book with one mapping for every version** needs nothing more than its `addSource` entry. A scan whose placeholders are replaced by the real pages keeps its print order, so its mapping doesn't change.
- **A book whose PDF gets a version with different pages** gets an `addVersion` entry for that version, under its hash, with that version's mapping. Entries go oldest first, and the last one is the latest mapping.
- **Then the versions the `addSource` mapping fits must be listed** with `addSourceVersions`. Every hash that isn't listed gets the latest mapping, so an old copy left unlisted would get the new one. A test (`SourcePdfVersionTests`) fails for a book that has `addVersion` entries but no `addSourceVersions`.
- **The hash** is SharePoint's `quickXorHash`: Graph's `driveItem.file.hashes.quickXorHash`. The app logs it each time it opens a PDF ("PDF version …"). Note it whenever a mapping is measured, so it is clear which version of the scan the mapping was measured against. For a file not yet uploaded, compute the hash locally; it is the same algorithm (`QuickXorHash.cs`).

- **When a repaired PDF goes up.** Not before the release that carries its mapping. Older releases have only the original mapping, and they apply it to whatever file they download first. For the 50 PDFs repaired in November 2026: [fsnow, 2026-11-27] "After beta 10 ships". That's a step in RELEASE_PROCESS.md, under Bucket B.

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
| 99 duplicate pages, a mislabelled page, and the end of the Itivuttaka missing | KN combined 1957 (Khuddakapāṭha–Suttanipāta) | Checked by hand 2026-11-13. After print 219 (PDF 241) the scan starts again at print 120 (PDF 242); print 219 recurs at PDF 340 and print 220 is PDF 341, so the repeat is 99 PDF pages. Print 275 is labelled "279". Print 276–277 (the end of the Itivuttaka, from ¶112) and the blank 278 are not in the scan. Recorded as `ExtraPagesAfter` (99 × 219) and `MissingPages` `{ 276, 277, 278 }` for `s0504m` and `s0505m`, both now at `startPage` 23; `s0505m`'s old workaround of 117 opened the Suttanipāta two pages early. **Repaired (#1153, #1155), to be uploaded after beta 10 ships.** The repaired file drops the second copy (original PDF 242–340) and has placeholders for print 12, 194 and 276–278; 653 → 554 pages. In it, s0501m–s0505m all start at 25, with no `ExtraPagesAfter`. Print 276–277 carry text, so their placeholders await a scan of the two pages. |
| Blank pages removed | Multiple PDFs | Causes decreasing `startPage` pattern across books in combined PDFs |
| Text pages missing from the scan | Netti-Peṭakopadesa 1957 (`s0519m`) | Print pages 156–157 are not in the PDF and are not blank. `MissingPages` `{ 156, 157 }` keeps later pages right (#1101); `s0520m` shares the PDF and its `startPage` 5 already absorbs the gap. [fsnow: VRI "probably have the physical book but not a better scan"] [suggestion] Scan the two pages from the volume and insert them. The new scan's mapping (no `MissingPages` for `s0519m`, `startPage` 7 for `s0520m`) then goes in `addVersion` entries under its hash, and the old scan's hash in `addSourceVersions` (#1136). |
| Two-volume span | MN Tika 1 (`s0201t.tik.xml`) | Spans two PDF files; needs volume-based PDF selection |
| Truncated scan | 1957 Sagāthāvagga-Nidānavagga | Holds only the first 6 printed pages of the Nidānavagga (`s0302m`, 1.0243–1.0248); the SharePoint copy is the same size, so it is short at the source (#1099) |
| Text pages missing from the scan | Jātaka-aṭṭhakathā-7 1957 (`s0514a3`) | [fsnow, 2026-11-19: "The s0514a3.att.xml PDF is missing its print pages 2 and 3"] The XML has markers for both, so they carry text. Added to `MissingPages` (now `{ 2, 3, 150 }`) so later pages stay right; a request for either lands on print page 4. [suggestion] Scan the two pages from the volume and insert them. The new scan's mapping, without 2 and 3 in `MissingPages`, then goes in an `addVersion` entry, and the old scan's hash in `addSourceVersions` (#1136). |
| Text pages missing | 1957 Netti-Peṭakopadesa | Print pages 156–157 are absent, and they are text pages, not blanks; listed in `MissingPages` for `s0519m` (#1101) |
| Two printed pages per PDF page | 1957 Vinayalankara Tika Part - 1 (`vin08t`), Pacityadiyojanapali-1-2 (`vin12t`) | The pages are scanned two to a PDF page, which `PdfPageFor` cannot map; both left at the placeholder start page 1. [fsnow, 2026-11-17: "we haven't handled that case yet and won't for this release"] (#1110) |
| Text pages missing, replaced by placeholders | 1957 Vinayavinicchaya Tika Part 2 (`vin11t` vol. 2) | [observed, tester's start-page sheet, 2026-11-18] Print pages 500–501 are missing; two placeholder pages stand in for them, so the page count stays aligned and no `MissingPages` entry should be needed. Volume 2 starts at PDF page 9. Unmapped until #546. |
| Pages scanned twice | Jātaka-aṭṭhakathā-4 1957 (`s0513a4`) | [fsnow, 2026-11-22: "in that PDF, pages 130 and 131 occur twice"] The scan runs 130, 131, 130, 131, 132. `ExtraPagesAfter` `{ 131, 131 }` keeps print page 132 onward right; start page 7. [suggestion] Remove the two duplicate PDF pages; then the entry comes out |
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
