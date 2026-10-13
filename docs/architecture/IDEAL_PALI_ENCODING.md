# IPE: The Ideal Pāli Encoding

This document explains the custom "Ideal Pāli Encoding" (IPE) used within CST4, detailing what it is, how it works, and most importantly, *why* it was created as a solution to the unique challenges of processing Pāli text.

## 1. The Problem: Why Not Use a Standard Script?

The source texts for CST are in Devanagari XML, and the application can transliterate them into 13 other scripts. A logical question is why not simply use one of these standard scripts—like Devanagari or Roman/Latin—for the internal search index in Lucene. The answer lies in two fundamental problems: **sorting** and **efficiency**.

### The Sorting Problem
Computers sort strings based on the numerical value of their characters (e.g., in the Unicode table). This standard alphabetical order does not match the traditional alphabetical order of the Pāli language, especially when dealing with the complexities of Indic scripts like Devanagari.

-   **The Inherent 'a'**: In Devanagari, the consonant character `क` represents the sound `ka`. There is no separate character for the vowel 'a'; it is inherent. To represent the consonant sound `k` alone, a `halant` (or `virāma`) character must be added: `क्`. This creates a mismatch between the writing system and the phonemic alphabet. A simple sort would incorrectly place `क` (`ka`) before `क्` (`k`), when the opposite is often required in linguistic analysis.

-   **The `halant` (virāma)**: The `halant` character (`्`, U+094D) is used to form consonant conjuncts. Its presence fundamentally changes how words should be sorted, but its position in the Unicode table does not guarantee a correct Pāli alphabetical sort when combined with various consonants.

-   **Zero-Width Non-Joiner (ZWNJ)**: Characters like ZWNJ (`U+200C`) are used to control the visual rendering of conjuncts. For example, it can be used to force a sequence like `kka` to render as a "half ka" followed by a "full ka" (`क्‍क`) instead of a stacked ligature, which some fonts might use by default. This means two Pāli words could be semantically identical but have different Unicode representations depending on the desired visual output. A standard sort would treat them as different words, leading to incorrect ordering.

-   **Latin Script Complications**: The problem is even worse with the Latin script. The Pāli alphabet includes aspirated consonants like 'kh' and 'gh'. In Latin script, these are represented by two characters. A standard sort would place 'kh' between 'kg' and 'ki', which is completely wrong. It should come immediately after 'k'.

IPE solves all these issues by creating a direct, one-to-one mapping between a Pāli phoneme and a single, unique character code, with the codes themselves arranged in proper Pāli alphabetical order.

### The Efficiency Problem
Using multi-character representations for single Pāli phonemes (like 'kh' in Latin) is inefficient for storage and, more importantly, for the complex positional logic that CST4's search feature relies on. Treating a single logical sound as two separate characters complicates phrase and proximity searches.

## 2. The Solution: Ideal Pāli Encoding (IPE)

IPE was created to solve these two problems directly. It is a custom, single-byte encoding scheme where each logical Pāli character is mapped to a unique value.

### How IPE Works
The core of IPE is a carefully designed mapping, defined in `Deva2Ipe.cs` and `Latn2Ipe.cs`. It maps each letter to a specific Unicode value in the `U+00C0` to `U+00EF` range (IPE version 2; see *Versions* below).

**The key insight is that the numerical order of these target Unicode values exactly matches the traditional Pāli alphabetical order.**

