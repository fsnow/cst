using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CST.Conversion;

namespace CST.ScriptValidation;

public class ConverterComparison
{
    public class ComparisonResult
    {
        public string OriginalWord { get; set; } = "";
        public string FileName { get; set; } = "";
        public Script SourceScript { get; set; }
        public Script TargetScript { get; set; }

        public string? CstOutput { get; set; }
        public string? PnfoOutput { get; set; }
        public string? AksharamukhaOutput { get; set; }

        public ComparisonCategory Category { get; set; }
        public string Notes { get; set; } = "";
    }

    public enum ComparisonCategory
    {
        AllAgree,              // ✅ All three converters produce the same output
        CstDiffersBoth,        // ⚠️ CST differs from both pnfo and Aksharamukha
        CstMatchesPnfo,        // ℹ️ CST matches pnfo but not Aksharamukha
        CstMatchesAksharamukha,// ℹ️ CST matches Aksharamukha but not pnfo
        AllDiffer,             // 🤔 All three produce different outputs
        PnfoFailed,            // ❌ pnfo conversion failed
        AksharamukhaFailed,    // ❌ Aksharamukha conversion failed
        BothExternalFailed     // ❌ Both external converters failed
    }

    public class ComparisonStats
    {
        public int TotalWords { get; set; }
        public Dictionary<ComparisonCategory, int> CategoryCounts { get; set; } = new();
        public List<ComparisonResult> Failures { get; set; } = new();
        public List<ComparisonResult> AllResults { get; set; } = new();
    }

    public static async Task<ComparisonResult> CompareConvertersAsync(
        string word,
        string fileName,
        Script sourceScript,
        Script targetScript)
    {
        var result = new ComparisonResult
        {
            OriginalWord = word,
            FileName = fileName,
            SourceScript = sourceScript,
            TargetScript = targetScript
        };

        // CST conversion
        try
        {
            result.CstOutput = ScriptConverter.Convert(word, sourceScript, targetScript);
        }
        catch (Exception ex)
        {
            result.Notes += $"CST error: {ex.Message}; ";
        }

        // pnfo conversion
        // Skip pnfo for Thai - it has too many rendering/encoding issues
        if (targetScript != Script.Thai)
        {
            string? pnfoSource = PnfoConverter.MapScriptName(sourceScript);
            string? pnfoTarget = PnfoConverter.MapScriptName(targetScript);

            if (pnfoSource != null && pnfoTarget != null)
            {
                result.PnfoOutput = await PnfoConverter.ConvertAsync(word, pnfoSource, pnfoTarget);
            }
        }

        // Aksharamukha conversion
        string? akSource = AksharamukhaConverter.MapScriptName(sourceScript);
        string? akTarget = AksharamukhaConverter.MapScriptName(targetScript);

        if (akSource != null && akTarget != null)
        {
            result.AksharamukhaOutput = await AksharamukhaConverter.ConvertAsync(word, akSource, akTarget);
        }

        // Categorize the result
        result.Category = CategorizeResult(result);

        return result;
    }

    /// <summary>
    /// Normalize script-specific orthographic differences for comparison.
    /// For Myanmar: Converts U+102B (tall aa) to U+102C (regular aa) to ignore orthographic differences.
    /// Pre-Unicode 5.0, only a single codepoint represented both forms.
    /// For Thai: Normalizes e/o vowel placement to canonical order (consonant + vowel).
    /// Thai display order puts เ (e) and โ (o) before consonants, but canonical order is after.
    /// CST and pnfo use canonical order; Aksharamukha uses display order.
    /// </summary>
    private static string NormalizeForComparison(string? text, Script targetScript)
    {
        if (text == null)
            return "";

        // Myanmar-specific normalization: tall aa vs regular aa
        if (targetScript == Script.Myanmar)
        {
            text = text.Replace('\u102B', '\u102C');
        }

        // Thai-specific normalization: move leading e/o vowels to after consonant (canonical order)
        // This normalizes both display order (เก) and canonical order (กเ) to the same form
        if (targetScript == Script.Thai)
        {
            // Move leading e vowel (เ U+0E40) from before consonant to after
            text = Regex.Replace(text, "\u0E40([\u0E01-\u0E2E])", "$1\u0E40");

            // Move leading o vowel (โ U+0E42) from before consonant to after
            text = Regex.Replace(text, "\u0E42([\u0E01-\u0E2E])", "$1\u0E42");
        }

        return text;
    }

