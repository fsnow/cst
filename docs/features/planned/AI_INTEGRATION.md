# AI Integration — Claude & Agent Access to the Corpus (Planned — Design of Record)

**Status:** Planned / design of record. Not started.
**Last updated:** July 2026 (reviewed + revised 2026-07-04 — see §16)
**Platform scope:** Cross-platform for the portable surfaces (HTTP + llms.txt, MCP); macOS-only for the Siri/App Intents surface.

> This is the durable design of record for the AI-integration feature area. GitHub issues track the
> work and will close; this document persists. Related issues: umbrella epic + children (see §12),
> plus #41 (App Intents / Siri = surface **A**) and #27 (vector/semantic search = surface **D**).

## 1. Motivation

CST Reader sits on a large, well-structured Pāli corpus with mature search (Lucene / IPE) and a rich
reference model. That makes it an ideal **research-assistant substrate**: a user should be able to ask an
agent a question, have it *search the corpus and the dictionaries*, and see the **results shown in context
in the reader itself** — the book open to the spot, hits highlighted — not just a snippet pasted into a chat.

Users span a **range of technical ability**, and we support both ends:
- **Coding-agent users** (Claude Code, or any agent that can run a shell / write code) → talk to a local
  HTTP API directly, oriented by an `llms.txt`.
- **BYO-MCP-chat users** (claude.ai, Claude Desktop, other MCP clients) → talk to a thin **MCP adapter**
  that proxies to the same API.

Neither user needs to know which spine they are standing on.

## 2. The surface map (A–E)

| # | Surface | What it is | Transport | Status |
|---|---|---|---|---|
| **A** | **Outbound to the system assistant** | Expose CST actions/content to Siri / Apple Intelligence / Spotlight. Claude reaches CST here only indirectly (as a Siri Extension provider). | App Intents (Swift, macOS) | Documented — #41 |
| **B** | **Claude inside CST Reader** | Select a passage → explain / translate / summarize; study companion over the open text or corpus. | In-app model calls | This doc |
| **C** | **CST as a tool server** | Expose the corpus + dictionaries as tools (search, fetch-passage, resolve-reference, dictionary lookup) to external agents. | Local HTTP API (+ MCP adapter) | This doc |
| **D** | **Retrieval substrate** | Semantic search / embeddings / RAG. Not a user feature on its own — the engine B and C stand on for "answer over the corpus." | internal | Research — #27 |
| **E** | **Agent-driven "show me in context"** | Agent searches, then **drives the running reader** to present the result — open book, go to reference, highlight hits, scroll into view. | Local HTTP API (present-tool) / App Intents | This doc |

## 3. The organizing principle

> **The app is the abstraction boundary. Every AI surface is a thin adapter over an existing app service —
> so an agent gets clean semantic tools and never touches a storage format.**

An agent must never parse the **UTF-16 corpus XML**, the **Lucene segments**, or the **binary dictionary
format** — it calls *search*, *fetch-passage*, *lookup*. The service layer owns every format. Payoffs:
- **Stable contract** — when a storage format changes, or new dictionaries are added (e.g. the future
  open-source dictionaries), the tool signatures don't move; the agent is insulated.
- **One reader per format** — nobody re-implements the VRI dictionary format or the TEI XML outside the app.

## 4. Two reusable cores

Everything in §2 is an adapter over one or both of:

1. **The navigation / present-in-context command** — a single internal action:
   *"open the reader to `(book, reference/paragraph, search-terms)`, render it in context, highlight the hits."*
   Drives **E**, and backs App Intents' `OpenBook` / `GoTo` / `Search` (**A**). Front-ends (HTTP present-tool,
   App Intents, `cstreader://` URL, local IPC) are thin adapters over it.
   **It must return a structured outcome, not fire-and-forget:** what was actually resolved and opened
   (book, normalized reference, paragraph), or a precise, machine-readable failure (`unknown-book`,
   `ambiguous-reference` + candidate list, `reference-out-of-range`). The agent needs this to confirm the
   user is looking at the right passage. **Decided: ambiguity resolves agent-side** — the API returns the
   candidates and the agent asks its user; the app never pops its own pick-list for an API-initiated
   navigation, and never silently best-guess jumps. **Its highlight/anchor payload is expressed in source
   coordinates** — see the coordinate model in §6.1.
