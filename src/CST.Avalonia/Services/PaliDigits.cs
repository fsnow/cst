using System.Globalization;

namespace CST.Avalonia.Services;

/// <summary>
/// Folds every decimal digit, in any script, to its ASCII digit. The display pipeline writes homonym numbers in
/// the reading script ("Nālandā 1" in Devanāgarī carries a Devanāgarī 1), and the converters carry a native digit
/// through to IPE and Latin unchanged, so anything that compares or looks up a numbered headword folds first.
/// Shared by the dictionary panel's selection key (#935) and DPD's root and homonym lookups (#1002).
/// </summary>
public static class PaliDigits
{
    /// <summary>Folding by numeric value rather than by code-point range, so this holds for any script whose
    /// digits the converters learn to emit later.</summary>
    public static string ToAscii(string s) =>
        string.Create(s.Length, s, static (span, source) =>
        {
            for (int i = 0; i < source.Length; i++)
            {
                int digit = CharUnicodeInfo.GetDecimalDigitValue(source[i]);
                span[i] = digit >= 0 ? (char)('0' + digit) : source[i];
            }
        });
}
