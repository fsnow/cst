# Pāli fidelity cases — a fixed set for evaluating model output

**Status:** Living. Seeded 2026-08-10.
**Owned by:** #587 (surface B evaluation harness). Cited by #584 (model registry and fidelity advisory).
**Design context:** [AI_SURFACE_B.md](../features/planned/AI_SURFACE_B.md) §7, §13; AI_INTEGRATION.md §11.1.

A set of Pāli questions with authoritative answers, used to evaluate what models actually produce over this
corpus. It is the fixed eval set #587 runs across model releases, and the evidence base for #584's tier
assignments — a registry that says a model is not recommended for translating canonical text has to be able to
answer *why*.

## The organizing principle: track cases, not culprits

**A case is durable; a verdict is dated.** That *appamāda* means heedfulness, and that a model defining it as
"negligence" has inverted it, is permanent domain truth — checkable against the corpus and the dictionaries. That
a particular model failed it on a particular day is a disposable annotation.

Getting this backwards produces a file that rots: models change monthly, so a list organized around "models that
got things wrong" is stale almost immediately, unfair to whatever shipped since, and tells you nothing when you
want to re-check. Organized around cases, the file only gets more useful — re-running is cheap, and each new
model is a new row rather than a new document.

Two rules follow:

- **Record observations for every model in the matrix, including Claude.** A file that only collects lesser
  models' errors quietly becomes evidence for a conclusion we already assumed. That is the in-family
  overfitting risk AI_INTEGRATION.md §14 flags, arriving through the back door. If Claude never fails a case,
  that is a finding worth having recorded rather than presumed.
