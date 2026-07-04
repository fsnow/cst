# AI Integration — Claude & Agent Access to the Corpus (Planned — Design of Record)

**Status:** Planned / design of record. Not started.
**Last updated:** July 2026
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
2. **The app's read services exposed as tools** — search, fetch-passage, resolve-reference, **dictionary
   lookup** — each already encapsulating its format (Lucene / TEI XML / binary dict). Backs **C** (and **B**).

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
- **Ephemeral port + per-session token**, both written to `…/CSTReader/local-api.json` (user-readable only);
  the MCP adapter and other clients read it to discover where to connect and how to authenticate.
- **Anti-DNS-rebinding** — validate `Origin` / `Host` headers; no wildcard CORS. (A localhost server is
  reachable by any local process *and* any web page via `fetch`; the navigation endpoints have side effects.)
- **Versioned** — `/v1/…` from day one; the moment an adapter depends on it, it's a contract.
- **Concurrency** — handlers are another concurrent caller into `IndexingService` / `SearchService`; they must
  route through the thread-safe service layer (the reader-refcounting hardened in SRCH-6/11), never around it.
- **UI thread** — navigation/presentation must marshal to the Avalonia dispatcher.
- **Lifecycle** — starts with the app, stops on shutdown; cold-start is the URL scheme's job (§5).

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
- **Model choice + BYO key** — Claude vs on-device vs none; who pays for tokens.
- **Fidelity of canonical text** — this is a sacred-text corpus; hallucinated "translations" or invented
  citations are a real hazard. Grounding (retrieve real passages, cite the reference) matters more here than in a
  typical app, and the standing terminology hard rule applies to any generated output.

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

1. **Navigation/present-in-context core** — the single internal command (§4.1). Highest leverage: backs A, C, E,
   and reuses the deep-link routing phase-1 of #41. Pure-.NET, cross-platform.
2. **Local HTTP API spine** (§6) — loopback + token + `/v1`, wired through the service layer; expose the read
   tool set (§4.2) + `POST /navigate`.
3. **Discovery & orientation** — `llms.txt` + served `/docs/*.md` (§8).
4. **Dictionary tools** (§9) — once `DictionaryService` lands.
5. **MCP adapter** (§7) — thin stdio proxy to the HTTP API, for BYO-MCP-chat users.
6. **B (in-app Claude)** and deeper **D (RAG)** — layered on the retrieval + tool substrate once it exists.

## 14. Open questions / risks

- Exact tool/endpoint set and result shapes (paragraph granularity, hit offsets, script of returned text).
- Generation pipeline for the single-sourced API reference (annotations → routes + MCP defs + llms.txt section).
- `DictionaryService` API shape (prerequisite for §9).
- Whether **B** uses a bundled/on-device model for the offline case, or is cloud-only + BYO key.
- Grounding/citation strategy to keep generated output faithful to the corpus (§11).

## 15. Related

- **#41** — App Intents / Siri & Apple Intelligence (surface **A**).
- **#27** — Vector / semantic search + RAG (surface **D**).
- `docs/features/in-progress/DICTIONARIES.md` — dictionary feature (§9 prerequisite).
- Auto-update work (#178/#181) — shares the offline/disconnected-user constraint (§11).