    private static ComparisonCategory CategorizeResult(ComparisonResult result)
    {
        bool pnfoAvailable = result.PnfoOutput != null;
        bool aksharamukhaAvailable = result.AksharamukhaOutput != null;
        bool cstAvailable = result.CstOutput != null;

        // Handle failures
        if (!pnfoAvailable && !aksharamukhaAvailable)
            return ComparisonCategory.BothExternalFailed;
        if (!cstAvailable)
            return ComparisonCategory.CstDiffersBoth; // CST failed

        // Two-way comparison: CST vs Aksharamukha only (pnfo intentionally excluded for Thai)
        if (!pnfoAvailable && aksharamukhaAvailable)
        {
            string cstNorm = NormalizeForComparison(result.CstOutput, result.TargetScript);
            string akNorm = NormalizeForComparison(result.AksharamukhaOutput, result.TargetScript);

            return cstNorm == akNorm
                ? ComparisonCategory.AllAgree
                : ComparisonCategory.CstDiffersBoth; // CST differs from the only external converter available
        }

        // Two-way comparison: CST vs pnfo only (Aksharamukha unavailable)
        if (pnfoAvailable && !aksharamukhaAvailable)
        {
            string cstNorm = NormalizeForComparison(result.CstOutput, result.TargetScript);
            string pnfoNorm = NormalizeForComparison(result.PnfoOutput, result.TargetScript);

            return cstNorm == pnfoNorm
                ? ComparisonCategory.AllAgree
                : ComparisonCategory.CstDiffersBoth; // CST differs from the only external converter available
        }

        // Three-way comparison: All converters available
        // Normalize script-specific orthographic differences before comparison
        string cstNorm3 = NormalizeForComparison(result.CstOutput, result.TargetScript);
        string pnfoNorm3 = NormalizeForComparison(result.PnfoOutput, result.TargetScript);
        string akNorm3 = NormalizeForComparison(result.AksharamukhaOutput, result.TargetScript);

        bool cstMatchesPnfo = cstNorm3 == pnfoNorm3;
        bool cstMatchesAk = cstNorm3 == akNorm3;
        bool pnfoMatchesAk = pnfoNorm3 == akNorm3;

        if (cstMatchesPnfo && cstMatchesAk && pnfoMatchesAk)
            return ComparisonCategory.AllAgree;

        if (cstMatchesPnfo && !cstMatchesAk)
            return ComparisonCategory.CstMatchesPnfo;

        if (cstMatchesAk && !cstMatchesPnfo)
            return ComparisonCategory.CstMatchesAksharamukha;

        if (!cstMatchesPnfo && !cstMatchesAk && !pnfoMatchesAk)
            return ComparisonCategory.AllDiffer;

        // CST differs from both, but pnfo and Aksharamukha agree
        return ComparisonCategory.CstDiffersBoth;
    }

    public static string GetCategoryIcon(ComparisonCategory category)
    {
        return category switch
        {
            ComparisonCategory.AllAgree => "✅",
            ComparisonCategory.CstDiffersBoth => "⚠️",
            ComparisonCategory.CstMatchesPnfo => "ℹ️ (pnfo)",
            ComparisonCategory.CstMatchesAksharamukha => "ℹ️ (Aksharamukha)",
            ComparisonCategory.AllDiffer => "🤔",
            ComparisonCategory.PnfoFailed => "❌ pnfo",
            ComparisonCategory.AksharamukhaFailed => "❌ Aksharamukha",
            ComparisonCategory.BothExternalFailed => "❌ both",
            _ => "?"
        };
    }

    public static string GetCategoryDescription(ComparisonCategory category)
    {
        return category switch
        {
            ComparisonCategory.AllAgree => "All three converters agree",
            ComparisonCategory.CstDiffersBoth => "CST differs from both external converters (potential issue)",
            ComparisonCategory.CstMatchesPnfo => "CST matches pnfo but not Aksharamukha (different standards)",
            ComparisonCategory.CstMatchesAksharamukha => "CST matches Aksharamukha but not pnfo (different standards)",
            ComparisonCategory.AllDiffer => "All three converters produce different outputs (ambiguous/variant forms)",
            ComparisonCategory.PnfoFailed => "pnfo conversion failed",
            ComparisonCategory.AksharamukhaFailed => "Aksharamukha conversion failed",
            ComparisonCategory.BothExternalFailed => "Both external converters failed",
            _ => "Unknown"
        };
    }

    public static ComparisonStats AnalyzeResults(List<ComparisonResult> results)
    {
        var stats = new ComparisonStats
        {
            TotalWords = results.Count,
            AllResults = results
        };

        // Count by category
        foreach (var result in results)
        {
            if (!stats.CategoryCounts.ContainsKey(result.Category))
                stats.CategoryCounts[result.Category] = 0;
            stats.CategoryCounts[result.Category]++;
        }

        // Extract failures (anything other than AllAgree)
        stats.Failures = results
            .Where(r => r.Category != ComparisonCategory.AllAgree)
            .ToList();

        return stats;
    }
}
