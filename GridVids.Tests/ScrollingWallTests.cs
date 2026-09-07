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
        public void Test_RandomizeTimer_IsStrictlyDisabled_DuringScrollingWall()
        {
            _output.WriteLine("=======================================================================");
            _output.WriteLine("TEST: Randomize timer is strictly stopped in Scrolling Wall");
            _output.WriteLine("=======================================================================");

            var vm = CreateIsolatedViewModel();

            // In Grid mode with SelectedRandomize = "Multiple", randomize timer runs
            vm.SelectedDisplayMode = "Grid";
            vm.SelectedRandomize = "Multiple";

            _output.WriteLine($"In Grid with Multiple: IsRandomizeTimerRunning = {vm.IsRandomizeTimerRunning}");
            Assert.True(vm.IsRandomizeTimerRunning);

            // Switching to Scrolling Wall must disable the randomize timer
            vm.SelectedDisplayMode = "Scrolling Wall";
            _output.WriteLine($"In Scrolling Wall with Multiple: IsRandomizeTimerRunning = {vm.IsRandomizeTimerRunning}");
            Assert.False(vm.IsRandomizeTimerRunning);

            // Switching back to Grid should re-enable the randomize timer
            vm.SelectedDisplayMode = "Grid";
            _output.WriteLine($"Back in Grid with Multiple: IsRandomizeTimerRunning = {vm.IsRandomizeTimerRunning}");
            Assert.True(vm.IsRandomizeTimerRunning);
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
    }
}

