# CST4 Keyboard Shortcuts

Reference catalog of every keyboard shortcut in the original **CST4** (WinForms)
application, for use when designing the CST Reader (Avalonia) equivalents. Each
entry cites the `src/Cst4/` source that implements it.

**Key finding:** CST4 does **not** define shortcuts through the normal WinForms
menu mechanism — no menu item sets `ShortcutKeys`, and no `.resx` carries a
`ShortcutKeyDisplayString`, so **the menus display no accelerator hints**. Every
real shortcut is implemented by hand in `KeyDown` / `KeyPress` handlers. The only
menu-driven keys are standard **Alt-mnemonics** derived from `&` in the menu
labels. There are also no `ProcessCmdKey`/`ProcessDialogKey` overrides.

## Shortcut summary

### Global — main window (`FormMain`, `KeyPreview = true`, so these fire anywhere)

| Keys | Action | Source |
|------|--------|--------|
| **Ctrl+O** | Open / Select Book (opens the book-tree picker) | `FormMain.cs:1059` → `SelectBook()` |
| **Ctrl+D** | Open Dictionary (looks up the selected word if a book is focused; otherwise opens empty) | `FormMain.cs:1052` → `OpenDictionary()` |
| **Ctrl+W** | Word Search (of the selected word if a book is focused) | `FormMain.cs:1061` → `SearchWord()` |

### Open book window (`FormBookDisplay`) — operate on the current text selection

| Keys | Action | Source |
|------|--------|--------|
| **Ctrl+D** | Dictionary lookup of the selected word | `FormBookDisplay.cs:1200,1236` → `OpenDictionary(selection)` |
| **Ctrl+W** | Word Search of the selected word | `FormBookDisplay.cs:1203,1250` → `SearchWord(selection)` |
| **Ctrl+G** | Go To (paragraph / page) | `FormBookDisplay.cs:1187` → `GoTo()` |
| **Ctrl+Q** | Show Source — Burmese **1957** edition (source PDF) | `FormBookDisplay.cs:1208,1263` → `ShowSource(true)` |
| **Ctrl+E** | Show Source — Burmese **2010** edition (source PDF) | `FormBookDisplay.cs:1211,1265` → `ShowSource(false)` |
| **Ctrl+T** | Translate — open an external translation page in the browser | `FormBookDisplay.cs:1214,1267` → `Translate()` |

### Select-a-Book tree (`FormSelectBook`)

| Keys | Action | Source |
|------|--------|--------|
| **Enter** | Open the selected book (leaf node only) | `FormSelectBook.cs:200` |

### Dialogs — standard WinForms default/cancel buttons

| Keys | Action | Source |
|------|--------|--------|
| **Enter** | Activate the default button (e.g. **OK** in Go To / About) | `FormGoTo.Designer.cs:138`, `AboutBox.Designer.cs:130` (`AcceptButton`) |
| **Esc** | Cancel (Go To dialog) | `FormGoTo.Designer.cs:141` (`CancelButton`) |

### Menu Alt-mnemonics (from `&` in labels, `FormMain.resx`)

Top-level menu bar:

| Keys | Menu |
|------|------|
| **Alt+B** | **B**ook |
| **Alt+S** | **S**earch |
| **Alt+D** | **D**ictionary (top-level item; opens the Dictionary window) |
| **Alt+W** | **W**indow |
| **Alt+H** | **H**elp |

Sub-item mnemonics (used after opening the parent menu): Book → **O**pen,
**S**ave, **P**rint…, E**x**it; Search → Wo**r**d; Help → **A**bout. (Items
without a `&` — Recently Viewed, Page Setup…, Print Preview…, Advanced, Contents,
Check for Updates, Cascade, Tile Horizontal/Vertical — have no mnemonic.)

## Implementation notes & quirks

These matter if the Avalonia port aims to reproduce behavior, and they explain
the odd shape of the code.

1. **Handlers, not menu accelerators.** Because shortcuts are coded in handlers,
   they are invisible in the menus and can't be re-bound by the framework. A port
   should define real accelerators on its commands instead.

2. **Duplicate handlers per book window.** Each book window wires the same Ctrl
   combos twice: once on the embedded IE/Trident **WebBrowser** via
   `Body_KeyPress` (`HtmlElementEventArgs`, `FormBookDisplay.cs:1180`) and once on
   the form via `FormBookDisplay_KeyDown` (`:1232`). The embedded browser swallows
   keystrokes, so both paths exist to make the shortcuts work whether the browser
   or the form has focus. `FormMain` *also* re-handles Ctrl+D/O/W globally
   (`KeyPreview = true`), with guards that check `ActiveMdiChild`'s type to avoid
   invoking the action twice (`FormMain.cs:1056,1065`).

3. **WebBrowser `KeyPressedCode` is alphabet-position, not ASCII.** In the
   `Body_KeyPress` handler the code is the letter's index (A=1 … Z=26), documented
   in-code at `FormBookDisplay.cs:1178-1179`: D=4, E=5, G=7, Q=17, T=20, W=23.
   Code **116** is VK_F5, used in a (self-described "doesn't work!") attempt to
   suppress the browser's Refresh (`:1172,1220`).

4. **Ctrl+G asymmetry.** Go To is handled **only** in the browser
   `Body_KeyPress` path (code 7), not in `FormBookDisplay_KeyDown` — unlike
   Ctrl+D/W/Q/E/T, which appear in both.

5. **Double-click focus HACK.** Double-clicking a word to select it left the
   embedded browser in a state where the Dictionary/GoTo keystrokes stopped
   firing; `Body_DoubleClick` (`FormBookDisplay.cs:1144`) works around it by
   pulling focus off the WebBrowser and back ("discovered by much trial and
   error").

6. **Single-word guard.** Ctrl+D and Ctrl+W ignore selections containing a space —
   only single-word lookups are performed (`FormBookDisplay.cs:1197,1244`).

7. **`Ctrl+T` is essentially a stub.** `Translate()` only maps two hard-coded div
   IDs (`dn1_2`, `dn1_9`) to accesstoinsight.org URLs (`FormBookDisplay.cs:844`);
   everything else does nothing.

## Porting implications (CST Reader / Avalonia)

- **Nothing is inherited automatically** — there are no framework accelerators to
  carry over; each shortcut must be defined intentionally on the Avalonia side.
- **macOS conventions.** CST4's Ctrl-combos map naturally to **⌘** on macOS.
  Alt-mnemonics don't translate to macOS menus. The Avalonia app also uses a CEF
  WebView, so the same "browser captures keystrokes" problem exists and the port
  will likely need its own focus/keybinding handling.
- **Already surfaced:** the dictionary feature work (#25) confirmed **Ctrl+D on a
  single-word selection** as CST4's selected-word→dictionary shortcut; see
  [../../features/in-progress/DICTIONARIES.md](../../features/in-progress/DICTIONARIES.md).
- Related source-PDF feature (Ctrl+Q / Ctrl+E): see
  [../../features/in-progress/SHOW_SOURCE_PDF.md](../../features/in-progress/SHOW_SOURCE_PDF.md).
