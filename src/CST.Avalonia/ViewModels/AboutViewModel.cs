using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using CST.Avalonia.Services;

namespace CST.Avalonia.ViewModels
{
    /// <summary>
    /// A credited source of text or data. <paramref name="Url"/> is the project's own home, so a reader who
    /// wants to know more goes to the source rather than to something we wrote about it.
    /// </summary>
    public sealed record AboutCredit(string Name, string Detail, string? Url = null)
    {
        public bool HasUrl => !string.IsNullOrWhiteSpace(Url);
    }

    /// <summary>
    /// One line of the library list.
    ///
    /// <para><see cref="Packages"/> names the NuGet ids the line stands for. That is what keeps a written
    /// list honest: <c>AboutInventoryTests</c> reads the shipping csproj files and fails when a package is
    /// referenced that no line here covers, or when a line names a package that has been removed. #746 left
    /// generated-or-written open — this is written, so it can read as prose, and checked, so it cannot go
    /// stale the next time someone adds a dependency.</para>
    /// </summary>
    public sealed record AboutLibrary(string Name, string Purpose, IReadOnlyList<string> Packages);

    /// <summary>
    /// The About box (#746): which build am I running, and who is this built on.
    ///
    /// <para>Deliberately free of every service. A tester reporting a bug needs the version even when
    /// startup went wrong, and a view model that resolves services could fail in exactly that state —
    /// <see cref="SettingsViewModel"/> cannot even be constructed without a service provider. Everything
    /// here comes from the assembly, from <see cref="RuntimeInformation"/>, or from the two static lists
    /// below.</para>
    /// </summary>
    public sealed class AboutViewModel
    {
        public AboutViewModel()
            : this(ReadInformationalVersion(),
                   RuntimeInformation.OSDescription,
                   RuntimeInformation.ProcessArchitecture,
                   RuntimeInformation.OSArchitecture,
                   RuntimeInformation.FrameworkDescription)
        {
        }

        /// <summary>Takes the environment as arguments so the formatting can be tested off the machine it
        /// describes — a build's own values would make the assertions tautological.</summary>
        internal AboutViewModel(string? informationalVersion, string osDescription,
            Architecture processArchitecture, Architecture osArchitecture, string frameworkDescription)
        {
            Version = VersionComparer.StripBuildMetadata(informationalVersion) is { Length: > 0 } v
                ? v
                : "unknown";
            Commit = ShortCommit(informationalVersion);
            Platform = $"{osDescription} · {DescribeArchitecture(processArchitecture, osArchitecture)}";
            Runtime = frameworkDescription;
        }

        public string AppName => "CST Reader";

        public string Subtitle => "A reader for the Chaṭṭha Saṅgāyana Tipiṭaka";

        /// <summary>The shipped version, e.g. "5.0.0-beta.6".</summary>
        public string Version { get; }

        /// <summary>The abbreviated commit the build came from, or null when it was not recorded.</summary>
        public string? Commit { get; }

        public bool HasCommit => Commit is not null;

        /// <summary>e.g. "macOS 26.5.1 · arm64".</summary>
        public string Platform { get; }

        /// <summary>e.g. ".NET 10.0.9".</summary>
        public string Runtime { get; }

        /// <summary>
        /// Everything above on one line, for the copy button. Beta 6 ships four builds across two operating
        /// systems, and "which one are you running?" is answered by pasting this into an issue rather than by
        /// a tester transcribing four fields.
        /// </summary>
        public string BuildSummary =>
            $"{AppName} {Version}{(HasCommit ? $" ({Commit})" : "")} · {Platform} · {Runtime}";

        /// <summary>
        /// The texts and data, VRI first. The app is a reader for VRI's corpus; everything else here is
        /// something it was built with.
        /// </summary>
        public IReadOnlyList<AboutCredit> Credits { get; } =
        [
            new("Vipassana Research Institute",
                "The Chaṭṭha Saṅgāyana Tipiṭaka texts this application reads, prepared and made freely "
                + "available by VRI. Every one of the 217 books comes from their edition.",
                "https://www.vridhamma.org"),

            new("models.dev",
                "The catalogue of AI providers and models, and the provider logos. MIT licensed; the licence "
                + "travels with the cached logo files.",
                "https://models.dev"),

            new("Digital Pāḷi Dictionary",
                "Bhikkhu Bodhirāsa's dictionary, shipped as a derived asset under CC BY-NC-SA 4.0.",
                "https://dpdict.net"),

            new("Dictionary of Pāli Proper Names",
                "G. P. Malalasekera's reference, in Ānandajoti Bhikkhu's revision, shipped as a derived asset."),

            new("A Dictionary of the Pali Language",
                "Robert Caesar Childers, 1875 — the bundled English dictionary."),
        ];

