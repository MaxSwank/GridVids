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
    }
}
