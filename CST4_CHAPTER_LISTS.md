# CST4 Chapter List Dropdown Feature

This document details the implementation of the chapter navigation dropdown feature in the CST4 application. This feature provides a hierarchical list of chapters and sections within a book, allowing for quick navigation.

The feature is primarily implemented in the `Cst4` project, with data generation occurring during the indexing process.

## Feature Overview

The chapter list dropdown (`tscbChapterList`) is a `ToolStripComboBox` located on the main toolbar of the `FormBookDisplay` window.

- **Visibility**: The dropdown is only visible if the currently opened book contains the necessary structural markup in its source XML file.
- **Content**: The dropdown is populated with a list of `DivTag` objects, which represent the chapters, sections, and sub-sections of the book. The text displayed is the chapter heading, converted to the currently selected script.
- **Hierarchy**: The hierarchical structure is visually represented by indenting the chapter headings based on the nesting level of the `<div>` tags in the XML source.
- **Functionality**:
    1.  **User Navigation**: When a user selects an item from the dropdown, the `FormBookDisplay` scrolls to the corresponding chapter's location in the text.
    2.  **Scroll-based Updates**: As the user scrolls through the text, the dropdown's selected item is automatically updated to reflect the current chapter visible at the top of the window.

---

## File Interactions

### 1. `src/Cst4/FormBookDisplay.cs`

This is the main form for displaying book content and the central component for this feature's UI logic.

- **`Init()` method (L71-88):**
    -   `List<DivTag> chapterList = ChapterLists.Inst[book.Index];` (L74): Retrieves the pre-compiled list of chapters for the current book from the singleton `ChapterLists` instance. The `book.Index` is used as a key.
    -   If `chapterList` is `null` (meaning the book has no structural markup), the dropdown `tscbChapterList` is hidden (L76).
    -   If a `chapterList` exists, it iterates through the `DivTag` objects to set their `BookScript` property (L80-83). This ensures the chapter titles are correctly converted for display.
    -   `tscbChapterList.Items.AddRange(chapterList.ToArray());` (L85): Populates the dropdown with the chapter list.
    -   The `SelectedIndex` is set to 0, and a flag `ignoreChapterListChanged` is set to `true` to prevent the selection change event from firing during initialization (L86-88).

- **`CalculatePageStatus()` method (L488-511):**
    -   This method is called periodically by a `Timer` to update the UI based on the current scroll position.
    -   `divId = FindScrollTopDivId(docPos);` (L491): Determines the ID of the `<div>` element currently at the top of the visible area.
    -   It then searches for the `DivTag` with the matching `Id` in the `tscbChapterList.Items` collection (L494-499).
    -   If the found index is different from the currently selected index, it updates the `tscbChapterList.SelectedIndex` (L502-505). The `ignoreChapterListChanged` flag is used here to prevent recursive navigation.

- **`tscbChapterList_SelectedIndexChanged()` event handler (L1288-1304):**
    -   This event fires when the user selects an item from the dropdown.
    -   It checks the `ignoreChapterListChanged` flag. If `true`, it does nothing and resets the flag. This prevents the navigation logic from executing when the selection is changed programmatically due to scrolling.
    -   `DivTag divTag = (DivTag)tscbChapterList.SelectedItem;` (L1298): Gets the selected `DivTag` object.
    -   `HtmlElement target = divDict[divTag.Id];` (L1299): Looks up the corresponding `HtmlElement` in the `divDict` dictionary (which was populated in `webBrowser_DocumentCompleted`).
    -   `target.ScrollIntoView(true);` (L1301): Scrolls the browser view to the target element.

- **`ReloadTSCBItems()` method (L1010-1027):**
    -   Called when the user changes the script for the book.
    -   It re-populates the chapter list dropdown, ensuring that the `ToString()` method of each `DivTag` will now use the new script for text conversion.

### 2. `src/Cst4/ChapterLists.cs`

This class is responsible for generating, caching, and providing access to the chapter lists for all books.

- **`Inst` property (L33-50):**
    -   Implements the singleton pattern. It loads the chapter lists from a serialized file (`chplists.dat`) if it exists, or creates a new instance.

- **`Generate(List<int> changedFiles)` method (L60-120):**
    -   This static method is the core of the data generation for the feature. It is called when the search index is being created or updated.
    -   It iterates through books that have changed.
    -   `book.ChapterListTypes` (L66): It checks the `Book` object for a comma-separated list of `div` types that should be included in the chapter list (e.g., "sutta, vagga").
    -   It reads the book's XML file (`Config.Inst.XmlDirectory + ...`) (L75).
    -   `xml.GetElementsByTagName("div")` (L83): It finds all `<div>` elements in the XML.
    -   It filters the `<div>` elements based on the `type` attribute matching one of the `chapterListTypes` (L88-91).
    -   For each matching `div`, it extracts the `id` attribute and the inner text of the `<head>` element (L94-101).
    -   `heading = Regex.Replace(heading, "<note>(.+?)</note>", "");` (L104): Strips footnote tags from the heading.
    -   `int indentLevel = CountUnderscores(id);` (L108): Calculates the indentation level by counting underscores in the `id` (e.g., `dn1_1` is level 1, `dn1_1_1` is level 2).
    -   `divTags.Add(new DivTag(id, "".PadRight(indentLevel * 3) + heading));` (L109): Creates a new `DivTag` object with the indented heading and adds it to a list.
    -   Finally, the generated list of `DivTag` objects is stored in the `chpListArray` at the book's index (L114).

- **`Serialize()` and `Deserialize()` methods (L130-175):**
    -   Handle saving and loading the `ChapterLists` object to/from the `chplists.dat` binary file for caching.

### 3. `src/Cst4/DivTag.cs`

A simple data class to hold information about a single chapter entry.

- **Properties:**
    -   `Id` (string): The unique identifier of the `div` (e.g., "dn1_9"). This is used as the key for navigation.
    -   `Heading` (string): The original Devanagari heading text, with indentation spaces.
    -   `BookScript` (Script): The target script for display. This is set by `FormBookDisplay`.

- **`ToString()` method (L14-20):**
    -   Overrides the default `ToString()` method.
    -   `return ScriptConverter.Convert(Heading, Script.Devanagari, bookScript, true);` (L16-19): This is crucial. When the `DivTag` object is added to the `ToolStripComboBox`, this method is called to get the display text. It converts the stored Devanagari `Heading` into the `BookScript` that the user has selected for the current book window.

---

## Data Flow Summary

1.  **Indexing Time**: `ChapterLists.Generate()` is called. It parses the XML files for books with defined `ChapterListTypes`. It extracts `<div>` tags, creates `DivTag` objects with indented headings, and stores them.
2.  **Serialization**: The entire `ChapterLists` object, containing lists of `DivTag`s for all applicable books, is serialized to `chplists.dat`.
3.  **Application Startup**: `ChapterLists.Deserialize()` loads the cached data.
4.  **Book Opening**: `FormBookDisplay.Init()` retrieves the specific `List<DivTag>` for the opened book from `ChapterLists.Inst`.
5.  **Display**: The `DivTag` objects are added to the `tscbChapterList` dropdown. The `DivTag.ToString()` method ensures the chapter titles are displayed in the correct script.
6.  **Interaction**: The user interacts with the dropdown, or scrolls the content, triggering the event handlers in `FormBookDisplay.cs` to sync the view and the dropdown's state.
