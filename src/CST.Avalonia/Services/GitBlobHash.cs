using System;
using System.Security.Cryptography;
using System.Text;

namespace CST.Avalonia.Services;

/// <summary>
/// Computes the Git "blob" object id - SHA-1 of the bytes <c>"blob {length}\0"</c> followed by the content -
/// so a downloaded file can be verified against the blob SHA that the GitHub Git Tree API reports for it.
/// A mismatch means a corrupt or partial download. Pure and dependency-free. (#65)
/// </summary>
public static class GitBlobHash
{
    /// <summary>The 40-character lowercase hex Git blob id of <paramref name="content"/>.</summary>
    public static string Compute(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var header = Encoding.ASCII.GetBytes($"blob {content.Length}\0");
        using var sha1 = SHA1.Create();
        sha1.TransformBlock(header, 0, header.Length, null, 0);
        sha1.TransformFinalBlock(content, 0, content.Length);
        return Convert.ToHexString(sha1.Hash!).ToLowerInvariant();
    }

    /// <summary>
    /// True if <paramref name="content"/> hashes to <paramref name="expectedSha"/> (the Git blob id from the
    /// tree). Case-insensitive; a null/empty expected SHA returns false (nothing to verify against).
    /// </summary>
    public static bool Matches(byte[] content, string? expectedSha) =>
        !string.IsNullOrEmpty(expectedSha) &&
        string.Equals(Compute(content), expectedSha, StringComparison.OrdinalIgnoreCase);
}
