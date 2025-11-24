using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Dock.Model.Core;
using Dock.Avalonia.Controls;
using CST.Avalonia.Views;
using CST.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CST.Avalonia.Services
{
    /// <summary>
    /// Host window implementation for multi-window docking support
    /// </summary>
    public class CstHostWindow : Window, IHostWindow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public object? Context { get; set; }
        public new string Title { get; set; } = "CST - Chaṭṭha Saṅgāyana Tipiṭaka";
        public double X { get; set; }
        public double Y { get; set; }
        public new bool Topmost { get; set; }
        public IDock? Layout { get; set; }
        public IFactory? Factory { get; set; }

        // IHostWindow implementation
        public IDockManager? DockManager { get; set; }
        public IHostWindowState? HostWindowState { get; set; }
        public bool IsTracked { get; set; } = true;
        public IDockWindow? Window { get; set; }

        private DockControl? _dockControl;

        public CstHostWindow()
        {
            Width = 1200;
            Height = 800;
            MinWidth = 600;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.Manual;

            // Add View menu to floating window (same as main window)
            SetupViewMenu();

            // Initialize the window layout
            InitializeWindow();
        }

        private void SetupViewMenu()
        {
            // Create native menu for this floating window
            var nativeMenu = new NativeMenu();

            // View menu
            var viewMenuItem = new NativeMenuItem
            {
                Header = "View",
                Menu = new NativeMenu()
            };

            var selectBookItem = new NativeMenuItem
            {
                Header = "Select a Book",
                ToggleType = NativeMenuItemToggleType.CheckBox
            };

            var searchItem = new NativeMenuItem
            {
                Header = "Search",
                ToggleType = NativeMenuItemToggleType.CheckBox
            };

            viewMenuItem.Menu.Add(selectBookItem);
            viewMenuItem.Menu.Add(searchItem);
            nativeMenu.Add(viewMenuItem);

            // Tools menu
            var toolsMenuItem = new NativeMenuItem
            {
                Header = "Tools",
                Menu = new NativeMenu()
            };

            var goToItem = new NativeMenuItem
            {
                Header = "Go To...",
                Gesture = KeyGesture.Parse("Cmd+G")
            };

            toolsMenuItem.Menu.Add(goToItem);
            nativeMenu.Add(toolsMenuItem);

            NativeMenu.SetMenu(this, nativeMenu);

            // Menu event handlers will be set up by App when this window is shown
        }

        private void InitializeWindow()
        {
            // Create a simple layout for the floating window
            _dockControl = new DockControl();
            Content = _dockControl;
            
            // Set up event handlers
            Closing += OnClosing;
            PositionChanged += OnPositionChanged;
        }

        private void OnPositionChanged(object? sender, PixelPointEventArgs e)
        {
            X = Position.X;
            Y = Position.Y;
        }

        private void OnClosing(object? sender, WindowClosingEventArgs e)
        {
            // If this window has any documents, try to move them back to the main window
            if (Factory is CstDockFactory factory && Layout != null)
            {
                factory.CloseHostWindow(this);
            }
        }

        public void Present(bool isDialog)
        {
            if (isDialog)
            {
                // For modal dialogs, use ShowDialog (though not applicable for floating windows)
                Show();
            }
            else
            {
                Show();
                Activate();
                Focus();
            }
        }

        public void Exit()
        {
            Close();
        }

        public void SetPosition(double x, double y)
        {
            Position = new PixelPoint((int)x, (int)y);
            X = x;
            Y = y;
        }

        public void GetPosition(out double x, out double y)
        {
            x = Position.X;
            y = Position.Y;
        }

        public void SetSize(double width, double height)
        {
            Width = width;
            Height = height;
        }

        public void GetSize(out double width, out double height)
        {
            width = Width;
            height = Height;
        }

        public void SetTitle(string? title)
        {
            Title = title ?? "CST - Chaṭṭha Saṅgāyana Tipiṭaka";
            base.Title = Title;
        }

        public void SetLayout(IDock layout)
        {
            Layout = layout;
            if (_dockControl != null && layout != null)
            {
                _dockControl.Layout = layout;
                
                // Ensure the factory is set on the layout
                if (layout is IDock dock && Factory != null)
                {
                    dock.Factory = Factory;
                    
                    // Set up floating window monitoring when layout is assigned
                    if (Factory is CstDockFactory cstFactory)
                    {
                        // Set up floating window monitoring
                        try
                        {
                            // Call the public method to set up monitoring
                            cstFactory.SetupFloatingWindowMonitoring(layout);
                        }
                        catch (Exception)
                        {
                            // Floating window monitoring setup failed, but window will still function
                        }
                    }
                }
                else
                {
                    // Layout set without floating window monitoring
                }
            }
        }
    }
}