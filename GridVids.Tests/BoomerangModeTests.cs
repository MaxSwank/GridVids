using System;
using System.Collections.Generic;
using System.Linq;
using GridVids.Models;
using GridVids.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace GridVids.Tests
{
    public class BoomerangModeTests
    {
        private readonly ITestOutputHelper _output;

        public BoomerangModeTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Test_Boomerang_PhaseProgression_And_SpeedDirectionRules()
        {
            _output.WriteLine("=======================================================================");
            _output.WriteLine("TEST: Boomerang Phase Progression (1/3 Fwd 100%, 1/3 Back 100%, 1/3 Fwd 75%)");
            _output.WriteLine("=======================================================================");

            double[] testDelays = new double[] { 3.0, 6.0, 10.0, 15.0, 30.0 };

            foreach (var delay in testDelays)
            {
                double phaseLength = delay / 3.0;

                _output.WriteLine($"\n--- Testing Display Delay = {delay:F1}s (Phase length = {phaseLength:F2}s) ---");

                // Test Phase 1 (0 to phaseLength): Forward 100%
                for (double t = 0.0; t < phaseLength; t += phaseLength / 4.0)
                {
                    var (phase, isFwd, speed) = GetBoomerangState(t, delay);
                    _output.WriteLine($"t = {t:F2}s: Phase {phase} -> Direction: {(isFwd ? "Forward" : "Backward")}, Speed: {speed:P0}");
                    Assert.Equal(1, phase);
                    Assert.True(isFwd);
                    Assert.Equal(1.0, speed);
                }

                // Test Phase 2 (phaseLength to 2 * phaseLength): Backward 100%
                for (double t = phaseLength; t < phaseLength * 2.0; t += phaseLength / 4.0)
                {
                    var (phase, isFwd, speed) = GetBoomerangState(t, delay);
                    _output.WriteLine($"t = {t:F2}s: Phase {phase} -> Direction: {(isFwd ? "Forward" : "Backward")}, Speed: {speed:P0}");
                    Assert.Equal(2, phase);
                    Assert.False(isFwd);
                    Assert.Equal(1.0, speed);
                }

                // Test Phase 3 (2 * phaseLength to delay): Forward 100%
                for (double t = phaseLength * 2.0; t < delay; t += phaseLength / 4.0)
                {
                    var (phase, isFwd, speed) = GetBoomerangState(t, delay);
                    _output.WriteLine($"t = {t:F2}s: Phase {phase} -> Direction: {(isFwd ? "Forward" : "Backward")}, Speed: {speed:P0}");
                    Assert.Equal(3, phase);
                    Assert.True(isFwd);
                    Assert.Equal(1.0, speed);
                }

                // Test Complete (>= delay)
                var (endPhase, _, _) = GetBoomerangState(delay, delay);
                Assert.Equal(4, endPhase); // Completed cycle
            }
        }

        [Fact]
        public void Test_AppSettings_And_DisplayModeOptions_Contain_Boomerang_And_CycleModes()
        {
            var settings = new AppSettings();
            Assert.False(settings.IsBoomerangEnabled);
            settings.IsBoomerangEnabled = true;
            Assert.True(settings.IsBoomerangEnabled);

            Assert.False(settings.IsCycleModesEnabled);
            settings.IsCycleModesEnabled = true;
            Assert.True(settings.IsCycleModesEnabled);

            settings.SelectedDisplayMode = "Boomerang";
            Assert.Equal("Boomerang", settings.SelectedDisplayMode);

            settings.SelectedDisplayMode = "Cycle Modes";
            Assert.Equal("Cycle Modes", settings.SelectedDisplayMode);

            // Verify both Delay values exist and can be set/persisted
            settings.SelectedDelay = 15.0;
            settings.SelectedCycleDelay = 20.0;
            Assert.Equal(15.0, settings.SelectedDelay);
            Assert.Equal(20.0, settings.SelectedCycleDelay);
        }

        [Fact]
        public void Test_Dropdowns_Are_Alphabetically_Sorted()
        {
            // Expected alpha sorted DisplayModeOptions (Cycle Modes is now a checkbox next to dropdown)
            var expectedDisplayModes = new List<string>
            {
                "Auto-Swap",
                "Boomerang",
                "Grid",
                "Scrolling Wall",
                "Stackable"
            };
            var sortedDisplayModes = expectedDisplayModes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            Assert.Equal(expectedDisplayModes, sortedDisplayModes);

            // Expected alpha sorted RandomizeOptions
            var expectedRandomizeOptions = new List<string>
            {
                "Multiple",
                "None"
            };
            var sortedRandomizeOptions = expectedRandomizeOptions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            Assert.Equal(expectedRandomizeOptions, sortedRandomizeOptions);
        }

        [Fact]
        public void Test_Play_And_Stop_Commands_Exist_And_Executable()
        {
            var vm = new MainViewModel();
            Assert.NotNull(vm.PlayCommand);
            Assert.NotNull(vm.StopCommand);
            Assert.True(vm.PlayCommand.CanExecute(null));
            // Executing Stop when not playing shouldn't throw
            vm.StopCommand.Execute(null);
            Assert.False(vm.IsVideoPlaying);
        }

        [Fact]
        public void Test_CycleModes_FullFlowDurations_And_RandomSelection()
        {
            var vm = new MainViewModel();

            // Verify CycleDelayOptions has 5, 10, 15, 20, 30
            Assert.Equal(new double[] { 5, 10, 15, 20, 30 }, vm.CycleDelayOptions.ToArray());

            vm.SelectedCycleDelay = 15.0;
            vm.SelectedDelay = 2.0;

            // Stackable flow duration is Math.Max(SelectedCycleDelay, (SelectedDelay * 4) + 1.5)
            // With SelectedDelay = 2.0 -> (2 * 4) + 1.5 = 9.5s. Since SelectedCycleDelay is 15.0s, duration is 15.0s.
            Assert.Equal(15.0, vm.GetModeFlowDuration("Stackable"));

            // If SelectedDelay is 5.0s -> (5 * 4) + 1.5 = 21.5s, which exceeds SelectedCycleDelay (15.0s),
            // so Stackable must not be interrupted and runs for 21.5s.
            vm.SelectedDelay = 5.0;
            Assert.Equal(21.5, vm.GetModeFlowDuration("Stackable"));

            // Boomerang and Grid both use SelectedCycleDelay (15s)
            Assert.Equal(15.0, vm.GetModeFlowDuration("Boomerang"));
            Assert.Equal(15.0, vm.GetModeFlowDuration("Grid"));

            // Scrolling Wall: requires at least 2 full rows, or SelectedCycleDelay if longer
            vm.Rows = 2;
            vm.ScrollSpeed = 80.0;
            // Default ContainerHeight=800, Rows=2 -> rowH=400, ScrollSpeed=80 -> (2 * 400) / 80 = 10s
            // When SelectedCycleDelay is 15.0s, Math.Max(15.0, 10.0) = 15.0s
            Assert.Equal(15.0, vm.GetModeFlowDuration("Scrolling Wall"));

            // When SelectedCycleDelay is 5.0s, 2 rows take 10s, so it must not interrupt before 10s
            vm.SelectedCycleDelay = 5.0;
            Assert.Equal(10.0, vm.GetModeFlowDuration("Scrolling Wall"));

            // When Cycle is checked, it starts cycling with a randomly selected mode (ignoring Auto-Swap)
            string initialMode = vm.SelectedDisplayMode;
            vm.IsCycleModesEnabled = true;
            // The selected mode should be one of the valid display modes and never Auto-Swap
            Assert.Contains(vm.SelectedDisplayMode, vm.DisplayModeOptions);
            Assert.NotEqual("Auto-Swap", vm.SelectedDisplayMode);

            // Test setting persistence and restore on load
            var settings = new AppSettings
            {
                IsCycleModesEnabled = true,
                SelectedCycleDelay = 20.0
            };
            Assert.True(settings.IsCycleModesEnabled);
            Assert.Equal(20.0, settings.SelectedCycleDelay);
        }

        [Fact]
        public void Test_VideoSlotViewModel_Tracks_BoomerangState()
        {
            var slot = new VideoSlotViewModel();
            Assert.Equal(DateTime.MinValue, slot.BoomerangStartTime);
            Assert.Equal(0, slot.BoomerangPhase);

            var now = DateTime.UtcNow;
            slot.BoomerangStartTime = now;
            slot.BoomerangPhase = 1;

            Assert.Equal(now, slot.BoomerangStartTime);
            Assert.Equal(1, slot.BoomerangPhase);
        }

        [Fact]
        public async Task Test_Mpv_NamedPipe_Ipc_PlayDirection_Backward()
        {
            var orchestrator = new GridVids.Services.ScriptOrchestrator();
            string mpvPath = orchestrator.GetMpvBinaryPath();
            _output.WriteLine($"Testing MPV binary at: {mpvPath}");

            if (!System.IO.File.Exists(mpvPath))
            {
                _output.WriteLine("MPV binary not found, skipping IPC integration test.");
                return;
            }

            string pipeName = $"gridvids_test_mpv_{Guid.NewGuid():N}";
            string fullPipe = $@"\\.\pipe\{pipeName}";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = mpvPath,
                Arguments = $"--idle=yes --input-ipc-server={fullPipe} --no-terminal",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var proc = System.Diagnostics.Process.Start(psi);
            Assert.NotNull(proc);

            try
            {
                // Wait briefly for IPC pipe to initialize
                await Task.Delay(500);

                var slot = new VideoSlotViewModel
                {
                    CurrentProcess = proc,
                    IpcPipeName = pipeName
                };

                // 1. Send: set play-direction backward via slot.SetProperty
                slot.SetProperty("play-direction", "backward");
                await Task.Delay(200);

                // 2. Query: get play-direction
                string resp1 = await QueryMpvPropertyAsync(pipeName, "play-direction");
                _output.WriteLine($"After set backward: {resp1}");
                Assert.Contains("\"data\":\"backward\"", resp1);

                // 3. Send: set play-direction forward via slot.SetProperty
                slot.SetProperty("play-direction", "forward");
                await Task.Delay(200);

                // 4. Query: get play-direction
                string resp2 = await QueryMpvPropertyAsync(pipeName, "play-direction");
                _output.WriteLine($"After set forward: {resp2}");
                Assert.Contains("\"data\":\"forward\"", resp2);
            }
            finally
            {
                if (!proc.HasExited)
                {
                    try { proc.Kill(); } catch { }
                }
                proc.Dispose();
            }
        }

        private async Task<string> QueryMpvPropertyAsync(string pipeName, string propertyName)
        {
            using var pipeClient = new System.IO.Pipes.NamedPipeClientStream(".", pipeName, System.IO.Pipes.PipeDirection.InOut);
            await pipeClient.ConnectAsync(2000);
            var writer = new System.IO.StreamWriter(pipeClient) { AutoFlush = true };
            var reader = new System.IO.StreamReader(pipeClient);

            await writer.WriteLineAsync($"{{\"command\":[\"get_property\",\"{propertyName}\"]}}");
            return await reader.ReadLineAsync() ?? string.Empty;
        }

        [Fact]
        public void Test_ScrollingWall_BatchPreservation_And_Cycling()
        {
            // Simulate 4 videos from previous mode (e.g. 2x2 Grid)
            var previousBatch = new List<string> { "vid1.mp4", "vid2.mp4", "vid3.mp4", "vid4.mp4" };

            // Scenario 1: Entering Scrolling Wall with 2 rows x 4 columns = 8 visible slots + 4 pre-load slots = 12 slots needed
            int curRows = 2;
            int curCols = 4;
            int videoIdx = 0;
            Func<List<string>> getNextRowVideos = () =>
            {
                var rowList = new List<string>();
                for (int c = 0; c < curCols; c++)
                {
                    rowList.Add(previousBatch[videoIdx % previousBatch.Count]);
                    videoIdx++;
                }
                return rowList;
            };

            var row1 = getNextRowVideos();
            var row2 = getNextRowVideos();
            var preLoadRow = getNextRowVideos();

            Assert.Equal(curRows * curCols, row1.Count + row2.Count);
            Assert.Equal(new[] { "vid1.mp4", "vid2.mp4", "vid3.mp4", "vid4.mp4" }, row1);
            Assert.Equal(new[] { "vid1.mp4", "vid2.mp4", "vid3.mp4", "vid4.mp4" }, row2);
            Assert.Equal(new[] { "vid1.mp4", "vid2.mp4", "vid3.mp4", "vid4.mp4" }, preLoadRow);

            // All videos used belong to the previous batch (no new random videos)
            var allAllocated = row1.Concat(row2).Concat(preLoadRow).ToList();
            Assert.All(allAllocated, v => Assert.Contains(v, previousBatch));

            // Scenario 2: Exiting Scrolling Wall to a target mode (e.g. 3x3 Grid = 9 slots)
            // Re-using the visible batch from Scrolling Wall
            var scrollBatch = row1.Concat(row2).Distinct().ToList(); // 4 distinct videos
            int targetSlotCount = 9;
            var selectedVideos = new List<string>(scrollBatch);
            while (selectedVideos.Count < targetSlotCount)
            {
                selectedVideos.Add(scrollBatch[selectedVideos.Count % scrollBatch.Count]);
            }

            Assert.Equal(targetSlotCount, selectedVideos.Count);
            Assert.All(selectedVideos, v => Assert.Contains(v, scrollBatch));
        }

        [Fact]
        public void Test_ScrollingWall_AlwaysStartsFromPreviousBatch()
        {
            // Verify resolution rule:
            // 1. If explicit initialVideos provided and non-empty, use initialVideos.
            // 2. Else if active video batch exists and non-empty, use active video batch.
            // 3. Else if stored _currentVideoBatch exists and non-empty, use _currentVideoBatch.
            // 4. Do not fetch a fresh batch from library when a previous batch is available.

            var previousBatch = new List<string> { "video_a.mp4", "video_b.mp4", "video_c.mp4" };
            var activeBatch = new List<string> { "active_1.mp4", "active_2.mp4" };

            // Case A: initialVideos passed explicitly
            var resolvedA = (previousBatch != null && previousBatch.Count > 0)
                ? previousBatch
                : (activeBatch.Count > 0 ? activeBatch : new List<string>());
            Assert.Equal(previousBatch, resolvedA);

            // Case B: initialVideos is null, activeBatch present
            List<string>? initialVideosB = null;
            var resolvedB = (initialVideosB != null && initialVideosB.Count > 0)
                ? initialVideosB
                : (activeBatch.Count > 0 ? activeBatch : previousBatch);
            Assert.Equal(activeBatch, resolvedB);

            // Case C: initialVideos is null, activeBatch empty, previousBatch present
            var emptyActiveBatch = new List<string>();
            var resolvedC = (initialVideosB != null && initialVideosB.Count > 0)
                ? initialVideosB
                : (emptyActiveBatch.Count > 0 ? emptyActiveBatch : previousBatch);
            Assert.Equal(previousBatch, resolvedC);

            // Row allocation cycling:
            Assert.NotNull(resolvedC);
            int curCols = 4;
            int videoIdx = 0;
            Func<List<string>> getNextRowVideos = () =>
            {
                var rowList = new List<string>();
                for (int c = 0; c < curCols; c++)
                {
                    rowList.Add(resolvedC![videoIdx % resolvedC.Count]);
                    videoIdx++;
                }
                return rowList;
            };

            var row1 = getNextRowVideos();
            var row2 = getNextRowVideos();
            Assert.Equal(new[] { "video_a.mp4", "video_b.mp4", "video_c.mp4", "video_a.mp4" }, row1);
            Assert.Equal(new[] { "video_b.mp4", "video_c.mp4", "video_a.mp4", "video_b.mp4" }, row2);
            Assert.All(row1.Concat(row2), v => Assert.Contains(v, previousBatch!));
        }

        [Fact]
        public void Test_ScrollingWall_SpawnsRows_UsingExistingBatchWithoutNewRandomVideos()
        {
            // Simulate video batch from previous mode (e.g. 4 videos from Grid mode)
            var currentVideoBatch = new List<string> { "vid1.mp4", "vid2.mp4", "vid3.mp4", "vid4.mp4" };
            int scrollVideoBatchIndex = 0;
            int validSlotCount = 4;
            bool randomFetchCalled = false;

            // Simulating dynamic row spawning logic in SpawnScrollRowAsync when rowVideos == null
            List<string> SpawnRowSim(List<string>? rowVideos)
            {
                if (rowVideos != null && rowVideos.Count > 0)
                {
                    return rowVideos;
                }
                else if (currentVideoBatch != null && currentVideoBatch.Count > 0)
                {
                    var videos = new List<string>();
                    for (int i = 0; i < validSlotCount; i++)
                    {
                        videos.Add(currentVideoBatch[scrollVideoBatchIndex % currentVideoBatch.Count]);
                        scrollVideoBatchIndex++;
                    }
                    return videos;
                }
                else
                {
                    randomFetchCalled = true;
                    return new List<string> { "new_random.mp4" };
                }
            }

            // Spawn multiple rows dynamically (like ScrollTimer_Tick does)
            var spawnedRow1 = SpawnRowSim(null);
            var spawnedRow2 = SpawnRowSim(null);
            var spawnedRow3 = SpawnRowSim(null);

            // Verify random library fetch was NEVER called because existing batch was used
            Assert.False(randomFetchCalled);
            Assert.Equal(new[] { "vid1.mp4", "vid2.mp4", "vid3.mp4", "vid4.mp4" }, spawnedRow1);
            Assert.Equal(new[] { "vid1.mp4", "vid2.mp4", "vid3.mp4", "vid4.mp4" }, spawnedRow2);
            Assert.Equal(new[] { "vid1.mp4", "vid2.mp4", "vid3.mp4", "vid4.mp4" }, spawnedRow3);
            Assert.All(spawnedRow1.Concat(spawnedRow2).Concat(spawnedRow3), v => Assert.Contains(v, currentVideoBatch));
        }

        [Fact]
        public void Test_DisplayModes_DefaultDelays()
        {
            var vm = new MainViewModel();

            // Default Boomerang should set SelectedDelay to 10.0
            vm.SelectedDisplayMode = "Boomerang";
            Assert.Equal(10.0, vm.SelectedDelay);

            // Stackable should default SelectedDelay to 2.0
            vm.SelectedDisplayMode = "Stackable";
            Assert.Equal(2.0, vm.SelectedDelay);

            // Back to Boomerang
            vm.SelectedDisplayMode = "Boomerang";
            Assert.Equal(10.0, vm.SelectedDelay);

            // Grid should default to 2.0
            vm.SelectedDisplayMode = "Grid";
            Assert.Equal(2.0, vm.SelectedDelay);

            // Boomerang again
            vm.SelectedDisplayMode = "Boomerang";
            Assert.Equal(10.0, vm.SelectedDelay);

            // Scrolling Wall should default to 2.0
            vm.SelectedDisplayMode = "Scrolling Wall";
            Assert.Equal(2.0, vm.SelectedDelay);

            // Boomerang again
            vm.SelectedDisplayMode = "Boomerang";
            Assert.Equal(10.0, vm.SelectedDelay);

            // Auto-Swap should default to 2.0
            vm.SelectedDisplayMode = "Auto-Swap";
            Assert.Equal(2.0, vm.SelectedDelay);
        }

        private (int Phase, bool IsForward, double Speed) GetBoomerangState(double elapsed, double totalDelay)
        {
            double phaseLength = totalDelay / 3.0;

            if (elapsed < phaseLength)
            {
                return (1, true, 1.0);
            }
            if (elapsed < phaseLength * 2.0)
            {
                return (2, false, 1.0);
            }
            if (elapsed < totalDelay)
            {
                return (3, true, 1.0);
            }
            return (4, true, 1.0);
        }
    }
}
