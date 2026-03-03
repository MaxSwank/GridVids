using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using GridVids.ViewModels;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GridVids.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Window Control Events
        var titleBar = this.FindControl<Control>("TitleBar");
        if (titleBar != null)
        {
            titleBar.PointerPressed += (s, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    BeginMoveDrag(e);
                }
            };
        }

        var btnMin = this.FindControl<Button>("BtnMinimize");
        if (btnMin != null) btnMin.Click += (s, e) => WindowState = WindowState.Minimized;

        var btnMax = this.FindControl<Button>("BtnMaximize");
        if (btnMax != null) btnMax.Click += (s, e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        var btnClose = this.FindControl<Button>("BtnClose");
        if (btnClose != null) btnClose.Click += (s, e) => Close();

        if (DataContext is MainViewModel vm)
        {
            vm.ShowFolderPickerAsync = ShowFolderPickerAsync;
        }

        DataContextChanged += (s, e) =>
        {
            if (DataContext is MainViewModel newVm)
            {
                newVm.ShowFolderPickerAsync = ShowFolderPickerAsync;
                if (Bounds.Width > 0) newVm.ContainerWidth = Bounds.Width;
                if (Bounds.Height > 0) newVm.ContainerHeight = Bounds.Height;
            }
        };

        this.SizeChanged += (s, e) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.ContainerWidth = e.NewSize.Width;
                vm.ContainerHeight = e.NewSize.Height;
            }
        };


        // Polling Timer for Auto-Hide
        _pollingTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _pollingTimer.Tick += PollingTimer_Tick;
        _pollingTimer.Start();
    }

    private Avalonia.Threading.DispatcherTimer _pollingTimer;
    private Point _lastMousePosition;
    private DateTime _lastMoveTime = DateTime.Now;

    // Auto-Hide Configuration
    private const double InactivityThresholdSeconds = 2.0;

    private void PollingTimer_Tick(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // 0. Check Auto-Hide Enabled
        if (!vm.IsAutoHideEnabled)
        {
            // Ensure visible if disabled
            if (!vm.IsControlBarVisible) vm.IsControlBarVisible = true;
            if (!vm.IsTitleBarVisible) vm.IsTitleBarVisible = true;
            return;
        }

        // 1. Get Global Cursor Position
        if (GetCursorPos(out POINT lpPoint))
        {
            var screenPoint = new PixelPoint(lpPoint.X, lpPoint.Y);
            var clientPoint = this.PointToClient(screenPoint);

            // 2. Check if Mouse is Inside Window (including Title Bar area)
            // We expand the bounds to capture the title bar (negative Y) and borders.
            bool isInside = clientPoint.X >= -10 &&
                           clientPoint.Y >= -60 &&
                           clientPoint.X < this.Bounds.Width + 10 &&
                           clientPoint.Y < this.Bounds.Height + 10;

            // 3. Check for Movement
            bool moved = Math.Abs(clientPoint.X - _lastMousePosition.X) > 2 ||
                         Math.Abs(clientPoint.Y - _lastMousePosition.Y) > 2;

            // 3. Update State
            // Always update last known position so we can detect movement
            if (moved)
            {
                _lastMousePosition = clientPoint;

                // If we are OUTSIDE, movement shouldn't keep the bar awake.
                if (isInside)
                {
                    _lastMoveTime = DateTime.Now;

                    // 3. Check for Activity vs Last State
                    // If moved recently (CurrentTime - LastMoveTime < 0.1s implies movement detected), Show UI
                    // Actually we update _lastMoveTime in step 1.
                    // We just check if we need to UN-HIDE
                    if (!vm.IsControlBarVisible || !vm.IsTitleBarVisible)
                    {
                        // If we just moved (idle ~= 0), show it.
                        // But parsing "idleSeconds" below is safer.
                        // Let's rely on the detector: if mouse moved, _lastMoveTime ≈ Now.
                        if ((DateTime.Now - _lastMoveTime).TotalSeconds < 0.5)
                        {
                            vm.IsControlBarVisible = true;
                            vm.IsTitleBarVisible = true;
                        }
                    }
                }
            }

            // 4. Determine Visibility State
            // We use a single unified check. 
            // If (Now - LastInsideMoveTime) > 2s, we hide.
            // This handles both "Stationary Inside" and "Moved Outside" cases.

            var idleSeconds = double.Parse((DateTime.Now - _lastMoveTime).TotalSeconds.ToString());

            if (idleSeconds > InactivityThresholdSeconds)
            {
                if (vm.IsControlBarVisible || vm.IsTitleBarVisible)
                {
                    vm.IsControlBarVisible = false;
                    vm.IsTitleBarVisible = false;
                }
            }
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    private async Task<string?> ShowFolderPickerAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Video Folder",
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();
        return folder?.Path.LocalPath;
    }
}