        /// <summary>
        /// The libraries, as a plain list. #746 asked for a list rather than a licence-by-licence table: a
        /// table implies a completeness it would lose the moment a package was added, and nobody audits
        /// licences from a dialog box.
        /// </summary>
        public IReadOnlyList<AboutLibrary> Libraries { get; } =
        [
            new("Avalonia", "The cross-platform UI framework, its Fluent theme, and the Inter typeface.",
                ["Avalonia", "Avalonia.Desktop", "Avalonia.Themes.Fluent", "Avalonia.Fonts.Inter",
                 "Avalonia.Diagnostics"]),
            new("Avalonia.Svg.Skia", "SVG rendering, which is how the provider logos are drawn.",
                ["Avalonia.Svg.Skia"]),
            new("Dock.Avalonia", "The docking layout — panels, tabs, and floating windows.",
                ["Dock.Avalonia", "Dock.Avalonia.Themes.Fluent", "Dock.Controls.Recycling", "Dock.Model",
                 "Dock.Model.Mvvm"]),
            new("ReactiveUI", "The MVVM framework the view models are built on.", ["ReactiveUI"]),
            new("WebViewControl-Avalonia", "The Chromium Embedded Framework browser the texts are rendered in.",
                ["WebViewControl-Avalonia", "WebViewControl-Avalonia-ARM64"]),
            new("Lucene.NET", "The full-text search index over the corpus.",
                ["Lucene.Net", "Lucene.Net.Analysis.Common"]),
            new("Serilog", "Logging.",
                ["Serilog", "Serilog.Extensions.Logging", "Serilog.Sinks.Console", "Serilog.Sinks.File"]),
            new("PdfPig", "Reading the scanned source editions.", ["PdfPig"]),
            new("Octokit", "Checking GitHub for corpus and dictionary updates.", ["Octokit"]),
            new("Mono.Cecil", "Reading .NET assembly metadata.", ["Mono.Cecil"]),
            new("ModelContextProtocol", "The MCP server that exposes the reader's tools to AI clients.",
                ["ModelContextProtocol.AspNetCore"]),
            new("Microsoft.Data.Sqlite and SQLitePCLRaw", "The dictionary and lemma databases.",
                ["Microsoft.Data.Sqlite", "SQLitePCLRaw.bundle_e_sqlite3"]),
            new("Azure.Identity and Microsoft.Graph", "Reaching the source PDFs held on SharePoint.",
                ["Azure.Identity", "Microsoft.Graph"]),
            new("Tmds.DBus.Protocol", "Desktop integration on Linux.", ["Tmds.DBus.Protocol"]),
            new("System.Security.Cryptography.ProtectedData", "Protecting stored API keys on Windows.",
                ["System.Security.Cryptography.ProtectedData"]),
        ];

        /// <summary>
        /// The version as the build recorded it. No hardcoded fallback: a literal here would be one more
        /// string to remember at release time, and the whole point of reading the assembly is that it cannot
        /// disagree with what shipped.
        /// </summary>
        private static string? ReadInformationalVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? assembly.GetName().Version?.ToString();
        }

        /// <summary>
        /// The commit from SemVer build metadata — the SDK appends "+&lt;sha&gt;" to InformationalVersion when
        /// it builds from a git checkout. Absent from a source-archive build, hence nullable rather than a
        /// placeholder that would look like a real answer.
        /// </summary>
        internal static string? ShortCommit(string? informationalVersion)
        {
            if (string.IsNullOrWhiteSpace(informationalVersion)) return null;

            var plus = informationalVersion.IndexOf('+');
            if (plus < 0) return null;

            var sha = informationalVersion[(plus + 1)..].Trim();
            return sha.Length >= 7 ? sha[..7] : null;
        }

        /// <summary>
        /// The architecture, naming BOTH when they differ — an x64 build under Rosetta on an Apple Silicon
        /// Mac is a distinct thing to be running, and a tester who reports "arm64" from the OS while running
        /// the x64 download sends the investigation to the wrong build.
        /// </summary>
        internal static string DescribeArchitecture(Architecture process, Architecture os)
        {
            var running = process.ToString().ToLowerInvariant();
            return process == os ? running : $"{running} on {os.ToString().ToLowerInvariant()}";
        }
    }
}
