# CST4 Mūla, Aṭṭhakathā, and Tīkā Navigation Buttons

This document details the functionality of the "Mūla", "Aṭṭhakathā", and "Tīkā" buttons located on the toolbar of the `FormBookDisplay` window in the CST4 application. These buttons allow users to quickly navigate between a text and its corresponding commentaries or sub-commentaries.

## Feature Overview

In the Pāli Canon, texts are often layered:
-   **Mūla**: The root text or scripture.
-   **Aṭṭhakathā**: The commentary on the Mūla text.
-   **Tīkā**: The sub-commentary on the Aṭṭhakathā.

The CST4 application models these relationships, and the toolbar buttons provide a direct way to open the linked text while attempting to maintain the user's reading position.

-   **UI Components**: The feature is exposed through three `ToolStripButton` controls: `tsbMula`, `tsbAtthakatha`, and `tsbTika`.
-   **Visibility and State**: The visibility and enabled state of these buttons are determined by the metadata of the currently displayed book. If a book does not have a corresponding commentary (e.g., no Aṭṭhakathā), that button will be disabled or hidden.
-   **Position Syncing**: When a user clicks one of these buttons, the application opens the linked book in a new window and automatically scrolls to the paragraph that corresponds to the user's current reading position.

---

## File Interactions

### 1. `src/Cst4/FormBookDisplay.cs` (UI and Event Handling)

This form is the primary location for the feature's logic.

-   **`Init()` method (L110-122):**
    -   This method, called when the form is initialized, is responsible for setting the initial state of the Mūla, Aṭṭhakathā, and Tīkā buttons.
    -   `if (book.MulaIndex < 0 && book.AtthakathaIndex < 0 && book.TikaIndex < 0)` (L112): It checks if the current `Book` object has any defined links. If all three index properties (`MulaIndex`, `AtthakathaIndex`, `TikaIndex`) are -1, it means there are no associated texts, and the buttons are hidden.
    -   `tsbMula.Enabled = (book.MulaIndex >= 0);` (L119): If links exist, each button's `Enabled` property is set based on whether its corresponding index is valid (>= 0).

-   **Button Click Event Handlers (L1348-1363):**
    -   `tsbMula_Click()`: Calls `OpenLinkedBook(CommentaryLevel.Mula);`
    -   `tsbAtthakatha_Click()`: Calls `OpenLinkedBook(CommentaryLevel.Atthakatha);`
    -   `tsbTika_Click()`: Calls `OpenLinkedBook(CommentaryLevel.Tika);`
    -   These handlers are simple wrappers that pass the appropriate `CommentaryLevel` enum to the core `OpenLinkedBook` method.

-   **`OpenLinkedBook(CommentaryLevel linkedBookType)` method (L789-923):**
    -   This is the central method for the feature.
    -   **Step 1: Identify the Target Book**:
        -   It uses the `linkedBookType` parameter to determine which index to use from the current `book` object.
        -   `linkedBook = Books.Inst[book.MulaIndex];` (L792): It retrieves the corresponding `Book` object from the master `Books.Inst` collection using the stored index (`MulaIndex`, `AtthakathaIndex`, or `TikaIndex`).
    -   **Step 2: Determine the Navigation Anchor**:
        -   The primary goal is to find the correct paragraph anchor to navigate to in the new book. This is complex because of different book structures.
        -   `GetPara()` and `GetParaWithBook()`: It calls these helper methods to get the name of the current paragraph anchor at the top of the user's screen. `GetPara()` is used for simple books, while `GetParaWithBook()` is used for "Multi" type books (which combine several smaller texts into one file) and returns an anchor with a book code suffix (e.g., "para345_an4").
    -   **Step 3: Handle Complex Book-to-Book Mappings**:
        -   The logic contains several `if/else if` blocks to handle the various `BookType` combinations (`Whole`, `Multi`, `Split`).
        -   For example, when navigating from a `Multi` book (like Anguttara Nikāya 2, 3, and 4 combined) to a `Whole` book (the corresponding Mūla text), it needs to parse the book code from the anchor (`GetBookCode()`) to select the correct target Mūla book (L851-870).
    -   **Step 4: Open the New Book Window**:
        -   `((FormMain)MdiParent).BookDisplay(linkedBook, anchor);`: Finally, it calls the `BookDisplay` method on the main MDI parent form, passing the target `Book` object and the determined navigation anchor string.

-   **Position Helper Methods (`GetPara`, `GetParaWithBook`, `FindScrollTopElementName`):**
    -   These methods determine the current reading position by checking the scroll position of the `WebBrowser` control (`webBrowser.Document.Body.ScrollRectangle.Y`).
    -   They iterate through pre-cached lists of paragraph anchor elements (`vParas`, `vParasWithBook`) to find the element closest to the top of the viewport.

### 2. `src/Cst/Book.cs` (Data Model)

This class defines the data structure for a single book and holds the critical linking information.

-   **Properties:**
    -   `MulaIndex` (int): Stores the index in the global `Books` array for the corresponding Mūla text. It is -1 if no link exists.
    -   `AtthakathaIndex` (int): Stores the index for the Aṭṭhakathā.
    -   `TikaIndex` (int): Stores the index for the Tīkā.
    -   `BookType` (enum: `Whole`, `Multi`, `Split`, `Unknown`): An important enum that defines the structural type of the book, which dictates the logic needed in `OpenLinkedBook` to resolve the correct target book and anchor.

### 3. `src/Cst4/FormBookDisplay.Designer.cs` (UI Control Definitions)

This file auto-generated by the WinForms designer defines the toolbar buttons.

-   **`tsbMula`, `tsbAtthakatha`, `tsbTika` (L158, L165, L172):** These are the `System.Windows.Forms.ToolStripButton` controls. Their properties (like `DisplayStyle`, `Text`, `Margin`) and their `Click` event handler assignments (`+= new System.EventHandler(...)`) are defined here.

---

## Data and Control Flow Summary

1.  When a `FormBookDisplay` window is created, its `Init()` method reads the `MulaIndex`, `AtthakathaIndex`, and `TikaIndex` from the `Book` object.
2.  It enables or disables the `tsbMula`, `tsbAtthakatha`, and `tsbTika` buttons based on whether these indices are valid (>= 0).
3.  The user clicks one of the enabled buttons (e.g., `tsbAtthakatha`).
4.  The corresponding click event handler (e.g., `tsbAtthakatha_Click`) is triggered.
5.  The handler calls `OpenLinkedBook`, passing the appropriate `CommentaryLevel` enum (`Atthakatha`).
6.  `OpenLinkedBook` uses the current book's `AtthakathaIndex` to look up the target `Book` object in the main `Books` collection.
7.  It determines the user's current paragraph position by analyzing the web browser's scroll position.
8.  It performs complex logic based on the `BookType` of the source and destination books to find the correct target book and paragraph anchor.
9.  It calls `FormMain.BookDisplay()` to open the target book in a new window, instructing it to scroll to the calculated anchor.
