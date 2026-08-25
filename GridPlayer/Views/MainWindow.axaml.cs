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

        this.Closing += (s, e) =>
        {
            _pollingTimer?.Stop();
            if (DataContext is MainViewModel vm)
            {
                vm.CleanupAllProcesses();
            }
            else
            {
                GridVids.Services.PlaybackService.KillAllMpvProcesses();
            }
        };

        this.Closed += (s, e) =>
        {
            GridVids.Services.PlaybackService.KillAllMpvProcesses();
        };

        this.Opened += (s, e) =>
        {
            if (DataContext is MainViewModel vm)
            {
                if (vm.SelectedDisplayMode == "Scrolling Wall" || vm.IsScrollEnabled)
                {
                    WindowState = WindowState.Maximized;
                }
                UpdateFullScreenState();
            }
        };

        if (DataContext is MainViewModel vm)
        {
            vm.ShowFolderPickerAsync = ShowFolderPickerAsync;
            if (vm.SelectedDisplayMode == "Scrolling Wall" || vm.IsScrollEnabled)
            {
                WindowState = WindowState.Maximized;
            }
            UpdateFullScreenState();
        }

        DataContextChanged += (s, e) =>
        {
            if (DataContext is MainViewModel newVm)
            {
                newVm.ShowFolderPickerAsync = ShowFolderPickerAsync;
                if (newVm.SelectedDisplayMode == "Scrolling Wall" || newVm.IsScrollEnabled)
                {
                    WindowState = WindowState.Maximized;
                }
                if (Bounds.Width > 0) newVm.ContainerWidth = Bounds.Width;
                if (Bounds.Height > 0) newVm.ContainerHeight = Bounds.Height;
                UpdateFullScreenState();
            }
        };

        this.PropertyChanged += (s, e) =>
        {
            if (e.Property == Window.WindowStateProperty)
            {
                UpdateFullScreenState();
            }
        };

        this.PositionChanged += (s, e) => UpdateFullScreenState();

        this.SizeChanged += (s, e) =>
        {
            if (DataContext is MainViewModel activeVm)
            {
                activeVm.ContainerWidth = e.NewSize.Width;
                activeVm.ContainerHeight = e.NewSize.Height;
                UpdateFullScreenState();
            }
        };

        // Polling Timer for Auto-Hide and Clicks
        _pollingTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(30)
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

        if (GetCursorPos(out POINT lpPoint))
        {
            var screenPoint = new PixelPoint(lpPoint.X, lpPoint.Y);
            var clientPoint = this.PointToClient(screenPoint);

            // 1. Movement detection for Auto-Hide
            bool isInside = clientPoint.X >= -10 &&
                           clientPoint.Y >= -60 &&
                           clientPoint.X < this.Bounds.Width + 10 &&
                           clientPoint.Y < this.Bounds.Height + 10;

            bool moved = Math.Abs(clientPoint.X - _lastMousePosition.X) > 2 ||
                          Math.Abs(clientPoint.Y - _lastMousePosition.Y) > 2;

            if (moved)
            {
                _lastMousePosition = clientPoint;
                if (isInside)
                {
                    _lastMoveTime = DateTime.Now;
                    if (!vm.IsControlBarVisible || !vm.IsTitleBarVisible)
                    {
                        vm.IsControlBarVisible = true;
                        vm.IsTitleBarVisible = true;
                    }
                }
            }

            // 2. Metadata Hover Logic (3 second delay)
            IntPtr hoveredHwnd = WindowFromPoint(lpPoint);
            VideoSlotViewModel? hoveredSlot = null;

            if (hoveredHwnd != IntPtr.Zero)
            {
                IntPtr current = hoveredHwnd;
                while (current != IntPtr.Zero)
                {
                    hoveredSlot = vm.VideoSlots.Concat(vm.CollageSlots).Concat(vm.StackSlots).Concat(vm.ScrollSlots).FirstOrDefault(s => s.WindowHandle == current);
                    if (hoveredSlot != null) break;
                    
                    // Break if we reach the main window handle to avoid climbing too high
                    if (current == this.TryGetPlatformHandle()?.Handle) break;

                    current = GetParent(current);
                }
            }

            foreach (var slot in vm.VideoSlots.Concat(vm.CollageSlots).Concat(vm.StackSlots).Concat(vm.ScrollSlots))
            {
                if (slot == hoveredSlot)
                {
                    if (!slot.IsHovered)
                    {
                        if (moved)
                        {
                            slot.IsHovered = true;
                            slot.HoverStartTime = DateTime.Now;
                        }
                    }
                    else if (!slot.ShowMetadataOverlay && (DateTime.Now - slot.HoverStartTime).TotalSeconds >= 0.5)
                    {
                        slot.UpdateOverlay(true);
                    }
                    else if (slot.ShowMetadataOverlay && (DateTime.Now - slot.HoverStartTime).TotalSeconds >= 5.5)
                    {
                        slot.UpdateOverlay(false);
                    }
                }
                else if (slot.IsHovered)
                {
                    slot.IsHovered = false;
                    slot.UpdateOverlay(false);
                }
            }

            // 3. Auto-Hide
            if (vm.IsAutoHideEnabled)
            {
                // Pause auto-hide timer if mouse is hovering over top settings/titlebar area (Y <= 90)
                if (clientPoint.Y >= 0 && clientPoint.Y <= 90 && clientPoint.X >= 0 && clientPoint.X <= this.Bounds.Width)
                {
                    _lastMoveTime = DateTime.Now;
                }

                var idleSeconds = (DateTime.Now - _lastMoveTime).TotalSeconds;
                if (idleSeconds > InactivityThresholdSeconds)
                {
                    vm.IsControlBarVisible = false;
                    vm.IsTitleBarVisible = false;
                }
            }
            else
            {
                if (!vm.IsControlBarVisible) vm.IsControlBarVisible = true;
                if (!vm.IsTitleBarVisible) vm.IsTitleBarVisible = true;
            }

            // 4. Hide videos covering settings bar on hover
            var controlBar = this.FindControl<Control>("ControlBar");
            var titleBar = this.FindControl<Control>("TitleBar");
            bool isControlBarActive = vm.IsControlBarVisible && controlBar != null && controlBar.IsVisible && controlBar.Bounds.Height > 0;

            bool isMouseOverSettingsBar = false;
            RECT settingsBarScreenRect = default;

            if (isControlBarActive)
            {
                double topY = (titleBar != null && titleBar.IsVisible) ? titleBar.Bounds.Top : controlBar!.Bounds.Top;
                double bottomY = controlBar!.Bounds.Bottom;

                var topLeftScreen = this.PointToScreen(new Point(0, topY));
                var bottomRightScreen = this.PointToScreen(new Point(this.Bounds.Width, bottomY));

                settingsBarScreenRect = new RECT
                {
                    Left = topLeftScreen.X,
                    Top = topLeftScreen.Y,
                    Right = bottomRightScreen.X,
                    Bottom = bottomRightScreen.Y
                };

                isMouseOverSettingsBar = lpPoint.X >= settingsBarScreenRect.Left &&
                                         lpPoint.X <= settingsBarScreenRect.Right &&
                                         lpPoint.Y >= settingsBarScreenRect.Top &&
                                         lpPoint.Y <= settingsBarScreenRect.Bottom;
            }

            var allSlots = vm.VideoSlots
                .Concat(vm.CollageSlots)
                .Concat(vm.StackSlots)
                .Concat(vm.ScrollSlots);

            foreach (var slot in allSlots)
            {
                bool isCoveringSettingsBar = false;

                if (isMouseOverSettingsBar)
                {
                    if (slot.WindowHandle != IntPtr.Zero)
                    {
                        if (GetWindowRect(slot.WindowHandle, out RECT videoRect))
                        {
                            isCoveringSettingsBar = videoRect.Left < settingsBarScreenRect.Right &&
                                                    videoRect.Right > settingsBarScreenRect.Left &&
                                                    videoRect.Top < settingsBarScreenRect.Bottom &&
                                                    videoRect.Bottom > settingsBarScreenRect.Top;
                        }
                    }

                    if (!isCoveringSettingsBar)
                    {
                        // Fallback: check canvas bounds relative to settings bar
                        double row2Top = controlBar!.Bounds.Bottom;
                        double slotClientTop = row2Top + slot.EffectiveY;
                        double slotClientBottom = slotClientTop + slot.EffectiveHeight;
                        double slotClientLeft = slot.EffectiveX;
                        double slotClientRight = slotClientLeft + slot.EffectiveWidth;

                        double settingsBarTop = (titleBar != null && titleBar.IsVisible) ? titleBar.Bounds.Top : controlBar.Bounds.Top;
                        double settingsBarBottom = controlBar.Bounds.Bottom;

                        bool yOverlap = slotClientTop < settingsBarBottom && slotClientBottom > settingsBarTop;
                        bool xOverlap = slotClientLeft < this.Bounds.Width && slotClientRight > 0;

                        if (yOverlap && xOverlap)
                        {
                            isCoveringSettingsBar = true;
                        }
                    }
                }

                if (isCoveringSettingsBar)
                {
                    if (!slot.IsHiddenBySettingsBar)
                    {
                        slot.IsHiddenBySettingsBar = true;
                        if (slot.WindowHandle != IntPtr.Zero)
                        {
                            ShowWindow(slot.WindowHandle, 0); // SW_HIDE
                        }
                    }
                }
                else
                {
                    if (slot.IsHiddenBySettingsBar)
                    {
                        slot.IsHiddenBySettingsBar = false;
                        if (slot.WindowHandle != IntPtr.Zero)
                        {
                            ShowWindow(slot.WindowHandle, 5); // SW_SHOW
                        }
                    }
                }
            }
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    internal static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
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

    private void UpdateFullScreenState()
    {
        if (DataContext is MainViewModel vm)
        {
            bool isMaxOrFull = WindowState == WindowState.FullScreen || WindowState == WindowState.Maximized;
            bool touchesTopAndBottom = false;

            if (isMaxOrFull)
            {
                touchesTopAndBottom = true;
            }
            else
            {
                var screen = Screens.ScreenFromWindow(this);
                if (screen != null)
                {
                    double scaling = RenderScaling > 0 ? RenderScaling : screen.Scaling;

                    double boundsTop = screen.Bounds.Y;
                    double boundsBottom = screen.Bounds.Y + screen.Bounds.Height;

                    double workTop = screen.WorkingArea.Y;
                    double workBottom = screen.WorkingArea.Y + screen.WorkingArea.Height;

                    double windowTop = Position.Y;
                    double windowBottom = Position.Y + (Bounds.Height * scaling);

                    bool touchesTop = (windowTop <= boundsTop + 20) || (Math.Abs(windowTop - workTop) <= 20);
                    bool touchesBottom = (windowBottom >= boundsBottom - 20) || (Math.Abs(windowBottom - workBottom) <= 20);

                    touchesTopAndBottom = touchesTop && touchesBottom;
                }
            }

            vm.IsFullScreen = touchesTopAndBottom;
        }
    }
}
