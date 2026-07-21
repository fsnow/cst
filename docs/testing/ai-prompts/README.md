# AI testing prompts

Prompts we hand to a **cold AI agent** (Claude Code, Claude Desktop, or any MCP/HTTP client) to exercise CST
Reader's AI interfaces — the local HTTP API and the MCP server, together called *surface C*.

They serve three purposes, in order of importance:

1. **Testing.** A cold agent following a prompt with no prior knowledge of the app is the only honest test of
   whether the tool descriptions, `llms.txt`, and error messages actually teach an agent to use the corpus. If
   the agent flails, the surface is wrong — not the agent.
2. **Showing what is possible.** These double as worked examples of the kinds of questions a modern agent can
   answer against the Tipiṭaka once it can search, read, gloss, and now *show* passages.
3. **A stable eval basis.** Because the task and the rubric stay fixed, re-running a prompt as new models ship
   gives a comparable read on how well our surface teaches an agent that has never seen it — and on how much
   of the improvement is the model rather than our tool descriptions. Each prompt therefore keeps a **results
   log**: date, model, platform, outcome. Change a prompt's task only when the feature changes, and note it in
   the log, since an edited task breaks comparability with earlier rows.
4. **Cross-platform coverage.** Each prompt works on macOS and Windows, so the same test can run on either
   machine and the results compared.

## How to use one

1. Launch CST Reader and let it finish indexing.
2. Open a fresh agent session — **no prior context about this repo**; a cold agent is the point.
3. Paste the prompt verbatim.
4. Score the run against the prompt's failure signals and add a row to its results log. A wrong turn is
   a defect in the surface, not in the agent.

## Conventions for a prompt in this directory

- **Cold-start-safe.** Begin with the handshake-file discovery for both platforms; assume nothing is configured.
- **Platform-neutral.** Give both the macOS and Windows path/command; never hardcode a home directory.
- **Task-shaped, not API-shaped.** Ask a scholarly question, not "call `/v1/search`". Whether the agent finds
  the right endpoint is exactly what's being tested.
- **State the expected outcome** for the human watching — not in the part pasted to the agent.
- **List explicit failure signals**, so a run is scored the same way by whoever runs it, and keep a results log.

## Prompts

| Prompt | Exercises | Requires |
| --- | --- | --- |
| [navigate-show-me.md](navigate-show-me.md) | `POST /v1/navigate` + MCP `navigate` — driving the reader window, and the consent gate (#187) | Settings → AI → "Allow agents to drive the reader" |
