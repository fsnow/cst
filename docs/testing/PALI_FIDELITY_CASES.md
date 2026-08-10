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

**Reasoning volume is not accuracy.** The failing run above produced substantially more reasoning than answer.
Worth remembering when the panel in #586 decides how much weight to give a visible reasoning pane.

### Open follow-up

Re-run this case **grounded** once #580 can inject Dhp 21 and the dictionary entry. If the same model then
answers correctly, that is the strongest available evidence for the injection-first design — and if it still
inverts the term with the passage in front of it, that is a much more serious finding about the model.

---

## Related

- **#587** — the evaluation harness that runs this set.
- **#584** — model registry and fidelity advisory; tier assignments should cite cases here.
- **#580** — the context bundler, which determines what "grounded" means in the table above.
- [`LOCAL_API_COLD_TESTS.md`](LOCAL_API_COLD_TESTS.md) — the surface C analog: cold-agent prompts and the model
  matrix. This file is the surface B counterpart, scoring *output fidelity* rather than *surface usability*.
