using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace GridVids.ViewModels
{
    public partial class MainViewModel
    {
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

            if (IsStackableEnabled && !IsSwapEnabled)
            {
                _stackQuadrantStep = 0;
                _isStackRunning = false;
                _stackTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.1, SelectedDelay));
                _stackTimer.Stop();
                _stackTimer.Start();
            }
            else
            {
                _stackTimer.Stop();
                ClearStackSlots();
            }
        }

        private void ClearStackSlots()
        {
            _isStackRunning = false;
            if (StackSlots.Count > 0)
            {
                var slotsToStop = StackSlots.ToList();
                StackSlots.Clear();
                _playbackService.Stop(slotsToStop);
            }
        }

        private async void StackTimer_Tick(object? sender, EventArgs e)
        {
            await TriggerStackStepAsync();
        }

        public async Task TriggerStackStepAsync()
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

                    // In Testing mode, as soon as the 4th quadrant (quadIndex 3) has stacked, immediately start over in Grid mode without waiting for another timer tick
                    if (SelectedDisplayMode == "Testing" && quadIndex == 3)
                    {
                        _stackQuadrantStep = 0;
                        await OnTestingStackingCompleted();
                        return;
                    }
                }
                else
                {
                    // The 4 stack videos have completed.
                    _stackQuadrantStep = 0;

                    // If a cycle mode switch was delayed waiting for Stackable to finish its full flow, switch now!
                    // Do NOT refresh base grid or clear stack slots here; let ApplyModeTransition seamlessly
                    // pass the active StackSlots batch to the incoming mode while preserving the visual overlay.
                    if (_pendingCycleModeSwitch && IsCycleModesEnabled)
                    {
                        _pendingCycleModeSwitch = false;
                        SwitchToNextCycleMode();
                        return;
                    }

                    if (SelectedDisplayMode == "Testing")
                    {
                        await OnTestingStackingCompleted();
                        return;
                    }

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

            // Wait for window handles to be created if running in UI context with real host
            bool isHeadless = Avalonia.Application.Current == null || !VideoSlots.Any(s => s.WindowHandle != IntPtr.Zero);
            if (!isHeadless)
            {
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 2500 && newSlots.Any(s => s.WindowHandle == IntPtr.Zero))
                {
                    await Task.Delay(50);
                }
            }

            // If we are running in a UI context, filter to slots with valid WindowHandles.
            // If running headlessly (such as unit tests where no NativeControlHost attaches a handle), retain the slots.
            List<VideoSlotViewModel> validSlots = newSlots.Where(s => s.WindowHandle != IntPtr.Zero).ToList();
            if (validSlots.Count == 0 && (isHeadless || newSlots.Count > 0))
            {
                validSlots = newSlots.ToList();
            }
            else
            {
                var invalidSlots = newSlots.Where(s => s.WindowHandle == IntPtr.Zero).ToList();
                foreach (var inv in invalidSlots)
                {
                    StackSlots.Remove(inv);
                }
            }

            if (validSlots.Count > 0 && IsStackableEnabled)
            {
                var excluded = VideoSlots.Concat(StackSlots)
                    .Select(s => s.CurrentVideoPath)
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Cast<string>()
                    .ToHashSet();

                List<string> vids;
                if (IsSingleVidEnabled)
                {
                    // In Single Vid mode, stackable video must match the background video
                    string? bgVideo = VideoSlots.Select(s => s.CurrentVideoPath).FirstOrDefault(p => !string.IsNullOrEmpty(p))
                                      ?? (_currentVideoBatch != null && _currentVideoBatch.Count > 0 ? _currentVideoBatch[0] : null);

                    if (!string.IsNullOrEmpty(bgVideo))
                    {
                        vids = Enumerable.Repeat(bgVideo, validSlots.Count).ToList();
                    }
                    else
                    {
                        vids = await GetRecycledOrFreshVideosAsync(validSlots.Count, null, preferExclusion: false);
                    }
                }
                else
                {
                    // If we have an existing pool of videos from scrolling (e.g. in Testing mode or cycle transition),
                    // stack directly on the previous videos from scrolling instead of fetching a new batch from library.
                    var scrollPool = (_scrollingVideoPool != null && _scrollingVideoPool.Count > 0)
                        ? _scrollingVideoPool
                        : null;

                    if (scrollPool != null && scrollPool.Count > 0)
                    {
                        var pool = scrollPool.Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
                        var available = pool.Where(p => !excluded.Contains(p)).ToList();
                        if (available.Count == 0)
                        {
                            available = pool.ToList();
                        }

                        var rnd = new Random();
                        vids = available.OrderBy(_ => rnd.Next()).Take(validSlots.Count).ToList();
                        while (vids.Count < validSlots.Count && pool.Count > 0)
                        {
                            vids.Add(pool[rnd.Next(pool.Count)]);
                        }
                    }
                    else
                    {
                        // Stackable video is random: exclude active background and existing stack slots
                        vids = await _videoLibraryService.GetRandomVideosAsync(validSlots.Count, excluded, isSingleVidMode: false);
                        if (vids.Count < validSlots.Count)
                        {
                            // Fallback excluding videos already picked in this batch
                            int remaining = validSlots.Count - vids.Count;
                            var batchExcluded = new HashSet<string>(vids);
                            var fallback = await _videoLibraryService.GetRandomVideosAsync(remaining, batchExcluded, isSingleVidMode: false);
                            vids.AddRange(fallback);

                            if (vids.Count < validSlots.Count)
                            {
                                var unconstrained = await _videoLibraryService.GetRandomVideosAsync(validSlots.Count - vids.Count, null, isSingleVidMode: false);
                                vids.AddRange(unconstrained);
                            }
                        }
                    }
                }

                if (vids.Count > 0)
                {
                    for (int i = 0; i < validSlots.Count && i < vids.Count; i++)
                    {
                        validSlots[i].CurrentVideoPath = vids[i];
                    }
                    await _playbackService.PlayAsync(validSlots, vids);

                    // Buffer Delay: Wait for MPV to initialize, decode, and render frames before displaying (skip in headless mode)
                    if (!isHeadless)
                    {
                        await Task.Delay(1000);
                    }

                    if (!IsStackableEnabled) return;

                    // Reveal all quadrant slots on screen simultaneously, already playing
                    foreach (var s in validSlots)
                    {
                        s.IsCollageVisible = true;
                    }
                }
                else
                {
                    foreach (var s in validSlots)
                    {
                        StackSlots.Remove(s);
                    }
                }
            }
            else if (validSlots.Count == 0)
            {
                // No slots obtained window handles; remove all newSlots
                foreach (var s in newSlots)
                {
                    StackSlots.Remove(s);
                }
            }
        }
    }
}
