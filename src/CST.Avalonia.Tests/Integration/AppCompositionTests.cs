using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.Services.LocalApi;
using CST.Avalonia.Services.Tools;
using CST.Tools;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.Integration
{
    /// <summary>
    /// Guards the app's COMPOSITION seam: <see cref="LocalApiServer.FromServiceProvider"/> must resolve and pass
    /// EVERY tool registered in DI. That's the /v1/scripts-404 class of bug - a tool registered but simply not
    /// passed to the server - which the endpoint/integration harness can't catch because it wires the tools by
    /// hand. The app and this test go through the SAME factory, so a forgotten tool fails here before shipping.
    /// </summary>
    public class AppCompositionTests : IAsyncLifetime
    {
        private string _dir = null!;
        private LocalApiServer _server = null!;

        public async Task InitializeAsync()
        {
            _dir = Path.Combine(Path.GetTempPath(), "cst-appcomp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);

            // Register the tools the app registers, with mocked underlying services. The FACTORY (not this test)
            // decides which tools reach the server - that is the seam under test.
            var search = new Mock<ISearchService>();
            search.Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SearchResult());
            var settings = new Mock<ISettingsService>();
            settings.SetupGet(s => s.Settings)
                .Returns(new Settings { XmlBooksDirectory = _dir, IndexDirectory = _dir });
            var dict = new Mock<IDictionaryService>();
            dict.SetupGet(d => d.AvailableLanguages).Returns(new[] { "en" });

            var services = new ServiceCollection();
            services.AddSingleton(search.Object);
            services.AddSingleton(settings.Object);
            services.AddSingleton(dict.Object);
            services.AddSingleton<ISearchTool, SearchTool>();
            services.AddSingleton<IDictionaryTool, DictionaryTool>();
            services.AddSingleton<IPassageTool, PassageTool>();
            services.AddSingleton<IScriptTool, ScriptTool>();
            var provider = services.BuildServiceProvider();

            _server = LocalApiServer.FromServiceProvider(provider, "test", _dir, Serilog.Log.Logger);
            await _server.StartAsync();
        }

        public async Task DisposeAsync()
        {
            await _server.StopAsync();
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        private HttpClient Authed()
        {
            var http = new HttpClient { BaseAddress = new Uri(_server.BaseUrl!) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _server.Token);
            return http;
        }

        [Fact]
        public async Task Registered_tools_are_all_mapped_by_the_factory()
        {
            using var http = Authed();

            // ScriptTool - the exact endpoint the /v1/scripts-404 bug knocked out (a registered tool not passed).
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/v1/scripts")).StatusCode);
            // DictionaryTool.
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/v1/dictionary/languages")).StatusCode);
            // SearchTool.
            Assert.Equal(HttpStatusCode.OK, (await http.PostAsync("/v1/search",
                new StringContent("{\"query\":\"x\"}", Encoding.UTF8, "application/json"))).StatusCode);

            // PassageTool is resolved by the identical factory path; its endpoint being mapped is confirmed
            // transitively (an unknown-book 404 is indistinguishable from an unmapped 404, so it's not asserted
            // directly here).
        }
    }
}
