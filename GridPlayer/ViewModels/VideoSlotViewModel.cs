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
        [NotifyPropertyChangedFor(nameof(IsEffectiveVisible))]
        private bool _isVisible = true;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsEffectiveVisible))]
        private bool _isHiddenBySettingsBar;

        public bool IsEffectiveVisible => IsVisible && !IsHiddenBySettingsBar;

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



        public DateTime BoomerangStartTime { get; set; } = DateTime.MinValue;
        public int BoomerangPhase { get; set; } = 0;

        [ObservableProperty]
        private string? _ipcPipeName;

        public System.Diagnostics.Process? UpdateProcess(System.Diagnostics.Process? newProcess, string newVideoPath, string? ipcPipeName = null)
        {
            var old = CurrentProcess;

            Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
            {
                CurrentProcess = newProcess;
                CurrentVideoPath = newVideoPath;
                IpcPipeName = ipcPipeName;
                FileName = System.IO.Path.GetFileName(newVideoPath);
                FrameRate = string.Empty;
                BitRate = string.Empty;
                HasTriggeredHover = false;
                BoomerangStartTime = DateTime.UtcNow;
                BoomerangPhase = 0;
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

        public void SendIpcCommand(string commandJson)
        {
            if (string.IsNullOrEmpty(IpcPipeName)) return;

            Task.Run(() =>
            {
                try
                {
                    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                    {
                        using var pipeClient = new System.IO.Pipes.NamedPipeClientStream(".", IpcPipeName, System.IO.Pipes.PipeDirection.Out);
                        pipeClient.Connect(150);
                        using var writer = new System.IO.StreamWriter(pipeClient);
                        writer.WriteLine(commandJson);
                        writer.Flush();
                    }
                }
                catch
                {
                    // Ignore transient errors when process is closing
                }
            });
        }

        public void SetProperty(string propertyName, object value)
        {
            var cmd = new
            {
                command = new object[] { "set_property", propertyName, value }
            };
            string json = System.Text.Json.JsonSerializer.Serialize(cmd);
            SendIpcCommand(json);
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
                        string text = $"File: {safeName} FPS: {FrameRate} Bitrate: {BitRate}";
                        var cmd = new { command = new object[] { "show-text", text, 1000000 } };
                        SendIpcCommand(System.Text.Json.JsonSerializer.Serialize(cmd));
                    }
                    else
                    {
                        var cmd = new { command = new object[] { "show-text", "", 1 } };
                        SendIpcCommand(System.Text.Json.JsonSerializer.Serialize(cmd));
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
