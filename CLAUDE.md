# CLAUDE.md — Agent guide for CST Reader

CST Reader (**CST = Chaṭṭha Saṅgāyana Tipiṭaka**) is a cross-platform Pāli text reader — .NET 10 + Avalonia UI, a ground-up rewrite of the WinForms CST4. Texts are provided by the Vipassana Research Institute (VRI). Currently **Beta 6 in development** (Beta 5 released 2026-07); development is on **macOS**, with Windows now shipping too (x64 + arm64) and tested on dedicated machines (Linux remains designed-in but untested).

- **Feature overview:** see [README.md](README.md) (front page).
- **Roadmap / planned work:** [GitHub issues](https://github.com/fsnow/cst/issues) (`feature`/`enhancement` labels) + specs in [docs/features/planned/](docs/features/planned/). Issues are the canonical tracker.
- **Doc index:** [docs/README.md](docs/README.md).
- **Working dir:** `src/CST.Avalonia`. **XML books:** `~/Library/Application Support/CSTReader/xml` (217 TEI XML files, **UTF-16-LE**).
- **CST4 is not in the tree.** `src/CST`, `src/Cst4`, `CST.sln`, `CST4.wxs`, `src/Fonts` and the MAUI POC were removed in August 2026 — they were 40% of the tracked files and no part of the build. Docs still cite `src/Cst4/...` paths on purpose; read them with `git show cst4-final:<path>`. **Never restore them into the working tree to "fix" something** — CST4 was final at 4.1, and the copy that sat on main had already drifted from the shipped source through exactly that kind of well-meant edit. Tags: `cst4-final` (authoritative), `cst4-2-final`, `cst4-main-final`, `cst-maui-final`.

## Hard rules — do not violate
- **Never use the word "Buddhist"** anywhere — app UI or documentation. Use "Pāli", "Tipiṭaka", "VRI texts", etc.
- **Commit/push only when explicitly asked.** The user reviews changes first.
- **Never `git add -A`, `git add .`, or `git add -u`** — always stage explicit paths. The working tree routinely holds the user's pending files (docs, screenshots, reports) and isolated agent worktrees; a blanket add sweeps those into the commit. Stage only the files you changed for the commit at hand.
- **Script-conversion code uses `\uXXXX` escapes** — never paste literal/invisible non-Latin characters into source.
- **The corpus XML is UTF-16-LE** — byte-level `grep`/`sed` is unreliable; decode first. (Repo *source* files are UTF-8 + LF, enforced by `.gitattributes` — only the corpus is UTF-16.)
- **The docking UI is non-negotiable** — never remove, replace, or "simplify away" the dock-based interface.
- **CEF/WebView: never carry a *live* WebView across a re-parent** — it SIGSEGVs on macOS. Books, PDFs and the dictionary are freely draggable and floatable (#39) *because* every float/move trigger funnels through overrides that dispose and evict the live browser first and let a fresh one be built — `SplitToWindow` (drag release, invalid-target drop, float indicator, tab double-click, context-menu Float) and `DisposeAndEvictRecycledView`; "Float all" is blocked outright. **The hazard now is a new path that re-parents a view without going through that funnel.** See [docs/architecture/DOCK_SUBSYSTEM.md](docs/architecture/DOCK_SUBSYSTEM.md).
- **Downloaded source PDFs are a preservation mechanism, not an evictable cache** — never propose deleting them. They are page **scans with no text layer**: find, text extraction and selection in the PDF pane are impossible by construction, not merely unimplemented. Don't investigate making them work.
- **Find is a book feature.** Cmd/Ctrl+F opens the app's own find bar (`BookDisplayView.ShowFindBar`) on the active book, from wherever focus happens to be — never a find over a tool pane's own content. There is no Chromium find to fall back on: Chrome's find bar is browser chrome, not web content, so CEF ships the API without UI and WebViewControl doesn't surface it.
- **Never suggest pausing, "calling it", or a "stopping point"** — you have no sense of elapsed time, so it is never your call. Finish the task, report the result, and either continue or wait for the next instruction. The user decides when to stop.

## Build / run / test
```bash
# from the repo root
dotnet build src/CST.Avalonia
dotnet run --project src/CST.Avalonia

dotnet test src/CST.Avalonia.Tests                                     # full suite (~3 min)
dotnet test src/CST.Avalonia.Tests --filter "FullyQualifiedName~CstDockFactoryTests"   # one class
```
**Always name the test project.** There is no solution file, so a bare `dotnet test` acts on the project in the current directory — and in `src/CST.Avalonia` that is the app, which is not a test project: it restores, runs nothing, and **exits 0**. A silent green indistinguishable from a passing suite. `CST.Avalonia.Tests` is the only test project in the tree.

macOS packaging/signing/notarization: `src/CST.Avalonia/package-macos.sh {arm64|x64}` then `notarize-macos.sh`. Full steps + the pre-release version-string checklist: [docs/development/RELEASE_PROCESS.md](docs/development/RELEASE_PROCESS.md).

## macOS code signing & entitlements
**Notarized apps fail *silently* without the right entitlements** (network calls hang → high CPU from retries, not a clear error). Required (in `package-macos.sh`): `cs.allow-jit`, `cs.allow-unsigned-executable-memory`, `cs.disable-library-validation`, `network.client`. Adding a feature that needs more (camera, mic, server, downloads…)? Add the entitlement and re-verify: `codesign -d --entitlements - "/Applications/CST Reader.app"`.

## Working with issues
Several Claude sessions work this repo (different machines, plus review subagents) and **share no memory with each other**. Anything that should bind all of them lives here or in `docs/`, never in one session's private notes.

- **A bug issue needs Expected / Actual / Contrast, plus how it was found.** The contrast case — where the same action *does* work — is the highest-value line, because it is what separates a defect from intended behaviour. Ask for it if it is missing. Without a stated expectation, the next agent infers intent from code comments, which record *past* intent and may be stale.
- **The issue body is the record, not a transcript.** Edit the body as understanding improves; delete your own wrong comment rather than stacking a retraction on it. Comment only to add something a future reader needs — a decision, a measurement, an outcome.
- **"Working as designed" is never a conclusion to post on your own.** It contradicts a human's report, and the maintainer owns intent. Bring the evidence to him first.
- **Read the contract, not just the code.** XML doc comments on the method you are changing often already answer the question — and separate durable facts (dated, attributed) from rationale, which rots. A comment can be right about the fact and wrong about the reason.

## Documentation workflow
Docs live in `docs/` (`architecture/`, `implementation/`, `features/{planned,in-progress,implemented}`, `research/`, `development/`, `testing/`). Feature docs move planned → in-progress → implemented. **When adding/removing a doc, update [docs/README.md](docs/README.md).** Bugs/features are tracked as GitHub issues, not in markdown backlogs.

## TodoWrite
Use it for multi-step work (3+ steps), multi-cause debugging, or any checklist: create the list immediately, work through every item, mark complete as you go.

## Key architecture pointers
- **Dock subsystem** (most fragile; CEF re-parent constraints, the protected layout spine, recreate-on-demand): [docs/architecture/DOCK_SUBSYSTEM.md](docs/architecture/DOCK_SUBSYSTEM.md) + the complete workarounds inventory [docs/architecture/DOCK_WEBVIEW_WORKAROUNDS.md](docs/architecture/DOCK_WEBVIEW_WORKAROUNDS.md).
- **Layout/ViewModels/Services** map + tech stack (Lucene IPE search, ReactiveUI, Dock.Avalonia, WebViewControl/CEF, Serilog): see README + `docs/architecture/`.