2. **The app's read services exposed as tools** — search, fetch-passage, resolve-reference, **dictionary
   lookup** — each already encapsulating its format (Lucene / TEI XML / binary dict). Backs **C** (and **B**).
   Result shapes are **token-frugal by design**: paragraph-granularity by default, explicit size limits +
   pagination on search results and passages, structured JSON with only the fields asked for.
   **Planned addition — an anchor catalog:** enumerable lookups of the *valid navigation targets*
   (`list_books()`, `list_anchors(book)` → paragraph ranges, page ranges per edition, chapter/section
   anchors), served from a **precomputed catalog** rather than parsing the TEI XML per query. This is what
   `resolve-reference` and disambiguation stand on, lets an agent know what's addressable before it asks,
   and keeps anchor queries fast.

Build the cores once; the surfaces become adapters.

## 5. Transport architecture

Do not think of HTTP *vs* MCP *vs* URL scheme — **layer them on the shared cores**:

```
        Claude Code / coding agent ──────────────┐
                                                  ▼
   BYO MCP chat ──▶ thin stdio MCP adapter ──▶  Local HTTP API (127.0.0.1/v1)  ──▶  App services + nav core
        (claude.ai, Desktop)                      ▲
   Siri / Apple Intelligence ──▶ App Intents ─────┘   (cold-start: cstreader:// URL launches, then HTTP)
        (macOS, surface A)
```

- **Local HTTP API is the spine** — the cross-platform, offline, code-mode-friendly "data + remote-control"
  surface. Serves both *data* tools and a `POST /navigate` *presentation* tool (**E**).
- **MCP is a thin, optional adapter** — a small stdio server that proxies MCP tool calls to the HTTP API,
  for chat clients that can't call HTTP themselves. **Not the foundation** (see §7).
- **URL scheme covers cold-start** — HTTP only listens while the app runs; `cstreader://…` launches it, HTTP
  takes over once up.
