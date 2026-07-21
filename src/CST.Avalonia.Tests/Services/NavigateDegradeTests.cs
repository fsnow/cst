using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CST.Avalonia.Services.LocalApi;
using CST.Avalonia.Tests.TestSupport;
using Xunit;

namespace CST.Avalonia.Tests.Services
{
    /// <summary>
    /// The one page-anchor path that must NEVER refuse: being unable to verify an anchor is not the same as
    /// knowing it is wrong. If a missing corpus directory or an unreadable book could block navigation, this
    /// feature would be worse than the gap it closes — a user would be denied a perfectly good reference
    /// because of a configuration problem. Exercised directly against NavigateService (no HTTP), because the
    /// integration harness always supplies a corpus directory. (#187, fable LOW-4)
    /// </summary>
    public class NavigateDegradeTests
    {
        private static string AnyBook() =>
            CST.Books.Inst.First(b => b.FileName.EndsWith(".mul.xml", StringComparison.Ordinal)).FileName;

        private static NavigateService Service(string? xmlDir, RecordingPresentationService reader) =>
            new(reader, search: null, isRemoteControlAllowed: () => true, xmlBooksDirectory: xmlDir);

        [Fact]
        public async Task An_unconfigured_corpus_directory_still_navigates_and_says_it_could_not_verify()
        {
            var reader = new RecordingPresentationService();
            var svc = Service(null, reader);

            var (outcome, response) = await svc.NavigateAsync(new NavigateRequest(AnyBook(), Anchor: "V1.0023"));

            Assert.Equal(NavigateOutcome.Presented, outcome);
            Assert.True(response.Presented);
            Assert.Contains("not verified", response.Note);
            Assert.Single(reader.Requests);   // the reader WAS driven — an unverifiable anchor is not a refusal
        }

        [Fact]
        public async Task A_missing_book_file_still_navigates_and_says_it_could_not_verify()
        {
            var dir = Path.Combine(Path.GetTempPath(), "cst-degrade-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);   // exists, but contains no book files
            try
            {
                var reader = new RecordingPresentationService();
                var svc = Service(dir, reader);

                var (outcome, response) = await svc.NavigateAsync(new NavigateRequest(AnyBook(), Anchor: "V1.0023"));

                Assert.Equal(NavigateOutcome.Presented, outcome);
                Assert.True(response.Presented);
                Assert.Contains("not verified", response.Note);
                Assert.Single(reader.Requests);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* best-effort */ }
            }
        }

        [Fact]
        public async Task A_corrupt_corpus_path_does_not_escape_as_an_unhandled_error()
        {
            // A settings value with invalid path characters throws ArgumentException rather than IOException.
            // The contract is that ANY inability to check degrades, so this must not surface as a 500.
            var reader = new RecordingPresentationService();
            var svc = Service("\0not-a-real-path", reader);

            var (outcome, response) = await svc.NavigateAsync(new NavigateRequest(AnyBook(), Anchor: "V1.0023"));

            Assert.Equal(NavigateOutcome.Presented, outcome);
            Assert.Contains("not verified", response.Note);
        }

        [Fact]
        public async Task An_unverifiable_anchor_is_passed_through_unchanged()
        {
            // No canonical spelling is known when verification could not run, so the caller's anchor must reach
            // the reader as given rather than being silently altered.
            var reader = new RecordingPresentationService();
            var svc = Service(null, reader);

            await svc.NavigateAsync(new NavigateRequest(AnyBook(), Anchor: "V1.23"));

            var target = Assert.IsType<CST.Avalonia.Services.Presentation.PresentationTarget.Anchor>(
                reader.Last!.Target);
            Assert.Equal("V1.23", target.Name);
        }
    }
}
