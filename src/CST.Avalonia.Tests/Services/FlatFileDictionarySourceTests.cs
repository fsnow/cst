using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Dictionaries;
using CST.Conversion;
using CST.Tools;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.Services
{
    /// <summary>
    /// Unit tests for the flat-file (en/hi) dictionary source adapter — the entry mapping, headword script
    /// projection, meaning pass-through, the MaxEntries clamp (#305), cancellation forwarding (#308), and
    /// attribution. IDictionaryService is mocked. Ported from the former DictionaryToolTests when the flat-file
    /// mapping moved out of the (deleted) DictionaryTool into this source under the registry (#466). Headword
    /// text uses ASCII placeholders with the expected output computed inline (no hardcoded non-Latin).
    /// </summary>
    public class FlatFileDictionarySourceTests
    {
        [Fact]
        public void Attribution_and_DisplayName_come_from_the_service_or_fall_back()
        {
            var mock = new Mock<IDictionaryService>();
            mock.SetupGet(d => d.AvailableLanguages).Returns(new[] { "en", "hi" });
            mock.Setup(d => d.SourceFor("en"))
                .Returns(new DictionarySourceInfo("Test PED", "A. Compiler", "2nd", "2020", "Pub", "CC", null));
            // hi: SourceFor left unset -> null (an unattributed dictionary is null, never guessed).

            var en = new FlatFileDictionarySource(mock.Object, "en");
            var hi = new FlatFileDictionarySource(mock.Object, "hi");

            Assert.Equal("Test PED", en.Attribution?.Title);
            Assert.Equal("Test PED", en.DisplayName);      // display name is the source title when attributed
            Assert.Null(hi.Attribution);
            Assert.Equal("hi", hi.DisplayName);            // falls back to the id when unattributed
        }

        [Fact]
        public void DisplayName_prefers_the_displayName_field_over_the_citation_title()
        {
            // en's real case: a short picker label distinct from the full citation title.
            var mock = new Mock<IDictionaryService>();
            mock.Setup(d => d.SourceFor("en")).Returns(new DictionarySourceInfo(
                "A Dictionary of the Pali Language", "Childers", null, "1875", null, null, null,
                DisplayName: "Childers' Dictionary of the Pali Language"));

            var en = new FlatFileDictionarySource(mock.Object, "en");
            Assert.Equal("Childers' Dictionary of the Pali Language", en.DisplayName);   // displayName wins
            Assert.Equal("A Dictionary of the Pali Language", en.Attribution?.Title);    // citation unchanged
        }

        [Fact]
        public async Task LookupAsync_attaches_the_source_title_to_each_entry()
        {
            var mock = new Mock<IDictionaryService>();
            mock.Setup(d => d.LookupAsync("en", "metta", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DictionaryWord> { new("abc", "def") });
            mock.Setup(d => d.SourceFor("en"))
                .Returns(new DictionarySourceInfo("Test PED", null, null, null, null, null, null));

            var src = new FlatFileDictionarySource(mock.Object, "en");
            var e = Assert.Single(await src.LookupAsync(new DictionaryRequest("en", "metta")));
            Assert.Equal("Test PED", e.Source);
        }

        [Fact]
        public async Task LookupAsync_maps_entries_and_projects_headword()
        {
            var mock = new Mock<IDictionaryService>();
            mock.Setup(d => d.LookupAsync("en", "metta", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DictionaryWord> { new("abc", "<b>loving-kindness</b>") });

            var src = new FlatFileDictionarySource(mock.Object, "en");
            var entries = await src.LookupAsync(new DictionaryRequest("en", "metta")); // OutputScript = Latin

            var e = Assert.Single(entries);
            Assert.Equal(ScriptConverter.Convert("abc", Script.Ipe, Script.Latin), e.Headword);
            Assert.Equal("<b>loving-kindness</b>", e.MeaningHtml);                       // HTML pass-through
        }

        [Fact]
        public async Task LookupAsync_respects_MaxEntries()
        {
            var mock = new Mock<IDictionaryService>();
            mock.Setup(d => d.LookupAsync("en", "d", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DictionaryWord> { new("a", "1"), new("b", "2"), new("c", "3") });

            var src = new FlatFileDictionarySource(mock.Object, "en");
            var entries = await src.LookupAsync(new DictionaryRequest("en", "d", MaxEntries: 2));

            Assert.Equal(2, entries.Count);
        }

        [Fact]
        public async Task LookupAsync_clamps_a_negative_MaxEntries_to_zero()
        {
            // #305: a negative MaxEntries must not throw or wrap into a huge Take — it yields no entries.
            var mock = new Mock<IDictionaryService>();
            mock.Setup(d => d.LookupAsync("en", "d", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DictionaryWord> { new("a", "1"), new("b", "2") });

            var src = new FlatFileDictionarySource(mock.Object, "en");
            var entries = await src.LookupAsync(new DictionaryRequest("en", "d", MaxEntries: -5));

            Assert.Empty(entries);
        }

        [Fact]
        public async Task LookupAsync_forwards_the_cancellation_token_to_the_service()
        {
            // #308 A3-5: the source must thread ct through so a client timeout can cancel a first-touch load.
            using var cts = new CancellationTokenSource();
            var mock = new Mock<IDictionaryService>();
            mock.Setup(d => d.LookupAsync("en", "x", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DictionaryWord>());

            var src = new FlatFileDictionarySource(mock.Object, "en");
            await src.LookupAsync(new DictionaryRequest("en", "x"), cts.Token);

            mock.Verify(d => d.LookupAsync("en", "x", cts.Token), Times.Once);
        }
    }
}
