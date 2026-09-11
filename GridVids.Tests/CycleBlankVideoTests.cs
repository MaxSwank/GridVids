using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GridVids.Models;
using GridVids.Services;
using GridVids.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace GridVids.Tests
{
    public class CycleBlankVideoTests
    {
        private readonly ITestOutputHelper _output;

        public CycleBlankVideoTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private (string folder, List<string> files) CreateSampleVideoFolder(int count = 6)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "GridVids_TestVideos_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var fileList = new List<string>();

            for (int i = 1; i <= count; i++)
            {
                string filePath = Path.Combine(tempDir, $"sample_test_video_{i}.mp4");
                // Write minimal dummy file so File.Exists is true
                File.WriteAllBytes(filePath, new byte[] { 0x00, 0x01, 0x02, 0x03 });
                fileList.Add(filePath);
            }

            return (tempDir, fileList);
        }

        [Fact]
        public void Test_CycleModeTransitions_NoSlotsBlank_AcrossAllModes()
        {
            _output.WriteLine("=======================================================================");
            _output.WriteLine("TEST: Cycle Mode Transitions - Track and Ensure Zero Blank Videos");
            _output.WriteLine("=======================================================================");

            var (tempFolder, videoFiles) = CreateSampleVideoFolder(8);
            string tempSettings = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");

            try
            {
                var settingsService = new SettingsService(tempSettings);
                var vm = new MainViewModel(settingsService)
                {
                    VideoPath = tempFolder,
                    Rows = 2,
                    Columns = 2,
                    IsCycleModesEnabled = true,
                    IsVideoPlaying = true
                };

                // Seed initial batch as would happen upon loading the folder and starting playback
                var initialBatch = videoFiles.Take(4).ToList();
                for (int i = 0; i < vm.VideoSlots.Count; i++)
                {
                    vm.VideoSlots[i].CurrentVideoPath = initialBatch[i % initialBatch.Count];
                    // Provide simulated window handle
                    vm.VideoSlots[i].WindowHandle = new IntPtr(1000 + i);
                }

                int totalBlankDetected = 0;
                var blankLog = new List<string>();

                void CheckForBlankSlots(string stepName)
                {
                    // Check base VideoSlots
                    for (int i = 0; i < vm.VideoSlots.Count; i++)
                    {
                        var slot = vm.VideoSlots[i];
                        if (slot.IsVisible && string.IsNullOrWhiteSpace(slot.CurrentVideoPath))
                        {
                            totalBlankDetected++;
                            string msg = $"[{stepName}] Base VideoSlot #{i} is visible but blank (CurrentVideoPath is empty)!";
                            blankLog.Add(msg);
                            _output.WriteLine("ERROR: " + msg);
                        }
                    }

                    // Check StackSlots if stackable is enabled
                    if (vm.IsStackableEnabled)
                    {
                        for (int i = 0; i < vm.StackSlots.Count; i++)
                        {
                            var sSlot = vm.StackSlots[i];
                            if (sSlot.IsCollageVisible && string.IsNullOrWhiteSpace(sSlot.CurrentVideoPath))
                            {
                                totalBlankDetected++;
                                string msg = $"[{stepName}] Visible StackSlot #{i} is visible but blank!";
                                blankLog.Add(msg);
                                _output.WriteLine("ERROR: " + msg);
                            }
                        }
                    }

                    // Check ScrollSlots if scrolling is enabled
                    if (vm.IsScrollEnabled)
                    {
                        for (int i = 0; i < vm.ScrollSlots.Count; i++)
                        {
                            var scSlot = vm.ScrollSlots[i];
                            if (scSlot.IsCollageVisible && string.IsNullOrWhiteSpace(scSlot.CurrentVideoPath))
                            {
                                totalBlankDetected++;
                                string msg = $"[{stepName}] Visible ScrollSlot #{i} is visible but blank!";
                                blankLog.Add(msg);
                                _output.WriteLine("ERROR: " + msg);
                            }
                        }
                    }
                }

                // Initial State: Grid
                Assert.Equal("Grid", vm.SelectedDisplayMode);
                CheckForBlankSlots("Step 0: Initial Grid");
                _output.WriteLine($"Step 0 (Grid) - VideoSlots: {vm.VideoSlots.Count}, All with assigned videos: {vm.VideoSlots.All(s => !string.IsNullOrEmpty(s.CurrentVideoPath))}");

                // 1. Transition: Grid -> Stackable
                vm.SwitchToNextCycleMode();
                Assert.Equal("Stackable", vm.SelectedDisplayMode);
                Assert.True(vm.IsStackableEnabled);
                CheckForBlankSlots("Step 1: Grid -> Stackable");
                _output.WriteLine($"Step 1 (Stackable) - VideoSlots: {vm.VideoSlots.Count}, All non-blank: {vm.VideoSlots.All(s => !string.IsNullOrEmpty(s.CurrentVideoPath))}");

                // 2. Transition: Stackable -> Boomerang
                vm.SwitchToNextCycleMode();
                Assert.Equal("Boomerang", vm.SelectedDisplayMode);
                Assert.True(vm.IsBoomerangEnabled);
                CheckForBlankSlots("Step 2: Stackable -> Boomerang");
                _output.WriteLine($"Step 2 (Boomerang) - VideoSlots: {vm.VideoSlots.Count}, All non-blank: {vm.VideoSlots.All(s => !string.IsNullOrEmpty(s.CurrentVideoPath))}");

                // 3. Transition: Boomerang -> Scrolling Wall
                vm.SwitchToNextCycleMode();
                Assert.Equal("Scrolling Wall", vm.SelectedDisplayMode);
                Assert.True(vm.IsScrollEnabled);
                CheckForBlankSlots("Step 3: Boomerang -> Scrolling Wall");
                _output.WriteLine($"Step 3 (Scrolling Wall) - ScrollSlots: {vm.ScrollSlots.Count}, Active Batch: {vm.GetCurrentActiveVideoBatch().Count}");

                // 4. Transition: Scrolling Wall -> Grid
                vm.SwitchToNextCycleMode();
                Assert.Equal("Grid", vm.SelectedDisplayMode);
                CheckForBlankSlots("Step 4: Scrolling Wall -> Grid");
                _output.WriteLine($"Step 4 (Scrolling Wall -> Grid) - VideoSlots: {vm.VideoSlots.Count}, All non-blank: {vm.VideoSlots.All(s => !string.IsNullOrEmpty(s.CurrentVideoPath))}");

                // 5. Full Second Cycle: Grid -> Stackable -> Boomerang -> Scrolling Wall -> Grid
                vm.SwitchToNextCycleMode(); // Stackable
                CheckForBlankSlots("Step 5: Grid -> Stackable (2nd cycle)");
                vm.SwitchToNextCycleMode(); // Boomerang
                CheckForBlankSlots("Step 6: Stackable -> Boomerang (2nd cycle)");
                vm.SwitchToNextCycleMode(); // Scrolling Wall
                CheckForBlankSlots("Step 7: Boomerang -> Scrolling Wall (2nd cycle)");
                vm.SwitchToNextCycleMode(); // Grid
                CheckForBlankSlots("Step 8: Scrolling Wall -> Grid (2nd cycle)");

                _output.WriteLine($"\n--- Test Complete ---");
                _output.WriteLine($"Total Blank Videos Encountered: {totalBlankDetected}");

                if (blankLog.Count > 0)
                {
                    _output.WriteLine("Blank Occurrences:");
                    foreach (var log in blankLog) _output.WriteLine("  " + log);
                }

                // Assert zero blank videos ever displayed during cycle transitions
                Assert.Equal(0, totalBlankDetected);
            }
            finally
            {
                if (Directory.Exists(tempFolder))
                {
                    try { Directory.Delete(tempFolder, true); } catch { }
                }
                if (File.Exists(tempSettings))
                {
                    try { File.Delete(tempSettings); } catch { }
                }
            }
        }

        [Fact]
        public void Test_EnsureSlotCount_AssignsActiveVideos_WhenExpandingGrid()
        {
            var tempSettings = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var vm = new MainViewModel(new SettingsService(tempSettings))
                {
                    IsVideoPlaying = true
                };

                // Seed active batch
                var activeBatch = new List<string> { "v1.mp4", "v2.mp4", "v3.mp4", "v4.mp4" };
                for (int i = 0; i < vm.VideoSlots.Count; i++)
                {
                    vm.VideoSlots[i].CurrentVideoPath = activeBatch[i % activeBatch.Count];
                }

                // Expand grid from 2x2 (4 slots) to 3x3 (9 slots)
                vm.Rows = 3;
                vm.Columns = 3;

                Assert.Equal(9, vm.VideoSlots.Count);

                int blankCount = vm.VideoSlots.Count(s => string.IsNullOrEmpty(s.CurrentVideoPath));
                _output.WriteLine($"Expanding Grid to 9 slots: blank slots = {blankCount}");

                // No slot should be left blank
                Assert.Equal(0, blankCount);
            }
            finally
            {
                if (File.Exists(tempSettings))
                {
                    try { File.Delete(tempSettings); } catch { }
                }
            }
        }

        [Fact]
        public async Task Test_PlaybackService_PlayAsync_PadsVideos_NeverLeavesSlotsBlank()
        {
            var playbackService = new PlaybackService();
            var slots = new List<VideoSlotViewModel>
            {
                new VideoSlotViewModel { WindowHandle = new IntPtr(101) },
                new VideoSlotViewModel { WindowHandle = new IntPtr(102) },
                new VideoSlotViewModel { WindowHandle = new IntPtr(103) },
                new VideoSlotViewModel { WindowHandle = new IntPtr(104) }
            };

            // Only 2 videos provided for 4 slots
            var videoFiles = new List<string> { "video_a.mp4", "video_b.mp4" };

            // When PlayAsync is invoked with fewer videos than slots, it should cyclically pad so no slot has empty CurrentVideoPath
            await playbackService.PlayAsync(slots, videoFiles);

            foreach (var slot in slots)
            {
                Assert.False(string.IsNullOrWhiteSpace(slot.CurrentVideoPath), "Slot CurrentVideoPath should not be blank after PlayAsync.");
            }

            Assert.Equal("video_a.mp4", slots[0].CurrentVideoPath);
            Assert.Equal("video_b.mp4", slots[1].CurrentVideoPath);
            Assert.Equal("video_a.mp4", slots[2].CurrentVideoPath);
            Assert.Equal("video_b.mp4", slots[3].CurrentVideoPath);
        }

        [Fact]
        public void Test_StackableMode_SecondCycleReentry_ResetsStateAndContinuesStacking()
        {
            _output.WriteLine("=======================================================================");
            _output.WriteLine("TEST: Stackable Mode Re-entry Lifecycle across Multiple Cycles");
            _output.WriteLine("=======================================================================");

            var (tempFolder, videoFiles) = CreateSampleVideoFolder(8);
            string tempSettings = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");

            try
            {
                var settingsService = new SettingsService(tempSettings);
                var vm = new MainViewModel(settingsService)
                {
                    VideoPath = tempFolder,
                    Rows = 2,
                    Columns = 2,
                    IsCycleModesEnabled = true,
                    IsVideoPlaying = true
                };

                // Populate initial videos
                for (int i = 0; i < vm.VideoSlots.Count && i < videoFiles.Count; i++)
                {
                    vm.VideoSlots[i].CurrentVideoPath = videoFiles[i];
                }

                // Cycle 1: Grid -> Stackable
                _output.WriteLine("Cycle 1: Transitioning to Stackable...");
                vm.SelectedDisplayMode = "Stackable";
                Assert.True(vm.IsStackableEnabled);
                Assert.Empty(vm.StackSlots);

                // Add mock stack slot to simulate stacking in cycle 1
                vm.StackSlots.Add(new VideoSlotViewModel
                {
                    WindowHandle = new IntPtr(999),
                    CurrentVideoPath = videoFiles[0],
                    IsCollageVisible = true
                });
                Assert.Single(vm.StackSlots);

                // Cycle 1: Stackable -> Boomerang
                _output.WriteLine("Cycle 1: Transitioning to Boomerang...");
                vm.SelectedDisplayMode = "Boomerang";
                Assert.True(vm.IsBoomerangEnabled);

                // Cycle 1: Boomerang -> Scrolling Wall
                _output.WriteLine("Cycle 1: Transitioning to Scrolling Wall...");
                vm.SelectedDisplayMode = "Scrolling Wall";
                Assert.True(vm.IsScrollEnabled);

                // Cycle 2: Scrolling Wall -> Grid
                _output.WriteLine("Cycle 2: Transitioning to Grid...");
                vm.SelectedDisplayMode = "Grid";
                Assert.False(vm.IsStackableEnabled);

                // Cycle 2: Grid -> Stackable (RE-ENTRY)
                _output.WriteLine("Cycle 2: Transitioning to Stackable (Re-entry)...");
                vm.SelectedDisplayMode = "Stackable";

                // Verify Stackable state is cleanly reset
                Assert.True(vm.IsStackableEnabled, "IsStackableEnabled should be true on Cycle 2.");
                Assert.Empty(vm.StackSlots); // Stale slots from cycle 1 must be cleared!
                Assert.Equal(2, vm.Rows);
                Assert.Equal(2, vm.Columns);
            }
            finally
            {
                if (Directory.Exists(tempFolder))
                {
                    try { Directory.Delete(tempFolder, true); } catch { }
                }
                if (File.Exists(tempSettings))
                {
                    try { File.Delete(tempSettings); } catch { }
                }
            }
        }

        [Fact]
        public void Test_ScrollingWall_To_Grid_ReusesBatch_WithoutRecreating()
        {
            _output.WriteLine("=======================================================================");
            _output.WriteLine("TEST: Scrolling Wall to Grid Reuses Batch Without Recreation");
            _output.WriteLine("=======================================================================");

            var (tempFolder, videoFiles) = CreateSampleVideoFolder(10);
            string tempSettings = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");

            try
            {
                var settingsService = new SettingsService(tempSettings);
                var vm = new MainViewModel(settingsService)
                {
                    VideoPath = tempFolder,
                    Rows = 2,
                    Columns = 2,
                    IsCycleModesEnabled = true,
                    IsVideoPlaying = true
                };

                // Switch to Scrolling Wall
                vm.SelectedDisplayMode = "Scrolling Wall";
                Assert.True(vm.IsScrollEnabled);

                // Simulate docked ScrollSlots with a distinct batch of videos
                var dockedVideos = new List<string> { videoFiles[2], videoFiles[3], videoFiles[4], videoFiles[5] };
                vm.ScrollSlots.Clear();
                for (int i = 0; i < dockedVideos.Count; i++)
                {
                    vm.ScrollSlots.Add(new VideoSlotViewModel
                    {
                        CurrentVideoPath = dockedVideos[i],
                        CollageY = (i / 2) * 50.0,
                        CollageX = (i % 2) * 50.0,
                        IsCollageVisible = true
                    });
                }

                var extractedBatch = vm.GetCurrentActiveVideoBatch();
                _output.WriteLine($"Extracted batch from docked ScrollSlots: {string.Join(", ", extractedBatch)}");
                Assert.Equal(4, extractedBatch.Count);
                for (int i = 0; i < 4; i++)
                {
                    Assert.Equal(dockedVideos[i], extractedBatch[i]);
                }

                // Transition to Grid mode (simulating Cycle end of Scrolling Wall)
                vm.SelectedDisplayMode = "Grid";

                // Ensure Grid mode adopts the exact batch
                var activeGridBatch = vm.GetCurrentActiveVideoBatch();
                _output.WriteLine($"Active Grid batch after transition: {string.Join(", ", activeGridBatch)}");
                Assert.Equal(4, activeGridBatch.Count);
                for (int i = 0; i < 4; i++)
                {
                    Assert.Equal(dockedVideos[i], activeGridBatch[i]);
                }
            }
            finally
            {
                if (Directory.Exists(tempFolder))
                {
                    try { Directory.Delete(tempFolder, true); } catch { }
                }
                if (File.Exists(tempSettings))
                {
                    try { File.Delete(tempSettings); } catch { }
                }
            }
        }

        [Fact]
        public void Test_Stackable_With_RandomSwap_Enabled_RunsTimer()
        {
            _output.WriteLine("=======================================================================");
            _output.WriteLine("TEST: Stackable Display Mode Allows Random Swap Timer");
            _output.WriteLine("=======================================================================");

            string tempSettings = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var settingsService = new SettingsService(tempSettings);
                var vm = new MainViewModel(settingsService);

                // Set mode to Stackable
                vm.SelectedDisplayMode = "Stackable";
                Assert.True(vm.IsStackableEnabled);

                // Enable Random Swap
                vm.IsRandomSwapEnabled = true;
                Assert.NotEqual("None", vm.SelectedRandomize);
                Assert.True(vm.IsRandomizeTimerRunning);

                // Changing SelectedRandomize to None stops the timer
                vm.SelectedRandomize = "None";
                Assert.False(vm.IsRandomizeTimerRunning);

                // Changing SelectedRandomize back to Single or Multiple restarts it
                vm.SelectedRandomize = "Multiple";
                Assert.True(vm.IsRandomizeTimerRunning);
            }
            finally
            {
                if (File.Exists(tempSettings))
                {
                    try { File.Delete(tempSettings); } catch { }
                }
            }
        }
    }
}

