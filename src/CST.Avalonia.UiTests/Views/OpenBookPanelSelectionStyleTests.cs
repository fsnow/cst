using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using CST.Avalonia.UiTests.TestSupport;
using CST.Avalonia.ViewModels;
using CST.Avalonia.Views;
using Xunit;

namespace CST.Avalonia.UiTests.Views;

/// <summary>
/// The book tree's selection styles, asserted rather than eyeballed. (#655, pinning #646)
///
/// <para><b>The failure these exist for.</b> #646's foreground rule was written
/// <c>TreeViewItem:selected /template/ Border#PART_LayoutRoot &gt; ContentPresenter#PART_HeaderPresenter</c>
/// and matched nothing: Avalonia's <c>&gt;</c> resolves on the LOGICAL parent, and in Fluent's
/// <c>TreeViewItem</c> template the header presenter's logical parent is <c>Grid#PART_Header</c>, not the
/// border. It compiled, shipped, and passed a full suite. In dark mode the default foreground is already
/// white so nothing looked wrong; in light mode a selected row was black text on #007ACC. Which of those a
/// reviewer saw was down to their OS appearance setting.</para>
///
/// <para>So these run under BOTH theme variants, and <see cref="StyleProbe.AssertStyled"/> checks where a
/// value came from as well as what it is - a white that a selector produced and a white that inheritance
/// produced are the same colour and a different bug.</para>
///
/// <para><b>What this does not cover.</b> That a rule matched and set a brush. Not that the result is
/// legible, or that the palette is a good one. Those still need eyes on the running app.</para>
/// </summary>
public class OpenBookPanelSelectionStyleTests
{
    private const string AccentFill = "DockApplicationAccentBrushLow";
    private const string AccentHoverFill = "DockApplicationAccentBrushMed";
    private const string AccentForeground = "DockApplicationAccentForegroundBrush";

