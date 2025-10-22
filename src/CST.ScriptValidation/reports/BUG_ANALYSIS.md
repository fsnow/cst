# Script Conversion Bug Analysis

**Generated:** 2025-10-21

## Summary

We have identified conversion failures in **Cyrillic** and **Thai** scripts. Both have 2 failing words with specific patterns.

## Failing Words

### Word 1: छेदनवधबन्धनविपरामोसआलोपसहसाकारा
**Latin**: chedanavadhabandhanaviparāmosaālopasahasākārā

- **Cyrillic**: IPE1=`chedanavadhabandhanaviparāmosaālopasahasākārā` → IPE2=`chedanavadhabandhanaviparāmosāalopasahasākārā`
  - Failing pattern: `saā` → `sāa` (consonant+inherent_a + independent_long_ā gets scrambled)
  - Detected in pair: `सआ` (saā)

- **Thai**: IPE1=`chedanavadhabandhanaviparāmosaālopasahasākārā` → IPE2=`chedanavadhabandhanaviparāmasoālopasahasākārā`
  - Failing pattern: `mosaā` → `masoā` (scrambled)
  - NOT detected in 2-syllable pair tests (requires longer sequence)

### Word 2: तिवण्टिपुप्फियत्थेरअपदानं
**Latin**: tivaṇṭipupphiyattheraapadānaṃ

- **Cyrillic**: IPE1=`tivaṇṭipupphiyattheraapadānaṃ` → IPE2=`tivaṇṭipupphiyattherāpadānaṃ`
  - Failing pattern: `raa` → `rā` (consonant+inherent_a + independent_short_a gets merged to long_ā)
  - Detected in pair: `रअ` (raa)

- **Thai**: IPE1=`tivaṇṭipupphiyattheraapadānaṃ` → IPE2=`tivaṇṭipupphiyatthareapadānaṃ`
  - Failing pattern: `theraa` → `tharea` (scrambled)
  - NOT detected in 2-syllable pair tests (requires longer sequence)

## Root Causes

### Cyrillic Bug: UNFIXABLE ENCODING AMBIGUITY

**Status**: This is a fundamental limitation of the Cyrillic encoding system, not a code bug.

**The Ambiguity**: The Cyrillic encoding cannot distinguish between:
1. **Consonant + dependent long ā**: द + ा (dā) → "д̣̇аа"
2. **Consonant + inherent a + independent short a**: र + अ (raa) → "раа"

Both patterns produce "consonant + а + а" in Cyrillic.

**Why This Happens**:

From `/Users/fsnow/github/fsnow/cst/src/CST.Core/Conversion/Deva2Cyrl.cs`:
- Line 76: Dependent vowel ा (U+093E) → "аа"
- Line 67: Independent vowel आ (U+0906) → "аа"
- Line 134: Regex inserts inherent 'а' after consonants not followed by dependent vowels, virama, or independent vowels

**Example 1: दा (dā) - long ā**
- Input: द (U+0926) + ा (U+093E)
- Cyrillic: "д̣̇" + "аа" = "д̣̇аа"
- Round-trip: "д̣̇аа" → द + ा ✓ (correct)

**Example 2: रअ (raa) - consonant + independent short a**
- Input: र (U+0930) + अ (U+0905)
- Cyrillic: "р" + "а" (inherent, inserted by regex) + "а" (independent अ) = "раа"
- Round-trip: "раа" → ???
  - Option A: Skip first 'а' (inherent), "аа" → आ = रआ ✗ (wrong - long ā)
  - Option B: Don't skip first 'а', "аа" → आ = अआ ✗ (wrong - lost the र)
  - **No correct option exists!**

**Impact**:
- Pattern `रअ` (raa): IPE1=`raa` → IPE2=`rā` (loses independent a, becomes long ā)
- Pattern `सआ` (saā): IPE1=`saā` → IPE2=`sāa` (scrambled)

**Why We Cannot "Fix" This**:
The Cyrillic encoding was designed by Russian and Mongolian stakeholders. Changing the encoding rules (e.g., using different markers for inherent vs independent 'а') would:
1. Break compatibility with existing Cyrillic texts
2. Violate stakeholder decisions about the encoding system
3. Require redesigning the entire Cyrillic orthography

**Conclusion**: This is an **inherent limitation** of the Cyrillic encoding system for Pali. Round-trip conversion will always fail for words containing consonant + independent short vowel अ patterns.

### Thai Bug
**Location**: `/Users/fsnow/github/fsnow/cst/src/CST.Core/Conversion/Thai2Deva.cs` lines 104-114

**Problem**: Vowel reordering creates ambiguous state

```csharp
// Pre-processing: move leading e vowel from before consonant to after
thaiStr = Regex.Replace(thaiStr, "\u0E40([\u0E01-\u0E2C\u0E2E])(?!\u0E2D)", "$1\u0E40");

// Pre-processing: move leading o vowel from before consonant to after
thaiStr = Regex.Replace(thaiStr, "\u0E42([\u0E01-\u0E2C\u0E2E])(?!\u0E2D)", "$1\u0E42");
```

**Analysis**:
- Thai writes some vowels BEFORE the consonant (เ, โ) but they're pronounced AFTER
- To simplify processing, these lines move them after the consonant
- On round-trip, we can't tell if the vowel was originally before or after!
- This creates ambiguity that leads to scrambling

**Fix Strategy** (as suggested by user):
- Use placeholder characters (e.g., high CJK range like U+FFF0) instead of moving actual vowels
- Replace placeholders with actual vowels at the end of conversion
- This preserves the original position information for round-trips

## Next Steps

1. Fix Cyrillic 'а' handling in Cyrl2Deva.cs
2. Fix Thai vowel reordering with placeholder approach
3. Run full validation suite to verify fixes
4. Generate new reports showing zero failures

