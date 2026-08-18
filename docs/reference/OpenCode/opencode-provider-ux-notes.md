# OpenCode Desktop — provider/model UX notes

Working notes for CST Reader's AI settings redesign. Source: screenshots of **OpenCode Desktop v1.18.18**
that Frank downloaded 2026-08-17 to look at somebody else's answer to the same problem.

Related issues: **#678** per-endpoint credentials · **#674** model picker at OpenRouter/HF scale ·
**#671** reasoning effort · **#673** wait timeouts.

Append a new `## Batch N` section per screenshot batch. The three running sections at the bottom
(**Ideas worth stealing** / **Ideas we should NOT take** / **Open questions**) are cumulative — edit them
in place rather than duplicating per batch.

---

## Where we are today (read 2026-08-17, main @ ab88a33)

So the comparison below is against what actually ships, not a memory of it.

`SettingsWindow.axaml:490-657` — the whole AI area is one `AiSettingsViewModel` template, three
`SettingGroup` borders stacked vertically in one scrolling page:

1. **AI Features** — master checkbox + "⚠ Takes effect after you restart CST Reader."
2. **Access for AI Clients** — REST / MCP / remote-control checkboxes, Copy-MCP-configuration button,
   an `Expander` holding the JSON.
3. **Assistant** — enable checkbox, then a flat vertical run of label + control + description:
   - Provider — `ComboBox`, **2 items**: "OpenAI-compatible endpoint", "Claude (Anthropic)"
     (`SettingsViewModel.cs:1418-1422`)
   - Endpoint address — `TextBox`, 480px
   - Model — `TextBox`, 480px, free text, deliberately never a dropdown (`:1493-1497`)
   - `PaliAbilityNote` — the paragraph that replaced the deleted registry's per-model verdict
   - API key — masked `TextBox` + Save + Remove, `KeyStatus` line under it
   - Answer language — `AutoCompleteBox`
   - "What is sent" privacy paragraph
   - `ReadinessText` — asks the SAME resolver the assistant uses ("Ready." / the problem)

Structurally: **one provider selected at a time, one set of fields, one key.** Confirmed at the storage
layer — `AiCredentialStore.AccountFor(ChatProviderKind)` (`Credentials/AiCredentialStore.cs:111`) keys the
keychain entry by the *enum*, and the enum has exactly two members (`ChatProviderResolver.cs:10-17`).
So every OpenAI-compatible endpoint in the world shares **one** key slot: store an OpenRouter key,
point the endpoint at a local Ollama, come back — the OpenRouter key is still what's there, and switching
back to OpenRouter later means re-pasting if you overwrote it. **That is #678, stated concretely.**

Also worth noting for contrast with OpenCode: our AI settings live in ONE page. OpenCode splits the same
territory across **Providers** and **Models** as separate nav items.

---

## Batch 1 (3 screenshots, 2026-08-17)

### Screen 1 — Providers, unconnected state

Left nav is split under two headings: **Desktop** (General, Shortcuts) and **Server** (Servers, Providers,
Models). Version "OpenCode Desktop v1.18.18" pinned bottom-left. Content pane has a page title
("Providers") and a single rounded card containing a vertical list of rows separated by hairline rules.

Each row: monochrome provider glyph, provider name (bold), one-line description under it, and a
right-aligned `+ Connect` button. All six read as *not connected* — every button says Connect.

| Provider | Description as written |
|---|---|
| Anthropic | "Direct access to Claude models, including Pro and Max" |
| GitHub Copilot | "AI models for coding assistance via GitHub Copilot" |
| OpenAI | "GPT models for fast, capable general AI tasks" |
| OpenRouter | "Access all supported models from one provider" |
| Vercel AI Gateway | "Unified access to AI models with smart routing" |
| Custom provider `[Custom]` | "Add an OpenAI-compatible provider by base URL." |

Below the card, a blue text link: **"Show more providers"**.

Observations:
- The descriptions describe *what the provider is for*, not how good its models are. Nobody is ranked.
  That is the line we drew when we deleted the registry, and OpenCode is on the right side of it here.
- The named providers are **presets over the same OpenAI-compatible mechanism** — the last row proves it:
  "Custom provider — add an OpenAI-compatible provider by base URL" is the generic case, and the five
  above it are that case with the URL pre-filled and a logo attached. This is exactly our
  `Provider` dropdown, but turned inside out: instead of asking the user to *know* that OpenRouter is
  reached by typing `https://openrouter.ai/api/v1` into an "Endpoint address" box, it names OpenRouter
  and fills the box.
- The Anthropic line — "including Pro and Max" — implies OAuth against a *subscription*, not an API key.
  We established (2026-08-15) that a Max subscription grants no API access; OpenCode appears to have a
  subscription-auth path we don't. Worth understanding before we copy any "Connect" wording, because
  ours can only ever mean "paste an API key."
- `+ Connect` is a **verb for a flow**, not a field. The card doesn't show a key box until you act.

### Screen 2 — Providers, connected state

Same page after one provider is live. Now two sections:

- **Connected providers** — its own card, containing one row: **Google**, badged `Environment`. No
  Connect button on this row (nothing visible where the button would be).
- **Popular providers** — a second card: **OpenCode Zen** `[Recommended]` "Curated models including Claude,
  GPT, Gemini and more"; **OpenCode Go** `[Recommended]` "Low cost subscription for everyone"; then
  Anthropic, GitHub Copilot, OpenAI… each still `+ Connect`.

Observations:
- **Connected float to the top, in their own section.** Answering "what is set up right now?" takes no
  reading — it's the first thing on the page. Our equivalent question is answered by a sentence
  (`KeyStatus`: "A key is stored for this provider") that only describes *the currently selected* provider,
  so you must click through the dropdown to learn the whole state.
- **"Popular providers"** re-labels the long list once a connected section exists. Progressive disclosure
  again, paired with "Show more providers" from screen 1.
- **The `Environment` badge — confirmed by Frank:** Google was **auto-detected from environment
  variables**. He never entered it in the UI. OpenCode is scanning the environment for known credential
  variables (`GOOGLE_API_KEY` / `GEMINI_API_KEY` or similar) and surfacing what it finds as
  already-connected. So the badge is **literal provenance**, not a category label.

  Two consequences, both worth carrying:

  1. **Credentials now have more than one source** — typed-in, environment, OAuth "Connect" flow — and
     the UI *names the source instead of hiding it*. That is a genuinely good idea and it is cheap. Our
     equivalent would be a badge on the key status line: `Keychain` vs `Environment` vs (later) whatever
     else. It makes "why does this work on my laptop but not my wife's?" a one-glance answer.
  2. **"Connected" becomes discovered state, not just stored state** — the list can populate itself on
     first run, with zero configuration.

  **The tension, recorded and NOT resolved:** silently adopting an ambient API key means the user may
  spend money through a credential they never knowingly handed to this app. An env var set for some other
  tool becomes CST Reader's billing relationship without a consent moment. Consent-on-first-use, or a
  discovered-but-not-active state, or explicit adoption — all plausible, none decided. Note also that our
  threat model differs from a coding agent's: OpenCode's user is a developer who put those vars in their
  own shell profile; ours may be a reader who has no idea what an environment variable is.
- **`Recommended` on OpenCode Zen and OpenCode Go is commercial placement, not a quality judgment.**
  Both are **OpenCode's own products** — Zen is their curated model gateway, Go is their subscription.
  This is *exactly* the distinction that got the model registry deleted in #681: we refused to publish a
  verdict on third-party model quality. OpenCode isn't publishing one either — it is promoting its own
  storefront, which is a different (and honest, if you know the branding) act. **The visual grammar is
  identical to a quality badge, though, and a user who doesn't recognize "OpenCode Zen" as first-party
  will read `Recommended` as "this one is better."** We have no first-party product to promote, so we have
  no legitimate use for this badge at all — and an illegitimate one would be the registry coming back
  through the UI door.

### Screen 3 — General

Not about providers, but the **row idiom** is the reusable thing:

One card, rows separated by hairline rules. Each row: **bold label** on the left, **grey description
sentence** beneath it, **control right-aligned and vertically centred**. Controls seen: dropdown
(Language → "English"; Terminal shell → "Auto (Default)") and toggle switches (Auto-accept permissions;
Show reasoning summaries; Expand shell tool parts; Expand edit tool parts) — all four toggles off. An
**Appearance** section heading follows below, so a page is a sequence of titled cards.

Observations:
- Label / description / control in **one row** vs. our stacked label → control → description → sometimes a
  second and third description paragraph. Theirs scans in a column; a user can run their eye down the
  right edge and see every current value. Ours cannot be scanned that way at all.
- Every row has a description. So do ours — but ours often have *three* (`KeyStatus`, then
  `ApiKeyDescription`, then "Stored securely in the operating system's credential store."). Worth a pass.
- "Show reasoning summaries" — same feature as our reasoning `Expander`, exposed as a preference. We
  currently always show it when present. Not obviously worth copying; noted for the record.
- Toggle vs. checkbox: they use toggles throughout. We use `CheckBox`. Cosmetic, but the toggle reads as
  "a setting that is on/off now" where a checkbox reads as "a thing you are selecting." Low priority.

---

