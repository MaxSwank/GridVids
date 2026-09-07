using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GridVids.ViewModels
{
    public partial class MainViewModel
    {
        private Avalonia.Threading.DispatcherTimer? _boomerangTimer;

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
    }
}
