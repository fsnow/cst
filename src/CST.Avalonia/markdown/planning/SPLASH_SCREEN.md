# PixelViewer Splash Screen Analysis

## Findings

The splash screen in PixelViewer is not implemented using a separate splash screen window as we attempted previously. Instead, it leverages the `CarinaStudio.AppSuite` library, which provides a base class `AppSuiteApplication`. This base class handles the splash screen functionality automatically.

### Key Implementation Details

1.  **`AppSuiteApplication` Base Class**: The `App` class in PixelViewer inherits from `CarinaStudio.AppSuite.AppSuiteApplication`. This base class contains the core logic for displaying and managing the splash screen.

2.  **`OnPrepareSplashWindow` Method**: The splash screen's appearance is customized by overriding the `OnPrepareSplashWindow` method. This method allows setting parameters like `AccentColor` and `BackgroundImageOpacity`.

    ```csharp
    protected override SplashWindowParams OnPrepareSplashWindow() => base.OnPrepareSplashWindow().Also((ref SplashWindowParams it) =>
    {
        it.AccentColor = Avalonia.Media.Color.FromArgb(0xff, 0x50, 0xb2, 0x9b);
        it.BackgroundImageOpacity = 0.75;
    });
    ```

3.  **`OnPrepareStartingAsync` Method**: The splash screen is updated with progress and messages during the application's startup sequence by calling `UpdateSplashWindowProgress` and `UpdateSplashWindowMessage` from within the `OnPrepareStartingAsync` method.

    ```csharp
    protected override async Task OnPrepareStartingAsync()
    {
        // ...
        this.UpdateSplashWindowProgress(0.1);
        // ...
        this.UpdateSplashWindowMessage(this.GetStringNonNull("App.InitializingColorSpaces"));
        // ...
    }
    ```

4.  **No `Program.cs`**: The application's entry point is a `Main` method within the `App` class itself, which calls `BuildApplicationAndStart<App>(args)`.

## Conclusion

The key to PixelViewer's splash screen implementation is the `CarinaStudio.AppSuite` library. It provides a framework for handling the splash screen in a way that is integrated with the application's lifecycle.

## Next Steps

To implement a similar splash screen in CST.Avalonia, we have two options:

1.  **Incorporate `CarinaStudio.AppSuite`**: We could add a dependency on the `CarinaStudio.AppSuite` library and refactor our `App` class to inherit from `AppSuiteApplication`. This would likely be the quickest way to get a working splash screen, but it would also introduce a significant third-party dependency.

2.  **Replicate the functionality**: We could study the `CarinaStudio.AppSuite` source code to understand how the splash screen is implemented and then replicate that functionality within our own codebase. This would give us more control over the implementation and avoid the external dependency, but it would also be more work.

Given the complexity of correctly implementing a splash screen on macOS, the recommended approach is to **incorporate the `CarinaStudio.AppSuite` library**. We can then leverage its splash screen functionality and adapt it to our needs.

## Concrete Implementation Details

After a thorough analysis of the `AppSuiteBase` source code, the splash screen implementation can be summarized as follows:

1.  **Custom Application Lifecycle**: The `AppSuiteApplication` class forgoes the standard `IClassicDesktopStyleApplicationLifetime` and manages its own application lifecycle. This is the cornerstone of the entire implementation.

2.  **`Start()` Method**: The application startup is orchestrated in the `Start(string[] args)` method:
    *   A `SplashWindowImpl` instance is created.
    *   The virtual `OnPrepareSplashWindow()` is called, allowing subclasses to customize the splash screen's appearance and properties.
    *   The splash window is shown with `splashWindow.Show()`. It is **not** run as the main application window (`Application.Run()` is not called on it).
    *   A background task is immediately started using `Task.Run()` to perform the main application initialization (`OnPrepareStartingAsync()`). This prevents the UI thread from blocking.
    *   While the background task runs, the splash screen is visible and can report progress.

3.  **Transition to Main Window**:
    *   Once `OnPrepareStartingAsync()` completes, the main window is created via `OnCreateMainWindow()`.
    *   The main window is then shown.
    *   Finally, the `splashWindow.Close()` method is called to hide the splash screen.

4.  **`SplashWindowImpl`**:
    *   This is a custom `Window` class with its own XAML for the UI.
    *   It contains platform-specific code, especially for positioning the window correctly on the screen on both Windows and macOS, accounting for screen scaling.
    *   It includes methods like `Progress` and `Message` that are called from the main application thread to update the UI.
    *   It uses a `DoubleRenderingAnimator` to smoothly animate the progress bar.

### Key Takeaway

The core concept is to **decouple the splash screen from the main application window's lifecycle**. The splash screen is treated as a temporary, independent window that is displayed while the main application initializes asynchronously in the background. This avoids the issues we previously faced with trying to show a splash screen within the confines of the standard Avalonia application lifecycle.
