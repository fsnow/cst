using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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