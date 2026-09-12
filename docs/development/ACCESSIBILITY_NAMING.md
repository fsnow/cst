# Accessibility naming: `AutomationProperties` in CST Reader

How controls are labelled for screen readers and addressed by scripts. Introduced with #862.

The convention below is **[suggestion]** — an agent's proposal, not a maintainer decision. The
mechanics recorded as **[observed]** are measured facts about Avalonia 11.3.6, macOS and
AppleScript, each with the measurement that produced it.

---

## 1. The two properties

`AutomationProperties.Name` and `AutomationProperties.AutomationId` are different things and are
used for different reasons.

**`Name` — what a screen reader speaks.** It should be, or contain, the control's visible text.
Set it where the control has no visible text of its own: an icon button, a glyph button, a bare
`ToggleSwitch`, a picker whose only label is a `TextBlock` sitting beside it. Do not set it where
the control already carries its own text — a `Button Content="Cancel"` is already named "Cancel",
and a second, different name is a WCAG "label in name" failure waiting to happen.

**`AutomationId` — a stable handle for scripts and tests.** Treat it as **public API**: once a
script depends on one, renaming it breaks that script silently. Add ids freely; change them only
deliberately.

## 2. The `AutomationId` scheme

Lowercase, dotted, `surface.group.control`, hyphens inside a segment:

```
settings.categories                 book.find.close          search.filter.vinaya
settings.ai.tab.providers           book.zoom-out            dictionary.source
settings.appearance.book-font       goto.type.pts-page       assistant.word-by-word
```

Surface prefixes in use: `settings`, `book`, `pdf`, `search`, `books` (the Open a Book tree),
`dictionary`, `assistant`, `about`, `goto`, `main` (the app window's own chrome).

**An id names the control, never where it currently sits.** Book, PDF, dictionary and assistant
panes are all draggable into their own windows, so any id encoding "main window" becomes false the
first time a reader floats the pane.

**Do not put a static `AutomationId` on anything inside a `DataTemplate`** — every generated row
would carry the same id. Put the id on the container (`settings.dictionary.sources`) and give the
rows a bound `Name` instead.

## 3. How to write it

`AutomationProperties` lives in the default `avaloniaui` XML namespace, so it needs **no `xmlns`
prefix and no namespace declaration** — write `AutomationProperties.Name="…"` directly.
**[observed]** — parsed successfully in an `Avalonia.Headless` 11.3.6 harness on 2026-09-06.

A generated container (`ListBoxItem`, `TreeViewItem`) has no text of its own, so name it with a
style setter on the container, not inside the item template:

```xml
<ListBox.Styles>
  <Style Selector="ListBoxItem" x:DataType="vm:SettingsCategoryViewModel">
    <Setter Property="AutomationProperties.Name" Value="{Binding Name}"/>
  </Style>
</ListBox.Styles>
```

A repeated control needs a name that distinguishes its row, so bind with a `StringFormat`:

```xml
<Button Content="&#x25B2;"
        AutomationProperties.Name="{Binding DisplayName, StringFormat='Move {0} up'}"/>
```

## 4. What the framework already does, and what it will not do

**[observed]**, all from `Avalonia.Controls` 11.3.6 (decompiled) and confirmed in a headless
automation-peer walk on 2026-09-06:

| Situation | Result |
|---|---|
| `AutomationProperties.Name` on `Button`, `CheckBox`, `ToggleButton`, `ComboBox`, `TextBox`, `Slider`, `TabItem`, `Expander`, `ListBox`, `TreeView` | Wins over every fallback — `ContentControlAutomationPeer.GetNameCore` calls `base.GetNameCore()` first |
| `AutomationProperties.Name` on a **`TextBlock`** | **Ignored.** `TextBlockAutomationPeer.GetNameCore` returns `Owner.Text` unconditionally. Set only an `AutomationId` on a `TextBlock` |
| `AutomationProperties.AutomationId` anywhere | Always reaches the peer, `TextBlock` included |
| No `AutomationId`, but an `x:Name` | `x:Name` is used as the id (`GetAutomationIdCore` returns `AutomationProperties.GetAutomationId(Owner) ?? Owner.Name`). This is why template parts show up as `PART_ContentPresenter`, `ExpanderHeader` and so on — noise we did not put there, and a reason to set ids explicitly rather than rely on the fallback |
| A container with no name (`ListBoxItem`, `TreeViewItem`) | Falls back to `Content?.ToString()` — **the view-model type name**. This is exactly what #862 reported as `row CST.Avalonia.ViewModels.SettingsCategoryViewModel` |
| A `Button` whose `Content` is a layout element (`Grid`, `StackPanel`) | Same fallback, same type name — e.g. `Avalonia.Controls.StackPanel` for the assistant's model chip |
| An `Expander` whose `Header` is a layout element | The **header `ToggleButton`** — the thing that is actually clicked — reports the header's type name. A `Name` on the `Expander` does **not** reach it; it has to be set through the template: `<Style Selector="Expander#X /template/ ToggleButton">`. `SearchPanel.axaml` does this for the text-type filters |
| An `Expander` on its own | Reports `AutomationControlType.None`. Its name and id are still published; it is the header button that carries the role |

## 5. What this buys, on macOS, and what it does not

**[observed]** The macOS bridge does publish names, ids and roles. `Avalonia.Native` 11.3.6's
`AvnAccessibilityElement` implements `accessibilityRole`, `accessibilityTitle` and
`accessibilityIdentifier`, wired to the peer's control type, `GetName()` and `GetAutomationId()`
respectively; the dylib imports `NSAccessibilityButtonRole`, `NSAccessibilityStaticTextRole`,
`NSAccessibilityCheckBoxRole` and eighteen more from AppKit. (`otool -oV` and `nm -mu` on
`libAvaloniaNative.dylib`, plus ILSpy on `Avalonia.Native.dll`, 2026-09-06.)

**[observed]** #862's three failing AppleScript queries fail the same way against a fully native
Cocoa app, so they were never evidence of an Avalonia gap:

```
$ osascript -e 'tell application "System Events" to tell process "Finder" \
      to get every static text of entire contents of menu bar 1'
execution error: Can't make every static text of entire contents of menu bar 1 …  (-1700)

$ osascript -e 'tell application "System Events" to tell process "Finder" \
      to get name of every menu bar item of menu bar 1'
Apple, Finder, File, Edit, View, Go, Window, Help
```

`entire contents` is declared in System Events' own dictionary as a **property returning a list**
(`<property name="entire contents" … <type type="specifier" list="yes"/>`), and AppleScript cannot
apply an element specifier or a `whose` clause to a list. The forms that work are
`<class>s of <container>` and `first <class> of <container> whose name is "…"` — both of which need
elements to have **names**, which is what this convention supplies.

**[observed]** `AXIdentifier` is readable per element —
`value of attribute "AXIdentifier" of menu bar item 3 of menu bar 1` returned `_NS:1115` against
Finder — but System Events cannot read it in bulk or filter on it: both
`value of attribute "AXIdentifier" of every menu bar item …` and
`… whose value of attribute "AXIdentifier" is "…"` fail with -1728. So from AppleScript,
**`Name` is what makes a control findable**; `AutomationId` is a stable handle you verify once you
have the element, and the property a real UI-automation driver (which talks to the AX API directly)
would key on.

**Not verified.** Nothing here was measured against a running CST Reader — the app was deliberately
not launched. That the names and ids reach Avalonia's automation peers is measured; that macOS then
surfaces them to AppleScript follows from the bridge implementation above but has not been observed
end to end.
