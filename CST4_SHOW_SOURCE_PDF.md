# CST4 "Show Source PDF" Feature

This document details the implementation of the feature that allows users to open a PDF source document corresponding to the Pāli text they are currently viewing.

## 1. Feature Overview

The "Show Source PDF" feature provides a direct link from a text in the application to its original scanned source document. The primary sources are two editions of the Burmese Chaṭṭha Saṅgāyana Tipiṭaka (1957 and 2010), which are available as PDF files hosted on `tipitaka.org`.

The feature is designed to calculate the precise page number in the source PDF that corresponds to the user's current scroll position in the application and open the PDF to that page in a new browser window.

## 2. How to Trigger the Feature

The feature is triggered exclusively by keyboard shortcuts from within an active "Book Display" window (`FormBookDisplay`):

-   **`Ctrl + Q`**: Opens the 1957 Burmese edition PDF.
-   **`Ctrl + E`**: Opens the 2010 Burmese edition PDF (Note: As of this writing, the data for the 2010 edition is not populated in `Sources.cs`).

These shortcuts are captured by two event handlers: `Body_KeyPress` (for when the web browser content has focus) and `FormBookDisplay_KeyDown` (as a fallback for when the form itself has focus).

## 3. Core Components

The feature is implemented across three main classes:

### `FormBookDisplay.cs`
This is the command center for the feature.
-   **Event Handlers**: The `Body_KeyPress` and `FormBookDisplay_KeyDown` methods capture the `Ctrl+Q` and `Ctrl+E` shortcuts.
-   **`ShowSource(bool is1957)` method**: This is the core logic. It is called by the event handlers and is responsible for:
    1.  Determining which `SourceType` to use (`Burmese1957` or `Burmese2010`).
    2.  Calling the `Sources` singleton to retrieve the correct `Source` object for the current book.
    3.  Calculating the correct page offset based on the current Myanmar page number displayed in the status bar (`mPage`).
    4.  Constructing the final URL with the page fragment (e.g., `.../MyBook.pdf#page=123`).
    5.  Calling the `OpenBrowser` method on the main MDI form to open the new window.

### `Sources.cs` (`CST.Core` project)
This class acts as the data repository for the feature.
-   **Singleton Design**: It uses a singleton pattern (`Sources.Inst`) for global access.
-   **Hard-coded Dictionary**: It contains a large, hard-coded `Dictionary` that maps a book's XML filename (e.g., `"s0101m.mul.xml"`) to a `Source` object.
-   **`Source` Object**: This simple data class holds the base `Url` of the PDF and an integer `PageStart`, which is the page number in the PDF that corresponds to the first page of that specific book.

### `FormBrowser.cs`
This is a simple `Form` that acts as the browser window.
-   **`WebBrowser` Control**: It contains a standard WinForms `WebBrowser` control docked to fill the window.
-   **Constructor**: Its constructor takes a single `url` string and navigates the `WebBrowser` control to it.

## 4. Execution Workflow

1.  The user is viewing a book in a `FormBookDisplay` window.
2.  The user presses `Ctrl+Q`.
3.  The `Body_KeyPress` event handler in `FormBookDisplay` captures the key combination and calls `ShowSource(true)`.
4.  `ShowSource` gets the current book's filename (e.g., `"s0101m.mul.xml"`) and the current Myanmar page number from the status bar (e.g., "5").
5.  It calls `Sources.Inst.GetSource("s0101m.mul.xml", SourceType.Burmese1957)`.
6.  The `Sources` class looks up the filename in its dictionary and returns the corresponding `Source` object, which might contain:
    -   `Url`: "https://.../Sīlakkhandhavaggapāḷi.pdf"
    -   `PageStart`: 19
7.  The `ShowSource` method calculates the final page: `finalPage = source.PageStart + (currentMPage - 1)`. For example, `19 + (5 - 1) = 23`.
8.  It constructs the final URL: `https://.../Sīlakkhandhavaggapāḷi.pdf#page=23`.
9.  It then calls the `OpenBrowser` method on the main form: `((FormMain)MdiParent).OpenBrowser(url);`.
10. A new `FormBrowser` window appears and navigates to the specified URL, displaying the PDF at the correct page.
