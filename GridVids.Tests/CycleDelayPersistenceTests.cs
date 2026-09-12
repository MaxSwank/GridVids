using System;
using System.IO;
using GridVids.Models;
using GridVids.Services;
using GridVids.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace GridVids.Tests
{
    public class CycleDelayPersistenceTests
    {
        private readonly ITestOutputHelper _output;

        public CycleDelayPersistenceTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Test_CycleDelay_SavesAndLoadsAcrossSessions()
        {
            string tempSettingsFile = Path.Combine(Path.GetTempPath(), $"gridvids_test_settings_{Guid.NewGuid():N}.json");

            try
            {
                // Session 1: User launches app, changes cycle delay to 20, enables cycle mode
                var settingsService1 = new SettingsService(tempSettingsFile);
                var vm1 = new MainViewModel(settingsService1);

                _output.WriteLine($"Session 1 Initial CycleDelay: {vm1.SelectedCycleDelay}");
                Assert.Equal(10.0, vm1.SelectedCycleDelay);

                // User sets Cycle Delay to 20
                vm1.SelectedCycleDelay = 20.0;
                vm1.IsCycleModesEnabled = true;

                // Emulate Window Closing where vm.SaveSettings() is called
                vm1.SaveSettings();

                // Verify saved file contains SelectedCycleDelay = 20
                var savedSettings = settingsService1.LoadSettings();
                _output.WriteLine($"Session 1 Saved to Disk: SelectedCycleDelay = {savedSettings.SelectedCycleDelay}");
                Assert.Equal(20.0, savedSettings.SelectedCycleDelay);
                Assert.True(savedSettings.IsCycleModesEnabled);

                // Session 2: User opens app next time
                var settingsService2 = new SettingsService(tempSettingsFile);
                var vm2 = new MainViewModel(settingsService2);

                _output.WriteLine($"Session 2 Loaded CycleDelay: {vm2.SelectedCycleDelay}");
                Assert.Equal(20.0, vm2.SelectedCycleDelay);
                Assert.True(vm2.IsCycleModesEnabled);
                Assert.Contains(20.0, vm2.CycleDelayOptions);
            }
            finally
            {
                if (File.Exists(tempSettingsFile))
                {
                    File.Delete(tempSettingsFile);
                }
            }
        }

        [Fact]
        public void Test_CustomCycleDelay_RestoredAndAddedToOptions()
        {
            string tempSettingsFile = Path.Combine(Path.GetTempPath(), $"gridvids_test_settings_{Guid.NewGuid():N}.json");

            try
            {
                var settingsService = new SettingsService(tempSettingsFile);
                var initialSettings = new AppSettings
                {
                    SelectedCycleDelay = 25.0,
                    IsCycleModesEnabled = true
                };
                settingsService.SaveSettings(initialSettings);

                var vm = new MainViewModel(settingsService);

                _output.WriteLine($"Custom Delay Loaded: {vm.SelectedCycleDelay}");
                Assert.Equal(25.0, vm.SelectedCycleDelay);
                Assert.Contains(25.0, vm.CycleDelayOptions);
            }
            finally
            {
                if (File.Exists(tempSettingsFile))
                {
                    File.Delete(tempSettingsFile);
                }
            }
        }

        [Theory]
        [InlineData("Boomerang", true, 15.0)]
        [InlineData("Scrolling Wall", false, 30.0)]
        [InlineData("Stackable", true, 20.0)]
        [InlineData("Auto-Swap", false, 10.0)]
        [InlineData("Grid", true, 10.0)]
        public void Test_DisplayModeAndCycleSettings_SavedAndLoadedFromPreviousSession(string displayMode, bool cycleEnabled, double cycleDelay)
        {
            string tempSettingsFile = Path.Combine(Path.GetTempPath(), $"gridvids_test_settings_{Guid.NewGuid():N}.json");

            try
            {
                // Session 1: User configures display mode and cycle settings
                var settingsService1 = new SettingsService(tempSettingsFile);
                var vm1 = new MainViewModel(settingsService1);

                vm1.SelectedDisplayMode = displayMode;
                vm1.IsCycleModesEnabled = cycleEnabled;
                vm1.SelectedCycleDelay = cycleDelay;

                // Emulate Window Closing sequence: vm.SaveSettings() followed by vm.CleanupAllProcesses()
                vm1.SaveSettings();
                vm1.CleanupAllProcesses();

                // Check saved file directly on disk
                var savedSettings = settingsService1.LoadSettings();
                Assert.Equal(displayMode, savedSettings.SelectedDisplayMode);
                Assert.Equal(cycleEnabled, savedSettings.IsCycleModesEnabled);
                Assert.Equal(cycleDelay, savedSettings.SelectedCycleDelay);

                // Session 2: User opens app next time
                var settingsService2 = new SettingsService(tempSettingsFile);
                var vm2 = new MainViewModel(settingsService2);

                _output.WriteLine($"Session 2: Loaded DisplayMode={vm2.SelectedDisplayMode}, CycleEnabled={vm2.IsCycleModesEnabled}, CycleDelay={vm2.SelectedCycleDelay}");
                Assert.Equal(displayMode, vm2.SelectedDisplayMode);
                Assert.Equal(cycleEnabled, vm2.IsCycleModesEnabled);
                Assert.Equal(cycleDelay, vm2.SelectedCycleDelay);

                // Verify corresponding flags in ViewModel are correctly matched
                switch (displayMode)
                {
                    case "Boomerang":
                        Assert.True(vm2.IsBoomerangEnabled);
                        Assert.False(vm2.IsScrollEnabled);
                        Assert.False(vm2.IsStackableEnabled);
                        Assert.False(vm2.IsSwapEnabled);
                        break;
                    case "Scrolling Wall":
                        Assert.True(vm2.IsScrollEnabled);
                        Assert.False(vm2.IsBoomerangEnabled);
                        Assert.False(vm2.IsStackableEnabled);
                        Assert.False(vm2.IsSwapEnabled);
                        break;
                    case "Stackable":
                        Assert.True(vm2.IsStackableEnabled);
                        Assert.False(vm2.IsScrollEnabled);
                        Assert.False(vm2.IsBoomerangEnabled);
                        Assert.False(vm2.IsSwapEnabled);
                        break;
                    case "Auto-Swap":
                        Assert.True(vm2.IsSwapEnabled);
                        Assert.False(vm2.IsScrollEnabled);
                        Assert.False(vm2.IsStackableEnabled);
                        Assert.False(vm2.IsBoomerangEnabled);
                        break;
                    case "Grid":
                        Assert.False(vm2.IsSwapEnabled);
                        Assert.False(vm2.IsScrollEnabled);
                        Assert.False(vm2.IsStackableEnabled);
                        Assert.False(vm2.IsBoomerangEnabled);
                        break;
                }
            }
            finally
            {
                if (File.Exists(tempSettingsFile))
                {
                    File.Delete(tempSettingsFile);
                }
            }
        }
    }
}
