# Prompt: citation fidelity

**Exercises:** `/v1/search` → `/v1/occurrences` → `/v1/passage`, and specifically whether an agent **cites by
page rather than paragraph** and reports honestly when a reference is uncertain.

**Platforms:** macOS and Windows.

**Requires:** CST Reader running with indexing finished and the local API enabled. Remote control is *not*
needed — this prompt is read-only.

**Why this one matters.** Paragraph numbers are a weak addressing scheme in this corpus: the same number occurs
more than once in 102 of 217 books, and ~3,600 paragraphs are printed as ranges. Page numbers are strong. We
changed `llms.txt` and the tool descriptions to steer agents toward pages — this prompt tests whether that
steering actually works on an agent that has never seen the app. If the agent still cites bare paragraph
numbers, the docs failed, not the agent.

---

## The prompt

```
I have an app called CST Reader running on this machine. It publishes a local API for AI agents.

Find its handshake file — on macOS it is
  ~/Library/Application Support/CSTReader/local-api.json
and on Windows it is
  %APPDATA%\CSTReader\local-api.json
It contains a port, a bearer token, and the path to the API's own documentation. Read the documentation
before you do anything else, and follow it.

I'm interested in the word "appamāda" (heedfulness/diligence).

1. Roughly how often does it occur across the corpus, and which parts of the canon favour it?
2. Pick two occurrences that look substantively different from each other, read enough of each to
   summarise what it is saying, and tell me what the difference is.
3. Give me the citations for those two passages, in a form I could look up in a printed edition.
4. Tell me how confident you are in each citation, and why.
```

### What should happen

- The agent reads `llms.txt` before guessing endpoints.
- Step 1 uses the term index rather than trying to read whole books.
- Step 3 citations are **page-based** — an edition, volume, and page (e.g. "VRI vol. 1, p. 23") — taken from
  the `refs` on an occurrence or the `pages` on a passage window.
- Step 4 distinguishes what it actually knows from what it inferred. A good answer notes that the page
  citation is read directly from the text, and does not over-claim.

### Failure signals

- **Cites a bare paragraph number** as the primary reference. This is the main thing under test.
- Constructs a citation in an edition the book does not carry — e.g. gives a VRI page for a text VRI never
  printed. The editions cover different parts of the corpus; the agent must take the edition from what the
  book reports.
- Presents a paragraph number as unambiguous, or silently drops the range when a paragraph is printed as one
  (`16-26`).
- Invents a page number that appears in neither `refs` nor `pages`.
- Claims uniform confidence across both citations without inspecting anything.
- Reads whole books when the term index would answer the question.

### A harder follow-up, if the first part went well

```
Now do the same for a text that isn't part of the VRI printed edition. What changes about how you cite it?
```

The point is whether the agent notices that not every text carries every edition's numbering, and adapts —
citing whichever edition the book actually has — rather than reporting an error or fabricating a VRI page.

---

## Results log

| Date | Model | Platform | Cited by page? | Confidence honest? | Notes |
| --- | --- | --- | --- | --- | --- |
| | | | | | |
