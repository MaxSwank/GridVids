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
        private static readonly Dictionary<string, List<double>> _recentStartTimes = new();
        private static readonly object _startTimeLock = new();

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

        public Process? StartMpvInstance(string videoPath, IntPtr windowHandle, bool randomStart = true)
        {
            double startTime = 0;
            if (randomStart)
            {
                try
                {
                    double duration = GetVideoDuration(videoPath);
                    if (duration > 5)
                    {
                        lock (_startTimeLock)
                        {
                            if (!_recentStartTimes.ContainsKey(videoPath))
                                _recentStartTimes[videoPath] = new List<double>();
                                
                            var recentTimes = _recentStartTimes[videoPath];
                            
                            double bestTime = _rng.NextDouble() * (duration - 2.0);
                            double maxMinDistance = -1;
                            
                            for (int i = 0; i < 50; i++)
                            {
                                double candidate = _rng.NextDouble() * (duration - 2.0);
                                double minDistance = double.MaxValue;
                                foreach (var t in recentTimes)
                                {
                                    // Treat video duration as circular to maximize perceived separation over loops
                                    double dist = Math.Min(Math.Abs(t - candidate), duration - Math.Abs(t - candidate));
                                    if (dist < minDistance) minDistance = dist;
                                }
                                
                                if (recentTimes.Count == 0)
                                {
                                    bestTime = candidate;
                                    break;
                                }
                                
                                if (minDistance > maxMinDistance)
                                {
                                    maxMinDistance = minDistance;
                                    bestTime = candidate;
                                }
                                
                                // Good enough spread found (30 seconds)
                                if (maxMinDistance >= 30)
                                {
                                    break;
                                }
                            }
                            
                            startTime = bestTime;
                            recentTimes.Add(startTime);
                            
                            // Keep history bounded securely to prevent memory leaks over time
                            if (recentTimes.Count > 30) recentTimes.RemoveAt(0);
                        }
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
                "--no-audio",
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
                "--audio=no"
            };


            var psi = new ProcessStartInfo
            {
                FileName = mpvPath,
                Arguments = string.Join(" ", args),
                UseShellExecute = false,
                CreateNoWindow = true
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
    }
}
