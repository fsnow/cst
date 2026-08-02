# Local API cold tests

The prompts we hand to a **cold agent** to exercise CST Reader's local HTTP API and MCP surface
(*surface C*), and the model matrix we run them across. Referenced from
[AI_INTEGRATION.md](../features/planned/AI_INTEGRATION.md) §14.

These are not hypothetical. **This is how the AI features were developed** — run as a fan-out of
subagents, one per model, with the friction reports driving the design of `llms.txt`, the tool
descriptions, and the error messages. They are recorded here verbatim so later runs stay comparable.

> **Do not reword a prompt.** The task and its phrasing are the instrument. An edited task breaks
> comparability with every earlier run, exactly as in
> [ai-prompts/README.md](ai-prompts/README.md). If a feature changes such that a prompt must change,
> add a new prompt rather than editing one in place, and say so in the results log.

## Scope and priority

**The surface targets frontier models.** Catering to non-frontier models is not a priority, and the
five-model matrix below is likely sufficient. That is a deliberate scoping decision, not an untested
assumption — see the competence findings below for what "non-frontier" currently means in practice.

This matters when reading a friction report: a finding from a weak model that a frontier model
handles cleanly is **not automatically a defect in the surface**. The §14 principle — that the doc
shape is calibrated to the weakest agent we intend to support — is bounded by *intend to support*,
and that floor sits at Haiku, not at a 7B local model.

## How the surface was built: the friction loop

These prompts were not a test applied to a finished API. They *were* the development method — a
recursive self-improvement loop:

1. Fan out subagents, one per model, each given a prompt and the API-only constraint.
2. Collect the friction reports.
3. Fix whatever caused friction — `llms.txt`, a tool description, an error message, a default.
4. Re-run.

**The termination condition was zero friction, not "good enough."** That is a stronger bar than it
sounds: every guessed parameter, every endpoint tried and abandoned, every place the agent wanted to
peek at the source counted as a defect in the surface. The loop ran until a cold agent could complete
every task without a wrong turn.

Two consequences worth holding onto:

- **A friction report is a bug report about the documentation**, not a transcript to skim. The whole
  value is in what the agent had to guess.
- **The surface is tuned to the models that ran the loop.** That is the in-family overfitting risk
  §14 names, stated concretely: the bar was set by Claude models reading docs written by a Claude
  model. It is the reason the Codex cell matters more than its share of the grid, and the reason a
  new model shipping is a reason to re-run rather than to assume the surface still teaches.

## Re-running the loop on a mature surface

The section above describes the loop that *built* the surface. Running it again later is a different
exercise, and the first re-run (2026-08-02, four Claude cells + Codex on prompt 4) is where these
were learned.

**The yield changes character.** During development, friction reports drove design: agents could not
complete tasks, and the fixes were structural. On a mature surface the tasks all succeed — every
cell completed all five, none was blocked — and what surfaces instead is *edge material the original
loop never probed*: an auth exception, a metadata contract, a misnamed key, a doc section that reads
as truncated. Do not read "no blocking failures" as "nothing to fix"; read it as the loop having
moved from construction to diagnosis.

**Doc drift is the dominant failure mode on a re-run, and only this loop catches it.** The sharpest
find was `search.md` describing lemma lookup as available *"if/when the API offers it"* — accurate
when written, obsolete once `/v1/forms/{lemmaId}` shipped, and never updated. Every cell dutifully
built the inferior regex family and discovered the better tool only at the last task. No unit test
can see that: the code is correct, the docs merely describe a shipped feature as hypothetical. It is
a **regression introduced by success**, and a cold agent is the only detector we have for it.
Whenever the surface grows, assume the docs now lie somewhere and re-run.

**Sloppiness in the runner is a feature.** Two real defects were found by making the mistake
naturally rather than by testing for it — sending `highlight` instead of `terms` to `navigate`, and
omitting the required `language` from `dictionary_lookup`. A careful operator who reads the docs
first will not find these. When re-running, let the first attempt at each call be the naive one.

**Treat a cell's report as a lead, not a result.** Every actionable claim from this round was
re-verified by hand before it reached an issue, and several first probes were confounded — a 409 that
was duplicate-suppression rather than a rejected key, a 404 that was a non-existent bookId rather
than a bad parameter, an `awk` range that silently matched its own terminator. Cross-cell
corroboration is the strongest signal available: of this round's findings only one (`/v1/status`
answering unauthenticated) was found independently by two cells, and it needed no verification.

