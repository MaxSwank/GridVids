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
        public IReadOnlyList<string> CurrentVideoBatch => _currentVideoBatch;


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
            if (SelectedDisplayMode == "Stackable")
            {
                if (Rows != 2)
                {
                    _suppressAutoRun = true;
                    try { Rows = 2; }
                    finally { _suppressAutoRun = false; }
                }
                if (Columns != 2 && Columns != 4)
                {
                    _suppressAutoRun = true;
                    try { Columns = 2; }
                    finally { _suppressAutoRun = false; }
                }
                IsStackableEnabled = true;
            }
            else if (IsStackableEnabled && (Rows != 2 || (Columns != 2 && Columns != 4)))
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
            if (SelectedDisplayMode == "Stackable")
            {
                if (Rows != 2)
                {
                    _suppressAutoRun = true;
                    try { Rows = 2; }
                    finally { _suppressAutoRun = false; }
                }
                if (Columns != 2 && Columns != 4)
                {
                    _suppressAutoRun = true;
                    try { Columns = 2; }
                    finally { _suppressAutoRun = false; }
                }
                IsStackableEnabled = true;
            }
            else if (IsStackableEnabled && (Rows != 2 || (Columns != 2 && Columns != 4)))
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
                        // Strictly recycle/loop through the existing videos without querying the library
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

        partial void OnSelectedDisplayModeChanged(string? oldValue, string newValue)
        {
            if (_isUpdatingDisplayMode) return;
            _isUpdatingDisplayMode = true;

            try
            {
                string oldMode = oldValue ?? (IsScrollEnabled ? "Scrolling Wall" : (IsStackableEnabled ? "Stackable" : (IsBoomerangEnabled ? "Boomerang" : (IsSwapEnabled ? "Auto-Swap" : "Grid"))));

                if (oldMode == newValue) return;

                _pendingCycleModeSwitch = false;
                _isAligningScrollForCycleSwitch = false;
                _scrolledDistanceInCycle = 0.0;

                // Set default delays based on mode
                if (newValue == "Boomerang")
                {
                    SelectedDelay = 10.0;
                }
                else if (newValue is "Stackable" or "Grid" or "Scrolling Wall" or "Auto-Swap")
                {
                    SelectedDelay = 2.0;
                }

                ApplyModeTransition(oldMode, newValue);
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

            // Case 1: Seamless switch between Grid, Auto-Swap, Stackable, and Boomerang
            if (wasUsingVideoSlots && willUseVideoSlots)
            {
                if (oldMode == "Auto-Swap") _swapTimer?.Stop();
                if (oldMode == "Boomerang") StopBoomerang();

                bool transitioningFromStackable = (oldMode == "Stackable" && value != "Stackable");

                if (oldMode == "Stackable")
                {
                    _stackTimer?.Stop();
                    // If transitioning to another mode, do NOT clear stack slots immediately;
                    // keep them displayed so there is zero blank space while incoming VideoSlots start.
                    if (!transitioningFromStackable)
                    {
                        ClearStackSlots();
                    }
                }

                IsScrollEnabled = false;
                IsGridVisible = true;

                if (value == "Auto-Swap")
                {
                    IsBoomerangEnabled = false;
                    IsSwapEnabled = true;
                    UpdateGrid();
                    _swapTimer?.Start();
                }
                else if (value == "Stackable")
                {
                    IsSwapEnabled = false;
                    IsBoomerangEnabled = false;
                    Rows = 2;
                    if (Columns != 2 && Columns != 4)
                    {
                        Columns = 2;
                    }
                    IsStackableEnabled = true;
                    UpdateGrid();
                    UpdateStackTimer();
                }
                else if (value == "Boomerang")
                {
                    IsSwapEnabled = false;
                    IsBoomerangEnabled = true;
                    UpdateGrid();
                    StartBoomerang();
                }
                else // "Grid"
                {
                    IsSwapEnabled = false;
                    IsBoomerangEnabled = false;
                    _isShowingGrid1 = true;
                    UpdateGrid();
                }

                if (transitioningFromStackable && existingBatch.Count > 0 && !string.IsNullOrEmpty(VideoPath))
                {
                    // Incoming mode needs the video batch that was visible in Stackable.
                    // Keep StackSlots visible until the new VideoSlots start decoding.
                    _ = Task.Run(async () =>
                    {
                        await ExecutePlayback(existingBatch);
                        await Task.Delay(600);
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (SelectedDisplayMode == "Stackable") return;

                            _isUpdatingDisplayMode = true;
                            try
                            {
                                IsStackableEnabled = false;
                                ClearStackSlots();
                            }
                            finally
                            {
                                _isUpdatingDisplayMode = false;
                            }
                        });
                    });
                }
                else if (transitioningFromStackable)
                {
                    IsStackableEnabled = false;
                    ClearStackSlots();
                }
                else if (IsVideoPlaying && !string.IsNullOrEmpty(VideoPath))
                {
                    // For transitions between other video-slot modes (e.g. Grid -> Stackable, Grid -> Boomerang, etc.),
                    // ensure all base grid slots are actively playing with non-empty videos.
                    bool needsPlayback = (value == "Stackable") ||
                                         VideoSlots.Any(s => string.IsNullOrEmpty(s.CurrentVideoPath) || s.CurrentProcess == null || s.CurrentProcess.HasExited);
                    if (needsPlayback)
                    {
                        _ = ExecutePlayback(existingBatch.Count > 0 ? existingBatch : null);
                    }
                }

                UpdateRandomizeTimer();
                return;
            }

            // Case 2: Transitioning to Scrolling Wall (Keep previous videos playing until scroll wall is ready)
            if (value == "Scrolling Wall")
            {
                _scrolledDistanceInCycle = 0.0;
                if (oldMode == "Auto-Swap") _swapTimer?.Stop();
                if (oldMode == "Boomerang") StopBoomerang();
                if (oldMode == "Stackable")
                {
                    _stackTimer?.Stop();
                    // Keep StackSlots overlay visible while Scrolling Wall buffers
                }

                IsSwapEnabled = false;
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
                            if (SelectedDisplayMode == "Stackable") return;

                            _isUpdatingDisplayMode = true;
                            try
                            {
                                if (oldMode == "Stackable")
                                {
                                    IsStackableEnabled = false;
                                    ClearStackSlots();
                                }

                                if (VideoSlots.Any(s => s.CurrentProcess != null && !s.CurrentProcess.HasExited))
                                {
                                    _playbackService.Stop(VideoSlots);
                                    IsGridVisible = false;
                                }
                            }
                            finally
                            {
                                _isUpdatingDisplayMode = false;
                            }
                        });
                    });
                }
                else if (oldMode == "Stackable")
                {
                    IsStackableEnabled = false;
                    ClearStackSlots();
                }

                UpdateRandomizeTimer();
                return;
            }

            // Case 3: Transitioning from Scrolling Wall to Grid / Auto-Swap / Stackable / Boomerang
            if (willUseVideoSlots)
            {
                if (oldMode == "Scrolling Wall")
                {
                    // Immediately halt scroll velocity and purge off-screen slots to eliminate GPU/CPU contention.
                    // Keep IsScrollEnabled = true so the docked scrolling wall remains visible on screen
                    // while the incoming grid slots start up in the background (preventing any blank/black gap).
                    _scrollTimer?.Stop();
                    CleanUpOffScreenScrollSlots();
                }

                if (value == "Stackable")
                {
                    Rows = 2;
                    if (Columns != 2 && Columns != 4)
                    {
                        Columns = 2;
                    }
                    IsStackableEnabled = true;
                    UpdateGrid();
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
                                IsScrollEnabled = false;
                            }

                            if (value == "Auto-Swap")
                            {
                                IsSwapEnabled = true;
                                _swapTimer?.Start();
                            }
                            else if (value == "Stackable")
                            {
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
                    if (oldMode == "Scrolling Wall")
                    {
                        StopScroll();
                        IsScrollEnabled = false;
                    }
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
            if (value)
            {
                if (SelectedCycleDelay <= 0 || !CycleDelayOptions.Contains(SelectedCycleDelay))
                {
                    SelectedCycleDelay = 10.0;
                }
            }
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
            if (CycleDelayOptions.Count > 0 && !CycleDelayOptions.Contains(value))
            {
                SelectedCycleDelay = 10.0;
                return;
            }

            if (_cycleModesTimer != null) UpdateCycleModesTimerForCurrentMode();
            SaveSettings();
        }
    }
}
