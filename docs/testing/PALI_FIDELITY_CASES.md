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

## Related

- **#587** — the evaluation harness that runs this set.
- **#584** — model registry and fidelity advisory; tier assignments should cite cases here.
- **#580** — the context bundler, which determines what "grounded" means in the table above.
- [`LOCAL_API_COLD_TESTS.md`](LOCAL_API_COLD_TESTS.md) — the surface C analog: cold-agent prompts and the model
  matrix. This file is the surface B counterpart, scoring *output fidelity* rather than *surface usability*.
