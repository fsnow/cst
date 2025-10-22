using System;
using CST.Conversion;

class TestConversion
{
    static void Main()
    {
        var deva = "ग्ग्ह";
        Console.WriteLine($"Devanagari: {deva}");

        // Show the Unicode breakdown
        Console.Write("Unicode: ");
        foreach (char c in deva)
        {
            Console.Write($"U+{((int)c):X4} ");
        }
        Console.WriteLine();

        // Convert to IPE
        var ipe1 = ScriptConverter.Convert(deva, Script.Devanagari, Script.Ipe);
        Console.WriteLine($"\nIPE1: {ipe1}");
        Console.Write("IPE1 Unicode: ");
        foreach (char c in ipe1)
        {
            Console.Write($"U+{((int)c):X4} ");
        }
        Console.WriteLine();

        // Convert to Gujarati
        var gujarati = ScriptConverter.Convert(ipe1, Script.Ipe, Script.Gujarati);
        Console.WriteLine($"\nGujarati: {gujarati}");
        Console.Write("Gujarati Unicode: ");
        foreach (char c in gujarati)
        {
            Console.Write($"U+{((int)c):X4} ");
        }
        Console.WriteLine();

        // Convert back to IPE
        var ipe2 = ScriptConverter.Convert(gujarati, Script.Gujarati, Script.Ipe);
        Console.WriteLine($"\nIPE2: {ipe2}");
        Console.Write("IPE2 Unicode: ");
        foreach (char c in ipe2)
        {
            Console.Write($"U+{((int)c):X4} ");
        }
        Console.WriteLine();

        // Convert back to Devanagari
        var deva2 = ScriptConverter.Convert(ipe2, Script.Ipe, Script.Devanagari);
        Console.WriteLine($"\nDevanagari2: {deva2}");
        Console.Write("Devanagari2 Unicode: ");
        foreach (char c in deva2)
        {
            Console.Write($"U+{((int)c):X4} ");
        }
        Console.WriteLine();

        Console.WriteLine($"\nDeva Match: {deva == deva2}");
        Console.WriteLine($"IPE Match: {ipe1 == ipe2}");

        // Show Latin for readability
        var latin1 = ScriptConverter.Convert(ipe1, Script.Ipe, Script.Latin);
        var latin2 = ScriptConverter.Convert(ipe2, Script.Ipe, Script.Latin);
        Console.WriteLine($"\nLatin1: {latin1}");
        Console.WriteLine($"Latin2: {latin2}");
    }
}