- **App Intents (A) is the macOS-native overlay** on the same nav core (Swift-gated; see #41).

## 6. Local HTTP API design

- **Loopback only** — bind `127.0.0.1` (and `::1`), **never** `0.0.0.0`.
- **Ephemeral port + per-session token**, both written to `…/CSTReader/local-api.json` with **owner-only
  permissions (0600)**; the MCP adapter and other clients read it to discover where to connect and how to
  authenticate. Include the app **PID** in the file so clients can detect a stale file after a crash.
- **Token in the `Authorization: Bearer` header only** — never in the URL/query string (query strings leak
  into logs and referers). This alone defeats DNS-rebinding and browser CSRF: a web page cannot attach a
  custom `Authorization` header cross-origin without a CORS preflight we will never approve.
- **No CORS at all** — never emit `Access-Control-Allow-Origin` (stronger than "no wildcard"); browsers are
  not a supported client. Additionally validate `Host` is the loopback literal, and reject requests carrying
  an `Origin` header outright.
- **Honest threat model** — the token protects against *browser-origin* attacks (rebinding, CSRF). It does
  **not** protect against a malicious same-user local process — nothing at this layer can (such a process can
  read `local-api.json` exactly like our own adapter). That is the OS's trust boundary, and the corpus is not
  sensitive; the side-effectful part is UI navigation, which is visible to the user (see consent below).
- **Versioned** — `/v1/…` from day one; the moment an adapter depends on it, it's a contract.
- **Status/readiness endpoint** — `GET /v1/status`: app version, API version, index state (ready / indexing
  + progress), corpus revision. Agents (and the MCP adapter) poll this before heavy use; a `navigate` or
  `search` during startup/re-indexing degrades to a clear `not-ready` response, never a hang.
- **Single instance** — define behavior when a second app instance launches: either enforce single-instance
  (preferred; deep links and the API route to the existing instance) or per-instance files. Never let two
  instances silently clobber one `local-api.json`.
- **User consent & control** — the API is governed by Settings, mirroring the auto-update philosophy
  (#178): a master **enable/disable toggle** for the local API (off = socket never opens), and a separate
  toggle for **remote-control** (`navigate`) vs read-only data access. When an agent drives navigation, give
  a subtle visible indication in the UI so the reader is never surprised by "the app moved by itself."
  **Decided: default off** — the user opts in from Settings; nothing listens until they do. (The "Copy MCP
  configuration" affordance in §7 lives next to the toggle, so enabling and connecting are one visit.)
- **Concurrency** — handlers are another concurrent caller into `IndexingService` / `SearchService`; they must
  route through the thread-safe service layer (the reader-refcounting hardened in SRCH-6/11), never around it.
- **UI thread** — navigation/presentation must marshal to the Avalonia dispatcher.
- **Lifecycle** — starts with the app (if enabled), stops on shutdown; cold-start is the URL scheme's job (§5).
  URL-scheme registration is itself a **packaging change** (macOS: `CFBundleURLTypes` in Info.plist +
  re-sign/notarize; Windows: registry; Linux: `.desktop` file) — schedule it with phase 1 (§13).

### 6.1 Highlighting & anchors — the coordinate model

The concordance/passage **reads** (C) and the present-in-context **command** (E) share one primitive: an
ordered **highlight set with a single navigable anchor**. It has two producers — search *computes* it (a hit,
or the multi-span set of a proximity match), and the agent *supplies* it (to show the user a span it chose) —
over **one coordinate space**. Get that space right and E is nearly free; get it wrong and nothing maps.

**Three coordinate spaces; only one is contractual for highlighting.**
- **Source char offsets** — into the decoded, BOM-stripped UTF-16 corpus XML (markup and all). Script- and
  render-independent. This is already what `cursor` and search `StartOffset`/`EndOffset` are, and what the
  reader's WebView highlighter consumes. **This is the highlight coordinate space.**
- **Rendered-text offsets** — into the script-converted, markup-stripped `text` a read returns. Depends on
  `script` and `includeVariantReadings`; a Latin offset ≠ a Devanagari offset. **Display-only — never a
  highlight coordinate.** The snippet's `HitStart`/`HitLength` live here: they mark the *returned string*, nothing more.
- **WebView/DOM offsets** — internal to the reader; the app owns the source→DOM mapping.

**Invariant: every offset the agent hands to a highlight/anchor command is a *source* offset that originated
from us.** No agent-invented coordinates. This is what makes it robust: the forward render
(`Clean` strip markup → `Convert` script → `Collapse` whitespace) is non-linear and **one-way** — we discard
the mapping, so a rendered offset *cannot* be inverted back to XML. The agent therefore cannot point at the
flat prose blob; it can only reference offsets we labelled for it. (Corollary: the read surface must stop
handing out anonymous prose and start handing out *offset-anchored* data when the agent intends to act.)

**Tiered escalation — pay for precision only when acting:**
1. **Anchor + navigate — free, works today.** `cursor` (the hit's source offset) → drop an anchor, scroll
   into view. No new data; the agent already holds it.
2. **Coarse highlight — a few more fields.** "Highlight the hit word" / "highlight the whole snippet" is a
   single source *span*. Today's returns mix spaces (`cursor` = source start, but `HitLength` = *rendered*
   length), so this tier means **also surfacing the hit's source end/length and the snippet window's source
   bounds** (`winStart`/`winEnd` — the extractor already computes them).
3. **Granular highlight — opt-in token offsets.** `includeTokenOffsets: true` → `tokens: [{ text, start,
   length }]`, where `text` is in the requested `script` but `start`/`length` are **source** offsets. The
   agent composes arbitrary highlight/anchor sets word-by-word. These offsets already exist — the Lucene
   index stores per-term `StartOffset`/`EndOffset`; we are *exposing* them, not inventing a system.

**The flag: `includeTokenOffsets` (default false), on both `/v1/occurrences` and `/v1/passage`** — same
option name, same token shape. Default-off keeps the read/scan path token-frugal (§4.2); the agent flips it
only on the one call where it has decided to *display*, not merely *read*. Read cheap; highlight precise.

**Multi-word / proximity is the forcing case.** A proximity match is inherently multi-span — the blue anchor
(first unit) plus the green context units scattered across the window — so a single `HitStart`/`HitLength`
cannot represent it; the KWIC would mark only one of the co-occurring words. The snippet model therefore
carries an ordered `highlights: [{ start, length, isAnchor }]`, of which the single-term hit is the
degenerate one-range case (one implementation, not two). This also removes the dead end where a multi-word
result's `~`-joined composite `term` is round-tripped through the *single*-term occurrences lookup: instead,
occurrences accepts the multi-word/proximity query (spaces = units, quotes = phrase, `proximityDistance` =
window) and returns one occurrence per window with the full highlight set and the anchor `cursor`.

**Surface-E command** (E) then takes a highlight/anchor request in **source coordinates** —
`{ book, ranges: [{ start, length }], anchor: <index into ranges> }` — fed only by offsets from tiers 1–3.
A **content-addressed** fallback ("highlight term T in paragraph P, anchor the first") is worth keeping for
when the agent never fetched offsets: the app resolves it exactly like search, fully script-independent, no
offset math — less flexible (only findable spans) but bulletproof. This composes with §4.1's structured
outcome/error contract.

## 7. Why not MCP-first

The token critique of MCP is real: a classic MCP server **front-loads every tool's schema** into context at
connect time, paid every turn. HTTP + `llms.txt` is the **progressive-disclosure / "code mode"** pattern that
avoids it — the agent reads a tiny index, fetches deeper docs only for the tools it needs, calls endpoints in
code, and takes back only the fields it asks for.

Precise conclusion — it depends on *which* Claude:
- **Code-capable agents** (shell / writes code) → **no MCP needed**; read `llms.txt`, call the API directly.
- **Non-code chat clients** (claude.ai, Desktop) → **can't** call HTTP; MCP earns its keep **there, and only there.**

So: **build HTTP + llms.txt as the foundation; keep MCP as a thin adapter** added for the chat surface. MCP
isn't obsolete — it's the interop standard and is itself moving toward progressive disclosure — but it's a
*packaging convenience, not the foundation.*

**Adapter packaging & lifecycle (decide early, it shapes UX):**
- The adapter ships **inside the app bundle** (a small executable; on macOS under `Contents/`, signed and
  notarized with everything else) so its version always matches the app's API. Users register it in their MCP
  client by absolute path; Settings should offer **"Copy MCP configuration"** producing the JSON snippet for
  Claude Desktop / claude.ai — that one affordance is most of the non-technical-user experience.
- On start, the adapter reads `local-api.json`, validates the PID, and checks `GET /v1/status`. If the app
  isn't running it returns a clear "CST Reader is not running" tool error — and may attempt cold-start via
  the URL scheme (§5) where the platform allows.
- Version skew is still possible (stale copied config, app upgraded mid-chat) — the adapter should compare
  its expected API version against `/v1/status` and say so plainly rather than half-work.

## 8. Discovery & orientation — `llms.txt`

Served over the same local port, an `llms.txt` is the agent's **front door**, with a specific advantage here:
served by the running app, it is **version-locked to the binary** — fetch it and you read the truth for *this*
build, whatever tools it exposes. It can't drift from the running surface.

- **Layered, curated (not a dump):** `GET /llms.txt` = overview + auth handshake + curated links;
  `GET /docs/{search,references,dictionary,navigation}.md` = deep detail pulled on demand; optional
  `/llms-full.txt` for one-shot ingestion. Mind the token budget — the index is read into context.
- **Especially necessary for CST:** the tools are unusable without domain orientation — the 14 scripts + **IPE
  encoding**, the **reference grammar** (Myanmar/PTS/VRI/Thai page + paragraph), the **book taxonomy**
  (mūla/aṭṭhakathā/ṭīkā, nikāya structure). This is its home.
- **House terminology** belongs here too, so agent-generated prose inherits the conventions (e.g. the standing
  terminology rule) instead of guessing.
- **Never serve secrets** — describe *how* to authenticate (read the token from `local-api.json`); the token
  itself is never in the docs, which are readable by anything that reaches the port.
- **Discovery is unauthenticated; everything else is not.** `GET /llms.txt` and `/docs/*.md` are served
  without the token (they contain no secrets, and it simplifies the bootstrap: read docs → learn the auth
  handshake → authenticate). All data/navigation endpoints require the bearer token.
- **Self-identifying** — `llms.txt` states the app version and API version at the top, so an agent (or a bug
  report) can always tell exactly which surface it was reading.

## 9. Dictionaries

The dictionary feature's **`DictionaryService` is a prerequisite** — it owns the binary format; the tools wrap
it. An agent never parses the dictionary files.
- Data tools: `dictionary_lookup(word, lang=en|hi)`, `dictionary_search(prefix)` → structured entries.
- Presentation tool (**E**): `show_dictionary_entry(word)` → open the dictionary panel to that entry in context.
- Format-agnostic contract survives added dictionaries (the future open-source set).

## 10. Apple security (macOS)

The app ships **hardened runtime + notarized, Developer ID, and is *not* sandboxed** (entitlements:
`cs.allow-jit`, `cs.allow-unsigned-executable-memory`, `cs.disable-library-validation`, `network.client` — no
`com.apple.security.app-sandbox`). Consequences for a loopback server:
- **No new entitlement needed.** `network.client`/`network.server` are **App Sandbox** keys and are inert when
  unsandboxed; a non-sandboxed app opens listening sockets freely. Hardened runtime does not gate server sockets.
- **Notarization/Gatekeeper** never inspect bound ports.
- **Application-firewall prompt** governs real interfaces — **loopback is not firewalled**, so `127.0.0.1`-only
  avoids it.
- **macOS Local Network Privacy** (Sequoia+) covers **LAN** access — **loopback is exempt**, so it's avoided too.
- **Boundary condition:** this flips only under **App Sandbox** (e.g. Mac App Store) — then `network.server` is
  required *and* CEF-in-sandbox is a separate, larger problem. Not the current Developer-ID path.
- **Testing:** entitlement/signing/firewall behavior is invisible to `dotnet run` — the server must be validated
  on a **signed, notarized build**.

## 11. Cross-cutting constraints

- **Offline / disconnected** — the local HTTP + code-agent path is fully offline (loopback). But **B** (in-app
  Claude) and any cloud model need connectivity, cost, and a key; the offline story is on-device (Apple
  Foundation Models) or none. Every AI feature needs a graceful *not-configured / no-network* state. (Ties to the
  "mostly-disconnected users" constraint in the auto-update work.)
- **Model choice + BYO key** — see §11.1; who pays for tokens is the user, via their own key.
- **Fidelity of canonical text** — this is a sacred-text corpus; hallucinated "translations" or invented
  citations are a real hazard. Grounding (retrieve real passages, cite the reference) matters more here than in a
  typical app, and the standing terminology hard rule applies to any generated output.
- **Script of agent-facing text** — internal processing stays **IPE**; text returned *to an agent* defaults
  to **romanized (Latin-script) Pāli**, with a `script` parameter to override per request. Frontier models
  read Latin-script Pāli natively and reliably; romanized output maximizes model comprehension per token.
  A future user-facing **preferred agent script** setting may layer on top (the script-conversion infra makes
  any of the 14 scripts cheap to serve); the per-request override ships first. **Decided.**

### 11.1 Model access policy (surface B)

Decided product values for any in-app model integration:

- **Support common LLM API standards** for maximum reach and flexibility: **OpenAI-compatible Chat
  Completions** (covers most providers, aggregators, and local runners such as Ollama/LM Studio — the
  offline/private option) **plus the Anthropic Messages API** (Claude direct). **BYO key and BYO endpoint.**
- **Model quality is a fidelity feature**, not merely a preference. Curate a **recommended-for-translation
  frontier tier**; default **Claude-first**. Frontier models are genuinely strong at Pāli understanding and
  translation — do not design as if the model were the weak link; the weak link is *ungrounded use of weak
  models*.
- **Discourage free/weak models for translating canonical text** — never a default, never in the recommended
  list, and a **fidelity advisory** when one is selected for translation. Curation and advisory, not a hard
  block (flexibility is preserved; the values are stated).
- **Grounded and cited is B's design goal** — B consumes the C/D tool layer (retrieve the real passage, look
  up dictionary entries, cite the reference) rather than free-running generation. Hallucination risk is
  inverse to *model quality × grounding*; B aims at the high end of both.
- **User-editable prompt templates with presets** (translate / explain / grammar / word-by-word) — planned
  from the start.
- **Visually distinguish generated text from canonical text** wherever B renders output near the corpus
  (styling/labeling), so a reader can never mistake model output for source text.

## 12. Documentation strategy

Three markdown layers, one format, **distinct purposes** — single-source only the overlap:

| Layer | Purpose | Audience | Lifecycle |
|---|---|---|---|
| **GitHub issues** | tracking, planning, decisions, phasing | maintainers | closed when done |
| **`docs/` design spec (this doc)** | design of record — architecture, rationale, security | developers (human + Claude Code) | planned → in-progress → implemented |
| **Served `llms.txt` + `/docs/*.md`** | runtime interaction contract (*how to call it*) | the agent, at runtime | version-locked to the binary |

- **API reference** (endpoints, params, tool signatures) → **generate** from one annotated route/tool definition
  that also drives the HTTP routes and MCP tool defs; never hand-maintain it twice.
- **Domain orientation** (scripts, IPE, references, taxonomy) → author **once** in `docs/`, embed as a build
  resource, serve verbatim as `/docs/*.md` (same markdown, dev + runtime consumers; version-locked at build).
- **Design rationale** (the A–E map, MCP-as-adapter, security) → issues + this doc **only**; the agent needs
  *how*, not *why*.

## 13. Phasing / recommendation

1. **Navigation/present-in-context core** — the single internal command (§4.1), with its structured
   outcome/error contract. Highest leverage: backs A, C, E, and reuses the deep-link routing phase-1 of #41.
   Pure-.NET, cross-platform. Includes the **URL-scheme packaging change** (§6 lifecycle note).
2. **Local HTTP API spine** (§6) — loopback + bearer token + `/v1` + `/v1/status`, Settings toggles, wired
   through the service layer; expose the read tool set (§4.2) + `POST /navigate`.
3. **Discovery & orientation** — `llms.txt` + served `/docs/*.md` (§8).
4. **Dictionary tools** (§9) — once `DictionaryService` lands.
5. **MCP adapter** (§7) — thin stdio proxy to the HTTP API, for BYO-MCP-chat users.
6. **B (in-app Claude)** and deeper **D (RAG)** — layered on the retrieval + tool substrate once it exists.

## 14. Open questions / risks

- Exact tool/endpoint set and result shapes (paragraph granularity, hit offsets). *(Script of returned text:
  **decided** — romanized default with per-request `script` override; possible future preferred-script
  setting. §11. Highlight/anchor coordinate model: **decided** — source-offset coordinate space, tiered
  cursor → source span → opt-in token offsets, `includeTokenOffsets` on occurrences + passage. §6.1.)*
- Highlight-command residuals (§6.1): token granularity (word tokens; punctuation/danda not independently
  addressable), and whether the surface-E command also accepts the content-addressed fallback from day one.
- *(Local API default: **decided — off**; user opts in from Settings. §6.)*
- *(Disambiguation: **decided — candidates back to the agent**, which asks its user; no app-side pick-list
  for API-initiated navigation. §4.1.)*
- Anchor-catalog design (§4.2): what gets precomputed and when (index time vs first request), and its shape
  (per-edition page ranges, paragraph extents, chapter anchors).
- Generation pipeline for the single-sourced API reference (annotations → routes + MCP defs + llms.txt section).
- `DictionaryService` API shape (prerequisite for §9).
- Whether **B** additionally offers an on-device model for the offline case (the BYO-endpoint support in
  §11.1 already covers local runners), or stays cloud-first + local-runner.
- Grounding/citation strategy to keep generated output faithful to the corpus (§11): how much context to
  retrieve, how citations are rendered, how translation presets constrain output.

## 15. Related

- **#41** — App Intents / Siri & Apple Intelligence (surface **A**).
- **#27** — Vector / semantic search + RAG (surface **D**).
- `docs/features/in-progress/DICTIONARIES.md` — dictionary feature (§9 prerequisite).
- Auto-update work (#178/#181) — shares the offline/disconnected-user constraint (§11) and the
  user-control/Settings philosophy (§6 consent toggles).

## 16. Review log

**2026-07-04 review (Fable 5)** — revisions made in place:
- §4.1: navigation command must return a **structured outcome/error** (incl. `ambiguous-reference` with
  candidates), never fire-and-forget; §4.2: token-frugal result shapes (pagination, limits).
- §6: security model tightened — bearer **header-only** token (never query), **no CORS at all** + reject
  `Origin`-bearing requests, 0600 + PID in `local-api.json`, and an **honest threat-model** statement
  (browser-origin attacks are defeated; same-user local processes are the OS's boundary, not ours).
- §6: added `GET /v1/status` (readiness/index-state; agents poll, `not-ready` instead of hangs),
  **single-instance** behavior, **Settings consent toggles** (master enable + separate remote-control
  toggle + visible indication when an agent navigates), URL-scheme registration flagged as a packaging change.
- §7: MCP **adapter packaging** — ships in the app bundle (version-matched, signed), "Copy MCP
  configuration" affordance in Settings, stale-PID/cold-start/version-skew handling.
- §8: discovery endpoints (`/llms.txt`, `/docs/*`) **unauthenticated** to simplify bootstrap; llms.txt is
  self-identifying (app + API version).
- §11: agent-facing text defaults to **romanized Pāli** (`script` override; IPE stays internal) — resolves
  a former open question. New **§11.1 model access policy** for surface B: OpenAI-compatible + Anthropic
  Messages standards, BYO key/endpoint (incl. local runners), curated frontier tier with **Claude-first**
  default, discourage free/weak models for canonical translation (advisory, not block), grounded-and-cited
  as B's design goal, prompt presets, and visual distinction of generated vs canonical text.
- §14: open questions updated (API default-on vs default-off; disambiguation UX).
- Post-review decisions (fsnow, same day): **local API defaults off** (opt-in from Settings); **romanized
  agent-facing default confirmed**, per-request `script` override first, possible preferred-script setting
  later; **disambiguation resolves agent-side** (candidates returned, agent asks its user); planned
  **anchor catalog** — precomputed lookups of valid navigation targets, faster than parsing the data files.

**2026-07-05 (fsnow + Opus 4.8)** — new **§6.1 highlight/anchor coordinate model**, worked out while
watching cold-agent iteration 7 struggle with proximity. Key realizations, now decided:
- The read surface hands the agent script-converted, markup-stripped prose plus one lonely `cursor`; the
  forward render is lossy/one-way, so a rendered offset can't be mapped back to XML. Highlighting must live
  in the **source-offset** space (what `cursor`/search offsets/the WebView highlighter already use), and
  **every offset the agent hands back must have originated from us** — no invented coordinates.
- **Tiered escalation**: `cursor` anchor+navigate (free today) → hit/snippet **source spans** (a few more
  fields; today's `HitLength` is *rendered*, not source) → **`includeTokenOffsets`** opt-in token array
  `[{ text, start, length }]` for word-level control. Flag lives on **both** occurrences and passage.
- **Proximity forced it**: a multi-span hit can't be one `HitStart/HitLength`, so the snippet model becomes
  an ordered `highlights: [{ start, length, isAnchor }]` (single-term = degenerate one-range case), and
  occurrences accepts the multi-word/proximity query directly instead of round-tripping the `~`-composite
  term through the single-term lookup. Implementation deferred pending the iteration-7 friction report.
