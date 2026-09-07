using System;
using System.Linq;

namespace GridVids.ViewModels
{
    public partial class MainViewModel
    {
        private Avalonia.Threading.DispatcherTimer? _cycleModesTimer;
        private readonly string[] _availableCycleModes =
        {
            "Boomerang",
            "Grid",
            "Scrolling Wall",
            "Stackable"
        };
        private bool _pendingCycleModeSwitch = false;

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
            _isAligningScrollForCycleSwitch = false;
            _scrolledDistanceInCycle = 0.0;
            if (_cycleModesTimer == null)
            {
                InitializeCycleModesTimer();
            }
            // Keep currently selected display mode as the starting point and start timer for subsequent alpha transitions
            UpdateCycleModesTimerForCurrentMode();
        }

        private void StopCycleModes()
        {
            _pendingCycleModeSwitch = false;
            _isAligningScrollForCycleSwitch = false;
            _cycleModesTimer?.Stop();
        }

        public void SwitchToNextCycleMode()
        {
            if (!IsCycleModesEnabled) return;
            _pendingCycleModeSwitch = false;

            int currentIndex = Array.IndexOf(_availableCycleModes, SelectedDisplayMode);
            int nextIndex = (currentIndex < 0) ? 0 : (currentIndex + 1) % _availableCycleModes.Length;
            SelectedDisplayMode = _availableCycleModes[nextIndex];
        }

        private void CycleModesTimer_Tick(object? sender, EventArgs e)
        {
            if (!IsCycleModesEnabled || !IsVideoPlaying || string.IsNullOrWhiteSpace(VideoPath)) return;

            // If Stackable is active, let it complete its 4-quadrant stacking sequence
            if (SelectedDisplayMode == "Stackable")
            {
                _pendingCycleModeSwitch = true;
                return;
            }

            // If Scrolling Wall is active, delegate the mode switch to ScrollTimer_Tick so it can
            // guarantee at least 2 full rows scrolled AND dock precisely on integer row boundaries.
            if (SelectedDisplayMode == "Scrolling Wall")
            {
                _pendingCycleModeSwitch = true;
                return;
            }

            SwitchToNextCycleMode();
        }
    }
}
