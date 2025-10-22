# CST Script Validation

This project validates round-trip conversions for all Pali scripts supported by CST.

## Overview

The CST (Chaṭṭha Saṅgāyana Tipiṭaka) project supports 14 different scripts for displaying Pali texts. This validation tool ensures that text can be converted between scripts without data loss. It tests round-trip conversion through the Ideal Pali Encoding (IPE) format:

```
Devanagari → IPE → Target Script → IPE → Target Script
```

For a successful round-trip, both IPE conversions must match, proving no data was lost in conversion.

## Quick Start

### Default Validation Mode (Recommended)
Test using the curated syllable test words only:

```bash
cd src/CST.ScriptValidation
dotnet run validate
```

This tests ~3,000 carefully selected words from `syllable-test-words.txt` that cover all Pali phonetic patterns.

### Full Corpus Validation Mode
Test using all unique words from the 217 XML files (~2.9M words):

```bash
dotnet run validate --full-corpus
# or
dotnet run validate -f
```

⚠️ **Warning**: This mode takes significantly longer and requires the XML files to be present.

### Other Testing Modes

**Compare with External Converters:**
```bash
dotnet run compare thai              # Compare Thai conversions
dotnet run compare myanmar --word ကမ္မ  # Test specific word
```

**Analyze Single Word:**
```bash
dotnet run analyze वंअ               # Analyze word in detail
dotnet run analyze छेदन -o report.md # Save analysis to file
```

**Extract Syllable Test Words:**
```bash
dotnet run extract                   # Extract from corpus
dotnet run extract --csv stats.csv   # Generate CSV statistics
```

### Help
View all available commands and options:

```bash
dotnet run --help
dotnet run validate --help
dotnet run compare --help
dotnet run analyze --help
dotnet run extract --help
```

## Test Data

### Syllable Test Words (`syllable-test-words.txt`)
This curated file contains test words specifically chosen to cover:
- All Pali consonants (velar, palatal, retroflex, dental, labial stops, nasals, liquids, fricatives)
- All vowels (short and long: a, ā, i, ī, u, ū, e, o)
- Special characters (niggahita ṃ, virama)
- **Critical edge cases:**
  - Dependent vowel + independent vowel combinations (e.g., आअ, ईइ, ऊउ)
  - Dependent vowel + niggahita + independent vowel (e.g., ांअ, ींइ)
  - Consonant + niggahita + independent vowel (e.g., वंअ)
  - Consonant clusters with virama
  - All aspirated consonants

The syllable test set is the **recommended default** because it:
- Runs quickly (< 1 minute)
- Covers all phonetic patterns systematically
- Catches most conversion bugs
- Easier to debug failures

### Full Corpus
The full corpus includes every unique word from all 217 Pali texts in the CSCD (Chaṭṭha Saṅgāyana CD) collection. This provides:
- Real-world validation across 2.9M+ words
- Detection of rare edge cases
- Comprehensive coverage

However, it:
- Takes much longer to run (10+ minutes)
- Requires ~1GB of XML files
- May contain duplicate patterns
- Harder to debug individual failures

## Supported Scripts

Currently tested scripts:
- **Cyrillic** - Used by Russian and Mongolian speakers
- **Thai** - Used in Thailand

Additional scripts in CST (display-only, not yet validated):
- Bengali, Devanagari, Gujarati, Gurmukhi, Kannada, Khmer, Latin, Malayalam, Myanmar, Sinhala, Telugu, Tibetan

## Tools

The project includes four testing modes:

### 1. Validate (`dotnet run validate`)
**Purpose:** Test round-trip conversion accuracy for all scripts

Tests that text can be converted from Devanagari to a target script and back without data loss. Uses the round-trip pattern: `Deva → IPE → Script → IPE → Script`, verifying that both IPE conversions match.

**Use when:** You want to verify conversion correctness for Cyrillic and Thai (or after implementing new script converters).

### 2. Compare (`dotnet run compare`)
**Purpose:** Compare CST converter against external converters

Compares CST's conversion output with two external converters (pnfo and Aksharamukha) to identify differences and potential issues.

**Use when:**
- Validating that CST produces standard/expected outputs
- Investigating discrepancies or bugs
- Checking if conversion differences are CST-specific or universal

### 3. Analyze (`dotnet run analyze`)
**Purpose:** Deep-dive analysis of a single problematic word

Breaks a word into syllables and tests each syllable individually, then tests consecutive syllable pairs to identify the minimal pattern that triggers a failure.

**Use when:**
- A specific word fails validation and you need to understand why
- Debugging conversion logic
- Identifying context-dependent vs context-free bugs

### 4. Extract (`dotnet run extract`)
**Purpose:** Generate minimal test set from full corpus

Scans all 217 XML files and extracts a minimal set of words that cover all unique syllable patterns and edge cases in the corpus.

**Use when:**
- Regenerating `syllable-test-words.txt` after corpus updates
- Creating custom test sets for specific phonetic patterns
- Analyzing syllable frequency distribution

## Output

### Successful Run
```
=== CST Script Validation Tool ===

Data source: syllable-test-words.txt (curated test set)
Total words to test: 3,247

=== Testing Cyrillic Round-Trip Conversion ===

Results:
  Successes: 3,234
  Failures:  13
  Success rate: 99.60%

First 10 failing words:
  छेदनवधबन्धनविपरामोसआलोपसहसाकारा: IPE1=chedanavadhabandhanaviparāmosaālopasahasākārā, IPE2=chedanavadhabandhanaviparāmosāalopasahasākārā
  ...
```

