# App Intents / Apple Intelligence & Siri Integration (Planned — Research)

**Status:** Planned / feasibility research. Not started.
**Last updated:** June 2026
**Platform scope:** macOS (Apple Intelligence / Siri / Shortcuts / Spotlight). No equivalent on Windows/Linux.

## 1. Motivation

Apple's **App Intents** framework is how a macOS/iOS app exposes its *actions* and *content* to the
system — Shortcuts, Spotlight, Siri, and (increasingly) **Apple Intelligence**. With the 2025–2026 push
toward an LLM-backed, agentic Siri, App Intents has become the substrate that lets the assistant *call an
app's actions directly* (via structured "App Intent domains" / assistant schemas) instead of scraping the
UI. For CST Reader this would mean a user could say or shortcut things like:

- "Open the Dīgha Nikāya in CST Reader"
- "Go to DN 1, page 12" / "Go to paragraph 45"
- "Search CST for *mettā*"
- Spotlight surfacing individual books/suttas as results that deep-link into the app

Frank's framing: real AI support is arriving on macOS (Apple Intelligence, a revamped Siri, and
third-party model integration), so it's worth capturing App Intents as a future feature now and
understanding what adoption would entail given our **Avalonia / .NET 9** stack.

**Success bar (Frank):** the deliverable is genuine **Siri / Apple Intelligence** integration via App
Intents (approach A below). Anything short of Siri support — AppleScript / Apple-Events scriptability, or
bare URL-scheme Shortcuts as an *end state* — is **not a viable substitute** for the feature. Such options
matter only as the **bridge layer** the real path reuses, never as the deliverable itself.

## 2. Landscape (as of WWDC 2026, June 8 2026)

- **App Intents expanded** to connect apps to Siri's AI capabilities: *personal context understanding*,
  *app actions*, and *onscreen awareness*. Entity schemas feed content into the Spotlight **semantic
  index**; intent schemas let users take action by natural language with no fixed phrases.
- **Advanced in-app actions** (Siri performing complex actions within/across apps via App Intents) — the
  long-delayed "agentic Siri," tracked for macOS/iOS 27 (fall 2026), with pieces landing from ~macOS 26.4.
- **Siri Extensions** — a new framework letting third-party AI chat services (**Anthropic Claude**, Google
  Gemini, OpenAI ChatGPT) plug into the Siri experience on iPhone/iPad/Mac.
- **Foundation Models framework** (on-device, Swift) enhanced: image input, server-model support, custom
  skills, Dynamic Profiles. A new **Core AI** framework targets full on-device LLM execution on Apple
  silicon (unified memory + Neural Engine). Apps can use "models of their choice (Claude, Gemini, …) that
  implement the new language model protocol."
- Context on Siri's brain: Apple reportedly selected **Google Gemini** to power the next-gen Siri's
  foundation model (the direct Anthropic/Claude deal fell through over cost); **Claude returns** to users
  via the user-selectable Siri Extensions, not as Siri's core model.

**Takeaway:** App Intents (not a bespoke Claude/Siri API) is the integration point an app like ours would
adopt. It's how both Apple's Siri and any plugged-in assistant discover and invoke our actions/content.

## 3. Candidate CST intents & entities

| Kind | Example | Notes |
|---|---|---|
| `AppEntity` | **Book** (Tipiṭaka text) | id = file name; queryable; feeds Spotlight semantic index. Maybe also a **Chapter/Sutta** entity. |
| `AppIntent` | **OpenBook(Book)** | deep-links to the book tab |
| `AppIntent` | **GoTo(Book, reference)** | reuse the planned Go-To reference parsing (Myanmar/PTS/VRI/Thai page, paragraph) |
| `AppIntent` | **Search(query, books?)** | run a search, open the results panel |
| `AppShortcut` | phrases for the above | "Open … in CST Reader", "Search CST for …" |

These map cleanly onto features we already have or plan (open book, Go-To navigation, search), so the
*intent surface* is small and well-defined. The hard part is **how to expose it from a .NET app**.

## 3a. Spoken-Pāli understanding — a key dependency

Even with intents exposed, the *quality* of the experience hinges on the assistant understanding
**spoken Pāli**, which splits into two layers we don't fully control:

1. **ASR (speech → text).** Siri has no Pāli dictation model, so spoken terms like *Dīgha Nikāya*,
   *Majjhima*, *Visuddhimagga*, *paṭiccasamuppāda* arrive mangled/anglicized — upstream of anything App
   Intents sees.
2. **NLU (text → the right entity/action).** Mapping that imperfect transcription to the correct
   book/sutta/reference — where **real-LLM-level understanding** (Apple Intelligence's model, or a Claude
   Siri Extension) earns its keep: knowing "the long discourses" = Dīgha Nikāya, "loving-kindness" → mettā,
   tolerating phonetic spellings.

We can't supply the assistant's brain, but we can make our surface **maximally resolvable**:
- Give `Book`/`Sutta` AppEntities **rich aliases** — Pāli name + common English name + abbreviations
  (DN/MN/SN) + alternate transliterations/scripts — so any model has more to match against.
- Back the `EntityQuery` with our **own fuzzy/phonetic matching** (reusing the existing search +
  script-conversion infra) as a safety net, so a rough transcription still resolves on our side rather than
  depending entirely on the assistant.

Net: experience quality tracks the assistant's LLM understanding of Pāli (outside our control), but rich
aliases + a forgiving resolver raise the floor regardless of which brain is driving.

## 4. The core challenge: App Intents is Swift-only + build-time metadata

Two hard requirements make this non-trivial for an Avalonia/.NET app:

