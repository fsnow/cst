using System;
using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.ViewModels;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services;

/// <summary>
/// Saves and restores the book-tree expansion state by stable node identity - the path of Devanagari
/// node text from the root - rather than by position. This survives future additions or reordering of
/// the tree (the structure has been stable for ~30 years but may gain entries): nodes that still exist
/// keep their expansion; new nodes default to collapsed; removed nodes are ignored. Previously the state
/// was a positional bool[] guarded by an order-dependent hash, so ANY structural change discarded all
/// saved expansion state. (#64)
/// </summary>
public class TreeStateService
{
    // Unit Separator (U+001F) - never appears in node text, so it safely delimits a node path.
    private const char PathSeparator = '\u001F';

    private readonly ILogger<TreeStateService> _logger;

    public TreeStateService(ILogger<TreeStateService> logger)
    {
        _logger = logger;
    }

    /// <summary>Collect the path-keys of all currently-expanded nodes.</summary>
    public List<string> CollectExpandedKeys(IEnumerable<BookTreeNode> rootNodes)
    {
        var keys = new List<string>();
        try
        {
            CollectRecursive(rootNodes, "", keys);
            _logger.LogDebug("Collected {Count} expanded node keys", keys.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect tree expansion keys");
        }
        return keys;
    }

    /// <summary>
    /// Expand exactly the nodes whose path-key is in <paramref name="expandedKeys"/>; returns the count restored.
    /// </summary>
    public int ApplyExpandedKeys(IEnumerable<BookTreeNode> rootNodes, IEnumerable<string>? expandedKeys)
    {
        try
        {
            var set = new HashSet<string>(expandedKeys ?? Enumerable.Empty<string>());
            if (set.Count == 0)
                return 0;
            int restored = ApplyRecursive(rootNodes, "", set);
            _logger.LogDebug("Restored expansion for {Restored} of {Saved} saved nodes", restored, set.Count);
            return restored;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply tree expansion keys");
            return 0;
        }
    }

    private static string NodeKey(string parentKey, BookTreeNode node)
        => parentKey.Length == 0
            ? node.OriginalDevanagariText
            : parentKey + PathSeparator + node.OriginalDevanagariText;

    private void CollectRecursive(IEnumerable<BookTreeNode> nodes, string parentKey, List<string> keys)
    {
        foreach (var node in nodes)
        {
            var key = NodeKey(parentKey, node);
            if (node.IsExpanded)
                keys.Add(key);
            if (node.Children.Count > 0)
                CollectRecursive(node.Children, key, keys);
        }
    }

    private int ApplyRecursive(IEnumerable<BookTreeNode> nodes, string parentKey, HashSet<string> expanded)
    {
        int restored = 0;
        foreach (var node in nodes)
        {
            var key = NodeKey(parentKey, node);
            bool shouldExpand = expanded.Contains(key);
            if (node.IsExpanded != shouldExpand)
                node.IsExpanded = shouldExpand;
            if (shouldExpand)
                restored++;
            if (node.Children.Count > 0)
                restored += ApplyRecursive(node.Children, key, expanded);
        }
        return restored;
    }
}
