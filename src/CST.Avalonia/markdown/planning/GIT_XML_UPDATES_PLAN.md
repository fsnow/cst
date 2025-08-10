# Git-Based XML File Update Strategy

**Last Updated**: August 10, 2025

## 1. Executive Summary

This document outlines a strategy for integrating an automatic update mechanism for the Tipitaka XML files directly into the CST.Avalonia application. The proposed solution avoids requiring a local `git` installation and minimizes bandwidth by using the GitHub REST API to track and download file changes.

The core idea is to leverage commit hashes to determine if updates are needed, both at the repository level and for individual files. This approach is efficient, robust, and provides a seamless experience for the end-user.

We will use the **Octokit.net** library, a standard and well-maintained GitHub API client for .NET, to handle all communication with GitHub.

## 2. Core Principles

- **No Local Git Dependency**: The user should not need to have `git` installed on their system.
- **Minimize Bandwidth**: Only download files that have actually changed. Avoid cloning the entire 1GB+ repository.
- **Atomic Updates**: The process should be designed to be resilient. The local state should only be updated after all files have been successfully downloaded.
- **User-Friendly**: The process should run in the background with clear notifications to the user about progress and completion.
- **Integration with Indexing**: After updating files, the application must automatically trigger the `IndexingService` to re-index the changed content.

## 3. Proposed Data Storage

We will extend the existing `file-dates.json` to become the single source of truth for the state of the local XML files.

### `file-dates.json` Structure Enhancement

The current file tracks modification dates. We will augment it to also store the commit hash for each file and a single top-level commit hash for the `deva` directory.

**Example `file-dates.json`:**

```json
{
  "LastKnownRepositoryCommitHash": "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2",
  "Files": {
    "dhp.xml": {
      "LastModified": "2025-08-10T10:00:00Z",
      "CommitHash": "f1e2d3c4b5a6f1e2d3c4b5a6f1e2d3c4b5a6f1e2"
    },
    "iti.xml": {
      "LastModified": "2025-08-09T18:30:00Z",
      "CommitHash": "c1b2a3f4e5d6c1b2a3f4e5d6c1b2a3f4e5d6c1b2"
    }
    // ... 215 more files
  }
}
```

- **`LastKnownRepositoryCommitHash`**: Stores the SHA of the most recent commit affecting the `deva` directory that we have successfully synced.
- **`Files.<filename>.CommitHash`**: Stores the SHA of the blob (file content) for that specific file.

## 4. Implementation Plan

This plan details the creation of a new service, `XmlUpdateService`, responsible for this logic.

### Step 1: Add Dependencies

- Add the `Octokit.net` NuGet package to the `CST.Avalonia.csproj` project.

```xml
<PackageReference Include="Octokit" Version="12.0.0" />
```

### Step 2: Create `IXmlUpdateService` and `XmlUpdateService`

- **`IXmlUpdateService.cs`**: Define the public interface for the service.
  ```csharp
  public interface IXmlUpdateService
  {
      Task CheckForUpdatesAsync();
      event Action<string> UpdateStatusChanged;
  }
  ```
- **`XmlUpdateService.cs`**: Implement the update logic. This service will require `ISettingsService` and `IIndexingService` via dependency injection.

### Step 3: The Update Workflow (Inside `XmlUpdateService`)

1.  **Initialization**:
    - On application startup, the `XmlUpdateService` is initialized.
    - It reads the `file-dates.json` file to load the `LastKnownRepositoryCommitHash` and the hash for each individual file into memory.

2.  **Check for Updates (`CheckForUpdatesAsync`)**:
    - This method will be called from the main application logic, perhaps on startup or via a user-triggered "Check for Updates" button.

3.  **Phase 1: Check the Top-Level Commit (Fast Path)**
    - Use `Octokit.net` to fetch the latest commit for the path `deva` on the `main` branch of the `VipassanaTech/tipitaka-xml` repository.
      ```csharp
      var client = new GitHubClient(new ProductHeaderValue("CST.Avalonia"));
      var commits = await client.Repository.Commit.GetAll("VipassanaTech", "tipitaka-xml", new CommitRequest { Path = "deva", Sha = "main" });
      var latestRemoteCommitHash = commits.FirstOrDefault()?.Sha;
      ```
    - **Compare Hashes**: If `latestRemoteCommitHash` is null, empty, or matches the locally stored `LastKnownRepositoryCommitHash`, the process stops. No updates are needed. This will be the most common case and is extremely fast.

4.  **Phase 2: Check Individual File Hashes (Deeper Check)**
    - If the top-level hash has changed, it means *something* in the `deva` directory is different.
    - Use `Octokit.net` to get the contents of the `deva` directory.
      ```csharp
      var contents = await client.Repository.Content.GetAllContents("VipassanaTech", "tipitaka-xml", "deva");
      ```
    - This returns a list of all files, including their names and their blob `Sha` (the individual file content hash).
    - Create a list of files to update:
      - Iterate through the `contents` received from the API.
      - For each remote file, look up its entry in the local `file-dates.json` data.
      - If the `Sha` from the remote file does not match the locally stored `CommitHash`, add this file to a `List<Content> filesToDownload`.

5.  **Phase 3: Download and Save Updated Files**
    - If `filesToDownload` is empty, update `LastKnownRepositoryCommitHash` and exit. (This can happen if a change was reverted, for example).
    - If there are files to download:
      - Notify the user: "Updating X files..."
      - Iterate through the `filesToDownload` list.
      - For each file, use its `Url` or other properties to download the raw content.
        ```csharp
        var fileContent = await client.Repository.Content.GetRawContent("VipassanaTech", "tipitaka-xml", file.Path);
        // fileContent is a byte array, convert to string
        var contentString = System.Text.Encoding.UTF8.GetString(fileContent);
        ```
      - Save the `contentString` to the corresponding local XML file in the user's data directory.
      - **Important**: Download all files to a temporary location first. Only after all downloads are successful, move them to the final destination. This prevents a partial update if the user's connection drops.

6.  **Phase 4: Finalize and Re-index**
    - After all files are successfully downloaded and saved:
      - Update the `file-dates.json` file on disk:
        - For each updated file, update its `CommitHash` and `LastModified` timestamp.
        - Update the top-level `LastKnownRepositoryCommitHash` to the `latestRemoteCommitHash` fetched in Phase 1.
      - **Crucially, trigger the incremental indexing process** by calling a method on the `IIndexingService`. This will ensure the new content is searchable.
      - Notify the user: "Update complete. X files were updated."

### Step 4: UI and Service Integration

- **Dependency Injection**: Register `IXmlUpdateService` and `XmlUpdateService` in `App.axaml.cs`.
- **Triggering the Check**: In `App.axaml.cs` or a relevant ViewModel, call `xmlUpdateService.CheckForUpdatesAsync()` after the main window is shown.
- **Displaying Status**: The UI can subscribe to the `UpdateStatusChanged` event to show non-intrusive status messages to the user (e.g., in a status bar).

## 5. Error Handling

The `XmlUpdateService` must handle potential issues gracefully:
- **No Internet Connection**: Catch exceptions and inform the user.
- **GitHub API Rate Limiting**: `Octokit.net` provides information on rate limits. While unlikely to be hit by a single user, the code should be aware of it.
- **Invalid Data**: Handle cases where files are missing or `file-dates.json` is corrupt.

By following this plan, CST.Avalonia will have a modern, efficient, and user-friendly data update system that builds upon the project's existing architecture.
