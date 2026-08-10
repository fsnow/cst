# Surface B — the in-app model, v1 by context injection (Planned)

**Status:** Planned. Not started.
**Parent:** [AI_INTEGRATION.md](AI_INTEGRATION.md) — the design of record for the A–E surface map. §11.1 there
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
- **Glosses are projected to plain text.** `DictionaryEntry.MeaningHtml` is an HTML fragment
  (`DictionaryToolContracts.cs:62`). Injecting raw markup wastes tokens and invites markup echo in the answer.
  The projection is bundler work, not a footnote.
- **Glosses carry their attribution.** `DictionarySourceInfo` records title/compiler/edition/license. Inject it
  so a repeated gloss can be cited honestly — and note the bundled English dictionary is Childers 1875 (#378).
- **Lemma data is our differentiator.** No general-purpose chat has stem + inflection for Pāli. But it is an
  *optional asset*, so see §4.
- **`BudgetReport` distinguishes three states, not two**: included, **trimmed for budget**, and **unavailable
  (asset not installed)**. Conflating the last two makes a missing DPD look like a budget problem.
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
| **Explain** | passage, book context, light glosses | — | The default. Free-form follow-up allowed. |
| **Translate** | passage, full glosses, apparatus notes | — | Fidelity-sensitive (§7). Apparatus matters: variant readings change translations. |
| **Grammar** | selection (or sentence), lemmas, glosses | B3a + lemma asset | Degrades to glosses-only without the asset — say so in the UI. |
| **Word-by-word** | selection, lemmas + glosses per token | B3a + lemma asset | Most grounded preset; smallest hallucination surface. |

**Both lemma presets must have a defined no-asset behavior** — degrade visibly, never silently.

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
- **`ICorpusTools`** — delete as dead, or implement it (with `INavigationTool`) as part of B3? (§2)
- **Which dictionaries feed glosses by default** — DPD is the strong one; Childers 1875 is what "English"
  currently means (#378). Probably DPD-first with Childers fallback, but that is a data-quality call.
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

**2026-08-09 (fsnow)** — **no special handling for Cyrillic.** The review had asked for an explicit degraded
path in the selection pipeline and an exclusion from the v1.1 script conversion, on the grounds that Cyrillic
is the one supported script that does not round-trip. That non-round-tripping is rare enough in practice not to
earn a code path; removed from §3.1, §9 and the §12 table, and deliberately kept out of #581. Work items filed
as #578–#587 under epic #186; issue numbers added to §12.
