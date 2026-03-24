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
                    _playedSingleVids.Clear();
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

        private HashSet<string> _playedSingleVids = new();

        public async Task<List<string>> GetRandomVideosAsync(int count, HashSet<string>? excludedPaths = null, bool isSingleVidMode = false)
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

                    if (isSingleVidMode)
                    {
                        var unplayedFiles = _cachedVideoFiles.Where(f => !_playedSingleVids.Contains(f) && !excludedPaths.Contains(f)).ToList();
                        if (unplayedFiles.Count == 0)
                        {
                            _playedSingleVids.Clear();
                            unplayedFiles = _cachedVideoFiles.Where(f => !excludedPaths.Contains(f)).ToList();
                        }
                        
                        var selectedSingle = unplayedFiles.OrderBy(x => rng.Next()).FirstOrDefault();
                        if (selectedSingle == null) selectedSingle = _cachedVideoFiles.FirstOrDefault();
                        
                        if (selectedSingle != null)
                        {
                            _playedSingleVids.Add(selectedSingle);
                            return Enumerable.Repeat(selectedSingle, count).ToList();
                        }
                        return new List<string>();
                    }

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
