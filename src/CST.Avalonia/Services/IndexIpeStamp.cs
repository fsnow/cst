using System.IO;
using CST.Conversion;

namespace CST.Avalonia.Services
{
    /// <summary>
    /// Records which IPE version built the search index, in a small file beside the Lucene segments. An index
    /// with no stamp, or a different one, was built with another IPE and has to be rebuilt (#1049). Indexes
    /// built before the stamp existed have none, so they are rebuilt once.
    /// </summary>
    public static class IndexIpeStamp
    {
        public const string FileName = "ipe-version";

        public static bool IsCurrent(string indexDirectory)
        {
            try
            {
                var path = Path.Combine(indexDirectory, FileName);
                return File.Exists(path)
                       && int.TryParse(File.ReadAllText(path).Trim(), out var v)
                       && v == IpeVersion.Current;
            }
            catch (IOException) { return false; }
        }

        public static void Write(string indexDirectory)
        {
            Directory.CreateDirectory(indexDirectory);
            File.WriteAllText(Path.Combine(indexDirectory, FileName), IpeVersion.Current.ToString());
        }
    }
}
