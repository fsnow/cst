# Lemma expansion — algorithm for review

**Status:** implemented (CST Reader Beta 5 development branch). **Purpose of this document:** to describe,
precisely and in one place, how CST Reader turns a word into its lemma and a lemma into the set of inflected
forms it searches for — so that a reviewer familiar with the Digital Pāḷi Dictionary (DPD) can assess whether
our use of the DPD data is linguistically and lexicographically sound. It is written to be reviewed: §8 lists
the specific open questions where expert judgement would change what we do.

Throughout, "we/our" means CST Reader; "DPD" means the upstream `dpd.db`. Pāli forms are shown romanized
(IAST); the running application renders them in the reader's chosen script.

---

## 1. What problem this solves

CST Reader is a reader of the VRI Chaṭṭha Saṅgāyana Tipiṭaka. Its search matches **whole-word tokens as literal
character patterns** — it has no grammatical knowledge of its own. A reader who wants "every occurrence of the
verb *pajānāti* across the canon" cannot get it from a single literal search: the finite verb alone appears as
*pajānāti, pajānanti, pajānāmi, pajāneyya, pajānissati, …*, and a naïve wildcard (`pajān*`) both over-matches
(unrelated words sharing the prefix) and under-matches (endings that drop the stem-final vowel, sandhi-fused
forms such as the negated *nappajānāti*).

**Lemma expansion** closes this gap by borrowing DPD's morphology: given any surface form the reader is looking
at, resolve it to its DPD headword(s), enumerate that headword's inflected forms from DPD, and search the corpus
for exactly those forms. DPD supplies the *candidate* forms; the corpus decides which are *real* (attested).

---

## 2. The DPD-lemma dataset

We do not ship `dpd.db` (2.1 GB). A build tool (`DpdLemmaBuilder`) extracts a small, corpus-agnostic subset —
**`dpd-lemma.db`** — from a given DPD release. Only the tables and columns the algorithm needs are copied:

| Our table    | Source in `dpd.db`        | Columns kept | Role |
|--------------|---------------------------|--------------|------|
| `lemma`      | `dpd_headwords`           | `id`, `lemma_1`→`lemma`, `pos`, `meaning_1`→`gloss`, **`derived_from`**, plus report fields (`root_key`, `construction`, `sanskrit`, `meaning_lit`, `pattern`, `ebt_count`, `source_1`, `sutta_1`, `example_1`, `synonym`, `antonym`) | one row per DPD headword (lemma) |
| `form_lemma` | `lookup` (`.headwords`)   | (surface `form` → `lemma_id`) edges | which headword(s) a surface form belongs to |
| `forms`      | `lookup` (`.grammar`, `.deconstructor`) | `form`, `grammar` JSON, [`deconstructor`] | the grammatical analysis of each surface form |
| `root`       | `dpd_roots`               | `root_key`, `root_meaning`, `root_group`, `sanskrit_root`, `dhatupatha_*` | root gloss for the report's etymology band |
| `meta`       | —                         | scope, DPD version, converter version, licence, attribution | provenance |

Two points a reviewer should note:

- The **form → lemma edges come from DPD's own inflection lookup** (`lookup.headwords`), and the **per-form
  grammar comes from `lookup.grammar`**. We do not re-inflect anything ourselves; we consume DPD's inflection
  engine output verbatim.
- The **`derived_from` column is copied verbatim** from `dpd_headwords`. Our notion of a "word family" (§4) is
  built entirely on this one column. We currently do **not** use DPD's `family_root` / `family_word` /
  `family_compound` / `family_set` columns. Whether that is the right choice is the central review question (§8).

Licence: DPD is CC BY-NC-SA 4.0; the project is non-commercial, and attribution + version travel in `meta`.

---

## 3. The expansion pipeline

The flow is **bidirectional and two-hop** — word in, family out — because a reader almost never knows the
citation lemma; they know the inflected word in front of them.

### Step 1 — Word → lemma (back-lookup)
`ResolveForm(form)` looks the surface form up in `form_lemma` and returns every headword it maps to:

```sql
SELECT fl.lemma_id, l.lemma, l.pos, l.gloss, l.derived_from
FROM form_lemma fl JOIN lemma l ON l.id = fl.lemma_id
WHERE fl.form = :form
```

A form with **more than one** candidate headword is a **homograph** (§5). This is where disambiguation belongs:
the reader (or an agent) picks the intended sense *before* the forward expansion runs.

### Step 2 — Lemma → forms (forward expansion)
`ExpandLemma(lemmaId)` returns every surface form of the chosen headword:

