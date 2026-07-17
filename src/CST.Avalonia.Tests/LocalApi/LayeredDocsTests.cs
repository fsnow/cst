using CST.Avalonia.Services.LocalApi;
using Xunit;

namespace CST.Avalonia.Tests.LocalApi
{
    public class LayeredDocsTests
    {
        private const string Sample =
            "# Doc\n\nintro\n\n## Connecting\n\nauth\n\n## Endpoints\n\n" +
            "<!--doc:search-->\n- `POST /v1/search` - find terms\n<!--/doc:search-->\n" +
            "- `GET /v1/status` - core (unmarked)\n" +
            "<!--doc:reading-->\n- `POST /v1/passage` - read\n<!--/doc:reading-->\n";

        [Fact]
        public void StripMarkers_removes_every_marker_but_keeps_content()
        {
            var s = LayeredDocs.StripMarkers(Sample);
            Assert.DoesNotContain("<!--doc:", s);
            Assert.DoesNotContain("<!--/doc:", s);
            Assert.Contains("/v1/search", s);
            Assert.Contains("/v1/passage", s);
            Assert.Contains("/v1/status", s);
        }

        [Fact]
        public void Slice_extracts_only_the_topics_regions()
        {
            var search = LayeredDocs.Slice(Sample, "search");
            Assert.NotNull(search);
            Assert.Contains("/v1/search", search);
            Assert.DoesNotContain("/v1/passage", search);   // the reading region is excluded
            Assert.DoesNotContain("/v1/status", search);     // unmarked content is excluded
            Assert.DoesNotContain("<!--", search);           // markers stripped from the slice
            Assert.Contains("focused slice", search);        // header present
        }

        [Fact]
        public void Slice_unknown_topic_is_null() => Assert.Null(LayeredDocs.Slice(Sample, "nope"));

        [Fact]
        public void WithPointer_injects_the_pointer_before_the_first_section()
        {
            var full = LayeredDocs.StripMarkers(Sample);
            var withP = LayeredDocs.WithPointer(full);
            Assert.Contains("Progressive discovery", withP);
            Assert.Contains("/docs/search.md", withP);
            Assert.Contains("/llms-full.txt", withP);
            Assert.True(withP.IndexOf("Progressive discovery", System.StringComparison.Ordinal)
                        < withP.IndexOf("## Connecting", System.StringComparison.Ordinal));
        }
    }
}
