# CST4 Lucene.NET Indexing Strategy

This document explains how the CST4 application builds and maintains its Lucene.NET search index at startup, including how it tracks file changes to ensure the index is always up-to-date.

## 1. Overview

The application uses a full-text search index, powered by Lucene.NET, to provide fast and powerful search capabilities across all 217 XML text files. To avoid the costly process of re-indexing all files every time the application starts, it implements an intelligent incremental update system based on file modification dates.

The entire process is orchestrated from the `FormMain` constructor during the application's startup sequence, providing status updates to the user via the splash screen.

## 2. Key Components

-   **`BookIndexer.cs`**: The core class responsible for all Lucene.NET operations, including adding, deleting, and optimizing documents in the search index.
-   **`XmlFileDates.cs`**: A helper class that serializes and deserializes a list of the last-known modification dates for each XML file. This is the "memory" of the indexing system.
-   **`FormMain.cs`**: The main form that drives the startup process. It calls the other components in the correct order.
-   **`xml.dat`**: The physical file on disk where the serialized `XmlFileDates` object is stored between application runs.

## 3. The Startup Indexing Process

The process executes in a precise order within the `FormMain` constructor.

### Step 1: Deserialize Old File Dates

Early in the startup, `XmlFileDates.Deserialize()` is called. This reads the `xml.dat` file from the disk and loads the previously saved `DateTime[]` array into the `XmlFileDates.Inst` singleton. This array contains the UTC timestamps of each XML file as they were at the end of the last application run.

If `xml.dat` does not exist (e.g., on a fresh install), this step is skipped, and the `FileDates` array will be empty (containing default `DateTime` values).

### Step 2: Validate XML Data and Detect Changes

Next, the `ValidateXmlData()` method is called. This is the heart of the change detection logic:

1.  It iterates through every `Book` object defined in the application.
2.  For each book, it gets the corresponding physical XML file's current `LastWriteTimeUtc` from the file system.
3.  It compares this current timestamp with the old timestamp loaded from `xml.dat` for that specific book index.
4.  **If `fi.LastWriteTimeUtc > lastTime`**, it means the file has been modified since the last run. The book's index is added to a `changedFiles` list, and the new, later timestamp is stored in the `XmlFileDates` singleton, overwriting the old one.

This method returns a `List<int>` containing the indexes of all books whose XML files have been created or modified.

### Step 3: Initialize the Indexer and Update the Index

The `BookIndexer` is then instantiated and its `IndexAll` method is called, passing in the `changedFiles` list.

1.  **Open Index**: The `BookIndexer` opens the Lucene index directory. If an index already exists, it first scans all existing documents to populate the `DocId` for each `Book` object. This maps a book to its internal Lucene document ID.
2.  **Delete Changed Files**: It iterates through the `changedFiles` list. For each book index in the list, it calls `indexModifier.DeleteDocument(book.DocId)`, removing the outdated entry from the Lucene index.
3.  **Index New and Un-indexed Files**: The code then iterates through all books one more time.
    -   If a book has a `DocId` of `-1` (meaning it was never indexed, or it was just deleted in the previous step), the `IndexBook` method is called.
    -   `IndexBook` reads the full content of the XML file.
    -   It creates a new Lucene `Document` and adds the content to a field named `"text"`. This field is configured to be tokenized and have term vectors with position offsets stored, which is essential for proximity searches and highlighting. The raw text is **not** stored in the index (`Field.Store.NO`) to save space.
    -   It also adds the `FileName`, `MatnField`, and `PitakaField` as stored fields for identification.
    -   The new document is added to the index via `indexModifier.AddDocument(doc)`.

### Step 4: Optimize and Save

After all indexing operations are complete, `indexModifier.Optimize()` is called to merge index segments for faster searching. Finally, when the application closes, `XmlFileDates.Serialize()` is called, saving the now-updated list of file timestamps to `xml.dat`, ready for the next run.

## 4. Diagram of the Workflow

```
[App Start]
    |
    v
[XmlFileDates.Deserialize()] -> Loads `xml.dat` into memory
    |
    v
[ValidateXmlData()]
    |
    +-> For each book:
    |   +-> Compare file system LastWriteTime with in-memory timestamp
    |   +-> If newer, add book index to `changedFiles` list & update in-memory timestamp
    |
    v
[BookIndexer.IndexAll(changedFiles)]
    |
    +-> Open Lucene Index
    |
    +-> For each book_index in `changedFiles`:
    |   +-> Delete document from Lucene index
    |
    +-> For each book in all books:
    |   +-> If book has no Lucene DocId:
    |       +-> Read XML file
    |       +-> Create Lucene Document
    |       +-> Add Document to Index
    |
    v
[Index is now up-to-date]
    |
    v
[App Close]
    |
    v
[XmlFileDates.Serialize()] -> Saves updated timestamps to `xml.dat`
```
