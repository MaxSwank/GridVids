using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq;

namespace GridVids.Services
{
    public class ScriptOrchestrator
    {
        private string _baseDir;
        private readonly Random _rng = new Random();

        public ScriptOrchestrator()
        {
            _baseDir = AppDomain.CurrentDomain.BaseDirectory;
        }

        public string GetMpvBinaryPath()
        {
            // Allow overriding via environment variable first
            var env = Environment.GetEnvironmentVariable("MPV_PATH");
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            {
                Debug.WriteLine($"Using MPV from MPV_PATH: {env}");
                return Path.GetFullPath(env);
            }

            // Next, try to find mpv on PATH (Windows: mpv.exe)
            string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "mpv.exe" : "mpv";
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), exeName);
                    if (File.Exists(candidate))
                    {
                        Debug.WriteLine($"Found mpv on PATH: {candidate}");
                        return Path.GetFullPath(candidate);
                    }
                }
                catch { }
            }

            // Fallback to bundled binary in the app's Binaries folder
            string relativePath = "";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                relativePath = Path.Combine("Binaries", "win-x64", "mpv.exe");
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                relativePath = Path.Combine("Binaries", "linux-x64", "mpv");
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                relativePath = Path.Combine("Binaries", "osx-x64", "mpv");

            var bundled = Path.GetFullPath(Path.Combine(_baseDir, relativePath));
            Debug.WriteLine($"Using bundled mpv: {bundled}");
            return bundled;
        }

        public string GetScriptPath(string scriptName)
        {
            // Look in Scripts folder or user provided folder
            // For now assume bundled in Scripts/
            return Path.GetFullPath(Path.Combine(_baseDir, "Scripts", scriptName));
        }

        public async Task RunScriptAsync(string scriptPath, List<IntPtr> windowHandles, string videoPath, string previousVids)
        {
            Debug.WriteLine($"Orchestrator RunScriptAsync. VideoPath: '{videoPath}'");
            Console.WriteLine($"[ScriptOrchestrator] RunScriptAsync called with VideoPath: '{videoPath}'");
            string mpvPath = GetMpvBinaryPath();

            if (!string.IsNullOrEmpty(videoPath) && videoPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                videoPath = videoPath.TrimEnd(Path.DirectorySeparatorChar);
            }

            string wids = string.Join(",", windowHandles.Select(h => h.ToString()));

            ProcessStartInfo psi = new ProcessStartInfo();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi.FileName = "powershell.exe";
                psi.Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -Wids \"{wids}\" -MpvPath \"{mpvPath}\" -VideoPath \"{videoPath}\" -PreviousVids \"{previousVids}\"";
            }
            else
            {
                psi.FileName = "/bin/bash";
                psi.Arguments = $"\"{scriptPath}\" \"{wids}\" \"{mpvPath}\" \"{videoPath}\"";
            }

            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;

            try
            {
                Debug.WriteLine($"Launching script: {psi.FileName} {psi.Arguments}");
                var proc = Process.Start(psi);
                if (proc == null) Debug.WriteLine("Failed to start script process.");
                else Debug.WriteLine($"Script started, PID: {proc.Id}");
            }
            catch (Exception ex) { Debug.WriteLine($"Error launching script: {ex.Message}"); }
        }

        public Process? StartMpvInstance(string videoPath, IntPtr windowHandle, bool randomStart = true, int totalInstances = 1, int instanceIndex = 0, bool isMuted = true, int volume = 10, bool isSloMo = false, string? ipcPipeName = null)
        {
            double startTime = 0;
            if (randomStart)
            {
                try
                {
                    double duration = GetVideoDuration(videoPath);
                    if (duration > 5)
                    {
                        totalInstances = Math.Max(1, totalInstances);
                        instanceIndex = Math.Max(0, Math.Min(totalInstances - 1, instanceIndex));
                        
                        double effectiveDuration = duration - 2.0;
                        if (effectiveDuration <= 0) effectiveDuration = duration;
                        
                        double segmentLength = effectiveDuration / totalInstances;
                        double segmentStart = instanceIndex * segmentLength;
                        
                        startTime = segmentStart + (_rng.NextDouble() * segmentLength);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to determine start time: {ex.Message}");
                }
            }

            string mpvPath = GetMpvBinaryPath();

            var args = new List<string>
            {
                $"--wid={windowHandle}",
                $"--start={startTime:F2}",
                $"\"{videoPath}\"",
                "--no-border",
                "--keep-open=yes",
                "--loop-file=inf",
                "--hwdec=auto",
                "--scale=bilinear",
                "--cscale=bilinear",
                "--dscale=bilinear",
                "--correct-downscaling=no",
                "--linear-downscaling=no",
                "--sigmoid-upscaling=no",
                "--hdr-compute-peak=no",
                "--osd-level=1",
                "--panscan=1.0",
                "--no-input-default-bindings",
                "--no-input-cursor",
                "--no-osc",
                "--input-vo-keyboard=no",
                "--cache=yes",
                "--cache-pause=no",
                "--demuxer-max-bytes=500MiB",
                "--demuxer-max-back-bytes=500MiB",
                "--demuxer-seekable-cache=yes",
                "--video-reversal-buffer=500MiB",
                "--audio-reversal-buffer=200MiB",
                "--video-sync=display-resample",
                "--vd-lavc-threads=0",
                "--vd-lavc-fast=yes",
                "--hr-seek=yes",
                "--hr-seek-framedrop=no"
            };

            if (!string.IsNullOrEmpty(ipcPipeName))
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    args.Add($"--input-ipc-server=\\\\.\\pipe\\{ipcPipeName}");
                }
                else
                {
                    args.Add($"--input-ipc-server=/tmp/{ipcPipeName}.sock");
                }
            }

            if (isSloMo)
            {
                args.Add("--speed=0.7");
            }

            if (isMuted)
            {
                args.Add("--mute=yes");
                args.Add("--volume=0");
            }
            else
            {
                args.Add("--mute=no");
                args.Add($"--volume={Math.Clamp(volume, 0, 100)}");
            }


            var psi = new ProcessStartInfo
            {
                FileName = mpvPath,
                Arguments = string.Join(" ", args),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true
            };

            Debug.WriteLine($"Starting MPV: {psi.FileName} {psi.Arguments}");

            try
            {
                return Process.Start(psi);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error starting MPV: {ex.Message}");
                return null;
            }
        }

        private double GetVideoDuration(string videoPath)
        {
            string mpvPath = GetMpvBinaryPath();
            string? dir = Path.GetDirectoryName(mpvPath);
            if (dir == null) return 0;

            string ffprobeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffprobe.exe" : "ffprobe";
            string ffprobePath = Path.Combine(dir, ffprobeName);

            if (!File.Exists(ffprobePath)) return 0;

            var psi = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return 0;

            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            if (double.TryParse(output.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double duration))
            {
                return duration;
            }
            return 0;
        }

        public (string FrameRate, string BitRate) GetVideoMetadata(string videoPath)
        {
            string mpvPath = GetMpvBinaryPath();
            string? dir = Path.GetDirectoryName(mpvPath);
            if (dir == null) return ("", "");

            string ffprobeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffprobe.exe" : "ffprobe";
            string ffprobePath = Path.Combine(dir, ffprobeName);

            if (!File.Exists(ffprobePath)) return ("", "");

            var psi = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = $"-v error -select_streams v:0 -show_entries stream=r_frame_rate -show_entries format=bit_rate -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            try
            {
                using var proc = Process.Start(psi);
                if (proc == null) return ("", "");

                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                string frameRate = "";
                string bitRate = "";

                if (lines.Length > 0)
                {
                    string[] frParts = lines[0].Split('/');
                    if (frParts.Length == 2 && double.TryParse(frParts[0], out double num) && double.TryParse(frParts[1], out double den) && den != 0)
                    {
                        frameRate = Math.Round(num / den, 2).ToString() + " fps";
                    }
                    else
                    {
                        frameRate = lines[0] + " fps";
                    }
                }

                if (lines.Length > 1)
                {
                    if (double.TryParse(lines[1], out double br))
                    {
                        bitRate = Math.Round(br / 1000.0) + " kbps";
                    }
                    else
                    {
                        bitRate = lines[1] + " kbps";
                    }
                }

                return (frameRate, bitRate);
            }
            catch
            {
                return ("", "");
            }
        }
    }
}
