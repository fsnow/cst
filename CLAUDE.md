# CLAUDE.md — Agent guide for CST Reader

CST Reader (**CST = Chaṭṭha Saṅgāyana Tipiṭaka**) is a cross-platform Pāli text reader — .NET 10 + Avalonia UI, a ground-up rewrite of the WinForms CST4. Texts are provided by the Vipassana Research Institute (VRI). Currently **Beta 5 in development**; development/testing is on **macOS** (Windows/Linux are designed-in but untested).

- **Feature overview:** see [README.md](README.md) (front page).
- **Roadmap / planned work:** [GitHub issues](https://github.com/fsnow/cst/issues) (`feature`/`enhancement` labels) + specs in [docs/features/planned/](docs/features/planned/). Issues are the canonical tracker.
- **Doc index:** [docs/README.md](docs/README.md).
- **Working dir:** `src/CST.Avalonia`. **XML books:** `~/Library/Application Support/CSTReader/xml` (217 TEI XML files, **UTF-16-LE**).

## Hard rules — do not violate
- **Never use the word "Buddhist"** anywhere — app UI or documentation. Use "Pāli", "Tipiṭaka", "VRI texts", etc.
- **Commit/push only when explicitly asked.** The user reviews changes first.
- **Script-conversion code uses `\uXXXX` escapes** — never paste literal/invisible non-Latin characters into source.
- **The corpus XML is UTF-16-LE** — byte-level `grep`/`sed` is unreliable; decode first.
- **The docking UI is non-negotiable** — never remove, replace, or "simplify away" the dock-based interface.
- **CEF/WebView: never carry a *live* WebView across a re-parent** — it SIGSEGVs on macOS. Float/unfloat go through the controlled button paths (dispose-before-move + fresh browser). See [docs/architecture/DOCK_SUBSYSTEM.md](docs/architecture/DOCK_SUBSYSTEM.md).
- **Downloaded source PDFs are a preservation mechanism, not an evictable cache** — never propose deleting them.

## Build / run / test
```bash
cd src/CST.Avalonia
dotnet build
dotnet run
dotnet test                                                   # full suite
dotnet test --filter "FullyQualifiedName~CstDockFactoryTests" # one class
```
macOS packaging/signing/notarization: `src/CST.Avalonia/package-macos.sh {arm64|x64}` then `notarize-macos.sh`. Full steps + the pre-release version-string checklist: [docs/development/RELEASE_PROCESS.md](docs/development/RELEASE_PROCESS.md).

## macOS code signing & entitlements
**Notarized apps fail *silently* without the right entitlements** (network calls hang → high CPU from retries, not a clear error). Required (in `package-macos.sh`): `cs.allow-jit`, `cs.allow-unsigned-executable-memory`, `cs.disable-library-validation`, `network.client`. Adding a feature that needs more (camera, mic, server, downloads…)? Add the entitlement and re-verify: `codesign -d --entitlements - "/Applications/CST Reader.app"`.

## Documentation workflow
Docs live in `docs/` (`architecture/`, `implementation/`, `features/{planned,in-progress,implemented}`, `research/`, `development/`, `testing/`). Feature docs move planned → in-progress → implemented. **When adding/removing a doc, update [docs/README.md](docs/README.md).** Bugs/features are tracked as GitHub issues, not in markdown backlogs.

## TodoWrite
Use it for multi-step work (3+ steps), multi-cause debugging, or any checklist: create the list immediately, work through every item, mark complete as you go.

## Key architecture pointers
- **Dock subsystem** (most fragile; CEF re-parent constraints, the protected layout spine, recreate-on-demand): [docs/architecture/DOCK_SUBSYSTEM.md](docs/architecture/DOCK_SUBSYSTEM.md) + the complete workarounds inventory [docs/architecture/DOCK_WEBVIEW_WORKAROUNDS.md](docs/architecture/DOCK_WEBVIEW_WORKAROUNDS.md).
- **Layout/ViewModels/Services** map + tech stack (Lucene IPE search, ReactiveUI, Dock.Avalonia, WebViewControl/CEF, Serilog): see README + `docs/architecture/`.
