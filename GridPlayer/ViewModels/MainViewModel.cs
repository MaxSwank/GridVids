using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GridVids.Models;
using GridVids.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace GridVids.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly SettingsService _settingsService;
        private readonly VideoLibraryService _videoLibraryService;
        private readonly PlaybackService _playbackService;
        private List<string> _currentVideoBatch = new();

        public List<string> GetCurrentActiveVideoBatch()
        {
            var list = new List<string>();

            if (IsCollageEnabled && CollageSlots.Count > 0)
            {
                list = CollageSlots
                    .Where(s => s.IsCollageVisible && !s.IsDying)
                    .Select(s => s.CurrentVideoPath)
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();

                if (list.Count == 0)
                {
                    list = CollageSlots
                        .Select(s => s.CurrentVideoPath)
                        .Where(p => !string.IsNullOrEmpty(p))
                        .ToList();
                }
            }
            else if (IsScrollEnabled && ScrollSlots.Count > 0)
            {
                list = ScrollSlots
                    .Where(s => s.CollageY >= -100 && s.CollageY <= ContainerHeight + 100)
                    .OrderBy(s => s.CollageY)
                    .ThenBy(s => s.CollageX)
                    .Select(s => s.CurrentVideoPath)
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();

                if (list.Count == 0)
                {
                    list = ScrollSlots
                        .Select(s => s.CurrentVideoPath)
                        .Where(p => !string.IsNullOrEmpty(p))
                        .ToList();
                }
            }
            else if (VideoSlots.Count > 0)
            {
                var visibleSlots = VideoSlots.Where(s => s.IsVisible).ToList();
                if (visibleSlots.Count > 0 && IsSwapEnabled)
                {
                    list = visibleSlots
                        .Select(s => s.CurrentVideoPath)
                        .Where(p => !string.IsNullOrEmpty(p))
                        .ToList();
                }
                else
                {
                    list = VideoSlots
                        .Select(s => s.CurrentVideoPath)
                        .Where(p => !string.IsNullOrEmpty(p))
                        .ToList();
                }
            }

            if (!IsSingleVidEnabled && list.Count > 0)
            {
                list = list.Distinct().ToList();
            }

            if (list.Count > 0)
            {
                _currentVideoBatch = list;
            }

            return _currentVideoBatch.ToList();
        }


        public ObservableCollection<VideoSlotViewModel> VideoSlots { get; } = new();
        public ObservableCollection<VideoSlotViewModel> CollageSlots { get; } = new();
        public ObservableCollection<VideoSlotViewModel> StackSlots { get; } = new();
        public ObservableCollection<VideoSlotViewModel> ScrollSlots { get; } = new();

        public MainViewModel()
        {
            _settingsService = new SettingsService();
            _videoLibraryService = new VideoLibraryService();
            _playbackService = new PlaybackService();

            // Load Settings
            var settings = _settingsService.LoadSettings();
            _isSwapEnabled = settings.IsSwapEnabled;
            _isSingleVidEnabled = settings.IsSingleVidEnabled;
            _isRandomStartEnabled = settings.IsRandomStartEnabled;
            _isMuted = true; // Always start muted
            _volume = settings.Volume > 0 ? settings.Volume : 10;
            _selectedGrid1 = !string.IsNullOrEmpty(settings.SelectedGrid1) ? settings.SelectedGrid1 : "2x2";
            _selectedGrid2 = !string.IsNullOrEmpty(settings.SelectedGrid2) ? settings.SelectedGrid2 : "3x3";
            _selectedRandomize = !string.IsNullOrEmpty(settings.SelectedRandomize) ? settings.SelectedRandomize : "None";
            _isStackableEnabled = settings.IsStackableEnabled;
            _isScrollEnabled = settings.IsScrollEnabled;
            _isCollageEnabled = settings.IsCollageEnabled;
            _scrollSpeed = settings.ScrollSpeed > 0 ? settings.ScrollSpeed : 80.0;
            _selectedScrollDirection = !string.IsNullOrEmpty(settings.SelectedScrollDirection) ? settings.SelectedScrollDirection : "Up";
            _isScrollDown = _selectedScrollDirection == "Down";
            _selectedDelay = settings.SelectedDelay > 0 ? settings.SelectedDelay : 10.0;

            if (!string.IsNullOrEmpty(settings.SelectedDisplayMode))
            {
                _selectedDisplayMode = settings.SelectedDisplayMode;
                switch (settings.SelectedDisplayMode)
                {
                    case "Auto-Swap":
                        _isSwapEnabled = true;
                        _isStackableEnabled = false;
                        _isScrollEnabled = false;
                        _isCollageEnabled = false;
                        break;
                    case "Stackable":
                        _isSwapEnabled = false;
                        _isStackableEnabled = true;
                        _isScrollEnabled = false;
                        _isCollageEnabled = false;
                        break;
                    case "Scrolling Wall":
                        _isSwapEnabled = false;
                        _isStackableEnabled = false;
                        _isScrollEnabled = true;
                        _isCollageEnabled = false;
                        break;
                    case "Collage":
                        _isSwapEnabled = false;
                        _isStackableEnabled = false;
                        _isScrollEnabled = false;
                        _isCollageEnabled = true;
                        break;
                    case "Grid":
                    default:
                        _isSwapEnabled = false;
                        _isStackableEnabled = false;
                        _isScrollEnabled = false;
                        _isCollageEnabled = false;
                        break;
                }
            }
            else
            {
                SyncSelectedDisplayModeFromFlags();
            }

            _isGridVisible = !_isCollageEnabled && !_isScrollEnabled;

            _isSloMoEnabled = settings.IsSloMoEnabled;

            _playbackService.IsRandomStartEnabled = _isRandomStartEnabled;
            _playbackService.IsMuted = true;
            _playbackService.Volume = _volume;
            _playbackService.IsSloMo = _isSloMoEnabled;

            InitializeOptions();
            InitializeSwapTimer();
            InitializeRandomizeTimer();
            InitializeStackTimer();

            _rows = settings.Rows > 0 ? settings.Rows : 2;
            _columns = settings.Columns > 0 ? settings.Columns : 2;
            _videoPath = settings.VideoPath ?? string.Empty;

            if (!string.IsNullOrEmpty(_videoPath))
            {
                // Ensure cache is populated before first playback
                _ = _videoLibraryService.RefreshCacheAsync(_videoPath).ContinueWith(t =>
                {
                    if (!t.IsFaulted)
                    {
                        if (IsScrollEnabled) StartScroll();
                        else if (IsCollageEnabled) _ = StartCollage();
                        else _ = ExecutePlayback();
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }

            UpdateGrid();
            ValidateDelay();
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsStackableVisible))]
        [NotifyPropertyChangedFor(nameof(IsScrollVisible))]
        private int _rows = 2;

        private bool _suppressAutoRun = false;

        partial void OnRowsChanged(int value)
        {
            if (!_suppressAutoRun)
            {
                int bestCols = CalculateBestColumnsFor16x9(value);
                if (bestCols != Columns)
                {
                    _suppressAutoRun = true;
                    try { Columns = bestCols; }
                    finally { _suppressAutoRun = false; }
                }
            }

            UpdateGrid();
            ValidateDelay();
            SaveSettings();
            if (IsStackableEnabled && (Rows != 2 || (Columns != 2 && Columns != 4)))
            {
                IsStackableEnabled = false;
            }
            if (IsScrollEnabled)
            {
                StartScroll(GetCurrentActiveVideoBatch());
            }
            else if (!string.IsNullOrEmpty(VideoPath) && !_suppressAutoRun)
            {
                _ = ExecutePlayback(GetCurrentActiveVideoBatch());
            }
        }

        private int CalculateBestColumnsFor16x9(int rows)
        {
            if (rows <= 0) return 2;

            double containerW = ContainerWidth > 100 ? ContainerWidth : 1600.0;
            double containerH = ContainerHeight > 100 ? ContainerHeight : 900.0;

            const double targetRatio = 16.0 / 9.0;

            int bestCols = 1;
            double minDiff = double.MaxValue;

            for (int c = 1; c <= 8; c++)
            {
                double cellW = containerW / c;
                double cellH = containerH / rows;
                double ratio = cellW / cellH;

                double diff = Math.Abs(ratio - targetRatio);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    bestCols = c;
                }
            }

            return bestCols;
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsStackableVisible))]
        [NotifyPropertyChangedFor(nameof(IsScrollVisible))]
        private int _columns = 2;

        partial void OnColumnsChanged(int value)
        {
            UpdateGrid();
            ValidateDelay();
            SaveSettings();
            if (IsStackableEnabled && (Rows != 2 || (Columns != 2 && Columns != 4)))
            {
                IsStackableEnabled = false;
            }
            if (IsScrollEnabled)
            {
                StartScroll(GetCurrentActiveVideoBatch());
            }
            else if (!string.IsNullOrEmpty(VideoPath) && !_suppressAutoRun)
            {
                _ = ExecutePlayback(GetCurrentActiveVideoBatch());
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

            _staircaseIndex = 0;
            _randomSlotQueue.Clear();
            _currentSingleVidForMultiple = null;
            _slotsUpdatedInCycle.Clear();

            EnsureSlotCount(total);
            UpdateVisibility();
        }

        [ObservableProperty]
        private string _videoPath = string.Empty;

        partial void OnVideoPathChanged(string value)
        {
            SaveSettings();
            if (!string.IsNullOrEmpty(value))
            {
                _ = _videoLibraryService.RefreshCacheAsync(value).ContinueWith(t =>
                {
                    if (!t.IsFaulted)
                    {
                        if (IsScrollEnabled) StartScroll();
                        else if (IsCollageEnabled) _ = StartCollage();
                        else _ = ExecutePlayback();
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
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

        public void SaveSettings()
        {
            ValidateDelay();
            var settings = new AppSettings
            {
                Rows = Rows,
                Columns = Columns,
                VideoPath = VideoPath,
                IsSwapEnabled = IsSwapEnabled,
                IsSingleVidEnabled = IsSingleVidEnabled,
                IsRandomStartEnabled = IsRandomStartEnabled,
                IsMuted = IsMuted,
                Volume = Volume,
                IsSloMoEnabled = IsSloMoEnabled,
                SelectedGrid1 = SelectedGrid1,
                SelectedGrid2 = SelectedGrid2,
                SelectedDelay = SelectedDelay,
                SelectedRandomize = SelectedRandomize,
                IsStackableEnabled = IsStackableEnabled,
                IsScrollEnabled = IsScrollEnabled,
                IsCollageEnabled = IsCollageEnabled,
                SelectedDisplayMode = SelectedDisplayMode,
                ScrollSpeed = ScrollSpeed,
                SelectedScrollDirection = SelectedScrollDirection
            };
            _settingsService.SaveSettings(settings);
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsScrollVisible))]
        private bool _isCollageEnabled;

        partial void OnIsCollageEnabledChanged(bool value)
        {
            if (_isUpdatingDisplayMode) return;
            SelectedDisplayMode = value ? "Collage" : "Grid";
        }

        [ObservableProperty]
        private double _containerWidth = 1500; // Default fallback

        [ObservableProperty]
        private double _containerHeight = 800; // Default fallback

        private Avalonia.Threading.DispatcherTimer? _collageTimer;
        private Random _rnd = new Random();

        private DateTime _lastTick;

        private async Task StartCollage(List<string>? initialVideos = null)
        {
            if (_collageTimer == null)
            {
                _collageTimer = new Avalonia.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(33) // 30 FPS - smoother for heavy window resizing
                };
                _collageTimer.Tick += CollageTimer_Tick;
            }

            IsVideoPlaying = true;
            IsControlBarVisible = false;
            IsTitleBarVisible = false;

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

            // Get Videos: prioritize initialVideos (existing batch)
            List<string> videos;
            if (initialVideos != null && initialVideos.Count > 0)
            {
                if (IsSingleVidEnabled)
                {
                    videos = Enumerable.Repeat(initialVideos[0], slots.Count).ToList();
                }
                else
                {
                    videos = new List<string>(initialVideos);
                    if (videos.Count < slots.Count)
                    {
                        var needed = slots.Count - videos.Count;
                        var extra = await _videoLibraryService.GetRandomVideosAsync(needed, videos.ToHashSet(), IsSingleVidEnabled);
                        videos.AddRange(extra);
                    }
                    else if (videos.Count > slots.Count)
                    {
                        videos = videos.Take(slots.Count).ToList();
                    }
                }
            }
            else
            {
                videos = await _videoLibraryService.GetRandomVideosAsync(slots.Count);
            }

            if (videos.Count > 0)
            {
                _currentVideoBatch = videos.ToList();
                for (int i = 0; i < CollageSlots.Count && i < videos.Count; i++)
                {
                    CollageSlots[i].CurrentVideoPath = videos[i];
                }
            }

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
            await _playbackService.PlayAsync(CollageSlots, videos);

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
                await _playbackService.PlayAsync(new[] { s }, v);
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

            // Select videos for the slots (keep specificVideoList if passed)
            List<string> selectedVideos;
            if (specificVideoList != null && specificVideoList.Count > 0)
            {
                if (IsSingleVidEnabled)
                {
                    selectedVideos = Enumerable.Repeat(specificVideoList[0], VideoSlots.Count).ToList();
                }
                else
                {
                    selectedVideos = new List<string>(specificVideoList);
                    if (selectedVideos.Count < VideoSlots.Count)
                    {
                        var needed = VideoSlots.Count - selectedVideos.Count;
                        var extra = await _videoLibraryService.GetRandomVideosAsync(needed, selectedVideos.ToHashSet(), IsSingleVidEnabled);
                        selectedVideos.AddRange(extra);
                    }
                    else if (selectedVideos.Count > VideoSlots.Count)
                    {
                        selectedVideos = selectedVideos.Take(VideoSlots.Count).ToList();
                    }
                }
            }
            else
            {
                selectedVideos = await _videoLibraryService.GetRandomVideosAsync(VideoSlots.Count, null, IsSingleVidEnabled);
            }

            if (selectedVideos.Count > 0)
            {
                _currentVideoBatch = selectedVideos.ToList();
                for (int i = 0; i < VideoSlots.Count && i < selectedVideos.Count; i++)
                {
                    VideoSlots[i].CurrentVideoPath = selectedVideos[i];
                }
            }

            bool isMultipleSingleVid = IsSingleVidEnabled && (SelectedRandomize == "Multiple" || SelectedRandomize == "Randomize Multiple" || SelectedRandomize == "Randomize multiple");
            if (isMultipleSingleVid && selectedVideos.Count > 0)
            {
                _currentSingleVidForMultiple = selectedVideos[0];
                _slotsUpdatedInCycle.Clear();
            }

            // Phase 1: Start all new instances concurrently and update immediately
            IsVideoPlaying = true;
            IsControlBarVisible = false;
            IsTitleBarVisible = false;
            UpdateVisibility();

            await _playbackService.PlayAsync(VideoSlots, selectedVideos);



            if (!IsStackableEnabled && !IsSwapEnabled)
            {
                _ = PreloadNextSlotAsync();
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
        private bool _isVideoPlaying = false;

        public void Stop()
        {
            IsSwapEnabled = false; // Stop timer
            _stackTimer?.Stop();
            _scrollTimer?.Stop();
            _playbackService.Stop(VideoSlots);
            ClearStackSlots();
            ClearScrollSlots();
            IsVideoPlaying = false;
        }

        public void CleanupAllProcesses()
        {
            IsSwapEnabled = false;
            _swapTimer?.Stop();
            _randomizeTimer?.Stop();
            _collageTimer?.Stop();
            _stackTimer?.Stop();
            _scrollTimer?.Stop();
            _playbackService.StopAll(VideoSlots, CollageSlots, StackSlots);
            _playbackService.Stop(ScrollSlots);
            StackSlots.Clear();
            ScrollSlots.Clear();
        }

        public ObservableCollection<string> DisplayModeOptions { get; } = new() { "Grid", "Auto-Swap", "Stackable", "Scrolling Wall", "Collage" };
        public ObservableCollection<string> GridSizeOptions { get; } = new();
        public ObservableCollection<double> DelayOptions { get; } = new();
        public ObservableCollection<string> RandomizeOptions { get; } = new() { "None", "Staircase", "Multiple" };

        private bool _isUpdatingDisplayMode = false;

        [ObservableProperty]
        private string _selectedDisplayMode = "Grid";

        partial void OnSelectedDisplayModeChanged(string value)
        {
            if (_isUpdatingDisplayMode) return;
            _isUpdatingDisplayMode = true;

            try
            {
                // Capture existing batch of videos BEFORE changing mode or stopping slots
                var existingBatch = GetCurrentActiveVideoBatch();

                string oldMode = "";
                if (IsCollageEnabled) oldMode = "Collage";
                else if (IsScrollEnabled) oldMode = "Scrolling Wall";
                else if (IsStackableEnabled) oldMode = "Stackable";
                else if (IsSwapEnabled) oldMode = "Auto-Swap";
                else oldMode = "Grid";

                if (oldMode == value) return;

                bool wasUsingVideoSlots = (oldMode == "Grid" || oldMode == "Auto-Swap" || oldMode == "Stackable");
                bool willUseVideoSlots = (value == "Grid" || value == "Auto-Swap" || value == "Stackable");

                // Case 1: Seamless switch between Grid, Auto-Swap, and Stackable (all share VideoSlots - NEVER STOP)
                if (wasUsingVideoSlots && willUseVideoSlots)
                {
                    if (oldMode == "Auto-Swap") _swapTimer?.Stop();
                    if (oldMode == "Stackable")
                    {
                        _stackTimer?.Stop();
                        ClearStackSlots();
                    }

                    IsCollageEnabled = false;
                    IsScrollEnabled = false;
                    IsGridVisible = true;

                    if (value == "Auto-Swap")
                    {
                        IsStackableEnabled = false;
                        IsSwapEnabled = true;
                        UpdateGrid();
                        _swapTimer?.Start();
                    }
                    else if (value == "Stackable")
                    {
                        IsSwapEnabled = false;
                        IsStackableEnabled = true;
                        UpdateGrid();
                        UpdateStackTimer();
                    }
                    else // "Grid"
                    {
                        IsSwapEnabled = false;
                        IsStackableEnabled = false;
                        _isShowingGrid1 = true;
                        UpdateGrid();
                    }

                    UpdateRandomizeTimer();
                    SaveSettings();
                    return;
                }

                // Case 2: Transitioning to Scrolling Wall (Keep previous videos playing until scroll wall is ready)
                if (value == "Scrolling Wall")
                {
                    if (oldMode == "Auto-Swap") _swapTimer?.Stop();
                    if (oldMode == "Stackable")
                    {
                        _stackTimer?.Stop();
                        ClearStackSlots();
                    }

                    IsSwapEnabled = false;
                    IsStackableEnabled = false;
                    IsScrollEnabled = true;

                    if (!string.IsNullOrEmpty(VideoPath))
                    {
                        _ = Task.Run(async () =>
                        {
                            await StartScrollAsync(existingBatch);
                            await Task.Delay(300);
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                if (VideoSlots.Any(s => s.CurrentProcess != null && !s.CurrentProcess.HasExited))
                                {
                                    _playbackService.Stop(VideoSlots);
                                    IsGridVisible = false;
                                }
                                if (CollageSlots.Count > 0)
                                {
                                    StopCollage();
                                    IsCollageEnabled = false;
                                }
                            });
                        });
                    }
                    UpdateRandomizeTimer();
                    SaveSettings();
                    return;
                }

                // Case 3: Transitioning to Collage (Keep previous videos playing until collage buffers and reveals)
                if (value == "Collage")
                {
                    if (oldMode == "Auto-Swap") _swapTimer?.Stop();
                    if (oldMode == "Stackable")
                    {
                        _stackTimer?.Stop();
                        ClearStackSlots();
                    }

                    IsSwapEnabled = false;
                    IsStackableEnabled = false;
                    IsCollageEnabled = true;

                    if (!string.IsNullOrEmpty(VideoPath))
                    {
                        _ = Task.Run(async () =>
                        {
                            await StartCollage(existingBatch);
                            await Task.Delay(200);
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                if (VideoSlots.Any(s => s.CurrentProcess != null && !s.CurrentProcess.HasExited))
                                {
                                    _playbackService.Stop(VideoSlots);
                                    IsGridVisible = false;
                                }
                                if (ScrollSlots.Count > 0)
                                {
                                    StopScroll();
                                    IsScrollEnabled = false;
                                }
                            });
                        });
                    }
                    UpdateRandomizeTimer();
                    SaveSettings();
                    return;
                }

                // Case 4: Transitioning from Scrolling Wall or Collage to Grid / Auto-Swap / Stackable
                if (willUseVideoSlots)
                {
                    IsGridVisible = true;
                    UpdateGrid();

                    if (!string.IsNullOrEmpty(VideoPath))
                    {
                        _ = Task.Run(async () =>
                        {
                            await ExecutePlayback(existingBatch);
                            await Task.Delay(400);
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                if (oldMode == "Scrolling Wall")
                                {
                                    StopScroll();
                                    IsScrollEnabled = false;
                                }
                                else if (oldMode == "Collage")
                                {
                                    StopCollage();
                                    IsCollageEnabled = false;
                                }

                                if (value == "Auto-Swap")
                                {
                                    IsSwapEnabled = true;
                                    _swapTimer?.Start();
                                }
                                else if (value == "Stackable")
                                {
                                    IsStackableEnabled = true;
                                    UpdateStackTimer();
                                }
                                UpdateRandomizeTimer();
                            });
                        });
                    }
                    SaveSettings();
                    return;
                }

                SaveSettings();
            }
            finally
            {
                _isUpdatingDisplayMode = false;
            }
        }

        private void SyncSelectedDisplayModeFromFlags()
        {
            if (_isUpdatingDisplayMode) return;
            _isUpdatingDisplayMode = true;
            try
            {
                if (IsCollageEnabled) SelectedDisplayMode = "Collage";
                else if (IsScrollEnabled) SelectedDisplayMode = "Scrolling Wall";
                else if (IsStackableEnabled) SelectedDisplayMode = "Stackable";
                else if (IsSwapEnabled) SelectedDisplayMode = "Auto-Swap";
                else SelectedDisplayMode = "Grid";
            }
            finally
            {
                _isUpdatingDisplayMode = false;
            }
        }

        private void InitializeOptions()
        {
            for (int c = 2; c <= 8; c++) GridSizeOptions.Add($"2x{c}");
            for (int c = 3; c <= 8; c++) GridSizeOptions.Add($"3x{c}");
            for (int c = 4; c <= 8; c++) GridSizeOptions.Add($"4x{c}");

            DelayOptions.Add(0.5);
            for (int d = 1; d <= 4; d++) DelayOptions.Add(d);
            for (int d = 5; d <= 200; d += 5) DelayOptions.Add(d);
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AreManualControlsEnabled))]
        [NotifyPropertyChangedFor(nameof(IsRandomizeEnabled))]
        [NotifyPropertyChangedFor(nameof(IsDelayEnabled))]
        [NotifyPropertyChangedFor(nameof(IsStackableVisible))]
        [NotifyPropertyChangedFor(nameof(IsScrollVisible))]
        private bool _isSwapEnabled;
        partial void OnIsSwapEnabledChanged(bool value)
        {
            if (_isUpdatingDisplayMode) return;
            SaveSettings();
            SelectedDisplayMode = value ? "Auto-Swap" : "Grid";
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDelayEnabled))]
        [NotifyPropertyChangedFor(nameof(IsRandomizeEnabled))]
        private string _selectedRandomize = "None";
        partial void OnSelectedRandomizeChanged(string value)
        {
            _staircaseIndex = 0;
            _randomSlotQueue.Clear();
            _currentSingleVidForMultiple = null;
            _slotsUpdatedInCycle.Clear();
            ClearPreloadedSlot();

            if (value == "Multiple" && IsScrollEnabled)
            {
            }

            SaveSettings();
            UpdateRandomizeTimer();
        }

        [ObservableProperty]
        private bool _isSingleVidEnabled;
        partial void OnIsSingleVidEnabledChanged(bool value)
        {
            SaveSettings();
            UpdateGrid();
            if (IsScrollEnabled)
            {
                StartScroll();
            }
            else if (!string.IsNullOrEmpty(VideoPath) && !_suppressAutoRun && !IsCollageEnabled)
            {
                _ = ExecutePlayback();
            }
        }

        [ObservableProperty]
        private bool _isRandomStartEnabled = true;

        partial void OnIsRandomStartEnabledChanged(bool value)
        {
            SaveSettings();
            if (_playbackService != null)
            {
                _playbackService.IsRandomStartEnabled = value;
            }
        }

        [ObservableProperty]
        private bool _isMuted = true;

        partial void OnIsMutedChanged(bool value)
        {
            SaveSettings();
            if (_playbackService != null)
            {
                var allSlots = VideoSlots.Concat(CollageSlots);
                _playbackService.UpdateVolume(allSlots, value, Volume);
            }
        }

        [ObservableProperty]
        private int _volume = 10;

        partial void OnVolumeChanged(int value)
        {
            SaveSettings();
            if (_playbackService != null)
            {
                var allSlots = VideoSlots.Concat(CollageSlots);
                _playbackService.UpdateVolume(allSlots, IsMuted, value);
            }
        }

        [ObservableProperty]
        private bool _isSloMoEnabled = false;

        partial void OnIsSloMoEnabledChanged(bool value)
        {
            SaveSettings();
            if (_playbackService != null)
            {
                var allSlots = VideoSlots.Concat(CollageSlots).Concat(StackSlots);
                _playbackService.UpdateSpeed(allSlots, value);
            }
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsScrollVisible))]
        private bool _isFullScreen = false;

        partial void OnIsFullScreenChanged(bool value)
        {
        }

        public bool AreManualControlsEnabled => !IsSwapEnabled && !IsStackableEnabled;
        public bool IsRandomizeEnabled => !IsSwapEnabled && !IsStackableEnabled;
        public bool IsDelayEnabled => IsSwapEnabled || (!IsScrollEnabled && AreManualControlsEnabled && SelectedRandomize != "None") || IsStackableEnabled || (IsScrollEnabled && SelectedRandomize == "Multiple");

        public bool IsStackableVisible => !IsCollageEnabled && !IsSwapEnabled && (Rows == 2 && (Columns == 2 || Columns == 4));
        public bool IsScrollVisible => !IsCollageEnabled && !IsSwapEnabled;

        private bool _isGridVisible = true;
        public bool IsGridVisible
        {
            get => _isGridVisible;
            set => SetProperty(ref _isGridVisible, value);
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AreManualControlsEnabled))]
        [NotifyPropertyChangedFor(nameof(IsRandomizeEnabled))]
        [NotifyPropertyChangedFor(nameof(IsDelayEnabled))]
        [NotifyPropertyChangedFor(nameof(IsStackableVisible))]
        [NotifyPropertyChangedFor(nameof(IsScrollVisible))]
        private bool _isStackableEnabled = false;

        partial void OnIsStackableEnabledChanged(bool value)
        {
            if (_isUpdatingDisplayMode) return;
            SaveSettings();
            SelectedDisplayMode = value ? "Stackable" : "Grid";
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AreManualControlsEnabled))]
        [NotifyPropertyChangedFor(nameof(IsRandomizeEnabled))]
        [NotifyPropertyChangedFor(nameof(IsDelayEnabled))]
        private bool _isScrollEnabled = false;

        partial void OnIsScrollEnabledChanged(bool value)
        {
            if (_isUpdatingDisplayMode) return;
            SaveSettings();
            SelectedDisplayMode = value ? "Scrolling Wall" : "Grid";
        }

        private Avalonia.Threading.DispatcherTimer? _scrollTimer;
        private DateTime _lastScrollTick;
        private bool _isSpawningScrollRow = false;
        public ObservableCollection<string> ScrollDirectionOptions { get; } = new() { "Up", "Down" };

        [ObservableProperty]
        private string _selectedScrollDirection = "Up";
        partial void OnSelectedScrollDirectionChanged(string value)
        {
            IsScrollDown = value == "Down";
            SaveSettings();
        }

        [ObservableProperty]
        private bool _isScrollDown = false;
        partial void OnIsScrollDownChanged(bool value)
        {
            SelectedScrollDirection = value ? "Down" : "Up";
            SaveSettings();
        }

        [ObservableProperty]
        private double _scrollSpeed = 80.0;

        partial void OnScrollSpeedChanged(double value)
        {
            SaveSettings();
        }

        private async Task StartScrollAsync(List<string>? initialVideos = null)
        {
            if (_scrollTimer == null)
            {
                _scrollTimer = new Avalonia.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS ultra-smooth animation
                };
                _scrollTimer.Tick += ScrollTimer_Tick;
            }

            _scrollTimer.Stop();
            ClearScrollSlots();
            IsVideoPlaying = true;
            IsControlBarVisible = false;
            IsTitleBarVisible = false;

            int curRows = Rows > 0 ? Rows : 2;
            int curCols = Columns > 0 ? Columns : 4;

            double effectiveW = ContainerWidth > 100 ? ContainerWidth : 1500;
            double effectiveH = ContainerHeight > 100 ? ContainerHeight : 800;

            double cellW = effectiveW / (double)curCols;
            double cellH = effectiveH / (double)curRows;

            var sourceVideos = (initialVideos != null && initialVideos.Count > 0)
                ? initialVideos
                : _currentVideoBatch;

            var videoQueue = (sourceVideos != null && sourceVideos.Count > 0)
                ? new Queue<string>(sourceVideos)
                : new Queue<string>();

            // Spawn initial rows for all visible rows on screen
            for (int r = 0; r < curRows; r++)
            {
                var rowVideos = new List<string>();
                while (rowVideos.Count < curCols && videoQueue.Count > 0)
                {
                    rowVideos.Add(videoQueue.Dequeue());
                }
                await SpawnScrollRowAsync(r * cellH, cellW, cellH, curCols, rowVideos.Count > 0 ? rowVideos : null);
            }

            if (SelectedScrollDirection == "Down")
            {
                // Spawn 1 pre-loading row above viewport
                var rowVideos = new List<string>();
                while (rowVideos.Count < curCols && videoQueue.Count > 0)
                {
                    rowVideos.Add(videoQueue.Dequeue());
                }
                _ = SpawnScrollRowAsync(-cellH, cellW, cellH, curCols, rowVideos.Count > 0 ? rowVideos : null);
            }
            else
            {
                // Spawn 1 pre-loading row below viewport
                var rowVideos = new List<string>();
                while (rowVideos.Count < curCols && videoQueue.Count > 0)
                {
                    rowVideos.Add(videoQueue.Dequeue());
                }
                _ = SpawnScrollRowAsync(curRows * cellH, cellW, cellH, curCols, rowVideos.Count > 0 ? rowVideos : null);
            }

            _lastScrollTick = DateTime.Now;
            _scrollTimer.Start();
        }

        private void StartScroll(List<string>? initialVideos = null)
        {
            _ = StartScrollAsync(initialVideos);
        }

        private void StopScroll()
        {
            _scrollTimer?.Stop();
            ClearScrollSlots();
        }

        private void ClearScrollSlots()
        {
            if (ScrollSlots.Count > 0)
            {
                var slotsToStop = ScrollSlots.ToList();
                ScrollSlots.Clear();
                _playbackService.Stop(slotsToStop);
            }
        }

        private async Task SpawnScrollRowAsync(double startY, double cellW, double cellH, int cols, List<string>? rowVideos = null)
        {
            if (string.IsNullOrWhiteSpace(VideoPath)) return;

            var newSlots = new List<VideoSlotViewModel>();
            for (int c = 0; c < cols; c++)
            {
                double x1 = Math.Round(c * cellW);
                double x2 = Math.Round((c + 1) * cellW);
                double w = x2 - x1;

                var slot = new VideoSlotViewModel
                {
                    CollageX = x1,
                    CollageY = startY,
                    CollageWidth = w,
                    CollageHeight = cellH,
                    Opacity = 1.0,
                    IsCollageVisible = true,
                    Index = ScrollSlots.Count + newSlots.Count
                };
                newSlots.Add(slot);
            }

            foreach (var s in newSlots) ScrollSlots.Add(s);

            // Offload handle polling and MPV process startup to background task
            await Task.Run(async () =>
            {
                int retries = 0;
                while (newSlots.Any(s => s.WindowHandle == IntPtr.Zero) && retries < 40)
                {
                    await Task.Delay(25);
                    retries++;
                }

                var validSlots = newSlots.Where(s => s.WindowHandle != IntPtr.Zero).ToList();
                if (validSlots.Count > 0)
                {
                    List<string> videos;
                    if (rowVideos != null && rowVideos.Count > 0)
                    {
                        if (IsSingleVidEnabled)
                        {
                            videos = Enumerable.Repeat(rowVideos[0], validSlots.Count).ToList();
                        }
                        else
                        {
                            videos = new List<string>(rowVideos);
                            if (videos.Count < validSlots.Count)
                            {
                                var needed = validSlots.Count - videos.Count;
                                var extra = await _videoLibraryService.GetRandomVideosAsync(needed, videos.ToHashSet(), IsSingleVidEnabled);
                                videos.AddRange(extra);
                            }
                            else if (videos.Count > validSlots.Count)
                            {
                                videos = videos.Take(validSlots.Count).ToList();
                            }
                        }
                    }
                    else
                    {
                        videos = await _videoLibraryService.GetRandomVideosAsync(validSlots.Count, null, IsSingleVidEnabled);
                    }

                    if (videos.Count > 0)
                    {
                        for (int i = 0; i < validSlots.Count && i < videos.Count; i++)
                        {
                            validSlots[i].CurrentVideoPath = videos[i];
                        }
                        foreach (var v in videos)
                        {
                            if (!_currentVideoBatch.Contains(v))
                            {
                                _currentVideoBatch.Add(v);
                            }
                        }
                        await _playbackService.PlayAsync(validSlots, videos);
                    }
                }
            });
        }

        private void ScrollTimer_Tick(object? sender, EventArgs e)
        {
            if (!IsScrollEnabled || !IsVideoPlaying) return;

            var now = DateTime.Now;
            double dt = (now - _lastScrollTick).TotalSeconds;
            _lastScrollTick = now;
            if (dt > 0.05) dt = 0.05; // Cap delta time to prevent frame jumps

            double delta = ScrollSpeed * dt;

            int curRows = Rows > 0 ? Rows : 2;
            int curCols = Columns > 0 ? Columns : 4;

            double effectiveW = ContainerWidth > 100 ? ContainerWidth : 1500;
            double effectiveH = ContainerHeight > 100 ? ContainerHeight : 800;
            double cellW = effectiveW / (double)curCols;
            double cellH = effectiveH / (double)curRows;

            bool isDown = SelectedScrollDirection == "Down";
            double dy = isDown ? delta : -delta;

            // 1. Move all slots
            var slotsCopy = ScrollSlots.ToList();
            foreach (var slot in slotsCopy)
            {
                slot.CollageY += dy;
            }

            // 2. Off-screen cleanup
            List<VideoSlotViewModel> offScreen;
            if (isDown)
            {
                // Scrolled completely off the bottom
                offScreen = ScrollSlots.Where(s => s.CollageY >= effectiveH).ToList();
            }
            else
            {
                // Scrolled completely off the top
                offScreen = ScrollSlots.Where(s => (s.CollageY + s.CollageHeight) <= 0).ToList();
            }

            if (offScreen.Count > 0)
            {
                foreach (var slot in offScreen)
                {
                    ScrollSlots.Remove(slot);
                }
                _playbackService.Stop(offScreen);
            }

            // 3. Preload next row
            if (!_isSpawningScrollRow && ScrollSlots.Count > 0)
            {
                if (isDown)
                {
                    double minY = ScrollSlots.Min(s => s.CollageY);
                    if (minY >= -cellH * 0.5)
                    {
                        _isSpawningScrollRow = true;
                        double spawnY = minY - cellH;
                        _ = Task.Run(async () =>
                        {
                            await SpawnScrollRowAsync(spawnY, cellW, cellH, curCols);
                            _isSpawningScrollRow = false;
                        });
                    }
                }
                else
                {
                    double maxY = ScrollSlots.Max(s => s.CollageY);
                    if (maxY + cellH <= effectiveH + (cellH * 0.5))
                    {
                        _isSpawningScrollRow = true;
                        double spawnY = maxY + cellH;
                        _ = Task.Run(async () =>
                        {
                            await SpawnScrollRowAsync(spawnY, cellW, cellH, curCols);
                            _isSpawningScrollRow = false;
                        });
                    }
                }
            }
        }

        [ObservableProperty]
        private string _selectedGrid1 = "2x2";
        partial void OnSelectedGrid1Changed(string value)
        {
            ValidateDelay();
            SaveSettings();
        }

        [ObservableProperty]
        private string _selectedGrid2 = "3x3";
        partial void OnSelectedGrid2Changed(string value)
        {
            ValidateDelay();
            SaveSettings();
        }

        public bool IsGridSizeTwo()
        {
            if (IsSwapEnabled)
            {
                int c1 = GetGridCount(SelectedGrid1);
                int c2 = GetGridCount(SelectedGrid2);
                return c1 == 2 || c2 == 2 || (c1 + c2) == 2;
            }
            return (Rows * Columns == 2) || (VideoSlots.Count == 2);
        }

        public void ValidateDelay()
        {
            if (IsGridSizeTwo() && SelectedDelay < 2.0)
            {
                SelectedDelay = 2.0;
            }
        }

        [ObservableProperty]
        private double _selectedDelay = 10.0;
        partial void OnSelectedDelayChanged(double value)
        {
            if (IsGridSizeTwo() && value < 2.0)
            {
                SelectedDelay = 2.0;
                return;
            }

            if (_swapTimer != null) _swapTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.1, value));
            if (_randomizeTimer != null) _randomizeTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.1, value));
            if (_stackTimer != null) _stackTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.1, value));
            SaveSettings();
        }


        private Avalonia.Threading.DispatcherTimer? _swapTimer;
        private Avalonia.Threading.DispatcherTimer? _randomizeTimer;
        private Avalonia.Threading.DispatcherTimer? _stackTimer;
        private int _stackQuadrantStep = 0; // 0 = reveal Quad 1, 1 = reveal Quad 2, 2 = reveal Quad 3, 3 = reveal Quad 4
        private bool _isStackRunning = false;

        private void InitializeStackTimer()
        {
            _stackTimer = new Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(Math.Max(0.1, SelectedDelay))
            };
            _stackTimer.Tick += StackTimer_Tick;
            UpdateStackTimer();
        }

        private void UpdateStackTimer()
        {
            if (_stackTimer == null) return;

            if (IsStackableEnabled && !IsCollageEnabled && !IsSwapEnabled)
            {
                _stackQuadrantStep = 0;
                _stackTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.1, SelectedDelay));
                if (!_stackTimer.IsEnabled) _stackTimer.Start();
            }
            else
            {
                _stackTimer.Stop();
                ClearStackSlots();
            }
        }

        private void ClearStackSlots()
        {
            if (StackSlots.Count > 0)
            {
                var slotsToStop = StackSlots.ToList();
                StackSlots.Clear();
                _playbackService.Stop(slotsToStop);
            }
        }

        private async void StackTimer_Tick(object? sender, EventArgs e)
        {
            if (!IsStackableEnabled || IsCollageEnabled || IsSwapEnabled || !IsVideoPlaying || string.IsNullOrWhiteSpace(VideoPath))
            {
                return;
            }

            if (_isStackRunning) return;
            _isStackRunning = true;

            try
            {
                double effectiveW = ContainerWidth > 100 ? ContainerWidth : 1500;
                double effectiveH = ContainerHeight > 100 ? ContainerHeight : 800;

                int curRows = Rows;
                int curCols = Columns;
                double cellW = effectiveW / curCols;
                double cellH = effectiveH / curRows;
                double quadW = cellW / 2.0;
                double quadH = cellH / 2.0;

                if (_stackQuadrantStep >= 0 && _stackQuadrantStep < 4)
                {
                    // Quadrant offsets (Clockwise: 0=Top-Left, 1=Top-Right, 2=Bottom-Right, 3=Bottom-Left)
                    int quadIndex = _stackQuadrantStep;
                    _stackQuadrantStep++;

                    double qOffsetX = 0;
                    double qOffsetY = 0;

                    switch (quadIndex)
                    {
                        case 0: // Top-Left
                            qOffsetX = 0;
                            qOffsetY = 0;
                            break;
                        case 1: // Top-Right
                            qOffsetX = quadW;
                            qOffsetY = 0;
                            break;
                        case 2: // Bottom-Right
                            qOffsetX = quadW;
                            qOffsetY = quadH;
                            break;
                        case 3: // Bottom-Left
                            qOffsetX = 0;
                            qOffsetY = quadH;
                            break;
                    }

                    await LoadAndDisplayQuadrantSlotsAsync(quadIndex, qOffsetX, qOffsetY, quadW, quadH, curRows, curCols, cellW, cellH);
                }
                else
                {
                    // The 4 stack videos have completed.
                    _stackQuadrantStep = 0;

                    // Refresh base grid videos
                    await ExecutePlayback();

                    // Buffer delay: wait for base videos to decode and present frames before clearing overlays
                    await Task.Delay(1000);

                    // Clear the overlays from the previous 4-stack cycle
                    ClearStackSlots();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Stack timer error: {ex.Message}");
            }
            finally
            {
                _isStackRunning = false;
            }
        }

        private async Task LoadAndDisplayQuadrantSlotsAsync(int quadIndex, double qOffsetX, double qOffsetY, double quadW, double quadH, int curRows, int curCols, double cellW, double cellH)
        {
            var newSlots = new List<VideoSlotViewModel>();

            for (int r = 0; r < curRows; r++)
            {
                for (int c = 0; c < curCols; c++)
                {
                    double posX = Math.Round((c * cellW) + qOffsetX);
                    double posY = Math.Round((r * cellH) + qOffsetY);
                    double width = Math.Round(quadW);
                    double height = Math.Round(quadH);

                    var s = new VideoSlotViewModel
                    {
                        CollageX = posX,
                        CollageY = posY,
                        CollageWidth = width,
                        CollageHeight = height,
                        IsCollageVisible = false, // Start hidden off-screen while buffering
                        Opacity = 1.0,
                        Index = StackSlots.Count + newSlots.Count
                    };
                    newSlots.Add(s);
                }
            }

            foreach (var s in newSlots)
            {
                StackSlots.Add(s);
            }

            // Wait for window handles to be created
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 2500 && newSlots.Any(s => s.WindowHandle == IntPtr.Zero))
            {
                await Task.Delay(50);
            }

            var validSlots = newSlots.Where(s => s.WindowHandle != IntPtr.Zero).ToList();
            if (validSlots.Count > 0 && IsStackableEnabled)
            {
                var excluded = VideoSlots.Concat(StackSlots)
                    .Select(s => s.CurrentVideoPath)
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Cast<string>()
                    .ToHashSet();

                var vids = await _videoLibraryService.GetRandomVideosAsync(validSlots.Count, excluded, IsSingleVidEnabled);
                if (vids.Count > 0)
                {
                    for (int i = 0; i < validSlots.Count && i < vids.Count; i++)
                    {
                        validSlots[i].CurrentVideoPath = vids[i];
                    }
                    await _playbackService.PlayAsync(validSlots, vids);

                    // Buffer Delay: Wait for MPV to initialize, decode, and render frames before displaying
                    await Task.Delay(1000);

                    if (!IsStackableEnabled) return;

                    // Reveal all quadrant slots on screen simultaneously, already playing
                    foreach (var s in validSlots)
                    {
                        s.IsCollageVisible = true;
                    }
                }
            }
        }

        private Queue<int> _randomSlotQueue = new();
        private string? _currentSingleVidForMultiple;
        private HashSet<int> _slotsUpdatedInCycle = new();
        private int _staircaseIndex = 0;
        private bool _isShowingGrid1 = true;

        private class PreloadedSlotData
        {
            public VideoSlotViewModel Slot { get; set; } = null!;
            public string VideoPath { get; set; } = string.Empty;
            public System.Diagnostics.Process? Process { get; set; }
            public DateTime PreloadTime { get; set; } = DateTime.UtcNow;
        }

        private PreloadedSlotData? _nextPreloadedSlot;
        private bool _isPreloading = false;

        private void ClearPreloadedSlot()
        {
            if (_nextPreloadedSlot?.Process != null && !_nextPreloadedSlot.Process.HasExited)
            {
                try { _nextPreloadedSlot.Process.Kill(); } catch { }
                _nextPreloadedSlot.Process.Dispose();
            }
            _nextPreloadedSlot = null;
        }

        private async Task PreloadNextSlotAsync()
        {
            var activeSlots = IsScrollEnabled
                ? ScrollSlots.Where(s => s.CollageY >= -50 && s.CollageY <= ContainerHeight && s.WindowHandle != IntPtr.Zero).ToList()
                : VideoSlots.Where(s => s.WindowHandle != IntPtr.Zero).ToList();

            if (_isPreloading || IsSwapEnabled || SelectedRandomize == "None" || IsStackableEnabled || !IsVideoPlaying || activeSlots.Count == 0) return;
            if (string.IsNullOrWhiteSpace(VideoPath)) return;

            _isPreloading = true;
            try
            {
                VideoSlotViewModel? nextSlot = null;

                if (SelectedRandomize == "Staircase" && !IsScrollEnabled)
                {
                    var scIndices = GetStaircaseSlotIndices(Rows, Columns);
                    if (scIndices.Count == 0) return;

                    int peekIndex = _staircaseIndex % scIndices.Count;
                    int targetSlotIndex = scIndices[peekIndex];
                    if (targetSlotIndex < activeSlots.Count)
                    {
                        nextSlot = activeSlots[targetSlotIndex];
                    }
                }
                else if (SelectedRandomize == "Multiple" || SelectedRandomize == "Randomize Multiple" || SelectedRandomize == "Randomize multiple")
                {
                    if (_randomSlotQueue.Count == 0)
                    {
                        var indices = Enumerable.Range(0, activeSlots.Count).OrderBy(_ => _rnd.Next()).ToList();
                        foreach (var idx in indices) _randomSlotQueue.Enqueue(idx);
                    }

                    if (_randomSlotQueue.Count > 0)
                    {
                        int targetSlotIndex = _randomSlotQueue.Peek();
                        if (targetSlotIndex < activeSlots.Count)
                        {
                            nextSlot = activeSlots[targetSlotIndex];
                        }
                    }
                }

                if (nextSlot == null) return;

                if (_nextPreloadedSlot != null && _nextPreloadedSlot.Slot == nextSlot && _nextPreloadedSlot.Process != null && !_nextPreloadedSlot.Process.HasExited)
                {
                    return; // Already preloaded for this slot
                }

                ClearPreloadedSlot();

                string videoPath;
                bool isMultipleSingleVidMode = IsSingleVidEnabled && (SelectedRandomize == "Multiple" || SelectedRandomize == "Randomize Multiple" || SelectedRandomize == "Randomize multiple");

                if (isMultipleSingleVidMode)
                {
                    if (string.IsNullOrEmpty(_currentSingleVidForMultiple))
                    {
                        var excludedPaths = activeSlots.Select(s => s.CurrentVideoPath).Where(p => !string.IsNullOrEmpty(p)).Cast<string>().ToHashSet();
                        var singleVids = await _videoLibraryService.GetRandomVideosAsync(1, excludedPaths, isSingleVidMode: true);
                        if (singleVids.Count > 0) _currentSingleVidForMultiple = singleVids[0];
                    }

                    if (string.IsNullOrEmpty(_currentSingleVidForMultiple)) return;
                    videoPath = _currentSingleVidForMultiple;
                }
                else
                {
                    var excludedPaths = activeSlots.Select(s => s.CurrentVideoPath).Where(p => !string.IsNullOrEmpty(p)).Cast<string>().ToHashSet();
                    var newVideos = await _videoLibraryService.GetRandomVideosAsync(1, excludedPaths, IsSingleVidEnabled);
                    if (newVideos.Count == 0) return;
                    videoPath = newVideos[0];
                }
                if (nextSlot.WindowHandle == IntPtr.Zero) return;

                var proc = await _playbackService.PreloadMpvAsync(nextSlot, videoPath);

                if (proc != null)
                {
                    _nextPreloadedSlot = new PreloadedSlotData
                    {
                        Slot = nextSlot,
                        VideoPath = videoPath,
                        Process = proc,
                        PreloadTime = DateTime.UtcNow
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error preloading slot: {ex.Message}");
            }
            finally
            {
                _isPreloading = false;
            }
        }

        private void InitializeSwapTimer()
        {
            _swapTimer = new Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(Math.Max(0.1, SelectedDelay))
            };
            _swapTimer.Tick += SwapTimer_Tick;
            if (IsSwapEnabled) _swapTimer.Start();
        }

        private void InitializeRandomizeTimer()
        {
            _randomizeTimer = new Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(Math.Max(0.1, SelectedDelay))
            };
            _randomizeTimer.Tick += RandomizeTimer_Tick;
            UpdateRandomizeTimer();
        }

        private void UpdateRandomizeTimer()
        {
            if (_randomizeTimer == null) return;

            bool shouldRun = !IsSwapEnabled && SelectedRandomize != "None" && !IsStackableEnabled && (!IsScrollEnabled || SelectedRandomize == "Multiple");

            if (shouldRun)
            {
                _randomizeTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.1, SelectedDelay));
                if (!_randomizeTimer.IsEnabled) _randomizeTimer.Start();
                _ = PreloadNextSlotAsync();
            }
            else
            {
                _randomizeTimer.Stop();
                ClearPreloadedSlot();
            }
        }

        private async void RandomizeTimer_Tick(object? sender, EventArgs e)
        {
            if (IsSwapEnabled || SelectedRandomize == "None" || IsStackableEnabled || !IsVideoPlaying) return;
            if (string.IsNullOrWhiteSpace(VideoPath)) return;

            var activeSlots = IsScrollEnabled
                ? ScrollSlots.Where(s => s.CollageY >= -50 && s.CollageY <= ContainerHeight && s.WindowHandle != IntPtr.Zero).ToList()
                : VideoSlots.Where(s => s.WindowHandle != IntPtr.Zero).ToList();

            if (activeSlots.Count == 0) return;

            var targetSlots = new List<VideoSlotViewModel>();

            if (SelectedRandomize == "Staircase" && !IsScrollEnabled)
            {
                var scIndices = GetStaircaseSlotIndices(Rows, Columns);
                if (scIndices.Count > 0)
                {
                    if (_staircaseIndex >= scIndices.Count) _staircaseIndex = 0;
                    int targetSlotIndex = scIndices[_staircaseIndex];
                    _staircaseIndex = (_staircaseIndex + 1) % scIndices.Count;

                    if (targetSlotIndex < activeSlots.Count)
                    {
                        targetSlots.Add(activeSlots[targetSlotIndex]);
                    }
                }
            }
            else if (SelectedRandomize == "Multiple" || SelectedRandomize == "Randomize Multiple" || SelectedRandomize == "Randomize multiple")
            {
                int totalSlots = activeSlots.Count;
                int rawCount = totalSlots > 1 ? _rnd.Next(2, totalSlots + 1) : 1;
                // Scale back by another 50% (i.e. 12.5% of raw count, minimum 1)
                int countToSelect = Math.Max(1, (int)Math.Round(rawCount * 0.125));

                var selectedIndices = new HashSet<int>();
                while (selectedIndices.Count < countToSelect && selectedIndices.Count < totalSlots)
                {
                    if (_randomSlotQueue.Count == 0)
                    {
                        var indices = Enumerable.Range(0, totalSlots).OrderBy(_ => _rnd.Next()).ToList();
                        foreach (var idx in indices) _randomSlotQueue.Enqueue(idx);
                    }

                    int nextIdx = _randomSlotQueue.Dequeue();
                    if (nextIdx < totalSlots)
                    {
                        selectedIndices.Add(nextIdx);
                    }
                }

                foreach (var idx in selectedIndices)
                {
                    targetSlots.Add(activeSlots[idx]);
                }
            }

            if (targetSlots.Count > 0)
            {
                var slotsToFetch = new List<VideoSlotViewModel>();

                if (_nextPreloadedSlot != null && targetSlots.Contains(_nextPreloadedSlot.Slot) && _nextPreloadedSlot.Process != null && !_nextPreloadedSlot.Process.HasExited)
                {
                    var preloaded = _nextPreloadedSlot;
                    _nextPreloadedSlot = null;

                    // Ensure the preloaded instance has been running in the background for at least 1.0 second
                    var elapsedMs = (DateTime.UtcNow - preloaded.PreloadTime).TotalMilliseconds;
                    if (elapsedMs < 1000)
                    {
                        await Task.Delay((int)(1000 - elapsedMs));
                    }

                    _playbackService.SwapPreloadedSlot(preloaded.Slot, preloaded.Process, preloaded.VideoPath);

                    foreach (var slot in targetSlots)
                    {
                        if (slot != preloaded.Slot)
                        {
                            slotsToFetch.Add(slot);
                        }
                    }
                }
                else
                {
                    ClearPreloadedSlot();
                    slotsToFetch.AddRange(targetSlots);
                }

                if (slotsToFetch.Count > 0)
                {
                    List<string> newVideos;
                    bool isMultipleSingleVid = IsSingleVidEnabled && (SelectedRandomize == "Multiple" || SelectedRandomize == "Randomize Multiple" || SelectedRandomize == "Randomize multiple");

                    if (isMultipleSingleVid)
                    {
                        if (string.IsNullOrEmpty(_currentSingleVidForMultiple))
                        {
                            var excludedPaths = VideoSlots.Select(s => s.CurrentVideoPath).Where(p => !string.IsNullOrEmpty(p)).Cast<string>().ToHashSet();
                            var singleVids = await _videoLibraryService.GetRandomVideosAsync(1, excludedPaths, isSingleVidMode: true);
                            if (singleVids.Count > 0) _currentSingleVidForMultiple = singleVids[0];
                        }

                        if (!string.IsNullOrEmpty(_currentSingleVidForMultiple))
                        {
                            newVideos = Enumerable.Repeat(_currentSingleVidForMultiple, slotsToFetch.Count).ToList();
                        }
                        else
                        {
                            var excludedPaths = VideoSlots.Select(s => s.CurrentVideoPath).Where(p => !string.IsNullOrEmpty(p)).Cast<string>().ToHashSet();
                            newVideos = await _videoLibraryService.GetRandomVideosAsync(slotsToFetch.Count, excludedPaths, IsSingleVidEnabled);
                        }
                    }
                    else
                    {
                        var excludedPaths = VideoSlots.Select(s => s.CurrentVideoPath).Where(p => !string.IsNullOrEmpty(p)).Cast<string>().ToHashSet();
                        newVideos = await _videoLibraryService.GetRandomVideosAsync(slotsToFetch.Count, excludedPaths, IsSingleVidEnabled);
                    }

                    if (newVideos.Count > 0)
                    {
                        for (int i = 0; i < slotsToFetch.Count && i < newVideos.Count; i++)
                        {
                            var slot = slotsToFetch[i];
                            var video = newVideos[i];
                            if (slot.WindowHandle == IntPtr.Zero) continue;

                            var proc = await _playbackService.PreloadMpvAsync(slot, video);
                            if (proc != null)
                            {
                                // Wait at least 1.0s in background to ensure mpv has fully buffered/drawn initial frame
                                await Task.Delay(1000);
                                _playbackService.SwapPreloadedSlot(slot, proc, video);
                            }
                        }
                    }
                }

                bool isMultipleSingleVidMode = IsSingleVidEnabled && (SelectedRandomize == "Multiple" || SelectedRandomize == "Randomize Multiple" || SelectedRandomize == "Randomize multiple");
                if (isMultipleSingleVidMode && targetSlots.Count > 0)
                {
                    foreach (var slot in targetSlots)
                    {
                        int slotIndex = VideoSlots.IndexOf(slot);
                        if (slotIndex >= 0)
                        {
                            _slotsUpdatedInCycle.Add(slotIndex);
                        }
                    }

                    if (_slotsUpdatedInCycle.Count >= VideoSlots.Count)
                    {
                        _currentSingleVidForMultiple = null;
                        _slotsUpdatedInCycle.Clear();
                    }
                }
            }

            _ = PreloadNextSlotAsync();
        }

        private List<int> GetStaircaseSlotIndices(int rows, int cols)
        {
            var list = new List<int>();
            if (rows <= 0 || cols <= 0) return list;

            for (int r = 0; r < rows; r++)
            {
                if (r % 2 == 0)
                {
                    // Even row: Left to Right
                    for (int c = 0; c < cols; c++)
                    {
                        list.Add(r * cols + c);
                    }
                }
                else
                {
                    // Odd row: Right to Left
                    for (int c = cols - 1; c >= 0; c--)
                    {
                        list.Add(r * cols + c);
                    }
                }
            }

            return list;
        }

        private async void SwapTimer_Tick(object? sender, EventArgs e)
        {
            if (!IsSwapEnabled || !IsVideoPlaying) return;
            if (string.IsNullOrWhiteSpace(VideoPath)) return;

            _isShowingGrid1 = !_isShowingGrid1;
            string targetSize = _isShowingGrid1 ? SelectedGrid1 : SelectedGrid2;

            if (string.IsNullOrEmpty(targetSize)) return;

            var parts = targetSize.Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out int r) && int.TryParse(parts[1], out int c))
            {
                _suppressAutoRun = true;
                try { Rows = r; Columns = c; }
                finally { _suppressAutoRun = false; }

                UpdateGrid();
            }

            // Allow Avalonia a brief tick to instantiate any new slot HWNDs if total slot count expanded
            await Task.Delay(100);

            var activeSlots = VideoSlots.Where(s => s.WindowHandle != IntPtr.Zero).ToList();
            if (activeSlots.Count == 0) return;

            var excludedPaths = activeSlots.Select(s => s.CurrentVideoPath).Where(p => !string.IsNullOrEmpty(p)).Cast<string>().ToHashSet();
            var newVideos = await _videoLibraryService.GetRandomVideosAsync(activeSlots.Count, excludedPaths, IsSingleVidEnabled);
            if (newVideos.Count == 0) return;

            var tasks = new List<Task>();
            for (int i = 0; i < activeSlots.Count && i < newVideos.Count; i++)
            {
                var slot = activeSlots[i];
                var videoPath = newVideos[i];
                tasks.Add(Task.Run(async () =>
                {
                    var proc = await _playbackService.PreloadMpvAsync(slot, videoPath);
                    if (proc != null)
                    {
                        // Wait 1.0 second so new MPV instance renders initial frames directly into the active HWND over the old process
                        await Task.Delay(1000);
                        _playbackService.SwapPreloadedSlot(slot, proc, videoPath);
                    }
                }));
            }

            await Task.WhenAll(tasks);
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
                var newVideos = await _videoLibraryService.GetRandomVideosAsync(hiddenSlots.Count, excludedPaths, IsSingleVidEnabled);
                if (newVideos.Count == 0) return;

                // 2. Start new instances (Delegated to PlaybackService)
                await _playbackService.RefreshSlotsAsync(hiddenSlots, newVideos);
            }
            finally
            {
                _isRefreshing = false;
            }
        }
    }
}
