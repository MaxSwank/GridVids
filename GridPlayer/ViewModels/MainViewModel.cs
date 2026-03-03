using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GridPlayer.Models;
using GridPlayer.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace GridPlayer.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly SettingsService _settingsService;
        private readonly VideoLibraryService _videoLibraryService;
        private readonly PlaybackService _playbackService;
        private bool firstRun = true;
        private int _restoredDelay = 10;

        public ObservableCollection<VideoSlotViewModel> VideoSlots { get; } = new();
        public ObservableCollection<VideoSlotViewModel> CollageSlots { get; } = new();

        public MainViewModel()
        {
            _settingsService = new SettingsService();
            _videoLibraryService = new VideoLibraryService();
            _playbackService = new PlaybackService();

            // Load Settings
            var settings = _settingsService.LoadSettings();
            _isSwapEnabled = settings.IsSwapEnabled;
            _selectedGrid1 = !string.IsNullOrEmpty(settings.SelectedGrid1) ? settings.SelectedGrid1 : "2x2";
            _selectedGrid2 = !string.IsNullOrEmpty(settings.SelectedGrid2) ? settings.SelectedGrid2 : "3x3";
            _restoredDelay = settings.SelectedDelay > 0 ? settings.SelectedDelay : 10;
            _selectedDelay = 0; // Start with 0 (no delay) for immediate first action
            _isSloMoEnabled = settings.IsSloMoEnabled;


            InitializeOptions();
            InitializeSwapTimer();

            _rows = settings.Rows > 0 ? settings.Rows : 2;
            _columns = settings.Columns > 0 ? settings.Columns : 2;
            _videoPath = settings.VideoPath ?? string.Empty;

            if (!string.IsNullOrEmpty(_videoPath))
            {
                // Ensure cache is populated before first playback
                _ = _videoLibraryService.RefreshCacheAsync(_videoPath).ContinueWith(t =>
                {
                    if (!t.IsFaulted) _ = ExecutePlayback();
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }

            UpdateGrid();
        }

        [ObservableProperty]
        private int _rows = 2;

        private bool _suppressAutoRun = false;

        partial void OnRowsChanged(int value)
        {
            UpdateGrid();
            SaveSettings();
            if (!string.IsNullOrEmpty(VideoPath) && !_suppressAutoRun)
            {
                _ = ExecutePlayback();
            }
        }

        [ObservableProperty]
        private int _columns = 2;

        partial void OnColumnsChanged(int value)
        {
            UpdateGrid();
            SaveSettings();
            if (!string.IsNullOrEmpty(VideoPath) && !_suppressAutoRun)
            {
                _ = ExecutePlayback();
            }
        }

        [RelayCommand]
        public void UpdateGrid()
        {
            int total = Rows * Columns;

            if (IsSwapEnabled)
            {
                int c1 = GetGridCount(SelectedGrid1);
                int c2 = GetGridCount(SelectedGrid2);
                if (c1 > 0 && c2 > 0) total = c1 + c2;
            }

            EnsureSlotCount(total);
            UpdateVisibility();
        }

        [ObservableProperty]
        private string _videoPath = string.Empty;

        partial void OnVideoPathChanged(string value)
        {
            SaveSettings();
            _ = _videoLibraryService.RefreshCacheAsync(value);
        }

        public Func<Task<string?>>? ShowFolderPickerAsync { get; set; }

        [RelayCommand]
        public async Task SelectVideoFolder()
        {
            if (ShowFolderPickerAsync != null)
            {
                var path = await ShowFolderPickerAsync();
                Debug.WriteLine($"Selected Path: {path}");
                if (!string.IsNullOrEmpty(path))
                {
                    VideoPath = path;
                }
            }
        }

        private void SaveSettings()
        {
            var settings = new AppSettings
            {
                Rows = Rows,
                Columns = Columns,
                VideoPath = VideoPath,
                IsSwapEnabled = IsSwapEnabled,
                SelectedGrid1 = SelectedGrid1,
                SelectedGrid2 = SelectedGrid2,
                SelectedDelay = (firstRun && SelectedDelay == 0) ? _restoredDelay : SelectedDelay,
                IsSloMoEnabled = IsSloMoEnabled
            };
            _settingsService.SaveSettings(settings);
        }

        [ObservableProperty]
        private bool _isCollageEnabled;

        partial void OnIsCollageEnabledChanged(bool value)
        {
            if (value)
            {
                // Stop Grid Playback if running (though we can keep the grid slots alive in background? No, better to stop to save resources)
                if (IsVideoPlaying) Stop();
                _ = StartCollage();
            }
            else
            {
                StopCollage();
            }
        }

        [ObservableProperty]
        private double _containerWidth = 1500; // Default fallback

        [ObservableProperty]
        private double _containerHeight = 800; // Default fallback

        private Avalonia.Threading.DispatcherTimer? _collageTimer;
        private Random _rnd = new Random();

        private DateTime _lastTick;

        private async Task StartCollage()
        {
            if (_collageTimer == null)
            {
                _collageTimer = new Avalonia.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(33) // 30 FPS - smoother for heavy window resizing
                };
                _collageTimer.Tick += CollageTimer_Tick;
            }

            // Calculate needed slots
            // Approx coverage: 15 active slots?
            // "Do not start... until all necessary videos... have been loaded"

            // Initial Pass: Even Grid to cover the screen
            // Use 4 columns to ensure better aspect ratio coverage
            double effectiveW = ContainerWidth > 100 ? ContainerWidth : 1500;
            double effectiveH = ContainerHeight > 100 ? ContainerHeight : 800;

            // Target 4 cols, min width 800 check
            int cols = 4;
            if ((effectiveW / cols) < 800) cols = (int)(effectiveW / 800);
            if (cols < 1) cols = 1;

            // Determine rows based on aspect ratio approximation
            double approxSlotH = (effectiveW / cols) / (16.0 / 9.0);
            int rows = (int)Math.Ceiling(effectiveH / approxSlotH);

            var slots = new List<VideoSlotViewModel>();
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    // Pixel-perfect integer logic
                    double x1 = Math.Round((c * effectiveW) / cols);
                    double x2 = Math.Round(((c + 1) * effectiveW) / cols);
                    // double y1 = Math.Round((r * effectiveH) / rows); // This line is commented out in the new code

                    // Note: Ideally we want aspect ratio height, but for "covering the grid perfectly"
                    // we should probably just tile the available space?
                    // No, usually we want 16:9 slots.
                    // But if we have 4 cols, the height is fixed by aspect ratio.
                    // The 'rows' calculation above decided how many rows fit.
                    // Let's use exact aspect height relative to width, but ensure rows tile vertically if possible?
                    // Actually, if we want "Collage" look, the initial grid should probably be standard aspect ratio videos.
                    // If we stretch height to fill screen, videos stretch.
                    // If we leave gaps, it's not "covering".

                    // Reverting to Aspect Ratio based height tiling, but using integer snapping for X/W.
                    // Y/H will also be snapped.

                    double cellW = x2 - x1;
                    double cellH = cellW / (16.0 / 9.0); // Exact aspect height

                    double layoutY = r * cellH; // Stack vertically exactly

                    var s = new VideoSlotViewModel
                    {
                        CollageX = x1,
                        CollageY = layoutY,
                        CollageWidth = cellW,
                        CollageHeight = cellH,
                        SpawnTime = DateTime.Now,
                        Lifetime = TimeSpan.FromSeconds(15 + _rnd.Next(15)),
                        Index = slots.Count,
                        IsCollageVisible = false
                    };
                    slots.Add(s);
                }
            }

            foreach (var s in slots) CollageSlots.Add(s);

            // Get Videos
            var videos = await _videoLibraryService.GetRandomVideosAsync(slots.Count);

            // Wait for handles
            // We must wait for the UI to attach and the handles to be ready BEFORE starting playback,
            // otherwise MPV receives a 0 handle and opens a standalone "rogue" window.
            int retries = 0;
            while (CollageSlots.Any(s => s.WindowHandle == IntPtr.Zero) && retries < 20)
            {
                await Task.Delay(200);
                retries++;
            }

            // Start Playback
            await _playbackService.PlayAsync(CollageSlots, videos, IsSloMoEnabled);

            // Buffer Delay: Wait for content to load and play
            await Task.Delay(2000);

            // Show All
            foreach (var s in CollageSlots) s.IsCollageVisible = true;

            _lastTick = DateTime.Now;
            _collageTimer.Start();
        }

        private void StopCollage()
        {
            _collageTimer?.Stop();
            _playbackService.Stop(CollageSlots);
            CollageSlots.Clear();
        }

        private DateTime _lastSpawnTime;
        private double _nextSpawnDelaySeconds = 0;

        private void CollageTimer_Tick(object? sender, EventArgs e)
        {
            var now = DateTime.Now;
            double dt = (now - _lastTick).TotalSeconds;
            _lastTick = now;

            if (dt > 0.1) dt = 0.1;
            var toRemove = new List<VideoSlotViewModel>();

            foreach (var slot in CollageSlots)
            {
                // Lifecycle Phase 1: Loading Buffer
                if (!slot.IsCollageVisible)
                {
                    if ((now - slot.SpawnTime).TotalSeconds > 1.5)
                    {
                        slot.IsCollageVisible = true;
                        slot.Opacity = 0;
                    }
                    else
                    {
                        continue;
                    }
                }

                // Lifecycle Phase 2: Fade In
                if (!slot.IsDying && slot.Opacity < 1.0)
                {
                    slot.Opacity += 1.5 * dt;
                    if (slot.Opacity > 1.0) slot.Opacity = 1.0;
                }

                // Handoff Logic: If I am fully visible and replacing someone, tell them to die
                if (slot.Opacity >= 1.0 && slot.Replaces != null)
                {
                    slot.Replaces.IsDying = true;
                    slot.Replaces = null; // job done
                }

                // Coverage Logic: If I am fully covered by any newer (higher Z-index), opaque slot, I should die
                if (!slot.IsDying)
                {
                    int myIndex = CollageSlots.IndexOf(slot);
                    for (int i = myIndex + 1; i < CollageSlots.Count; i++)
                    {
                        var over = CollageSlots[i];
                        // If 'over' is opaque and completely contains 'slot'
                        if (over.Opacity >= 0.98 &&
                            over.CollageX <= slot.CollageX &&
                            over.CollageY <= slot.CollageY &&
                            (over.CollageX + over.CollageWidth) >= (slot.CollageX + slot.CollageWidth) &&
                            (over.CollageY + over.CollageHeight) >= (slot.CollageY + slot.CollageHeight))
                        {
                            slot.IsDying = true;
                            break;
                        }
                    }
                }

                // Hard Limit: Kill if running too long (> 30s)
                // if (!slot.IsDying && (now - slot.SpawnTime).TotalSeconds > 30)
                // {
                //     slot.IsDying = true;
                // }

                // Lifecycle Phase 3: Expiration
                // "Do not kill ... until replacement". 
                // So we just mark as Expired.
                if (!slot.IsExpired && !slot.IsDying && now > slot.SpawnTime + slot.Lifetime)
                {
                    slot.IsExpired = true;
                }

                // Lifecycle Phase 4: Dying (Fade Out)
                if (slot.IsDying)
                {
                    slot.Opacity -= 1.0 * dt;
                    if (slot.Opacity <= 0)
                    {
                        slot.Opacity = 0;
                        toRemove.Add(slot);
                    }
                }
            }

            // Remove dead
            foreach (var dead in toRemove)
            {
                if (dead.CurrentProcess != null && !dead.CurrentProcess.HasExited)
                    try { dead.CurrentProcess.Kill(); } catch { }
                CollageSlots.Remove(dead);
            }

            // Spawn Logic
            int targetCount = GetIdealCollageCount();

            // Rate Check
            if ((now - _lastSpawnTime).TotalSeconds >= _nextSpawnDelaySeconds)
            {
                // Priority 1: Replace Expired Slots
                var expiredCandidate = CollageSlots.FirstOrDefault(x => x.IsExpired && !x.IsDying && !x.HasIncomingReplacement);

                if (expiredCandidate != null)
                {
                    // Spawn replacement
                    _ = SpawnSingleCollageSlot(expiredCandidate);
                    expiredCandidate.HasIncomingReplacement = true;

                    _lastSpawnTime = now;
                    _nextSpawnDelaySeconds = 1.0 + _rnd.NextDouble() * 2.0;
                }
                // Priority 2: Fill Gaps (if below target count)
                else if (CollageSlots.Count < targetCount)
                {
                    _ = SpawnSingleCollageSlot(null);
                    // Double spawn for quicker filling if we have room
                    if (CollageSlots.Count < targetCount) _ = SpawnSingleCollageSlot(null);

                    _lastSpawnTime = now;
                    _nextSpawnDelaySeconds = 0.5 + _rnd.NextDouble() * 0.3;
                }
                // Else: Everyone is happy and fresh, do nothing.
            }
        }

        private int GetIdealCollageCount()
        {
            // User wants wider coverage and more videos to fill gaps.
            // Min video area = 640 * (640/1.77) = 640 * 360 = 230,400.

            double videoArea = 720.0 * 407.0;
            double screenArea = ContainerWidth * ContainerHeight;
            if (screenArea <= 0) screenArea = 1500 * 800; // Fallback

            // Increased multiplier to 4.0 to ensure full coverage as requested
            double count = (screenArea / videoArea) * 4.0;
            // Lower minimum count slightly as well
            return Math.Max(12, (int)count);
        }

        // Helper to get quadrant index (0=TL, 1=TR, 2=BL, 3=BR)
        private int GetQuadrant(double x, double y, double w, double h)
        {
            double cx = x + w / 2;
            double cy = y + h / 2;
            int qx = (cx > ContainerWidth / 2) ? 1 : 0;
            int qy = (cy > ContainerHeight / 2) ? 1 : 0;
            return qy * 2 + qx;
        }

        private async Task SpawnSingleCollageSlot(VideoSlotViewModel? target = null)
        {
            int? preferredQuad = null;
            if (target == null)
            {
                // Filling gaps
                int[] qCounts = new int[4];
                foreach (var slot in CollageSlots)
                {
                    int q = GetQuadrant(slot.CollageX, slot.CollageY, slot.CollageWidth, slot.CollageHeight);
                    if (q >= 0 && q < 4) qCounts[q]++;
                }

                int minVal = qCounts.Min();
                var candidates = qCounts.Select((val, idx) => new { val, idx }).Where(x => x.val == minVal).Select(x => x.idx).ToList();
                if (candidates.Any())
                {
                    preferredQuad = candidates[_rnd.Next(candidates.Count)];
                }
            }

            var s = CreateCollageSlot(target, preferredQuad);
            if (target != null)
            {
                s.Replaces = target;
            }

            CollageSlots.Add(s);

            // Wait for View to bind handle
            int retries = 0;
            while (s.WindowHandle == IntPtr.Zero && retries < 20)
            {
                await Task.Delay(100);
                retries++;
            }

            var v = await _videoLibraryService.GetRandomVideosAsync(1);
            if (v.Any())
            {
                await _playbackService.PlayAsync(new[] { s }, v, IsSloMoEnabled);
            }
        }

        private VideoSlotViewModel CreateCollageSlot(VideoSlotViewModel? target = null, int? preferredQuad = null)
        {
            double startW, startH, startX, startY;

            if (target != null)
            {
                // Target specific area (Replacement)
                // Use tighter variance to maintain grid structure ("replace that section")
                double variance = 10.0;

                startW = target.CollageWidth + (_rnd.NextDouble() * variance - (variance / 2));

                // Clamp W - Only enforce minimum, allow it to be as large as the target (e.g. half screen)
                if (startW < 600) startW = 600;
                // REMOVED upper clamp (1000) to allow full-size grid replacements

                startH = startW / (16.0 / 9.0);

                // Use slightly randomized position but stay close to original
                startX = target.CollageX + (_rnd.NextDouble() * variance - (variance / 2));
                startY = target.CollageY + (_rnd.NextDouble() * variance - (variance / 2));
            }
            else
            {
                // Random Generation (New Slot)
                // Range 600px - 1000px
                startW = 600 + _rnd.Next(401);
                startH = startW / (16.0 / 9.0);

                // Candidate sampling to minimize overlap
                double bestX = 0;
                double bestY = 0;
                double minOverlap = double.MaxValue;

                // Stratified Sampling to ensure coverage of all 4 regions
                // Divide screen into 4 quadrants and sample each one.
                int samplesPerQuad = 8;
                double halfW = ContainerWidth / 2.0;
                double halfH = ContainerHeight / 2.0;

                for (int qx = 0; qx < 2; qx++)
                {
                    for (int qy = 0; qy < 2; qy++)
                    {
                        int currentQuadIndex = qy * 2 + qx;
                        if (preferredQuad.HasValue && preferredQuad.Value != currentQuadIndex) continue;

                        // Define quadrant bounds (padded by the 10% rule)
                        double qMinX = (qx == 0) ? -0.1 * startW : halfW;
                        double qMaxX = (qx == 0) ? halfW : ContainerWidth - (0.9 * startW);

                        double qMinY = (qy == 0) ? -0.1 * startH : halfH;
                        double qMaxY = (qy == 0) ? halfH : ContainerHeight - (0.9 * startH);

                        // Safety Checks
                        if (qMaxX < qMinX) qMaxX = qMinX;
                        if (qMaxY < qMinY) qMaxY = qMinY;

                        // Boost samples if focused on one quadrant
                        int loops = preferredQuad.HasValue ? 25 : samplesPerQuad;

                        for (int i = 0; i < loops; i++)
                        {
                            double x = qMinX + (qMaxX - qMinX) * _rnd.NextDouble();
                            double y = qMinY + (qMaxY - qMinY) * _rnd.NextDouble();

                            double currentOverlap = 0;
                            foreach (var existing in CollageSlots)
                            {
                                double interLeft = Math.Max(x, existing.CollageX);
                                double interTop = Math.Max(y, existing.CollageY);
                                double interRight = Math.Min(x + startW, existing.CollageX + existing.CollageWidth);
                                double interBottom = Math.Min(y + startH, existing.CollageY + existing.CollageHeight);

                                if (interRight > interLeft && interBottom > interTop)
                                {
                                    currentOverlap += (interRight - interLeft) * (interBottom - interTop);
                                }
                            }

                            if (currentOverlap < minOverlap)
                            {
                                minOverlap = currentOverlap;
                                bestX = x;
                                bestY = y;
                                // Can't break early easily in stratified, but if 0 we are happy. 
                                // Ideally we want to check all quads to find the *most* empty one if minOverlap is 0?
                                // No, any 0 overlap is good.
                                if (minOverlap <= 1.0) goto FoundBest;
                            }
                        }
                    }
                }

            FoundBest:
                startX = bestX;
                startY = bestY;
            }

            return new VideoSlotViewModel
            {
                CollageX = startX,
                CollageY = startY,
                CollageWidth = startW,
                CollageHeight = startH,
                SpawnTime = DateTime.Now,
                Lifetime = TimeSpan.FromSeconds(15 + _rnd.Next(6)), // 15-20s
                Index = CollageSlots.Count
            };
        }

        [RelayCommand]
        public async Task TogglePlayback()
        {
            if (IsVideoPlaying)
            {
                Stop();
            }
            else
            {
                await ExecutePlayback();
            }
        }

        [ObservableProperty]
        private bool _isSloMoEnabled;

        partial void OnIsSloMoEnabledChanged(bool value)
        {
            // Optionally restart playback or just apply to next
            // User requested "when checked... video WILL start" -> implies next start.
            SaveSettings();
        }

        private async Task ExecutePlayback(List<string>? specificVideoList = null)
        {
            if (_suppressAutoRun && IsVideoPlaying) return; // double check

            Debug.WriteLine($"ExecutePlayback called. VideoPath: '{VideoPath}'");

            // Validate VideoPath
            if (string.IsNullOrWhiteSpace(VideoPath))
            {
                Debug.WriteLine("Error: No video path selected.");
                // Optionally show an error message to user, for now debug log
                return;
            }

            var handles = VideoSlots.Select(s => s.WindowHandle).ToList();
            if (handles.Any(h => h == IntPtr.Zero))
            {
                // Warn: Some handles not ready - wait a short while for bindings to populate.
                Debug.WriteLine("Warning: Some slots don't have handles yet. Waiting up to 2s...");
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 2000 && handles.Any(h => h == IntPtr.Zero))
                {
                    await Task.Delay(100);
                    handles = VideoSlots.Select(s => s.WindowHandle).ToList();
                }

                if (handles.Any(h => h == IntPtr.Zero))
                {
                    Debug.WriteLine("Error: Some handles still zero after waiting; aborting script start.");
                    return;
                }
            }

            // Select random videos for the slots
            var selectedVideos = specificVideoList ?? await _videoLibraryService.GetRandomVideosAsync(VideoSlots.Count);

            // Phase 1: Start all new instances concurrently and update immediately
            IsVideoPlaying = true;
            IsControlBarVisible = false;
            IsTitleBarVisible = false;
            UpdateVisibility();

            await _playbackService.PlayAsync(VideoSlots, selectedVideos, IsSloMoEnabled);

            if (firstRun)
            {
                // Restore the user's preferred delay after the first load
                firstRun = false;
                SelectedDelay = _restoredDelay;
            }
        }



        [ObservableProperty]
        private Avalonia.Controls.SystemDecorations _windowDecorations = Avalonia.Controls.SystemDecorations.Full;

        [ObservableProperty]
        private bool _isTitleBarVisible = true;

        [ObservableProperty]
        private bool _isControlBarVisible = true;

        [ObservableProperty]
        private bool _isAutoHideEnabled = true;



        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PlayButtonText))]
        [NotifyPropertyChangedFor(nameof(PlayButtonColor))]
        private bool _isVideoPlaying = false;

        public string PlayButtonText => IsVideoPlaying ? "Stop" : "Play";
        public Avalonia.Media.IBrush PlayButtonColor => IsVideoPlaying ? Avalonia.Media.Brushes.OrangeRed : Avalonia.Media.Brushes.LightGreen;

        public void Stop()
        {
            IsSwapEnabled = false; // Stop timer
            _playbackService.Stop(VideoSlots);
            IsVideoPlaying = false;
        }

        public ObservableCollection<string> GridSizeOptions { get; } = new();
        public ObservableCollection<int> DelayOptions { get; } = new();

        private void InitializeOptions()
        {
            for (int c = 2; c <= 8; c++) GridSizeOptions.Add($"2x{c}");
            for (int c = 3; c <= 8; c++) GridSizeOptions.Add($"3x{c}");
            for (int c = 4; c <= 8; c++) GridSizeOptions.Add($"4x{c}");
            for (int d = 5; d <= 200; d += 5) DelayOptions.Add(d);
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AreManualControlsEnabled))]
        private bool _isSwapEnabled;
        partial void OnIsSwapEnabledChanged(bool value)
        {
            SaveSettings();
            if (value) _swapTimer?.Start();
            else _swapTimer?.Stop();
        }

        public bool AreManualControlsEnabled => !IsSwapEnabled;

        [ObservableProperty]
        private string _selectedGrid1 = "2x2";
        partial void OnSelectedGrid1Changed(string value) => SaveSettings();

        [ObservableProperty]
        private string _selectedGrid2 = "3x3";
        partial void OnSelectedGrid2Changed(string value) => SaveSettings();

        [ObservableProperty]
        private int _selectedDelay = 10;
        partial void OnSelectedDelayChanged(int value)
        {
            if (firstRun && value > 0) _restoredDelay = value; // User intervention during startup

            if (_swapTimer != null) _swapTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, value));
            SaveSettings();
        }


        private Avalonia.Threading.DispatcherTimer? _swapTimer;
        private bool _isShowingGrid1 = true;

        private void InitializeSwapTimer()
        {
            _swapTimer = new Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(Math.Max(1, SelectedDelay))
            };
            _swapTimer.Tick += SwapTimer_Tick;
            if (IsSwapEnabled) _swapTimer.Start();
        }

        private async void SwapTimer_Tick(object? sender, EventArgs e)
        {
            _isShowingGrid1 = !_isShowingGrid1;
            string targetSize = _isShowingGrid1 ? SelectedGrid1 : SelectedGrid2;

            if (string.IsNullOrEmpty(targetSize)) return;

            var parts = targetSize.Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out int r) && int.TryParse(parts[1], out int c))
            {
                _suppressAutoRun = true;
                try { Rows = r; Columns = c; }
                finally { _suppressAutoRun = false; }

                UpdateVisibility();

                // Trigger background refresh for the now-hidden slots
                _ = RefreshHiddenSlots();
            }
        }

        private void EnsureSlotCount(int total)
        {
            // Remove excess
            while (VideoSlots.Count > total)
            {
                var slot = VideoSlots.Last();
                if (slot.CurrentProcess != null && !slot.CurrentProcess.HasExited)
                {
                    try { slot.CurrentProcess.Kill(); } catch { }
                    slot.CurrentProcess.Dispose();
                }
                VideoSlots.RemoveAt(VideoSlots.Count - 1);
            }

            // Add missing
            while (VideoSlots.Count < total)
            {
                VideoSlots.Add(new VideoSlotViewModel { Index = VideoSlots.Count });
            }
        }

        private int GetGridCount(string size)
        {
            var p = size.Split('x');
            if (p.Length == 2 && int.TryParse(p[0], out int r) && int.TryParse(p[1], out int c)) return r * c;
            return 0;
        }

        private void UpdateVisibility()
        {
            if (!IsSwapEnabled)
            {
                foreach (var s in VideoSlots) s.IsVisible = true;
                return;
            }

            int count1 = GetGridCount(SelectedGrid1);
            // If showing Grid 1 (First Section): 0 to count1 - 1
            // If showing Grid 2 (Last Section): count1 to End

            for (int i = 0; i < VideoSlots.Count; i++)
            {
                if (_isShowingGrid1)
                {
                    VideoSlots[i].IsVisible = i < count1;
                }
                else
                {
                    VideoSlots[i].IsVisible = i >= count1;
                }
            }
        }
        private bool _isRefreshing = false;

        private async Task RefreshHiddenSlots()
        {
            if (_isRefreshing) return;
            _isRefreshing = true;

            try
            {
                var hiddenSlots = VideoSlots.Where(s => !s.IsVisible).ToList();
                if (hiddenSlots.Count == 0) return;

                // 1. Pick new videos (GetRandomVideos excludes currently assigned paths)
                // This ensures we get fresh content not currently playing (visible or hidden)
                var excludedPaths = VideoSlots.Select(s => s.CurrentVideoPath).Where(p => !string.IsNullOrEmpty(p)).Cast<string>().ToHashSet();
                var newVideos = await _videoLibraryService.GetRandomVideosAsync(hiddenSlots.Count, excludedPaths);
                if (newVideos.Count == 0) return;

                // 2. Start new instances (Delegated to PlaybackService)
                await _playbackService.RefreshSlotsAsync(hiddenSlots, newVideos, IsSloMoEnabled);
            }
            finally
            {
                _isRefreshing = false;
            }
        }
    }
}
