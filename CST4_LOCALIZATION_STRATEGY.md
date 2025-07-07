# CST4 Localization Strategy

This document details the localization mechanism used in the CST4 WinForms application, which allows for dynamic, runtime language switching.

## 1. Core Technology

The localization system is built entirely on the standard .NET Framework infrastructure for WinForms applications.

-   **.resx Files**: Every localizable string, layout property, and even icon is stored in `.resx` (resource) files.
-   **Satellite Assemblies**: For each language (e.g., `de`, `es`, `zh-CHS`), Visual Studio compiles the corresponding `.resx` files into a separate satellite assembly (`.resources.dll`). These are placed in subdirectories named after the culture code (e.g., `de/Cst4.resources.dll`).
-   **`System.Resources.ResourceManager`**: This .NET class is responsible for automatically detecting the current UI culture and loading the appropriate satellite assembly to retrieve localized resources.

## 2. The Implementation: A Reflection-Based Approach

The key to the **runtime** language switching is the `FormLanguageSwitchSingleton` class, found in `FormLanguageSwitch.cs`. This class implements a robust, if brute-force, method for applying a new language to the entire application without requiring a restart.

### How It Works:

1.  **Initiation**: The process begins in `FormMain.cs` when the user selects a new language from the `tscbInterfaceLanguage` dropdown. This triggers the `tscbInterfaceLanguage_SelectedIndexChanged` event handler.

2.  **Setting the Culture**: The event handler first tells .NET which language to use for all future resource lookups:
    ```csharp
    // In FormMain.cs
    var cultureInfo = ((CultureInfoDisplayItem)tscbInterfaceLanguage.SelectedItem).CultureInfo;
    FormLanguageSwitchSingleton.Instance.ChangeCurrentThreadUICulture(cultureInfo);
    ```
    This sets `Thread.CurrentThread.CurrentUICulture`, which is the standard way to control localization in .NET.

3.  **Triggering the UI Refresh**: Next, it calls the singleton to begin the UI update process, starting with the main form:
    ```csharp
    // In FormMain.cs
    FormLanguageSwitchSingleton.Instance.ChangeLanguage(this);
    ```

4.  **Recursive Traversal**: The `ChangeLanguage` method in the singleton is where the core logic resides. It performs the following steps:
    -   It iterates through the main form (`FormMain`) and all of its open MDI child windows.
    -   For each form, it creates a `ResourceManager` instance specific to that form's type (e.g., `new ResourceManager(typeof(FormBookDisplay))`).
    -   It then calls `RecurControls`, a method that walks the entire control tree of the form (panels, buttons, text boxes, etc.).

5.  **Applying Resources with Reflection**: For each control it encounters, the singleton uses **.NET Reflection** to dynamically read and apply every localizable property from the `.resx` files.
    -   It gets a list of all properties for a control (e.g., `Text`, `Size`, `Font`, `RightToLeft`, `ToolTipText`).
    -   It constructs the resource key name (e.g., `buttonOK.Text`, `labelName.Font`).
    -   It uses the `ResourceManager` to look up this key in the newly selected language's resource file.
    -   If a value is found, it uses reflection (`propertyInfo.SetValue(...)`) to apply the localized value directly to the control.

6.  **Handling Non-Standard Controls**: The system also includes special logic in `ScanNonControls` to handle items that are not in the standard `Controls` collection, such as `MenuStrip` items, `ToolStrip` buttons, and `ListView` column headers.

### Diagram of the Process

```
[User selects "Deutsch"]
       |
       v
[tscbInterfaceLanguage_SelectedIndexChanged event]
       |
       +---> [Thread.CurrentThread.CurrentUICulture = "de-DE"]
       |
       +---> [FormLanguageSwitchSingleton.Instance.ChangeLanguage(this)]
                   |
                   v
             [For each MDI child form...]
                   |
                   v
             [RecurControls(form, ...)]
                   |
                   v
             [For each control on the form...]
                   |
                   v
             [Use Reflection to get properties (Text, Size, etc.)]
                   |
                   v
             [ResourceManager looks up "controlName.Text" in Cst4.de.resx]
                   |
                   v
             [Use Reflection to set control.Text = "gefundenen Wert"]
```

## 3. Key Characteristics

-   **Comprehensive**: The reflection-based approach is extremely thorough. It attempts to reload dozens of properties for every single control, ensuring that everything from text labels to layout direction (`RightToLeft`) is updated.
-   **Decentralized Resources**: Each form manages its own set of `.resx` files. This is a standard Visual Studio pattern but can make it tedious to find and update strings, as they are scattered across many files.
-   **Maintainability**: While powerful, this system can be brittle. If a control is renamed in the designer, the connection to its resources can be broken. Adding new localizable properties requires modifying the `FormLanguageSwitchSingleton` class.
-   **Performance**: The use of reflection is inherently slower than direct code, but for a UI update that happens only on user action, the performance is perfectly acceptable. The user perceives a brief pause as the UI redraws.

## 4. How to Add/Modify Localizations

1.  **Open the Form in the Designer**: The easiest way to edit resources is to open the target form (e.g., `FormSearch.cs`) in the Visual Studio designer.
2.  **Change the `Language` Property**: In the form's properties pane, change the `Language` property from `(Default)` to the desired language (e.g., `German (Germany)`).
3.  **Edit Controls**: You can now directly edit the `Text`, `Size`, and other properties of the controls on the form. Your changes will be saved to the corresponding `.resx` file (e.g., `FormSearch.de.resx`).
4.  **Rebuild**: Rebuilding the project will create the necessary satellite assembly.
