# CST Converter Comparison Framework

**Created**: 2025-10-19
**Status**: ✅ Complete and Ready to Use

## Overview

A three-way comparison framework that validates CST script converters against two external standards:
1. **pnfo/pali-script-converter** - JavaScript library (likely used by SuttaCentral)
2. **Aksharamukha** - Python library (academic/community standard)

## Architecture

```
CST.ScriptValidation/
├── PnfoConverter.cs              # Node.js wrapper for pnfo package
├── AksharamukhaConverter.cs      # Python wrapper for Aksharamukha
├── ConverterComparison.cs        # Three-way comparison framework
├── SuttaCentralFetcher.cs        # SC API investigation tool
└── Program.cs                    # Main entry point with 3 modes
```

## Prerequisites

### 1. Install Node.js and pnfo Package (Global)
```bash
npm install -g https://github.com/Path-Nirvana-Foundation/pali-script-converter --force
```

### 2. Install Python and Aksharamukha
```bash
python3 -m pip install aksharamukha
```

**Note**: The framework will attempt auto-installation if packages are missing, but global installation is faster.

## Usage

### Mode 1: Standard Validation (CST Internal)
```bash
cd /Users/fsnow/github/fsnow/cst/src/CST.ScriptValidation
dotnet run
```
Tests CST round-trip conversions on 2.9M words from 217 books.

### Mode 2: SuttaCentral API Investigation
```bash
dotnet run --suttacentral
```
Investigates SuttaCentral API endpoints and downloads sample data.

### Mode 3: Three-Way Converter Comparison
```bash
dotnet run --compare
```
Compares CST vs pnfo vs Aksharamukha converters.

## Three-Way Comparison Details

### What It Tests

**Sample Size**: First 10 XML files, 100 words per file (~1,000 words total)
**Test Pattern**: Devanagari → Myanmar (configurable to other scripts)

For each word:
```
1. CST:          Deva → Myanmar (C#)
2. pnfo:         Deva → Myanmar (JavaScript)
3. Aksharamukha: Deva → Myanmar (Python)
```

### Output Categories

| Icon | Category | Meaning |
|------|----------|---------|
| ✅ | AllAgree | All three converters produce identical output |
| ⚠️ | CstDiffersBoth | CST differs from both external converters (potential issue) |
| ℹ️ (pnfo) | CstMatchesPnfo | CST matches pnfo but not Aksharamukha (different standards) |
| ℹ️ (Aksharamukha) | CstMatchesAksharamukha | CST matches Aksharamukha but not pnfo |
| 🤔 | AllDiffer | All three produce different outputs (ambiguous/variants) |
| ❌ | External Failed | One or both external converters failed |

### Report Output

**Location**: `~/Desktop/CST-Converter-Comparison.html`

**Contents**:
- Summary statistics by category
- Detailed differences table with original word, all three outputs, and category
- Color-coded rows (green = agree, yellow = warn, blue = info)

## Script Name Mappings

### CST → pnfo
```csharp
Devanagari → "Devanagari"
Latin      → "Roman"
Myanmar    → "Myanmar"
Thai       → "Thai"
etc.
```

### CST → Aksharamukha
```csharp
Devanagari → "Devanagari"
Latin      → "IAST"  // or "ISO", "IPA"
Myanmar    → "Burmese"
Thai       → "Thai"
etc.
```

## Customization

### Change Target Script

Edit `Program.cs` line 645:
```csharp
Script targetScript = Script.Myanmar;  // Change to any script
```

### Change Sample Size

Edit `Program.cs` line 639 (number of files):
```csharp
.Take(10)  // Change to test more files
```

Edit `Program.cs` line 657 (words per file):
```csharp
.Take(100)  // Change to test more words
```

### Test Multiple Scripts

Add a loop in `RunComparisonTestsAsync()`:
```csharp
foreach (var targetScript in new[] { Script.Myanmar, Script.Thai, Script.Sinhala })
{
    // existing comparison code
}
```

## Use Cases

### 1. Validate New Converter Development
When developing Thai/Telugu/Tibetan/Khmer/Cyrillic input parsers:
```bash
# Test your new converter against both standards
dotnet run --compare
```

### 2. Document Known Differences
Identify where CST intentionally differs from community standards:
```bash
# Generate comparison report
dotnet run --compare

# Review "CstDiffersBoth" category in HTML report
# Document intentional differences (e.g., ZWJ insertion)
```

### 3. Find Conversion Issues
Detect systematic problems in existing converters:
```bash
# If "CstDiffersBoth" > 5%, investigate patterns
# Check if pnfo and Aksharamukha agree (validates external converters)
```

### 4. Cross-Validate Against SuttaCentral
Use SC texts (different edition) as test data:
```bash
# Fetch SC text in Latin
dotnet run --suttacentral

# Extract text from SC API response
# Test: SC-Latin → CST-Myanmar → CST-Latin
```

## Expected Results

### Healthy Converter
```
✅ AllAgree:              850 (85.00%)
ℹ️ CstMatchesPnfo:        100 (10.00%)
⚠️ CstDiffersBoth:         50 (5.00%)
```

### Problematic Converter
```
✅ AllAgree:              300 (30.00%)
ℹ️ CstMatchesPnfo:        200 (20.00%)
⚠️ CstDiffersBoth:        500 (50.00%)  ← Investigate!
```

## Technical Notes

### Subprocess Communication
- **pnfo**: Spawns `node convert.js` subprocess, UTF-8 encoded
- **Aksharamukha**: Spawns `python3 convert.py` subprocess, UTF-8 encoded
- Both return null on conversion failure (logged to console)

### Performance
- ~1 second per 100 words (subprocess overhead)
- Parallel execution not implemented (subprocess bottleneck)
- Consider batching for large-scale testing

### Error Handling
- Missing dependencies: Auto-install attempted
- Conversion failures: Logged but don't stop test
- Both external failures: Categorized separately

## Future Enhancements

1. **Batch Conversion**: Send 100 words per subprocess call
2. **Parallel Testing**: Test multiple scripts simultaneously
3. **Difference Analysis**: Automatic pattern detection (e.g., "all differences are ZWJ-related")
4. **SC Integration**: Fetch SC text in various scripts, test round-trip
5. **Regression Testing**: Store "known differences" baseline, alert on changes

## Summary

You now have a comprehensive validation framework that:
- ✅ Tests CST converters internally (round-trip validation)
- ✅ Compares CST against two external standards
- ✅ Categorizes differences for easy analysis
- ✅ Generates detailed HTML reports
- ✅ Supports all 14 Pali scripts (where external converters exist)

This provides multiple "sources of truth" for converter validation and will be invaluable when developing the 5 missing input parsers.
