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
    }
}
