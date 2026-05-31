using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GridVids.ViewModels
{
    public partial class VideoSlotViewModel : ObservableObject, Models.IGridSlot
    {
        [ObservableProperty]
        private IntPtr _windowHandle;

        public int Index { get; set; }

        public void OnHandleReady(IntPtr handle)
        {
            WindowHandle = handle;
            System.Diagnostics.Debug.WriteLine($"Slot {Index} handle ready: {handle}");
        }

        [ObservableProperty]
        private bool _isVisible = true;

        public System.Diagnostics.Process? CurrentProcess { get; set; }
        public string CurrentVideoPath { get; set; } = string.Empty;
        
        public bool IsHovered { get; set; }
        public DateTime HoverStartTime { get; set; }
        public bool HasTriggeredHover { get; set; }

        [ObservableProperty]
        private string _fileName = string.Empty;

        [ObservableProperty]
        private string _frameRate = string.Empty;

        [ObservableProperty]
        private string _bitRate = string.Empty;

        [ObservableProperty]
        private bool _showMetadataOverlay;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EffectiveX))]
        private double _collageX;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EffectiveY))]
        private double _collageY;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EffectiveWidth))]
        [NotifyPropertyChangedFor(nameof(EffectiveHeight))]
        private double _collageWidth = 640;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EffectiveHeight))]
        private double _collageHeight = 281; // 16:9 approx

        [ObservableProperty]
        private double _opacity = 0;

        public DateTime SpawnTime { get; set; }
        public TimeSpan Lifetime { get; set; } = TimeSpan.FromSeconds(15);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EffectiveX))]
        [NotifyPropertyChangedFor(nameof(EffectiveY))]
        [NotifyPropertyChangedFor(nameof(EffectiveWidth))]
        [NotifyPropertyChangedFor(nameof(EffectiveHeight))]
        private bool _isCollageVisible = false; // Starts hidden until delayed show

        // Use integers for HWND bounds to prevent sub-pixel jitter and aspect ratio drift (stretching)
        public double EffectiveX => IsCollageVisible ? Math.Round(CollageX) : -10000;
        public double EffectiveY => IsCollageVisible ? Math.Round(CollageY) : -10000;

        public double EffectiveWidth => Math.Round(CollageWidth);
        // Force Height to be derived from Width to lock Aspect Ratio (16:9)
        public double EffectiveHeight => Math.Round(CollageHeight);

        // State for Seamless Replacement
        [ObservableProperty]
        private bool _isExpired;

        [ObservableProperty]
        private bool _isDying;

        public bool HasIncomingReplacement { get; set; }
        public VideoSlotViewModel? Replaces { get; set; }

        public System.Diagnostics.Process? UpdateProcess(System.Diagnostics.Process? newProcess, string newVideoPath)
        {
            var old = CurrentProcess;

            // Ensure UI update happens on UI thread if we are not already there?
            // Since this sets properties that might be bound (though CurrentProcess isn't ObservableProperty currently, it's just a property)
            // If we want to be safe, we invoke.
            // However, CurrentProcess implies state. If we make it ObservableProperty later, we definitely need Dispatcher.

            Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
            {
                CurrentProcess = newProcess;
                CurrentVideoPath = newVideoPath;
                FileName = System.IO.Path.GetFileName(newVideoPath);
                FrameRate = string.Empty;
                BitRate = string.Empty;
                HasTriggeredHover = false;
            });

            if (!string.IsNullOrEmpty(newVideoPath))
            {
                System.Threading.Tasks.Task.Run(() =>
                {
                    var orchestrator = new GridVids.Services.ScriptOrchestrator();
                    var meta = orchestrator.GetVideoMetadata(newVideoPath);
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (CurrentVideoPath == newVideoPath)
                        {
                            FrameRate = meta.FrameRate;
                            BitRate = meta.BitRate;
                        }
                    });
                });
            }

            return old;
        }

        public void UpdateOverlay(bool show)
        {
            if (ShowMetadataOverlay == show) return;

            if (show)
            {
                if (HasTriggeredHover) return;
                HasTriggeredHover = true;
            }

            ShowMetadataOverlay = show;

            if (CurrentProcess != null && !CurrentProcess.HasExited)
            {
                try
                {
                    if (show)
                    {
                        // Styling for mpv OSD using ASS tags: 
                        // an7: Top-Left, fs18: Font size, bord1: border, b1: Bold
                        // We use double backslashes for the ASS tags in the string.
                        string safeName = FileName.Replace("\\", "\\\\").Replace("\"", "\\\"");
                        string text = $"File: {safeName}\\nFPS: {FrameRate}\\nBitrate: {BitRate}";
                        string assText = "{\\\\an7}{\\\\fs18}{\\\\bord1}{\\\\shad1}{\\\\b1}" + text;
                        CurrentProcess.StandardInput.WriteLine($"show-text \"{assText}\" 1000000");
                    }
                    else
                    {
                        CurrentProcess.StandardInput.WriteLine("show-text \"\" 1");
                    }
                }
                catch { }
            }
        }

        public void ToggleMetadataOverlay()
        {
            UpdateOverlay(!ShowMetadataOverlay);
        }
    }
}