- **Always record whether the model was grounded.** Surface B's whole design bet is that injected context —
  the passage plus dictionary glosses (#580) — beats answering from memory. A case that fails ungrounded and
  passes grounded is direct evidence for that bet; the same verdict without the grounding state is nearly
  worthless.

## The model matrix

**The target audience is frontier models** (AI_INTEGRATION.md §11.1), and this file's verdicts are only
meaningful against a matrix that includes them. What is listed here is the *sub-frontier* half — cheap to run,
and the half that actually decides two open questions: whether #584's advisory has evidence behind it, and
whether the lemma injection #580 kept "for grammar and word-by-word only" earns its place, since §4 records
that **the interesting cell is the sub-frontier one**.

### Ollama cloud, free tier (probed 2026-08-11, fsnow's account)

Ollama's cloud catalogue is much larger than any one account can reach; most of it is subscription-gated. These
are the models that actually answer on a free account, largest first:

| Model tag | Parameters | Context |
|---|---|---|
| `nemotron-3-ultra:cloud` | 550B | 262K |
| `minimax-m3:cloud` | MoE (not reported) | 524K |
| `nemotron-3-super:cloud` | 120B | 262K |
| `gpt-oss:120b-cloud` | 117B | 131K |
| `gemma4:cloud` (= `gemma4:31b-cloud`) | 32.7B | 262K |
| `nemotron-3-nano:30b-cloud` | 32B | 262K |
| `gpt-oss:20b-cloud` | 20.9B | 131K |

**Gated behind a paid plan**, so not available for routine runs: `glm-5.2`, `glm-5.1`, `deepseek-v4-flash`,
`deepseek-v4-pro`, `kimi-k2.6`, `kimi-k2.7-code`, `minimax-m2.7`, `qwen3.5` (both cloud tags),
`mistral-large-3:675b`. `kimi-k3` needs a plan *and* extra usage on top.

> **`gpt-oss:120b` is not the best available and should stop being the default probe.** It is fourth of seven by
> size, and it is only the one appearing in the observations below because it was the model at hand
> (fsnow: *"you are testing with that one only because I am familiar with it… not because it is the best
> available"*). **`nemotron-3-ultra:cloud` is 550B and free.**

### One model per family — the run matrix

**Rule (fsnow, 2026-08-11): where two models of the same family are both free here and one is better, only the
better one is run.** Testing `nemotron-3-super` and `nemotron-3-nano` alongside `nemotron-3-ultra` spends quota
re-measuring the same lineage's weaker members, and a family's floor tells you little that its ceiling does not.

That reduces seven models to **four**:

| Model | Family | Why this one |
|---|---|---|
| `nemotron-3-ultra:cloud` | nemotron-3 | Best of three free variants (550B vs 120B vs 30B) |
| `minimax-m3:cloud` | minimax | Only free variant |
| `gemma4:cloud` | gemma | Only free variant |
| `gpt-oss:120b-cloud` | gpt-oss | Better of two free variants — and the matrix's deliberately weak cell |

The observations below **keep** their `nemotron-3-super`, `nemotron-3-nano` and `gpt-oss:20b` rows: those were
measured, the findings are real, and Case 4 rests on two of them independently. The rule governs what gets run
*next*, not what has already been learned.

### Screening run — 2026-08-11

All seven free-tier models, run **serially** (the account runs one cloud model at a time), on two items: Case 1
ungrounded with no system prompt, and Case 2 grounded through the real Translate preset.

| Model | Case 1 (ungrounded) | Case 2 (grounded translation) | Keep? |
|---|---|---|---|
| `nemotron-3-ultra:cloud` | PASS | **PASS** — all four lines | **yes** |
| `nemotron-3-super:cloud` | PASS | **PASS** — all four lines | **yes**, with a caveat below |
| `gemma4:cloud` | PASS | **PASS** — all four lines, concise | **yes** |
| `minimax-m3:cloud` | PASS | **PASS** — all four lines | **yes**, budget-hungry (#601) |
| `gpt-oss:120b-cloud` | PASS | **FAIL** — *matā* as "as they think" | no, for translation |
| `nemotron-3-nano:30b-cloud` | PASS | **FAIL** — *matā* as "as a mother" | no |
| `gpt-oss:20b-cloud` | **FAIL** — glossed *appamāda* as "failing or stumbling… to make a mistake" | **FAIL** — "free of pride… leads to annihilation"; "as mothers" | no |

**The shortlist is the top four.** `nemotron-3-ultra`, `nemotron-3-super`, `gemma4` and `minimax-m3` all render
Dhp 21 correctly, mark inline Pāli, refuse cross-corpus questions, and handle the apparatus note sensibly.
`gemma4` is the surprise: at 32.7B it matches the 550B model on this verse and is the cheapest capable option.

**Set aside for Pāli work:** `gpt-oss:20b` is not usable — it fails the most basic vocabulary question in the
canon. `nemotron-3-nano` and `gpt-oss:120b` both read the verse's key word wrongly (Case 4 below). Keep them
only as *deliberately weak* cells, where the question is what a poor model does with good context.

> **Encoding caveat on `nemotron-3-super`.** Its output contained mojibake where the niggahita should be —
> `amatapada??` rather than `amatapadaṃ` — while the same model rendered other diacritics correctly. This
> matters beyond cosmetics: **a mangled diacritic inside the quote markers cannot be script-converted**, which
> is exactly what §9's v1.1 proviso ("validate that marked content is actually convertible") was written for.
> Worth re-checking before this model is relied on, and worth being the first real test of that validator.

### One reading of these results

Every model passed the *ungrounded* vocabulary question except the smallest — but three then mistranslated the
verse **with the verse in front of them**. Recall and reading are different capabilities, and surface B depends
on the second. A matrix built only from "does it know what this word means" would have rated four of these
models as fine.

---

## Case 4 — vowel length: `matā` (dead) read as `mātā` (mother)

| | |
|---|---|
| **Prompt** | Translate Dhp 21 with the verse supplied. |
| **Correct** | `ye pamattā yathā matā` — "those who are heedless are as if dead". *matā* is the past participle of *marati*, to die. |
| **Authority** | The corpus. *matā* (short *a*) is "dead"; *mātā* (long *ā*) is "mother". **Vowel length is phonemic in Pāli** — they are different words, not spellings. The verse settles it independently: the same root supplies *amata*, *maccu* and *mīyanti* in the two lines around it, and "as a mother" is not a reading the sentence supports. |
| **Failure signature** | "as a mother", "like a mother", "as mothers" — or any reading of *matā* not derived from *marati*, including *maññati* ("think"). |
| **Why it discriminates** | Two of seven models made this exact error **independently**, and the four larger ones did not. It is mechanically checkable, it is invisible to a reader without Pāli, and it needs no judgement to score. It also probes the one thing romanized Pāli most needs from a model: that a macron is information, not decoration. |

### Observations

| Date | Model | Grounded | Verdict | Note |
|---|---|---|---|---|
| 2026-08-11 | `nemotron-3-nano:30b-cloud` | Yes | **FAIL** | "those who are heedless — as a mother —", then offered both "as a mother (does)" and "like a mother" as the ambiguity. It reported the line as ambiguous, which the prompt asks for — but between two readings that are both wrong. |
| 2026-08-11 | `gpt-oss:20b-cloud` | Yes | **FAIL** | "those who are attached are as mothers". Also lost the rest of the verse entirely. |
| 2026-08-11 | `gpt-oss:120b-cloud` | Yes | **FAIL** | Different error, same word: "as they think", reading *matā* from *maññati*. Reproduced across three runs. |
| 2026-08-11 | `nemotron-3-ultra`, `nemotron-3-super`, `gemma4`, `minimax-m3` | Yes | **PASS** | All four: "as if dead" / "as the dead" / "like the dead". |

**A model that volunteers an ambiguity note for the wrong pair of readings is more dangerous than one that just
gets it wrong**, because the hedging reads as care. Worth remembering when #586 decides how much authority the
panel's presentation lends an answer.

---

## Adding a case

A case earns its place when it has:

1. **An authoritative answer** — settled by the corpus or a bundled dictionary, not by opinion, so scoring needs
   no judgement call.
2. **A crisp failure signature** — something mechanically checkable, and hard to pass by luck.
3. **Discriminating power** — it should separate models rather than being universally passed or universally
   failed.

Prefer cases caught in the wild over cases invented at a desk. A failure someone actually hit is evidence that
the failure mode is real.

---

## Case 1 — `appamāda`: defining a term as its own opposite

| | |
|---|---|
| **Prompt** | "What does the Pali word appamada mean?" |
| **Correct** | Heedfulness, diligence, vigilance. Literally *a-pamāda*, non-negligence. |
| **Authority** | Dhp 21: `appamādo amatapadaṃ, pamādo maccuno padaṃ` — heedfulness is the path to the deathless, heedlessness the path to death. The verse contrasts the two directly, so the corpus itself settles the polarity. |
| **Failure signature** | Defines *appamāda* as heedlessness, negligence, carelessness — i.e. gives the meaning of *pamāda*, its opposite. |
| **Why it discriminates** | This is among the most quoted words in the canon and the term is transparently negated in its own morphology. A model that inverts it is not reliable on subtler vocabulary. |

### Observations

| Date | Model | Harness | Grounded | Verdict | Note |
|---|---|---|---|---|---|
| 2026-08-10 | `gpt-oss:120b` | Ollama OpenAI-compat, surface B provider layer (#578) | No | **FAIL** | Answered *"means heedlessness or negligence, especially the lack of mindful vigilance in practice"* — fluent, confident, and inverted. Spent 56 reasoning deltas reaching it. |
| 2026-08-11 | `gpt-oss:120b-cloud` | Same, with the #582 prompt layer over a #580 bundle | Yes | **PASS** (incidental) | Glossed it *"(heedfulness, diligence)"* and correctly contrasted it with *pamāda*. **Not a clean re-run:** the request was an Explain preset on Dhp 21, not the Case 1 prompt, so the term was glossed in passing rather than asked about. Suggestive, not conclusive. |

**Reasoning volume is not accuracy.** The failing run above produced substantially more reasoning than answer.
Worth remembering when the panel in #586 decides how much weight to give a visible reasoning pane.

### Open follow-up

Re-run this case **grounded** once #580 can inject Dhp 21 and the dictionary entry. If the same model then
answers correctly, that is the strongest available evidence for the injection-first design — and if it still
inverts the term with the passage in front of it, that is a much more serious finding about the model.

The 2026-08-11 row is the first evidence in that direction, but it is not the experiment: the term was glossed
in passing during an Explain request rather than asked about directly. **The clean paired run — Case 1's own
prompt, ungrounded and grounded, same model, same day — is still owed.**

---

## Case 2 — Dhp 21, second half: rendering the passage that is on screen

| | |
|---|---|
| **Prompt** | Explain or translate Dhp 21 with the verse supplied: `Appamādo amatapadaṃ, pamādo maccuno padaṃ; appamattā na mīyanti, ye pamattā yathā matā.` |
| **Correct** | Heedfulness is the path to the deathless (*amata*), heedlessness the path to death (*maccu*); the heedful do not die, those who are heedless are **as if already dead**. |
| **Authority** | The corpus itself, plus morphology settled by any bundled dictionary: *maccu* = death; *mīyanti* = they die (√mar); *matā* = dead (pp. of √mar); *yathā matā* = "like dead people". |
| **Failure signature** | Any of: rendering *maccuno padaṃ* as something other than death; rendering *mīyanti* as a verb of decline rather than dying; reading *matā* as from *maññati* ("think") rather than *marati* ("die"). |
| **Why it discriminates** | The whole verse turns on one root appearing four times (*amata*, *maccu*, *mīyanti*, *matā*). A model that loses the root loses the verse — and it does so **with the text in front of it**, which is what makes this a grounding case rather than a recall case. |

### Observations

| Date | Model | Harness | Grounded | Verdict | Note |
|---|---|---|---|---|---|
| 2026-08-11 | `gpt-oss:120b-cloud` | Ollama OpenAI-compat, #582 prompt layer over a #580 bundle | Yes | **FAIL** | Three of the four: *maccuno padaṃ* became *"the path of the samsaric (world-bound) condition"*; *amatapadaṃ* became *"the unsurpassed (or 'immortal') foot-path"*; and the second line became *"those who are heedful never decline; those who are negligent, as they think, decline"* — reading *matā* as *maññati* and losing *mīyanti* entirely. It got *appamāda* itself right (Case 1) while mistranslating the line that defines it. |
| 2026-08-11 | `gpt-oss:120b-cloud` | Same, second run (prompt revised for Case 3) | Yes | **FAIL** | Reproduced: *"the path of macca (the world of becoming)"*, *"the heedful do not fall away"*, *"those who are heedless, as they think, are lost."* Same three errors, different wording — and it invented a lemma, *macca*, for *maccuno*. |

**The grounded/ungrounded distinction does not save a model from its own morphology.** This failure happened with
the verse supplied verbatim, so it is not a recall failure — which makes it a sharper instrument than Case 1 for
separating models, and a direct argument for the fidelity advisory in #584.

---

## Case 3 — marker discipline: does the model mark *inline* Pāli?

| | |
|---|---|
| **Prompt** | Any preset. The system prompt (#582) instructs wrapping every span of quoted Pāli in `[[…]]`, explicitly "including single words and words inside lists or tables". |
| **Correct** | Every Pāli span marked, including single terms in running prose; nothing else marked. |
| **Failure signature** | Block quotations marked but inline terms left unmarked — typically rendered with markdown emphasis instead. Also: marking English, marking a translation, or leaving markers unbalanced. |
| **Why it matters** | This is not a fidelity case; it is the **feasibility** case for v1.1. Answer prose and quoted Pāli are separate axes (AI_SURFACE_B.md §9), and script conversion can only be enabled for models that mark reliably. Unmarked inline terms are the failure mode that matters most, because those are exactly the words a Burmese-script reader wants converted. |

### Observations

| Date | Model | Harness | Grounded | Verdict | Note |
|---|---|---|---|---|---|
| 2026-08-11 | `gpt-oss:120b-cloud` | #582 prompt layer, first live run | Yes | **PARTIAL** | Marked both block-quoted verse lines correctly and balanced every marker — but rendered every inline term (*appamāda*, *pamāda*, *amatapadaṃ*) in markdown italics instead. Enabling conversion for this model would convert the verse and leave the vocabulary in Latin, which is the worse half. |
| 2026-08-11 | `gpt-oss:120b-cloud` | Same, after the system prompt was revised | Yes | **PASS** | Every inline term marked — `[[appamāda]]`, `[[macca]]` — and every marker balanced. It also wrapped some markers in italics (`*[[…]]*`), which is harmless: the marked span itself stays clean, so conversion is unaffected. |

**The wording fix, and what it says about instruction design.** The original instruction already said "including
single words"; the model followed the markdown-emphasis convention anyway. What changed its behaviour was
**naming the competing convention and giving the consequence**: *"Use the markers instead of italics or bold.
Italicising a Pāli word is the usual convention in writing about these texts, and here it is the wrong one …
an italicised word is a word left behind in Latin."* Plus an inline example beside the block one.

The generalisable point for #582 and #587: when a model has a strong prior convention for a task, an instruction
that merely states the requirement loses to it. An instruction that *names the prior* and says what it costs
wins. Worth trying before concluding that a model cannot follow a formatting contract — and worth re-checking
per model, since this is one data point.

---

## The harness (#587)

The scorers and the machine-readable case set live beside this file:

- `docs/testing/ai-eval/cases.json` — the cases a machine re-runs. **This file stays the prose of record**: the
  authoritative answer, why a case discriminates, and the dated per-model observations.
- `AnswerScorer` — mechanical scoring for marker discipline, unmarked Pāli, ungrounded quotes, unsupported
  references, terminology counts, and per-case failure signatures. Unit-tested without a model or a network,
  because a scorer debugged against live output is debugged at several dollars a mistake.
- `AiEvalHarness` — the paid runner. **Opt-in and silent by default**: nothing happens unless `CST_AI_EVAL=1`.

```sh
CST_AI_EVAL=1 CST_AI_EVAL_BASE_URL=http://localhost:11434/v1 \
  dotnet test --filter "FullyQualifiedName~AiEvalHarness"
```

**The harness reports; it never decides.** Tier changes are a judgement recorded by a person, here and in
`model-registry.json`. A harness trusted to promote and demote models would quietly become the definition of
fidelity — the failure this file's organizing principle exists to avoid.

### First harness run — 2026-08-11

Two models, three cases. It reproduced Case 4 mechanically and found something new.

| Model | Case | Result |
|---|---|---|
| `gemma4:cloud` | translate | 68 marked quotes, 0 unbalanced, 1 unmarked (*Nibbāna*) |
| `gemma4:cloud` | cross-corpus refusal | **clean** |
| `gemma4:cloud` | word-by-word | 164 marked quotes, 0 unbalanced, 1 unmarked (*Nibbāna*) |
| `gpt-oss:120b-cloud` | translate | 9 quotes, **16 unmarked**, failure signature *"as they think"* matched |
| `gpt-oss:120b-cloud` | cross-corpus refusal | **18 unmarked**, and it cited **MN/DN/SN/AN references** |
| `gpt-oss:120b-cloud` | word-by-word | 71 quotes, **37 unmarked** |

Two findings worth keeping:

- **The marker-discipline gap is now a number.** `gemma4` leaves 1 term unmarked across a 164-quote answer;
  `gpt-oss:120b` leaves 16–37. That is the measurement §9 needs to decide the v1.1 script-conversion rollout
  per model, and it says these two models cannot share a setting.
- **`gpt-oss:120b` fails the cross-corpus refusal** — asked where else *appamāda* is discussed, it answered with
  sutta references rather than declining. That is the invented-citation hazard §6 is written against, observed
  rather than theorised. It is a **new** case, not a re-run of an existing one.

*Nibbāna* being flagged on `gemma4` is the scorer erring the way it was built to: an English-naturalized term
carrying a diacritic. A small unmarked count of such terms is expected and is not a defect.

One false positive was fixed by this run rather than by review: the scorer called *amataṃ padaṃ* an ungrounded
quote, when it is the sī/syā **variant reading from the print apparatus** — which the Translate preset
explicitly asks the model to cite. The apparatus is now part of the quotable corpus.

---

## Related

- **#587** — the evaluation harness that runs this set.
- **#584** — model registry and fidelity advisory; tier assignments should cite cases here.
- **#580** — the context bundler, which determines what "grounded" means in the table above.
- [`LOCAL_API_COLD_TESTS.md`](LOCAL_API_COLD_TESTS.md) — the surface C analog: cold-agent prompts and the model
  matrix. This file is the surface B counterpart, scoring *output fidelity* rather than *surface usability*.
