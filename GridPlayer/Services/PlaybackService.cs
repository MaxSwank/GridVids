using GridVids.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace GridVids.Services
{
    public class PlaybackService
    {
        private readonly ScriptOrchestrator _orchestrator;
        private readonly SemaphoreSlim _processLaunchSemaphore = new(5);

        public PlaybackService()
        {
            _orchestrator = new ScriptOrchestrator();
        }

        public async Task PlayAsync(IEnumerable<IGridSlot> slots, List<string> videos)
        {
            var tasks = new List<Task>();
            var slotList = new List<IGridSlot>(slots);

            for (int i = 0; i < slotList.Count; i++)
            {
                if (i < videos.Count)
                {
                    var slot = slotList[i];
                    var video = videos[i];
                    tasks.Add(TransitionSlotAsync(slot, video));
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

        private async Task TransitionSlotAsync(IGridSlot slot, string videoPath)
        {
            await _processLaunchSemaphore.WaitAsync();
            Process? newProcess = null;
            try
            {
                // Capture the handle on the potentially-UI thread before going background
                var handle = slot.WindowHandle;

                // Run the heavy process creation (Launch + ffrprobe duration check) on a background thread
                newProcess = await Task.Run(() => _orchestrator.StartMpvInstance(videoPath, handle));
            }
            finally
            {
                _processLaunchSemaphore.Release();
            }

            // Update UI/Slot on the original context (UI thread)
            var oldProcess = slot.UpdateProcess(newProcess, videoPath);

            // Cleanup old process on background thread
            if (oldProcess != null)
            {
                _ = Task.Run(() => CleanupProcess(oldProcess));
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
