using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GridPlayer.Services
{
    public static class BinaryDownloader
    {
        private const string DefaultFfprobeZipUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
        private const string DefaultMpvZipUrl = "https://github.com/mpv-player/mpv-build/releases/latest/download/mpv-x86_64-w64-mingw32.zip";

        public static Task<string> EnsureFfprobeAsync(string downloadUrl = null)
            => EnsureBinaryFromArchiveAsync("ffprobe.exe", Path.Combine("Binaries", "win-x64"), downloadUrl ?? DefaultFfprobeZipUrl);

        public static Task<string> EnsureMpvAsync(string downloadUrl = null)
            => EnsureBinaryFromArchiveAsync("mpv.exe", Path.Combine("Binaries", "win-x64"), downloadUrl ?? DefaultMpvZipUrl);

        private static async Task<string> EnsureBinaryFromArchiveAsync(string binaryName, string relativeTargetDir, string downloadUrl)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                throw new PlatformNotSupportedException("This helper currently targets Windows builds. Add other platforms as needed.");

            var baseDir = AppContext.BaseDirectory;
            var targetDir = Path.Combine(baseDir, relativeTargetDir);
            Directory.CreateDirectory(targetDir);

            var targetPath = Path.Combine(targetDir, binaryName);
            if (File.Exists(targetPath))
                return targetPath;

            if (string.IsNullOrWhiteSpace(downloadUrl))
                throw new ArgumentException("A download URL must be supplied when no default is available.", nameof(downloadUrl));

            using var http = new HttpClient();
            using var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

            var ext = Path.GetExtension(downloadUrl).Split('?').FirstOrDefault();
            var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + (string.IsNullOrEmpty(ext) ? ".tmp" : ext));
            try
            {
                using (var fs = File.Create(tmp))
                    await stream.CopyToAsync(fs).ConfigureAwait(false);

                if (Path.GetExtension(tmp).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using var archive = ZipFile.OpenRead(tmp);
                    var entry = archive.Entries.FirstOrDefault(e => e.Name.Equals(binaryName, StringComparison.OrdinalIgnoreCase))
                                ?? archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("/" + binaryName, StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith("\\" + binaryName, StringComparison.OrdinalIgnoreCase));

                    if (entry != null)
                    {
                        var destPath = Path.Combine(targetDir, binaryName);
                        entry.ExtractToFile(destPath, overwrite: true);
                        return destPath;
                    }
                }

                // fallback: assume download is a direct exe
                var destExe = Path.Combine(targetDir, binaryName);
                File.Copy(tmp, destExe, overwrite: true);
                return destExe;
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }
    }
}
