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
        Assert.Contains("#page=5", url);
        Assert.Contains("%20", url);           // space escaped
        Assert.DoesNotContain(" ", url);       // no raw spaces
    }

    [Fact]
    public void BuildPdfUrl_CollapsesTheThumbnailSidebar()
    {
        // #630. Chromium's viewer only leaves the sidebar to its remembered state when navpanes AND
        // toolbar are both absent; any explicit navpanes decides it, and only '1' opens it. So this must
        // be PRESENT rather than merely not '1' — dropping the parameter hands the decision back to
        // whatever the embedded viewer last remembered, which is exactly the behaviour being fixed.
        var url = PdfDisplayViewModel.BuildPdfUrl(Path.Combine(Path.GetTempPath(), "src.pdf"), 12);

        Assert.Contains("navpanes=0", url);
        Assert.EndsWith("#page=12&navpanes=0", url);
        // One '#': the parameters live in a single fragment, separated by '&'. A second '#' would make
        // everything after it part of the fragment's value rather than a parameter.
        Assert.Equal(1, url.Count(c => c == '#'));
    }

    [Fact]
    public void BuildPdfUrl_LeavesTheToolbarAlone()
    {
        // shouldShowToolbar is `navpanes === '1' || toolbar !== '0'`. Passing toolbar=0 to tidy the view
        // would take the zoom and page controls with it — and the page number is what a source PDF is
        // being read for. Pinned because "hide the sidebar" and "hide the toolbar" look like neighbours.
        var url = PdfDisplayViewModel.BuildPdfUrl(Path.Combine(Path.GetTempPath(), "src.pdf"), 1);

        Assert.DoesNotContain("toolbar", url);
    }

    [Fact]
    public void BuildPdfUrl_EscapesHashInPath_SoThePathCannotOpenTheFragment()
    {
        var path = Path.Combine(Path.GetTempPath(), "a#b.pdf");

        var url = PdfDisplayViewModel.BuildPdfUrl(path, 2);

        Assert.Contains("a%23b.pdf", url);              // '#' inside the path is escaped, not a fragment
        Assert.EndsWith("#page=2&navpanes=0", url);
        Assert.Equal(1, url.Count(c => c == '#'));      // exactly one '#': the viewer parameters
    }
}
