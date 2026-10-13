# CST Character Set Analysis - Project Retrospective

**Date**: October 17, 2025
**Author**: Frank Snow (with Claude Code assistance)
**Status**: Completed (findings revised 2026-10-13, see #1046)

## Background

### Historical Context

The CST (Chaṭṭha Saṅgāyana Tipiṭaka) texts were finalized and published in print in Devanagari script during the 1993-1995 period at the Vipassana Research Institute (VRI). At that time, 140 books were published representing "pure" Pali canonical texts. Later, approximately 80 additional post-canonical books were added to the electronic edition, primarily files with the "e" prefix.

The original texts were typed in India using Aalekh, an early DOS-based Devanagari word processor with proprietary encoding. In early 2007, these texts were converted from Aalekh's encoding to Unicode Devanagari.

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

### What the characters turned out to be

Revised 2026-10-13, after the original Aalekh files were recovered from the CSCD 3.0 ISO. The 2025 reading of these results ("likely typos", "low frequency suggests errors", "IPE extension may be unnecessary") is superseded. Details in #1031 and #1046.

- **ai, au and vocalic ṛ** (#1031): [observed] All 67 occurrences are already in the 1990s Aalekh typing, so none came from the conversion.
  - Genuine: the alphabet lists in the grammars, and Burmese words written in Devanagari.
  - The rest are typing errors, most often ai/au written where a vowel junction gives e/o.
  - The two ṛ are stray typos in Pāli. Neither is Sanskrit.
  - The per-occurrence classification is in [`docs/research/aalekh/non-pali-letters-1031.csv`](../research/aalekh/non-pali-letters-1031.csv).
- **Visarga.**
  - [observed] The Aalekh files use the visarga byte both as a colon-like mark in Pāli and as a true visarga in Sanskrit passages.
  - The 18 that survive in today's XML (abh01m 11, s0504m 6, s0519m 1) are the punctuation use.
  - The Sanskrit visargas became `?` in the 2007 conversion.
- **The Sanskrit letters that are absent** (ś, ṣ, and ṛ in Sanskrit). [observed] They are absent because they were never typed, not because the conversion stripped them.
  - The Aalekh files contain no ś or ṣ at all.
  - The Myanmar-script source print does contain them. The e0105n quotation of the Puruṣasūkta prints ś, ṛ, ai and visarga; the typing has ग for ś and drops ṛ and ai.
  - So the low frequency of Sanskrit letters in the corpus is not evidence that the Sanskrit isn't there.
- **IPE and the converters will need extending.** [fsnow, 2026-10-13] "All the converters need to support these additional characters, IPE needs to be modified to support them (in correct alphabetical order), etc."

## Digital Archaeology

1. **Source**: Myanmar-script printed editions, which include the Sanskrit letters in quoted Sanskrit.
2. **Typing**: Devanagari typed into Aalekh, a DOS-based word processor, in India in the early 1990s. Most Sanskrit letters were not typed: ś and ṣ became other letters, and ṛ and some vowel signs were dropped. Visarga, ai and au were typed.
3. **First Conversion**: Aalekh → Unicode Devanagari, early 2007, by fsnow's C# converter (`VriDevToUni` in `VipassanaTech/tipitaka-xml`). The converter had no Sanskrit entries at that time, so the typed visargas in Sanskrit passages became `?`.
4. **Current corpus**: about 20 years of VRI corrections on top of that conversion.

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

Tracked in #1031 (classifying ai/au/ṛ) and #1046 (restoring the Sanskrit from the print, and extending IPE and the converters).

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

The inventory answers what is in the XML. What the source books contain is a different question, answered by the 2026 work in #1031 and #1046: most of the Sanskrit letters were never typed, so the XML alone understates the Sanskrit.

The tool provides a systematic, repeatable way to audit the character set across the entire corpus and will serve as a quality control mechanism for ongoing text correction work.

Most importantly, this analysis demonstrates the value of systematic code-based approaches to questions that would be nearly impossible to answer through manual review of 217 books containing millions of characters.

---

**Project Duration**: ~1 hour (October 17, 2025)
**Lines of Code**: ~300
**Books Analyzed**: 217
**Characters Found**: 13
**Insights Gained**: Priceless
