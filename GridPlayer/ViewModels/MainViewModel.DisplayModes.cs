using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GridVids.ViewModels
{
    public partial class MainViewModel
    {
        private Avalonia.Threading.DispatcherTimer? _swapTimer;
        private bool _isShowingGrid1 = true;
        private bool _isRefreshing = false;

        public List<string> GetCurrentActiveVideoBatch()
        {
            var list = new List<string>();

            if (IsScrollEnabled && ScrollSlots.Count > 0)
            {
                double effectiveH = ContainerHeight > 100 ? ContainerHeight : 800;
                // Prioritize on-screen docked slots (e.g. slots within [0, effectiveH))
                list = ScrollSlots
                    .Where(s => s.CollageY >= -1.0 && s.CollageY < (effectiveH - 1.0))
                    .OrderBy(s => Math.Round(s.CollageY))
                    .ThenBy(s => s.CollageX)
                    .Select(s => s.CurrentVideoPath)
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();

                if (list.Count == 0)
                {
                    list = ScrollSlots
                        .Where(s => (s.CollageY + s.CollageHeight) > 0 && s.CollageY < effectiveH)
                        .OrderBy(s => s.CollageY)
                        .ThenBy(s => s.CollageX)
                        .Select(s => s.CurrentVideoPath)
                        .Where(p => !string.IsNullOrEmpty(p))
                        .ToList();
                }

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

        private void InitializeSwapTimer()
        {
            _swapTimer = new Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(Math.Max(0.1, SelectedDelay))
            };
            _swapTimer.Tick += SwapTimer_Tick;
            if (IsSwapEnabled) _swapTimer.Start();
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
    }
}
