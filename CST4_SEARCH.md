# CST4 Search and Highlighting

This document details the unique search architecture of the CST4 application. Unlike traditional search engines that rank results by relevance, CST4 uses Lucene.NET primarily as a **positional store**. This allows the application to perform highly specific, deterministic searches (like multi-word proximity) and then reconstruct the exact positions of all hits for highlighting.

## 1. The Index: Storing Positions, Not Just Text

The foundation of the search feature is laid in `BookIndexer.cs`. The critical decision here is how the text is indexed:

```csharp
// In BookIndexer.cs
doc.Add(new Field("text", deva, Field.Store.NO, Field.Index.TOKENIZED, Field.TermVector.WITH_POSITIONS_OFFSETS));
```

-   **`Field.Store.NO`**: The original text is **not** stored in the Lucene index. This saves a significant amount of disk space. The application always retrieves the original XML from disk when displaying a book.
-   **`Field.Index.TOKENIZED`**: The text is analyzed and broken down into individual words (tokens).
-   **`Field.TermVector.WITH_POSITIONS_OFFSETS`**: This is the most important setting. It commands Lucene to store a "term vector" for the `text` field. This is essentially a map for each document that lists every unique word and an array of every single position (and character offset) where that word appears.

This term vector is the raw data that enables all of the application's advanced, position-based search logic.

## 2. The Search Form: Building the Query

All search logic is initiated from `FormSearch.cs`. The application does not use Lucene's built-in `QueryParser`. Instead, it manually builds queries and retrieves positional data to satisfy its specific requirements.

### Single-Word Search (Wildcard & Regex)

1.  **User Input**: The user enters a term. They can select "Wildcard" or "Regular Expression" search.
2.  **Term Enumeration**: The application iterates through the entire Lucene term dictionary (`IndexReader.Terms()`).
3.  **Matching**: For each term in the dictionary, a `TermMatchEvaluator` class checks if it matches the user's wildcard or regex pattern.
4.  **Positional Retrieval**: For every matching term, the application retrieves its full term vector, which includes the list of all documents it appears in and the exact positions of every occurrence within each document.
5.  **Aggregation**: The results are aggregated into `MatchingWord` and `MatchingWordBook` objects, which store the words, the books they are in, and the total count of occurrences.

### Multi-Word and Proximity Search

This is where the power of the positional index is most evident.

1.  **User Input**: The user enters multiple words separated by spaces.
    -   If the query is enclosed in quotes (`"word1 word2"`), it's a **phrase search**.
    -   Otherwise, it's a **proximity search**, using the "Context Distance" value from the UI.
2.  **Positional Retrieval for Each Term**: The application first performs the single-word search logic for *each* term in the query, retrieving the complete positional data for all of them.
3.  **Finding Co-occurrences**: The core logic in `Search.GetMatchingTermsWithContext` then processes this data. For each book that contains *all* of the search terms, it gets the arrays of positions for each term.
4.  **Proximity Check**: It then iterates through these position arrays, looking for places where all the terms appear within the specified "Context Distance" (or directly adjacent for a phrase search).
5.  **Result Generation**: When a valid co-occurrence is found, a `WordPosition` object is created that stores the character offsets of all the words in the found phrase. These positions are the key to highlighting.

## 3. Hit Highlighting in the Book Display

When a user opens a book from the search results list, the `FormBookDisplay` is responsible for showing the highlighted hits.

1.  **Data Transfer**: The list of search terms (for single-word search) or the list of `WordPosition` objects (for multi-word search) is passed to the `FormBookDisplay` constructor.
2.  **Pre-Render Processing**: Before the book's XML is transformed into HTML, the `OpenBook()` method calls one of two helper methods:
    -   `Search.HighlightTerms()`: For single-word searches.
    -   `Search.HighlightMultiWordTerms()`: For multi-word searches.
3.  **Injecting `<hi>` Tags**: These methods take the raw XML content of the book as a string. They use the precise character offsets (`WordPosition.Start` and `WordPosition.End`) retrieved from the search to inject `<hi>` (highlight) tags directly into the XML string around the matching words.
    -   Example: `<p>... word1 word2 ...</p>` becomes `<p>... <hi>word1</hi> <hi>word2</hi> ...</p>`
4.  **XSL Transformation**: The modified XML, now containing the `<hi>` tags, is then passed to the XSLT engine. The XSL stylesheet (`tipitaka-*.xsl`) has a template that matches the `<hi>` tag and transforms it into an HTML `<span>` with a specific class and a unique ID.
    -   Example: `<hi>` becomes `<span class="hit" id="hit0">...</span>`
5.  **Display and Navigation**: The final HTML is loaded into the `WebBrowser` control. The user can then use the "First", "Previous", "Next", and "Last" buttons on the toolbar, which simply call JavaScript to scroll the element with the corresponding ID (`hit0`, `hit1`, etc.) into view.

This entire process, from indexing with positional data to injecting tags before rendering, allows CST4 to bypass traditional relevance-based search and implement a powerful, deterministic, and highly specific search and display system.
