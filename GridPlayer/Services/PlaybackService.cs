using GridVids.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace GridVids.Services
{
    public class PlaybackService
    {
        private readonly ScriptOrchestrator _orchestrator;
        private readonly SemaphoreSlim _processLaunchSemaphore = new(5);
        public bool IsRandomStartEnabled { get; set; } = true;
        public bool IsMuted { get; set; } = true;
        public int Volume { get; set; } = 10;
        public bool IsSloMo { get; set; } = false;

        public PlaybackService()
        {
            _orchestrator = new ScriptOrchestrator();
        }

        public void UpdateSpeed(IEnumerable<IGridSlot> slots, bool isSloMo)
        {
            IsSloMo = isSloMo;
            double speed = isSloMo ? 0.7 : 1.0;

            foreach (var slot in slots)
            {
                slot.SetProperty("speed", speed);
            }
        }

        public void SetPlayDirectionAndSpeed(IGridSlot slot, bool isForward, double speed)
        {
            string dir = isForward ? "forward" : "backward";
            slot.SetProperty("play-direction", dir);
            slot.SetProperty("speed", speed);
        }

        public void ResetPlayDirectionAndSpeed(IEnumerable<IGridSlot> slots, bool isSloMo)
        {
            double speed = isSloMo ? 0.7 : 1.0;
            foreach (var slot in slots)
            {
                slot.SetProperty("play-direction", "forward");
                slot.SetProperty("speed", speed);
            }
        }

        public void UpdateVolume(IEnumerable<IGridSlot> slots, bool isMuted, int volume)
        {
            IsMuted = isMuted;
            Volume = volume;

            foreach (var slot in slots)
            {
                if (isMuted)
                {
                    slot.SetProperty("mute", true);
                }
                else
                {
                    slot.SetProperty("mute", false);
                    slot.SetProperty("volume", Math.Clamp(volume, 0, 100));
                }
            }
        }

        public async Task PlayAsync(IEnumerable<IGridSlot> slots, List<string> videos)
        {
            var tasks = new List<Task>();
            var slotList = new List<IGridSlot>(slots);

            if (slotList.Count == 0 || videos.Count == 0) return;

            // Ensure we have enough videos for all slots by recycling cyclically
            var videoList = new List<string>(videos);
            if (videoList.Count < slotList.Count)
            {
                int originalCount = videoList.Count;
                int idx = 0;
                while (videoList.Count < slotList.Count)
                {
                    videoList.Add(videos[idx % originalCount]);
                    idx++;
                }
            }

            var videoInstanceCounts = new Dictionary<string, int>();
            foreach (var video in videoList)
            {
                if (videoInstanceCounts.ContainsKey(video))
                    videoInstanceCounts[video]++;
                else
                    videoInstanceCounts[video] = 1;
            }

            var videoInstanceIndices = new Dictionary<string, Stack<int>>();
            var rng = new Random();
            foreach (var kvp in videoInstanceCounts)
            {
                var indices = Enumerable.Range(0, kvp.Value).OrderBy(x => rng.Next()).ToList();
                videoInstanceIndices[kvp.Key] = new Stack<int>(indices);
            }

            for (int i = 0; i < slotList.Count; i++)
            {
                if (i < videoList.Count)
                {
                    var slot = slotList[i];
                    var video = videoList[i];
                    
                    int totalInstances = videoInstanceCounts[video];
                    int instanceIndex = videoInstanceIndices[video].Pop();
                    
                    tasks.Add(TransitionSlotAsync(slot, video, totalInstances, instanceIndex));
                }
            }
            await Task.WhenAll(tasks);
        }

        public async Task RefreshSlotsAsync(IEnumerable<IGridSlot> slots, List<string> videos)
        {
            // Similar to PlayAsync but used for refreshing specific slots (like hidden ones)
            await PlayAsync(slots, videos);
        }

        public void Stop(IEnumerable<IGridSlot> slots)
        {
            foreach (var slot in slots)
            {
                var oldProcess = slot.UpdateProcess(null, string.Empty);
                // Offload cleanup to background thread to avoid UI blocking
                if (oldProcess != null)
                {
                    Task.Run(() => CleanupProcess(oldProcess));
                }
            }
        }

        public void StopAll(IEnumerable<IGridSlot> slots1, IEnumerable<IGridSlot> slots2, IEnumerable<IGridSlot>? slots3 = null)
        {
            Stop(slots1);
            Stop(slots2);
            if (slots3 != null) Stop(slots3);
            KillAllMpvProcesses();
        }

        public static void KillAllMpvProcesses()
        {
            try
            {
                var processes = Process.GetProcessesByName("mpv");
                foreach (var proc in processes)
                {
                    try
                    {
                        if (!proc.HasExited)
                        {
                            proc.Kill();
                        }
                    }
                    catch { }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            catch { }
        }

        public async Task<(Process? Process, string IpcPipeName)> PreloadMpvAsync(IGridSlot slot, string videoPath)
        {
            await _processLaunchSemaphore.WaitAsync();
            try
            {
                var handle = slot.WindowHandle;
                string ipcPipeName = $"gridvids_mpv_{Guid.NewGuid():N}";
                var proc = await Task.Run(() => _orchestrator.StartMpvInstance(videoPath, handle, IsRandomStartEnabled, 1, 0, IsMuted, Volume, IsSloMo, ipcPipeName));
                return (proc, ipcPipeName);
            }
            finally
            {
                _processLaunchSemaphore.Release();
            }
        }

        public void SwapPreloadedSlot(IGridSlot slot, Process newProcess, string videoPath, string? ipcPipeName = null)
        {
            var oldProcess = slot.UpdateProcess(newProcess, videoPath, ipcPipeName);
            if (oldProcess != null)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(600);
                    CleanupProcess(oldProcess);
                });
            }
        }

        private async Task TransitionSlotAsync(IGridSlot slot, string videoPath, int totalInstances = 1, int instanceIndex = 0)
        {
            await _processLaunchSemaphore.WaitAsync();
            Process? newProcess = null;
            string ipcPipeName = $"gridvids_mpv_{Guid.NewGuid():N}";
            try
            {
                // Capture the handle on the potentially-UI thread before going background
                var handle = slot.WindowHandle;

                // Run the heavy process creation (Launch + ffrprobe duration check) on a background thread
                newProcess = await Task.Run(() => _orchestrator.StartMpvInstance(videoPath, handle, IsRandomStartEnabled, totalInstances, instanceIndex, IsMuted, Volume, IsSloMo, ipcPipeName));
            }
            finally
            {
                _processLaunchSemaphore.Release();
            }

            // Update UI/Slot on the original context (UI thread)
            var oldProcess = slot.UpdateProcess(newProcess, videoPath, ipcPipeName);

            // Cleanup old process on background thread with smooth grace period
            if (oldProcess != null)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(600);
                    CleanupProcess(oldProcess);
                });
            }
        }

        private void CleanupProcess(Process? process)
        {
            if (process != null)
            {
                try
                {
                    if (!process.HasExited) process.Kill();
                }
                catch { }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }
}
