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

            if (IsScrollEnabled && ScrollSlots.Count > 0)
            {
                double effectiveH = ContainerHeight > 100 ? ContainerHeight : 800;
                list = ScrollSlots
                    .Where(s => (s.CollageY + s.CollageHeight) > 0 && s.CollageY < effectiveH)
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
        public ObservableCollection<VideoSlotViewModel> StackSlots { get; } = new();
        public ObservableCollection<VideoSlotViewModel> ScrollSlots { get; } = new();

        public MainViewModel() : this(new SettingsService())
        {
        }

        public MainViewModel(SettingsService settingsService)
        {
            _settingsService = settingsService;
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
            _isRandomSwapEnabled = settings.IsRandomSwapEnabled || (_selectedRandomize != "None");
            if (_isRandomSwapEnabled && _selectedRandomize == "None")
            {
                _selectedRandomize = "Multiple";
            }
            _isStackableEnabled = settings.IsStackableEnabled;
            _isScrollEnabled = settings.IsScrollEnabled;
            _scrollSpeed = settings.ScrollSpeed > 0 ? settings.ScrollSpeed : 80.0;
            _selectedScrollDirection = !string.IsNullOrEmpty(settings.SelectedScrollDirection) ? settings.SelectedScrollDirection : "Up";
            _isScrollDown = _selectedScrollDirection == "Down";
            _selectedDelay = settings.SelectedDelay > 0 ? settings.SelectedDelay : 10.0;
            _selectedCycleDelay = settings.SelectedCycleDelay > 0 ? settings.SelectedCycleDelay : 10.0;
            _isCycleModesEnabled = settings.IsCycleModesEnabled;
            _isDebugEnabled = settings.IsDebugEnabled;

            if (!string.IsNullOrEmpty(settings.SelectedDisplayMode))
            {
                _selectedDisplayMode = settings.SelectedDisplayMode;
                switch (settings.SelectedDisplayMode)
                {
                    case "Auto-Swap":
                        _isSwapEnabled = true;
                        _isStackableEnabled = false;
                        _isScrollEnabled = false;
                        _isBoomerangEnabled = false;
                        break;
                    case "Stackable":
                        _isSwapEnabled = false;
                        _isStackableEnabled = true;
                        _isScrollEnabled = false;
                        _isBoomerangEnabled = false;
                        break;
                    case "Boomerang":
                        _isSwapEnabled = false;
                        _isStackableEnabled = false;
                        _isScrollEnabled = false;
                        _isBoomerangEnabled = true;
                        break;
                    case "Scrolling Wall":
                        _isSwapEnabled = false;
                        _isStackableEnabled = false;
                        _isScrollEnabled = true;
                        _isBoomerangEnabled = false;
                        break;
                    case "Grid":
                    default:
                        _isSwapEnabled = false;
                        _isStackableEnabled = false;
                        _isScrollEnabled = false;
                        _isBoomerangEnabled = false;
                        break;
                }
            }
            else
            {
                SyncSelectedDisplayModeFromFlags();
            }

            _isGridVisible = !_isScrollEnabled;

            _isSloMoEnabled = settings.IsSloMoEnabled;

            _playbackService.IsRandomStartEnabled = _isRandomStartEnabled;
            _playbackService.IsMuted = true;
            _playbackService.Volume = _volume;
            _playbackService.IsSloMo = _isSloMoEnabled;

            InitializeOptions();
            InitializeSwapTimer();
            InitializeRandomizeTimer();
            InitializeStackTimer();
            InitializeBoomerangTimer();
            InitializeCycleModesTimer();

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
                        if (IsCycleModesEnabled)
                        {
                            StartCycleModes();
                        }
                        else if (IsScrollEnabled) StartScroll();
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
            if (IsScrollEnabled && !_isUpdatingDisplayMode)
            {
                var batch = GetCurrentActiveVideoBatch();
                if (batch.Count == 0 && _currentVideoBatch.Count > 0) batch = _currentVideoBatch.ToList();
                StartScroll(batch);
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
            if (IsScrollEnabled && !_isUpdatingDisplayMode)
            {
                var batch = GetCurrentActiveVideoBatch();
                if (batch.Count == 0 && _currentVideoBatch.Count > 0) batch = _currentVideoBatch.ToList();
                StartScroll(batch);
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
                SelectedCycleDelay = SelectedCycleDelay,
                SelectedRandomize = SelectedRandomize,
                IsRandomSwapEnabled = IsRandomSwapEnabled,
                IsStackableEnabled = IsStackableEnabled,
                IsScrollEnabled = IsScrollEnabled,
                IsBoomerangEnabled = IsBoomerangEnabled,
                IsCycleModesEnabled = IsCycleModesEnabled,
                SelectedDisplayMode = SelectedDisplayMode,
                ScrollSpeed = ScrollSpeed,
                SelectedScrollDirection = SelectedScrollDirection,
                IsDebugEnabled = IsDebugEnabled
            };
            _settingsService.SaveSettings(settings);
        }

        [ObservableProperty]
        private double _containerWidth = 1500; // Default fallback

        [ObservableProperty]
        private double _containerHeight = 800; // Default fallback

        private Random _rnd = new Random();


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
                    var distinctInitial = specificVideoList.Distinct().ToList();
                    selectedVideos = new List<string>(distinctInitial);
                    if (selectedVideos.Count < VideoSlots.Count)
                    {
                        var excluded = new HashSet<string>(selectedVideos);
                        var moreVids = await _videoLibraryService.GetRandomVideosAsync(VideoSlots.Count - selectedVideos.Count, excluded, isSingleVidMode: false);
                        selectedVideos.AddRange(moreVids);

                        // If still short, query without exclusion to avoid repeating a single video
                        if (selectedVideos.Count < VideoSlots.Count)
                        {
                            var unconstrained = await _videoLibraryService.GetRandomVideosAsync(VideoSlots.Count - selectedVideos.Count, null, isSingleVidMode: false);
                            selectedVideos.AddRange(unconstrained);
                        }

                        // If library has fewer videos than total slots, only then loop over distinct videos
                        int idx = 0;
                        while (selectedVideos.Count < VideoSlots.Count && distinctInitial.Count > 0)
                        {
                            selectedVideos.Add(distinctInitial[idx % distinctInitial.Count]);
                            idx++;
                        }
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

            if (IsBoomerangEnabled)
            {
                StartBoomerang();
            }

            if (IsCycleModesEnabled)
            {
                UpdateCycleModesTimerForCurrentMode();
            }

            if (!IsStackableEnabled && !IsSwapEnabled && !IsBoomerangEnabled && !IsCycleModesEnabled)
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

        [RelayCommand]
        public async Task Play()
        {
            if (string.IsNullOrWhiteSpace(VideoPath)) return;

            var existingBatch = GetCurrentActiveVideoBatch();
            Stop();

            await Task.Delay(150);

            if (IsScrollEnabled)
            {
                StartScroll(existingBatch);
            }
            else
            {
                await ExecutePlayback(existingBatch);
                if (IsSwapEnabled) _swapTimer?.Start();
                if (IsStackableEnabled) UpdateStackTimer();
            }

            if (IsCycleModesEnabled)
            {
                StartCycleModes();
            }

            if (SelectedRandomize != "None")
            {
                UpdateRandomizeTimer();
            }
        }

        [RelayCommand]
        public void Stop()
        {
            _swapTimer?.Stop();
            _randomizeTimer?.Stop();
            _stackTimer?.Stop();
            _scrollTimer?.Stop();
            _boomerangTimer?.Stop();
            _cycleModesTimer?.Stop();
            _playbackService.StopAll(VideoSlots, StackSlots);
            _playbackService.Stop(ScrollSlots);
            ClearStackSlots();
            ClearScrollSlots();
            ClearPreloadedSlot();
            IsVideoPlaying = false;
        }

        public void CleanupAllProcesses()
        {
            IsSwapEnabled = false;
            _swapTimer?.Stop();
            _randomizeTimer?.Stop();
            _stackTimer?.Stop();
            _scrollTimer?.Stop();
            _boomerangTimer?.Stop();
            _cycleModesTimer?.Stop();
            _playbackService.StopAll(VideoSlots, StackSlots);
            _playbackService.Stop(ScrollSlots);
            StackSlots.Clear();
            ScrollSlots.Clear();
        }

        public ObservableCollection<string> DisplayModeOptions { get; } = new()
        {
            "Auto-Swap",
            "Boomerang",
            "Grid",
            "Scrolling Wall",
            "Stackable"
        };
        public ObservableCollection<string> GridSizeOptions { get; } = new();
        public ObservableCollection<double> DelayOptions { get; } = new();
        public ObservableCollection<double> CycleDelayOptions { get; } = new();
        public ObservableCollection<string> RandomizeOptions { get; } = new()
        {
            "Multiple",
            "None"
        };

        private bool _isUpdatingDisplayMode = false;

        [ObservableProperty]
        private string _selectedDisplayMode = "Grid";

        partial void OnSelectedDisplayModeChanged(string value)
        {
            if (_isUpdatingDisplayMode) return;
            _isUpdatingDisplayMode = true;

            try
            {
                string oldMode = "";
                if (IsScrollEnabled) oldMode = "Scrolling Wall";
                else if (IsStackableEnabled) oldMode = "Stackable";
                else if (IsBoomerangEnabled) oldMode = "Boomerang";
                else if (IsSwapEnabled) oldMode = "Auto-Swap";
                else oldMode = "Grid";

                if (oldMode == value) return;

                _pendingCycleModeSwitch = false;
                _scrolledDistanceInCycle = 0.0;

                // Set default delays based on mode
                if (value == "Boomerang")
                {
                    SelectedDelay = 10.0;
                }
                else if (value is "Stackable" or "Grid" or "Scrolling Wall" or "Auto-Swap")
                {
                    SelectedDelay = 2.0;
                }

                ApplyModeTransition(oldMode, value);
                SaveSettings();
                if (IsCycleModesEnabled)
                {
                    UpdateCycleModesTimerForCurrentMode();
                }
            }
            finally
            {
                _isUpdatingDisplayMode = false;
            }
        }

        private void ApplyModeTransition(string oldMode, string value)
        {
            // Capture existing batch of videos BEFORE changing mode or stopping slots
            var existingBatch = GetCurrentActiveVideoBatch();
            if (existingBatch.Count == 0 && _currentVideoBatch.Count > 0)
            {
                existingBatch = _currentVideoBatch.ToList();
            }

            bool wasUsingVideoSlots = (oldMode == "Grid" || oldMode == "Auto-Swap" || oldMode == "Stackable" || oldMode == "Boomerang");
            bool willUseVideoSlots = (value == "Grid" || value == "Auto-Swap" || value == "Stackable" || value == "Boomerang");

            // Case 1: Seamless switch between Grid, Auto-Swap, Stackable, and Boomerang (all share VideoSlots - NEVER STOP)
            if (wasUsingVideoSlots && willUseVideoSlots)
            {
                if (oldMode == "Auto-Swap") _swapTimer?.Stop();
                if (oldMode == "Stackable")
                {
                    _stackTimer?.Stop();
                    ClearStackSlots();
                }
                if (oldMode == "Boomerang") StopBoomerang();

                IsScrollEnabled = false;
                IsGridVisible = true;

                if (value == "Auto-Swap")
                {
                    IsStackableEnabled = false;
                    IsBoomerangEnabled = false;
                    IsSwapEnabled = true;
                    UpdateGrid();
                    _swapTimer?.Start();
                }
                else if (value == "Stackable")
                {
                    IsSwapEnabled = false;
                    IsBoomerangEnabled = false;
                    IsStackableEnabled = true;
                    UpdateGrid();
                    UpdateStackTimer();
                }
                else if (value == "Boomerang")
                {
                    IsSwapEnabled = false;
                    IsStackableEnabled = false;
                    IsBoomerangEnabled = true;
                    UpdateGrid();
                    StartBoomerang();
                }
                else // "Grid"
                {
                    IsSwapEnabled = false;
                    IsStackableEnabled = false;
                    IsBoomerangEnabled = false;
                    _isShowingGrid1 = true;
                    UpdateGrid();
                }

                UpdateRandomizeTimer();
                return;
            }

            // Case 2: Transitioning to Scrolling Wall (Keep previous videos playing until scroll wall is ready)
            if (value == "Scrolling Wall")
            {
                _scrolledDistanceInCycle = 0.0;
                if (oldMode == "Auto-Swap") _swapTimer?.Stop();
                if (oldMode == "Stackable")
                {
                    _stackTimer?.Stop();
                    ClearStackSlots();
                }
                if (oldMode == "Boomerang") StopBoomerang();

                IsSwapEnabled = false;
                IsStackableEnabled = false;
                IsBoomerangEnabled = false;
                IsScrollEnabled = true;

                if (existingBatch.Count > 0)
                {
                    _currentVideoBatch = existingBatch.ToList();
                }

                if (!string.IsNullOrEmpty(VideoPath))
                {
                    _ = Task.Run(async () =>
                    {
                        await StartScrollAsync(existingBatch);
                        await Task.Delay(600);
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (VideoSlots.Any(s => s.CurrentProcess != null && !s.CurrentProcess.HasExited))
                            {
                                _playbackService.Stop(VideoSlots);
                                IsGridVisible = false;
                            }
                        });
                    });
                }
                UpdateRandomizeTimer();
                return;
            }

            // Case 3: Transitioning from Scrolling Wall to Grid / Auto-Swap / Stackable / Boomerang
            if (willUseVideoSlots)
            {
                if (oldMode == "Scrolling Wall")
                {
                    // Immediately halt scroll velocity and purge off-screen slots to eliminate GPU/CPU contention
                    _scrollTimer?.Stop();
                    CleanUpOffScreenScrollSlots();
                    IsScrollEnabled = false;
                }

                if (existingBatch.Count > 0)
                {
                    _currentVideoBatch = existingBatch.ToList();
                }

                if (!string.IsNullOrEmpty(VideoPath))
                {
                    _ = Task.Run(async () =>
                    {
                        await ExecutePlayback(existingBatch);
                        await Task.Delay(500);
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            // Reveal the grid and hide previous scroll simultaneously for seamless handoff
                            IsGridVisible = true;
                            UpdateGrid();

                            if (oldMode == "Scrolling Wall")
                            {
                                StopScroll();
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
                            else if (value == "Boomerang")
                            {
                                IsBoomerangEnabled = true;
                                StartBoomerang();
                            }
                            UpdateRandomizeTimer();
                        });
                    });
                }
                else
                {
                    IsGridVisible = true;
                    UpdateGrid();
                }

                UpdateRandomizeTimer();
            }
        }

        private void SyncSelectedDisplayModeFromFlags()
        {
            if (_isUpdatingDisplayMode) return;
            _isUpdatingDisplayMode = true;
            try
            {
                if (IsScrollEnabled) SelectedDisplayMode = "Scrolling Wall";
                else if (IsStackableEnabled) SelectedDisplayMode = "Stackable";
                else if (IsBoomerangEnabled) SelectedDisplayMode = "Boomerang";
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

            CycleDelayOptions.Add(5);
            CycleDelayOptions.Add(10);
            CycleDelayOptions.Add(15);
            CycleDelayOptions.Add(20);
            CycleDelayOptions.Add(30);
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
        private bool _isRandomSwapEnabled = false;
        partial void OnIsRandomSwapEnabledChanged(bool value)
        {
            SelectedRandomize = value ? "Multiple" : "None";
            _randomSlotQueue.Clear();
            _currentSingleVidForMultiple = null;
            _slotsUpdatedInCycle.Clear();
            ClearPreloadedSlot();
            SaveSettings();
            UpdateRandomizeTimer();
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDelayEnabled))]
        [NotifyPropertyChangedFor(nameof(IsRandomizeEnabled))]
        private string _selectedRandomize = "None";
        partial void OnSelectedRandomizeChanged(string value)
        {
            bool swapEnabled = value != "None";
            if (IsRandomSwapEnabled != swapEnabled)
            {
                IsRandomSwapEnabled = swapEnabled;
            }

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
            else if (!string.IsNullOrEmpty(VideoPath) && !_suppressAutoRun)
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
        [NotifyPropertyChangedFor(nameof(DebugActiveSlotsCount))]
        [NotifyPropertyChangedFor(nameof(DebugRecycledBatchCount))]
        private bool _isDebugEnabled;

        partial void OnIsDebugEnabledChanged(bool value)
        {
            SaveSettings();
            OnPropertyChanged(nameof(DebugActiveSlotsCount));
            OnPropertyChanged(nameof(DebugRecycledBatchCount));
        }

        public int DebugActiveSlotsCount
        {
            get
            {
                if (IsScrollEnabled) return ScrollSlots.Count(s => s.IsEffectiveVisible);
                if (IsStackableEnabled) return StackSlots.Count(s => s.IsEffectiveVisible);
                return VideoSlots.Count(s => s.IsVisible);
            }
        }

        public int DebugRecycledBatchCount => _currentVideoBatch.Count;

        public string DebugModeDetails
        {
            get
            {
                var parts = new List<string>();
                if (IsCycleModesEnabled) parts.Add($"Cycle ({SelectedCycleDelay}s)");
                if (IsSingleVidEnabled) parts.Add("SingleVid: On");
                if (SelectedRandomize != "None") parts.Add($"Rnd: {SelectedRandomize} ({SelectedDelay}s)");
                if (IsScrollEnabled) parts.Add($"Speed: {ScrollSpeed:0} ({SelectedScrollDirection})");
                if (IsSwapEnabled) parts.Add($"Swap: {SelectedGrid1} ↔ {SelectedGrid2}");
                if (!IsScrollEnabled) parts.Add($"Grid: {Rows}x{Columns}");
                return parts.Count > 0 ? string.Join(" • ", parts) : "Default settings";
            }
        }

        [ObservableProperty]
        private bool _isMuted = true;

        partial void OnIsMutedChanged(bool value)
        {
            SaveSettings();
            if (_playbackService != null)
            {
                var allSlots = VideoSlots.Concat(ScrollSlots).Concat(StackSlots);
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
                var allSlots = VideoSlots.Concat(ScrollSlots).Concat(StackSlots);
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
                var allSlots = VideoSlots.Concat(ScrollSlots).Concat(StackSlots);
                _playbackService.UpdateSpeed(allSlots, value);
            }
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsScrollVisible))]
        private bool _isFullScreen = false;

        partial void OnIsFullScreenChanged(bool value)
        {
        }

        public bool AreManualControlsEnabled => !IsSwapEnabled && !IsStackableEnabled && !IsBoomerangEnabled && !IsCycleModesEnabled;
        public bool IsRandomizeEnabled => !IsSwapEnabled && !IsStackableEnabled && !IsBoomerangEnabled && !IsCycleModesEnabled;
        public bool IsDelayEnabled => IsSwapEnabled || IsBoomerangEnabled || IsCycleModesEnabled || (!IsScrollEnabled && AreManualControlsEnabled && SelectedRandomize != "None") || IsStackableEnabled || (IsScrollEnabled && SelectedRandomize == "Multiple");

        public bool IsStackableVisible => !IsSwapEnabled && !IsBoomerangEnabled && !IsCycleModesEnabled && (Rows == 2 && (Columns == 2 || Columns == 4));
        public bool IsScrollVisible => !IsSwapEnabled && !IsBoomerangEnabled && !IsCycleModesEnabled;

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
        private bool _isCycleModesEnabled = false;

        partial void OnIsCycleModesEnabledChanged(bool value)
        {
            SaveSettings();
            if (value)
            {
                StartCycleModes();
            }
            else
            {
                StopCycleModes();
            }
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AreManualControlsEnabled))]
        [NotifyPropertyChangedFor(nameof(IsRandomizeEnabled))]
        [NotifyPropertyChangedFor(nameof(IsDelayEnabled))]
        [NotifyPropertyChangedFor(nameof(IsStackableVisible))]
        [NotifyPropertyChangedFor(nameof(IsScrollVisible))]
        private bool _isBoomerangEnabled = false;

        partial void OnIsBoomerangEnabledChanged(bool value)
        {
            if (_isUpdatingDisplayMode) return;
            SaveSettings();
            SelectedDisplayMode = value ? "Boomerang" : "Grid";
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
        private double _scrolledDistanceInCycle = 0.0;
        private int _scrollVideoBatchIndex = 0;
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
                : (_currentVideoBatch.Count > 0 ? _currentVideoBatch : GetCurrentActiveVideoBatch());

            int totalInitialSlots = (curRows + 1) * curCols;
            if (sourceVideos == null || sourceVideos.Count == 0)
            {
                sourceVideos = await _videoLibraryService.GetRandomVideosAsync(totalInitialSlots, null, IsSingleVidEnabled);
            }
            else if (!IsSingleVidEnabled && sourceVideos.Distinct().Count() < totalInitialSlots)
            {
                var distinctSource = sourceVideos.Distinct().ToList();
                var excluded = new HashSet<string>(distinctSource);
                var additional = await _videoLibraryService.GetRandomVideosAsync(totalInitialSlots - distinctSource.Count, excluded, isSingleVidMode: false);
                distinctSource.AddRange(additional);
                if (distinctSource.Count < totalInitialSlots)
                {
                    var unconstrained = await _videoLibraryService.GetRandomVideosAsync(totalInitialSlots - distinctSource.Count, null, isSingleVidMode: false);
                    distinctSource.AddRange(unconstrained);
                }
                sourceVideos = distinctSource;
            }

            if (sourceVideos != null && sourceVideos.Count > 0)
            {
                _currentVideoBatch = sourceVideos.ToList();
            }

            _scrollVideoBatchIndex = 0;
            Func<List<string>?> getNextRowVideos = () =>
            {
                if (sourceVideos == null || sourceVideos.Count == 0) return null;
                var rowList = new List<string>();
                for (int c = 0; c < curCols; c++)
                {
                    rowList.Add(sourceVideos[_scrollVideoBatchIndex % sourceVideos.Count]);
                    _scrollVideoBatchIndex++;
                }
                return rowList;
            };

            // Spawn initial rows for all visible rows on screen
            for (int r = 0; r < curRows; r++)
            {
                var rowVideos = getNextRowVideos();
                await SpawnScrollRowAsync(r * cellH, cellW, cellH, curCols, rowVideos);
            }

            if (SelectedScrollDirection == "Down")
            {
                // Spawn 1 pre-loading row above viewport
                var rowVideos = getNextRowVideos();
                _ = SpawnScrollRowAsync(-cellH, cellW, cellH, curCols, rowVideos);
            }
            else
            {
                // Spawn 1 pre-loading row below viewport
                var rowVideos = getNextRowVideos();
                _ = SpawnScrollRowAsync(curRows * cellH, cellW, cellH, curCols, rowVideos);
            }

            _lastScrollTick = DateTime.Now;
            _scrollTimer.Start();
        }

        private void StartScroll(List<string>? initialVideos = null)
        {
            initialVideos ??= GetCurrentActiveVideoBatch();
            if (initialVideos == null || initialVideos.Count == 0)
            {
                initialVideos = _currentVideoBatch.Count > 0 ? _currentVideoBatch.ToList() : null;
            }
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

        private void CleanUpOffScreenScrollSlots()
        {
            double effectiveH = ContainerHeight > 100 ? ContainerHeight : 800;
            var offScreen = ScrollSlots.Where(s => (s.CollageY + s.CollageHeight) <= 0 || s.CollageY >= effectiveH).ToList();
            if (offScreen.Count > 0)
            {
                foreach (var slot in offScreen)
                {
                    ScrollSlots.Remove(slot);
                }
                _playbackService.Stop(offScreen);
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
                                while (videos.Count < validSlots.Count)
                                {
                                    videos.Add(rowVideos[videos.Count % rowVideos.Count]);
                                }
                            }
                            else if (videos.Count > validSlots.Count)
                            {
                                videos = videos.Take(validSlots.Count).ToList();
                            }
                        }
                    }
                    else if (_currentVideoBatch != null && _currentVideoBatch.Count > 0)
                    {
                        if (IsSingleVidEnabled)
                        {
                            videos = Enumerable.Repeat(_currentVideoBatch[0], validSlots.Count).ToList();
                        }
                        else
                        {
                            videos = new List<string>();
                            for (int i = 0; i < validSlots.Count; i++)
                            {
                                videos.Add(_currentVideoBatch[_scrollVideoBatchIndex % _currentVideoBatch.Count]);
                                _scrollVideoBatchIndex++;
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
                        if (_currentVideoBatch == null || _currentVideoBatch.Count == 0)
                        {
                            _currentVideoBatch = videos.Distinct().ToList();
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

            // 4. In Cycle mode, track distance scrolled to guarantee at least 2 full rows before switching
            if (IsCycleModesEnabled && SelectedDisplayMode == "Scrolling Wall")
            {
                _scrolledDistanceInCycle += delta;
                double twoRowsDistance = 2.0 * cellH;
                if (_pendingCycleModeSwitch && _scrolledDistanceInCycle >= twoRowsDistance)
                {
                    _scrolledDistanceInCycle = 0.0;
                    _pendingCycleModeSwitch = false;
                    SwitchToRandomCycleMode();
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

        [ObservableProperty]
        private double _selectedCycleDelay = 10.0;
        partial void OnSelectedCycleDelayChanged(double value)
        {
            if (_cycleModesTimer != null) UpdateCycleModesTimerForCurrentMode();
            SaveSettings();
        }


        private Avalonia.Threading.DispatcherTimer? _swapTimer;
        private Avalonia.Threading.DispatcherTimer? _randomizeTimer;
        private Avalonia.Threading.DispatcherTimer? _stackTimer;
        private Avalonia.Threading.DispatcherTimer? _boomerangTimer;
        private Avalonia.Threading.DispatcherTimer? _cycleModesTimer;
        private readonly string[] _availableCycleModes =
        {
            "Boomerang",
            "Grid",
            "Scrolling Wall",
            "Stackable"
        };

        private void InitializeCycleModesTimer()
        {
            _cycleModesTimer = new Avalonia.Threading.DispatcherTimer();
            _cycleModesTimer.Tick += CycleModesTimer_Tick;
            UpdateCycleModesTimerForCurrentMode();
        }

        public double GetModeFlowDuration(string mode)
        {
            if (mode == "Scrolling Wall")
            {
                // Ensure at least 2 full rows are scrolled before switching, or SelectedCycleDelay, whichever is longer
                double effectiveH = ContainerHeight > 100 ? ContainerHeight : 800;
                int curRows = Rows > 0 ? Rows : 2;
                double rowH = effectiveH / (double)curRows;
                double twoRowsDuration = (2.0 * rowH) / Math.Max(20.0, ScrollSpeed);
                return Math.Max(SelectedCycleDelay, twoRowsDuration);
            }
            if (mode == "Stackable")
            {
                // Ensure the complete 4-quadrant reveal lifecycle + base refresh buffer finishes
                // 4 quadrants * SelectedDelay + 1.5s refresh buffer
                double stackFlowDuration = (Math.Max(0.1, SelectedDelay) * 4.0) + 1.5;
                return Math.Max(SelectedCycleDelay, stackFlowDuration);
            }
            return Math.Max(2.0, SelectedCycleDelay);
        }

        private void UpdateCycleModesTimerForCurrentMode()
        {
            if (_cycleModesTimer == null) return;
            if (IsCycleModesEnabled && IsVideoPlaying)
            {
                double duration = GetModeFlowDuration(SelectedDisplayMode);
                _cycleModesTimer.Interval = TimeSpan.FromSeconds(duration);
                _cycleModesTimer.Stop();
                _cycleModesTimer.Start();
            }
            else
            {
                _cycleModesTimer.Stop();
            }
        }

        private void StartCycleModes()
        {
            _pendingCycleModeSwitch = false;
            _scrolledDistanceInCycle = 0.0;
            if (_cycleModesTimer == null)
            {
                InitializeCycleModesTimer();
            }
            // Immediately start selecting a random Display mode
            SwitchToRandomCycleMode();
            UpdateCycleModesTimerForCurrentMode();
        }

        private void StopCycleModes()
        {
            _pendingCycleModeSwitch = false;
            _cycleModesTimer?.Stop();
        }

        private void SwitchToRandomCycleMode()
        {
            if (!IsCycleModesEnabled) return;
            _pendingCycleModeSwitch = false;
            var otherModes = _availableCycleModes.Where(m => m != SelectedDisplayMode).ToList();
            if (otherModes.Count == 0) return;

            string nextMode = otherModes[_rnd.Next(otherModes.Count)];
            SelectedDisplayMode = nextMode;
        }

        private bool _pendingCycleModeSwitch = false;

        private void CycleModesTimer_Tick(object? sender, EventArgs e)
        {
            if (!IsCycleModesEnabled || !IsVideoPlaying || string.IsNullOrWhiteSpace(VideoPath)) return;

            // If Stackable is actively mid-quadrant sequence (steps 1, 2, 3), let it complete the full 4 quadrants
            if (SelectedDisplayMode == "Stackable" && _stackQuadrantStep > 0)
            {
                _pendingCycleModeSwitch = true;
                return;
            }

            // If Scrolling Wall has not yet scrolled at least 2 full rows, let it finish scrolling before switching
            if (SelectedDisplayMode == "Scrolling Wall")
            {
                int curRows = Rows > 0 ? Rows : 2;
                double effectiveH = ContainerHeight > 100 ? ContainerHeight : 800;
                double cellH = effectiveH / (double)curRows;
                double twoRowsDistance = 2.0 * cellH;
                if (_scrolledDistanceInCycle < twoRowsDistance)
                {
                    _pendingCycleModeSwitch = true;
                    return;
                }
            }

            SwitchToRandomCycleMode();
        }

        private int _stackQuadrantStep = 0; // 0 = reveal Quad 1, 1 = reveal Quad 2, 2 = reveal Quad 3, 3 = reveal Quad 4
        private bool _isStackRunning = false;

        private void InitializeBoomerangTimer()
        {
            _boomerangTimer = new Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _boomerangTimer.Tick += BoomerangTimer_Tick;
            UpdateBoomerangTimer();
        }

        private void UpdateBoomerangTimer()
        {
            if (_boomerangTimer == null) return;
            if (IsBoomerangEnabled && IsVideoPlaying)
            {
                ResetAllSlotsBoomerangState();
                if (!_boomerangTimer.IsEnabled) _boomerangTimer.Start();
            }
            else
            {
                _boomerangTimer.Stop();
            }
        }

        private void StartBoomerang()
        {
            ResetAllSlotsBoomerangState();
            if (_boomerangTimer == null)
            {
                InitializeBoomerangTimer();
            }
            else
            {
                if (!_boomerangTimer.IsEnabled) _boomerangTimer.Start();
            }
        }

        private void StopBoomerang()
        {
            _boomerangTimer?.Stop();
            _playbackService.ResetPlayDirectionAndSpeed(VideoSlots, IsSloMoEnabled);
        }

        private void ResetAllSlotsBoomerangState()
        {
            var now = DateTime.UtcNow;
            foreach (var slot in VideoSlots)
            {
                slot.BoomerangStartTime = now;
                slot.BoomerangPhase = 0;
            }
        }

        private async void BoomerangTimer_Tick(object? sender, EventArgs e)
        {
            if (!IsBoomerangEnabled || !IsVideoPlaying || string.IsNullOrWhiteSpace(VideoPath)) return;

            double totalDelay = Math.Max(0.5, SelectedDelay);
            double phaseLength = totalDelay / 3.0;
            var now = DateTime.UtcNow;
            var slotsToSwap = new List<VideoSlotViewModel>();

            foreach (var slot in VideoSlots)
            {
                if (slot.CurrentProcess == null || slot.CurrentProcess.HasExited) continue;

                if (slot.BoomerangPhase == 4)
                {
                    // Slot is waiting for incoming preloaded video swap; do not send direction commands to old process
                    continue;
                }

                if (slot.BoomerangStartTime == DateTime.MinValue)
                {
                    slot.BoomerangStartTime = now;
                    slot.BoomerangPhase = 0;
                }

                double elapsed = (now - slot.BoomerangStartTime).TotalSeconds;

                if (elapsed < phaseLength)
                {
                    // Phase 1: Forward at 100% speed
                    if (slot.BoomerangPhase != 1)
                    {
                        slot.BoomerangPhase = 1;
                        _playbackService.SetPlayDirectionAndSpeed(slot, isForward: true, speed: 1.0);
                    }
                }
                else if (elapsed < phaseLength * 2.0)
                {
                    // Phase 2: Backward at 100% speed
                    if (slot.BoomerangPhase != 2)
                    {
                        slot.BoomerangPhase = 2;
                        _playbackService.SetPlayDirectionAndSpeed(slot, isForward: false, speed: 1.0);
                    }
                }
                else if (elapsed < totalDelay)
                {
                    // Phase 3: Forward at 100% speed
                    if (slot.BoomerangPhase != 3)
                    {
                        slot.BoomerangPhase = 3;
                        _playbackService.SetPlayDirectionAndSpeed(slot, isForward: true, speed: 1.0);
                    }
                }
                else
                {
                    // Full display time completed (Phase 1, 2, 3 finished)!
                    slot.BoomerangPhase = 4;
                    slotsToSwap.Add(slot);
                }
            }

            if (slotsToSwap.Count > 0)
            {
                var excludedPaths = VideoSlots.Select(s => s.CurrentVideoPath).Where(p => !string.IsNullOrEmpty(p)).Cast<string>().ToHashSet();
                var newVideos = await GetRecycledOrFreshVideosAsync(slotsToSwap.Count, excludedPaths, preferExclusion: true);
                if (newVideos.Count > 0)
                {
                    for (int i = 0; i < slotsToSwap.Count && i < newVideos.Count; i++)
                    {
                        var slot = slotsToSwap[i];
                        var video = newVideos[i];
                        if (slot.WindowHandle == IntPtr.Zero) continue;

                        var (proc, ipcPipe) = await _playbackService.PreloadMpvAsync(slot, video);
                        if (proc != null)
                        {
                            await Task.Delay(400);
                            _playbackService.SwapPreloadedSlot(slot, proc, video, ipcPipe);
                            slot.BoomerangStartTime = DateTime.UtcNow;
                            slot.BoomerangPhase = 0;
                        }
                        else
                        {
                            slot.BoomerangStartTime = DateTime.UtcNow;
                            slot.BoomerangPhase = 0;
                        }
                    }
                }
                else
                {
                    foreach (var slot in slotsToSwap)
                    {
                        slot.BoomerangStartTime = DateTime.UtcNow;
                        slot.BoomerangPhase = 0;
                    }
                }
            }
        }

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

            if (IsStackableEnabled && !IsSwapEnabled)
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
            if (!IsStackableEnabled || IsSwapEnabled || !IsVideoPlaying || string.IsNullOrWhiteSpace(VideoPath))
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

                    // If a cycle mode switch was delayed waiting for Stackable to finish its full flow, switch now
                    if (_pendingCycleModeSwitch && IsCycleModesEnabled)
                    {
                        _pendingCycleModeSwitch = false;
                        SwitchToRandomCycleMode();
                    }
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

                var vids = await GetRecycledOrFreshVideosAsync(validSlots.Count, excluded, preferExclusion: false);
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
        private bool _isShowingGrid1 = true;

        private class PreloadedSlotData
        {
            public VideoSlotViewModel Slot { get; set; } = null!;
            public string VideoPath { get; set; } = string.Empty;
            public System.Diagnostics.Process? Process { get; set; }
            public string? IpcPipeName { get; set; }
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
            if (IsScrollEnabled) return;

            var activeSlots = VideoSlots.Where(s => s.WindowHandle != IntPtr.Zero).ToList();

            if (_isPreloading || IsSwapEnabled || SelectedRandomize == "None" || IsStackableEnabled || !IsVideoPlaying || activeSlots.Count == 0) return;
            if (string.IsNullOrWhiteSpace(VideoPath)) return;

            _isPreloading = true;
            try
            {
                VideoSlotViewModel? nextSlot = null;

                if (SelectedRandomize == "Multiple" || SelectedRandomize == "Randomize Multiple" || SelectedRandomize == "Randomize multiple" || IsRandomSwapEnabled)
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

                var (proc, ipcPipe) = await _playbackService.PreloadMpvAsync(nextSlot, videoPath);

                if (proc != null)
                {
                    _nextPreloadedSlot = new PreloadedSlotData
                    {
                        Slot = nextSlot,
                        VideoPath = videoPath,
                        Process = proc,
                        IpcPipeName = ipcPipe,
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

            bool shouldRun = !IsSwapEnabled && SelectedRandomize != "None" && !IsStackableEnabled && !IsScrollEnabled;

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

        public bool IsRandomizeTimerRunning => _randomizeTimer != null && _randomizeTimer.IsEnabled;

        public IReadOnlyList<string> CurrentVideoBatch => _currentVideoBatch;

        /// <summary>
        /// Attempts to recycle as many videos as possible from _currentVideoBatch / existing batch
        /// before calling the video library service for new random videos.
        /// </summary>
        private async Task<List<string>> GetRecycledOrFreshVideosAsync(int count, HashSet<string>? excludedPaths = null, bool preferExclusion = true)
        {
            if (count <= 0) return new List<string>();

            // Always attempt to capture/synchronize from active slots if current batch is empty
            if (_currentVideoBatch == null || _currentVideoBatch.Count == 0)
            {
                GetCurrentActiveVideoBatch();
            }

            var result = new List<string>();

            if (IsSingleVidEnabled)
            {
                if (_currentVideoBatch != null && _currentVideoBatch.Count > 0)
                {
                    string singleVid = _currentVideoBatch[0];
                    if (excludedPaths == null || !excludedPaths.Contains(singleVid))
                    {
                        return Enumerable.Repeat(singleVid, count).ToList();
                    }
                }
                var freshSingle = await _videoLibraryService.GetRandomVideosAsync(1, excludedPaths, isSingleVidMode: true);
                if (freshSingle.Count > 0)
                {
                    return Enumerable.Repeat(freshSingle[0], count).ToList();
                }
                return result;
            }

            // In multi-video mode, only recycle from _currentVideoBatch if count is small (like a single slot swap) or explicitly requested.
            // But when generating batches or swapping, ensure we do NOT force the same single video repeatedly unless IsSingleVidEnabled is true.
            if (!IsSingleVidEnabled && _currentVideoBatch != null && _currentVideoBatch.Count > 1)
            {
                var candidatePool = _currentVideoBatch.Where(v => !string.IsNullOrEmpty(v)).Distinct().ToList();

                if (preferExclusion && excludedPaths != null && excludedPaths.Count > 0)
                {
                    var nonExcludedCandidates = candidatePool.Where(v => !excludedPaths.Contains(v)).ToList();
                    if (nonExcludedCandidates.Count > 0)
                    {
                        foreach (var vid in nonExcludedCandidates)
                        {
                            if (result.Count < count)
                            {
                                result.Add(vid);
                            }
                        }
                    }
                }
                else
                {
                    foreach (var vid in candidatePool)
                    {
                        if (result.Count < count)
                        {
                            result.Add(vid);
                        }
                    }
                }
            }

            // If we still need more videos, fetch the remainder from the library service
            int needed = count - result.Count;
            if (needed > 0)
            {
                var combinedExcluded = new HashSet<string>(result);
                if (excludedPaths != null)
                {
                    foreach (var p in excludedPaths) combinedExcluded.Add(p);
                }

                var freshVideos = await _videoLibraryService.GetRandomVideosAsync(needed, combinedExcluded, IsSingleVidEnabled);
                result.AddRange(freshVideos);

                // If still short (e.g. library has fewer videos than requested after exclusion),
                // query library without exclusion to avoid repeating a single video
                if (result.Count < count && !IsSingleVidEnabled)
                {
                    int remaining = count - result.Count;
                    var unconstrained = await _videoLibraryService.GetRandomVideosAsync(remaining, null, isSingleVidMode: false);
                    result.AddRange(unconstrained);
                }

                // If still short, only recycle from distinct candidate pool if available
                if (result.Count < count && _currentVideoBatch != null && _currentVideoBatch.Count > 0)
                {
                    var distinctBatch = _currentVideoBatch.Where(v => !string.IsNullOrEmpty(v)).Distinct().ToList();
                    if (IsSingleVidEnabled || distinctBatch.Count > 1)
                    {
                        int idx = 0;
                        while (result.Count < count && distinctBatch.Count > 0)
                        {
                            result.Add(distinctBatch[idx % distinctBatch.Count]);
                            idx++;
                        }
                    }
                }
            }

            return result;
        }

        private async void RandomizeTimer_Tick(object? sender, EventArgs e)
        {
            if (IsScrollEnabled || IsSwapEnabled || SelectedRandomize == "None" || IsStackableEnabled || !IsVideoPlaying) return;
            if (string.IsNullOrWhiteSpace(VideoPath)) return;

            var activeSlots = IsScrollEnabled
                ? ScrollSlots.Where(s => s.CollageY >= -50 && s.CollageY <= ContainerHeight && s.WindowHandle != IntPtr.Zero).ToList()
                : VideoSlots.Where(s => s.WindowHandle != IntPtr.Zero).ToList();

            if (activeSlots.Count == 0) return;

            var targetSlots = new List<VideoSlotViewModel>();

            if (SelectedRandomize == "Multiple" || SelectedRandomize == "Randomize Multiple" || SelectedRandomize == "Randomize multiple" || IsRandomSwapEnabled)
            {
                int totalSlots = activeSlots.Count;
                if (_randomSlotQueue.Count == 0)
                {
                    var indices = Enumerable.Range(0, totalSlots).OrderBy(_ => _rnd.Next()).ToList();
                    foreach (var idx in indices) _randomSlotQueue.Enqueue(idx);
                }

                int nextIdx = _randomSlotQueue.Dequeue();
                if (nextIdx < totalSlots)
                {
                    targetSlots.Add(activeSlots[nextIdx]);
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

                    _playbackService.SwapPreloadedSlot(preloaded.Slot, preloaded.Process, preloaded.VideoPath, preloaded.IpcPipeName);

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

                            var (proc, ipcPipe) = await _playbackService.PreloadMpvAsync(slot, video);
                            if (proc != null)
                            {
                                // Wait at least 1.0s in background to ensure mpv has fully buffered/drawn initial frame
                                await Task.Delay(1000);
                                _playbackService.SwapPreloadedSlot(slot, proc, video, ipcPipe);
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
            var newVideos = await GetRecycledOrFreshVideosAsync(activeSlots.Count, excludedPaths, preferExclusion: true);
            if (newVideos.Count == 0) return;

            if (newVideos.Count > 0)
            {
                _currentVideoBatch = newVideos.Distinct().ToList();
            }

            var tasks = new List<Task>();
            for (int i = 0; i < activeSlots.Count && i < newVideos.Count; i++)
            {
                var slot = activeSlots[i];
                var videoPath = newVideos[i];
                tasks.Add(Task.Run(async () =>
                {
                    var (proc, ipcPipe) = await _playbackService.PreloadMpvAsync(slot, videoPath);
                    if (proc != null)
                    {
                        // Wait 1.0 second so new MPV instance renders initial frames directly into the active HWND over the old process
                        await Task.Delay(1000);
                        _playbackService.SwapPreloadedSlot(slot, proc, videoPath, ipcPipe);
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

                // 1. Pick videos prioritizing recycled videos from the existing batch
                // Exclude currently visible slots so the hidden slots don't duplicate visible ones if possible
                var visiblePaths = VideoSlots.Where(s => s.IsVisible).Select(s => s.CurrentVideoPath).Where(p => !string.IsNullOrEmpty(p)).Cast<string>().ToHashSet();
                var newVideos = await GetRecycledOrFreshVideosAsync(hiddenSlots.Count, visiblePaths, preferExclusion: true);
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
