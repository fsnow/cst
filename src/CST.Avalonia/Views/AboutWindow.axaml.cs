using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CST.Avalonia.ViewModels;
using Serilog;

namespace CST.Avalonia.Views
{
    public partial class AboutWindow : Window
    {
        private readonly ILogger _logger = Log.ForContext<AboutWindow>();

        public AboutWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Opens a credited project's own site.
        ///
        /// <para>Through Avalonia's <c>Launcher</c> rather than the <c>Process.Start</c> ladder WelcomeView
        /// carries: that ladder exists because the welcome page hands URLs out of a CEF navigation callback,
        /// which has no TopLevel to hand. Here there is one, so the framework can pick the platform's
        /// opener.</para>
        /// </summary>
        private async void OnOpenLink(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not HyperlinkButton { DataContext: AboutCredit credit } || credit.Url is not { } url)
                    return;

                await Launcher.LaunchUriAsync(new Uri(url));
                _logger.Information("Opened {Url} from the About window", url);
            }
            catch (Exception ex)
            {
                _logger.Warning("Could not open the link from the About window | {Details}", ex.Message);
            }
        }

        /// <summary>
        /// Puts version, platform and runtime on the clipboard as one line, so a bug report carries the
        /// build without the reporter transcribing four fields. Lives here rather than in the view model
        /// because the clipboard is reached through the window's TopLevel, exactly as SettingsWindow's
        /// MCP-config copy is.
        /// </summary>
        private async void OnCopyBuildSummary(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (DataContext is not AboutViewModel about) return;

                var clipboard = Clipboard ?? TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard == null)
                {
                    _logger.Warning("No clipboard available to copy the build summary");
                    return;
                }

                await clipboard.SetTextAsync(about.BuildSummary);

                // Async void resumes on the UI thread under Avalonia's sync context, so touching the
                // confirmation after the delay is safe.
                CopyConfirm.IsVisible = true;
                await Task.Delay(1500);
                CopyConfirm.IsVisible = false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to copy the build summary");
            }
        }

        private void OnClose(object? sender, RoutedEventArgs e) => Close();
    }
}