**Separate harness artifacts from surface findings.** They look identical in a report. This round
produced two: the Codex cell died on a usage quota mid-run, and one Claude cell's `Write` was refused
by subagent policy (*"Subagents should return findings as text"*), so prompt 4's "write the report to
a markdown file" silently failed for that cell alone while the others complied. Neither says anything
about the API.

**Match the prompt to the cell's constraints.** Prompt 4 was the wrong choice for a quota-limited
cell: it writes its report only at the end, so the quota took the report with it. Prompt 1 exists for
exactly this and writes per task. Comparability across cells is worth less than surviving the run.

**Equalise the harnesses deliberately.** The Codex cell gets isolation for free by running from
`/tmp/<workdir>`; a Claude Code subagent starts *inside the repo* and can satisfy the whole task by
reading `llms.txt` from source — passing for entirely the wrong reason. Each cell was therefore given
a scratch directory and told the working directory was off limits. That is harness setup, not a
change to the prompt, and it must be applied to every cell or the cells are not comparable.

**Derive coverage from observed runs, not from reading the prompts.** The coverage table below was
written by reading prompt texts and was wrong within hours: prompt 4 does reach the lemma surface,
because "report the matching word-forms" leads there once the tools exist. Capability-shaped prompts
acquire coverage as the surface grows, so the table is only trustworthy when rebuilt from what the
cells actually called.

## Deciding what to act on — a judgment call, not a queue

**These runs are non-deterministic.** The same prompt, model and surface can produce a different
path and a different set of complaints on the next run. A friction report is evidence, not a work
list, and turning every item into an issue would bloat the docs and over-constrain the API — which
would itself damage the pointer-index shape the loop was built to protect.

Questions worth asking of each finding, roughly in order of weight:

- **Does it MISLEAD, or merely annoy?** This is the main axis. `search.md` describing a shipped
  feature as available *"if/when the API offers it"* sent every cell down the inferior path — that
  misleads, and is worth fixing at once. "`search.md` is dense prose that punishes skimming" is a
  style opinion from one cell about a doc that nevertheless taught it correctly; acting on it risks
  trading precision for readability in a document whose precision is the point.
- **Did more than one cell find it independently?** The strongest available signal. Of this round's
  findings only `/v1/status` answering unauthenticated was hit separately by two cells, and it needed
  no further verification.
- **Does it reproduce by hand?** Necessary before filing anything. Several first probes this round
  were confounded by unrelated behaviour.
- **Is the friction actually the docs WORKING?** An agent grumbling about noise in the regex
  inflection family is not reporting a defect: the doc warns about exactly that noise, and the
  warning is load-bearing. Agents sometimes complain about a difficulty the surface has already told
  them is inherent.
- **Would fixing it help the target audience?** The surface targets frontier models. A complaint only
  a weak cell raised, which every frontier cell handled cleanly, is usually not worth doc bulk.
- **Is it a defect or a feature request?** "No sutta-level addressing" is a real gap, but it belongs
  in the feature backlog, not in a friction fix. The loop finds both; only the first kind belongs in
  this cycle.

A finding that fails these tests is not necessarily wrong — non-determinism cuts both ways, and an
unreproduced item may simply be rare. Record it and move on rather than fixing it or deleting it.

## What makes it a valid test

Every prompt carries the same hard constraint: **use only the API and what it says about itself** —
no source code, no git repo, no on-disk docs. That constraint is the whole point. If the agent
flails, the surface is under-documented; it is not the agent's failure. A prompt that tempts the
agent toward the source asks it to record that temptation as a finding instead.

## The model matrix

| Cell | Why it is in the matrix |
| --- | --- |
| **Opus** | The ceiling — strongest coding agent. Establishes what the surface can support at best. |
| **Fable** | Second frontier reading, in-family. Also our design/review model, so a useful cross-check. |
| **Sonnet** | The realistic median for an agent driving the API day to day. |
| **Haiku** | The floor. The doc shape is calibrated to *the weakest agent we intend to support*, so this cell sets the spec rather than confirming it. |
| **Codex** | The only genuinely **out-of-family** cell we run, and so the only check on in-family overfitting — Claude writing docs that Claude then reads and rates well. Read its findings with the capability confound in mind (see *How to read a result*): the cell runs a mid-tier model, so a failure is not self-evidently a surface defect. |

