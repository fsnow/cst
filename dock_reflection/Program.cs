using System;
using System.Reflection;
using System.Linq;
using Dock.Model.Core;
using Dock.Model.Mvvm;

// Quick reflection program to examine Dock types
class Program
{
    static void Main()
    {
        try
        {
            Console.WriteLine("=== Looking for Factory type and UpdateDockable method ===");
            
            // Direct approach - create a Factory instance and examine it
            var factory = new Factory();
            var factoryType = factory.GetType();
            
            Console.WriteLine($"Factory type: {factoryType.FullName}");
            Console.WriteLine($"Base type: {factoryType.BaseType?.FullName}");
            
            // Get all methods on this type and its base types
            var allMethods = factoryType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            var updateMethods = allMethods.Where(m => m.Name.Contains("Update"));
            
            Console.WriteLine("\n=== All Update-related methods ===");
            foreach (var method in updateMethods)
            {
                Console.WriteLine($"Method: {method.Name}");
                foreach (var param in method.GetParameters())
                {
                    Console.WriteLine($"  Parameter: {param.Name} - Type: {param.ParameterType.Name}");
                    Console.WriteLine($"    Full type: {param.ParameterType.FullName}");
                    
                    // If it's an enum, show the values
                    if (param.ParameterType.IsEnum)
                    {
                        var values = Enum.GetNames(param.ParameterType);
                        Console.WriteLine($"    Enum values: {string.Join(", ", values)}");
                    }
                }
                Console.WriteLine();
            }
            
            // If no update methods found, show ALL virtual methods
            if (!updateMethods.Any())
            {
                Console.WriteLine("\n=== All virtual/override methods in Factory ===");
                var virtualMethods = allMethods.Where(m => m.IsVirtual);
                foreach (var method in virtualMethods.Take(20)) // Limit to first 20
                {
                    Console.WriteLine($"Method: {method.Name}");
                    foreach (var param in method.GetParameters())
                    {
                        Console.WriteLine($"  Parameter: {param.Name} - Type: {param.ParameterType.Name}");
                    }
                    Console.WriteLine();
                }
            }
            
            Console.WriteLine("=== All enums in Dock assemblies ===");
            
            // Get all loaded assemblies that contain "Dock"
            var dockAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.FullName?.Contains("Dock") == true)
                .ToList();
                
            Console.WriteLine($"Found {dockAssemblies.Count} Dock assemblies:");
            foreach (var assembly in dockAssemblies)
            {
                Console.WriteLine($"  {assembly.FullName}");
            }
            
            // Look for all enums
            foreach (var assembly in dockAssemblies)
            {
                try
                {
                    var enums = assembly.GetTypes().Where(t => t.IsEnum);
                    
                    foreach (var enumType in enums)
                    {
                        Console.WriteLine($"\nEnum: {enumType.FullName}");
                        var values = Enum.GetNames(enumType);
                        Console.WriteLine($"  Values: {string.Join(", ", values)}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Error examining assembly {assembly.FullName}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
}