## Batch 2 (2 screenshots, 2026-08-17) — Models

### Screen 4 — Models

Same chrome as batch 1: left nav (**Desktop**: General, Shortcuts · **Server**: Servers, Providers, Models),
"OpenCode Desktop / v1.18.18" bottom-left. **Models** is the selected nav item. Page title "Models".

Top of the content pane, full width: a single text box, placeholder **"Search models"**. No filter chips, no
sort control, no capability/modality selector, no "connected only" switch — one free-text box and nothing else.

Beneath it, the list is **grouped by provider**, each group a disclosure section:

- **`▸` OpenCode Zen** — collapsed. Nothing beneath it.
- **`▾` Google** — expanded, its contents in a rounded card.

Group headers are provider glyph + provider name, no model count, no description, no per-group action.
The two groups visible correspond to what batch 1 showed as reachable: Google (connected from the
environment) and OpenCode Zen (OpenCode's own gateway). Whether more groups sit below the fold is not
visible — the page clearly scrolls (Google's last row is cut off at the window edge).

Inside Google's card, rows separated by hairline rules. **Each row is: model display name, left-aligned —
and a toggle switch, right-aligned. That is the entire row.** Visible, in order:

| Model name as written | toggle |
|---|---|
| Deep Research Max Preview (Apr-21-2026) | off |
| Deep Research Preview (Apr-21-2026) | off |
| Gemini 2.5 Computer Use Preview 10-2025 | off |
| Gemini 2.5 Flash | off |
| Gemini 2.5 Flash Preview TTS | off |
| Gemini 2.5 Flash-Lite | off (row cut off by window edge) |

Six rows visible; the group obviously continues (Gemini 2.5 Pro etc. are not on screen). Every visible toggle
reads as off — knob left, grey track. **I never see a row in the on state, so everything below about what the
toggle means is inference from the control type, not something the screenshot proves.**

Observations, in order of what matters to #674:

- **Per-model metadata: there is none.** No context window, no pricing, no modality icon, no
  reasoning/tool-use flag, no release date field, no uptime, no badge of any kind. The only information beyond
  the bare name is what the *provider baked into the name string* — "Preview", "(Apr-21-2026)", "10-2025",
  "TTS", "Computer Use". So the provenance question ("provider-published or OpenCode-curated?") barely
  arises on this page: OpenCode displays the vendor's own display name and stops. That is the maximally
  conservative position and it is on the right side of our line — but it is also **less** than we could safely
  show. OpenRouter's `/api/v1/models` publishes `context_length`, `pricing`, `architecture.modality` and
  `supported_parameters` per model; HF publishes per-provider modality and latency. All of that is
  provider-published and adoptable. OpenCode simply doesn't bother. **We can exceed OpenCode here without
  going anywhere near the registry.**
- **Ordering is alphabetical by display name** within the group. No "recommended first", no popularity, no
  recency. Nobody is ranked. Good.
- **The list is not filtered by capability, and it shows.** A TTS model and a Computer-Use model sit in the
  same flat list as chat models, in an app that only does chat. At Google's ~30 models that is untidy; at
  OpenRouter's ~500, or HF's tens of thousands, it is the difference between a usable picker and a wall.
  Note that the safe fix is available: filtering on *provider-published* modality/`supported_parameters`
  ("text in, text out") is a mechanical capability filter, not a quality judgment.
- **The toggle is almost certainly enablement, not selection.** A per-row toggle has no single-selection
  semantics — nothing prevents several being on, and there is no radio, check, or "Active" marker anywhere.
  Read together with the fact that this is a *Settings* page (not a chat surface), the coherent reading is:
  **this page decides which models appear in the model picker used elsewhere, per session.** The actual
  "which model answers this message" choice is not on this screen and was not captured.
- **That shape is the most valuable thing in either batch for #674.** It replaces "rank 500 models for the
  user" — which we refuse to do — with "let the user mark the handful they care about, once." The curation
  is *the user's*, sourced from the provider's own list, and we publish no opinion at all. It is also the
  only mechanism seen so far that actually scales: search to find, toggle to keep, and thereafter you deal
  with your own short list rather than the catalogue.
- **Where a registry could sneak back in on this page: the default toggle state.** If any model ships toggled
  on, we have shipped a verdict. Default must be all-off (empty short list) or "whatever you last used".
- **And the subtler one: "OpenCode Zen" is a curated model list wearing a provider's clothes.** Batch 1
  flagged Zen's `Recommended` badge; here Zen is a *group in the model list*, indistinguishable in grammar
  from Google. The curation isn't labelled as curation — it's laundered into the provider axis. The
  equivalent move for us would be a "CST Reader recommended" pseudo-provider whose contents we maintain.
  That is the deleted registry with a different noun on it. Hard no, and worth naming explicitly because
  it would not *look* like a registry in the UI.
- No per-model affordance other than the toggle — no gear, no chevron, no click-through. So per-model
  configuration (temperature, reasoning effort, #671) does **not** live here.

### Screen 5 — Connect OpenRouter

A **full-pane modal sheet** (it covers the whole settings window; batch 1's nav and page content are gone).
Chrome: a **back arrow** top-left and an **✕** top-right — so the Connect flow is a stack with its own
navigation, not an inline expansion of the provider row.

Contents, top to bottom, all left-aligned in a wide column:

- Small monochrome key glyph + heading **"Connect OpenRouter"**.
- Body paragraph, grey: **"Enter your OpenRouter API key to connect your account and use OpenRouter models
  in OpenCode."**
- Field label **"OpenRouter API key"** (bold), then a full-width single-line text box, placeholder
  **"API key"**. The box is empty, so I cannot tell whether input is masked.
- A dark filled primary button: **"Continue"**.

The rest of the sheet is empty. Nothing else at all: no base URL field, no display-name field, no
"Test connection", no link out to where you get a key, no OAuth alternative, no "paste from clipboard",
no cancel button beyond the ✕/back arrow.

Observations:

- **This confirms batch 1's central inference: named providers are presets.** Connecting OpenRouter asks for
  *one* thing — the key. The base URL is knowledge the app already has. Compare ours, where reaching
  OpenRouter requires the reader to know and type `https://openrouter.ai/api/v1` into a box labelled
  "Endpoint address". The Custom-provider flow presumably asks for a URL too, but that screen was not
  captured.
- **"Continue", not "Save" or "Connect".** A verb that promises a next step. The most likely next step is
  validate-the-key-and-fetch-the-model-list — which would also explain how the Models page comes to know
  Google's catalogue. Not proven; the following screen wasn't captured.
- **The wording is worth stealing almost verbatim, minus the product name.** "Enter your ⟨provider⟩ API key
  to connect your account and use ⟨provider⟩ models in CST Reader" is one sentence that says what the key is
  for, whose account it bills, and what it unlocks. Our current three stacked description paragraphs under
  the API key field say less.
- The batch-1 caution stands and is now sharper: for OpenRouter, **"Connect" means exactly "paste a key"** —
  which is all *we* can ever mean. So the objection to the word was narrower than it looked; it applies to
  the Anthropic row ("including Pro and Max", implying subscription OAuth), not to the API-key case. If we
  only ever do keys, "Connect" is honest for us too, as long as the sheet says "Enter your API key".
- Note the screenshot's filename places this modal in the **Models** context, not Providers. Either the
  Models page offers a route to connect an unconnected provider, or the user simply navigated from
  Providers and the filename records where they'd been. Unresolved.

### What this means for our code, concretely

- `ChatSettings` (`Models/Settings.cs:76-109`) is **scalar**: one `Provider`, one `BaseUrl`, one `Model`.
  Everything OpenCode does here presumes the plural: a *set* of connections, each with its own key, each
  contributing a *set* of models, plus a short list drawn across them. #678 is therefore not "add a second
  key slot" — it is `ChatSettings.Connections[]`, with the scalar fields becoming the selected connection.
- `AiCredentialStore.AccountFor(ChatProviderKind)` (`Credentials/AiCredentialStore.cs:111-117`) keys the
  keychain by the two-member **wire-format** enum. OpenCode keys credentials by **provider identity**
  (OpenRouter's key is OpenRouter's). Same fix, stated in their terms: the account must be a stable
  per-connection id — the preset id for a named provider, a slug of the base URL for a custom one — never
  the wire format. `ChatProviderKind` stays what it correctly is: which HTTP shape to speak.
- The dropdown to replace is the static `Providers` array in **`ViewModels/SettingsViewModel.cs:1418-1422`**
  (not in `Settings.cs`, despite the name) — two `AiProviderChoice` entries, "OpenAI-compatible endpoint"
  and "Claude (Anthropic)". That array is a list of *wire formats offered as if they were providers*, which
  is exactly the conflation the OpenCode screens don't make.
- `Model` is a free-text `TextBox` with a comment (`SettingsViewModel.cs:1493-1497`) explaining it is
  deliberately never a dropdown, because a shipped list would be wrong within a month. **That reasoning
  survives batch 2 intact** — and OpenCode shows the way out of it that doesn't violate it: the list is
  fetched live from the connected provider, not shipped. Free text must remain as the escape hatch for
  endpoints that publish no list (a local runner), but it need not be the *only* path.

---

## Batch 3 (2 screenshots, 2026-08-17) — Custom provider

The two images are the **same modal sheet, scrolled**: screen 6 is the top, screen 7 picks up at the API key
field (which appears in both) and runs to the bottom. They overlap, so between them the form is covered
end to end — nothing is hidden between the two.

### Screen 6 — Custom provider, top of the sheet

Same full-pane modal chrome as Connect OpenRouter: **back arrow** top-left, **✕** top-right.

- Sparkle glyph + heading **"Custom provider"**.
- Grey body line: **"Configure an OpenAI-compatible provider. See the provider config docs."** —
  "provider config docs" is an underlined link out.
- **Provider ID** — text box, placeholder `myprovider`. It carries the **focus ring** (blue outline), so it
  is the first field. Helper line beneath, in near-black rather than grey:
  **"Lowercase letters, numbers, hyphens, or underscores"**.
- **Display name** — text box, placeholder `My AI Provider`. No helper text.
- **Base URL** — text box, placeholder `https://api.myprovider.com/v1`. No helper text.
- **API key** — text box, placeholder `API key`. Helper: **"Optional. Leave empty if you manage auth via
  headers."** The box is empty so I cannot tell whether input is masked.

### Screen 7 — Custom provider, rest of the sheet

- **API key** again (the overlap), with the same "Optional…" helper.
- **Models** — a **repeating row**: a `model-id` box and a `Display Name` box side by side, with a **trash
  icon** at the far right of the row. Below it, a **`+ Add model`** link. One empty row is shown.
- **Headers (optional)** — same repeating-row idiom: a `Header-Name` box and a `value` box, trash icon,
  and **`+ Add header`**. One empty row shown.
- A dark filled primary button: **"Submit"**.

No "Test connection", no "Fetch models", no wire-format selector, no cancel button beyond ✕/back.

### What this answers

1. **What a connection consists of, in their model.** Five parts:
   `Provider ID` · `Display name` · `Base URL` · optional `API key` · optional `Headers[]`
   — plus an explicit, hand-entered **`Models[] = (id, display name)`**. Only **API key** and **Headers**
   are marked Optional; by contrast the other four read as required, though nothing says so in words
   (**inference**, and the Models list in particular might accept zero rows — unverified).
2. **How a custom endpoint's models get populated: they don't. The user types them.** There is no
   `/v1/models` discovery, no fetch button, no "load models from endpoint" affordance anywhere on the
   sheet — and the presence of a hand-typed **display name per model** puts it beyond doubt that this is
   manual entry, not a fallback for when discovery fails. **So the live catalogue that makes the Models page
   work for Google and OpenRouter does not extend to arbitrary endpoints at all.**

   This is the most important finding in batch 3, and it is a *negative* result: OpenCode has two entirely
   different model-acquisition paths — known provider ⇒ catalogue we already have; unknown provider ⇒ you
   tell us. It never calls the endpoint to ask.

   Worth stating the likely reason, flagged as **inference**: OpenCode's known-provider catalogue almost
   certainly comes from an aggregated model-metadata catalogue they maintain (models.dev is theirs), not
   from live `/v1/models` calls at all. If so, the batch-2 reading needs a correction — *"fetched live from
   the connected provider"* may be wrong, and the truth may be *"read from a catalogue OpenCode maintains."*
   **That is a maintained table, and we should look at it with the #670/#681 rule in hand** — see the
   NOT-list, item 10.
3. **Identity: a user-supplied slug, separate from the label.** `Provider ID` (`myprovider`, lowercase +
   digits + `-` + `_`) is the machine identity; `Display name` (`My AI Provider`) is the human one. They are
   deliberately two fields. Uniqueness isn't stated on screen, but an "ID" with a character-class constraint
   and a separate label field means nothing else.
4. **Two custom endpoints coexist**, distinguished by their IDs and shown by their display names. The design
   is plainly plural — nothing about the sheet is "the custom provider", it is "a custom provider".

### What it changes for #678 and #674

- **Their `Provider ID` beats our sketched base-URL slug, and it isn't close.** A slug derived from the URL
  changes when the URL changes: move Ollama from `:11434` to another port, or swap a gateway hostname, and
  the keychain account silently changes with it — the key "disappears" and the user re-pastes. A stable
  user-supplied ID survives every one of those edits. It is also legible in `security find-generic-password`
  output and in a settings JSON diff, where a hash is not. **Take the two-part identity: `Id` (slug, stable,
  the keychain account) + `DisplayName` (free text, cosmetic, renameable).** Presets get a reserved id
  (`openrouter`, `anthropic`); custom connections get one the user picks, defaulted from the host.
- **`Headers[]` is the escape hatch we currently lack.** "Optional. Leave empty if you manage auth via
  headers" covers Azure's `api-key`, gateway tokens, and anything else that isn't a bearer key — and it
  makes "API key optional" coherent rather than odd. Our resolver already treats a missing key as legitimate
  (`ChatProviderResolver`'s local-runner case), so this fits what we have.
- **A `Models[]` list per connection, typed by hand, is exactly the shape of our current free-text Model
  box — only plural and named.** That is a strictly better version of what we ship: instead of one model id
  in a `TextBox`, the user keeps a small named list per endpoint. It requires no catalogue, no network call,
  no registry, and it works on day one for a local Ollama. **This is the floor of the #674 design**, and
  live-catalogue search (batch 2's toggle page) becomes the *upgrade* for providers that publish one — not
  a prerequisite.
- Consequence for `ChatSettings`: the connection record is now well specified —
  `{ Id, DisplayName, Kind (wire format), BaseUrl, HasKey, Headers[], Models[] }` — with the key itself in
  the keychain under `Id`, never in settings JSON. Note `Kind` is *ours*, not theirs: OpenCode's custom
  sheet is OpenAI-compatible only, but we support the Anthropic shape too, so we need the field they don't.
- **Nothing on this sheet ranks, scores, or recommends anything.** The only editorial content is the user's
  own display names. Clean.

---

## Batch 4 (1 screenshot, 2026-08-17) — Ollama connected

### Screen 8 — Providers, with a custom connection live

The Providers page again (nav item highlighted), now with **two** rows under **Connected providers**, in one
card separated by a hairline:

| | Row | Badge | Right-aligned action |
|---|---|---|---|
| ✦ | **Google** | `Environment` | *(nothing — the space is empty)* |
| ✧ | **Local Ollama** | `Custom` | **Disconnect** |

Below, **Popular providers**: OpenCode Zen `Recommended` "Curated models including Claude, GPT, Gemini and
more" · OpenCode Go `Recommended` "Low cost subscription for everyone" · Anthropic "Direct access to Claude
models, including Pro and Max" · GitHub Copilot (cut off at the window edge, its `+ Connect` half-visible).
Each of those has a bordered **`+ Connect`** button. Google is **no longer** in this list — connecting
removed it, confirming connected-first is a move, not a copy.

Answers to the four questions:

1. **A custom connection renders as a first-class peer of a preset**, in the same Connected card, same row
   grammar. It shows the **Display name only** — "Local Ollama". The **Provider ID is not shown. The base
   URL is not shown.** No host, no port, nothing about where it points.
2. **Actions differ by row, and by provenance.** Local Ollama offers **Disconnect**, as plain text with no
   button chrome — deliberately quieter than the bordered `+ Connect` on unconnected rows. **Google offers
   nothing at all**: the action slot is empty. That asymmetry is coherent and worth naming — an
   environment-sourced credential *cannot* be disconnected from inside the app, because the env var is the
   source of truth and the fix is in your shell profile. Provenance therefore determines the available
   actions, not just the label. **No Edit affordance is visible** on either row; whether the row itself is
   clickable is not something a screenshot can tell me.
3. **The models the user typed are neither shown nor counted here.** No "3 models", no expansion. Providers
   and Models stay strictly separate pages, even where the model list is data the user hand-entered on the
   provider form.
4. **Yes, the two credential sources are visually distinguished — by badge, and only by badge.** `Environment`
   vs `Custom`, identical grey pills in identical positions. **This confirms the batch-1 provenance-badge
   idea with two sources actually on screen at once, and it works: the whole state is legible in one glance.**
   One honest caveat: `Custom` is really a *kind* of connection (it came from the Custom-provider form)
   rather than a credential *source* the way `Environment` is. OpenCode is overloading one badge slot for
   both axes. It reads fine here because the two happen not to collide, but a preset connected by pasting a
   key presumably gets no badge at all — so the scheme is "badge the unusual case", not a systematic
   provenance label. Ours should pick one axis and be consistent.

### Reachability — the part that matters most

> **⚠ CORRECTED 2026-08-17, after the user tested the live app.** The paragraph immediately below was
> written from the screenshot alone and its headline claim — that OpenCode performs *no* reachability check —
> **is wrong**. The check exists; it lives at *use* time, not on this page. The framing error was in the
> question (it asked only about the settings screen), not in the reading of the image. The original text is
> kept verbatim, struck where it is false, and the corrected finding follows in **"What actually
> happens"** below. The corrected version is a *stronger* argument for our three-state proposal, not a
> weaker one — see there.

~~**There is no reachability indication of any kind.** No status dot, no "reachable"/"unreachable", no
last-used timestamp, no error state, no retry.~~ **(False as stated — see the correction below. What is
true, and all the screenshot could show: there is no reachability indication *on the Providers page*.)**
Local Ollama sits under a heading that says **Connected**
whether or not `ollama serve` is running — indeed whether or not anything has ever listened on that port.
"Connected" here means **a configuration record exists**, nothing more. **(This part stands, and is in
fact the whole problem.)**

### What actually happens (user test, live app, 2026-08-17)

Frank quit Ollama and then tried to use it. Observed:

- The failure surfaces **at use time**, in the chat surface: **"Cannot connect to API — Retrying in 7s"**.
- **Exponential backoff, capped around 10s, roughly 6 attempts — about 30s total** before giving up.
- Final state: a bare **"Cannot connect to API"**. **No URL, no port, no cause, no suggestion.**
- **Throughout all of it, the Providers settings page still lists Local Ollama as Connected.**

So the corrected finding is not "OpenCode does no reachability check". It is:

> **The reachability check exists and lives only at use time, while the settings surface goes on reporting
> stale configured-state as "Connected". The two surfaces disagree simultaneously.**

**That is a sharper argument for the three-state proposal than the original claim was**, because it makes
the failure mode concrete rather than hypothetical. Play it out: the reader asks a question, gets "Cannot
connect to API", and does the sensible thing — **goes to Settings to find out what is wrong. Settings is the
surface that lies.** It says Connected. The one screen a user consults to diagnose the problem is the one
screen guaranteed to be stale, and it contradicts the error they just read. The reader is now worse off than
if there were no status at all, because they have been actively told the configuration is fine.

The lesson generalises past this app: **a status word that is computed from stored configuration rather than
from contact will eventually contradict the thing the user is looking at it to explain.** Either the status
reflects contact, or it must not use a word like "Connected" — "Configured" is honest and costs nothing.

For OpenCode the *runtime* half is survivable: its user configured the endpoint, knows what a daemon is, and
will guess. **For our reader it is not**, on both halves. A configured-but-dead local endpoint is the single
most likely failure our AI settings will produce, and a screen that says "Connected" in that state actively
misleads — it sends the reader looking for the problem everywhere except the one place it is.

Two design consequences, both cheap, both worth carrying:

1. **Retry policy must distinguish failure kinds. (#673.)** ~30s of exponential backoff is exactly right for
   a flaky remote, a 429, or a 503 — and exactly wrong for **connection-refused on loopback**, which is not
   transient and is knowable on the *first* attempt. Retrying it six times spends 30 seconds of the reader's
   attention proving something the OS already said definitively. Classify: refused/DNS-failure/no-route ⇒
   fail fast with a diagnosis; timeout/5xx/429 ⇒ back off and retry.
2. **Name the endpoint in the error.** We have the base URL in configuration; there is no cost to putting it
   in the message. **"No response from `http://localhost:11434/v1` — is the server running?"** versus
   **"Cannot connect to API"** is the difference between a reader diagnosing it in five seconds and a reader
   filing a bug. (Same rule as the settings-page copy above — the endpoint is the fact that makes the
   sentence actionable.) Do redact any credential from the URL if one is ever embedded in it.

So this is a place to **diverge deliberately**, and we already have the better instinct: `ReadinessText`
asks the real resolver rather than restating settings. Extending that to a connection list means three
states, not two — **configured / reachable / unreachable** — with the honest default being *configured, not
yet checked* rather than a green light we haven't earned. A cheap probe on opening the settings page (a
`GET {baseUrl}/models`, or any request whose *transport* success is the signal) distinguishes them without
spending a token or needing a valid key. The failure copy is the valuable part: "No response from
`http://localhost:11434/v1` — is the server running?" is a sentence that ends the search, and it is exactly
the sentence OpenCode declines to write.

Note this cuts the other way too: an *unreachable* label must not be sticky or alarming for a laptop that
is simply offline right now. Configured-and-unchecked is a legitimate resting state; the point is not to
call it Connected.

And one more thing the live test adds: **a failure at use time should write back to the settings state.** If
a request just got connection-refused, the settings page has been *told* the endpoint is unreachable — it
should not need its own probe to say so, and it certainly should not go on saying Connected. One shared
last-known-contact fact, read by both surfaces, is what keeps them from contradicting each other. That is
the concrete fix for the disagreement observed above, and it is cheaper than either surface checking alone.

---

## Batch 5 (1 screenshot, 2026-08-17) — the per-turn picker

The chat surface, not settings. **Closes open question 8.**

### Screen 9 — composer chip + model popup

Bottom of the window, the composer: a rounded input with placeholder "Ask…" (mostly covered), a **`+`**
button at bottom-left, then a grey rounded **chip: sparkle glyph + "Gemma4 12B MLX" + a `⌄` chevron**, and a
dark send button (↑) at bottom-right. **The chip is the per-turn model control — the current model is
readable without opening anything.**

The chip is open, and its popup floats **above** it, anchored to it, about 570px wide:

- **Pinned at the top: a search field** — magnifier icon, placeholder **"Search models"**, text caret
  visible, so it takes focus on open.
- **A scrolling list beneath it**, clipped by the search box (the first row, "Gemini 3.5 Live Translate
  Preview", is cut off mid-glyph at the top edge — proof the list scrolls *under* a pinned header).
- Rows visible, in order: Gemini 3.5 Live Translate Preview (clipped) · Gemini 3.7 Flash · Gemini Omni Flash
  Preview · Gemma 4 26B A4B IT · Lyria 3 Clip Preview · Veo 3.1 lite — all belonging to a Google group whose
  header has scrolled out of view. Then a **grey group header "Local Ollama"**, then **"Gemma4 12B MLX" in
  blue with a blue ✓ at the right**, on a lightly shaded row.
- **Pinned at the bottom: "Manage models"** with a sliders icon.
- Bare names again — **no metadata of any kind** on any row, consistent with batches 2 and 4.
- Alphabetical within group, consistent with batch 2. (Batch 2 showed the Gemini *2.5* family; this shows
  3.5/3.7/Omni — same alphabetical list, further down. Not a discrepancy.)
- Behind the popup, top-left, the transcript reads **"I am gemma4:12b-mlx."**, with the user's **"hi"** in a
  bubble at the right — so the selection genuinely took effect. Partly hidden behind the popup is a
  red-bar-prefixed line beginning "Car…" — the leftover **"Cannot connect to API"** from the batch-4 test.

### What it settles

- **The per-turn choice is a chip on the composer, not a settings field.** It shows the current model at
  rest, opens a searchable grouped list, marks the current one, and offers one escape hatch back to
  settings. This is the whole of question 8, answered.
- **The display-name / id split from batch 3 is visible working end to end.** The user typed
  `gemma4:12b-mlx` as the model-id and "Gemma4 12B MLX" as the Display Name on the Custom-provider form
  (batch 3); the chip and the picker show the **name**; the wire carried the **id**, which is how the model
  can answer "I am gemma4:12b-mlx." That is direct confirmation that `Models[] = (id, displayName)` is the
  right record shape — the id is for the API, the name is for the human, and they are different strings.
- **Search is pinned, not inline.** At Ollama-scale it is unnecessary; the fact that it is present anyway,
  focused on open, and pinned above a scrolling region is the design conceding that this list can be long.
- **Local Ollama's group contains exactly the one model the user typed.** A custom endpoint's group is its
  hand-entered list, nothing more — consistent with batch 3's finding that there is no `/v1/models`
  discovery.

### ⚠ Correction 1 — recorded, not silently fixed

> The coordinator briefly read this screenshot as **undercutting batch 2's "user-built short list"**
> conclusion, on the grounds that the list contains **Lyria 3 Clip Preview** (music generation) and
> **Veo 3.1 lite** (video) — models nobody would deliberately enable for a chat picker. **The maintainer,
> who has the app in hand, confirms the picker lists the *enabled* models under each provider heading.**
> So batch 2's reading was right and the doubt was wrong. **Resolved in favour of batch 2.**

The evidence that misled is worth keeping, because it will recur: **a list containing obvious junk cannot
distinguish "there is no curation mechanism" from "there is a curation mechanism with a permissive
default."** Lyria and Veo are in the picker not because nothing filters it, but because a connected provider
starts **all-on** and the user prunes. The inference "junk present ⇒ unfiltered" silently assumed the default
was all-off. When reading someone else's UI, the *default state* of a control is exactly the thing a single
screenshot cannot show — and it is usually the thing that determines what the mechanism means.

### ⚠ Correction 2 — NOT-list item 6 was wrong; revised in place

> **⚠ SUPERSEDED 2026-08-18 by the source finding below** ("where the pre-enabled subset comes from"). The
> rule stated here — that subsets are the verdict and all-on/all-off are both neutral — **stands and is
> unchanged**. What is wrong below is the factual claim that OpenCode defaults a connected provider to
> all-on. It does not: it enables the newest model per family released in the last six months, which is a
> pre-enabled subset, i.e. the very thing this correction rules out. The Veo/Lyria evidence was consistent
> with all-on but did not establish it.

Batch 2's NOT-list said "Any model toggled on by default… **Default must be all-off**." **That is wrong as
written**, and it is now revised in place (see NOT-list 6). The correct rule is about **subsets**, not about
the on-state:

- **all-on is neutral** — "here is what this provider offers."
- **all-off is neutral** — "choose what you want."
- **a pre-selected subset is the verdict**, because somebody chose *which ones*. That is the registry.

The Veo/Lyria presence indicates OpenCode defaults a connected provider to **all-on**, with the user pruning
— which is fine by the corrected rule.

And the usability argument runs the same way: **all-off means connecting OpenRouter yields an empty picker
with 500 invisible models hidden behind a search box** — a reader who doesn't already know a model id by
heart is stuck at a blank list. **So all-on is the better default for us**, with the mechanical
modality/`supported_parameters` filter (steal 9) doing the obvious pruning.

One clarification so the two rules don't look contradictory: **a filter defined by a provider-published
field is not a hand-picked subset.** It is a rule, stated in the UI, reversible by the user, and it removes
models that cannot answer a question at all — no judgment about which of the *remaining* ones is better.
A hand-picked "here are the good ones" list is the thing we refuse.

**And note the pairing this screenshot demonstrates:** an all-on default with **no** capability filter
produces a chat picker offering a music model and a video model. All-on and the mechanical modality filter
are not independent choices — all-on without the filter gives you exactly the list in this screenshot, and
the filter without all-on gives you an empty one. **Take both, or neither.**

---

## Live-use finding (no screenshot, 2026-08-17) — the env-var connection has no exit

From the maintainer, using the app rather than reading a screenshot of it:

> OpenCode automatically connects through my Google env vars — I forgot that I had them set on this
> machine — and **there is no way to disconnect it as a provider.** You could toggle off all of its models
> and I assume then it wouldn't show in the Provider/Model picker, but I haven't tested that. Bottom line:
> OpenCode takes an opinionated stance on "consent". If it has valid env vars, it connects.

Two things follow, and they land on two different running-list items.

### 1. The empty action slot is a dead end, not a virtue (corrects steal 17)

Batch 4 read Google's empty action slot as principled: the app cannot remove a credential it never stored,
so it honestly offers nothing. **That reading was half right and operationally wrong.** Honest about the
*why*, yes — but the user is left with a provider connected, billable, and unremovable from inside the app.
The only escape anyone can see is toggling off each of its models one at a time, on a page meant for
managing a short list, and that workaround is **assumed, not verified**.

The correction is recorded in steal 17: **split the one missing control into two.** *Disconnect* stays
absent (nothing to revoke). *"Don't use this credential"* — a local suppression flag keyed to the connection
id — is entirely ours to offer. Declining to **use** a credential is always within an app's power even when
deleting it is not.

**And note who this happened to: the person building this software, on his own machine, surprised by which
credentials were live.** If the author of an AI-integrated app can lose track of which ambient variables are
spending money, a reader of a Pāli text app has no chance whatsoever. That is the whole argument, in one
data point.

### 2. The batch-1 consent tension is now resolved — against OpenCode's stance (resolves NOT-list 3)

Batch 1 recorded the auto-adoption question as "recorded, not resolved". It is resolved now.

"Valid env var ⇒ connected" is a defensible stance **for a developer tool** whose user exported that
variable that morning and knows what it is for. It is the wrong stance for CST Reader: our readers mostly
did not set those variables, will not recognise the names, and the downside is **spending their money**
through a billing relationship they never knowingly gave us.

**Our stance: discover, but do not auto-connect.** A found credential is shown as **available**, not
*connected*, with a Connect action next to it. Consent becomes an act rather than an inference. The
`Environment` provenance badge from batch 1 survives untouched — it simply labels a connection the reader
chose, whose key happens to come from the environment rather than the keychain.

**Caveat, so this is not over-applied.** None of the above touches the **`CST_AI_*` namespaced
environment-variable design under consideration for beta 6.** Setting a variable that names *our own
application* **is** the consenting act — that is what the namespace is for. The hazard here is narrow and
specific: **adopting variables that were set for somebody else's tool.** `GOOGLE_API_KEY` was not a message
to us; `CST_AI_API_KEY` is.

---

## Source finding (2026-08-18) — where the pre-enabled subset comes from

Read from the source rather than the screen, at `anomalyco/opencode@0033bb3`. This **closes open question 1's
tail and corrects Correction 2**, and it is the first case in this study where the falsified thing is a rule
we had already built policy on.

### What the maintainer observed

Connecting OpenRouter with a real key produced a toast — *"OpenRouter connected. Select models."* — and a
Models list of a couple of hundred entries under an OpenRouter group, of which **roughly 30 to 40 were on and
the rest off**.

That is neither of the two defaults this document had argued about. Batch 2's screenshot suggested all-off;
Correction 2 reversed it to all-on from the Veo/Lyria evidence. The truth is a **pre-enabled subset**, which
is the one outcome #689 names as forbidden — and which neither a screenshot nor a single live session could
have explained, because the question is not what the toggles show but what computes them.

### The rule, from source

`packages/app/src/context/models.tsx`:

```js
const visible = (model) => {
  const state = visibility().get(key)
  if (state === "hide") return false      // the user turned it off
  if (state === "show") return true       // the user turned it on
  if (latestSet().has(key)) return true   // otherwise: is it "latest"?
  const date = release().get(key)
  if (!date?.isValid) return true         // no release date at all -> on
  return false                            // dated, and not latest -> off
}
```

and `latest` is, in the same file: take every available model, **keep only those released within the last six
months**, group by provider, group again by `family`, and keep **the newest member of each family**.

So the default-on set is *the newest model in each family, per provider, released in the last six months*,
plus anything whose `release_date` is missing or unparseable. `family` and `release_date` come from
models.dev. There is no hand-written list of model ids anywhere in it.

### Why this matters to us, precisely

**It is mechanical in implementation and a verdict in substance.** Nobody typed a list of good models; a rule
computed one. But the rule encodes "newer is what you want", and #689 rejects exactly that in words:

> even "newest first" editorializes, since a newer point release can be worse at Pāli.

For a coding agent the assumption is defensible — capability there does track recency fairly well. For Pāli it
is the assumption the model registry was deleted over (#670/#681), and adopting it because it arrived as
`release_date` arithmetic rather than as a curated table would be the registry returning through the back
door. **A mechanical computation of an editorial judgment is still the editorial judgment.**

Note also what this does *not* settle: it is a recency rule, **not** the capability filter this document
speculated about under steal 9. There is no modality or `supported_parameters` gate on the default at all —
which is why a music model and a video model reached the picker in batch 5. The capability filter remains a
good idea that upstream has not had.

### The second finding: "Popular" is a literal list

`packages/app/src/hooks/use-providers.ts:8`:

```js
export const popularProviders = [
  "opencode", "opencode-go", "anthropic", "github-copilot",
  "openai", "google", "openrouter", "vercel",
]
```

Eight entries, hand-written, **their own two products first**. It is used three times: to sort provider
groups in Settings → Models, to sort the groups in the composer's model picker, and as the *"Popular
providers"* section on the Providers page.

So batch 1's suspicion about OpenCode Zen's `Recommended` badge was right and understated. The curation is not
a badge on one row — it is the ordering of every provider surface in the app, and it is a constant in a hooks
file. Nothing in the UI says a choice was made.

### Consequences for the ongoing sync

The plan is to track upstream provider facts and fold in changes. This finding sharpens the do-not-take list,
which now has two tiers:

- **Never take as an input to any default or ordering:** `release_date`, `family`, `popularProviders` or any
  successor, pricing, `status`, vendor `description`. Displaying `release_date` verbatim next to a model is
  fine; letting it decide what is enabled is not.
- **Take freely:** base URLs, env var names, auth shapes, wire protocol, prompts/inputs a provider needs.

The general rule this produces, worth keeping in front of whoever does the next sync: **pull named fields,
never "whatever the source says", and audit any field that reaches a default rather than a display.** A
recency rule is easy to re-adopt by accident precisely because it looks like arithmetic rather than an
opinion.

### Consequence for our own defaults (#692, #674)

The two cases separate, and conflating them is what made this argument go round twice:

- **A hand-typed model list — what #691/#692 ship.** Typing a model id *is* the act of selecting it. All-on
  is right, and all-off would mean typing a model and then hunting for a switch before it appears anywhere.
- **A fetched catalogue — #674.** Hundreds of models nobody asked for. All-off is right there, and the
  maintainer's instinct on seeing OpenRouter's list agrees. OpenCode's toast (*"Select models"*) reads as if
  they had made that choice too; their code did something else.

Neither case may use a pre-enabled subset, however it is computed.

---

## Method note — what screenshots could not show

Worth stating plainly for whoever reads these notes later, because the pattern repeated with unusual
consistency. **Three separate times in this study, a design detail that read as good (or as settled) in a
screenshot was falsified by someone actually using the app:**

1. **Batch 4 — "no reachability check."** The screenshot showed a Providers page with no status of any kind,
   and the note concluded OpenCode simply doesn't check. It does; the check lives at *use* time. The real —
   and worse — finding was that two surfaces disagree simultaneously, which no single screen could reveal.
2. **Batch 5 — the short-list doubt.** A picker containing a music model and a video model looked like proof
   that nothing curates the list. It was actually a curation mechanism with an all-on default. **A screenshot
   shows a control's current state; it cannot show its default state**, and the default is usually what
   determines the mechanism's meaning.
3. **The finding above — the empty action slot.** It photographed as principled restraint. In use it is a
   dead end with no exit.
4. **(added 2026-08-18) The pre-enabled subset.** Two screenshots and a live session produced three different
   answers about the default toggle state — off, then all-on, then "about forty of two hundred". Only the
   source settled it, and the answer was a *rule*, which is a thing no amount of looking at the UI can show.
   The lesson extends the one below: use gives you behaviour, but where behaviour is computed from data you
   cannot see, **the source is the only witness** — and it is worth reading before building policy on an
   inference about it.

The generalisation: **screenshots show structure, wording, hierarchy and affordances — the happy path, at
rest. They do not show what happens when something goes wrong, or when the user changes their mind.**
Failure states, defaults, and reversibility are precisely the three things a still image hides, and all
three are where the interesting design decisions live. Read screenshots for layout and language; get
someone to use the thing before concluding anything about behaviour.

A corollary for our own work: **the reversibility question — "how does the user undo this?" — deserves to be
asked explicitly of every affordance we design**, because it is the one that never shows up in a mockup and
it is where two of the three errors above came from.

---

## Ideas worth stealing

Cumulative. Ordered by value-to-effort as currently judged.

1. **Named provider presets over the same OpenAI-compatible mechanism.** Replace the 2-item
   Provider dropdown with a list: OpenRouter, DeepSeek, Together, Groq, Ollama (local), LM Studio (local),
   Claude (Anthropic), and "Custom — OpenAI-compatible endpoint" as the escape hatch. A preset supplies
   the base URL and the key-required flag; it supplies **no model list and no quality claim**. This is
   pure ergonomics — today a reader must already know the URL to use OpenRouter at all — and it
   reintroduces nothing we deleted, because the registry was about *models*, not endpoints.
   ⚠ It does create a maintenance surface (base URLs drift), so keep the list short, keep Custom
   first-class, and never let a preset be *required* to reach a provider.
2. **Name the credential's source in the UI.** A small badge/word on the key status line —
   `Keychain`, `Environment`, later `OAuth`. Cheap, honest, and it makes a whole class of "works on one
   machine, not the other" confusion self-diagnosing. Take this even if we never do env discovery.
   **Confirmed by batch 4** with two sources on screen at once (`Environment` on Google, `Custom` on Local
   Ollama): the entire configuration state reads in one glance. One correction from that screen — OpenCode
   overloads the badge slot with two different axes (credential *source* and connection *kind*) and badges
   only the unusual case. Pick one axis and apply it to every row, including the ordinary one.
3. **Connected-first ordering.** Whatever the provider UI becomes, show what is configured *now* at the
   top, before the catalogue of what could be. Directly fixes the fact that our current screen can only
   report on the one provider the dropdown happens to be showing.
4. **Providers and Models as separate concerns** — for us probably two *sections*, not two windows.
   Choosing an endpoint and choosing a model are different decisions with different cardinality (a dozen
   vs. tens of thousands), and #674 is only tractable if the model picker gets a surface of its own
   instead of being a 480px `TextBox` wedged between the endpoint and the API key.
5. **The label / description / right-aligned control row.** A general settings idiom worth adopting
   across the whole Settings window, not just AI. Makes current values scannable down the right edge.
6. **Progressive disclosure for the long tail** — "Show more providers". Our version is more likely
   "Show more presets"; the real long-tail problem for us is models (#674), where this device alone
   won't be enough (you cannot "show more" your way through 90K HF models — that needs search).
7. **A user-built short list of models, via a per-model toggle. (batch 2 — the best idea in either batch,
   and the answer to #674.)** The catalogue is fetched live from each connected provider; the user searches
   it and toggles on the handful they actually use; everywhere else in the app they pick from *their* short
   list, not from 500 rows. We publish no ranking, ship no list, and maintain no table — the curation is the
   user's own and the raw material is the provider's. Requirements it implies: default state all-off (see
   the NOT-list), search that filters *across* provider groups, groups collapsed by default with a count in
   the header, and a virtualized list so 500 rows don't build 500 controls.
8. **Group the model list by provider, collapsible, alphabetical within group. (batch 2.)** Structural, not
   editorial. Add the model count to the header, which OpenCode omits and which is exactly the number a user
   needs to decide whether to expand.
9. **A capability filter built only from provider-published fields. (batch 2.)** OpenCode's list mixes TTS
   and Computer-Use models into a chat picker. Filtering to text-in/text-out on OpenRouter's
   `architecture.modality` / `supported_parameters` is mechanical and vendor-sourced — it removes models
   that literally cannot answer a question, which is not a quality judgment. Show the filter, and let it be
   turned off.
10. **Per-model provider-published metadata, which OpenCode does not show at all. (batch 2.)** Context
    length, price per million tokens, modality, whether reasoning is a supported parameter. All published by
    OpenRouter/HF per model, all safe by the rule (provider-published is fine; a table we maintain is not),
    and all genuinely useful for a reader choosing between two unfamiliar names. Display verbatim, attribute
    to the provider, never re-rank or re-score it.
11. **The Connect sheet's one-sentence framing. (batch 2.)** "Enter your ⟨provider⟩ API key to connect your
    account and use ⟨provider⟩ models in CST Reader." One sentence covering what the key is, whose account
    pays, and what it unlocks — replacing our three stacked paragraphs. Take the sentence; the *word*
    "Connect" is fine too for a key-only flow (batch 2 revised the batch-1 objection: see NOT-list #4).
12. **A connect flow as a modal sheet with back/✕, not an inline row expansion. (batch 2.)** It gives the
    flow room for a second step (validate the key, then fetch the model list) without the settings page
    having to hold that state.
13. **A two-part connection identity: a stable slug `Id` plus a cosmetic `DisplayName`. (batch 3 — the
    single most directly usable finding for #678.)** OpenCode's Custom-provider sheet asks for **Provider ID**
    ("lowercase letters, numbers, hyphens, or underscores") *and separately* a **Display name**. The slug is
    what the keychain account should be keyed on: it survives base-URL edits, port changes and renames,
    which a URL-derived slug does not, and it is readable in a keychain dump and a settings diff where a
    hash is not. Presets take reserved ids; custom connections get a user-chosen one, defaulted from the host.
14. **An explicit per-connection `Models[]` list the user types, `(id, display name)` pairs with add/delete
    rows. (batch 3 — the floor of the #674 design.)** OpenCode does **not** call `/v1/models` on a custom
    endpoint; the user enters model ids by hand and names them. That is our current free-text Model box made
    plural and labelled — better than what we ship, requires no network call and no catalogue, and works on
    day one against a local Ollama. Build this first; make the searchable live catalogue (idea 7) the
    *upgrade* for providers that publish one, not a prerequisite.
   **Batch 5 confirms this working end to end**: the user's typed `(gemma4:12b-mlx, "Gemma4 12B MLX")` pair
   shows as the display name in the composer chip and picker while the id goes on the wire — the model
   answers "I am gemma4:12b-mlx." Two different strings, both needed.
15. **Optional API key with a `Headers[]` escape hatch. (batch 3.)** "Optional. Leave empty if you manage
    auth via headers", plus repeating Header-Name/value rows. Covers Azure's `api-key`, gateway tokens and
    anything non-bearer, and it makes an absent key coherent rather than an error — which our resolver
    already believes (the local-runner case) but our UI doesn't say.
16. **Custom connections as first-class peers of presets in the connected list. (batch 4.)** "Local Ollama"
    sits beside Google in the same card with the same row grammar — no second-class "custom" section, no
    different layout. Combined with idea 13, this is the shape our `Connections[]` UI should take: one list,
    presets and custom alike, each row a display name plus a badge.
17. **Quieter actions on connected rows than on unconnected ones. (batch 4 — the first half stands; the
    second half is ⚠ CORRECTED by live use.)** `+ Connect` is a bordered button; **Disconnect** is plain
    text. The invitation is loud, the undo is quiet. **Take that much.**

    ~~"Also note the provenance-driven asymmetry worth copying literally: an environment-sourced credential
    offers **no** action at all, because the app cannot remove what it did not store — the honest response
    is an empty action slot (ideally plus a hint about where it came from), not a Disconnect button that
    would lie."~~ **Do not copy this.** The maintainer hit it in live use (see "Live-use finding — the
    env-var connection has no exit" below): the empty slot is honest about *why* there is no Disconnect,
    but it leaves **no exit at all**. Google is connected, he did not choose it, and the app offers nothing
    — the only apparent escape is toggling off every one of its models individually, through a surface built
    for a different purpose, and even that is untested.

    **The fix is to split one missing control into two actions**, which are genuinely different:
    - **Disconnect** — remains impossible and absent for a credential we never stored. Nothing to revoke.
    - **"Don't use this credential"** — a local suppression flag against the connection id. Entirely within
      our power, touches no secret, needs no write access to anyone's shell profile, and is reversible.

    An app can always decline to *use* something even when it cannot *delete* it. Conflating those two is
    what produces a dead end.
18. **A visible retry countdown during a failing request. (batch 4, live test — #673.)** "Cannot connect to
    API — **Retrying in 7s**" tells the reader the app is still working, that waiting is the right thing to
    do, and roughly for how long. That much is genuinely good and we should copy it: a wait with a stated
    reason and a number beats a spinner. Copy only the countdown, not the message it is attached to (see
    NOT-list 11 and 12 — the retry policy and the wording both need fixing).
19. **The two-surface split: full catalogue in Settings, enabled subset at the composer. (batch 5 — the
    complete #674 shape, now seen end to end.)** *Settings → Models* is the whole catalogue grouped by
    provider with a toggle per row; the **composer chip** shows only the **enabled** models, searchable and
    grouped, with the current one checked and a **"Manage models"** link pinned at the bottom as the escape
    hatch back to settings. Two surfaces, two jobs: one is *"decide what I want available"*, done rarely;
    the other is *"pick for this turn"*, done constantly. Neither is forced to serve the other's cardinality.
    The `Manage models` link is the small piece that makes it work — it means the fast surface never has to
    apologise for being short.
20. **Group the per-turn picker by provider. (batch 5.)** Claude Desktop's picker is flat; OpenCode's puts a
    grey provider header above each block (`Local Ollama` over the one hand-entered model, Google over its
    own). With several endpoints connected this is what stops "the 8B I run locally" and "the hosted 70B"
    reading as interchangeable rows in one undifferentiated list — the provider is exactly the context that
    tells the reader what a name will cost, how fast it will answer, and whether it works on a plane.
21. **A model chip on the composer, showing the current model at rest. (batch 5.)** Glyph + display name +
    chevron, bottom-left of the input. The current model is answerable at a glance without opening anything,
    and the control to change it is the same object — no trip to settings, no separate menu bar item.
    Pair it with a search field **pinned above a scrolling list** and the selection marked in-place
    (blue text + ✓), which is what lets the same popup serve 1 model or 500.
22. **A link to real docs from inside the flow. (batch 3.)** "Configure an OpenAI-compatible provider. See
    the *provider config docs*." One sentence plus one link, instead of paragraphs of in-window explanation.
    Cheap, and it is where the long tail of "what URL do I use for X?" belongs.

## Ideas we should NOT take

1. **`Recommended` badges.** OpenCode's are self-promotion of first-party products; we have no first-party
   product, so any badge we shipped would necessarily be a quality verdict on somebody else's model or
   service. That is the registry, re-entering through the UI. Hard no. (#681, #670)
2. **A curated "popular models" list anywhere.** Same reasoning. Provider-published capability
   (OpenRouter's `supported_parameters`, HF's `providers[]`) is fine to *display*; a list we maintain is not.
3. **Silent adoption of environment credentials.** ~~"Recorded, not resolved."~~ **RESOLVED 2026-08-17 by
   live use, against OpenCode's stance** — see "Live-use finding — the env-var connection has no exit".
   OpenCode's rule is "valid env var ⇒ connected", with **no way to disconnect** what it adopted; the
   maintainer hit exactly that, on his own machine, having forgotten the variables were set. Defensible for
   a developer tool, wrong for us: our readers mostly did not set those variables and the downside is
   spending their money. **Our stance: discover, but do not auto-connect** — surface a found credential as
   *available*, with a Connect action, so consent is an act rather than an inference. The `Environment`
   badge still gets taken (steal 2); it labels a connection the reader chose. **Caveat:** this does not
   touch the **`CST_AI_*`** namespaced-variable design for beta 6 — setting a variable that names our own
   application *is* the consenting act. The hazard is confined to adopting variables set for another tool.
4. ~~**"Connect" as our button verb for API-key providers**~~ — **revised by batch 2.** The Connect
   OpenRouter sheet is nothing but an API-key box and a Continue button, so for a key-based provider
   "Connect" already means "paste a key" in OpenCode too. The objection survives only for the *Anthropic*
   row, whose "including Pro and Max" implies a subscription-OAuth path we do not have. Verdict: the word is
   fine as long as the sheet's first sentence says "Enter your API key"; do not copy any wording that
   implies a subscription login.
5. **A card per provider showing a logo.** Provider logos are trademarks, and a 6-provider grid of
   third-party marks in a Pāli reader is a licensing question we do not need. Text names are fine.
6. **A pre-selected *subset* of models enabled by default. (batch 2 — ⚠ REVISED IN PLACE by batch 5; the
   original wording was wrong.)**

   ~~"Any model toggled on by default… Default must be all-off (empty short list)… If an empty picker feels
   bad, fix it with copy, not with a pre-selection."~~ **Wrong as written.** The verdict is not in the
   on-state, it is in the **subset**:

   - **all-on is neutral** — "here is what this provider offers."
   - **all-off is neutral** — "choose what you want."
   - **a hand-picked subset is the registry**, because somebody decided which ones made the cut. *That* is
     what must never ship, whatever it is called.

   **And all-off is the wrong default for us on usability grounds**, which the original entry got backwards:
   connect OpenRouter with an all-off default and the reader gets an **empty picker with ~500 invisible
   models behind a search box**, unusable unless they already know a model id by heart. OpenCode defaults a
   connected provider to **all-on** and lets the user prune (this is why Lyria and Veo appear in the batch-5
   picker), and that is the right call. **Adopt all-on**, with the mechanical modality filter (steal 9)
   doing the obvious pruning, and the toggles available for the reader to cut it down to their own short
   list.

   Clarification, so this doesn't read as contradicting steal 9: **a filter defined by a provider-published
   field is not a hand-picked subset** — it is a stated, reversible rule that removes models which cannot
   answer a question at all, and it makes no claim about which of the survivors is better.
7. **A curated gateway presented as a provider — the "OpenCode Zen" move. (batch 2.)** Zen appears in the
   model list as a peer of Google, in identical visual grammar, but its contents are a list OpenCode
   maintains. For them it is a product; for us the equivalent would be a "CST Reader recommended"
   pseudo-provider, which is the deleted registry with a different noun. Named separately from the
   `Recommended` badge because it does **not** look like a registry in the UI — it looks like a provider.
8. **Sorting or ranking the model list by anything other than a mechanical key. (batch 2.)** Alphabetical,
   provider order, recently-used, or the provider's own published ordering are all fine. "Popular",
   "recommended", "best for translation", or any score we compute are not. Note that even *recency* is
   borderline if we editorialize it as "newest = better"; newer point releases can be worse at Pāli, which
   is the whole reason #670 exists.
9. **Depending on an aggregated third-party model catalogue without auditing what it carries. (batch 3 —
   NEW, and the one to watch.)** Batch 3 makes it likely (**not proven**) that OpenCode's known-provider
   model lists come from a catalogue *they* maintain rather than from live `/v1/models` calls — the custom
   sheet has no discovery at all, which is hard to explain if discovery were the mechanism. If we ever lean
   on such a catalogue, the #670/#681 rule applies to *its contents*, not to who hosts it: **aggregated
   provider-published fields (context length, price, modality, supported parameters) are fine; any field
   that ranks, scores, tiers or recommends is the registry arriving through a dependency.** And a
   third-party catalogue can add such a field in a release we didn't read. So: pull specific named fields,
   never render "whatever the catalogue says", and prefer the provider's own endpoint where it exists.
10. **Letting the settings surface say "Connected" from stored config while the runtime knows better.
    (batch 4, amended by the live test — the clearest place to diverge.)** *Original wording said OpenCode
    performs "no reachability check whatsoever"; that was wrong and is corrected in batch 4.* What it
    actually does: detects the failure **at use time** ("Cannot connect to API — Retrying in 7s", ~6
    attempts, ~30s), then reports a bare "Cannot connect to API" with no URL — **while the Providers page
    goes on listing Ollama as Connected the whole time.** The two surfaces contradict each other, and the
    one the reader will consult to diagnose is the one that lies. Use three states — configured / reachable
    / unreachable — default honestly to *configured, not yet checked*, have a use-time failure **write back**
    so both surfaces read one shared last-known-contact fact, and never use the word "Connected" for a state
    computed only from stored settings. Equally: do not swing to a permanent red `unreachable` on a laptop
    that is merely offline.
11. **Uniform retry for every failure kind. (batch 4, live test — relevant to #673.)** ~30s of exponential
    backoff (capped ~10s, ~6 attempts) is right for a flaky remote, a 429 or a 503, and wrong for
    **connection-refused on loopback**, which is not transient and is knowable on the first attempt.
    Retrying it six times spends half a minute proving what the OS already said. Classify the failure:
    refused / DNS / no-route ⇒ fail fast with a diagnosis; timeout / 5xx / 429 ⇒ back off.
12. **An error message that withholds the endpoint. (batch 4, live test.)** "Cannot connect to API" names
    nothing — not the URL, not the port, not the cause — even though the base URL is sitting in config.
    "No response from `http://localhost:11434/v1` — is the server running?" costs nothing more and is the
    difference between a reader diagnosing it and a reader filing a bug. (Redact any credential embedded in
    the URL.)
13. **Showing zero per-model metadata, as OpenCode does. (batch 2 — a thing to do BETTER, not to copy.)**
   Their caution is over-applied: bare model names force the user to guess. Provider-published context
   length, price and modality are safe and useful. Listed here so nobody reads "OpenCode shows nothing" as
   the conservative model to imitate.

## Open questions

Cumulative. Batch-1 items are marked with what batch 2 settled.

1. ~~**The Models page was never shown.**~~ **ANSWERED (batch 2), with one gap.** Search box: yes, one free-text
   box, no filters/sort. Grouping: by provider, collapsible, alphabetical within group. Per-model metadata:
   **none at all** — name and a toggle. Selection: a per-row toggle, i.e. enablement of a short list, with
   the actual per-turn choice living somewhere not captured. **Still open:** what the page looks like at
   OpenRouter's ~500-model scale — only Google (~30) was shown, so whether search alone carries it, and
   whether the list virtualizes, is untested. That is precisely the #674 question.
2. ~~**What does `+ Connect` actually open?**~~ **ANSWERED for the API-key case (batch 2)** and **for Custom
   (batch 3)**. Preset: a full-pane modal sheet with back-arrow/✕, a heading, a one-sentence explanation, a
   single "API key" field, and a **Continue** button. Custom: the same sheet shape, asking **Provider ID,
   Display name, Base URL, API key (optional), Models[] (id + display name, add/delete rows), Headers[]
   (optional)**, ending in **Submit**. **Still open:** only the OAuth/Anthropic variant.
3. ~~**Can more than one provider be connected at once?**~~ **ANSWERED in part (batch 2):** yes — the Models
   page shows two provider groups (OpenCode Zen, Google) coexisting, each connected by its own flow, so
   credentials are clearly keyed per provider identity rather than per wire format. **How the active model
   is chosen: ANSWERED (batch 5)** — a composer chip, per-turn, drawing across all connected providers in one
   grouped list. **Still open (narrower):** whether that choice is per-session, sticky-global, or
   remembered per conversation. The toggle page remains availability, not selection.
4. ~~**What does a connected row look like after connecting normally?**~~ **ANSWERED (batch 4):** display name,
   a grey badge naming the connection's kind/source (`Custom` on Local Ollama, beside Google's
   `Environment`), **no description line** (unlike unconnected rows), and a right-aligned plain-text
   **Disconnect** — quieter than the bordered `+ Connect` on unconnected rows. Google's action slot is
   **empty**: an env-sourced credential offers no action, because the app cannot remove what it did not
   store. **Still open (narrower):** what a *preset* connected by pasting a key looks like — Zen/Anthropic
   were never connected on camera, so whether badges are systematic or only mark unusual cases is unproven.
5. ~~**Where does reasoning effort live** (#671)?~~ **PARTLY ANSWERED (batch 2): not on the Models page.** Rows
   have a toggle and no other affordance — no gear, no chevron, no click-through — so there is no per-model
   configuration surface there. Where it *does* live (per-session in the chat UI? nowhere?) is still open.
6. ~~**Any per-model or per-provider health/latency display** (#673)?~~ **ANSWERED, negative (batch 2):** none.
   No uptime, no latency, no throughput anywhere on the Models page. OpenCode ignores what HF and OpenRouter
   publish. Since that data is provider-published it remains ours to use if we want it; there is just no
   precedent here to borrow.
7. **How is a missing/invalid key surfaced** — at connect time, or at first failed request? **Still open for
   the key case**, but batch 4's live test settles the neighbouring one: an **unreachable endpoint** is
   surfaced only at first failed request, never in settings. Batch 2's hint stands for the key
   (**"Continue"**, not "Save", suggests a validation step follows the paste) but remains uncaptured. Worth
   noting the two are different failures that deserve different copy — a refused connection and a rejected
   key should never produce the same sentence.
8. ~~**Where does the actual per-turn model picker live**, and what does it show?~~ **CLOSED (batch 5).** A
   **chip at the composer's bottom-left** (glyph + display name + chevron) opening a popup **above** it: a
   pinned **"Search models"** field, a scrolling list **grouped by provider** under grey headers, the current
   model in **blue with a ✓**, and **"Manage models"** pinned at the bottom. It shows **the enabled subset
   only** — not the full catalogue — confirmed by the maintainer. So the split is: Settings → Models = full
   catalogue with toggles; composer chip = enabled models, searchable, with an escape hatch back.
9. **(batch 2)** ~~**Does the model list refresh, and when?**~~ **REFRAMED, not closed, by batch 3.** The
   staleness question is now sharper, because the mechanism is probably not what batch 2 assumed: if
   known-provider lists come from a maintained catalogue rather than a live call, "refresh" means *ship a
   catalogue update*, and the offline story is different (you have last-shipped data, not nothing). For us
   the decision stands either way — and batch 3 supplies the safe default: hand-entered `Models[]` always
   works, so any catalogue is additive and its staleness is never fatal.
10. **(batch 2)** ~~**What does the Custom-provider group look like in the Models list?**~~ **ANSWERED
    (batch 3):** it is populated by the models the user **typed into the Custom sheet** — `model-id` plus a
    `Display Name` per row. There is no `/v1/models` discovery for custom endpoints at all. So a custom
    provider does get its own group, and its contents are the user's own list. **Consequence for us: the
    free-text model entry must REMAIN** (as a per-connection list, not one box) — a picker cannot replace it,
    because an arbitrary endpoint may publish no catalogue.
11. **(new, batch 3)** **Where does OpenCode's known-provider model metadata actually come from?** Live
    `/v1/models`, or a catalogue OpenCode ships/maintains (models.dev is theirs)? Batch 3 makes the latter
    likely but does not prove it, and the answer changes both the staleness design and how carefully we'd
    have to audit any such source for smuggled quality judgments (NOT-list #9).
12. **(new, batch 3)** **Is `Provider ID` validated for uniqueness, and what happens on collision** — is
    an existing custom provider silently overwritten, merged, or rejected? Directly relevant, since the id
    would be our keychain account: a collision would mean one connection quietly inheriting another's key.
13. **(batch 3)** **Can an existing connection be edited or is it delete-and-recreate?** **Partly answered
    (batch 4), negatively:** the connected row shows **only Disconnect** — no Edit, no gear, no chevron. So
    either the row itself is clickable (a screenshot can't say) or editing genuinely is disconnect-and-
    recreate. Still open, and still load-bearing for us: an editable ID means migrating the keychain account
    with it, so an **immutable Id + editable everything-else** is probably the rule to adopt outright.
14. **(new, batch 4)** **What does Disconnect actually remove for a custom connection** — just the stored
    key, or the whole record (base URL, headers, the hand-typed `Models[]`)? For a preset, dropping the key
    is enough. For a custom endpoint, the models list is real user-authored work, and destroying it on a
    Disconnect meant only to un-bill an account would be a genuine data loss. Our answer should probably be
    two distinct actions (remove key / delete connection), which OpenCode does not offer.
15. **(new, batch 4)** **Where does a connected preset's base URL or account identity get shown, if ever?**
    Local Ollama's row shows no host, no port, no ID — a user with two Ollama instances sees only two
    display names. Fine when the user chose good names; opaque when they didn't. Worth deciding whether our
    row carries a grey second line with the base URL.
16. **(new, batch 5)** **Is the composer's model choice per-conversation, per-session, or sticky-global?**
    The chip shows one model; whether opening a new conversation inherits it, resets to a default, or
    remembers per-thread is not visible. Matters for us: a reader who picked a local model for a cheap
    question should not silently keep it for the next one if they'd rather not — or should, if that's the
    expectation. Pick deliberately.
17. **(new, live use)** **Does toggling off every model hide a provider from the picker entirely?** The
    maintainer's assumed workaround for an unremovable env-var provider, explicitly **untested**. Only
    matters to us as a warning: if that *is* the only exit, it means a provider's presence is a derived
    property of its model list rather than a thing the user controls — which is precisely the coupling our
    "Don't use this credential" flag (steal 17) avoids. Not worth testing on their app; worth designing
    against in ours.
18. **(new, batch 3)** **Is anything validated on Submit?** No "Test connection" button exists, and the
    button says Submit rather than Continue (unlike the preset flow), which hints the custom form just saves.
    If so, a wrong base URL is discovered at first request — which makes our `ReadinessText`/resolver
    approach look better than theirs, not worse.

## Implementation trap: the per-model control must be multi-select

Recorded 2026-08-17 after the maintainer confirmed, from the running app, that **the controls under
Settings → Models are what determine the contents of the composer picker** — the gating relationship is now
observed, not inferred from the control type.

The controls in *Models 1* have been described both as **toggle switches** and as **radio buttons**. The
distinction is behavioural, not cosmetic, and only one of the two is correct here:

- **Toggle switch / checkbox** — independent on/off per row; any number on at once.
- **Radio button** — mutually exclusive *within a group*; turning one on turns the rest off.

It must be the former. The composer picker shows **several models under a single provider heading**, which
radio semantics cannot produce — radio would cap the enabled set at exactly one model per provider and
silently destroy the whole short-list design.

**For our Avalonia implementation:** `ToggleSwitch` or `CheckBox`, never `RadioButton`. Worth stating
explicitly in the spec, because "radio button" is a natural way to describe the control by sight, and taking
that word literally at implementation time would produce a bug that looks like a design decision.
