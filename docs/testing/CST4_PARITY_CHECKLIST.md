# CST4 ↔ CST Reader (Avalonia) feature-parity checklist

Purpose: drive a side-by-side comparison of CST4 (legacy WinForms) against the Avalonia rewrite. The CST4 columns were inventoried from the CST4 source (`src/Cst4/` — menus/toolbars/forms `.resx` + `.cs` handlers), so they are authoritative; the Avalonia status was cross-checked against `src/CST.Avalonia`. Flip any **UNKNOWN** rows to a verdict during the live comparison.

Status legend: **PRESENT** = at parity · **PARTIAL** = present but differs / incomplete · **MISSING** = no counterpart · **UNKNOWN** = needs a live check · **(changed)** = same capability, different paradigm · **(new)** = Avalonia-only enhancement.

_Assembled 2026-07-03 from a 3-agent CST4 source inventory._

## Major gaps at a glance

| Gap | Tracker | Notes |
|---|---|---|
| UI localization — 24 languages, runtime switch | **[#26](https://github.com/fsnow/cst/issues/26)** | Avalonia has zero satellite `.resx` + inert stub (SCRIPT-8). Authoritative language list posted to #26. |
| Per-book **View** menu: Show Footnotes / Show Search Terms | **[#224](https://github.com/fsnow/cst/issues/224)** | XSL JS + state fields exist; only the UI + wiring missing. |
| ~~Global "Pali Script" should re-script open books~~ | **[#225](https://github.com/fsnow/cst/issues/225)** ✅ | **Done 2026-07-04** — global re-scripts all open books (CST4 parity). |
| Print / Page Setup / Print Preview (book + main) | _needs issue_ | No print anywhere in Avalonia. |
| Book **Collections** (custom book sets) + editor + search scoping | _needs issue_ | `comboBookSet`, FormBookCollEditor — no Avalonia concept. |
| ~~Search **Reports** (Choose Report → Report viewer)~~ | **Won't implement** | Barely-there in CST4 (only "All Words" was implemented; other report radios disabled). Maintainer decision 2026-07-04. |
| **Recently Viewed** (MRU) | _needs issue_ | `RecentBooks` model exists but never populated/surfaced. |
| **About** box | _needs issue_ | `LayoutViewModel.ShowAbout()` is a TODO stub, unwired. |

**Not parity targets** (confirmed with maintainer): Ctrl+T *Translate* (an undocumented **Easter egg** — it shipped in CST4 but was never documented or publicized); FormAdvancedSearch (was "under development" / incomplete in CST4); Book > Save (no-op stub in CST4); **Search Reports** (barely-there in CST4 — only "All Words" implemented; won't implement, 2026-07-04).

---

## 1. Main window / shell — menus, toolbar, dropdowns (CST4 `FormMain`)

| CST4 feature | CST4 behavior | Avalonia status | Notes / hint |
|---|---|---|---|
| **Book** menu | Groups book/file actions | PARTIAL | No top-level Book/File menu; actions spread across native View/Tools menus |
| Book > Open (Ctrl+O) | Opens Select-a-Book tree | PRESENT | View > "Select a Book" toggle; no Cmd+O binding |
| Book > Recently Viewed | MRU of recently closed books | MISSING | `RecentBooks` model exists, never populated/surfaced |
| Book > Save | No-op stub | MISSING | Intentionally dropped |
| Book > Print… / Print Preview… / Page Setup… | Print active book | MISSING | No print family anywhere |
| Book > Exit | Close app | PRESENT | Cmd+Q; saves state on close |
| **Search** menu > Word (Ctrl+W) | Opens word search | PRESENT | View > Search; Tools > "Search for Selection" (Cmd+F) feeds it |
| Search > Advanced | Opens FormAdvancedSearch | MISSING | CST4 form was incomplete; not a target |
| Search > Dictionary (Ctrl+D) | Opens dictionary | PRESENT | View > Dictionary + Tools > Look Up (Cmd+D) |
| **Window** menu (Cascade / Tile H / Tile V / MDI list) | MDI window management | MISSING (changed) | Docking/tabs model replaces MDI |
| **Help** menu | | MISSING | No Help menu |
| Help > Check for Updates | MessageBox TODO stub | PARTIAL | Updates run automatically at startup, not user-invoked |
| Help > About | AboutBox dialog | MISSING | `ShowAbout()` TODO stub, unwired |
| Toolbar: Open Book | Opens tree | PARTIAL | Via View menu; no toolbar button |
| Toolbar: Save / Print / Print Preview / Page Setup | | MISSING | Dropped / no print |
| Toolbar: Word Search / Go To / Dictionary | | PRESENT | Cmd+F / Cmd+G / Cmd+D |
| "Pali Script:" dropdown | Re-scripts ALL open books + tree/search/dict | PRESENT | Fixed in #225 (2026-07-04): global change re-scripts all open books; per-tab overrides that book; later global re-applies to all |
| "Interface Language:" dropdown | Live UI-culture switch (many langs) | PARTIAL/MISSING | Combo present but hardcoded to English, no handler — **[#26](https://github.com/fsnow/cst/issues/26)** |
| Global accelerators (Ctrl+D/O/W) | Keyboard shortcuts | PARTIAL | Rebound to Cmd+D/G/F/E; no Cmd+O |
| Window state + session restore | Restore size/pos + reopen books/search/dict | PRESENT | #70, DOCK-6, #105 |
| Font install on startup | Copies fonts to OS | PARTIAL (changed) | Pre-loads script fonts instead of OS install |

## 2. Navigation — Select a Book & Go To (CST4 `FormSelectBook`, `FormGoTo`)

| CST4 feature | CST4 behavior | Avalonia status | Notes / hint |
|---|---|---|---|
| Select a Book tree | Hierarchical collections/books tree | PRESENT | OpenBookPanel + BookTreeNode (icons + counts) |
| Double-click / Enter opens book | Opens in current script | PRESENT | TreeView double-tap |
| Live re-script of tree labels | On Pali Script change | PRESENT | Script/font bindings |
| Tree expansion-state persistence | Save/restore expanded nodes | PRESENT | ✅ verified 2026-07-04 — survives restart (SCRIPT-5 deterministic restore) |
| Go To dialog | Para/page jump | PRESENT | GoToDialog |
| Go To radios: Paragraph / VRI / Myanmar / PTS / Thai / Other Page | Navigate by number, disabled if unavailable | PRESENT | Availability flags per book |
| Go To number box + V/M/P/T prefix auto-switch | Typing a letter prefix switches the radio | PRESENT | ✅ maintainer-verified 2026-07-04 — all working (number entry + letter-prefix radio auto-switch) |
| OK / Cancel | Confirm / dismiss | PRESENT | IsDefault / IsCancel |

## 3. Reading window (CST4 `FormBookDisplay`)

| CST4 feature | CST4 behavior | Avalonia status | Notes / hint |
|---|---|---|---|
| Chapter dropdown | Scroll to chapter + auto-track on scroll | PRESENT | Chapters/SelectedChapter combo |
| Per-book "Pali Script:" | 14 scripts, re-render book | PRESENT | BookScript combo (learning tool) |
| First / Previous / Next / Last Result | Navigate search hits | PRESENT | Hit commands + N-of-M counter (new) |
| Mūla / Aṭṭhakathā / Ṭīkā | Open linked text at matching paragraph | PRESENT | HasMula/HasAttha/HasTika |
| **View** dropdown → Show Search Terms / Show Footnotes | Toggle hit highlight / footnote visibility | MISSING | XSL JS + state fields exist — **[#224](https://github.com/fsnow/cst/issues/224)** |
| Page-numbers status bar | V/M/P/T/O page refs tracked to scroll | PRESENT | PageReferencesText |
| Print / Page Setup / Print Preview | Browser print dialogs | MISSING | No print |
| Go To (Ctrl+G) | Para/page jump | PRESENT | Cmd+G |
| Dictionary lookup on selection (Ctrl+D) | Look up selected word | PRESENT | Cmd+D via WebView selection |
| Word Search on selection (Ctrl+W) | Send selected word to Search | PRESENT | ✅ verified 2026-07-04 — Tools > Search for Selection (Cmd+F) |
| Show 1957 / 2010 source edition (Ctrl+Q / Ctrl+E) | Open matching source at current page | PRESENT (changed) | Internal PDF viewer (Cmd+E / Shift+Cmd+E) not external browser |
| Translate (Ctrl+T) | External translation link | N/A | Undocumented Easter egg (shipped in CST4 but unpublicized) — not a target |
| Search-term highlighting | Wrap matches in hit anchors, scroll to hit0 | PRESENT | showHits JS |
| Window title = nav path (script-converted) | | PRESENT | |
| Float / Dock | | PRESENT (new) | Avalonia adds float/unfloat |

## 4. Dictionary (CST4 `FormDictionary`)

| CST4 feature | CST4 behavior | Avalonia status | Notes / hint |
|---|---|---|---|
| Modeless dictionary | Hidden not closed on X | PRESENT (changed) | Dockable DictionaryPanel |
| Word entry box | Live lookup per keystroke; script font per first char | PRESENT | SearchText live lookup |
| Definition Language combo (English / Hindi) | Choose En/Hi dictionary | PRESENT | ✅ verified 2026-07-04 — English + Hindi both present |
| Words list | Prefix/nearest-match in current script | PRESENT | Words ListBox |
| Meaning pane | Render meaning HTML; `<see>` cross-refs clickable | PRESENT | Native TextBlock + MeaningParser |
| See-also / Back navigation | Follow `<see>`, "Back to" return | PRESENT | Back/Forward buttons |
| Close button | Hide window | PARTIAL | Dock chrome close; no dedicated button |
| Live re-script | Reload word list in current script | PRESENT | Font bindings |

## 5. Source viewer / browser (CST4 `FormBrowser`)

| CST4 feature | CST4 behavior | Avalonia status | Notes / hint |
|---|---|---|---|
| Embedded browser window | Generic in-app browser (source PDFs + Translate links) | PARTIAL (changed) | Source PDFs → internal PdfDisplayView; no generic web browser (only Translate used it — not a target) |

## 6. Search (CST4 `FormSearch`)

| CST4 feature | CST4 behavior | Avalonia status | Notes / hint |
|---|---|---|---|
| Search box + Enter | Query; script auto-detect | PRESENT | + live 300ms debounce search (new) |
| Use: Wildcards / Regular expressions | Term-match mode | PRESENT | Wildcard / Regex combo |
| Exact (no wildcard chars) | Behaves as exact | PRESENT | Auto-downgrade Wildcard→Exact |
| Regex invalid-pattern handling | Modal error | PRESENT (changed) | Inline "Invalid regex" hint |
| Multi-word | Split terms + context | PRESENT | MultiWordSearch |
| Phrase (quoted) | Adjacency | PRESENT | Multi-phrase supported |
| Word distance (1–99, default 10) | Proximity window (non-phrase multi-word) | PRESENT | Proximity slider |
| Limit Search: 7 book-type checkboxes | Vinaya/Sutta/Abhi + Mūla/Aṭṭha/Ṭīkā + Other | PRESENT | Identical bit logic |
| "All" checkbox | Toggle all types | PARTIAL | Select All / Select None buttons instead |
| Book Collection combo + Edit/Delete | Scope search to a saved custom collection | MISSING | No collection concept — _needs issue_ |
| Words list + Ctrl+A select-all | Matching terms, multi-select | PRESENT | ✅ verified 2026-07-04 — cmd-A selects all terms + occurrences merge (`UpdateOccurrences`). Books/Occurrences list is single-select in both CST4 and Avalonia (no cmd-A there is correct) |
| Word stats ("Words:"/"Word Combinations:") | Term/selection counts | PARTIAL | Always "Words:"; no "Word Combinations" wording for multi-word |
| Occurrences–Books list + stats | Books with per-book hit counts | PRESENT | Occurrences + OccurrenceStats |
| Double-click occurrence → open + highlight | Open book at hits | PRESENT | Opens with Positions |
| Report… button | Open report chooser | Won't implement | Search Reports dropped (barely-there in CST4) — 2026-07-04 |
| Ctrl+B bad-word checker | Scan index for malformed terms | MISSING | IpeWordChecker unported (minor) |
| Live re-script | Re-render term/book lists | PRESENT | |
| Results-truncated warning / pane state persistence | | PRESENT (new) | Avalonia-only (#87) |

## 7. Reports, Collections, Advanced Search

| CST4 feature | CST4 behavior | Avalonia status | Notes / hint |
|---|---|---|---|
| Choose Report (All Words / Selected / Occurrences ×2) | Pick report; only "All Words" implemented | Won't implement | Barely-there in CST4 (3 of 4 radios disabled) — maintainer decision 2026-07-04 |
| Report viewer | XSL→HTML report + print | Won't implement | Dropped with Search Reports — 2026-07-04 |
| Advanced Search | Multi-word into results box | MISSING | CST4 "under development" — not a target |
| Book Collection Editor | Dual-list custom book-set builder | MISSING | _needs issue_ (pairs with search scoping) |

---

## Live-check queue — ✅ all resolved (2026-07-04)
Every UNKNOWN/PARTIAL row has been maintainer-verified: Go To V/M/P/T prefix auto-switch; cmd-A select-all + occurrence-merge (Books list single-select in both, correct); Cmd+F word-search-from-selection; Dictionary English/Hindi language list; tree-expansion restore across restart; and global-script re-scripts open books (#225). Nothing left to check live.

## Gaps that need an issue filed (maintainer decision on priority)
Print family · Book Collections + editor + search scoping · Recently Viewed (MRU) · About box · Ctrl+B bad-word checker (minor). _(Search Reports dropped as won't-implement, 2026-07-04.)_
