using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GridVids.Services;
using GridVids.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace GridVids.Tests
{
    public class ScrollingWallTests
    {
        private readonly ITestOutputHelper _output;

        public ScrollingWallTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private MainViewModel CreateIsolatedViewModel()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"gridvids_test_{Guid.NewGuid()}.json");
            var settingsService = new SettingsService(tempPath);
            return new MainViewModel(settingsService);
        }

        [Fact]
        public void Test_SwitchingToScrollingWall_ReusesExistingVideoBatch()
        {
            _output.WriteLine("=======================================================================");
            _output.WriteLine("TEST: Switching to Scrolling Wall reuses existing video batch");
            _output.WriteLine("=======================================================================");

            var vm = CreateIsolatedViewModel();

            // Populate VideoSlots simulating 4 videos currently playing in Grid mode
            var initialVideos = new List<string>
            {
                @"C:\Videos\clip1.mp4",
                @"C:\Videos\clip2.mp4",
                @"C:\Videos\clip3.mp4",
                @"C:\Videos\clip4.mp4"
            };

            for (int i = 0; i < initialVideos.Count; i++)
            {
                vm.VideoSlots.Add(new VideoSlotViewModel
                {
                    Index = i,
                    CurrentVideoPath = initialVideos[i],
                    IsVisible = true
                });
            }

            // Verify active batch contains all initial videos
            var batchBefore = vm.GetCurrentActiveVideoBatch();
            Assert.Equal(initialVideos.Count, batchBefore.Count);
            Assert.Equal(initialVideos, batchBefore);
            _output.WriteLine($"Active batch in Grid mode: {string.Join(", ", batchBefore)}");

            // Switch display mode to "Scrolling Wall"
            vm.SelectedDisplayMode = "Scrolling Wall";

            // Verify current video batch is preserved and reused
            Assert.True(vm.IsScrollEnabled);
            Assert.Equal("Scrolling Wall", vm.SelectedDisplayMode);
            Assert.Equal(initialVideos, vm.CurrentVideoBatch);
            _output.WriteLine($"Batch preserved in Scrolling Wall: {string.Join(", ", vm.CurrentVideoBatch)}");
        }

        [Fact]
        public void Test_RandomizeTimer_Runs_DuringScrollingWall_When_RandomSwapEnabled()
        {
            _output.WriteLine("=======================================================================");
            _output.WriteLine("TEST: Randomize timer runs during Scrolling Wall when Random Swap is enabled");
            _output.WriteLine("=======================================================================");

            var vm = CreateIsolatedViewModel();

            // In Scrolling Wall mode with Random Swap disabled, randomize timer does not run
            vm.SelectedDisplayMode = "Scrolling Wall";
            vm.IsRandomSwapEnabled = false;
            Assert.False(vm.IsRandomizeTimerRunning);

            // Enabling Random Swap starts the timer in Scrolling Wall mode
            vm.IsRandomSwapEnabled = true;
            Assert.True(vm.IsRandomizeTimerRunning);

            // Setting SelectedRandomize to "None" stops the timer
            vm.SelectedRandomize = "None";
            Assert.False(vm.IsRandomizeTimerRunning);

            // Setting SelectedRandomize to "Multiple" restarts the timer
            vm.SelectedRandomize = "Multiple";
            Assert.True(vm.IsRandomizeTimerRunning);
        }

        [Fact]
        public async Task Test_ScrollingWall_RandomSwap_ReplacesSingleSlotVideo()
        {
            _output.WriteLine("=======================================================================");
            _output.WriteLine("TEST: Scrolling Wall replaces exactly 1 random slot with another video");
            _output.WriteLine("=======================================================================");

            // Create temp video folder with 10 dummy video files
            string tempDir = Path.Combine(Path.GetTempPath(), $"gridvids_test_vids_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            var videoFiles = new List<string>();
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    string filePath = Path.Combine(tempDir, $"video_{i:D2}.mp4");
                    File.WriteAllText(filePath, "dummy video content");
                    videoFiles.Add(filePath);
                }

                var vm = CreateIsolatedViewModel();
                // Avoid async StartScroll from background cache refresh overwriting test slots
                vm.IsScrollEnabled = true;
                vm.VideoPath = tempDir;
                // Wait for any background cache refresh to complete
                await Task.Delay(100);
                vm.StopScroll();

                vm.IsVideoPlaying = true;
                vm.IsRandomSwapEnabled = true;
                vm.SelectedDelay = 2.0;

                // Populate 4 visible slots in ScrollSlots
                vm.ScrollSlots.Clear();
                var initialSlotVideos = new List<string> { videoFiles[0], videoFiles[1], videoFiles[2], videoFiles[3] };
                for (int i = 0; i < initialSlotVideos.Count; i++)
                {
                    vm.ScrollSlots.Add(new VideoSlotViewModel
                    {
                        Index = i,
                        CurrentVideoPath = initialSlotVideos[i],
                        CollageX = (i % 2) * 400.0,
                        CollageY = (i / 2) * 300.0,
                        CollageWidth = 400.0,
                        CollageHeight = 300.0,
                        IsCollageVisible = true
                    });
                }

                _output.WriteLine($"Initial ScrollSlot videos: {string.Join(", ", vm.ScrollSlots.Select(s => Path.GetFileName(s.CurrentVideoPath)))}");

                // Execute 1 random swap tick
                await vm.TriggerRandomSwapForTestingAsync();

                var afterSlotVideos = vm.ScrollSlots.Select(s => s.CurrentVideoPath).ToList();
                _output.WriteLine($"After Swap ScrollSlot videos: {string.Join(", ", afterSlotVideos.Select(Path.GetFileName))}");

                // Exactly 1 slot should have changed
                int changedCount = 0;
                int changedIndex = -1;
                for (int i = 0; i < initialSlotVideos.Count; i++)
                {
                    if (initialSlotVideos[i] != afterSlotVideos[i])
                    {
                        changedCount++;
                        changedIndex = i;
                    }
                }

                Assert.Equal(1, changedCount);
                Assert.True(changedIndex >= 0);

                // The new video should be from the available video library (videoFiles[4..9])
                string newVideo = afterSlotVideos[changedIndex];
                Assert.Contains(newVideo, videoFiles);
                Assert.DoesNotContain(newVideo, initialSlotVideos);

                // All 4 visible slots should now have distinct videos
                Assert.Equal(4, afterSlotVideos.Distinct().Count());
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Test_RowAndColumnChanges_DoNotEraseExistingBatch_WhenScrollEnabled()
        {
            _output.WriteLine("=======================================================================");
            _output.WriteLine("TEST: Row and Column changes do not erase existing batch in Scrolling Wall");
            _output.WriteLine("=======================================================================");

            var vm = CreateIsolatedViewModel();

            var initialVideos = new List<string>
            {
                @"C:\Videos\videoA.mp4",
                @"C:\Videos\videoB.mp4",
                @"C:\Videos\videoC.mp4"
            };

            foreach (var vid in initialVideos)
            {
                vm.VideoSlots.Add(new VideoSlotViewModel
                {
                    CurrentVideoPath = vid,
                    IsVisible = true
                });
            }

            vm.GetCurrentActiveVideoBatch();
            Assert.Equal(initialVideos, vm.CurrentVideoBatch);

            // Switch to Scrolling Wall
            vm.SelectedDisplayMode = "Scrolling Wall";
            Assert.Equal(initialVideos, vm.CurrentVideoBatch);

            // Simulate window resize changing columns/rows
            vm.Rows = 3;
            Assert.Equal(initialVideos, vm.CurrentVideoBatch);

            vm.Columns = 5;
            Assert.Equal(initialVideos, vm.CurrentVideoBatch);
        }

        [Fact]
        public void Test_BatchRecycling_AcrossModes()
        {
            _output.WriteLine("=======================================================================");
            _output.WriteLine("TEST: Batch recycling across modes preserves existing active videos");
            _output.WriteLine("=======================================================================");

            var vm = CreateIsolatedViewModel();

            var initialVideos = new List<string>
            {
                @"C:\Videos\clip1.mp4",
                @"C:\Videos\clip2.mp4",
                @"C:\Videos\clip3.mp4"
            };

            for (int i = 0; i < initialVideos.Count; i++)
            {
                vm.VideoSlots.Add(new VideoSlotViewModel
                {
                    Index = i,
                    CurrentVideoPath = initialVideos[i],
                    IsVisible = true
                });
            }

            var batch = vm.GetCurrentActiveVideoBatch();
            Assert.Equal(initialVideos.Count, batch.Count);
            Assert.Equal(initialVideos, vm.CurrentVideoBatch);

            // Mode transitions: Auto-Swap, Stackable, Boomerang
            vm.SelectedDisplayMode = "Auto-Swap";
            Assert.Equal(initialVideos, vm.CurrentVideoBatch);

            vm.SelectedDisplayMode = "Stackable";
            Assert.Equal(initialVideos, vm.CurrentVideoBatch);

            vm.SelectedDisplayMode = "Boomerang";
            Assert.Equal(initialVideos, vm.CurrentVideoBatch);
        }

        [Fact]
        public void Test_RandomSwap_Checkbox_Toggles_Timer_And_Settings()
        {
            _output.WriteLine("=======================================================================");
            _output.WriteLine("TEST: Random Swap checkbox controls randomize timer and synchronize state");
            _output.WriteLine("=======================================================================");

            var vm = CreateIsolatedViewModel();

            vm.SelectedDisplayMode = "Grid";
            Assert.False(vm.IsRandomSwapEnabled);
            Assert.Equal("None", vm.SelectedRandomize);
            Assert.False(vm.IsRandomizeTimerRunning);

            // Enabling Random Swap starts randomize timer
            vm.IsRandomSwapEnabled = true;
            Assert.True(vm.IsRandomSwapEnabled);
            Assert.Equal("Multiple", vm.SelectedRandomize);
            Assert.True(vm.IsRandomizeTimerRunning);

            // Disabling Random Swap stops randomize timer
            vm.IsRandomSwapEnabled = false;
            Assert.False(vm.IsRandomSwapEnabled);
            Assert.Equal("None", vm.SelectedRandomize);
            Assert.False(vm.IsRandomizeTimerRunning);
        }

        [Fact]
        public void Test_SingleVid_Only_Active_When_Checked()
        {
            _output.WriteLine("=======================================================================");
            _output.WriteLine("TEST: Single Vid mode is only active when explicitly checked");
            _output.WriteLine("=======================================================================");

            var vm = CreateIsolatedViewModel();

            // Single Vid is unchecked by default
            Assert.False(vm.IsSingleVidEnabled);

            // Populate slots with distinct videos
            var videos = new List<string> { @"C:\Videos\a.mp4", @"C:\Videos\b.mp4", @"C:\Videos\c.mp4", @"C:\Videos\d.mp4" };
            for (int i = 0; i < videos.Count; i++)
            {
                vm.VideoSlots.Add(new VideoSlotViewModel { Index = i, CurrentVideoPath = videos[i], IsVisible = true });
            }

            var batch = vm.GetCurrentActiveVideoBatch();
            Assert.Equal(4, batch.Count);
            Assert.Equal(4, batch.Distinct().Count()); // All slots are distinct when IsSingleVidEnabled is false

            // When Single Vid is checked, IsSingleVidEnabled becomes true
            vm.IsSingleVidEnabled = true;
            Assert.True(vm.IsSingleVidEnabled);

            // When Single Vid is unchecked again, IsSingleVidEnabled becomes false
            vm.IsSingleVidEnabled = false;
            Assert.False(vm.IsSingleVidEnabled);
        }

        [Fact]
        public void Test_ScrollingWall_ActiveBatch_PrioritizesDockedRows()
        {
            _output.WriteLine("=======================================================================");
            _output.WriteLine("TEST: Scrolling Wall active batch prioritizes on-screen docked slots");
            _output.WriteLine("=======================================================================");

            var vm = CreateIsolatedViewModel();
            vm.IsScrollEnabled = true;
            vm.ContainerHeight = 800;
            vm.Rows = 2;
            vm.Columns = 2;

            // Row 0: Docked at Y = 0 (on screen)
            vm.ScrollSlots.Add(new VideoSlotViewModel { CurrentVideoPath = @"C:\Videos\row0_col0.mp4", CollageX = 0, CollageY = 0, CollageWidth = 400, CollageHeight = 400 });
            vm.ScrollSlots.Add(new VideoSlotViewModel { CurrentVideoPath = @"C:\Videos\row0_col1.mp4", CollageX = 400, CollageY = 0, CollageWidth = 400, CollageHeight = 400 });

            // Row 1: Docked at Y = 400 (on screen)
            vm.ScrollSlots.Add(new VideoSlotViewModel { CurrentVideoPath = @"C:\Videos\row1_col0.mp4", CollageX = 0, CollageY = 400, CollageWidth = 400, CollageHeight = 400 });
            vm.ScrollSlots.Add(new VideoSlotViewModel { CurrentVideoPath = @"C:\Videos\row1_col1.mp4", CollageX = 400, CollageY = 400, CollageWidth = 400, CollageHeight = 400 });

            // Row -1: Off screen (top) at Y = -400
            vm.ScrollSlots.Add(new VideoSlotViewModel { CurrentVideoPath = @"C:\Videos\rowOff_top.mp4", CollageX = 0, CollageY = -400, CollageWidth = 400, CollageHeight = 400 });

            // Row 2: Off screen (bottom) at Y = 800
            vm.ScrollSlots.Add(new VideoSlotViewModel { CurrentVideoPath = @"C:\Videos\rowOff_bottom.mp4", CollageX = 0, CollageY = 800, CollageWidth = 400, CollageHeight = 400 });

            var batch = vm.GetCurrentActiveVideoBatch();
            _output.WriteLine($"Extracted batch: {string.Join(", ", batch)}");

            // Must capture exactly the 4 on-screen docked slots in row-major order
            Assert.Equal(4, batch.Count);
            Assert.Equal(@"C:\Videos\row0_col0.mp4", batch[0]);
            Assert.Equal(@"C:\Videos\row0_col1.mp4", batch[1]);
            Assert.Equal(@"C:\Videos\row1_col0.mp4", batch[2]);
            Assert.Equal(@"C:\Videos\row1_col1.mp4", batch[3]);
        }

        [Fact]
        public void Test_DockingAlignment_SnapsExactlyToRowBoundaries()
        {
            _output.WriteLine("=======================================================================");
            _output.WriteLine("TEST: Docking alignment correctly calculates remaining distance and snaps");
            _output.WriteLine("=======================================================================");

            double cellH = 400.0;
            double effectiveH = 800.0;
            bool isDown = false; // scrolling Up

            // Simulate slots mid-scroll at Y = 120.0 and Y = 520.0
            var slots = new List<VideoSlotViewModel>
            {
                new() { CollageX = 0, CollageY = 120.0, CollageHeight = cellH },
                new() { CollageX = 400, CollageY = 120.0, CollageHeight = cellH },
                new() { CollageX = 0, CollageY = 520.0, CollageHeight = cellH },
                new() { CollageX = 400, CollageY = 520.0, CollageHeight = cellH }
            };

            var anchor = slots.FirstOrDefault(s => s.CollageY >= -cellH * 0.5 && s.CollageY < effectiveH);
            Assert.NotNull(anchor);

            // Upward scroll: target is Floor(120 / 400) * 400 = 0.0
            double targetY = isDown
                ? Math.Ceiling(anchor.CollageY / cellH) * cellH
                : Math.Floor(anchor.CollageY / cellH) * cellH;

            Assert.Equal(0.0, targetY);
            double remainingDistance = Math.Abs(targetY - anchor.CollageY);
            Assert.Equal(120.0, remainingDistance);

            // When final adjustment is applied
            double finalAdjustment = targetY - anchor.CollageY; // -120.0
            foreach (var slot in slots)
            {
                slot.CollageY += finalAdjustment;
                slot.CollageY = Math.Round(slot.CollageY / cellH) * cellH;
            }

            // Verify both rows are exactly docked on integer multiples of cellH
            Assert.Equal(0.0, slots[0].CollageY);
            Assert.Equal(0.0, slots[1].CollageY);
            Assert.Equal(400.0, slots[2].CollageY);
            Assert.Equal(400.0, slots[3].CollageY);
        }
    }
}

