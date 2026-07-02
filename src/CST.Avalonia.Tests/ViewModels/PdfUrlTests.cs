using System.IO;
using System.Linq;
using CST.Avalonia.ViewModels;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// NET-5: the PDF browser URL must be a properly-escaped file:// URI plus a #page=N fragment. The old
/// $"file://{path}#page=N" left spaces raw (real PDF paths live under ".../Application Support/...") and
/// would let a '#' in the path truncate the URL.
/// </summary>
public class PdfUrlTests
{
    [Fact]
    public void BuildPdfUrl_EscapesSpaces_AndAppendsPageFragment()
    {
        var path = Path.Combine(Path.GetTempPath(), "sub dir", "my source.pdf");

        var url = PdfDisplayViewModel.BuildPdfUrl(path, 5);

        Assert.StartsWith("file:///", url);   // a real file URI, not "file://<raw path>"
        Assert.EndsWith("#page=5", url);
        Assert.Contains("%20", url);           // space escaped
        Assert.DoesNotContain(" ", url);       // no raw spaces
    }

    [Fact]
    public void BuildPdfUrl_EscapesHashInPath_SoTheOnlyFragmentIsThePage()
    {
        var path = Path.Combine(Path.GetTempPath(), "a#b.pdf");

        var url = PdfDisplayViewModel.BuildPdfUrl(path, 2);

        Assert.Contains("a%23b.pdf", url);            // '#' inside the path is escaped, not a fragment
        Assert.EndsWith("#page=2", url);
        Assert.Equal(1, url.Count(c => c == '#'));    // exactly one '#': the page fragment
    }
}
