using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
using GridVids.Interop;
using GridVids.ViewModels;
using System;
using System.Linq;
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

        this.Opened += async (s, e) =>
        {
            if (DataContext is MainViewModel vm)
            {
                if (vm.SelectedDisplayMode == "Scrolling Wall" || vm.IsScrollEnabled)
                {
                    WindowState = WindowState.Maximized;
                }
                UpdateFullScreenState();
                UpdateDebugPopupPosition();

                if (!string.IsNullOrWhiteSpace(vm.VideoPath))
                {
                    await vm.Play();
                }
            }
        };

        void AttachVm(MainViewModel vm)
        {
            vm.ShowFolderPickerAsync = ShowFolderPickerAsync;
            if (vm.SelectedDisplayMode == "Scrolling Wall" || vm.IsScrollEnabled)
            {
                WindowState = WindowState.Maximized;
            }
            if (Bounds.Width > 0) vm.ContainerWidth = Bounds.Width;
            if (Bounds.Height > 0) vm.ContainerHeight = Bounds.Height;
            UpdateFullScreenState();

            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.SelectedDisplayMode) || e.PropertyName == nameof(MainViewModel.IsScrollEnabled))
                {
                    if (vm.SelectedDisplayMode == "Scrolling Wall" || vm.IsScrollEnabled)
                    {
                        WindowState = WindowState.Maximized;
                    }
                }
            };
        }

        if (DataContext is MainViewModel initialVm)
        {
            AttachVm(initialVm);
        }

        DataContextChanged += (s, e) =>
        {
            if (DataContext is MainViewModel newVm)
            {
                AttachVm(newVm);
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
            UpdateDebugPopupPosition();
        };

        var debugPopup = this.FindControl<Popup>("DebugPopup");
        if (debugPopup != null)
        {
            debugPopup.Opened += (s, e) => UpdateDebugPopupPosition();
            if (debugPopup.Child is Control child)
            {
                child.SizeChanged += (s, e) => UpdateDebugPopupPosition();
            }
        }

        // Polling Timer for Auto-Hide and Clicks
        _pollingTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };
        _pollingTimer.Tick += PollingTimer_Tick;
        _pollingTimer.Start();
    }

    private void UpdateDebugPopupPosition()
    {
        var popup = this.FindControl<Popup>("DebugPopup");
        if (popup != null)
        {
            popup.HorizontalOffset = 0;
            popup.VerticalOffset = 0;
        }
    }

    private Avalonia.Threading.DispatcherTimer _pollingTimer;
    private Point _lastMousePosition;
    private DateTime _lastMoveTime = DateTime.Now;

    // Auto-Hide Configuration
    private const double InactivityThresholdSeconds = 2.0;

    private void PollingTimer_Tick(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (Win32Interop.GetCursorPos(out Win32Interop.POINT lpPoint))
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
            IntPtr hoveredHwnd = Win32Interop.WindowFromPoint(lpPoint);
            VideoSlotViewModel? hoveredSlot = null;

            if (hoveredHwnd != IntPtr.Zero)
            {
                IntPtr current = hoveredHwnd;
                while (current != IntPtr.Zero)
                {
                    hoveredSlot = vm.VideoSlots.Concat(vm.StackSlots).Concat(vm.ScrollSlots).FirstOrDefault(s => s.WindowHandle == current);
                    if (hoveredSlot != null) break;
                    
                    // Break if we reach the main window handle to avoid climbing too high
                    if (current == this.TryGetPlatformHandle()?.Handle) break;

                    current = Win32Interop.GetParent(current);
                }
            }

            foreach (var slot in vm.VideoSlots.Concat(vm.StackSlots).Concat(vm.ScrollSlots))
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

            // 3. Auto-Hide & Settings Bar Hover Detection
            var controlBar = this.FindControl<Control>("ControlBar");
            var titleBar = this.FindControl<Control>("TitleBar");

            // Calculate effective settings area height in client coordinates
            // When controlBar is rendered, Bounds.Bottom gives the bottom coordinate of Row 1 (TitleBar + ControlBar)
            double controlBarBottom = (controlBar != null && controlBar.Bounds.Bottom > 0) ? controlBar.Bounds.Bottom : 0;
            double effectiveSettingsHeight = Math.Max(100.0, controlBarBottom);

            // Determine if mouse is over the top settings/titlebar region
            bool isMouseInSettingsArea = clientPoint.X >= 0 &&
                                         clientPoint.X <= this.Bounds.Width &&
                                         clientPoint.Y >= 0 &&
                                         clientPoint.Y <= effectiveSettingsHeight;

            if (isMouseInSettingsArea)
            {
                // Instantly wake up settings bar when mouse hovers over it
                _lastMoveTime = DateTime.Now;
                if (!vm.IsControlBarVisible || !vm.IsTitleBarVisible)
                {
                    vm.IsControlBarVisible = true;
                    vm.IsTitleBarVisible = true;
                }
            }

            if (vm.IsAutoHideEnabled)
            {
                var idleSeconds = (DateTime.Now - _lastMoveTime).TotalSeconds;
                if (idleSeconds > InactivityThresholdSeconds && !isMouseInSettingsArea)
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
            bool isMouseOverSettingsBar = isMouseInSettingsArea;
            Win32Interop.RECT settingsBarScreenRect = default;

            if (isMouseOverSettingsBar)
            {
                // Top screen coordinate of window content
                var topLeftScreen = this.PointToScreen(new Point(0, 0));
                // Bottom-right screen coordinate of settings area
                var bottomRightScreen = this.PointToScreen(new Point(this.Bounds.Width, effectiveSettingsHeight));

                settingsBarScreenRect = new Win32Interop.RECT
                {
                    Left = Math.Min(topLeftScreen.X, bottomRightScreen.X),
                    Top = Math.Min(topLeftScreen.Y, bottomRightScreen.Y),
                    Right = Math.Max(topLeftScreen.X, bottomRightScreen.X),
                    Bottom = Math.Max(topLeftScreen.Y, bottomRightScreen.Y)
                };
            }

            var allSlots = vm.VideoSlots
                .Concat(vm.StackSlots)
                .Concat(vm.ScrollSlots);

            foreach (var slot in allSlots)
            {
                bool isCoveringSettingsBar = false;

                if (isMouseOverSettingsBar)
                {
                    if (slot.WindowHandle != IntPtr.Zero)
                    {
                        if (Win32Interop.GetWindowRect(slot.WindowHandle, out Win32Interop.RECT videoRect))
                        {
                            isCoveringSettingsBar = videoRect.Left < settingsBarScreenRect.Right &&
                                                    videoRect.Right > settingsBarScreenRect.Left &&
                                                    videoRect.Top < settingsBarScreenRect.Bottom &&
                                                    videoRect.Bottom > settingsBarScreenRect.Top;
                        }
                    }

                    if (!isCoveringSettingsBar)
                    {
                        // Fallback: check slot coordinates in client space
                        // In GridVids, GridContainer/StackContainer/ScrollContainer is Row 2,
                        // but during stack/scroll animations or scrolling wall, slots can span or overlap client Y <= effectiveSettingsHeight.
                        double slotClientTop = controlBarBottom + slot.EffectiveY;
                        double slotClientBottom = slotClientTop + slot.EffectiveHeight;
                        double slotClientLeft = slot.EffectiveX;
                        double slotClientRight = slotClientLeft + slot.EffectiveWidth;

                        bool yOverlap = slotClientTop < effectiveSettingsHeight && slotClientBottom > 0;
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
                            Win32Interop.ShowWindow(slot.WindowHandle, Win32Interop.SW_HIDE);
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
                            Win32Interop.ShowWindow(slot.WindowHandle, Win32Interop.SW_SHOW);
                        }
                    }
                }
            }
        }
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
