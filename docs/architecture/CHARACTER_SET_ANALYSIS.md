# CST Character Set Analysis - Project Retrospective

**Date**: October 17, 2025
**Author**: Frank Snow (with Claude Code assistance)
**Status**: Completed

## Background

### Historical Context

The CST (Chaṭṭha Saṅgāyana Tipiṭaka) texts were finalized and published in print in Devanagari script during the 1993-1995 period at the Vipassana Research Institute (VRI). At that time, 140 books were published representing "pure" Pali canonical texts. Later, approximately 80 additional post-canonical books were added to the electronic edition, primarily files with the "e" prefix.

The original texts were typed in India using Aalekh, an early DOS-based Devanagari word processor with proprietary encoding. Around 2007, these texts were converted from Aalekh's encoding to Unicode Devanagari

### The Problem

When CST4 was developed in 2007, the encoding architecture (IPE - Internal Phonetic Encoding) and script conversion system was designed exclusively for Pali phonology, based on the character set in the Deva2Ipe converter dictionary. The later post-canonical texts were not as familiar at that time, and no systematic analysis had been done to identify non-Pali characters across the entire corpus.

**Key Questions**:
1. Do the post-canonical texts contain Sanskrit characters that need encoding/conversion support?
2. Are there characters in the texts that fall outside the standard Pali character set?
3. If so, which characters, how frequently, and in which books?

## The Character Analysis Project

### Objectives

1. Build a systematic inventory of all characters across all 217 XML files
2. Exclude standard Pali characters (as defined in Deva2Ipe.cs) and common punctuation
3. Generate a comprehensive report showing:
   - Each non-standard character with Unicode details
   - Frequency counts
   - Book locations
   - Context examples

### Implementation

**Tool**: Console application `CST.CharacterAnalysis`
**Technology**: .NET 9.0, C#
**Source Files**: XML files in `~/Library/Application Support/CSTReader/xml/` (217 files)

**Key Features**:
- XML text extraction (excluding element names and attributes)
- Character filtering based on Deva2Ipe standard Pali character set
- Frequency counting and book tracking
- Context extraction for examples
- HTML report generation with full Unicode metadata

**Standard Pali Character Set** (excluded from analysis):
- Niggahita (ṃ)
- Independent vowels: a, ā, i, ī, u, ū, e, o
- Consonants: All standard Pali consonants including:
  - Velar stops: k, kh, g, gh, ṅ
  - Palatal stops: c, ch, j, jh, ñ
  - Retroflex stops: ṭ, ṭh, ḍ, ḍh, ṇ
  - Dental stops: t, th, d, dh, n
  - Labial stops: p, ph, b, bh, m
  - Liquids/fricatives: y, r, l, v, s, h, ḷ
- Dependent vowel signs
- Virama, ZWNJ, ZWJ
- Devanagari digits (०-९)
- Devanagari punctuation: danda (।), double danda (॥)
- Common ASCII punctuation and digits

### Results

**Total non-standard characters found**: 13

**Character Breakdown**:

1. **Sanskrit Vowels**:
   - ai (ऐ) - independent form
   - ai vowel sign - dependent form
   - au (औ) - independent form
   - au vowel sign - dependent form
   - Vocalic r (ऋ) - independent form only

2. **Sanskrit Consonant**:
   - Visarga (ः) - voiceless glottal fricative (21 instances)

3. **Punctuation**:
   - Various marks

### Key Insights

1. **Visarga instances are likely typos**: All 21 occurrences of visarga (ः) appear in canonical texts, not post-canonical ones. These are very likely data entry errors from the original Aalekh typing process, possibly confused with niggahita (ṃ).

2. **All Sanskrit characters may be typos**: Given that earlier conversion work systematically stripped out Sanskrit sibilants and other characters, the remaining 13 characters are likely:
   - Data entry artifacts from the DOS-based Aalekh word processor
   - Edge cases missed during the Aalekh → Unicode conversion
   - Characters that survived because they were infrequent or looked similar to Pali characters

3. **Low frequency suggests errors, not content**: If these were legitimate Sanskrit quotations or terminology in post-canonical texts, we would expect to see them more frequently and clustered in specific contexts. The low instance counts across the entire 217-book corpus suggest they are data quality issues rather than legitimate content.

4. **IPE extension may be unnecessary**: If the text correction team determines these are all errors, no extension to the IPE encoding or script conversion system will be needed. The character analysis becomes a quality control tool rather than a requirements gathering exercise for encoding work.

## Digital Archaeology

This project represents multiple layers of digital archaeology:

1. **Text History**: Pali texts typed in India in the early 1990s using DOS-based Aalekh software
2. **First Conversion**: Aalekh proprietary encoding → Unicode Devanagari (circa 2007)
3. **Systematic Cleanup**: Removal of Sanskrit characters like sibilants during conversion
4. **Current Analysis**: Identifying what survived 30+ years and multiple format conversions

Finding the original Aalekh conversion code to understand *why* these specific characters survived would itself be another archaeology project. Instead, this analysis takes a pragmatic approach: it doesn't matter *why* they're there, only *that* they're there and whether they should remain.

## Deliverables

1. **Console Application**: `/Users/fsnow/github/fsnow/cst/src/CST.CharacterAnalysis/`
   - Standalone .NET 9 console app
   - Can be run by colleagues: `dotnet run`
   - Processes all 217 XML files in ~5 seconds

2. **HTML Report**: `~/Desktop/CST-Character-Analysis.html`
   - Complete character inventory with Unicode details
   - Frequency counts and book locations
   - Context examples showing usage
   - Sortable, searchable format

## Next Steps

1. **Text Correction Team Review**: Share the HTML report with the VRI text correction team for editorial review
2. **Character-by-Character Evaluation**: Review each of the 13 characters in context to determine if they are:
   - Legitimate content that should remain
   - Typos/errors that should be corrected to proper Pali
3. **Source Text Corrections**: Make necessary corrections to the XML files
4. **Re-run Analysis**: After corrections, re-run the analysis tool to verify cleanup
5. **IPE Extension Decision**: Based on review outcomes, determine if any genuine Sanskrit characters need to be added to the encoding system

## Technical Notes

**Running the Analysis**:
```bash
cd /Users/fsnow/github/fsnow/cst/src/CST.CharacterAnalysis
dotnet run
```

**Output**: HTML report saved to `~/Desktop/CST-Character-Analysis.html`

**Modifying Excluded Characters**: Edit the `StandardPaliChars` HashSet in `Program.cs` to add or remove characters from the exclusion list.

**Performance**: Processes all 217 XML files (complete Pali canon) in approximately 5 seconds on Apple Silicon.

## Conclusion

This side project successfully answered a question that had been outstanding since 2007: "What non-Pali characters exist in the CST texts, and where are they?"

The answer turned out to be simpler than expected: only 13 distinct characters, with low frequency counts suggesting they are primarily data quality issues rather than legitimate Sanskrit content requiring encoding system extensions.

The tool provides a systematic, repeatable way to audit the character set across the entire corpus and will serve as a quality control mechanism for ongoing text correction work.

Most importantly, this analysis demonstrates the value of systematic code-based approaches to questions that would be nearly impossible to answer through manual review of 217 books containing millions of characters.

---

**Project Duration**: ~1 hour (October 17, 2025)
**Lines of Code**: ~300
**Books Analyzed**: 217
**Characters Found**: 13
**Insights Gained**: Priceless