```sql
SELECT form FROM form_lemma WHERE lemma_id = :id
```

Optionally it also returns the headword's **word family** (§4).

### Step 3 — Forms → corpus (regex alternation)
The candidate forms are compiled into a single **anchored IAST alternation** and handed to the *existing* regex
search path unchanged:

```
^(pajānāti|pajānanti|pajānāmi|pajāneyya|pajānissati| … )$
```

The search layer converts this string to the application's internal encoding (IPE) with `Any2Ipe`, which
**preserves the regex metacharacters** `^ ( | ) $`, and matches it against the whole-word token index. So "lemma
expansion" is, mechanically, *"compile DPD's inflected forms into one regex and run it as an ordinary search."*
No new search infrastructure is involved.

The alternation is bounded at **2000 forms** (a whole large family can exceed this); if it is truncated the
result is flagged `ExpansionCapped` so the reader is told the count is a floor, not a total.

### Step 4 — Attach corpus counts
The search returns, per matched form, its **corpus occurrence count** and the number of books it appears in.
**All counts come from the corpus index, never from `dpd-lemma.db`.** A DPD candidate form with count 0 is
therefore *synthetic* here — a form DPD can generate but that does not occur in this edition — and is reported as
such (attested vs. candidate counts are shown separately).

---

## 4. Word family (the `derived_from` cluster)

For "show me this word's whole family everywhere," a single headword's forms are not enough: the reader usually
wants the finite verb *and* its participles, absolutives, and deverbal nouns. We assemble a **word family** as
the derivational cluster around the focus headword, taken in **both directions** so that it is the same set no
matter which member the reader started from:

Let `base = derived_from(focus)` if the focus is itself derived, else `base = headword(focus)`; and
`self = headword(focus)` (both with any homonym number stripped). The family is:

```sql
SELECT id, lemma, pos, gloss, derived_from FROM lemma
WHERE id = :focus                 -- the focus itself
   OR lemma = :base               -- the parent headword
   OR derived_from = :base         -- co-derived siblings
   OR derived_from = :self         -- the focus's own children
   OR lemma GLOB :base || ' [0-9]*' -- numbered-homonym parent headwords ('paññā 1', 'paññā 2')
```

The family's forms are then the **de-duplicated union** of each member's forms, expanded and searched as in §3.

Deliberate choices in this definition, for review:

- **Homonyms are NOT merged by spelling.** Two headwords that merely share a spelling but are different words
  (`dhamma 1` "nature/teaching" vs `dhamma 2` "phenomenon", both with empty `derived_from`) are kept apart —
  their shared surface forms are treated as genuine homographs, not one family. The homonym GLOB above is used
  *only* to reach a **parent** headword (because `derived_from` is stored unnumbered — `paññā` — while the
  headword is numbered — `paññā 1`), never to fold a base word's own spelling-twins together.
- **The cluster is (at most) one derivational hop in each direction**, not the transitive closure of
  `derived_from`. A chain *pajānāti → paññā → paññāya 2* is reachable from *paññā* in both directions but is not
  fully flattened from either end.

Worked example — focus *pajānāti* (verb, DPD id 39702):
- own forms: 34 DPD candidates → **27 attested**, **4,195 occurrences**;
- family adds, among others, the negated finite verb *nappajānāti* (id 35708, `derived_from = pajānāti`), the
  deverbal noun *paññā*, the absolutive/gerund *paññāya*, and the participles;
- the de-duplicated family union is the "whole word family" total the report shows.

---

## 5. Homographs

Because the corpus index tallies **surface strings**, two different words spelled identically cannot be
separated by counting:

- On the **input** side, a form's back-lookup (§ Step 1) may return several headwords — e.g. *paññāya* resolves
  to both the **gerund** of *pajānāti* and the instrumental/dative/etc. of the **noun** *paññā*. The reader
  disambiguates here, before expansion.
- On the **output** side, a form in the chosen lemma's own set may *also* belong to a headword **outside** that
  lemma's family. We flag such a form as a homograph and list the other senses, because its corpus count
  inevitably includes those other words and cannot be apportioned. A form shared only *within* the family (the
  finite verb sharing a form with its own participle) is **not** flagged — that is not a meaningful ambiguity.

This is a fundamental limit of a surface-string index, disclosed rather than hidden; per-occurrence
disambiguation of a homographic form is out of scope for this algorithm.

---

## 6. Grammatical analysis of each form

