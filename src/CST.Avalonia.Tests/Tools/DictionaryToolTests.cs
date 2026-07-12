using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Tools;
using CST.Conversion;
using CST.Tools;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.Tools
{
    /// <summary>
    /// Unit tests for the surface-C dictionary tool wrapper. IDictionaryService is mocked; these assert the
    /// entry mapping, headword script projection, meaning pass-through, and MaxEntries limit. Headword text
    /// uses ASCII placeholders with expected output computed inline (no hardcoded non-Latin).
    /// </summary>
    public class DictionaryToolTests
    {
        [Fact]
        public void Languages_passes_through()
        {
            var mock = new Mock<IDictionaryService>();
            mock.SetupGet(d => d.AvailableLanguages).Returns(new[] { "en", "hi" });

            var tool = new DictionaryTool(mock.Object);

            Assert.Equal(new[] { "en", "hi" }, tool.Languages);
        }

        [Fact]
        public async Task LookupAsync_maps_entries_and_projects_headword()
        {
            var mock = new Mock<IDictionaryService>();
            mock.Setup(d => d.LookupAsync("en", "metta"))
                .ReturnsAsync(new List<DictionaryWord>
                {
                    new DictionaryWord("abc", "<b>loving-kindness</b>")
                });

            var tool = new DictionaryTool(mock.Object);
            var entries = await tool.LookupAsync(new DictionaryRequest("en", "metta")); // OutputScript = Latin

            var e = Assert.Single(entries);
            Assert.Equal(ScriptConverter.Convert("abc", Script.Ipe, Script.Latin), e.Headword);
            Assert.Equal("<b>loving-kindness</b>", e.MeaningHtml);                       // HTML pass-through
        }

        [Fact]
        public async Task LookupAsync_respects_MaxEntries()
        {
            var mock = new Mock<IDictionaryService>();
            mock.Setup(d => d.LookupAsync("en", "d"))
                .ReturnsAsync(new List<DictionaryWord>
                {
                    new DictionaryWord("a", "1"),
                    new DictionaryWord("b", "2"),
                    new DictionaryWord("c", "3")
                });

            var tool = new DictionaryTool(mock.Object);
            var entries = await tool.LookupAsync(new DictionaryRequest("en", "d", MaxEntries: 2));

            Assert.Equal(2, entries.Count);
        }

        [Fact]
        public async Task LookupAsync_clamps_a_negative_MaxEntries_to_zero()
        {
            // #305: a negative MaxEntries must not throw or wrap into a huge Take — it yields no entries.
            var mock = new Mock<IDictionaryService>();
            mock.Setup(d => d.LookupAsync("en", "d"))
                .ReturnsAsync(new List<DictionaryWord>
                {
                    new DictionaryWord("a", "1"),
                    new DictionaryWord("b", "2")
                });

            var tool = new DictionaryTool(mock.Object);
            var entries = await tool.LookupAsync(new DictionaryRequest("en", "d", MaxEntries: -5));

            Assert.Empty(entries);
        }

        [Fact]
        public async Task LookupAsync_forwards_the_cancellation_token_to_the_service()
        {
            // #308 A3-5: the tool must thread ct through so a client timeout can cancel a first-touch load.
            using var cts = new CancellationTokenSource();
            var mock = new Mock<IDictionaryService>();
            mock.Setup(d => d.LookupAsync("en", "x", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DictionaryWord>());

            var tool = new DictionaryTool(mock.Object);
            await tool.LookupAsync(new DictionaryRequest("en", "x"), cts.Token);

            mock.Verify(d => d.LookupAsync("en", "x", cts.Token), Times.Once);
        }
    }
}
