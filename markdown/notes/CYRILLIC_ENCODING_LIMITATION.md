# Cyrillic Encoding Limitation

**Status**: PERMANENT LIMITATION - Cannot be fixed without redesigning the encoding system

**Last Updated**: 2025-10-21

## Summary

The Cyrillic encoding system for Pali has an inherent ambiguity that makes round-trip conversion impossible for certain phonetic patterns. This is a fundamental limitation of the encoding design, not a software bug.

## The Ambiguity

The Cyrillic encoding cannot distinguish between:

1. **Consonant + dependent long ā**: द + ा (dā) → "д̣̇аа"
2. **Consonant + inherent a + independent short a**: र + अ (raa) → "раа"

Both patterns produce "consonant + аа" in Cyrillic, creating an unresolvable ambiguity.

## Technical Details

### Encoding Rules (from Deva2Cyrl.cs)

- **Line 76**: Dependent vowel ा (U+093E) → "аа"
- **Line 67**: Independent vowel आ (U+0906) → "аа"
- **Line 134**: Regex inserts inherent 'а' after consonants not followed by dependent vowels, virama, or independent vowels

### Example 1: Long ā (Unambiguous)

```
Input:  द (U+0926) + ा (U+093E) = दा (dā)
To Cyrillic: "द̣̇" + "аа" = "д̣̇аа"
Round-trip:  "д̣̇аа" → द + ा = दा ✓ (CORRECT)
```

### Example 2: Consonant + Independent Short a (Ambiguous)

```
Input:  र (U+0930) + अ (U+0905) = रअ (raa)
To Cyrillic: "р" + "а" (inherent) + "а" (independent) = "раа"
Round-trip:  "раа" → ???
```

When converting "раа" back to Devanagari, there are two possible interpretations:

**Option A**: Skip first 'а' (inherent), interpret "аа" as long ā
- Result: रा (rā) ✗ WRONG - This is long ā, not ra + short a

**Option B**: Don't skip first 'а', interpret "аа" as long ā
- Result: अआ (aā) ✗ WRONG - Lost the र consonant entirely

**No correct option exists!** The original pattern रअ (raa) cannot be recovered.

## Affected Patterns

### Pattern 1: रअ (raa)
- **IPE1**: `raa` (consonant r + independent short a)
- **IPE2**: `rā` (consonant r + dependent long ā)
- **Error**: Independent short vowel is lost and becomes long ā

### Pattern 2: सआ (saā)
- **IPE1**: `saā` (consonant s + independent long ā)
- **IPE2**: `sāa` (scrambled)
- **Error**: Order is scrambled during round-trip

## Why This Cannot Be Fixed

The Cyrillic encoding was designed by **Russian and Mongolian stakeholders** as part of the CST project. Changing the encoding rules would:

1. **Break compatibility** with existing Cyrillic Pali texts
2. **Violate stakeholder decisions** about the orthographic system
3. **Require redesigning** the entire Cyrillic encoding scheme
4. **Impact established users** who rely on the current encoding

## Frequency of Occurrence

This limitation only affects words containing:
- Consonant + independent short vowel अ (U+0905)
- Consonant + independent long vowel आ (U+0906) following inherent 'a'

These patterns are **relatively rare** in Pali texts. The vast majority of Cyrillic conversions work correctly.

## Impact on CST Reader

### What Works
- Reading and displaying Cyrillic texts: ✓ WORKS
- Devanagari → Cyrillic conversion: ✓ WORKS
- Most Cyrillic → Devanagari conversions: ✓ WORKS

### What Doesn't Work
- Round-trip validation for words with affected patterns: ✗ FAILS
- Search across scripts when query contains affected patterns: ⚠️ MAY FAIL

### Recommendations

1. **For users**: Cyrillic display and reading work perfectly. This limitation only affects programmatic round-trip conversion testing.

2. **For developers**: Do not attempt to "fix" this by modifying Deva2Cyrl.cs or Cyrl2Deva.cs without stakeholder approval.

3. **For validation**: Accept that Cyrillic will have a small number of permanent round-trip failures. This is expected and documented.

## References

- **Source files**:
  - `/Users/fsnow/github/fsnow/cst/src/CST.Core/Conversion/Deva2Cyrl.cs`
  - `/Users/fsnow/github/fsnow/cst/src/CST.Core/Conversion/Cyrl2Deva.cs`

- **Test reports**:
  - `/Users/fsnow/github/fsnow/cst/src/CST.ScriptValidation/reports/report-छ-दनवधबन-धनव-पर-म-सआ-2025-10-21-111722.md`

- **Analysis**:
  - `/Users/fsnow/github/fsnow/cst/src/CST.ScriptValidation/reports/BUG_ANALYSIS.md`
