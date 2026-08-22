# Surface B — the in-app Assistant, v1 by context injection (In progress)

**Status:** In progress, and **the headline feature of beta 6**. The whole B1–B9 chain has shipped and is
user-visible: provider layer (#578), key storage (#579, macOS **and** Windows), context bundler (#580),
selection pipeline (#581), presets and the grounding contract (#582), orchestrator (#583), evaluation
harness (#587), **Settings UI (#585)** and **the Assistant panel (#586)**, plus the reader-state read path
and `POST /v1/ai/context-preview` (#593).

**Two things this document's older status paragraph got wrong, kept here because they are easy to re-derive
incorrectly from the section numbering below:**

- **B6, the model registry (#584), was built and then deliberately removed** (#670). Pāli ability is
  emergent and not predicted by benchmarks or model size, so a table of our own model judgements was the
  wrong artifact at any level of care. Provider-published capability shown verbatim replaced it.
- **The scalar provider model B7 shipped with is gone.** #689 replaced one provider / one base URL / one
  model / one credential with a list of **connections**, a per-connection model list, and a per-turn picker
  (#678, #691, #692, #693, #674), fed by the models.dev catalogue (#736, #737, #739, #740).

**Open against this surface for beta 6:** #711 (connection headers), #742 (base-URL versioning), #728
(models removed by a provider), #759 (credential storage format), #671 (reasoning effort), #672 (context
budget). The verification gap — #676, #677, #675, #651 — is tracked separately and is not beta 6 scope.

**Parent:** [AI_INTEGRATION.md](../planned/AI_INTEGRATION.md) — the design of record for the A–E surface map. §11.1 there
states the decided model-access *policy*; this document is the *implementation plan* for B.
**Tracker:** epic #186 → children #578–#587 (filed 2026-08-09; the per-item numbers are in §12).
**Prompted by:** Antonio's July 2026 question — *"do you have any plans to support DeepSeek as an AI provider
directly… the current setup through MCP and third-party servers is quite complex for non-developers."*
**Reviewed:** 2026-08-08 (Fable, adversarial) — see §15.

---

## 1. What B is, and what v1 is not

**B is Claude (or any configured model) *inside* the reader**: the user has a passage open, selects some of it
or none of it, picks a task — explain, translate, grammar, word-by-word — and gets an answer next to the text.

**v1 uses context injection, not tool calling.** The app assembles everything the model needs into one request
and sends it. There is no agent loop: no tool schemas, no `tool_use` round trips, no dispatch layer, no
malformed-arguments handling, no runaway-loop cap, no per-provider divergence in how faithfully a model fills a
schema.

This is a deliberate scoping call, and it is the right one because **the app already knows what the user is
looking at.** Tool calling exists so a model can decide *what to fetch*; here the app decides, and it decides
better — it has the cursor position, the selection, the book's place in the taxonomy, and the dictionary. The
tasks Antonio actually named (explain this, translate this, what does this word mean here) are fully answered
by injection.

**What we give up:** open-ended multi-hop research over the whole corpus — *"find every place the Buddha
addresses Ānanda about mindfulness and compare the wording."* That genuinely needs the model to choose its own
searches. It is a later capability tier (§11), not a v1 gap. Users who want it today have surface C.

AI_INTEGRATION §2 describes B as a companion "over the open text **or corpus**". **v1 is the open-text half
only**; the corpus half arrives with the tool-calling tier.

**B does not depend on surface C running.** B calls the tool layer **in-process**. No loopback port, no bearer
token, no Kestrel. A user can have AI features on, the local API off, and B works.

---

## 2. Architecture — and what the tool layer actually is today

```
    Book view (current reference, selection)
                  │
                  ▼
        AiContextBundler ──────▶ IPassageTool, IDictionaryTool, IScriptTool   (in-process, from DI)
                  │              ILemmaSearchService, ILemmaReportService     (nullable — optional asset)
                  │              CST.Core book data                            (for BookContext)
                  ▼
        PromptTemplate (per preset) ──▶ rendered request
                  │
                  ▼
           IChatProvider ──┬── AnthropicMessagesProvider     (Claude direct)
                           └── OpenAiCompatibleProvider      (DeepSeek, OpenRouter, Ollama, LM Studio, …)
                  │
                  ▼ streamed deltas
        AiResponseViewModel ──▶ Avalonia panel  (never the book WebView — see §8)
```

**Correcting a wrong assumption.** `ICorpusTools` (`src/CST.Core/Tools/ICorpusTools.cs`) *documents itself* as
the facade "the local HTTP API and the in-app AI surface (B) both call" — but it is **referenced nowhere except
its own definition file.** Nothing implements it and nothing consumes it. Surface C resolves the focused
interfaces individually (`LocalApiServer.FromServiceProvider`, lines 131-142). So:

- **B consumes the same individual DI-registered interfaces surface C does** — `ISearchTool`,
  `IDictionaryTool`, `IPassageTool`, `IScriptTool`. Drift risk is bounded because both surfaces resolve the
  same singletons, not because they share a facade.
- **`INavigationTool` is declared but unimplemented.** The bundle's `BookContext` (piṭaka, commentary level,
  nikāya path) therefore has no tool-layer source today. **Decision required in B3:** read `CST.Core` book data
  directly (cheap, recommended for v1) or implement `INavigationTool` (correct long-term, unbudgeted here).
- **Lemma is not in the tool layer at all.** `ILemmaSearchService` / `ILemmaReportService` live in
  `src/CST.Avalonia/Services/` and are resolved with `GetService` — **nullable, because the DPD-lemma asset is
  an optional separate download.** Two consequences: the bundler cannot live in `CST.Core` beside the
  contracts, and the lemma-dependent presets need defined behavior when the asset is absent (§4).
- **Either delete `ICorpusTools` as dead, or make B the occasion to implement it.** Do not leave a third
  document asserting it is load-bearing. Add a composition test in the style of `FromServiceProvider`'s
  "caught by one composition test instead of shipping" comment.

---

## 3. The context bundle — the heart of the feature

Everything that determines B's output quality lives here. It is **data, not a string**: serializable,
inspectable, diffable, and unit-testable without a network call or an API key.

```csharp
public sealed record AiContextBundle(
    string            TaskId,          // preset: explain | translate | grammar | word-by-word
    string            OutputLanguage,  // language of the ANSWER — separate axis from Pāli script (§9)
    string?           UserQuestion,    // free-form, when the preset allows it
    PassageResult     Passage,         // StructuredNotes: true, OutputScript: Latin
    SelectionContext? Selection,       // Latin-converted, normalized, located in the window (§3.1)
    IReadOnlyList<GlossEntry>  Glosses,      // PLAIN-TEXT definitions + source attribution
    IReadOnlyList<LemmaEntry>  Lemmas,       // stem + inflection; empty when the asset is absent
    BookContext       Book,            // title, piṭaka, commentary level, nikāya path
    CitationRef       Citation,        // bookId, normalized reference, per-edition pages
    Provenance        Provenance,      // app version, corpus revision, per-dictionary source versions
    BudgetReport      Budget);         // included / trimmed / UNAVAILABLE, + estimated tokens
```

Design rules, each load-bearing:

- **Text comes from `IPassageTool`, never scraped from the WebView DOM.** The tool layer owns the format; the
  DOM is a rendering. `StructuredNotes: true` gives brace-free quotable Pāli with the print apparatus arriving
  as separate data — exactly what you want a model to translate.
- **Latin script to the model** (AI_INTEGRATION §11). What the *user* sees is a separate question (§9).
- **`OutputLanguage` is not optional.** Translate — into what? Explain — in what language? The motivating user
  reads Burmese script; other testers are not Anglophone. This is the first knob a non-English user hits, and
  retrofitting it touches the bundle, every template, Settings, and the eval baseline.
- **Lemma data is our differentiator.** No general-purpose chat has stem + inflection for Pāli. But it is an
  *optional asset*, so see §4.
- **`BudgetReport` distinguishes four states**: included, **trimmed for budget**, **unavailable** (asset not
  installed) and **empty** (gathered fine, nothing there). Conflating unavailable with trimmed makes a missing
  download look like a budget problem; conflating it with empty makes a healthy window with no print apparatus
  look like a missing asset.
- **There is deliberately no "the passage was truncated" flag.** An earlier version derived one from
  `PassageResult.NextCursor`, which is wrong: that cursor means the window ended before the end of the BOOK
  FILE, not before the end of the requested paragraph, so on the real corpus it is set for nearly every request
  and the badge would always fire. A fidelity signal that always fires is one users learn to ignore. The bundle
  reports `ParagraphsCovered`, measured from where the window actually ended (#602), and a trim signal needs
  support from the passage reader.
- **A failed fetch is loud.** The passage tool reports "book not available", "reference not found" and
  unsupported reference kinds all as empty text with the reason in `NormalizedReference` — the field the app
  renders as the citation. Bundling one produces a healthy-looking request citing *"book not available"*, so
  the bundler throws instead.
- **`Provenance` stamps app version, corpus revision, and dictionary source versions.** Trivial now; without
  it, B9's cross-release eval regressions are unattributable.

**The app owns the citation, the model owns only the prose.** The reference, page numbers, and book title are
rendered by us from `CitationRef` — never parsed back out of model output. A model that garbles the citation in
its prose cannot produce a false citation in the UI.

### 3.1 Selection is a pipeline, not a field

The selection is the one input that genuinely is scraped from the DOM, and it needs real work before it is
usable. `GetWebViewSelectionAsync` (`BookDisplayView.axaml.cs:226`) returns the selection **in the user's
display script**, round-tripped through the `document.title` channel with a **700 ms timeout that returns null
on failure**. So:

1. **Convert display script → Latin** before any dictionary or lemma lookup. For a Burmese-script reader this
   is a hard prerequisite, not a refinement — which means it is a prerequisite for the two presets that
   differentiate us.
2. **Normalize whitespace and locate the selection within the fetched passage window.** On no match, fall back
   to the whole window and record that in `BudgetReport` rather than silently ignoring the selection.
3. **Handle the null/timeout case explicitly.** Otherwise it surfaces to the user as "the AI ignored my
   selection" — a failure state the plan previously had no answer for.

This is work item **B3a** (§12), and Grammar and Word-by-word are gated on it.

---

## 4. Presets

Each preset is a triple: *which bundle pieces to gather*, *the token budget*, *the instruction template*.
Templates are data (embedded resources), user-editable with reset-to-default, per §11.1.

| Preset | Gathers | Requires | Notes |
|---|---|---|---|
| **Explain** | passage, book context | — | The default. Free-form follow-up allowed. |
| **Translate** | passage, apparatus notes | — | Fidelity-sensitive (§7). Apparatus matters: variant readings change translations. |
| **Grammar** | selection (or sentence), lemmas | B3a + DPD | Leans on lemma resolution. |
| **Word-by-word** | selection, lemmas per token | B3a + DPD | Most grounded preset; smallest hallucination surface. |

### No dictionary glosses in v1 — decided 2026-08-10 (fsnow)

An earlier cut of #580 injected dictionary glosses for words in the passage. They are out, and the reason is
**context clutter with little expected benefit**: the app must choose which words to look up *before* anything
has read the passage, so the choice is a heuristic guess, and the entries it produces then compete for context
with the passage itself — the one thing certain to be relevant. On a small-context model that trade is plainly
bad; on any model a confident but irrelevant gloss is worse than silence, because it hands over a plausible
authority. Lookups belong to the **tool-calling tier**, where the model asks for the word it has decided it
needs.

**This supersedes the DPD-prerequisite decision recorded earlier the same day.** That decision followed from
glossing needing form→lemma resolution; with glosses gone, DPD is a prerequisite only for the *grammatical*
presets, and surface B v1 runs without it.

**Lemma data survives, for grammar and word-by-word only** — on exactly the logic that condemns glossing. It is
scoped to what the **user selected**, so the relevance signal comes from the user rather than from a heuristic
of ours, and it is grammatical analysis rather than a definition to be believed.

**Whether it earns its place is an empirical question, deliberately left open** (fsnow, 2026-08-10): *"let's
wait and see how real non-frontier models do with this before we rip it out."* The reasoning that removed
glosses — clutter competing with the passage — applies here too in principle, and the difference is a judgement
call that evidence can settle better than argument. **#587 should run it as a paired comparison**: the same
grammar and word-by-word cases, with lemma data injected and withheld, across the model matrix. The interesting
cell is the sub-frontier one, since a frontier model may carry the morphology anyway while a smaller model may
be the one that needs it — or the one most crowded out by it. Do not remove it before that comparison exists.

> **A DPD defect recorded here so it is not rediscovered.** DPD distinguishes homographs with numeric suffixes
> on the lemma (`mata 1.1`), and those suffixed strings do not appear in the form index — roughly a quarter of
> its ~89,000 lemmas. Resolving a lemma back to a dictionary entry **by headword string therefore fails for
> every homograph**, which is precisely the case such a lookup exists to serve. The correct join is on
> `LemmaId`, which both `LemmaCandidate` and `DictionaryEntry` carry. This is a bug to fix when lookups return
> in the tool-calling tier, not a reason against them.

The **system prompt** is shared and carries: house terminology conventions (stated *positively* — §10), the
grounding contract, the scope declaration (§6), the cross-corpus refusal (§6), and the Pāli-quote marking
instruction (§9).

---

## 5. Provider layer

One narrow interface, two adapters. Both stream; both take a `CancellationToken`.

```csharp
public interface IChatProvider
{
    IAsyncEnumerable<ChatDelta> StreamAsync(ChatRequest request, CancellationToken ct);
}
```

**Wire shapes** (inlined so this doc stands alone):

- **Anthropic Messages** — `POST /v1/messages`, SSE. `max_tokens` is **required** (set per preset). Current
  Claude models **reject `temperature`/`top_p`/`top_k` with a 400** — the adapter must not send defaults, and
  B7 must not offer a temperature control.
- **OpenAI-compatible** — `POST {baseUrl}/chat/completions`, SSE, deltas at
  `choices[0].delta.content`. **BYO base URL** is what makes one adapter serve DeepSeek, OpenRouter, Together,
  Ollama, and LM Studio. This is the direct answer to Antonio: base URL, key, model name — no MCP, no
  third-party server.

**Per-provider quirks the "one adapter serves everyone" framing hides:**

- **DeepSeek reasoning models emit reasoning through the same OpenAI-compatible surface** (a
  `reasoning_content` delta field, or inline think-tags). An adapter that naively concatenates deltas will
  render chain-of-thought into the answer panel. Skip or segregate reasoning fields, and defensively strip
  think-tags.
- **`max_tokens` is not an answer budget — deferred rather than guessed (2026-08-11, fsnow).** An earlier cut of
  #582 sized a cap per preset from the expected length of each answer. That is the wrong quantity to predict:
  on a reasoning model the cap covers reasoning *and* answer, and reasoning volume varies by an order of
  magnitude between models — `minimax-m3` needed 3,177 completion tokens to translate a two-line verse against
  a 2,400 budget (#601). **The per-preset table remains, with every entry unset**, as the seam #584/#585 fills
  in from Settings; today the OpenAI-compatible adapter *omits* the field and the Anthropic adapter — where it
  is required — sends the largest value valid across every current Claude model. That is **64K, not 128K**:
  Opus 5, Sonnet 5 and the Opus 4.x family all allow 128K, but **Haiku 4.5 caps at 64K**, and the model id is
  whatever the user typed into Settings. Cost is controlled by reporting what each call spent (§10), not by a
  truncation the user never sees.
- **A cap we do not set can still be hit, so truncation is detected rather than assumed away** (`length` on the
  OpenAI-compatible shape, `max_tokens` on Anthropic's → `AiErrorKind.Truncated`). Omitting the field hands the
  ceiling to the endpoint, not to nobody; and the Anthropic adapter always sends one because the API requires
  it. The dangerous case is not the blank panel but the **half-written answer**: a stream cut off mid-verse ends
  exactly as a complete one does, so without this the app renders a partial translation under a citation and
  nothing distinguishes it from a finished one. Reported *after* the usage it explains — an `Error` delta is
  terminal, and on both wire formats the token counts arrive with or behind the finish reason.
- **`HttpClient`'s 100 s default timeout will kill long generations.** Use `Timeout.InfiniteTimeSpan` with
  per-request cancellation, plus an adapter-level *idle* timeout.
- **Mid-stream network drop is distinct from cancellation** — partial text is already on screen. Keep the
  partial, append an inline error.

**Concurrency:** invoking a preset while a stream is running is **cancel-and-replace**, not queue. That is the
right semantic for a reader and it shapes B5's orchestrator.

All errors normalize into one `AiError` type: not-configured, no-network, 401, 429 + retry-after,
context-too-long, provider-shaped. **Provider error bodies are sanitized before logging** — some providers echo
request material.

No SDK dependency — two adapters over `HttpClient` is less code than either SDK's transitive graph.

**No new macOS entitlement.** `com.apple.security.network.client` is already at `package-macos.sh:237` for the
XML/DPD updaters. Still verify on a **signed, notarized build** — per CLAUDE.md, entitlement failures are
silent (calls hang, CPU spins) rather than erroring.

---

## 6. Grounding — the scope problem

Injection's characteristic failure is not hallucination; it is **truthful output over a silently narrower scope
than the user assumed.** "Explain this sutta" against a ~1200-char window produces a confident, correct-looking
answer about three paragraphs. Three fences:

1. **Display the scope next to the answer** — "¶ 271–273", rendered by us from `CitationRef`.
2. **Instruct the model to name its scope** in the system prompt, and to say so when the passage does not
   contain the answer.
3. **Refuse cross-corpus questions explicitly.** "Where else does this phrase occur", "who is Ānanda" need
   search or DPPN, not the passage. The system prompt must direct a graceful refusal pointing at surface C —
   *not* let the model improvise from training data, which is exactly the invented-citation hazard.
4. **A trimmed passage gets a visible "partial passage" badge**, driven by `BudgetReport`. A *translation*
   labelled as of a passage that was silently truncated is a fidelity failure specific to this corpus.
   *Implemented 2026-08-21 (#672).* Until then the badge could not fire at all: the bundler wrote
   `BundlePartState.Included` for every passage on every path, so the signal it reads was never raised. It now
   fires when a selection was longer than the cap and was cut — the only case that currently trims one.

> **Fence 4, and how it was finally driveable — 2026-08-11 (#580 → #602).** #580 could not implement this. The
> obvious signal, `PassageResult.NextCursor`, is non-null whenever the window ends before the end of the *book
> file* rather than the requested paragraph, so on the real corpus it is set for almost every request — a badge
> driven by it would have fired always, and a fidelity signal that always fires is one users learn to ignore.
> What shipped instead was the weaker true statement `WindowMayExtendPastReference`.
>
> That understatement then hid a real bug (#602): a Translate window on Dhp 21 covered **25 paragraphs** while
> the citation read "paragraph 21", and the notice said only that the window "may extend past" it. The passage
> reader now reports the paragraph in effect where the window **ended**, so `BudgetReport.ParagraphsCovered` is
> measured rather than guessed — the citation names a range, the notice states the count, and the badge this
> fence asks for is at last driveable from a signal that means something.

Free-form follow-up on the Explain preset is the leak in the dam — keep it, but these are its seatbelts.

---

## 7. Model quality as a fidelity feature

§11.1 decided: curate, advise, never block. Implementation:

- A **model registry** shipped as data: `{ providerId, modelId, tier }` where tier is `recommended` /
  `permitted` / `discouraged-for-translation`, plus **`unrated`** for anything not listed.
- **`unrated` is not `discouraged`.** A registry that punishes anything it hasn't heard of ages badly and would
  have flagged half of today's good models a year ago.
- The advisory fires **at selection time in Settings** and as a **badge on translate output**: *this model is
  not in the recommended tier for translating canonical text; check its output against the Pāli.* Never a block.
- Recommended tier defaults **Claude-first**, per the standing policy.

---

## 8. Rendering — generated text is structurally separate

**Generated text renders in an Avalonia control, never inside the book's CEF WebView.** Two payoffs from one
decision:

1. §11.1 requires generated text to be visually distinguishable from canonical text. A different *widget* makes
   that structural rather than a styling convention someone can later erode.
2. It keeps the fragile surface untouched. Streaming tokens into the CEF DOM would put live mutation on the
   SIGSEGV-on-reparent component **and** couple B8 to the float/unfloat dispose-recreate cycle — generated text
   would be destroyed with the browser on every float.

Implementation notes for B8: use a **selectable** text control (users will copy translations), **throttle
per-delta UI updates**, and plan for per-script font handling (`FontSettings.ScriptFonts` already exists) once
§9's conversion lands.

Trade-off: no rich inline rendering (no click-through glosses inside generated prose) in v1. Acceptable, and
recoverable later without moving the text back into the book view.

---

## 9. Two axes: language of the prose, script of the quoted Pāli

These are **separate** and were previously conflated. Decide both together in Settings — "Answer language" and
"Pāli script in answers" — rather than a release apart.

- **Language of the answer** — `OutputLanguage` in the bundle (§3), user-set, v1.
- **Script of quoted Pāli** — we send Latin; Antonio reads Burmese. Naively converting model output would
  mangle the surrounding prose.

**Decision: instruct Pāli-quote marking from v1, render verbatim in v1, convert in v1.1.** The system prompt
tells the model to wrap quoted Pāli in a marker from day one, even though v1 strips the markers and renders
Latin. Reason: if marking arrives in v1.1 the prompt shape changes *after* B9 has baselined, and we learn
nothing about per-model marker discipline in the meantime — which is precisely the data that decides whether
conversion is safe to enable per model tier.

v1.1 conversion proviso: **validate that marked content is actually convertible** (pure Pāli character set)
and leave it Latin otherwise — models will occasionally mark English or emit malformed diacritics.

### The marker is `[[…]]` — decided 2026-08-11 (#582)

Two constraints. It must not occur in the corpus, and it must not occur in ordinary answer prose in **any**
language a user might set as their answer language.

A survey of all 217 XML books found **zero** occurrences of `[[`, `]]`, `«`, `»`, `⟦`, `⟧`, `{`, `}`, `|`, `~`
and `¶`; `^` appears 3 times, `` ` `` 6 times, `§` 53 times. So corpus collision eliminates none of the leading
candidates. (Incidentally: braces being absent from the source confirms that the ones around inline notes are
added by the passage renderer.)

The **second** constraint is what decides it. `«…»` is out: it is the standard quotation mark in Russian and
French, so a user answering in either would have every ordinary quotation read as marked Pāli — a v1.1
conversion bug seeded in v1. `⟦…⟧` is collision-free but measures a weak model's *Unicode fidelity* rather than
its instruction-following, which inverts what B9 is trying to learn. `[[…]]` is emittable by any model, absent
from the corpus, and claimed by no language's punctuation.

**Unbalanced markers are stripped anyway and counted, never rendered.** A literal `[[` on screen is a visible
defect in the answer for a rule the user never asked about; the imbalance is real signal about the model, so it
is recorded for B9 rather than silently repaired. Marker fragments split across stream deltas are handled by a
hold-back filter — the same hazard as the think-tag filter, and the same failure if ignored.

---

## 10. Failure, privacy, and terminology

- **Not configured** — no key/model: the panel explains what to set, links to Settings. Never a raw error.
- **Offline** — B is the one AI surface that needs the network (§11). Say so plainly; do not retry-storm.
- **Cancellation** — a visible stop control; the token cancels the HTTP stream.
- **Usage** — show tokens in/out and, where reported, cost after each call. The user is paying.
- **What leaves the machine** — passage text, glosses, the user's question, and the system prompt go to the
  configured provider. State this in Settings in plain language. The **fully local option is real**: a local
  runner through the OpenAI-compatible adapter sends nothing off the machine.
- **Prompt logging** — bundle contents and responses contain the user's question. Do not log them above Debug.
- **Reading your shell's environment** — when an AI feature is enabled, or a connection has been set to use a
  variable, CST Reader runs your login shell once per launch and asks it for its environment. This is what
  makes a key exported from `~/.zshrc` or `~/.bash_profile` visible at all: an app launched from Finder, the
  Dock or Spotlight is started by launchd and inherits launchd's environment, not your shell's (#817). Only
  variables the provider catalogue or one of your connections actually names are kept; everything else is
  discarded as it is read. Nothing is written to disk, and the log records a count, never a name and never a
  value. An ordinary launch — no AI features, no adopted connection — runs no shell at all.

  It is a session snapshot, like the process environment has always been: editing a shell profile takes effect
  at the next launch. Where the probe cannot run — Windows, which does not need it; `nu`, `csh` and `tcsh`,
  whose flags do not mean what this needs; or a profile slow enough to hit the five-second timeout — behaviour
  falls back to reading this process's own environment, and the two workarounds are `launchctl setenv NAME
  value` (which publishes the value to every process in the login session, worth knowing before using it) or
  launching the binary from a shell that already has the variable:
  `"/Applications/CST Reader.app/Contents/MacOS/CST Reader"`. Note that `open -a "CST Reader"` does **not**
  work — `open` hands the request to launchd, which supplies its own environment.
- **House terminology in generated output — do not post-filter.** String surgery on model prose produces worse
  artifacts than the term it removes, cannot handle inflected or compounded forms, and blurs the line the plan
  correctly draws: output is *labeled generated*, and the project's own scoping of the rule governs our prose
  and UI, not third-party text displayed as such. Instead: (1) state the convention **positively** in the
  system prompt ("use 'Pāli texts', 'the Tipiṭaka', 'VRI texts'") — positive phrasing outperforms don't-lists;
  (2) **B9 scores terminology compliance and gates the `recommended` tier on it** — a model that cannot follow
  the convention is arguably not recommended-tier for this app; (3) count, don't mutate. The ceiling, if ever
  wanted, is a non-blocking notice on the output — never a rewrite.

---

## 11. Deliberately deferred: the tool-calling tier

**The deferral costs more than capability, and it is worth being honest about what.** Under injection the app
chooses what to retrieve *before the model has seen the passage* — the word set is a heuristic over the text,
not a response to what is actually needed. Under tool calling the model reads first and then asks for the
lookups it wants. So everything injected is best-effort by construction, and two things follow that #582's
system prompt must state: **no injected entry is authoritative**, and **absence is not evidence** — a word with
no gloss was missed by the heuristic, not found to be undefined. A model not told this will reasonably read the
injected set as the relevant set. *(Decided 2026-08-10 with fsnow.)*

When B eventually needs open-ended corpus research, the loop is additive, not a rewrite: the same tool
interfaces become tool schemas, and the injection path stays as the fast path for scoped tasks. Two things to
know before starting:

- The loop is ours to own — dispatch, id matching, malformed-JSON arguments (OpenAI-compatible sends
  `arguments` as a **string**, Anthropic sends `input` **parsed**), and an iteration cap.
- Model capability varies sharply. Weaker models invent parameters and call the wrong tool. It ships as a
  **per-provider capability tier**, not a global feature — which is what the cold-agent matrix
  (`docs/testing/LOCAL_API_COLD_TESTS.md`) exists to measure.

---

## 12. Phasing

Ordered by dependency. **UI-free** items are `dotnet test`-verifiable and need no GUI work.

| # | Issue | Work | UI-free | Depends on |
|---|---|---|---|---|
| **B1** | #578 | Provider layer: `IChatProvider`, **both** adapters, SSE, cancellation, normalized errors, per-provider quirks (§5) | ✅ | — |
| **B2** | #579 | Credential storage: Keychain + DPAPI, lazy read, no-logging test | ⚠️ platform-gated | — |
| **B3** | #580 | **Context bundler**: `AiContextBundle` + gathering, budgeting, availability states, `BookContext` source decision | ✅ | — |
| **B3a** | #581 | **Selection pipeline**: display-script → Latin, normalization, window location, null/timeout | ✅ | — |
| **B4** | #582 | Presets + system prompt + template rendering (embedded defaults, user-editable) | ✅ | B3 |
| **B5** | #583 | Orchestrator: bundle → prompt → provider → stream, cancel-and-replace, usage accounting | ✅ | B1, B2, B4 |
| **B6** | #584 | Model registry + fidelity advisory data | ✅ | — |
| **B7** | #585 | Settings UI: provider, base URL, model, key entry, answer language, Pāli script, advisory | ✗ (Frank) | B2, B6 |
| **B8** | #586 | In-app panel: invoke, stream, stop, scope + citation chrome, generated-text treatment, **input plumbing** | ✗ (Frank) | B5, B3a |
| **B9** | #587 | Eval harness: fixed passages × presets × models, scored for grounding, citation accuracy, terminology, marker discipline | ✅ | B5 |

**B2 is not fully UI-free**: DPAPI cannot be tested under `dotnet test` on macOS — it needs a Windows target
(Merlin or Placid).

**B8 includes input plumbing that is easy to miss**: the bundler's inputs are
`(bookId, reference-or-null, selectionText-or-null)`, and producing them is UI work — `CurrentParagraph` is
scroll-derived and can be `"*"` (unknown), and multi-book files need the sub-book code
(`GetCurrentParagraphAnchorWithBookCode` exists).

**B1, B3, and B3a are independent** and can run in parallel. B3 is the highest-value one.

**Walking skeleton (first user-visible slice):** B1 (**both** adapters) + B2 + B3 (passage + citation +
`OutputLanguage`) + B4 (Explain preset only) + B5 + minimal B7 + minimal B8. Additive after that: glosses,
lemmas, B3a, the other three presets, the registry, the eval harness.

**The OpenAI-compatible adapter belongs in the skeleton, not a later slice** — the feature exists because a
user asked for DeepSeek, and testing the skeleton against a local runner (Ollama on Egret) exercises the
fully-local privacy story for free. Antonio's ask lands at the **whole skeleton**, not at B1+B7.

### A note on build order

Per the standing pattern — build the contract before the GUI; the contract and its cold-agent usage clarify
what the human UI should be. B1–B6 are all headless. Add a **dev-only** affordance that dumps the assembled
`AiContextBundle` for the current reader position without calling any model: the bundle is what determines B's
output quality, and it is far easier to critique as inspectable data than as a paragraph of English.

## 13. Testing

- **B1** — stubbed `HttpMessageHandler` replaying recorded SSE from both wire formats. No network, no key.
  Include malformed/truncated streams, a mid-stream error, and a DeepSeek-style `reasoning_content` stream.
- **B2** — no-logging assertion; DPAPI path runs on a Windows target.
- **B3 / B3a** — golden-file tests against the real corpus: fixed (book, reference, selection, preset) → bundle
  shape. Catches regressions in passage windowing, gloss selection, script conversion, and budget trimming.
  Include a no-lemma-asset case and a non-Latin selection.
- **B5** — end-to-end with a fake provider that echoes the prompt back, asserting the assembled request.
- **B9** — the only tests that spend money. A fixed eval set across the model matrix, scored on: stayed inside
  the supplied passage, invented no reference, honored terminology conventions, marked quoted Pāli correctly,
  and produced a defensible translation. The B-side analog of the surface-C cold-agent loop.

## 14. Open questions

- **Follow-up turns** — one-shot per invocation, or a short conversation over the same bundle? (One-shot covers
  the named use cases; conversation needs history management and re-budgeting.)
- ~~**`ICorpusTools`** — delete as dead, or implement it?~~ *Deleted in #580: no implementation, no consumer,
  and one of its four members had no implementation at all.*
- ~~**Which dictionaries feed glosses by default**~~ *Moot for v1: no glosses are injected (§4). Returns as a
  question for the tool-calling tier.*
- **Token estimation** — no local tokenizer, so `BudgetReport` uses a chars-per-token heuristic that is
  per-script inaccurate (Latin Pāli with diacritics tokenizes worse than English). Don't build budgeting that
  assumes a provider `count_tokens` endpoint.
- **Template staleness** — where user-edited templates live, and what happens when a shipped default changes
  underneath one. (This ships after users exist, so the pre-Beta-5 "no migration" stance does not apply.)
  Proposal: user file shadows default; staleness detected by a version stamp in the template header.
- **Linux credential storage** — unaddressed (macOS + Windows only). Acceptable while Linux is unshipped, but
  define the behavior so it is not an accidental gap.

## 15. Review log

**2026-08-08 (Fable, adversarial review)** — findings accepted and folded in:
- **Corrected a factual error**: `ICorpusTools` is referenced only in its own definition file; surface C
  consumes the focused interfaces individually. `INavigationTool` is unimplemented; lemma services are outside
  the tool layer and nullable (optional asset). §2 rewritten; §14 adds the delete-or-implement question.
- **Selection promoted from a footnote to B3a** — it arrives in the display script with a 700 ms timeout and
  silent null, and the two differentiating presets depend on it. Without this the skeleton served Latin-script
  users only — the opposite of the user who prompted the feature.
- **`OutputLanguage` added to the bundle**; language of the prose separated from script of quoted Pāli (§9).
  Also: plain-text gloss projection (`MeaningHtml` is HTML), `Provenance` stamps, and a third `BudgetReport`
  state for asset-unavailable.
- **OpenAI-compatible adapter moved into the walking skeleton**; B2 marked platform-gated; B8's input plumbing
  itemized.
- **Provider failure modes added** (§5): `HttpClient` timeout, mid-stream drop, DeepSeek `reasoning_content`,
  Anthropic `max_tokens` required + sampling params rejected, cancel-and-replace.
- **New §6 on scope** — injection's characteristic failure is truthful output over a silently narrowed scope,
  not hallucination. Scope display, model-declared scope, cross-corpus refusal, partial-passage badge.
- **Both open decisions resolved**: marker instruction ships in v1 (render verbatim, convert in v1.1) so B9
  baselines on the final prompt shape; no post-filtering for terminology — positive phrasing plus B9 scoring
  gating the recommended tier.
- Verdict: sound enough to build from. B1/B2 can start as specced; B3 waits on the §2 corrections.

**2026-08-10, later (fsnow, during #580)** — **dictionary glosses are out of v1** (§4), superseding the DPD
prerequisite recorded earlier the same day. Reason: context clutter, unlikely to help the model discern meaning.
A Fable review of the removed code separately found it did not work against the real DPD asset (homograph
suffixes absent from the form index) — a bug to fix when lookups return with tool calling, and explicitly *not*
the argument for removing them. Same review caught two defects in code that survives: the trimmed-passage flag
fired on nearly every request, and three reachable fetch failures bundled an empty passage with the error string
as the citation. Both fixed.

**2026-08-10 (fsnow, during #580)** — two decisions and a correction:
- **DPD is a prerequisite for surface B**, not an optional enhancement (§4). Enforcement belongs to #583/#585;
  the bundler reports absence honestly rather than assuming the precondition.
- **Glosses are candidates carrying provenance, never filtered to "the right one" by the app** (§3). The first
  implementation kept only exact headword matches, which both dropped the correct entry for most inflected
  words and treated the survivors as authoritative. Homographs are emitted in full for the model to choose
  between. fsnow: *"ultimately it is up to the model to decide on what gloss is correct in context… even a
  dictionary 'hit' is not always a gloss"* — and *"it could be a homophone"*.
- **Injection is best-effort by construction** (§11), because the app retrieves before the model has read
  anything. The bundle now says so in its own data, so the prompt template inherits the caveat rather than
  relying on someone remembering it.

**2026-08-09 (fsnow)** — **no special handling for Cyrillic.** The review had asked for an explicit degraded
path in the selection pipeline and an exclusion from the v1.1 script conversion, on the grounds that Cyrillic
is the one supported script that does not round-trip. That non-round-tripping is rare enough in practice not to
earn a code path; removed from §3.1, §9 and the §12 table, and deliberately kept out of #581. Work items filed
as #578–#587 under epic #186; issue numbers added to §12.

**2026-08-11 (#582, the prompt layer)** — three decisions worth keeping, all forced by evidence rather than
argument:

- **The Pāli-quote marker is `[[…]]`** — see §9. Decided by a corpus survey plus one constraint the plan had
  not stated: the marker must not collide with ordinary answer prose in any language a user may select, which
  is what eliminates the guillemets.
- **The template engine is substitution-only** — no conditionals, no loops. These templates are user-editable
  (B7), and control flow would make them a small programming language whose breakage we would then have to
  diagnose. The cost is that a placeholder cannot be omitted when it has nothing to say, so every value renders
  as a self-describing sentence instead: "the reader has not selected anything", "the word-analysis data is not
  installed". That turns out to be the better prompt, and it is the mechanism that makes the plan's
  "degrade visibly, never silently" requirement fall out of the design rather than rest on discipline.
- **The provisions inventory is derived from the template's own placeholders.** Found by reading a dumped
  prompt, not by a test: the inventory said `apparatus — given` on presets whose template never rendered the
  apparatus, so the model was told it had been given something it could not see. Deriving the inventory from
  what the template actually uses makes the two agree by construction, including after a user edit.

**A live run against `gpt-oss:120b-cloud` was worth more than the third code review.** The fences held —
cross-corpus refusal, scope naming, no invented references — but the model **mistranslated the verse it was
looking at** (Case 2 in [PALI_FIDELITY_CASES.md](../../testing/PALI_FIDELITY_CASES.md)) and left inline Pāli
unmarked while marking block quotes. The marker failure was fixed by naming the competing convention —
"use the markers instead of italics, an italicised word is a word left behind in Latin" — where merely stating
the requirement had not worked. The mistranslation was not fixable by prompting and is exactly the evidence
#584's fidelity advisory exists to carry.

**2026-08-11 (#583, the orchestrator)** — the pieces are now one feature. Decisions worth keeping:

- **Nothing expected throws.** Not-configured, an unreadable passage, a dead network, a 401 — all arrive as a
  terminal `Error` event. §10 requires the panel to render each as a sentence, and collapsing #578's two
  failure shapes here spares every future caller from re-deriving that. #578's contract is unchanged: the
  *provider* still throws before the response and yields an `Error` delta after it.
- **A superseded turn ends quietly; the caller's own cancellation throws.** Being replaced is not a failure —
  an error banner under an answer the reader abandoned would be both wrong and alarming — but a consumer that
  cancels its own enumeration must not be told the turn succeeded. Two cancellation sources, two behaviours,
  distinguished by which token fired.
- **An empty answer is a named failure** (`AiErrorKind.EmptyAnswer`). A model can end a turn having produced
  only reasoning (#601), and #578 is *right* to segregate reasoning from answer — which is exactly what makes
  this failure invisible, leaving the caller a well-formed, successful, blank turn.
- **Truncation is measured where the provider reports it, and worded here.** `AiErrorKind.Truncated` says *that*
  the output limit was hit; only the orchestrator knows what the user got for it, so it composes one of three
  messages — cut off mid-answer, all budget spent reasoning, or nothing produced at all. Three situations, three
  fixes. `EmptyAnswer` remains the fallback for endpoints that report no finish reason at all.
- **`ChatSettings` and `IChatProviderResolver` are the seam** for B2/B7. The key is deliberately absent from
  settings.json — it belongs in the OS credential store (#579) — and a key is required for Anthropic but *not*
  for OpenAI-compatible, because the motivating deployment is a local runner on loopback with no credential.

**A live end-to-end run found a scope bug the unit tests could not** — filed as **#602**. A Translate turn on
Dhp 21 returned a good translation of roughly *thirty* verses, spanning into two later chapters, beside a
citation reading "paragraph 21". The window was budgeted in **characters** and was structurally blind: 2,400
characters of prose is a passage, but a Dhammapada verse is ~80 characters. `WindowMayExtendPastReference` fired
and #582 surfaced its notice, which is true and reads like a rounding caveat rather than a warning that the
answer covers twenty-nine paragraphs nobody asked about. §6 names truthful output over a *narrower* scope than
assumed as the characteristic failure; this is that failure mirrored, and the app-rendered citation makes it
worse rather than better. Fixing it also unblocks §6's fence 4, since the missing capability is the same one.

**2026-08-21 (#672, the budget that was never derived from anything)** — the five per-task character budgets
are gone. Expansion is **two sentences either side of the selection**, bounded as before by the enclosing
`<div>`; a character figure survives only where there is no selection to count from. Characters were the wrong
unit for exactly the reason #602 records above — verse and prose priced identically — and the numbers
themselves had no derivation anywhere.

Two things were measured rather than argued. Sending the whole enclosing section was the obvious alternative
and the corpus rules it out: the 968 innermost `<div>` sections in the 78 books carrying div markup have a
median rendered length of 12,224 characters and a p75 of 30,967, so the section is the right *bound* and a poor
*target*. And the walk's scan cap comes from the corpus's own sentences — of 1,038,209 danda-delimited
sentences the median is 56 characters and 30 (0.003%) exceed 2,000.

A selection is also capped for the first time, so a select-all cannot send a whole book, and the cut is
reported rather than silent — which is what finally makes fence 4's badge fire, having been unreachable since
it was specified.

**2026-08-11 (#602, the window's real extent)** — the passage reader now reports the paragraph in effect where
the window **ended**, not only where it began. Three consequences: `normalizedReference` names a range
("paragraphs 21-45 (kn2)") whenever the window spans more than one paragraph; `BudgetReport.ParagraphsCovered`
replaces the always-true `WindowMayExtendPastReference` with a measured count; and §6's fence 4 becomes
implementable.

**Reporting the extent is not the same as fixing it, and the rest is a product decision.** The Dhp 21 window
still covers 25 paragraphs — the citation and the notice are now honest about it, which stops the app
*misrepresenting* the answer, but a reader who asks to translate one verse still gets twenty-five. The remaining
options in #602 (clamp the window to the cited reference; budget in paragraphs rather than characters) change
what "translate this" *means*, and trade surrounding context against focus differently per preset. That is
fsnow's call, not something to settle in an implementation commit.

**2026-08-11 (#581, the selection pipeline)** — the selection is the one bundler input scraped from the DOM, and
it now gets handling to match.

- **Conversion moved into a pure, testable pipeline.** The display script is known only to the reader, so
  `ReaderStateService` still supplies it, but the rules live in one place. Splitting them would let the two
  sides of the window comparison drift, which is precisely how a locator starts reporting false misses.
- **Three selection states, not two.** "Nothing selected" and "we could not read the selection" were the same
  null. They are different: the first means the whole passage is legitimately in view; the second means the
  words the user highlighted were dropped. Only the second is worth telling them about — and it is exactly the
  state that otherwise reaches the user as *"the AI ignored my selection"*. The channel already distinguished
  them (`GetWebViewSelectionAsync` returns `""` for nothing and `null` for a failed or timed-out round trip);
  the pipeline stops throwing that away.
- **Composition (NFC) is cheap insurance behind a measured risk.** A corpus survey found the Devanagari source
  already NFC, and `ScriptConverter` emits composed Latin, so in the ordinary path both sides of the comparison
  agree. But a decomposed selection is *ordinally* unequal (`a`+U+0304 ≠ U+0101), and the symptom would be a
  bundle reporting "selection not found in the passage window" — a false diagnostic from the component whose
  whole job is faithful diagnostics.

**2026-08-11 (#584, the model registry)** — the fidelity advisory's data, shipped as an embedded JSON resource
with a lookup that normalizes the several spellings one model arrives in (vendor prefix, deployment suffix,
dated snapshot) while never folding away **size**, since `gpt-oss:20b` and `gpt-oss:120b` are different models
rated differently.

- **`unrated` is not `discouraged`, and that distinction is the design.** Frontier models appear constantly; a
  registry treating anything it had not heard of as suspect would have flagged half of today's good models a
  year ago and would decay into noise the moment it stopped being updated — at which point users dismiss it,
  including on the entries that matter. Unknown models get a mild "not evaluated"; the strong warning is
  reserved for models with **evidence against them on this corpus**.
- **Every non-recommended entry cites its evidence**, and a test enforces it. A registry that says a model is
  not recommended for translating canonical text has to answer *why*, or it is an opinion with a version number.
- **The advisory is scoped to translation.** Explaining or parsing a passage the model can see is a far smaller
  fidelity surface than producing English a reader will take as the meaning of the Pāli — and an advisory
  attached to everything is one nobody reads.
- **Nothing is ever blocked** (AI_INTEGRATION.md §11.1). The interface returns a rating or advice; there is no
  member that can refuse a model, and a test asserts there is no boolean verdict on it.

**2026-08-11 (#587, the evaluation harness)** — mechanical scorers plus a paid, opt-in runner.

- **The scorers are unit-tested without a model or a network.** The live runs cost money, so what they spend it
  on must be measurement whose behaviour is already pinned down; a scorer debugged against live output is
  debugged at several dollars a mistake.
- **The runner is silent unless `CST_AI_EVAL=1`.** A live call on every `dotnet test` would bill for a full
  matrix whenever anyone touched an unrelated file.
- **It scores the RAW answer**, so it calls the provider directly — the orchestrator strips quote markers by
  design, and the markers are the measurement. Everything upstream (bundler, templates, prompt builder) is the
  real thing, so what is scored is what the app would send.
- **It reports; it never decides.** Tier changes are recorded by a person. A harness trusted to promote and
  demote models would quietly become the definition of fidelity.

The first run reproduced Case 4 mechanically and found a **new** failure: `gpt-oss:120b` answers the
cross-corpus question with invented sutta references instead of declining — the hazard §6 is written against,
observed rather than theorised. It also turned marker discipline into a number that says two models cannot
share a script-conversion setting (`gemma4` 1 unmarked term across a 164-quote answer; `gpt-oss:120b` 16–37).

**The terminology gate ships disarmed, deliberately.** The convention is stated positively in the system
prompt, but the term the scorer would COUNT is one CLAUDE.md forbids appearing anywhere in this repo.
Committing it to make the check fire would break that rule in order to test a convention — not a trade an
implementation gets to make. The list is data (`cases.json` → `terminology.discouraged`), so populating it
locally arms the gate with no code change.

**2026-08-12 (#579, key storage — macOS half)** — Keychain via Security.framework, plus the common seam. The
DPAPI half is deliberately not attempted from here: it cannot be exercised under `dotnet test` on a Mac, and
shipping an untested credential store would be worse than reporting the truth.

- **The modern `SecItem*` API, not the far simpler `SecKeychain*` one.** The legacy calls would be a third of
  the code, but they have been deprecated since 10.10 — and a credential store is precisely the component that
  must not stop working on an OS release.
- **It targets the file-based (login) keychain, checked against Apple's docs rather than assumed.** `SecItem`
  routes to the data-protection keychain only when the query carries `kSecUseDataProtectionKeychain` or
  `kSecAttrSynchronizable` ([TN3137](https://developer.apple.com/documentation/technotes/tn3137-on-mac-keychains));
  with neither, it talks to the file-based one. That is the right target while development is `dotnet run`,
  because the data-protection keychain is only available to code carrying an entitlement and its access groups
  come from code signing — an unsigned development build and the signed `.app` would not share a key. Revisiting
  is **#609**.
- **No `kSecAttrAccessible`, and that is a correction.** The first cut passed
  `kSecAttrAccessibleAfterFirstUnlock` with a comment claiming it kept the key off a powered-down machine. It
  does not: accessibility classes are the data-protection keychain's access model, the file-based keychain uses
  ACLs, and the attribute was accepted and silently ignored. Inert code that *looks* like a security control is
  worse than none, because it stops anyone asking the question again.
- **The `kSec*` keys are read with `dlsym`.** Their underlying string values are stable in practice and
  undocumented in principle; loading the real symbols costs a few lines and removes a dependency on something
  Apple never promised. Any missing symbol reports the store unavailable rather than building a half-populated
  query that fails obscurely at every call site.
- **Nothing is cached.** A cache goes stale the moment the user changes the key in Settings, and the lookup
  costs microseconds — the wrong side of that trade is the one where the app keeps using a credential the user
  has already replaced.
- **"No key entered" and "nowhere to store one" are different problems with different fixes**, so the resolver
  says which. Sending a Windows user to Settings to add a key would send them to a screen that cannot help.
- **Platform behaviour is defined, not left to accident.** Windows and Linux report unavailable with a reason,
  and both say that *an endpoint needing no API key still works* — the local-runner configuration is unaffected,
  which keeps the privacy-first path open on every platform.

The acceptance test asserts the key never appears in log output at any level — across store, read and delete,
and against the **structured** log values as well as the formatted message, since a leak through a log property
is just as public and the easier one to introduce by accident. Every test uses a unique service name, so
running the suite can never read, overwrite or delete a developer's own stored key.