### Failure Analysis
Each failure shows:
- The original Devanagari word
- IPE1 (first IPE conversion)
- IPE2 (second IPE conversion after round-trip)
- The difference between them indicates where data was lost

## Development

### Adding Test Words

To add test words to `syllable-test-words.txt`:

1. Identify missing phonetic patterns
2. Find or create minimal test words containing those patterns
3. Add words to the file (space or newline separated)
4. Run validation to verify they pass

**Example patterns to test:**
```
# Dependent + independent vowel combinations
दाअ    # dāa - long ā + independent a
किइ    # kii - dependent i + independent i

# Dependent + niggahita + independent
कांअ   # kāṃa - long ā + niggahita + independent a

# Consonant + niggahita + independent
वंअ    # vaṃa - consonant + niggahita + independent a (critical test case)
```

### Architecture

**Main Components:**
- `QuickTest.cs` - Main entry point, command line parsing
- `syllable-test-words.txt` - Curated test word corpus
- `CST.Core/Conversion/` - Conversion classes for each script:
  - `Deva2Cyrl.cs` / `Cyrl2Deva.cs` - Cyrillic bidirectional conversion
  - `Deva2Thai.cs` / `Thai2Deva.cs` - Thai bidirectional conversion
  - `Deva2Ipe.cs` / `Ipe2Deva.cs` - IPE bidirectional conversion
  - `ScriptConverter.cs` - High-level conversion dispatcher

**Test Flow:**
```
For each test word:
  1. Deva → IPE (ipe1)
  2. IPE → Target Script (target1)
  3. Target Script → IPE (ipe2)
  4. IPE → Target Script (target2)

  Success = (ipe1 == ipe2) && (target1 == target2)
```

## Known Issues

### Cyrillic Encoding Ambiguity

Cyrillic has an inherent encoding ambiguity for certain patterns:
- Pattern `consonant + аа` can represent either:
  1. Consonant + dependent long ā (द + ा → "д̣̇аа")
  2. Consonant + inherent a + independent short a (र + अ → "раа")

Both produce identical Cyrillic output, making round-trip conversion impossible for these patterns. This is a limitation of the Cyrillic orthography design, not a code bug.

See `reports/BUG_ANALYSIS.md` for detailed technical analysis.

## Reports

The `reports/` directory contains detailed analysis:
- `BUG_ANALYSIS.md` - Root cause analysis of known issues
- `ScriptValidation-Coverage-Analysis.md` - Test coverage statistics

## Command Line Reference

### Validation Mode
Round-trip conversion testing:

```bash
# Test with curated syllable test words (recommended)
# Automatically generates HTML report in reports/ directory
dotnet run validate

# Test with full corpus (slow, comprehensive)
dotnet run validate --full-corpus
dotnet run validate -f

# Save report to custom location
dotnet run validate -o reports/my-report.html

# Show help
dotnet run validate --help
```

**Note:** By default, validation automatically generates an HTML report in `reports/validation-report-YYYYMMDD-HHMMSS.html`

### Compare Mode
Compare CST converter with external converters (pnfo, Aksharamukha):

```bash
# Compare specific script
dotnet run compare thai
dotnet run compare cyrillic
dotnet run compare myanmar

# Test specific word
dotnet run compare thai --word थेरवाद

# Test words from file
dotnet run compare cyrillic --file my-words.txt

# Save detailed results
dotnet run compare myanmar -o comparison-results.md

# Show help
dotnet run compare --help
```

Supported scripts: `bengali`, `cyrillic`, `gujarati`, `gurmukhi`, `kannada`, `khmer`, `latin`, `malayalam`, `myanmar`, `sinhala`, `telugu`, `thai`, `tibetan`

### Analyze Mode
Detailed analysis of a single word (breaks into syllables, tests each):

```bash
# Analyze a word (console output)
dotnet run analyze छेदनवधबन्धनविपरामोसआलोपसहसाकारा

# Analyze and save to markdown
dotnet run analyze वंअ -o reports/word-analysis.md

# Show help
dotnet run analyze --help
```

### Extract Mode
Extract syllable test words from the full corpus:

```bash
# Extract to default file (syllable-test-words.txt)
# XML files location: /Users/fsnow/Library/Application Support/CSTReader/xml
dotnet run extract

# Extract with custom output
dotnet run extract -o custom-words.txt

# Generate CSV statistics
dotnet run extract --csv syllable-stats.csv

# Custom XML directory
dotnet run extract --xml-dir /path/to/cscd-xml

# Show help
dotnet run extract --help
```

## Contributing

When fixing conversion bugs:

1. **Identify the failing pattern** - Look at the IPE difference
2. **Create minimal test case** - Add to `syllable-test-words.txt`
3. **Trace the conversion** - Use debugger or add console output to converter
4. **Fix the conversion logic** - Update the relevant converter class
5. **Verify the fix** - Run validation to ensure no regressions
6. **Update documentation** - Add notes to BUG_ANALYSIS.md if needed

## See Also

- [CST.Core Conversion README](../CST.Core/Conversion/README.md) - Detailed conversion algorithm documentation
- [CSCD XML Format](../../data/cscd-xml/README.md) - Source text format
- [Pali Scripts](../../markdown/PALI_SCRIPTS.md) - Overview of all 14 supported scripts
