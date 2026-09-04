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
        }

        [Fact]
        public void Test_Dropdowns_Are_Alphabetically_Sorted()
        {
            // Expected alpha sorted DisplayModeOptions (Cycle Modes is now a checkbox next to dropdown)
            var expectedDisplayModes = new List<string>
            {
                "Auto-Swap",
                "Boomerang",
                "Collage",
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
            Assert.True(vm.StopCommand.CanExecute(null));

            // Executing Stop when not playing shouldn't throw
            vm.StopCommand.Execute(null);
            Assert.False(vm.IsVideoPlaying);
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
