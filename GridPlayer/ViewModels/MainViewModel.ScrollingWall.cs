using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace GridVids.ViewModels
{
    public partial class MainViewModel
    {
        private Avalonia.Threading.DispatcherTimer? _scrollTimer;
        private DateTime _lastScrollTick;
        private bool _isSpawningScrollRow = false;
        private bool _isAligningScrollForCycleSwitch = false;
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

            // 4. In Cycle mode, track distance scrolled to guarantee at least 2 full rows,
            // then smoothly dock the scroll onto exact integer row boundaries (0, cellH, 2*cellH...)
            // before initiating the seamless transition to the next display mode.
            if (IsCycleModesEnabled && SelectedDisplayMode == "Scrolling Wall")
            {
                _scrolledDistanceInCycle += delta;
                double twoRowsDistance = 2.0 * cellH;

                if (_pendingCycleModeSwitch && _scrolledDistanceInCycle >= twoRowsDistance)
                {
                    _isAligningScrollForCycleSwitch = true;
                }

                if (_isAligningScrollForCycleSwitch)
                {
                    // Find an on-screen anchor slot to measure distance to the nearest integer row boundary
                    var anchor = ScrollSlots.FirstOrDefault(s => s.CollageY >= -cellH * 0.5 && s.CollageY < effectiveH);
                    if (anchor == null)
                    {
                        _isAligningScrollForCycleSwitch = false;
                        _pendingCycleModeSwitch = false;
                        _scrolledDistanceInCycle = 0.0;
                        SwitchToNextCycleMode();
                        return;
                    }

                    // Target row boundary in the current scroll direction
                    double targetY = isDown
                        ? Math.Ceiling(anchor.CollageY / cellH) * cellH
                        : Math.Floor(anchor.CollageY / cellH) * cellH;

                    double remainingDistance = Math.Abs(targetY - anchor.CollageY);

                    // If already aligned within a tiny threshold (or next step would overshoot)
                    if (remainingDistance <= 0.001 || remainingDistance <= delta)
                    {
                        // Apply the exact final adjustment to land precisely on the target row
                        double finalAdjustment = targetY - anchor.CollageY;
                        foreach (var slot in ScrollSlots)
                        {
                            slot.CollageY += finalAdjustment;
                            // Snap to the nearest integer row grid line
                            slot.CollageY = Math.Round(slot.CollageY / cellH) * cellH;
                        }

                        _isAligningScrollForCycleSwitch = false;
                        _pendingCycleModeSwitch = false;
                        _scrolledDistanceInCycle = 0.0;
                        SwitchToNextCycleMode();
                        return;
                    }
                }
            }
        }
    }
}