| Letter | Devanagari | Latin | IPE Value | Unicode Point |
| :----- | :--------- | :---- | :-------- | :------------ |
| niggahīta | `ं` | `ṃ` | `À` | `U+00C0` |
| visarga | `ः` | `ḥ` | `Á` | `U+00C1` |
| a | `अ` | `a` | `Â` | `U+00C2` |
| ā | `आ` | `ā` | `Ã` | `U+00C3` |
| ... | ... | ... | ... | ... |
| ū | `ऊ` | `ū` | `Ç` | `U+00C7` |
| vocalic r | `ऋ` | `ṛ` | `È` | `U+00C8` |
| e | `ए` | `e` | `É` | `U+00C9` |
| ai | `ऐ` | `ai` | `Ê` | `U+00CA` |
| o | `ओ` | `o` | `Ë` | `U+00CB` |
| au | `औ` | `au` | `Ì` | `U+00CC` |
| k | `क` | `k` | `Í` | `U+00CD` |
| kh | `ख` | `kh` | `Î` | `U+00CE` |
| ... | ... | ... | ... | ... |
| v | `व` | `v` | `ê` | `U+00EA` |
| palatal s | `श` | `ś` | `ë` | `U+00EB` |
| retroflex s | `ष` | `ṣ` | `ì` | `U+00EC` |
| s | `स` | `s` | `í` | `U+00ED` |
| h | `ह` | `h` | `î` | `U+00EE` |
| ḷ | `ळ` | `ḷ` | `ï` | `U+00EF` |

*(This is a simplified excerpt of the full mapping)*

### Versions

IPE packs the alphabet into consecutive code points, skipping only `U+00D7` (the multiplication sign). Adding a letter at its place in the alphabet therefore renumbers every letter after it, and any stored IPE becomes stale.

- **Version 1** (2007): the Pāli alphabet, `U+00C0`–`U+00E9`.
- **Version 2** (#1049, 2026-10): adds visarga, vocalic r, ai, au and the palatal and retroflex sibilants, which the corpus's Sanskrit needs (#1046). [fsnow, 2026-10-13] "IPE needs to be modified to support them (in correct alphabetical order)". The order was proposed by the agent and agreed by fsnow: visarga after niggahīta; vocalic r after ū; ai after e; au after o; the two sibilants between v and s.

The version is `IpeVersion.Current` in `src/CST.Core/Conversion/IpeVersion.cs`. The search index is the only place that stores IPE. It records the version that built it in an `ipe-version` file in the index directory, and the app deletes and rebuilds an index whose stamp is missing or different (`IndexIpeStamp`, `IndexingService.BuildIndexAsync`). The dictionaries and DPD data store Latin or IAST and convert to IPE at lookup time, so they need no rebuild.

Two display decisions came with version 2 (fsnow, 2026-10-13):
- **Latin ai/au.** "Keep Pāli as is": Latin writes the diphthongs as `ai`/`au`, and typed Latin `ai`/`au` keeps meaning the Pāli vowel hiatus *a + i* / *a + u*.
- **Missing letters.** "Keep Devanagari": where a script has no letter for one of these, the Devanagari letter stays in place.

### The Benefits of IPE

1.  **Correct Alphabetical Sorting**: Because the character codes are assigned in Pāli alphabetical order, performing a standard sort on a list of IPE strings results in a list that is correctly sorted according to Pāli tradition. This is critical for the dictionary feature (`FormDictionary`), where words must be listed in the correct order.
2.  **Efficiency and Simplicity**: Every Pāli character, including aspirated consonants like 'kh', becomes a single, unique byte. This makes the internal representation compact and dramatically simplifies the logic for positional searches, as the application can deal with single units of information instead of multi-character sequences.
3.  **Script Independence**: By converting all text to IPE before indexing or searching, the application becomes script-agnostic. A user can type a search term in Sinhala, and the system will convert it to IPE. It then searches the Lucene index, which also contains IPE strings. The matching results, which are in IPE, can then be converted back to any of the 13 supported scripts for display.

## 3. The IPE Workflow in CST4

1.  **Indexing**: During the indexing process, the original Devanagari XML is read, and its content is converted to IPE using `Deva2Ipe.cs` before being stored in the Lucene term dictionary.
2.  **Searching**: When a user enters a search term in `FormSearch`, the `Any2Ipe.Convert()` method is called. It auto-detects the script of the input string, converts it to the internal IPE representation, and then performs the search against the IPE-encoded Lucene index.
3.  **Displaying**: When search results or dictionary entries are displayed, the IPE strings are converted back to the user's currently selected display script using converters like `Ipe2Deva.cs` or `Ipe2Latn.cs`.

In summary, IPE is a clever, custom-engineered solution that solves the fundamental computer science problems of sorting and representation for the specific domain of the Pāli language, enabling features that would be difficult and inefficient to implement using standard text encodings.
