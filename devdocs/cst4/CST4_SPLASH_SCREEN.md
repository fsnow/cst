# CST4 Splash Screen Feature

This document details the implementation of the splash screen feature in the CST4 application. The splash screen is displayed during application startup to provide visual feedback while initial resources are being loaded.

## Feature Overview

The splash screen is a custom `System.Windows.Forms.Form` that is displayed in a separate thread before the main application window (`FormMain`) is created and run. It features:

-   **Threaded Display**: Runs on its own UI thread to remain responsive while the main thread is busy.
-   **Fade Effects**: Fades in at the beginning and fades out when closed.
-   **Status Updates**: Displays status messages about the loading process (e.g., "Loading books...").
-   **Progress Bar**: Shows a smoothed, self-calibrating progress bar to indicate loading progress.
-   **Time Remaining**: Estimates and displays the time remaining until the application is ready.
-   **Self-Calibration**: On subsequent launches, it uses timing data stored in the registry from the previous launch to provide a more accurate progress bar and time-remaining estimate.
-   **Dual Use**: The same form is also used for the "Help | About" dialog, but with the progress and status elements hidden.

---

## File Interactions

### 1. `src/Cst4/Program.cs` (The Entry Point)

This is where the application execution begins.

-   **`Main()` method (L13-33):**
    -   `SplashScreen.ShowSplashScreen();` (L18): This static method is the first significant call. It creates and starts a new background thread to show the splash screen form.
    -   `SplashScreen.SetStatus(" ");` (L19): Sets the initial status text on the splash screen.
    -   `Application.Run(new FormMain());` (L22): This starts the main application message loop with `FormMain`. The splash screen remains visible during the `FormMain` constructor and its `Load` event, where the bulk of the initialization occurs. `FormMain` is responsible for closing the splash screen when it's ready.

### 2. `src/Cst4/SplashScreen.cs` (The Core Implementation)

This file contains the logic for the splash screen form itself.

-   **Static Methods (L193-263):**
    -   `ShowSplashScreen()`: Creates a new `Thread` (`ms_oThread`), sets it to be a background thread with STA (Single-Threaded Apartment) state, and starts it. The thread executes the `ShowForm` method.
    -   `ShowForm()`: Creates an instance of the `SplashScreen` form (`ms_frmSplash`) and runs it using `Application.Run()`, giving it its own message loop.
    -   `CloseForm()`: Called by `FormMain` when loading is complete. It initiates the fade-out effect by setting a negative opacity increment and then closes the form.
    -   `SetStatus(string newStatus)`: A static method that allows the main application thread to update the status label (`lblStatus`) on the splash screen thread.
    -   `SetReferencePoint()`: Called by the main thread at various points during initialization. This is the key to the self-calibrating progress bar. Each call marks a checkpoint in the loading process.

-   **Instance Logic:**
    -   **Constructor `SplashScreen()` (L70-77):** Sets the form's initial opacity to `0.00` and starts a `System.Windows.Forms.Timer` (`timer1`) to handle the fade-in/fade-out animations and progress bar updates.
    -   **`timer1_Tick()` event handler (L411-448):** This is the heart of the animation logic.
        -   It increments or decrements the form's `Opacity` to create the fade effects.
        -   It smoothly increments the progress bar's value (`m_dblLastCompletionFraction`) toward the target value set by the last `SetReferencePoint()` call. This avoids a jumpy progress bar.
        -   It calculates and displays the "time remaining" text.
    -   **Self-Calibration (`SetReferenceInternal()`, `ReadIncrements()`, `StoreIncrements()`):**
        -   On the first launch, the progress bar is not shown. The time taken to reach each `SetReferencePoint()` is recorded.
        -   `StoreIncrements()`: When the splash screen closes, it calculates the percentage of the total load time that each checkpoint represents and stores these percentages in the Windows Registry. It also stores the average time per "tick" for the progress bar smoothing.
        -   `ReadIncrements()`: On subsequent launches, it reads these stored percentages from the registry to control the progress bar, making it much more representative of the actual progress.
    -   **`pnlStatus_Paint()` (L451-458):** Custom paints the progress bar using a `LinearGradientBrush` for a modern look.

### 3. `src/Cst4/SplashScreen.resx` (The Resources)

This is the resource file for the `SplashScreen` form, embedded into the assembly at compile time.

-   **`$this.BackgroundImage` (L146):** This data element contains the main visual component of the splash screen: a Base64 encoded `System.Drawing.Bitmap` object. The image is of a Dhamma wheel with text "Chaṭṭha Saṅgāyana Tipiṭaka 4.1".
-   **Control Properties**: It also stores the initial properties of the form's controls, such as the position and size of the `lblStatus` and `pnlStatus` controls.

---

## Data and Control Flow

1.  `Program.Main()` starts.
2.  `SplashScreen.ShowSplashScreen()` is called, which spawns a new thread.
3.  The new thread creates and shows the `SplashScreen` form. The form starts transparent and begins to fade in via its timer.
4.  The main thread continues its work, creating `FormMain`.
5.  During `FormMain`'s initialization, it periodically calls `SplashScreen.SetStatus()` and `SplashScreen.SetReferencePoint()` to update the splash screen's display and advance the progress bar's target.
6.  The splash screen's timer tick handler receives these updates and animates the UI smoothly.
7.  Once `FormMain` is fully loaded and ready to be displayed, it calls `SplashScreen.CloseForm()`.
8.  `SplashScreen.CloseForm()` tells the splash screen to begin its fade-out animation.
9.  The splash screen's timer decrements the opacity. When it reaches zero, the form closes, its thread ends, and the splash screen is removed from memory.
