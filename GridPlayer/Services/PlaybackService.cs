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

        public PlaybackService()
        {
            _orchestrator = new ScriptOrchestrator();
        }

        public void UpdateVolume(IEnumerable<IGridSlot> slots, bool isMuted, int volume)
        {
            IsMuted = isMuted;
            Volume = volume;

            foreach (var slot in slots)
            {
                if (slot.CurrentProcess != null && !slot.CurrentProcess.HasExited)
                {
                    try
                    {
                        if (isMuted)
                        {
                            slot.CurrentProcess.StandardInput.WriteLine("set mute yes");
                        }
                        else
                        {
                            slot.CurrentProcess.StandardInput.WriteLine("set mute no");
                            slot.CurrentProcess.StandardInput.WriteLine($"set volume {Math.Clamp(volume, 0, 100)}");
                        }
                    }
                    catch { }
                }
            }
        }

        public async Task PlayAsync(IEnumerable<IGridSlot> slots, List<string> videos)
        {
            var tasks = new List<Task>();
            var slotList = new List<IGridSlot>(slots);

            var videoInstanceCounts = new Dictionary<string, int>();
            foreach (var video in videos)
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
                if (i < videos.Count)
                {
                    var slot = slotList[i];
                    var video = videos[i];
                    
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

        public void StopAll(IEnumerable<IGridSlot> slots1, IEnumerable<IGridSlot> slots2)
        {
            Stop(slots1);
            Stop(slots2);
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

        public async Task<Process?> PreloadMpvAsync(IGridSlot slot, string videoPath)
        {
            await _processLaunchSemaphore.WaitAsync();
            try
            {
                var handle = slot.WindowHandle;
                return await Task.Run(() => _orchestrator.StartMpvInstance(videoPath, handle, IsRandomStartEnabled, 1, 0, IsMuted, Volume));
            }
            finally
            {
                _processLaunchSemaphore.Release();
            }
        }

        public void SwapPreloadedSlot(IGridSlot slot, Process newProcess, string videoPath)
        {
            var oldProcess = slot.UpdateProcess(newProcess, videoPath);
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
            try
            {
                // Capture the handle on the potentially-UI thread before going background
                var handle = slot.WindowHandle;

                // Run the heavy process creation (Launch + ffrprobe duration check) on a background thread
                newProcess = await Task.Run(() => _orchestrator.StartMpvInstance(videoPath, handle, IsRandomStartEnabled, totalInstances, instanceIndex, IsMuted, Volume));
            }
            finally
            {
                _processLaunchSemaphore.Release();
            }

            // Update UI/Slot on the original context (UI thread)
            var oldProcess = slot.UpdateProcess(newProcess, videoPath);

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
