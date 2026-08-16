using CST.Avalonia.ViewModels;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// #646: ⌘O has to be able to reach the remembered book. Fluent realises no TreeViewItem container inside a
/// collapsed branch, so a selection under one is unfocusable, unscrollable and invisible — ⌘O appears to do
/// nothing. ExpandAncestors opens the way to it.
/// </summary>
public class BookTreeNodeTests
{
    private static BookTreeNode Node(string text, BookTreeNodeType type = BookTreeNodeType.Category)
        => new() { OriginalDevanagariText = text, DisplayName = text, NodeType = type };

    // Mirrors BuildBookTreeAsync, which sets Parent as it descends and leaves roots with Parent == null.
    private static BookTreeNode AddChild(BookTreeNode parent, BookTreeNode child)
    {
        child.Parent = parent;
        parent.Children.Add(child);
        return child;
    }

    [Fact]
    public void ExpandAncestors_OpensEveryBranchOnTheWayDown()
    {
        var root = Node("Tika");
        var pitaka = AddChild(root, Node("Sutta"));
        var nikaya = AddChild(pitaka, Node("Anguttara"));
        var book = AddChild(nikaya, Node("Duka-Tika", BookTreeNodeType.Book));

        book.ExpandAncestors();

        Assert.True(root.IsExpanded);
        Assert.True(pitaka.IsExpanded);
        Assert.True(nikaya.IsExpanded);
    }

    [Fact]
    public void ExpandAncestors_LeavesTheNodeItselfClosed()
    {
        // A category the user deliberately collapsed must stay collapsed: revealing a node is not the same
        // as opening it.
        var root = Node("Anya");
        var category = AddChild(root, Node("Visuddhimagga"));
        AddChild(category, Node("A book", BookTreeNodeType.Book));

        category.ExpandAncestors();

        Assert.True(root.IsExpanded);
        Assert.False(category.IsExpanded);
    }

    [Fact]
    public void ExpandAncestors_LeavesSiblingBranchesAlone()
    {
        var root = Node("Mula");
        var sutta = AddChild(root, Node("Sutta"));
        var vinaya = AddChild(root, Node("Vinaya"));
        var book = AddChild(sutta, Node("Digha", BookTreeNodeType.Book));

        book.ExpandAncestors();

        Assert.True(sutta.IsExpanded);
        Assert.False(vinaya.IsExpanded);
    }

    [Fact]
    public void ExpandAncestors_OnARootNodeChangesNothing()
    {
        var root = Node("Mula");

        root.ExpandAncestors();

        Assert.False(root.IsExpanded);
        Assert.Null(root.Parent);
    }

    [Fact]
    public void ExpandAncestors_RaisesPropertyChangedSoTheExpansionIsPersisted()
    {
        // OpenBookDialogViewModel persists expansion by subscribing to IsExpanded on every node, so an
        // ancestor opened this way must announce itself or the state is lost at the next restart.
        var root = Node("Tika");
        var book = AddChild(root, Node("A book", BookTreeNodeType.Book));

        var raised = 0;
        root.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BookTreeNode.IsExpanded)) raised++;
        };

        book.ExpandAncestors();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void ExpandAncestors_OnAnAlreadyOpenBranchRaisesNothing()
    {
        // Cmd+O is pressed repeatedly, and every raise costs a full-tree key walk plus a state broadcast.
        // Re-revealing a book that is already visible must be free.
        var root = Node("Tika");
        var nikaya = AddChild(root, Node("Anguttara"));
        var book = AddChild(nikaya, Node("A book", BookTreeNodeType.Book));
        book.ExpandAncestors();

        var raised = 0;
        root.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(BookTreeNode.IsExpanded)) raised++; };
        nikaya.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(BookTreeNode.IsExpanded)) raised++; };

        book.ExpandAncestors();

        Assert.Equal(0, raised);
    }
}
