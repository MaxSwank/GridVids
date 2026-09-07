using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace GridVids.ViewModels
{
    public partial class MainViewModel
    {
        private Avalonia.Threading.DispatcherTimer? _randomizeTimer;
        private Queue<int> _randomSlotQueue = new();
        private string? _currentSingleVidForMultiple;
        private HashSet<int> _slotsUpdatedInCycle = new();

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
    }
}
