using System;
using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.ViewModels;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services;

/// <summary>
/// Service for managing tree expansion state persistence
/// Implements CST4 BitArray pattern but stores as boolean array for JSON debugging
/// </summary>
public class TreeStateService
{
    private readonly ILogger<TreeStateService> _logger;
    private int _nodeIndex;

    public TreeStateService(ILogger<TreeStateService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Collect expansion states from tree nodes - equivalent to CST4 GetNodeStates()
    /// </summary>
    public bool[] CollectExpansionStates(IEnumerable<BookTreeNode> rootNodes)
    {
        try
        {
            var totalNodes = CountAllNodes(rootNodes);
            var states = new bool[totalNodes];
            _nodeIndex = 0;

            CollectNodeStatesRecursive(rootNodes, states);

            _logger.LogDebug("Collected expansion states for {NodeCount} nodes", totalNodes);
            return states;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect tree expansion states");
            return Array.Empty<bool>();
        }
    }

    /// <summary>
    /// Apply expansion states to tree nodes - equivalent to CST4 SetNodeStates()
    /// </summary>
    public bool ApplyExpansionStates(IEnumerable<BookTreeNode> rootNodes, bool[] states)
    {
        try
        {
            var totalNodes = CountAllNodes(rootNodes);
            
            // Validate state array size matches current tree structure
            if (states.Length != totalNodes)
            {
                _logger.LogWarning(
                    "Tree expansion state size mismatch: saved={SavedCount}, current={CurrentCount}", 
                    states.Length, totalNodes);
                return false;
            }

            _nodeIndex = 0;
            ApplyNodeStatesRecursive(rootNodes, states);

            _logger.LogDebug("Applied expansion states for {NodeCount} nodes", totalNodes);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply tree expansion states");
            return false;
        }
    }

    /// <summary>
    /// Count total number of nodes in tree (for validation)
    /// </summary>
    public int CountAllNodes(IEnumerable<BookTreeNode> rootNodes)
    {
        int count = 0;
        foreach (var node in rootNodes)
        {
            count += CountNodeRecursive(node);
        }
        return count;
    }

    /// <summary>
    /// Generate a version hash of tree structure for change detection
    /// </summary>
    public int GenerateTreeVersion(IEnumerable<BookTreeNode> rootNodes)
    {
        try
        {
            var pathHash = 0;
            foreach (var node in rootNodes)
            {
                pathHash = HashCode.Combine(pathHash, GenerateNodeHash(node));
            }
            return pathHash;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate tree version hash");
            return 0;
        }
    }

    /// <summary>
    /// Check if tree structure has changed since states were saved
    /// </summary>
    public bool ValidateTreeStructure(IEnumerable<BookTreeNode> rootNodes, int savedVersion, int savedNodeCount)
    {
        var currentNodeCount = CountAllNodes(rootNodes);
        var currentVersion = GenerateTreeVersion(rootNodes);

        var isValid = currentNodeCount == savedNodeCount && currentVersion == savedVersion;
        
        if (!isValid)
        {
            _logger.LogInformation(
                "Tree structure changed - NodeCount: {CurrentCount} vs {SavedCount}, Version: {CurrentVersion} vs {SavedVersion}",
                currentNodeCount, savedNodeCount, currentVersion, savedVersion);
        }

        return isValid;
    }

    /// <summary>
    /// Create a debug-friendly representation of tree expansion states
    /// </summary>
    public TreeExpansionDebugInfo CreateDebugInfo(IEnumerable<BookTreeNode> rootNodes, bool[] states)
    {
        var debugInfo = new TreeExpansionDebugInfo();
        _nodeIndex = 0;

        foreach (var rootNode in rootNodes)
        {
            var rootInfo = CreateNodeDebugInfo(rootNode, states, 0);
            debugInfo.RootNodes.Add(rootInfo);
        }

        return debugInfo;
    }

    private void CollectNodeStatesRecursive(IEnumerable<BookTreeNode> nodes, bool[] states)
    {
        foreach (var node in nodes)
        {
            if (_nodeIndex < states.Length)
            {
                states[_nodeIndex] = node.IsExpanded;
                _nodeIndex++;

                if (node.Children.Count > 0)
                {
                    CollectNodeStatesRecursive(node.Children, states);
                }
            }
        }
    }

    private void ApplyNodeStatesRecursive(IEnumerable<BookTreeNode> nodes, bool[] states)
    {
        foreach (var node in nodes)
        {
            if (_nodeIndex < states.Length)
            {
                node.IsExpanded = states[_nodeIndex];
                _nodeIndex++;

                if (node.Children.Count > 0)
                {
                    ApplyNodeStatesRecursive(node.Children, states);
                }
            }
        }
    }

    private int CountNodeRecursive(BookTreeNode node)
    {
        int count = 1; // Count this node

        foreach (var child in node.Children)
        {
            count += CountNodeRecursive(child);
        }

        return count;
    }

    private int GenerateNodeHash(BookTreeNode node)
    {
        var hash = HashCode.Combine(node.OriginalDevanagariText, node.Level, node.NodeType);
        
        foreach (var child in node.Children)
        {
            hash = HashCode.Combine(hash, GenerateNodeHash(child));
        }

        return hash;
    }

    private NodeDebugInfo CreateNodeDebugInfo(BookTreeNode node, bool[] states, int depth)
    {
        var info = new NodeDebugInfo
        {
            Index = _nodeIndex,
            Name = node.DisplayName,
            OriginalText = node.OriginalDevanagariText,
            IsExpanded = _nodeIndex < states.Length ? states[_nodeIndex] : false,
            Depth = depth,
            IsBook = node.NodeType == BookTreeNodeType.Book
        };

        _nodeIndex++;

        foreach (var child in node.Children)
        {
            var childInfo = CreateNodeDebugInfo(child, states, depth + 1);
            info.Children.Add(childInfo);
        }

        return info;
    }
}

/// <summary>
/// Debug information for tree expansion states (helpful for troubleshooting)
/// </summary>
public class TreeExpansionDebugInfo
{
    public List<NodeDebugInfo> RootNodes { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int TotalNodes => CountTotalNodes();

    private int CountTotalNodes()
    {
        int count = 0;
        foreach (var root in RootNodes)
        {
            count += CountNodeRecursive(root);
        }
        return count;
    }

    private int CountNodeRecursive(NodeDebugInfo node)
    {
        int count = 1;
        foreach (var child in node.Children)
        {
            count += CountNodeRecursive(child);
        }
        return count;
    }
}

/// <summary>
/// Debug information for individual tree node
/// </summary>
public class NodeDebugInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string OriginalText { get; set; } = string.Empty;
    public bool IsExpanded { get; set; }
    public int Depth { get; set; }
    public bool IsBook { get; set; }
    public List<NodeDebugInfo> Children { get; set; } = new();
}