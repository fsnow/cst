# Cloud / Web Session Setup (Claude Code on the web)

Notes for running CST work in a **remote/ephemeral cloud container** (Claude Code on the
web, GitHub Actions, etc.) rather than on a local dev machine (Kestrel). The container is
cloned fresh from the repo each session and reclaimed after inactivity, so **anything not in
git must be re-provisioned every session**. This page records what has to be set up and the
gotchas, so they don't have to be rediscovered.

> Local macOS dev (Kestrel) already has the SDK and the corpus and needs none of this. This
> page is only for fresh cloud containers.

## TL;DR — what's missing in a fresh container

| Need | Status in a fresh cloud container | Fix |
|------|-----------------------------------|-----|
| Source repo | ✅ cloned fresh | — |
| **XML corpus** (217 Devanagari books) | ❌ not present (lives outside the repo) | Download from `raw.githubusercontent.com` (works) — see below |
| **.NET 10 SDK** | ❌ not installed; Microsoft download CDNs are **egress-policy blocked** | Needs the network policy widened, or a pre-baked image — see below |

Without the SDK you can **write** code but cannot `dotnet build` / `dotnet test` / benchmark.
The corpus alone is not enough for verification — both are required.

## Outbound network: the egress proxy

All outbound HTTPS goes through a policy-enforcing proxy (`HTTPS_PROXY=http://127.0.0.1:<port>`,
CA bundle at `/root/.ccr/ca-bundle.crt`). Diagnose with:

```bash
curl -sS "$HTTPS_PROXY/__agentproxy/status"     # proxy state + recentRelayFailures (the real reason)
```

- A **403 / 407** on a `CONNECT` is an **organization egress-policy denial** for that host.
  Do **not** retry or route around it (mirrors, alternate CDNs) — it must be allow-listed in
  the environment's network policy, or report it.
- `raw.githubusercontent.com` and `github.com` **are reachable** — that's how we get the corpus
  and the dotnet-install script.
- `pypi.org`, `registry.npmjs.org`, crates, and the Go module proxy are in the proxy `noProxy`
  allow-list; most Microsoft/Azure dotnet hosts are **not**.

## Provisioning the XML corpus (works today)

The 217 Devanagari TEI books are **not** in this repo. The app normally downloads them from the
public `VipassanaTech/tipitaka-xml` repo (`XmlUpdateService`), and we can do the same by hand.

- Source: `https://raw.githubusercontent.com/VipassanaTech/tipitaka-xml/main/deva%20master/<file>.xml`
  - Defaults come from `XmlUpdateSettings`: owner `VipassanaTech`, repo `tipitaka-xml`,
    branch `main`, path **`deva master`** (note the space → `%20` in URLs).
- File list: the 217 canonical filenames are hard-coded in `src/CST.Core/Books.cs`
  (`book.FileName = "..."`).
- Files are **UTF-16-LE with BOM** (`ff fe`), ~226 MB total.

```bash
# 1. Extract the authoritative 217-file list from Books.cs
python3 - <<'PY' > /tmp/booklist.txt
import re
data = open('src/CST.Core/Books.cs', encoding='utf-8').read()
print("\n".join(re.findall(r'FileName\s*=\s*"([^"]+\.xml)"', data)))
PY

# 2. Download them in parallel (raw.githubusercontent is allowed through the proxy)
CORPUS="$HOME/cst-corpus/deva"; mkdir -p "$CORPUS"
BASE="https://raw.githubusercontent.com/VipassanaTech/tipitaka-xml/main/deva%20master"
xargs -P 12 -I{} sh -c '[ -s "'"$CORPUS"'/{}" ] || curl -sS -f -o "'"$CORPUS"'/{}" "'"$BASE"'/{}"' < /tmp/booklist.txt
ls -1 "$CORPUS"/*.xml | wc -l   # expect 217
```

### Point the tests at the corpus

The equivalence/perf tests read `CST_XML_DIR`, falling back to
`~/Library/Application Support/CSTReader/xml` (a macOS path that doesn't exist in Linux
containers). Set the env var to wherever you downloaded the corpus:

```bash
export CST_XML_DIR="$HOME/cst-corpus/deva"
```

See `src/CST.Avalonia.Tests/Conversion/ConverterEquivalenceTests.cs` and
`Performance/ScriptConverterPerformanceTests.cs`.

## Provisioning the .NET 10 SDK (currently blocked)

The official installer script downloads fine from GitHub, but the **SDK payload CDNs are
egress-blocked** in the default policy:

```bash
# Script itself: OK from GitHub (dot.net redirector is blocked, raw GitHub works)
curl -sSL -o dotnet-install.sh \
  https://raw.githubusercontent.com/dotnet/install-scripts/main/src/dotnet-install.sh

# Payload: BLOCKED (403 CONNECT) under the default network policy
./dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"
# → 403 on builds.dotnet.microsoft.com, ci.dot.net (and aka.ms redirector)
```

To enable in-container build/test, **one of**:

1. **Widen the network policy** for the environment to allow the dotnet hosts, then re-run the
   installer. Hosts observed needed: `aka.ms`, `builds.dotnet.microsoft.com`, `ci.dot.net`
   (and likely `dotnetcli.azureedge.net` / `dotnetcli.blob.core.windows.net`). Configure via
   the environment's network policy — see
   https://code.claude.com/docs/en/claude-code-on-the-web.
2. **Bake the SDK into the environment image / setup script** so it's present at session start
   (a SessionStart hook or the environment setup script). This also lets a setup script
   pre-download the corpus.

Until one of those is in place, do build/test verification on a local machine (Kestrel).

## Running the converter verification (#86), once the SDK is present

```bash
export CST_XML_DIR="$HOME/cst-corpus/deva"
cd src/CST.Avalonia
dotnet test --filter "FullyQualifiedName~ConverterEquivalenceTests"        # byte-identical oracle
dotnet test --filter "FullyQualifiedName~ScriptConverterPerformanceTests"  # before/after timings
```

## Misc gotchas

- **GitHub MCP scope:** this session's GitHub API access is scoped to `fsnow/cst` only.
  The corpus comes from a *different* repo (`VipassanaTech/tipitaka-xml`) — fetch it via plain
  **raw HTTPS** (allowed), not the GitHub MCP tools (which would be denied for that repo).
- **Corpus is UTF-16-LE:** the corpus XML files are UTF-16-LE with BOM; byte-level `grep`/`sed` is
  unreliable — decode first. (Repo *source* files are UTF-8 + LF, per `.gitattributes`.)
- **Branch:** cloud work for this effort lands on the designated feature branch; commit/push
  only when explicitly asked (per the root `CLAUDE.md` hard rules).