    // The one #646 got wrong. Foreground is the property that went dead, so this is the canary: revert
    // OpenBookPanel.axaml to the ">" form and this test - and only tests in this class - go red.
    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void A_selected_rows_label_takes_the_accent_foreground(string themeVariant)
    {
        var (window, category, _) = ShowTree(themeVariant);
        try
        {
            var header = StyleProbe.TemplatePart<ContentPresenter>(category, "PART_HeaderPresenter");

            StyleProbe.AssertStyled(
                header,
                ContentPresenter.ForegroundProperty,
                StyleProbe.Brush(header, AccentForeground),
                "The selected row's foreground rule matched nothing. This is the #646 defect exactly: " +
                "'TreeViewItem:selected /template/ Border#PART_LayoutRoot > ContentPresenter#PART_Header" +
                "Presenter' is dead, because > resolves on the LOGICAL parent and the header presenter's " +
                "logical parent is Grid#PART_Header. Drop the '> Border#PART_LayoutRoot' step.");

            // The label itself is reached by inheritance from the presenter, which is the claim the
            // OpenBookPanel comment makes ("Foreground is inherited, so this reaches the node's own
            // label"). Value only - inheritance is the mechanism being pinned, so an inherited priority
            // is the correct answer here rather than a failure.
            var label = OwnHeaderPanel(category).GetLogicalChildren().OfType<TextBlock>().First();
            Assert.Equal(
                ((ISolidColorBrush)StyleProbe.Brush(header, AccentForeground)).Color,
                ((ISolidColorBrush)label.Foreground!).Color);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void A_selected_row_is_accent_filled(string themeVariant)
    {
        var (window, category, _) = ShowTree(themeVariant);
        try
        {
            var layoutRoot = StyleProbe.TemplatePart<Border>(category, "PART_LayoutRoot");
            var fill = StyleProbe.Brush(layoutRoot, AccentFill);

            StyleProbe.AssertStyled(layoutRoot, Border.BackgroundProperty, fill,
                "The selected row has no accent fill: 'TreeViewItem:selected /template/ " +
                "Border#PART_LayoutRoot' reached nothing. Without it the tree has no 'you are here' at " +
                "all, which is the state #646 was fixing.");

            StyleProbe.AssertStyled(layoutRoot, Border.BorderBrushProperty, fill,
                "The selected row's border brush is not the accent fill, so Fluent's own selection " +
                "border is showing through as a differently coloured outline.");
        }
        finally
        {
            window.Close();
        }
    }

    // The icons need their own rule: Fluent's PathIcon ControlTheme sets Foreground, and a ControlTheme
    // setter outranks the inherited value the label rides on. Without this rule a selected row renders
    // label-white and icon-black in light mode - and, once again, looks fine in dark.
    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void A_selected_rows_icon_takes_the_accent_foreground(string themeVariant)
    {
        var (window, category, _) = ShowTree(themeVariant);
        try
        {
            var icons = OwnIcons(category);
            Assert.NotEmpty(icons);

            var expected = StyleProbe.Brush(category, AccentForeground);
            foreach (var icon in icons)
            {
                StyleProbe.AssertStyled(icon, PathIcon.ForegroundProperty, expected,
                    "A selected row's icon kept the theme's ordinary text colour. " +
                    "'TreeViewItem:selected > StackPanel > PathIcon' matched nothing - inheritance does " +
                    "not reach a PathIcon, because its ControlTheme sets Foreground and that outranks " +
                    "an inherited value.");
            }
        }
        finally
        {
            window.Close();
        }
    }

    // The scoping claim /template/ is there to make: it matches only controls whose TemplatedParent is
    // THIS item, so a child book's own header is out of reach. A plain descendant selector
    // ("TreeViewItem:selected TextBlock") would recolour every book under a selected category, because a
    // child TreeViewItem is a visual descendant of its parent.
    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void A_selected_categorys_accent_stays_off_its_child_books(string themeVariant)
    {
        var (window, category, book) = ShowTree(themeVariant);
        try
        {
            Assert.True(book.IsSelected is false, "Precondition: only the category is selected.");

            var childHeader = StyleProbe.TemplatePart<ContentPresenter>(book, "PART_HeaderPresenter");
            StyleProbe.AssertNotStyled(childHeader, ContentPresenter.ForegroundProperty,
                StyleProbe.Brush(childHeader, AccentForeground),
                "Selecting a category recoloured the books underneath it. /template/ is what confines " +
                "the rule to the selected item's own template parts.");

            var childLayoutRoot = StyleProbe.TemplatePart<Border>(book, "PART_LayoutRoot");
            StyleProbe.AssertNotStyled(childLayoutRoot, Border.BackgroundProperty,
                StyleProbe.Brush(childLayoutRoot, AccentFill),
                "Selecting a category accent-filled the rows of the books underneath it.");

            foreach (var icon in OwnIcons(book))
            {
                StyleProbe.AssertNotStyled(icon, PathIcon.ForegroundProperty,
                    StyleProbe.Brush(icon, AccentForeground),
                    "Selecting a category recoloured the icons of the books underneath it. '>' matches a " +
                    "LOGICAL child, and a child book's icon's logical ancestor is the child item.");
            }
        }
        finally
        {
            window.Close();
        }
    }

    // IsPointerOver is containment-based: an item is "hovered" whenever the pointer is anywhere in its
    // subtree. Written as "TreeViewItem:selected:pointerover", mousing over a book would brighten its
    // selected parent category two rows away. Fluent scopes hover to the border, and so do we.
    //
    // One variant is enough here - the claim is about which element carries the pseudo-class, and that is
    // the same in both.
    [AvaloniaFact]
    public void Hovering_a_child_book_leaves_its_selected_parent_row_unbrightened()
    {
        var (window, category, book) = ShowTree("Light");
        try
        {
            var bookRow = StyleProbe.TemplatePart<Border>(book, "PART_LayoutRoot");
            var categoryRow = StyleProbe.TemplatePart<Border>(category, "PART_LayoutRoot");

            var centre = bookRow.TranslatePoint(
                new Point(bookRow.Bounds.Width / 2, bookRow.Bounds.Height / 2), window);
            Assert.True(centre.HasValue, "Could not locate the child book's row in the window.");

            window.MouseMove(centre!.Value);
            StyleProbe.Pump(window);

            // Without this the rest is vacuous: if the pointer never landed on the book, nothing below
            // could brighten anything and the test would pass for the wrong reason.
            Assert.True(bookRow.IsPointerOver, "The synthetic pointer did not land on the child book's row.");

            // The containment fact the comment rests on. If this ever goes false, the rationale in
            // OpenBookPanel.axaml has stopped being true and the rule can be simplified.
            Assert.True(category.IsPointerOver,
                "Hovering a child no longer marks its parent TreeViewItem as pointer-over.");

            Assert.False(categoryRow.IsPointerOver,
                "The parent category's own row is pointer-over, so scoping hover to the border no longer " +
                "separates the two.");

            StyleProbe.AssertStyled(categoryRow, Border.BackgroundProperty,
                StyleProbe.Brush(categoryRow, AccentFill),
                "Hovering a book brightened its selected parent category's row. The :pointerover " +
                "pseudo-class has moved off Border#PART_LayoutRoot and onto the TreeViewItem.");

            Assert.NotEqual(
                ((ISolidColorBrush)StyleProbe.Brush(categoryRow, AccentHoverFill)).Color,
                ((ISolidColorBrush)categoryRow.Background!).Color);
        }
        finally
        {
            window.Close();
        }
    }

    // ---- fixture -------------------------------------------------------------------------------------

    /// <summary>
    /// The real <see cref="OpenBookPanel"/> markup - not a copy of its styles - showing one expanded
    /// category with two books under it, the category selected.
    /// </summary>
    private static (Window Window, TreeViewItem Category, TreeViewItem Book) ShowTree(string themeVariant)
    {
        var variant = themeVariant switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => throw new ArgumentOutOfRangeException(nameof(themeVariant), themeVariant, null)
        };

        var category = new BookTreeNode
        {
            DisplayName = "Vinaya Piṭaka",
            NodeType = BookTreeNodeType.Category,
            BookCount = 2,
            IsExpanded = true
        };
        foreach (var title in new[] { "Pārājikapāḷi", "Pācittiyapāḷi" })
        {
            category.Children.Add(new BookTreeNode
            {
                DisplayName = title,
                NodeType = BookTreeNodeType.Book,
                Parent = category
            });
        }

        var panel = new OpenBookPanel();
        var tree = panel.FindControl<TreeView>("BookTreeView")
                   ?? throw new InvalidOperationException("OpenBookPanel no longer has a TreeView named BookTreeView.");
        tree.ItemsSource = new[] { category };

        var window = StyleProbe.Show(panel, variant);

        // Selection is driven through the node, because the TreeViewItem style binds IsSelected TwoWay to
        // it. Setting TreeView.SelectedItem as well covers the case where the container is realised late.
        category.IsSelected = true;
        tree.SelectedItem = category;
        StyleProbe.Pump(window);

        var categoryItem = ItemFor(tree, category);
        var bookItem = ItemFor(tree, category.Children[0]);

        Assert.True(categoryItem.IsSelected,
            "Precondition: the category's container is not selected, so nothing below tests a :selected rule.");
        Assert.True(categoryItem.IsExpanded && bookItem.IsVisible,
            "Precondition: the child books are not realised, so the scoping assertions would be vacuous.");

        return (window, categoryItem, bookItem);
    }

    private static TreeViewItem ItemFor(TreeView tree, BookTreeNode node)
    {
        var item = tree.GetVisualDescendants().OfType<TreeViewItem>()
            .FirstOrDefault(i => ReferenceEquals(i.DataContext, node));

        Assert.True(item is not null, $"No realised TreeViewItem for '{node.DisplayName}'.");
        return item!;
    }

    // The StackPanel from the TreeDataTemplate. It is a LOGICAL child of the item - which is what lets
    // "TreeViewItem:selected > StackPanel > PathIcon" work at all - and each item has exactly one.
    private static StackPanel OwnHeaderPanel(TreeViewItem item) =>
        item.GetLogicalChildren().OfType<StackPanel>().First();

    // Only this item's own icons: a child book's icons are logical children of the CHILD item, so they
    // are not reachable here, exactly as the '>' in the selector is not able to reach them.
    private static IReadOnlyList<PathIcon> OwnIcons(TreeViewItem item) =>
        OwnHeaderPanel(item).GetLogicalChildren().OfType<PathIcon>().ToList();
}
