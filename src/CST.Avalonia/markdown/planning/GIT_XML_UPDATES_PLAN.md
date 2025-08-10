# Git-Based XML File Update Strategy

**Last Updated**: August 10, 2025

## 1. Executive Summary

This document outlines a strategy for integrating an automatic update mechanism for the Tipitaka XML files directly into the CST.Avalonia application. The proposed solution avoids requiring a local `git` installation and minimizes bandwidth by using the GitHub REST API to track and download file changes.

The core idea is to leverage commit hashes to determine if updates are needed, both at the repository level and for individual files. This approach is efficient, robust, and provides a seamless experience for the end-user.

We will use the **Octokit.net** library, a standard and well-maintained GitHub API client for .NET, to handle all communication with GitHub.

## 2. Configuration Settings

To provide user control and flexibility, the following settings will be added to the application's settings model (`Settings.cs`) and exposed in the Settings Window.

-   `EnableAutomaticUpdates` (boolean): A master switch to enable or disable the entire update-checking feature. Defaults to `true`.
-   `XmlRepositoryOwner` (string): The owner of the GitHub repository. Defaults to `VipassanaTech`.
-   `XmlRepositoryName` (string): The name of the GitHub repository. Defaults to `tipitaka-xml`.

The `XmlUpdateService` will depend on `ISettingsService` to access these values.

## 3. Core Principles

- **No Local Git Dependency**: The user should not need to have `git` installed on their system.
- **Configurable & Controllable**: Users can disable the feature or point to their own repository fork.
- **Minimize Bandwidth**: Only download files that have actually changed. Avoid cloning the entire 1GB+ repository.
- **Atomic Updates**: The process should be designed to be resilient. The local state should only be updated after all files have been successfully downloaded.
- **User-Friendly**: The process should run in the background with clear notifications to the user about progress and completion.
- **Integration with Indexing**: After updating files, the application must automatically trigger the `IndexingService` to re-index the changed content.

## 4. Proposed Data Storage

We will extend the existing `file-dates.json` to become the single source of truth for the state of the local XML files.

### `file-dates.json` Structure Enhancement

The current file tracks modification dates for indexing purposes. We will augment it to also store the commit hash for each file and a single top-level commit hash for the `deva` directory. The `LastModified` field will be renamed to `LastIndexedTimestamp` for clarity.

**Example `file-dates.json`:**

```json
{
  "LastKnownRepositoryCommitHash": "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2",
  "Files": {
    "dhp.xml": {
      "LastIndexedTimestamp": "2025-08-10T10:00:00Z",
      "CommitHash": "f1e2d3c4b5a6f1e2d3c4b5a6f1e2d3c4b5a6f1e2"
    },
    "iti.xml": {
      "LastIndexedTimestamp": "2025-08-09T18:30:00Z",
      "CommitHash": "c1b2a3f4e5d6c1b2a3f4e5d6c1b2a3f4e5d6c1b2"
    }
    // ... 215 more files
  }
}
```

-   **`LastKnownRepositoryCommitHash`**: Stores the SHA of the most recent commit affecting the `deva` directory that we have successfully synced.
-   **`Files.<filename>.CommitHash`**: Stores the SHA of the blob (file content) for that specific file.
-   **`Files.<filename>.LastIndexedTimestamp`**: The timestamp of the local file when it was last indexed by Lucene.

## 5. Implementation Plan

This plan details the creation of a new service, `XmlUpdateService`, responsible for this logic.

### Step 1: Add Dependencies & Modify Settings

1.  **Add NuGet Package**: Add `Octokit.net` to `CST.Avalonia.csproj`.
    ```xml
    <PackageReference Include="Octokit" Version="12.0.0" />
    ```
2.  **Update `Settings.cs`**: Add the new properties to the `Settings` class in `Models/Settings.cs`.
3.  **Update `SettingsViewModel.cs`**: Expose the new settings properties from the view model.
4.  **Update `SettingsWindow.axaml`**: Add a `CheckBox` and two `TextBox` controls for the new settings.

