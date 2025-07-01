using CST;
using System;
using System.Collections.Generic;
using System.Linq;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("CST Book Tree Demo");
        Console.WriteLine("==================");
        Console.WriteLine();

        // Load the complete CST book collection
        var books = Books.Inst;
        var totalBooks = books.Count();
        
        Console.WriteLine($"Total books in collection: {totalBooks}");
        Console.WriteLine();

        // Build tree structure like FormSelectBook does
        var rootNodes = new Dictionary<string, TreeNode>();

        foreach (var book in books)
        {
            string[] parts = book.LongNavPath.Split('/');
            
            // Add root node if it doesn't exist
            if (!rootNodes.TryGetValue(parts[0], out TreeNode? node))
            {
                node = new TreeNode
                {
                    Name = parts[0],
                    Children = new List<TreeNode>()
                };
                rootNodes[parts[0]] = node;
            }

            // Add everything under the root
            var currentNode = node;
            for (int i = 1; i < parts.Length; i++)
            {
                var existingChild = currentNode.Children.FirstOrDefault(c => c.Name == parts[i]);
                if (existingChild == null)
                {
                    var newChild = new TreeNode
                    {
                        Name = parts[i],
                        Children = new List<TreeNode>(),
                        Book = (i == parts.Length - 1) ? book : null
                    };
                    currentNode.Children.Add(newChild);
                    currentNode = newChild;
                }
                else
                {
                    currentNode = existingChild;
                }
            }
        }

        // Display the tree
        Console.WriteLine("Book Tree Structure:");
        Console.WriteLine("====================");
        foreach (var rootNode in rootNodes.Values.OrderBy(n => n.Name))
        {
            PrintTree(rootNode, 0, true);
        }

        Console.WriteLine();
        Console.WriteLine("Sample book details:");
        Console.WriteLine("====================");
        
        // Show first few books
        var sampleBooks = books.Take(5).ToList();
        foreach (var book in sampleBooks)
        {
            Console.WriteLine($"File: {book.FileName}");
            Console.WriteLine($"Path: {book.LongNavPath}");
            Console.WriteLine($"Short: {book.ShortNavPath}");
            Console.WriteLine($"Pitaka: {book.Pitaka}");
            Console.WriteLine($"Commentary: {book.Matn}");
            Console.WriteLine("---");
        }
    }

    static void PrintTree(TreeNode node, int depth, bool showBooks = false)
    {
        var indent = new string(' ', depth * 2);
        var icon = node.Book != null ? "📖" : "📁";
        var bookCount = CountBooks(node);
        var countText = bookCount > 0 ? $" ({bookCount} books)" : "";
        
        Console.WriteLine($"{indent}{icon} {node.Name}{countText}");
        
        if (node.Book != null && showBooks)
        {
            Console.WriteLine($"{indent}   → {node.Book.FileName}");
        }
        
        // Only show first few children to avoid overwhelming output
        var childrenToShow = node.Children.Take(depth < 2 ? 10 : 3);
        foreach (var child in childrenToShow)
        {
            PrintTree(child, depth + 1, showBooks && depth < 2);
        }
        
        if (node.Children.Count > (depth < 2 ? 10 : 3))
        {
            Console.WriteLine($"{new string(' ', (depth + 1) * 2)}... and {node.Children.Count - (depth < 2 ? 10 : 3)} more");
        }
    }

    static int CountBooks(TreeNode node)
    {
        if (node.Book != null) return 1;
        return node.Children.Sum(c => CountBooks(c));
    }
}

class TreeNode
{
    public string Name { get; set; } = string.Empty;
    public List<TreeNode> Children { get; set; } = new();
    public Book? Book { get; set; }
}