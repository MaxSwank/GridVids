using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GridVids.Services
{
    public static class BinaryDownloader
    {
        private const string DefaultFfprobeZipUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
        private const string DefaultMpvZipUrl = "https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20260303/mpv-x86_64-v3-20260303-git-c55bdc3.7z";

        public static Task<string> EnsureFfprobeAsync(string? downloadUrl = null, string? baseDir = null)
            => EnsureBinaryFromArchiveAsync("ffprobe.exe", Path.Combine("Binaries", "win-x64"), downloadUrl ?? DefaultFfprobeZipUrl, baseDir);

        public static Task<string> EnsureMpvAsync(string? downloadUrl = null, string? baseDir = null)
            => EnsureBinaryFromArchiveAsync("mpv.exe", Path.Combine("Binaries", "win-x64"), downloadUrl ?? DefaultMpvZipUrl, baseDir);

        private static async Task<string> EnsureBinaryFromArchiveAsync(string binaryName, string relativeTargetDir, string downloadUrl, string? baseDir = null)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                throw new PlatformNotSupportedException("This helper currently targets Windows builds.");

            baseDir ??= AppContext.BaseDirectory;
            var targetDir = Path.Combine(baseDir, relativeTargetDir);
            Directory.CreateDirectory(targetDir);

            var targetPath = Path.Combine(targetDir, binaryName);
            if (File.Exists(targetPath)) return targetPath;

            Console.WriteLine($"Downloading {binaryName} from {downloadUrl}...");
            using var http = new HttpClient();
            // GitHub requires a User-Agent
            http.DefaultRequestHeaders.Add("User-Agent", "GridVids-BinaryDownloader");
            using var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

            var ext = Path.GetExtension(downloadUrl).Split('?').FirstOrDefault();
            var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + (string.IsNullOrEmpty(ext) ? ".tmp" : ext));
            try
            {
                using (var fs = File.Create(tmp))
                    await stream.CopyToAsync(fs).ConfigureAwait(false);

                Console.WriteLine($"Extracting {binaryName} using native tar...");

                // Use 'tar' (bsdtar) which is built-in to modern Windows and handles .zip, .7z, etc.
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "tar.exe",
                    Arguments = $"-xf \"{tmp}\" -C \"{targetDir}\" --strip-components 0",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                using var proc = System.Diagnostics.Process.Start(startInfo);
                await proc!.WaitForExitAsync();

                // Look for the binary in targetDir (it might be in a subfolder from tar extraction)
                var found = Directory.GetFiles(targetDir, binaryName, SearchOption.AllDirectories).FirstOrDefault();
                if (found != null)
                {
                    if (!found.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(found, targetPath, overwrite: true);
                    }
                    return targetPath;
                }

                // Final fallback: copy the downloaded file directly if it's not an archive
                File.Copy(tmp, targetPath, overwrite: true);
                return targetPath;
            }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
        }
    }
}
