# CST Git Integration Strategy

This document outlines the recommended approach for integrating Git functionality into the CST application to manage XML data sources.

## 1. Core Requirements

The application needs to manage local copies of XML text files that are stored in remote Git repositories. The key requirements are:

1.  **Configurable Local Directory**: The user can specify a local path to store the XML files.
2.  **Automatic Updates**: If the specified directory is already a Git repository, the application should pull the latest changes.
3.  **Initial Clone**: If the directory does not exist, the application should clone a default or specified remote repository.
4.  **Multiple Repositories**: The system should be able to handle different potential source repositories.
5.  **Sparse Checkout**: For large repositories, the application must be able to clone only a specific subdirectory containing the necessary XML files, not the entire repository.

## 2. Recommended .NET Git Library: LibGit2Sharp

For most standard Git operations, **LibGit2Sharp** is the recommended library. It provides a powerful and comprehensive set of APIs for interacting with Git repositories directly from .NET.

- **Website**: [https://github.com/libgit2/libgit2sharp](https://github.com/libgit2/libgit2sharp)
- **NuGet**: `LibGit2Sharp`

### Why LibGit2Sharp?

- **Native Speed**: It's a .NET wrapper around the highly performant `libgit2` native library.
- **Full Feature Set**: It supports a wide range of operations, including cloning, pulling, branching, committing, and more.
- **Actively Maintained**: It is a mature and well-supported project.

### Example: Cloning a Repository

```csharp
using LibGit2Sharp;

public void CloneRepo(string repoUrl, string localPath)
{
    try
    {
        Repository.Clone(repoUrl, localPath);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error cloning repository: {ex.Message}");
    }
}
```

### Example: Pulling Updates

```csharp
using LibGit2Sharp;

public void PullUpdates(string repoPath)
{
    using (var repo = new Repository(repoPath))
    {
        var options = new PullOptions
        {
            FetchOptions = new FetchOptions()
        };
        var signature = new Signature("CST", "cst@example.com", DateTimeOffset.Now);
        Commands.Pull(repo, signature, options);
    }
}
```

## 3. The Challenge: Sparse Checkout

A key requirement is to support "sparse checkout," which allows cloning only a specific subdirectory from a repository. This is crucial for scenarios where the XML files are part of a much larger project.

**LibGit2Sharp does not currently have a direct API for sparse checkout.** This functionality is dependent on the underlying `libgit2` library, which has not yet implemented it.

## 4. Solution for Sparse Checkout: Calling the Git CLI

The most reliable and effective way to perform a sparse checkout is to call the `git` command-line interface (CLI) directly from the C# application. This approach requires that `git` is installed on the user's machine and is available in the system's PATH.

This can be achieved using the `System.Diagnostics.Process` class.

### Example: Sparse Checkout using the Git CLI

This example demonstrates how to initialize a repository, configure it for sparse checkout, define the target directory, and pull the files.

```csharp
using System.Diagnostics;
using System.IO;

public class GitSparseCheckout
{
    public void SparseCheckout(string repoUrl, string localPath, string directoryToCheckout)
    {
        // Ensure the target directory exists and is empty
        Directory.CreateDirectory(localPath);

        // 1. Initialize an empty git repository
        RunGitCommand(localPath, "init");

        // 2. Add the remote origin
        RunGitCommand(localPath, $"remote add origin {repoUrl}");

        // 3. Enable sparse checkout
        RunGitCommand(localPath, "config core.sparsecheckout true");

        // 4. Define the directory to fetch
        File.WriteAllText(
            Path.Combine(localPath, ".git", "info", "sparse-checkout"),
            directoryToCheckout
        );

        // 5. Pull the files from the main branch
        RunGitCommand(localPath, "pull origin main"); // Or master, or a specific branch
    }

    private void RunGitCommand(string workingDirectory, string command)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = command,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Git command failed with exit code {process.ExitCode}: {error}");
        }
    }
}
```

## 5. Summary and Recommended Strategy

A hybrid approach is the best solution for meeting all requirements:

1.  **Use `LibGit2Sharp` for Standard Operations**: For full repository clones and pulls, `LibGit2Sharp` is the ideal choice. It avoids the dependency on a pre-installed Git client and provides a clean, programmatic API.

2.  **Use `git` CLI for Sparse Checkout**: When a sparse checkout is required, the application should fall back to invoking the `git` CLI using `System.Diagnostics.Process`. The application should first check if `git` is available on the system and guide the user to install it if it's missing.

### Implementation Flow:

1.  Check the user's configuration for the XML data source.
2.  Determine if a full clone or a sparse checkout is needed based on the repository URL and settings.
3.  **If full clone/pull**:
    - Use `Repository.IsValid(path)` to check if a directory is a valid repo.
    - If yes, use `Commands.Pull()` to update.
    - If no, use `Repository.Clone()` to download.
4.  **If sparse checkout**:
    - Check for the existence of `git.exe` on the system.
    - If available, use the `Process` class to execute the sequence of `git` commands for sparse checkout.
    - If not available, notify the user that Git is required for this feature.
