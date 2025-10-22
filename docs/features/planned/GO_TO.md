# CST4 Go To Dialog Feature

This document details the implementation of the "Go To" dialog feature in the CST4 application. This feature allows users to quickly navigate to a specific paragraph or page number within the currently active book window.

## Feature Overview

The "Go To" feature provides a small dialog window where the user can specify a number and the type of reference they want to navigate to.

-   **Access Points**: The dialog can be opened in two ways:
    1.  Clicking the "Go To" button (`tsbGoto`) on the main toolbar.
    2.  Using the `Ctrl+G` keyboard shortcut from within a book display window.
-   **UI Components**: The dialog (`FormGoTo`) consists of:
    -   A set of `RadioButton` controls to select the numbering system (Paragraph, VRI Page, Myanmar Page, PTS Page, Thai Page, Other).
    -   A `TextBox` (`textBoxNumber`) for entering the desired page or paragraph number.
    -   "OK" and "Cancel" buttons.
-   **Context-Awareness**: The dialog is context-aware. It disables the radio buttons for page numbering systems that are not present in the currently active book.
-   **Functionality**: After the user enters a number, selects a reference type, and clicks "OK", the active `FormBookDisplay` window scrolls to the corresponding location.

---

## File Interactions

### 1. `src/Cst4/FormMain.cs` (Dialog Invocation)

This is the main MDI parent form, which handles the invocation of the `FormGoTo` dialog.

-   **`GoTo()` method (L684-717):**
    -   This method is the primary entry point for the feature. It is called by the toolbar button's click event (`tsbGoto_Click`, L801) and the Edit menu item.
    -   `FormBookDisplay formBookDisplay = (FormBookDisplay)ActiveMdiChild;` (L686): It first gets a reference to the currently active MDI child, ensuring it's a `FormBookDisplay`.
    -   `FormGoTo formGoTo = new FormGoTo(formBookDisplay);` (L687): It instantiates the `FormGoTo` dialog, passing the active `formBookDisplay` instance to its constructor. This reference is used by the dialog to check which page numbering systems are available.
    -   The method then manually calculates the position to center the `formGoTo` dialog over the `formBookDisplay` window (L690-693).
    -   `if (formGoTo.ShowDialog(this) == DialogResult.OK)` (L695): It displays the form as a modal dialog. The code inside the `if` block only executes if the user clicks the "OK" button.
    -   **Data Retrieval**: Inside the `if` block, it inspects the state of the `formGoTo` dialog's controls to determine what the user selected.
        -   It determines a `prefix` string ("para", "V", "M", "P", "T", "O") based on which `RadioButton` is checked (L699-714).
        -   It gets the `number` string from `formGoTo.textBoxNumber.Text` (L698).
    -   `formBookDisplay.GoToAnchor(prefix, number);` (L715): Finally, it calls the `GoToAnchor` method on the `formBookDisplay` instance, passing the determined prefix and number to perform the actual navigation.

-   **`FormMainNew_MdiChildActivate()` event handler (L984):**
    -   `tsbGoto.Enabled = (ActiveMdiChild is FormBookDisplay);`: This ensures the "Go To" toolbar button is only enabled when a book window is active.

### 2. `src/Cst4/FormGoTo.cs` (The Dialog's Logic)

This file contains the code-behind for the dialog form itself.

-   **Constructor `FormGoTo(FormBookDisplay formBookDisplay)` (L13-18):**
    -   It accepts and stores a reference to the parent `FormBookDisplay`. This reference is crucial for the dialog to know which numbering systems are available in the active book.

-   **`FormGoTo_Load()` event handler (L22-37):**
    -   This method is called when the dialog is about to be displayed.
    -   It checks the page number properties (`vPage`, `mPage`, etc.) on the stored `formBookDisplay` instance.
    -   `if (formBookDisplay.vPage == "*") radioButtonVriPage.Enabled = false;` (L27): If a page number property has the value "*", it means that numbering system is not available for the current book, and the corresponding radio button is disabled.

-   **`textBoxNumber_TextChanged()` event handler (L80-105):**
    -   This provides a small usability enhancement.
    -   `if (Regex.IsMatch(textBoxNumber.Text, "^[VvMmPpTt]"))` (L83): It checks if the user types one of the prefix letters (V, M, P, T) into the number box.
    -   If a prefix letter is typed, it automatically checks the corresponding radio button and removes the letter from the textbox, saving the user a mouse click.

### 3. `src/Cst4/FormBookDisplay.cs` (Navigation Execution)

This form receives the data from the `FormGoTo` dialog and performs the scrolling.

-   **`GoToAnchor(string prefix, string number)` method (L719-751):**
    -   This method takes the prefix and number gathered by `FormMain`.
    -   **Paragraph Navigation**: If the prefix is "para", it constructs the full anchor name (e.g., "para123"). It also handles the special case for `Multi` type books where a book code suffix is needed (e.g., "para123_an5") (L724-727). It then calls `FindPreviousAnchor` to handle cases where the exact paragraph number doesn't exist (e.g., ranges or gaps).
    -   **Page Navigation**: If the prefix is a page type ("V", "M", etc.), it constructs the full anchor name by padding the number with leading zeros (e.g., "V1.0023") and searches for a matching anchor in a loop (L736-748).
    -   `SafeScrollIntoView(goToAnchor);` (L732): Once the final anchor name is determined, it calls this helper method, which finds the HTML element by its ID and calls `element.ScrollIntoView(true)` to navigate the `WebBrowser` control.

---

## Data and Control Flow Summary

1.  User clicks the "GoTo" button or presses `Ctrl+G`.
2.  `FormMain.GoTo()` is called.
3.  A new `FormGoTo` dialog is created and given a reference to the active `FormBookDisplay`.
4.  `FormGoTo` loads, checks the available page systems from the `FormBookDisplay` reference, and disables the corresponding radio buttons if a system is unavailable.
5.  The dialog is shown to the user.
6.  The user selects a numbering system, types a number, and clicks "OK".
7.  The `GoTo()` method in `FormMain` resumes, reads the user's selections from the dialog's controls.
8.  `FormMain` calls `GoToAnchor()` on the active `FormBookDisplay`, passing the user's selections.
9.  `GoToAnchor()` constructs the appropriate HTML anchor ID and uses the `WebBrowser` control's methods to find the element and scroll it into view.