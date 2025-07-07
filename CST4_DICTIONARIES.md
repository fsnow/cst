# CST4 Dictionary Feature

This document details the implementation of the Pāli-to-English and Pāli-to-Hindi dictionary feature in the CST4 application, accessible via the "Dictionary" window (`FormDictionary`).

## 1. Feature Overview

The dictionary provides users with a simple and fast way to look up Pāli words and see their definitions in either English or Hindi. The feature is designed to be responsive, searching for matching words as the user types and displaying results in a clean, readable format. It also supports navigating between related terms via `<see>` links.

## 2. Core Components

-   **`FormDictionary.cs`**: The main form that encapsulates all UI and logic for the dictionary feature.
-   **Dictionary Data Files**: The dictionary definitions are not stored in a database but are loaded from simple text files located in the `Reference` directory.
    -   **English**: Multiple files located in the `Reference/en-dict/` directory. Each file contains pairs of lines: the first line is the Pāli word, and the second is its HTML-formatted English definition.
    -   **Hindi**: A single file, `pali-hindi.dat`, located in the `Reference/` directory. It follows the same two-line pair format: Pāli word on one line, Hindi definition on the next.
-   **`DictionaryWord.cs`**: A simple class that holds a Pāli word and its corresponding definition string.
-   **`Any2Ipe.cs`**: A crucial helper class that converts Pāli text from any script into a standardized internal representation (IPE), which is a variant of the International Alphabet of Sanskrit Transliteration (IAST). This ensures that searches work regardless of the script the user types in.

## 3. The Dictionary Workflow

The entire process is managed within `FormDictionary.cs`.

### Step 1: On-Demand Data Loading

The dictionary data is not loaded into memory until the user selects a definition language for the first time.

-   **Trigger**: The `cbDefinitionLanguage_SelectedIndexChanged` event handler, or the first time a search is performed with a given language selected.
-   **`LoadEnglishDictionary()`**:
    1.  Reads every file in the `Reference/en-dict/` directory.
    2.  Iterates through the files line-by-line, reading Pāli words and their HTML definitions in pairs.
    3.  It converts every Pāli word to the internal IPE script using `Any2Ipe.Convert()`.
    4.  If a word already exists in the dictionary (from a different source file), it appends the new definition to the existing one, separated by an `<hr/>` tag.
    5.  The words are stored in a `List<DictionaryWord>` and then sorted alphabetically. This sorted list is crucial for the efficient binary search lookup.
-   **`LoadHindiDictionary()`**:
    1.  Reads the single `Reference/pali-hindi.dat` file.
    2.  Follows a similar process of reading word/definition pairs, converting the Pāli word to IPE, and storing them in a sorted `List<DictionaryWord>`.

### Step 2: The Search Process

The search is triggered by the `txtWord_TextChanged` event, which calls the `Search()` method.

1.  **Normalization**: The user's input text is also converted to the IPE script using `Any2Ipe.Convert()`. This ensures the user's input can be matched against the IPE-normalized dictionary words.
2.  **Binary Search**: The code performs a `BinarySearch` on the sorted `List<DictionaryWord>` for an exact match.
3.  **Result Population**:
    -   If an exact match is found, it's added to the `lbWords` list box. The code then continues to scan forward in the list, adding all subsequent words that *start with* the search term.
    -   If no exact match is found, the `BinarySearch` returns the bitwise complement of the index where the word *would* be inserted. The code uses this index to find the closest matching words (both before and after the insertion point) by comparing how many initial characters they have in common with the search term. This provides "best guess" results even if the user misspells a word.
4.  **Display**: The `lbWords` list box is populated with the matching `DictionaryWord` objects.

### Step 3: Displaying the Definition

1.  **Selection**: When the user selects a word in the `lbWords` list box, the `lbWords_SelectedIndexChanged` event is triggered.
2.  **HTML Rendering**: The `DisplayMeaning()` method is called. It takes the HTML string from the selected `DictionaryWord.Meaning` property.
3.  **Link Generation**: It uses a regular expression to find any `<see>word</see>` tags in the definition and converts them into clickable HTML `<a>` links that call a JavaScript function (`window.external.SeeAlso(...)`).
4.  **Styling**: It injects a `<style>` block into the HTML to set the correct font and size for the selected language (Tahoma for English, CDAC-GISTSurekh for Hindi).
5.  **Rendering**: The final HTML string is assigned to the `wbMeaning.DocumentText` property, which renders it in the `WebBrowser` control.

### "See Also" Navigation

The dictionary supports navigating between entries.

-   When a user clicks a "See Also" link, the `SeeAlso(string word)` method is called from the JavaScript in the `WebBrowser` control.
-   This method pushes the current search state onto a `backStack` and programmatically changes the `txtWord.Text` to the new word, triggering a new search.
-   A "Back to..." link is displayed in the definition, which calls `SeeAlsoBack()`, popping the previous state from the stack and restoring it.
