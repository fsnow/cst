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

2. **Duplicate pages**: At least one PDF (the KN combined volume containing Khuddakapāṭha through Suttanipāta) has **100 duplicate pages** (Myanmar pages 120–219 are repeated), shifting all subsequent page numbers.

3. **Combined PDFs spanning multiple books**: Several PDFs contain multiple logical books end-to-end with continuous Myanmar page numbering. For example, one PDF contains Khuddakapāṭha, Dhammapada, Udāna, Itivuttaka, and Suttanipāta. Each book has its own `startPage` in `Sources.cs`.

4. **Two zero-byte PDFs**: Dhammasaṅgaṇī and Yamaka-3 in the 1957 Abhidhamma folder are 0 bytes.

## Project Scope

### Phase 1: PDF Surgery

For each source PDF, ensure a 1:1 correspondence between PDF pages and original printed book pages:

- **Remove duplicate pages** (e.g., the 100 duplicates in the KN combined PDF)
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
| 100 duplicate pages | KN combined (Khuddakapāṭha–Suttanipāta) | Myanmar pages 120–219 repeated; Suttanipāta `startPage` is 117 as workaround |
| Blank pages removed | Multiple PDFs | Causes decreasing `startPage` pattern across books in combined PDFs |
| Zero-byte files | Dhammasaṅgaṇī, Yamaka-3 | 0 bytes in SharePoint; need replacement |
| Two-volume span | MN Tika 1 (`s0201t.tik.xml`) | Spans two PDF files; needs volume-based PDF selection |

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
