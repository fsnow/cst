using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using CST;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.Services.LocalApi;
using CST.Avalonia.Services.Tools;
using CST.Conversion;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CST.Avalonia.Tests.TestSupport
{
    /// <summary>
    /// Stands up the REAL surface-C stack for integration tests: a tiny self-contained Lucene index (built by the
    /// real <see cref="BookIndexer"/> over a few synthetic books that carry REAL catalog filenames), a real
    /// <see cref="SearchService"/>, the real tool adapters, and a real <see cref="LocalApiServer"/> over loopback
    /// HTTP. Unlike the mocked endpoint tests, this exercises the ASSEMBLED path - the DI/JSON/routing seams the
    /// mocks hide. Devanagari fixture content is generated at runtime from ASCII-Latin words via
    /// <see cref="ScriptConverter"/>, so there are no non-Latin literals in source.
    ///
    /// NOTE: building the index writes DocIds into the global <see cref="Books"/> singleton, so integration tests
    /// that use this MUST run serially (keep them in one test class / collection).
    /// </summary>
    public sealed class LocalApiTestServer : IAsyncDisposable
    {
        private readonly string _root;
        private readonly LocalApiServer _server;

        public string BaseUrl { get; }
        public string Token { get; }

        /// <summary>The indexed books, by role (real catalog filenames), for tests to reference.</summary>
        public string MulaBook { get; }
        public string AtthaBook { get; }

        private LocalApiTestServer(string root, LocalApiServer server, string mula, string attha)
        {
            _root = root;
            _server = server;
            BaseUrl = server.BaseUrl!;
            Token = server.Token!;
            MulaBook = mula;
            AtthaBook = attha;
        }

        /// <summary>An HttpClient pointed at the server with the bearer token attached.</summary>
        public HttpClient Http()
        {
            var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            return http;
        }

        public static async Task<LocalApiTestServer> StartAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "cst-int-" + Guid.NewGuid().ToString("N"));
            var xmlDir = Path.Combine(root, "xml");
            var indexDir = Path.Combine(root, "index");
            var handshakeDir = Path.Combine(root, "app");
            Directory.CreateDirectory(xmlDir);
            Directory.CreateDirectory(indexDir);
            Directory.CreateDirectory(handshakeDir);

            // Two REAL catalog books so Books.Inst resolves them (name/pitaka/commentaryLevel come from the catalog).
            var mula = Books.Inst.First(b => b.FileName.EndsWith(".mul.xml", StringComparison.Ordinal));
            var attha = Books.Inst.First(b => b.FileName.EndsWith(".att.xml", StringComparison.Ordinal));

            // Synthetic Devanagari content generated from ASCII-Latin words (no non-Latin literals here). The
            // tokenizer converts Devanagari -> IPE; a Latin query converts to the SAME IPE, so it matches.
            static string Deva(params string[] latin) =>
                string.Join(" ", latin.Select(w => ScriptConverter.Convert(w, Script.Latin, Script.Devanagari)));

            // Mula: several words + an apparatus <note>; "dhamma" also appears in the Atthakatha (bookCount test).
            string mulaXml =
                "<body><div id=\"dn1\" type=\"book\">" +
                "<pb ed=\"V\" n=\"1.0001\"/>" +
                "<p rend=\"bodytext\" n=\"1\">" + Deva("dhamma", "citta", "kamma", "dukkha") +
                " <note>" + Deva("dhamma") + " (si)</note></p>" +
                "</div></body>";
            string atthaXml =
                "<body><div id=\"dn1\" type=\"book\">" +
                "<pb ed=\"V\" n=\"1.0001\"/>" +
                "<p rend=\"bodytext\" n=\"1\">" + Deva("dhamma", "magga") + "</p>" +
                "</div></body>";

            await File.WriteAllTextAsync(Path.Combine(xmlDir, mula.FileName), mulaXml, Encoding.Unicode);
            await File.WriteAllTextAsync(Path.Combine(xmlDir, attha.FileName), atthaXml, Encoding.Unicode);

            // Build the index directly via the real BookIndexer (bypassing the IndexingService pipeline).
            var indexer = new BookIndexer { XmlDirectory = xmlDir, IndexDirectory = indexDir };
            indexer.IndexAll(_ => { }, new List<int> { mula.Index, attha.Index });

            // Wire the REAL SearchService + tool adapters.
            var settings = new Mock<ISettingsService>();
            settings.SetupGet(s => s.Settings)
                .Returns(new Settings { IndexDirectory = indexDir, XmlBooksDirectory = xmlDir });
            var scriptSvc = new Mock<IScriptService>();
            scriptSvc.SetupGet(s => s.CurrentScript).Returns(Script.Latin);
            var indexingSvc = new Mock<IIndexingService>();   // stored but unused by SearchService's read path

            var searchService = new SearchService(
                NullLogger<SearchService>.Instance, indexingSvc.Object, scriptSvc.Object, settings.Object);

            var searchTool = new SearchTool(searchService, settings.Object);
            var passageTool = new PassageTool(settings.Object);
            var scriptTool = new ScriptTool();

            var server = new LocalApiServer("test", handshakeDir, Serilog.Log.Logger,
                searchTool, dictionary: null, passage: passageTool, script: scriptTool);
            await server.StartAsync();

            return new LocalApiTestServer(root, server, mula.FileName, attha.FileName);
        }

        public async ValueTask DisposeAsync()
        {
            try { await _server.StopAsync(); } catch { /* best-effort */ }
            try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
        }
    }
}
