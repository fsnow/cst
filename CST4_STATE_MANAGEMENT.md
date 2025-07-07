# CST4 Application State Management

This document explains the mechanism by which the CST4 application saves its state upon closing and restores it upon reopening, providing a seamless user experience.

## 1. Core Concept: The `AppState` Singleton

The entire state management system revolves around a central class: `AppState`. This class is a singleton, meaning there is only ever one instance of it, accessible via `AppState.Inst`.

-   **Location**: `src/Cst4/AppState.cs`
-   **Purpose**: To act as a data container for every piece of information required to restore the application to its previous state.
-   **Serialization**: The class is marked with the `[Serializable]` attribute, which allows the .NET Framework to convert the entire object instance into a byte stream that can be saved to a file.

The `AppState` class contains properties for:
-   The main window's size, location, and state (maximized, etc.).
-   The selected interface language and Pāli script.
-   The visibility, location, size, and all input values for the **Search**, **Select Book**, and **Dictionary** windows.
-   A complete list of all open **Book Windows**, including their size, location, the book they are displaying, and their scroll position.
-   The Most Recently Used (MRU) list of opened books.

## 2. The State Management Cycle

The process is divided into two main phases: saving the state (serialization) and loading the state (deserialization).

### Saving State on Application Exit

1.  **Trigger**: When the user closes the application, the `FormMain_FormClosing` event is triggered. Inside this event handler, a call is made to `ToAppState()`.
2.  **Data Transfer**: The `ToAppState()` method in `FormMain.cs` is responsible for systematically reading the current state of the UI and writing it into the `AppState.Inst` singleton object. It meticulously records:
    -   `this.WindowState`, `this.Size`, `this.Location` from the main form.
    -   The text from `searchForm.txtSearchTerms`, the checked status of `searchForm.cbVinaya`, etc.
    -   It iterates through all open MDI child forms (`this.MdiChildren`) and, for each `FormBookDisplay`, it creates an `AppStateBookWindow` object to store its specific state.
3.  **Serialization**: After `ToAppState()` has fully populated the singleton, `AppState.Serialize()` is called. This method uses a `BinaryFormatter` to serialize the entire `AppState.Inst` object graph into a single file named `app-state.dat`.

### Loading State on Application Startup

1.  **Deserialization**: This is one of the very first actions in the `FormMain` constructor. The `AppState.Deserialize()` method is called.
    -   It checks if `app-state.dat` exists.
    -   If it does, it uses a `BinaryFormatter` to read the file and reconstruct the entire `AppState` object in memory, assigning it to the `AppState.Inst` singleton.
2.  **UI Restoration**: Later in the constructor, the `FromAppState()` method is called. This method reads the data from the now-populated `AppState.Inst` singleton and applies it to the UI. It is the reverse of the `ToAppState` method:
    -   It sets the main window's size and location.
    -   It re-creates all MDI child windows that were open, including the Search, Select Book, and Dictionary forms, restoring their positions and content.
    -   For each `AppStateBookWindow` found in the state, it creates a new `FormBookDisplay`, sets its size and location, and tells it which book to open.
    -   It restores the search terms, checkbox states, and selected items in the various forms.

## 3. Workflow Diagram

```
+------------------+     (On Close)      +------------------+
|                  |-------------------->|                  |
|   Live UI Forms  |   ToAppState()      |  AppState Object |
| (FormMain, etc.) |<--------------------|   (In Memory)    |
|                  |   FromAppState()    |                  |
+------------------+     (On Start)      +------------------+
                                                 |
                                                 | Serialize() / Deserialize()
                                                 |
                                           +--------------+
                                           |              |
                                           | app-state.dat|
                                           |  (On Disk)   |
                                           +--------------+
```

## 4. Key Characteristics

-   **Comprehensive**: The system is extremely thorough, capturing nearly every aspect of the user's session.
-   **Centralized State**: Using a single `AppState` class makes it easy to see exactly what data is being saved and to add new state properties.
-   **Binary Serialization**: The use of `BinaryFormatter` is efficient but has some drawbacks. The resulting `app-state.dat` file is not human-readable, and it can be sensitive to changes in the `AppState` class definition between application versions. If you add or remove a field from `AppState`, deserializing an older `app-state.dat` file can fail.
-   **Controller Logic**: `FormMain` acts as the central controller, orchestrating the flow of data between the live UI controls and the `AppState` data container.
