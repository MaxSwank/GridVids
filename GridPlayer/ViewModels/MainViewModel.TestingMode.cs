using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using GridVids.Interop;

namespace GridVids.ViewModels
{
    public enum TestingPhase
    {
        None,
        Grid,
        Scrolling,
        Stacking
    }

    public partial class MainViewModel
    {
        private DispatcherTimer? _testingPhaseTimer;
        private TestingPhase _currentTestingPhase = TestingPhase.None;
        public TestingPhase CurrentTestingPhase => _currentTestingPhase;

        private void InitializeTestingMode()
        {
            if (_testingPhaseTimer == null)
            {
                _testingPhaseTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(200)
                };
                _testingPhaseTimer.Tick += TestingPhaseTimer_Tick;
            }
        }

        public void StartTestingMode()
        {
            InitializeTestingMode();
            _currentTestingPhase = TestingPhase.Grid;

            // Start in Grid mode with current Rows and Columns
            IsScrollEnabled = false;
            IsStackableEnabled = false;
            IsSwapEnabled = false;
            IsBoomerangEnabled = false;
            IsGridVisible = true;
            _isShowingGrid1 = true;
            UpdateGrid();

            _testingPhaseTimer?.Stop();
            _testingPhaseTimer?.Start();

            if (IsVideoPlaying && !string.IsNullOrEmpty(VideoPath))
            {
                var existingBatch = GetCurrentActiveVideoBatch();
                _ = ExecutePlayback(existingBatch.Count > 0 ? existingBatch : null);
            }
        }

        public void StopTestingMode()
        {
            _testingPhaseTimer?.Stop();
            _currentTestingPhase = TestingPhase.None;
        }

        private async void TestingPhaseTimer_Tick(object? sender, EventArgs e)
        {
            if (SelectedDisplayMode != "Testing" || !IsVideoPlaying || string.IsNullOrEmpty(VideoPath))
            {
                _testingPhaseTimer?.Stop();
                return;
            }

            if (_currentTestingPhase == TestingPhase.Grid)
            {
                // Check if all slots have loaded and are playing
                bool allSlotsPlaying = VideoSlots.Count > 0 &&
                                       VideoSlots.All(s => s.CurrentProcess != null &&
                                                           !s.CurrentProcess.HasExited &&
                                                           !string.IsNullOrEmpty(s.CurrentVideoPath));

                if (allSlotsPlaying)
                {
                    _testingPhaseTimer?.Stop();
                    // Brief pause so playback is visibly rolling before scroll begins
                    await Task.Delay(800);

                    if (SelectedDisplayMode != "Testing" || !IsVideoPlaying) return;

                    await TransitionTestingGridToScrollingAsync();
                }
            }
        }

        private async Task TransitionTestingGridToScrollingAsync()
        {
            _currentTestingPhase = TestingPhase.Scrolling;
            var activeBatch = GetCurrentActiveVideoBatch();
            if (activeBatch.Count == 0 && _currentVideoBatch.Count > 0)
            {
                activeBatch = _currentVideoBatch.ToList();
            }

            _scrolledDistanceInCycle = 0.0;
            _isAligningScrollForCycleSwitch = false;

            IsScrollEnabled = true;

            await StartScrollAsync(activeBatch);
        }

        public async Task OnTestingScrollDocked()
        {
            if (SelectedDisplayMode != "Testing") return;

            await TransitionTestingScrollingToStackableAsync();
        }