1. **Swift-only API.** App Intents (iOS 16 / macOS 13+) has **no Objective-C surface**. .NET's macOS interop
   (Avalonia's AppKit backend, or classic Xamarin-style bindings) reaches **Objective-C** runtimes; it does
   not bind Swift-only frameworks. So the intents/entities **cannot be declared in C#**.
2. **Build-time metadata extraction.** Xcode's `appintentsmetadataprocessor` parses the Swift App Intents
   source at build/archive time (via Swift reflection metadata) and emits a `Metadata.appintents` bundle
   *inside the .app*. The system uses that bundle to **discover** intents. CST builds via `dotnet publish`
   + a hand-rolled `package-macos.sh` — **neither step runs this processor**, so even hand-written intents
   would be invisible to Siri/Spotlight.

**Conclusion:** there is no pure-.NET path. Real App Intents adoption requires **actual Swift code,
compiled with the Xcode toolchain (so the metadata processor runs), embedded in our .app bundle**, plus a
bridge from those Swift intents to the running .NET process.

## 5. Integration approaches (ranked)

### A. Embedded Swift App Intents component + bridge to the .NET app  *(the "real" path)*
- A small **Swift Package / App Intents bundle** (or app-intents-bearing helper) declares `Book` (AppEntity),
  `OpenBook`/`GoTo`/`Search` (AppIntent), and `AppShortcuts`. Built with Xcode tooling so
  `appintentsmetadataprocessor` produces `Metadata.appintents`; the result is embedded in `CST Reader.app`.
- On invocation, the Swift intent **forwards to the running .NET app** via one of:
  - **Custom URL scheme** (`cstreader://open?book=…&ref=…`) — simplest; the .NET app already can register a
    scheme and handle deep links.
  - **Local IPC** — XPC service, a loopback HTTP endpoint, or a named pipe the .NET app hosts.
  - **App Group + shared file / `UserDefaults`** the .NET side polls or observes.
  - **Distributed notifications** (`NSDistributedNotificationCenter`, Obj-C-accessible on both sides).
- **Pros:** full Apple Intelligence / Siri / Spotlight / Shortcuts integration; future-proof for agentic Siri.
- **Cons / unknowns (need a PoC):**
  - Can an App Intents `Metadata.appintents` bundle be **embedded in a non-Xcode `.app`** and still be
    discovered? (Apple's flow assumes an Xcode app/extension target.) **Biggest open question.**
  - Adds a **Swift + Xcode toolchain** step to the macOS build (today: pure `dotnet` + shell). CI/codesigning/
    notarization/entitlements all grow.
  - Bridging reliability (cold-start: intent fired while app not running → launch + route the deep link).

### B. URL scheme + Shortcuts  *(lightweight interim)*
- Register a custom URL scheme; users compose **Shortcuts** that call `cstreader://…` URLs (Shortcuts'
  "Open URL" action), which Siri can run. No Swift, no App Intents.
- **Pros:** cheap, pure-.NET (just deep-link handling), works today; gives basic Siri/Shortcuts reach.
- **Cons:** **not** in the App Intents / Apple Intelligence semantic layer — no rich entities, no Spotlight
  semantic index, no "agentic" composition, clunky user setup (manual Shortcut authoring).

### C. NSUserActivity / Spotlight donation  *(Obj-C-accessible, complementary)*
- Donate `NSUserActivity` for opened books / searches (Obj-C API → reachable via Avalonia native interop):
  enables Siri Suggestions, Handoff, and Spotlight indexing of recent items.
- **Pros:** no Swift/Xcode; modest but real system presence.
- **Cons:** suggestion/indexing only — not invokable actions; predates the App Intents model Apple now pushes.

## 5a. Update (June 2026): `net10.0-macos` + .NET 10 Swift interop — refines approach A's bridge, not the requirement

A claim worth recording (it will resurface): *"change the TFM to `net10.0-macos` alongside the desktop
target → full .NET-for-macOS workload → write Swift-style interop and register `NSAppleEventDescriptor`s
natively."* Assessment against the success bar and §4:

- **Does not remove the Swift-only / build-time requirement.** App Intents has no Obj-C surface, so the
  .NET-for-macOS workload (which projects **Objective-C** frameworks into C#) still doesn't expose it.
  Intents must be **authored in Swift and processed by `appintentsmetadataprocessor`**. `net10.0-macos`
  changes the *plumbing*, not the *authoring* — §4's conclusion stands.
- **It is, though, a cleaner bridge than the §5.A options.** .NET 10's **Swift interop** (calling Swift
  directly via the Swift calling convention) is the most direct way for our managed code to talk to the
  embedded Swift App-Intents component — preferable to URL-scheme / XPC / distributed-notification bridges
  **if** the Avalonia-TFM question (§7) resolves favorably. Add it to the §5.A bridge list as the preferred
  mechanism. The macOS workload also uses Xcode tooling for packaging, which *might* be the natural place to
  run the Swift build + metadata phase — to be confirmed in the PoC.
- **`NSAppleEventDescriptor` is a red herring here.** That is **Apple Events / AppleScript** — the classic
  scripting mechanism, *not* App Intents and *not* Siri / Apple Intelligence. Per the success bar,
  AppleScript scriptability is **ruled out** as a substitute (it does not deliver Siri). Recorded so the
  conflation in that paragraph doesn't get chased again.

## 6. Build & packaging implications (approach A)
- `package-macos.sh` would gain an **Xcode-built Swift target** producing the App Intents bundle + metadata,
  merged into `Contents/` (likely under `Contents/Frameworks` or an app-intents location) before signing.
- The Swift component and any helper must be **Developer ID signed + notarized** alongside the rest (we
  already sign dylibs/helpers for CEF — same machinery, new artifact).
- Possibly new entitlements (App Groups for the shared-state bridge; Siri entitlement if required).

## 6a. How this differs from our existing CEF native handling

A natural question: we already do platform-specific native work for CEF — is a Swift App Intents
component "just more of the same"? **Mostly no.** Our CEF handling does **zero native compilation**: CEF
arrives **prebuilt** via the `WebViewControl-Avalonia` NuGet (`libcef` + `Xilium.CefGlue.BrowserProcess`);
`dotnet publish` copies it in; `package-macos.sh` creates the 4 helper `.app` bundles as **shell-script
launchers** that `exec` the prebuilt CEF subprocess, then code-signs/notarizes. No `swiftc`/`xcodebuild`/
`clang` runs anywhere.

| Aspect | CEF (today) | App Intents Swift component |
|---|---|---|
| **Native source** | None — consume a prebuilt third-party binary | We **author Swift source** (entities/intents) |
| **Build toolchain** | `dotnet` + `cp` + `codesign` (no compiler) | Adds **Xcode/`swiftc`** + the `appintentsmetadataprocessor` build phase |
| **System discovery** | None — binaries just need to be *found* at runtime (`browser-subprocess-path`) | OS reads a build-time **`Metadata.appintents`** bundle to *discover* intents (no CEF analog) |
| **ABI / reachability** | **C ABI** → P/Invokable; called via the managed CefGlue wrapper | **Swift-only**, no C/Obj-C surface → **unreachable from .NET**; must be a separate Swift unit |
| **Call direction** | **We call CEF**, in-process | **OS calls the Swift component**, out-of-process — then it bridges *back* to our app |
| **Process/lifecycle** | Helper subprocesses **we launch** while running | System-driven; can fire **while the app is closed** (cold-start launch + route) |
| **Packaging + signing** | Add artifacts to `Contents/`, Developer-ID sign, hardened runtime, entitlements, notarize | **Same machinery** — one more signed artifact (+ possibly App Groups/Siri entitlements) |

**Bottom line:** the *packaging/signing* step is genuinely "more of the same" (we'd reuse the existing
`codesign`/notarize flow for one more artifact). Everything upstream is new: CEF is **consume + wrap +
sign a prebuilt C-ABI binary**; App Intents is **author + compile + register Swift source that the system
drives out-of-process** — a new toolchain, a build-time metadata phase with no CEF parallel, an inverted
(OS-invokes-it) interop model, and a Swift-only ABI .NET can't bind.

## 7. Open questions / risks
- **Feasibility of embedding App Intents in a non-Xcode bundle** — must be proven with a minimal PoC before
  committing. If Apple's discovery strictly requires a real Xcode app target, approach A may be blocked and
  we fall back to B/C (or reconsider the build to produce the macOS app via Xcode wrapping the .NET output).
- **Can an Avalonia app target / multi-target `net10.0-macos` (the macOS workload) cleanly?** Avalonia
  targets plain `net10.0` and does its own native interop; combining it with the MAUI/macOS workload TFM
  (to get .NET 10 Swift interop + Foundation bindings) is **non-standard and unproven for our stack** —
  validate before relying on Swift interop as the bridge. If it doesn't compose, fall back to the §5.A
  out-of-process bridges (URL scheme / XPC / distributed notifications), which need no TFM change.
- **Apple Intelligence is region-limited** and OS-version-gated (macOS 26.4 / 27) — feature availability is
  uneven for our users.
- **Maintenance surface**: a Swift sub-project + bridge is new tech in an otherwise .NET codebase.
- **macOS-only**: no Windows/Linux analog; keep the intent layer thin over existing cross-platform actions.

## 8. Recommendation / phasing
1. **Now (cheap):** add a **custom URL scheme + deep-link routing** (approach B) reusing the Go-To/open/search
   plumbing. Immediately enables user-authored Shortcuts and basic Siri reach, and is the **bridge layer**
   approach A would build on anyway. Add **NSUserActivity** donations (C) for Spotlight/Suggestions.
2. **Spike (de-risk):** a minimal **Swift App Intents PoC** embedded in a test `.app` to answer the
   discovery/embedding question (§7) — the gate for approach A.
3. **Full adoption (when warranted):** if the PoC succeeds, implement the `Book` entity + open/goto/search
   intents in Swift, bridged to the .NET app, and wire `package-macos.sh` accordingly — delivering real
   Apple Intelligence / Siri / Spotlight integration.

## Sources
- [App Intents — Apple Developer Documentation](https://developer.apple.com/documentation/appintents)
- [Apple: new intelligence frameworks and advanced tools (Newsroom, June 2026)](https://www.apple.com/newsroom/2026/06/apple-aids-app-development-with-new-intelligence-frameworks-and-advanced-tools/)
- [Get to know App Intents — WWDC25](https://developer.apple.com/videos/play/wwdc2025/244/)
- [Making onscreen content available to Siri and Apple Intelligence](https://developer.apple.com/documentation/appintents/making-onscreen-content-available-to-siri-and-apple-intelligence)
- [Gurman: new App Intents + Siri overhaul on track (9to5Mac, Aug 2025)](https://9to5mac.com/2025/08/10/apple-intelligence-new-siri-app-intents-on-track-to-launch-next-spring-report/)
- [iOS 27: Apple to let Claude and other AI apps integrate with Siri (9to5Mac, Mar 2026)](https://9to5mac.com/2026/03/26/ios-27-apple-will-reportedly-let-claude-and-other-ai-chatbot-apps-integrate-with-siri/)
- [appintentsmetadataprocessor changes (Marc Palmer)](https://marcpalmer.net/changes-in-app-intents-pre-processing-causing-confusing-errors-in-xcode-16/)
- [Donating Shortcuts (NSUserActivity) — Apple Developer](https://developer.apple.com/documentation/SiriKit/donating-shortcuts)
- [Avalonia macOS platform guide](https://docs.avaloniaui.net/docs/platform-specific-guides/macos)
