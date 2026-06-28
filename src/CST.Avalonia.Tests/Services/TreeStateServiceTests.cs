using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.Services;
using CST.Avalonia.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CST.Avalonia.Tests.Services;

public class TreeStateServiceTests
{
    private static TreeStateService NewSvc() => new(NullLogger<TreeStateService>.Instance);

    private static BookTreeNode Node(string text, bool expanded, params BookTreeNode[] children)
    {
        var n = new BookTreeNode { OriginalDevanagariText = text, IsExpanded = expanded };
        foreach (var c in children) n.Children.Add(c);
        return n;
    }

    private static BookTreeNode Child(BookTreeNode n, string text)
        => n.Children.First(c => c.OriginalDevanagariText == text);

    [Fact]
    public void Expansion_SurvivesTreeAdditionAndReorder()
    {
        var svc = NewSvc();
        // original: A(expanded)->[A1(expanded), A2], B
        var tree1 = new List<BookTreeNode>
        {
            Node("A", true, Node("A1", true), Node("A2", false)),
            Node("B", false),
        };
        var keys = svc.CollectExpandedKeys(tree1); // "A", "A/A1"

        // later tree: a new top node + a new child under A; A and A1 still exist (collapsed initially)
        var tree2 = new List<BookTreeNode>
        {
            Node("A", false, Node("A0New", false), Node("A1", false), Node("A2", false)),
            Node("NewTop", false),
            Node("B", false),
        };
        var restored = svc.ApplyExpandedKeys(tree2, keys);

        Assert.Equal(2, restored);
        Assert.True(tree2[0].IsExpanded);                 // A restored
        Assert.True(Child(tree2[0], "A1").IsExpanded);     // A/A1 restored
        Assert.False(Child(tree2[0], "A0New").IsExpanded); // newly added node defaults collapsed
        Assert.False(tree2.First(n => n.OriginalDevanagariText == "NewTop").IsExpanded);
    }

    [Fact]
    public void Keying_IsByPath_NotJustText()
    {
        var svc = NewSvc();
        // "X" under A is expanded; a different "X" under B is not
        var tree = new List<BookTreeNode>
        {
            Node("A", false, Node("X", true)),
            Node("B", false, Node("X", false)),
        };
        var keys = svc.CollectExpandedKeys(tree); // only "A/X"

        var aX = Child(tree[0], "X");
        var bX = Child(tree[1], "X");
        aX.IsExpanded = false; // reset, then restore
        svc.ApplyExpandedKeys(tree, keys);

        Assert.True(aX.IsExpanded);  // A/X restored
        Assert.False(bX.IsExpanded); // B/X stays collapsed - path-keyed, not text-keyed
    }
}
