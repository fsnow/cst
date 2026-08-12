using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace CST.Avalonia.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly ILogger _logger = Log.ForContext<SettingsWindow>();
        
        public SettingsWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Applies the saved size. (#42)
        ///
        /// <para>
        /// Called from the CONSTRUCTOR, not OnOpened. WindowStartupLocation is CenterOwner, which centres
        /// the window using whatever size it has when shown — so resizing afterwards left it off-centre by
        /// half the difference, with a visible jump, on every single open. (fable review)
        /// </para>
        ///
        /// <para>
        /// Size only, no position. The main window persists X/Y and needs bounds validation against the
        /// current screens so a disconnected monitor cannot strand it off-screen; a dialog centred on its
        /// owner cannot have that failure. The size is still clamped to the owner's screen, because a
        /// window saved on a large display and reopened on a small one would otherwise extend past the
        /// edges — the validator only rejects NaN, infinity and non-positive values.
        /// </para>
        /// </summary>
        internal void ApplySavedSizeBeforeShowing(Window? owner)
        {
            try
            {
                var saved = (App.ServiceProvider?.GetService(typeof(IApplicationStateService))
                            as IApplicationStateService)?.Current?.SettingsWindow;
                if (saved == null) return;

                var width = saved.Width;
                var height = saved.Height;

                var work = (owner?.Screens?.ScreenFromWindow(owner) ?? owner?.Screens?.Primary)?.WorkingArea;
                if (work.HasValue)
                {
                    var scale = owner?.RenderScaling ?? 1.0;
                    width = Math.Min(width, work.Value.Width / scale);
                    height = Math.Min(height, work.Value.Height / scale);
                }

                Width = Math.Max(width, MinWidth);
                Height = Math.Max(height, MinHeight);
            }
            catch (Exception ex)
            {
                Log.Warning("Could not restore the Settings dialog size | {Details}", ex.Message);
            }
        }

        /// <summary>
        /// Captures the dialog's size on close.
        ///
        /// Read in Closing rather than Closed: Width/Height are styled properties that survive teardown,
        /// but reading geometry after the native window is destroyed is the trap #535 documents for the
        /// main window, and there is no reason to court it here.
        /// </summary>
        protected override void OnClosing(global::Avalonia.Controls.WindowClosingEventArgs e)
        {
            base.OnClosing(e);
            try
            {
                var stateService = App.ServiceProvider?.GetService(typeof(IApplicationStateService))
                                   as IApplicationStateService;
                var state = stateService?.Current;
                if (state == null) return;

                state.SettingsWindow ??= new SettingsWindowState();
                state.SettingsWindow.Width = Width;
                state.SettingsWindow.Height = Height;
                stateService!.MarkDirty();
            }
            catch (Exception ex)
            {
                Log.Warning("Could not save the Settings dialog size | {Details}", ex.Message);
            }
        }

        /// <summary>
        /// Releases the view model's subscriptions. The Appearance panel listens for zoom changes so its
        /// per-script size readout stays live (#42/#572), and that subscription is on a singleton — without
        /// this the panel would leak one instance per Settings open.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            (DataContext as SettingsViewModel)?.Dispose();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            
            // Set up actions for the view model when DataContext is set
            if (DataContext is SettingsViewModel viewModel)
            {
                viewModel.CloseWindow = Close;
                viewModel.BrowseForXmlDirectory = async () => await BrowseForXmlDirectory();
                viewModel.BrowseForIndexDirectory = async () => await BrowseForIndexDirectory();
            }
        }

        // #277: copy the pre-populated Claude Desktop MCP config to the clipboard. Done here rather than
        // in the ViewModel because the clipboard is reached through the window's TopLevel, which the VM
        // has no handle to. The confirmation TextBlock lives inside the AI DataTemplate (a separate
        // namescope from the window), so we find it as a sibling of the button rather than by FindControl.
        private async void OnCopyMcpConfig(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button button || button.DataContext is not AiSettingsViewModel ai)
                    return;

                var clipboard = Clipboard ?? TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard == null)
                {
                    _logger.Warning("No clipboard available to copy MCP configuration");
                    return;
                }

                await clipboard.SetTextAsync(ai.McpClientConfigJson);
                _logger.Information("Copied MCP configuration to clipboard");

                // Flash the "Copied ✓" sibling for ~1.5s. Async void handler resumes on the UI thread
                // (Avalonia sync context), so touching UI after the delay is safe.
                if (button.Parent is Panel panel &&
                    panel.Children.OfType<TextBlock>().FirstOrDefault(t => t.Name == "CopyMcpConfirm") is { } confirm)
                {
                    confirm.IsVisible = true;
                    await Task.Delay(1500);
                    confirm.IsVisible = false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to copy MCP configuration");
            }
        }

        private async Task BrowseForXmlDirectory()
        {
            try
            {
                _logger.Debug("Opening folder picker for XML Books Directory");
                
                var options = new FolderPickerOpenOptions
                {
                    Title = "Select XML Books Directory",
                    AllowMultiple = false
                };

                // Try to set the suggested start location to the current directory
                var viewModel = DataContext as SettingsViewModel;
                if (viewModel != null)
                {
                    foreach (var category in viewModel.Categories)
                    {
                        if (category.Content is GeneralSettingsViewModel generalSettings)
                        {
                            var currentPath = generalSettings.XmlBooksDirectory;
                            if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath))
                            {
                                try
                                {
                                    var folder = await StorageProvider.TryGetFolderFromPathAsync(currentPath);
                                    if (folder != null)
                                    {
                                        options.SuggestedStartLocation = folder;
                                        _logger.Debug("Set folder picker start location to: {Path}", currentPath);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.Warning(ex, "Failed to set folder picker start location");
                                }
                            }
                            break;
                        }
                    }
                }

                var result = await StorageProvider.OpenFolderPickerAsync(options);
                if (result.Count > 0)
                {
                    var folder = result[0];
                    try
                    {
                        var path = folder.Path.LocalPath;
                        _logger.Information("User selected XML directory: {Path}", path);
                        
                        // Update the GeneralSettings view model
                        var settingsVm = DataContext as SettingsViewModel;
                        if (settingsVm != null)
                        {
                            foreach (var category in settingsVm.Categories)
                            {
                                if (category.Content is GeneralSettingsViewModel generalSettings)
                                {
                                    generalSettings.XmlBooksDirectory = path;
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "Failed to get folder path, using folder name as fallback");
                        // Fallback for older Avalonia versions or different storage providers
                        var settingsVm = DataContext as SettingsViewModel;
                        if (settingsVm != null)
                        {
                            foreach (var category in settingsVm.Categories)
                            {
                                if (category.Content is GeneralSettingsViewModel generalSettings)
                                {
                                    generalSettings.XmlBooksDirectory = folder.Name;
                                    break;
                                }
                            }
                        }
                    }
                }
                else
                {
                    _logger.Debug("User cancelled folder selection");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to open folder picker");
            }
        }

        private async Task BrowseForIndexDirectory()
        {
            try
            {
                _logger.Debug("Opening folder picker for Index Directory");
                
                var options = new FolderPickerOpenOptions
                {
                    Title = "Select Index Directory",
                    AllowMultiple = false
                };

                // Try to set the suggested start location to the current directory
                var viewModel = DataContext as SettingsViewModel;
                if (viewModel != null)
                {
                    foreach (var category in viewModel.Categories)
                    {
                        if (category.Content is GeneralSettingsViewModel generalSettings)
                        {
                            var currentPath = generalSettings.IndexDirectory;
                            if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath))
                            {
                                try
                                {
                                    var folder = await StorageProvider.TryGetFolderFromPathAsync(currentPath);
                                    if (folder != null)
                                    {
                                        options.SuggestedStartLocation = folder;
                                        _logger.Debug("Set folder picker start location to: {Path}", currentPath);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.Warning(ex, "Failed to set folder picker start location");
                                }
                            }
                            break;
                        }
                    }
                }

                var result = await StorageProvider.OpenFolderPickerAsync(options);
                if (result.Count > 0)
                {
                    var folder = result[0];
                    try
                    {
                        var path = folder.Path.LocalPath;
                        _logger.Information("User selected Index directory: {Path}", path);
                        
                        // Update the GeneralSettings view model
                        var settingsVm = DataContext as SettingsViewModel;
                        if (settingsVm != null)
                        {
                            foreach (var category in settingsVm.Categories)
                            {
                                if (category.Content is GeneralSettingsViewModel generalSettings)
                                {
                                    generalSettings.IndexDirectory = path;
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "Failed to get folder path, using folder name as fallback");
                        // Fallback for older Avalonia versions or different storage providers
                        var settingsVm = DataContext as SettingsViewModel;
                        if (settingsVm != null)
                        {
                            foreach (var category in settingsVm.Categories)
                            {
                                if (category.Content is GeneralSettingsViewModel generalSettings)
                                {
                                    generalSettings.IndexDirectory = folder.Name;
                                    break;
                                }
                            }
                        }
                    }
                }
                else
                {
                    _logger.Debug("User cancelled folder selection");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to open folder picker");
            }
        }
    }
}