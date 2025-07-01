using System;
using System.Reflection;
using System.Linq;

try
{
    // Load the CefGlue.Avalonia assembly
    var assemblyPath = "/Users/fsnow/.nuget/packages/cefglue.avalonia/120.6099.211/lib/net8.0/Xilium.CefGlue.Avalonia.dll";
    var assembly = Assembly.LoadFrom(assemblyPath);
    
    // Find the AvaloniaCefBrowser type
    var avaloniaCefBrowserType = assembly.GetTypes()
        .FirstOrDefault(t => t.Name == "AvaloniaCefBrowser");
    
    if (avaloniaCefBrowserType == null)
    {
        Console.WriteLine("AvaloniaCefBrowser type not found");
        return;
    }
    
    Console.WriteLine($"Found type: {avaloniaCefBrowserType.FullName}");
    Console.WriteLine($"Base type: {avaloniaCefBrowserType.BaseType?.FullName}");
    Console.WriteLine();
    
    // Get ALL public methods including inherited ones
    var allMethods = avaloniaCefBrowserType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Where(m => !m.IsSpecialName && m.DeclaringType != typeof(object)) // Exclude property getters/setters and Object methods
        .OrderBy(m => m.Name)
        .ToArray();
    
    Console.WriteLine("All Public Methods:");
    Console.WriteLine("===================");
    
    foreach (var method in allMethods)
    {
        var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
        Console.WriteLine($"{method.ReturnType.Name} {method.Name}({parameters}) - from {method.DeclaringType?.Name}");
    }
    
    // Check ALL public properties
    var allProperties = avaloniaCefBrowserType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .OrderBy(p => p.Name)
        .ToArray();
    
    Console.WriteLine();
    Console.WriteLine("All Public Properties:");
    Console.WriteLine("======================");
    
    foreach (var prop in allProperties)
    {
        Console.WriteLine($"{prop.PropertyType.Name} {prop.Name} {{ {(prop.CanRead ? "get; " : "")}{(prop.CanWrite ? "set; " : "")}}} - from {prop.DeclaringType?.Name}");
    }
    
    // Look for any Browser, Frame, or GetBrowser/GetFrame related members
    Console.WriteLine();
    Console.WriteLine("Browser/Frame Related Members:");
    Console.WriteLine("==============================");
    
    var browserRelated = allMethods.Where(m => 
        m.Name.Contains("Browser", StringComparison.OrdinalIgnoreCase) || 
        m.Name.Contains("Frame", StringComparison.OrdinalIgnoreCase) ||
        m.Name.Contains("Load", StringComparison.OrdinalIgnoreCase) ||
        m.ReturnType.Name.Contains("Browser", StringComparison.OrdinalIgnoreCase) ||
        m.ReturnType.Name.Contains("Frame", StringComparison.OrdinalIgnoreCase))
        .Union(allProperties.Where(p => 
            p.Name.Contains("Browser", StringComparison.OrdinalIgnoreCase) || 
            p.Name.Contains("Frame", StringComparison.OrdinalIgnoreCase) ||
            p.PropertyType.Name.Contains("Browser", StringComparison.OrdinalIgnoreCase) ||
            p.PropertyType.Name.Contains("Frame", StringComparison.OrdinalIgnoreCase)).Select(p => (MemberInfo)p))
        .ToArray();
    
    foreach (var member in browserRelated)
    {
        if (member is MethodInfo method)
        {
            var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
            Console.WriteLine($"Method: {method.ReturnType.Name} {method.Name}({parameters})");
        }
        else if (member is PropertyInfo prop)
        {
            Console.WriteLine($"Property: {prop.PropertyType.Name} {prop.Name}");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
}