### Step 2: Create `IXmlUpdateService` and `XmlUpdateService`

-   **`IXmlUpdateService.cs`**: Define the public interface for the service.
    ```csharp
    public interface IXmlUpdateService
    {
        Task CheckForUpdatesAsync();
        event Action<string> UpdateStatusChanged;
    }
    ```
-   **`XmlUpdateService.cs`**: Implement the update logic. This service will require `ISettingsService` and `IIndexingService` via dependency injection.

### Step 3: The Update Workflow (Inside `XmlUpdateService`)

1.  **Initialization & Pre-check**:
    - On application startup, the `XmlUpdateService` is initialized.
    - It first checks the `EnableAutomaticUpdates` setting. If `false`, it does nothing further.
    - It reads `file-dates.json` to load the `LastKnownRepositoryCommitHash` and file hashes into memory.

2.  **Check for Updates (`CheckForUpdatesAsync`)**:
    - This method will be called from the main application logic (e.g., on startup).

3.  **Phase 1: Check the Top-Level Commit (Fast Path)**
    - Use `Octokit.net` to fetch the latest commit for the path `deva` on the `main` branch, using the repository owner and name from `ISettingsService`.
      ```csharp
      var settings = _settingsService.LoadSettings();
      var owner = settings.XmlRepositoryOwner;
      var repo = settings.XmlRepositoryName;

      var client = new GitHubClient(new ProductHeaderValue("CST.Avalonia"));
      var commits = await client.Repository.Commit.GetAll(owner, repo, new CommitRequest { Path = "deva", Sha = "main" });
      var latestRemoteCommitHash = commits.FirstOrDefault()?.Sha;
      ```
    - **Compare Hashes**: If `latestRemoteCommitHash` matches the locally stored `LastKnownRepositoryCommitHash`, the process stops. No updates needed.

4.  **Phase 2: Check Individual File Hashes (Deeper Check)**
    - If the top-level hash has changed, get the contents of the `deva` directory from the repository.
      ```csharp
      var contents = await client.Repository.Content.GetAllContents(owner, repo, "deva");
      ```
    - Iterate through the remote `contents` and compare their `Sha` with the locally stored `CommitHash` for each file to build a list of files that need to be downloaded.

5.  **Phase 3: Download and Save Updated Files**
    - If files need downloading, notify the user.
    - Download each file's raw content to a temporary directory.
      ```csharp
      var fileContent = await client.Repository.Content.GetRawContent(owner, repo, file.Path);
      ```
    - After all downloads are successful, move the files to the final user data directory.

6.  **Phase 4: Finalize and Re-index**
    - After files are saved:
      - Update `file-dates.json`: update the `CommitHash` for each changed file and set `LastKnownRepositoryCommitHash` to the new top-level hash. **Do not modify `LastIndexedTimestamp` here.**
      - **Crucially, trigger the incremental indexing process** via `IIndexingService`. The indexing service itself is responsible for updating the `LastIndexedTimestamp` after it successfully processes a file.
      - Notify the user that the update is complete.

### Step 4: UI and Service Integration

- **Dependency Injection**: Register `IXmlUpdateService` and `XmlUpdateService` in `App.axaml.cs`.
- **Triggering the Check**: In `App.axaml.cs`, call `xmlUpdateService.CheckForUpdatesAsync()` after the main window appears.
- **Displaying Status**: The UI can subscribe to the `UpdateStatusChanged` event to show status messages.

## 6. Error Handling

The `XmlUpdateService` must handle potential issues gracefully:
- **No Internet Connection**: Catch exceptions and inform the user.
- **GitHub API Rate Limiting**: `Octokit.net` provides information on rate limits.
- **Invalid Data**: Handle cases where files are missing or `file-dates.json` is corrupt.

By following this plan, CST.Avalonia will have a modern, efficient, and user-friendly data update system that builds upon the project's existing architecture.