Each surface form carries DPD's analysis in `forms.grammar` — a JSON array of `[headword, pos, grammar]` triples
(one surface form can analyse under several headwords). For a given lemma's report we keep only the triples
whose headword is the **focus headword** and whose part-of-speech falls in the focus's coarse category
(nominal / adjectival / verbal), expand DPD's abbreviations to full words (`pr`→present, `instr`→instrumental,
…), and join a form's multiple analyses. So *pajānanti* shows "present 3rd plural", *pajānāmi* shows "present
1st singular / imperative 1st singular". Sandhi-fused enclitic forms (e.g. *pajānātīti* = *pajānāti* + *iti*)
carry no analysis in the current dataset scope — see §7.

---

## 7. Known limitations

1. **Family is one hop, not transitive closure** (§4). Deep derivational chains are not fully flattened.
2. **Homonym parent ambiguity.** `derived_from` is stored unnumbered, so when a parent headword has homonyms
   (`paññā 1`, `paññā 2`) we cannot tell which one a child was derived from and (coarsely) include all of them.
3. **Coarse part-of-speech grouping.** The family view groups members three ways (verbs & participles / nouns /
   adjectives & other). DPD's `pos` cannot by itself separate, e.g., a causative or passive or negated stem from
   the finite verb (all tagged `pr`), so finer grouping is not currently attempted.
4. **Enclitic / sandhi-fused forms.** Forms like *pajānātīti* (…+ *iti*) or *nappajānāti* (*na* + …) are
   genuine members but carry no direct grammatical analysis; DPD's `deconstructor` (retained only in the fullest
   build scope) would let us render "present 3rd singular, + iti", which is planned but not yet shipped.
5. **Synthetic vs. attested.** DPD can generate forms this edition does not contain; we report attested and
   candidate counts separately rather than presenting the paradigm as if all forms occur.
6. **Expansion cap** at 2000 forms (§3) can floor a very large family's total; disclosed via `ExpansionCapped`.

---

## 8. Open questions for review

These are the points where an expert's judgement would directly change the algorithm:

1. **Is `derived_from` the right basis for the word family, or should we use DPD's `family_*` columns?**
   We build the family from `derived_from` alone (§4). DPD also carries `family_root`, `family_word`,
   `family_compound`, and `family_set`. Would one of those (or a combination) give a more faithful "word family"
   than our one-hop `derived_from` cluster? Should the family be the **transitive** `derived_from` closure
   instead of one hop?

2. **Homonym resolution.** When `derived_from = 'paññā'` and both `paññā 1` and `paññā 2` exist, is including
   both parents acceptable, or is there a field that disambiguates the intended parent?

3. **Family grouping.** Is a `pos`-based three-way grouping defensible for display, or is there a better
   DPD-native grouping of a family's members (by derivational role: finite verb, participle, absolutive,
   action noun, agent noun, …)?

4. **Form → lemma basis.** We take the form→headword edges and per-form grammar directly from `lookup.headwords`
   / `lookup.grammar`. Is consuming `lookup` this way the intended use, and are there edges (e.g. compounds,
   sandhi) we are missing or should exclude?

5. **Negated and prefixed stems.** We treat *nappajānāti* etc. as family members via `derived_from`. Is that the
   right modelling, or should negation/prefixation be handled separately from lexical derivation?

6. **Anything we are under-using.** Are there DPD columns that would materially improve either the expansion
   (recall/precision of the form set) or the family view that we are currently ignoring?

---

## 9. End-to-end example

Reader is looking at the word **paññāya** and asks for its family across the canon.

1. **Back-lookup** `paññāya` → candidates: gerund of *pajānāti*, and forms of the noun *paññā* (and an
   adjective). **Homograph** → the reader picks the sense (say the noun *paññā 1*, DPD id 39994).
2. **Expand** id 39994 with family. `base = derived_from(39994) = pajānāti`; the family reaches up to the verb
   *pajānāti* (39702) and across to its co-derivations, and down to *paññā*'s own children.
3. **Search** the de-duplicated union of all those members' forms as one anchored IAST regex, converted to IPE.
4. **Report** each attested form with its corpus count and DPD analysis, the family grouped by role, and any
   form (like *paññāya* itself) that is also shared with a word **outside** this family, flagged as a homograph
   whose count cannot be split.

---

*Implementation:* `CST.Lemma.SqliteLemmaProvider` (data access), `CST.Avalonia.Services.LemmaSearchService`
(expansion → search), `CST.Avalonia.Services.LemmaReportService` / `LemmaReportRenderer` (the dossier). Dataset
built by `tools/DpdLemmaBuilder`. Related: [DPD dictionary integration spike](../research/DPD_DICTIONARY_INTEGRATION_SPIKE.md).