        public async Task TransitionTestingScrollingToStackableAsync()
        {
            _currentTestingPhase = TestingPhase.Stacking;

            // Halting scroll velocity
            _scrollTimer?.Stop();
            CleanUpOffScreenScrollSlots();

            double effectiveH = ContainerHeight > 100 ? ContainerHeight : 800;
            var dockedSlots = ScrollSlots
                .Where(s => s.CollageY >= -2.0 && s.CollageY < (effectiveH - 0.5))
                .OrderBy(s => Math.Round(s.CollageY))
                .ThenBy(s => s.CollageX)
                .ToList();

            if (dockedSlots.Count == 0)
            {
                dockedSlots = ScrollSlots
                    .Where(s => (s.CollageY + s.CollageHeight) > 0 && s.CollageY < effectiveH)
                    .OrderBy(s => s.CollageY)
                    .ThenBy(s => s.CollageX)
                    .ToList();
            }

            var existingBatch = dockedSlots
                .Select(s => s.CurrentVideoPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            if (existingBatch.Count == 0 && _currentVideoBatch.Count > 0)
            {
                existingBatch = _currentVideoBatch.ToList();
            }

            // Ensure _scrollingVideoPool contains all scrolling videos and docked videos
            foreach (var p in existingBatch)
            {
                if (!_scrollingVideoPool.Contains(p))
                {
                    _scrollingVideoPool.Add(p);
                }
            }

            if (existingBatch.Count > 0)
            {
                // Put docked videos first so base slots match docked positions, then append the rest of the scrolling pool
                var combined = new List<string>(existingBatch);
                foreach (var p in _scrollingVideoPool)
                {
                    if (!combined.Contains(p))
                    {
                        combined.Add(p);
                    }
                }
                _currentVideoBatch = combined;
            }

            // Reparent running mpv processes from docked ScrollSlots into base VideoSlots to avoid blank frames
            int count = Math.Min(dockedSlots.Count, VideoSlots.Count);
            for (int i = 0; i < count; i++)
            {
                VideoSlots[i].CurrentVideoPath = dockedSlots[i].CurrentVideoPath;
            }

            _suppressAutoRun = true;
            try
            {
                int retries = 0;
                while (VideoSlots.Any(s => s.WindowHandle == IntPtr.Zero) && retries < 40)
                {
                    await Task.Delay(25);
                    retries++;
                }

                for (int i = 0; i < count; i++)
                {
                    var source = dockedSlots[i];
                    var target = VideoSlots[i];

                    if (source.WindowHandle != IntPtr.Zero && target.WindowHandle != IntPtr.Zero)
                    {
                        IntPtr mpvHwnd = Win32Interop.FindWindowEx(source.WindowHandle, IntPtr.Zero, null, null);
                        if (mpvHwnd != IntPtr.Zero)
                        {
                            Win32Interop.SetParent(mpvHwnd, target.WindowHandle);
                            Win32Interop.SetWindowPos(mpvHwnd, IntPtr.Zero, 0, 0, 0, 0,
                                Win32Interop.SWP_NOZORDER | Win32Interop.SWP_NOACTIVATE | Win32Interop.SWP_SHOWWINDOW);
                        }
                    }

                    var proc = source.CurrentProcess;
                    var video = source.CurrentVideoPath;
                    var ipc = source.IpcPipeName;

                    source.CurrentProcess = null;
                    target.UpdateProcess(proc, video, ipc);
                }

                IsGridVisible = true;
                StopScroll();
                IsScrollEnabled = false;
            }
            finally
            {
                _suppressAutoRun = false;
            }

            // Begin Stacking with current rows & columns
            IsStackableEnabled = true;
            ClearStackSlots();
            _stackQuadrantStep = 0;
            _isStackRunning = false;

            if (_stackTimer == null)
            {
                InitializeStackTimer();
            }
            else
            {
                _stackTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.1, SelectedDelay));
                _stackTimer.Stop();
                _stackTimer.Start();
            }

            // The moment 2 rows finish and docking completes, begin stacking quadrant 1 immediately
            _ = TriggerStackStepAsync();
        }

        public async Task OnTestingStackingCompleted()
        {
            if (SelectedDisplayMode != "Testing") return;

            await TransitionTestingStackableToGridAsync();
        }

        public async Task TransitionTestingStackableToGridAsync()
        {
            _stackTimer?.Stop();

            // Clear stack overlays immediately
            ClearStackSlots();
            IsStackableEnabled = false;

            // Reset scrolling video pool so next cycle gets fresh videos
            _scrollingVideoPool.Clear();

            // Reset the main Grid immediately
            _currentTestingPhase = TestingPhase.Grid;
            IsGridVisible = true;
            _isShowingGrid1 = true;
            UpdateGrid();

            // Refresh base grid videos immediately without pause
            await ExecutePlayback();

            // Immediately start scrolling after this
            await TransitionTestingGridToScrollingAsync();
        }
    }
}