### Running the Codex cell

**Egret is the machine.** The AI features were developed and tested there, and Codex is installed
only there — so the out-of-family cell cannot be run from anywhere else.

From a scratch workdir:

```bash
cd /tmp/<workdir> && codex exec \
  --skip-git-repo-check \
  -s workspace-write \
  -c 'sandbox_permissions=["disk-full-read-access"]' \
  -c 'sandbox_workspace_write.network_access=true' \
  --model gpt-5.6-terra \
  "$(cat /path/to/prompt.txt)" < /dev/null > out.log 2> err.log
```

Four things that each silently no-op or hang:

1. **`codex exec`, not bare `codex`** — bare `codex "prompt"` opens the interactive TUI and hangs.
2. **`--skip-git-repo-check`** — otherwise it refuses in a non-git workdir ("Not inside a trusted
   directory").
3. **`< /dev/null`** — without it, `codex exec` blocks on *"Reading additional input from stdin…"*.
4. **No `timeout` on macOS.** Don't wrap the run; background it and kill by pid.

The sandbox flags let it read `local-api.json` outside the workdir and reach `127.0.0.1` without
approval prompts. Default runs report `reasoning effort: none`; for a harder run add
`-c model_reasoning_effort="high"`.

**If a run hangs with empty logs, it is almost certainly a macOS security prompt, not a bad
command.** A Codex auto-update re-triggers the one-time warning, and on a headless machine the run
sits at 0% CPU producing nothing until it is dismissed *at the machine*. The tell is a live
`CoreServicesUIAgent` process. Seen 2026-07-19 (13 minutes of nothing; ~60s after dismissal) and
again 2026-08-02 after the 0.146.0 update. Do not kill and conclude the invocation is wrong.

## The harness is the second axis — and it is not a detail

The matrix is **model × harness**, and the harness is not a delivery mechanism for the same test.
Observed during development: **defects surfaced in Claude Chat + MCP that Claude Code never found**,
and on some tasks Chat was simply the better Pāli researcher.

The mechanism is escape hatches. A coding harness has a shell, a filesystem and a bias toward
solving problems with code — so when a tool description is unclear it can `curl` around the tool,
read the source, or write a script to brute-force the shape. **It routes around surface defects,
which hides them.** A chat client with only MCP has none of those exits: it must use the tools
exactly as described, which is precisely the experience the surface is supposed to deliver. Fewer
escape hatches makes it the more honest test of the tool layer, not the weaker one.

The role difference compounds it. A coding harness is prompted to complete a task mechanically; a
chat client engages with the scholarly question as asked. The same model in the two harnesses is
effectively two different researchers, and the surface has to serve both.

### Each surface has an intended audience — test it in that audience's harness

This is not "also spot-check in Chat." The two surfaces are built for two different clients:

| Surface | Intended audience | Therefore validated in |
| --- | --- | --- |
| **`/v1` HTTP API** | code-capable agents | a coding agent (Claude Code, Codex CLI) |
| **MCP** | chat clients | Claude Chat + MCP |

Testing MCP from a coding agent is the wrong harness for MCP: the shell lets it `curl` past any tool
description that fails to teach, which is the exact defect the run exists to find. And the reverse
cannot be done at all — a chat client has no shell with which to exercise `/v1`.

**Standing rule: a change to the AI features gets a full run of BOTH surfaces, each in its own
harness.** Not one, not a sample. Passing in Claude Code is weaker evidence for anything MCP-shaped,
because Code can succeed *despite* the text rather than because of it. Green in Code and red in Chat
is the expected direction of disagreement; the reverse is worth investigating.

## What we know about model competence in Pāli

Observed across the development runs. These are findings, and they constrain how a result is read.

**Frontier models understand Pāli, and understand it well.** This is remarkable and worth stating
plainly: Pāli is certainly not a target of anyone's post-training, and the canon is a few million
words — nothing at web scale. The competence is an emergent consequence of pre-training, presumably
from the derived material rather than the canon itself: a century and a half of philology, the
dictionaries, the grammars, and large volumes of aligned translation. Nobody aimed at it.

**Script makes no difference.** Frontier models reason about Pāli **equally well in Latin,
Devanagari, and the other supported scripts.** There is no romanized-versus-Devanagari competence
gap. Do not theorize one into existence from corpus-composition arguments — it has been checked.

**Small local models cannot do this at all.** Models small enough to run on Apple Silicon under
ollama have consistently failed: they hallucinate Pāli badly. This is what the long tail looks like
under parameter reduction — low-resource language knowledge lives in rarely-activated weights and is
the first thing to go. Scale is the operative variable, so this is unlikely to be fixed by
fine-tuning a small model.

**Near-frontier Chinese models are an open question, not an out-of-family guarantee.** They are
worth testing and would yield useful data. But they are **largely a product of distillation from
Western frontier models**, so a distilled model may inherit the very doc-reading priors we are trying
to test independently. Treat "this is an out-of-family cell" as a hypothesis to be checked — genuine
independence should show up as *differently shaped* friction, not merely more of it — rather than as
the reason for running it.

### Candidate cells not yet run

| Candidate | Standing |
| --- | --- |
| **Kimi K3** (Moonshot, open-weight ~2026-07-27) | 2.8T total / 104B active MoE, 1M context, native multimodal, MXFP4; benchmarks claim frontier parity. **Not locally runnable** — weights alone are ~1.4 TB and MoE needs experts resident, so cloud only, never Apple Silicon. Independence uncertain per the distillation caveat above. |
| Open models generally | The live alternative for widening out-of-family coverage. Untested. |
| Frontier out-of-family (newest Codex tier) | Out of scope. The Codex cell runs a mid-tier model, so its findings carry a capability confound — read them against the Haiku row to separate "surface unclear" from "model weaker". |

## What to watch — the four signals

From AI_INTEGRATION.md §14. Score every run on all four; they are what the prompts are *for*.

| Signal | Question | What it decides |
| --- | --- | --- |
| **Pointer discipline** | Does it fetch the right `/docs/*`, or only read inline? | Monolith vs pointer-index doc shape (§8) |
| **Invention rate** | Hallucinated endpoints or parameters? | How prescriptive `llms.txt` must be |
| **Tool-loop hold** | Does it sustain `search → occurrences → passage`, or collapse to one call and a guess? | Whether multi-step workflows need scaffolding |
| **Script compliance** | Does it honour the romanized default and `outputScript`? | Whether script handling is discoverable |

### How to read a result

Three confounds, each of which has already caused a wrong reading at least once:

- **A poor Devanagari run is evidence about OUR `outputScript` handling, not about the model's
  Pāli.** Since script makes no difference to frontier competence, a Devanagari failure in prompt 3
  or 5 localises to the surface — a leaked Latin value, an endpoint ignoring the parameter. Do not
  discount it as the model struggling with the script.
- **A Claude Code pass is weak evidence** (see the harness section). Re-run in Chat + MCP before
  calling a documentation change validated.
- **A Codex finding carries a capability confound** while that cell runs a mid-tier model. Compare
  against Haiku: if the in-family floor clears a hurdle the out-of-family cell trips on, the problem
  is comprehension of *our text*; if both trip, suspect capability.

## The prompts


### 1. Codex early run — write-as-you-go

The original Codex prompt. Writes the friction log **per task, immediately**, so a run that exhausts its token budget still leaves usable evidence. Use this shape for any long or expensive run.

Exercises: books → search → occurrences → passage → dictionary.

```text
Read  ~/Library/Application Support/CSTReader/local-api.json

Figure out how to use the API from there — it's meant to be self-describing —
and complete the research tasks below. Use curl.

Constraint (this is the actual test): rely ONLY on the API itself. Do NOT read
CST Reader's source code, its git repo, or any on-disk docs — the point is to
find out whether the API documents itself well enough for a fresh agent. If
you're tempted to look at the source, note it as a finding instead.

Create a document codex-friction-log.md in your working directory first. 
Write to the document per task, immediately after each task 
because I might run out of tokens during the run: 
did it work? Show the exact requests and a short result.
anything unclear, missing, or surprising; anything you had to
  guess; any endpoint that errored; and whether you could finish everything
  without looking at the source.

Tasks:
1. List the available books and identify the mūla (root) text of the Dīgha Nikāya.
2. Search for "mettā"; report the matching word-forms and roughly where they occur.
3. Pick one form and show several occurrences in context, with their citations.
4. Read the passage around one occurrence, then page forward to more text.
5. Look up "mettā" in a dictionary and give the gloss.
```

### 2. Smoke test — orientation and one search

The shortest cell. Answers only: is the service identifiable, does auth work, is it healthy, and can it count one term. Use when checking that a build's surface is alive rather than scoring it.

```text
Read the file ~/Library/Application Support/CSTReader/local-api.json — it describes a local HTTP API.
Follow whatever it points you to, connect to the API, and report back briefly:
(1) what this service is, (2) whether you were able to authenticate, and (3) the result of its
health/status check. Use ONLY the API and what it tells you about itself — no source code or other docs.

Using that same API, search the corpus for the exact word "mettā" and tell me how many times it
occurs and in how many books. One or two calls is enough — keep it brief.
```

### 3. Deep research — Devanagari throughout

The hardest prompt. Adds a **standing script constraint** (Devanagari for all Pāli *and* all book names) on top of five multi-step tasks, then asks where the output leaked Latin. This is the strongest test of script compliance and of proximity/co-occurrence search, and it deliberately requires addressing a paragraph *by number* rather than paging.

```text
See ~/Library/Application Support/CSTReader/local-api.json. It points to a local HTTP API for a
Pāli text reader. Using ONLY that API and whatever it tells you about itself, do the tasks below
and then write me a friction report. Do NOT read the app's source code, git repo, or any on-disk
docs — the API's own self-description is the whole contract under test.

Standing constraint: I read Devanagari, not romanized Latin. Give me all Pāli text and all
book/reference names in Devanagari throughout. Fall back to Latin only if some value genuinely
cannot be converted — and if so, flag it.

1. Catalog orientation. How many books are in the Abhidhamma Piṭaka, and what are the seven books
   of its mūla (root) layer? Give their names in Devanagari.

2. Term study — paññā. Report the full inflectional family of the stem paññā: the distinct
   case-forms and their occurrence counts, kept separate from unrelated look-alikes and from longer
   compounds. Where is it concentrated across the three Piṭakas?

3. Co-occurrence. Find passages where avijjā and saṅkhāra occur close together (the opening links
   of dependent origination), restricted to commentarial texts only. Give a couple of cited examples
   in context. If the API can't express a proximity/co-occurrence query directly, say so and
   approximate however it does allow.

4. Read + variants. Take one of those hits, read the surrounding passage, and include the variant
   readings (the footnote/edition variants) for it. Then open the paragraph that immediately follows
   it — addressed by its paragraph number, not by paging through cursors.

5. Cross-script. Give me the canonical dictionary gloss of paññā, and separately show me the single
   word paññā rendered in every script the reader supports.

End with a friction report: which endpoints each task needed, anything you had to guess, any mismatch
between what the docs promised and what actually came back, and whether the Devanagari output was
correct and complete on every endpoint (or where it leaked Latin / was wrong). Write the report to 
markdown and return the path. 
```

### 4. Standard research run

The baseline five-task run with a friction log written to markdown. The default cell for cross-model comparison — closest to prompt 1 without the per-task write discipline.

```text
Read  ~/Library/Application Support/CSTReader/local-api.json

Figure out how to use the API from there — it's meant to be self-describing —
and complete the research tasks below. Use curl.

Constraint (this is the actual test): rely ONLY on the API itself. Do NOT read
CST Reader's source code, its git repo, or any on-disk docs — the point is to
find out whether the API documents itself well enough for a fresh agent. If
you're tempted to look at the source, note it as a finding instead.

Tasks:
1. List the available books and identify the mūla (root) text of the Dīgha Nikāya.
2. Search for "mettā"; report the matching word-forms and roughly where they occur.
3. Pick one form and show several occurrences in context, with their citations.
4. Read the passage around one occurrence, then page forward to more text.
5. Look up "mettā" in a dictionary and give the gloss.

Report:
- Per task: did it work? Show the exact requests and a short result.
- A friction log: anything unclear, missing, or surprising; anything you had to
  guess; any endpoint that errored; and whether you could finish everything
  without looking at the source.
- Write the report to markdown file and return the path
```

### 5. Hindi-language run

Prompt 4's tasks, posed in Hindi, requiring Devanagari output and Hindi commentary. Tests whether a non-English-speaking user's agent can use the surface at all — the friction log stays in English so runs remain comparable.

```text
~/Library/Application Support/CSTReader/local-api.json पढ़ें।

वहाँ से पता लगाएँ कि API का उपयोग कैसे करना है — यह स्व-वर्णनकारी
(self-describing) होने के लिए बनाया गया है — और नीचे दिए गए शोध कार्यों को
पूरा करें। curl का उपयोग करें।

भाषा: यह प्रॉम्प्ट एक हिंदी-भाषी उपयोगकर्ता के लिए है। सभी पालि सामग्री
देवनागरी लिपि में हो (रोमन/IAST में नहीं) — इसमें खोज शब्द, शब्द-रूप, पाठ के
अंश, उद्धरण और शब्दकोश-अर्थ शामिल हैं। यदि API लिपि चुनने का विकल्प देता है,
तो देवनागरी चुनें; अन्यथा परिणामों को देवनागरी में लिप्यंतरित करें। अपनी
टिप्पणियाँ और उत्तर हिंदी में लिखें। (घर्षण-लॉग / friction log अंग्रेज़ी में
रह सकता है।)

प्रतिबंध (यही असली परीक्षण है): केवल API पर ही निर्भर रहें। CST Reader का
सोर्स कोड, उसका git repo, या डिस्क पर मौजूद कोई भी दस्तावेज़ न पढ़ें —
उद्देश्य यह जानना है कि क्या API किसी नए एजेंट के लिए खुद को पर्याप्त रूप से
दस्तावेज़ित करता है। यदि आपको सोर्स देखने का मन हो, तो इसके बजाय उसे एक
निष्कर्ष (finding) के रूप में नोट करें।

कार्य:
1. उपलब्ध ग्रंथों की सूची बनाएँ और दीघनिकाय के मूल पाठ की पहचान करें।
2. "मेत्ता" खोजें; मिलते-जुलते शब्द-रूपों की रिपोर्ट दें और वे लगभग कहाँ
   आते हैं।
3. एक रूप चुनें और उसके कई प्रयोगों को संदर्भ सहित उनके उद्धरणों के साथ
   दिखाएँ।
4. किसी एक प्रयोग के आसपास का अंश पढ़ें, फिर आगे बढ़कर अधिक पाठ देखें।
5. शब्दकोश में "मेत्ता" देखें और उसका अर्थ दें।

रिपोर्ट:
- प्रत्येक कार्य के लिए: क्या यह काम कर गया? सटीक अनुरोध (requests) और एक
  संक्षिप्त परिणाम दिखाएँ।
- A friction log: anything unclear, missing, or surprising; anything you had to
  guess; any endpoint that errored; and whether you could finish everything
  without looking at the source.
- Write the report to markdown file (in English) and return the path
```

### 6. Morphology — sandhi, homographs, and the lemma family

**Reconstructs a lost prompt.** One covering this corner existed during development and was not
captured, so the morphology endpoints have no run on record — the coverage gap is in the record, and
not necessarily in the history.

The newest part of the surface, and the least exercised: sandhi deconstruction, form→lemma
back-lookup, the attested paradigm, the multi-lemma union, and the lemma dossier. Written
capability-shaped like prompt 3 — no endpoint is named, so it stays valid as the surface grows.

The design leans on real traps rather than invented ones. `paññāya` is genuinely both the
instrumental/locative of the noun *paññā* and the absolutive of *pajānāti* — the exact homograph
`search.md` warns about, so task 2 cannot be answered by counting surface strings. Task 3 asks for **one combined figure** for
the family, and task 4 then narrows it to an **arbitrary subset** — the noun, the gerund and the
adjective. That distinction is load-bearing: the family total is reachable from a flag on the
per-lemma endpoint, but an arbitrary set of senses is not, so only task 4 forces the union.
The first draft had task 3 alone, and cells split: one reached `POST /v1/forms`, another answered
with the family flag — leaving coverage dependent on which path a model happened to pick. Task 4
removes that chance. This is the coverage illusion the suite exists to prevent, caught in the suite's
own material.

Two further corrections came from the first runs. The original task 1 asserted that `kiñcāpi` "isn't
in the dictionary" — **false**, DPD carries it as a headword with pos `sandhi`, and two cells said so.
A prompt must not contain a premise the surface will contradict, or the run scores the prompt rather
than the API. And "family of closely-related headwords" turns out to be ambiguous in the API's own
terms: nothing distinguishes case-split siblings of one lexeme from same-root different words. That
ambiguity is left in deliberately — it is a real property of the data, and how a cell handles it is
worth observing.

Note the extra friction-log question about **response format**: at the time of writing, one endpoint
in this group returns styled HTML rather than JSON without saying so in the docs.

```text
Read  ~/Library/Application Support/CSTReader/local-api.json
(on Windows: %APPDATA%\CSTReader\local-api.json)

Figure out how to use the API from there — it's meant to be self-describing —
and complete the research tasks below. Use curl.

Constraint (this is the actual test): rely ONLY on the API itself. Do NOT read
CST Reader's source code, its git repo, or any on-disk docs — the point is to
find out whether the API documents itself well enough for a fresh agent. If
you're tempted to look at the source, note it as a finding instead.

I am reading a commentary and keep running into forms I can't look up directly.

Tasks:
1. I meet the word "kiñcāpi" in the text. It looks like several small words fused
   together. What is it actually made of, and what does the whole thing mean?
2. The form "paññāya" is ambiguous — I'm told it can be two quite different words.
   Which ones? Give the sense of each, and tell me what would let me decide which
   is meant in a given sentence.
3. For the NOUN, list the inflected forms that actually occur in the corpus, with
   how often each occurs. Then give me a single combined figure for the whole
   family of closely-related headwords together — one total, not a per-headword
   list I have to add up myself.
4. Now narrow it. I do NOT want the whole family — only three senses I actually
   care about: the feminine noun, the gerund, and the adjective. Give me one
   combined occurrence figure for just those three, counted once each even where
   they share a form. Again: a single number, not three I have to reconcile.
5. Give me the derivation of that noun: its root, how it is built, and anything
   the reference says about its formation.
6. Finally, ground it: find a passage where the ambiguous form occurs, quote it,
   and cite it precisely enough that I could find it in a printed edition.

Report:
- Per task: did it work? Show the exact requests and a short result.
- A friction log: anything unclear, missing, or surprising; anything you had to
  guess; any endpoint that errored; anything whose RESPONSE FORMAT was not what
  the documentation led you to expect; and whether you could finish everything
  without looking at the source.
- Write the report to a markdown file and return the path
```

**Expected outcome** (for the human scoring the run, not part of the pasted prompt): `kiñcāpi`
resolves to three elements; `paññāya` yields both a feminine noun and a gerund among its candidates;
the noun's paradigm comes back with per-form counts; the family total arrives as a single number; the
three-sense subset arrives as a *different* single number, reached by a different call; the
derivation names the root; and the final citation carries a book and page, not just a paragraph.

**Failure signals**: reporting `kiñcāpi` as unanalysable; treating `paññāya` as one word; answering
task 3 with several totals rather than one; substituting a hand-built regex family for the lemma
tools; quoting a passage without a page-level citation; or claiming a task is impossible without
having consulted the doc slice that covers it.

## Coverage — and what no prompt reaches

| Surface | 1 | 2 | 3 | 4 | 5 | ai-prompts |
| --- | :-: | :-: | :-: | :-: | :-: | :-: |
| `books` / catalog | ● | | ● | ● | ● | |
| `search` | ● | ● | ● | ● | ● | ● |
| `occurrences` | ● | | ● | ● | ● | ● |
| `passage` | ● | | ● | ● | ● | ● |
| passage variants (footnote apparatus) | | | ● | | | |
| paragraph-addressed navigation (not paging) | | | ● | | | |
| proximity / co-occurrence search | | | ● | | | |
| `dictionary_lookup` | ● | | ● | ● | ● | |
| `convert` / `scripts` | | | ● | | | |
| status / health | | ● | | | | |
| `navigate` (drive the reader) | | | | | | ● |
| `lemma_lookup` / `/v1/lemma/{form}` | | | | ● | | |
| `lemma_forms` / `/v1/forms/{id}` | | | | ● | | |
| `lemma_forms_union` / `POST /v1/forms` | | | | | | |
| **`sandhi_split` / `/v1/deconstruct`** | | | | | | |
| **`/v1/lemma-report/{id}`** | | | | | | |
| **`dictionary_languages`** | | | | | | |
| **`llms.txt` as an MCP *resource*** | | | | | | |

Prompt 6 (below) is written to cover the four bolded `/v1` rows. The `●` in column 4 for the first
two lemma rows is **observed, not designed**: round 1 showed cells reaching them from "report the
matching word-forms", which is the capability-shaped effect at work.

The bolded rows have **no cold-agent prompt at all**. They matter disproportionately because several
of them require the agent to *discover a required argument* — `dictionary_lookup` needs `language`,
`occurrences` needs `bookId` + `term` — which is exactly the kind of thing a friction run is for.

Note that prompt 3 asks for "the full inflectional family of the stem paññā … kept separate from
unrelated look-alikes and from longer compounds." That is written **capability-shaped rather than
endpoint-shaped**, so it remains a valid task as the surface grows: an agent would once have had to
approximate it with `search`, and can now solve it directly with the lemma tools. Prompts written
this way do not go stale. Prefer that style when adding one.

### Prompts that were lost

Other prompts from the original loop were **not captured** — generated in-session and never saved.
Their *fixes* survive, fossilised in `llms.txt`, the tool descriptions, the romanized-by-default
decision, and the error messages. What is gone is coverage knowledge: we can no longer say which
parts of the surface were hammered and which were never touched. Hence the table above, built from
the other direction. **Capture a prompt when you run it.**

## Results log

One row per run. This is the point of keeping the prompts in the repo: the same task, re-run as new
models ship, gives a comparable read on how well the surface teaches an agent that has never seen it
— and on how much of any improvement is the model rather than our documentation.

| Date | Model | Harness | Prompt | Pointer discipline | Invention rate | Tool-loop hold | Script compliance | Friction report |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2026-08-02 | gpt-5.6-terra | Codex CLI (headless, Egret) | 2 (smoke) | ✅ index → topic slice | ✅ none | n/a (1–2 calls) | ✅ | none — clean run |

**2026-08-02, prompt 2.** Four calls, no wrong turns: `GET /llms.txt` → `GET /docs/search.md` →
`GET /v1/status` → `POST /v1/search {"query":"mettā","mode":"Exact"}`. Answered **393 occurrences in
104 books**, matching ground truth exactly. Notable: it followed the index to the *topic slice*
rather than pulling `/llms-full.txt` wholesale, and chose `mode:"Exact"` unprompted from "the exact
word" — both are the pointer-index doc shape (§8) working as intended. No 4xx responses; the only
strings resembling errors in the transcript are the docs *describing* error codes.

This is the first recorded row. It is a low bar — prompt 2 is the smoke test — so it establishes the
pipeline, not the surface.

Record the **harness** on every row — Claude Code, Claude Chat + MCP, Codex CLI, or whatever else.
A row without it cannot be compared against another, because the harness changes the result as much
as the model does.

Runs that predate this file drove the original design of `llms.txt`, the tool descriptions and the
error messages; their friction reports were not kept in the repo.

## Outstanding

- **#530 owes a full two-surface run.** The MCP surface moved to the 2026-07-28 stateless core and
  was verified with raw `curl` against the running app — a coding-agent harness exercising the
  *chat-client* surface, i.e. the wrong harness for what changed. The tool descriptions have not been
  re-exercised by a client that must rely on them. Owed: a Chat + MCP run, and a coding-agent run
  over `/v1`.
- Prompts 1, 3, 5 and 6 have no recorded run in any cell.
- **No prompt is MCP-shaped.** All six read `local-api.json` and most mandate `curl`, so every one is
  a coding-agent `/v1` prompt — a chat client cannot even start them, since it has no filesystem. By
  the audience rule above, half the surface and the whole chat-client audience are untested. An
  MCP-shaped prompt, opening at the tool list rather than at a handshake file, is the largest
  remaining gap.
- **`/v1/lemma-report/{id}` returns styled HTML, not JSON**, while the docs describe it beside
  `/v1/forms/{id}` as a "full dossier" with no mention of the format. Verified 2026-08-02. Prompt 6's
  friction log asks about response format specifically so a future run catches this class.
- No prompt covers the lemma family, `dictionary_languages`, or the `llms.txt` MCP resource.
- The results log is empty: the runs that built the surface predate this file.

## Related

- **#260** — cross-harness / cross-model validation of the local API. This file is its instrument.
- [ai-prompts/](ai-prompts/README.md) — task-shaped prompts for specific features (`navigate`,
  citation fidelity), same method, scored the same way.
- [AI_INTEGRATION.md](../features/planned/AI_INTEGRATION.md) §14 — why cross-model testing *sets*
  the spec rather than confirming it.
