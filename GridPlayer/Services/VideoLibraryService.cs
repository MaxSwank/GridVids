using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace GridVids.Services
{
    public class VideoLibraryService
    {
        private List<string> _cachedVideoFiles = new();
        private readonly System.Threading.SemaphoreSlim _cacheLock = new(1, 1);

        public async Task RefreshCacheAsync(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !System.IO.Directory.Exists(folderPath))
            {
                await _cacheLock.WaitAsync();
                try
                {
                    _cachedVideoFiles.Clear();
                }
                finally { _cacheLock.Release(); }
                return;
            }

            await Task.Run(async () =>
            {
                await _cacheLock.WaitAsync();
                try
                {
                    var files = System.IO.Directory.GetFiles(folderPath, "*.mp4", System.IO.SearchOption.AllDirectories);
                    _cachedVideoFiles = files.ToList();
                    Debug.WriteLine($"Cache refreshed: {_cachedVideoFiles.Count} videos found.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error caching videos: {ex.Message}");
                    _cachedVideoFiles.Clear();
                }
                finally
                {
                    _cacheLock.Release();
                }
            });
        }

        public async Task<List<string>> GetRandomVideosAsync(int count, HashSet<string>? excludedPaths = null)
        {
            await _cacheLock.WaitAsync();
            try
            {
                if (_cachedVideoFiles.Count == 0)
                {
                    return new List<string>();
                }

                excludedPaths ??= new HashSet<string>();

                return await Task.Run(() =>
                {
                    var rng = new Random();
                    var availableFiles = _cachedVideoFiles.Where(f => !excludedPaths.Contains(f)).ToList();

                    // Fallback
                    if (availableFiles.Count < count)
                    {
                        availableFiles = _cachedVideoFiles.ToList();
                    }

                    return availableFiles.OrderBy(x => rng.Next()).Take(count).ToList();
                });
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        public int GetCacheSize() => _cachedVideoFiles.Count;
    }
}
