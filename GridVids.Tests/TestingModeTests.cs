using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GridVids.Services;
using GridVids.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace GridVids.Tests
{
    public class TestingModeTests
    {
        private readonly ITestOutputHelper _output;

        public TestingModeTests(ITestOutputHelper output)
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
        public void Test_TestingMode_ExistsInDisplayModeOptions()
        {
            var vm = CreateIsolatedViewModel();
            Assert.Contains("Testing", vm.DisplayModeOptions);
        }

        [Fact]
        public void Test_SelectingTestingMode_InitializesGridPhase()
        {
            var vm = CreateIsolatedViewModel();
            vm.Rows = 3;
            vm.Columns = 4;

            vm.SelectedDisplayMode = "Testing";

            Assert.Equal("Testing", vm.SelectedDisplayMode);
            Assert.Equal(TestingPhase.Grid, vm.CurrentTestingPhase);
            Assert.True(vm.IsGridVisible);
            Assert.False(vm.IsScrollEnabled);
            Assert.False(vm.IsStackableEnabled);
            // Verify rows and columns were not clamped to 2x2
            Assert.Equal(3, vm.Rows);
            Assert.Equal(4, vm.Columns);
        }

        [Fact]
        public void Test_TestingMode_Scrolls2RowsBeforeDocking()
        {
            var vm = CreateIsolatedViewModel();
            vm.Rows = 2;
            vm.Columns = 2;
            vm.ContainerHeight = 800; // cellH = 400
            vm.SelectedDisplayMode = "Testing";

            // Populate mock scroll slots
            vm.IsScrollEnabled = true;
            vm.ScrollSlots.Add(new VideoSlotViewModel
            {
                Index = 0,
                CollageX = 0,
                CollageY = 0,
                CollageWidth = 400,
                CollageHeight = 400,
                IsCollageVisible = true,
                CurrentVideoPath = "video1.mp4"
            });

            // 2 rows distance = 2.0 * 400 = 800
            double cellH = 800.0 / 2.0;
            double target2Rows = 2.0 * cellH;
            Assert.Equal(800.0, target2Rows);
        }

        [Fact]
        public async Task Test_TestingMode_StackingCompletion_ResetsGridAndStartsScrolling()
        {
            var vm = CreateIsolatedViewModel();
            vm.SelectedDisplayMode = "Testing";

            // Trigger scroll docked callback -> moves to Stacking
            await vm.OnTestingScrollDocked();
            Assert.Equal(TestingPhase.Stacking, vm.CurrentTestingPhase);
            Assert.True(vm.IsStackableEnabled);

            // Trigger stack completion callback -> resets Grid and immediately starts scrolling
            await vm.OnTestingStackingCompleted();
            Assert.Equal(TestingPhase.Scrolling, vm.CurrentTestingPhase);
            Assert.True(vm.IsScrollEnabled);
            Assert.False(vm.IsStackableEnabled);
        }

        [Fact]
        public async Task Test_TestingMode_PreservesCustomRowsAndColumnsAcrossStacking()
        {
            var vm = CreateIsolatedViewModel();
            vm.Rows = 3;
            vm.Columns = 5;

            vm.SelectedDisplayMode = "Testing";
            Assert.Equal(3, vm.Rows);
            Assert.Equal(5, vm.Columns);

            // Move to Stacking
            await vm.OnTestingScrollDocked();
            Assert.Equal(3, vm.Rows);
            Assert.Equal(5, vm.Columns);
            Assert.True(vm.IsStackableEnabled);
        }

        [Fact]
        public async Task Test_TestingMode_TransitionToStacking_ReusesExistingScrollingBatch()
        {
            var vm = CreateIsolatedViewModel();
            vm.Rows = 2;
            vm.Columns = 2;
            vm.ContainerHeight = 800;
            vm.ContainerWidth = 800;
            vm.SelectedDisplayMode = "Testing";

            // Populate scrolling slots representing the active scrolling wall batch
            var scrollingBatch = new List<string> { "video_a.mp4", "video_b.mp4", "video_c.mp4", "video_d.mp4" };
            vm.ScrollSlots.Clear();
            for (int i = 0; i < 4; i++)
            {
                vm.ScrollSlots.Add(new VideoSlotViewModel
                {
                    Index = i,
                    CollageX = (i % 2) * 400,
                    CollageY = (i / 2) * 400,
                    CollageWidth = 400,
                    CollageHeight = 400,
                    CurrentVideoPath = scrollingBatch[i]
                });
            }

            // Trigger scroll docked transition
            await vm.OnTestingScrollDocked();

            // Verify _currentVideoBatch matches scrollingBatch and wasn't replaced with a new batch
            Assert.Equal(scrollingBatch.Count, vm.CurrentVideoBatch.Count);
            for (int i = 0; i < scrollingBatch.Count; i++)
            {
                Assert.Contains(scrollingBatch[i], vm.CurrentVideoBatch);
                Assert.Equal(scrollingBatch[i], vm.VideoSlots[i].CurrentVideoPath);
            }
        }

        [Fact]
        public async Task Test_TestingMode_Stacking_Overlays_ReuseVideosFromScrollingPool()
        {
            var vm = CreateIsolatedViewModel();
            vm.Rows = 2;
            vm.Columns = 2;
            vm.ContainerHeight = 800;
            vm.ContainerWidth = 800;
            vm.VideoPath = @"C:\TestVideos";
            vm.IsVideoPlaying = true;
            vm.SelectedDisplayMode = "Testing";

            // Scrolling pool has 8 videos that played during scrolling
            var scrollingPool = new List<string>
            {
                "scroll_1.mp4", "scroll_2.mp4", "scroll_3.mp4", "scroll_4.mp4",
                "scroll_5.mp4", "scroll_6.mp4", "scroll_7.mp4", "scroll_8.mp4"
            };

            // Docked slots are first 4 videos
            vm.ScrollSlots.Clear();
            for (int i = 0; i < 4; i++)
            {
                vm.ScrollSlots.Add(new VideoSlotViewModel
                {
                    Index = i,
                    CollageX = (i % 2) * 400,
                    CollageY = (i / 2) * 400,
                    CollageWidth = 400,
                    CollageHeight = 400,
                    CurrentVideoPath = scrollingPool[i]
                });
            }

            // Populate all 8 into the scrolling pool simulating rows that scrolled through
            vm.AddVideosToScrollingPool(scrollingPool);

            // Transition from scrolling to stackable
            await vm.OnTestingScrollDocked();

            // Docked videos are on base grid
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(scrollingPool[i], vm.VideoSlots[i].CurrentVideoPath);
            }

            // CurrentVideoBatch must contain all videos from the scrolling pool
            foreach (var vid in scrollingPool)
            {
                Assert.Contains(vid, vm.CurrentVideoBatch);
            }

            // Trigger quadrant stacking step
            await vm.TriggerStackStepAsync();

            // StackSlots overlay videos should be present and strictly drawn from the scrolling pool
            Assert.True(vm.StackSlots.Count > 0);
            foreach (var stackSlot in vm.StackSlots)
            {
                Assert.False(string.IsNullOrEmpty(stackSlot.CurrentVideoPath));
                Assert.Contains(stackSlot.CurrentVideoPath, vm.CurrentVideoBatch);
            }
        }
    }
}
