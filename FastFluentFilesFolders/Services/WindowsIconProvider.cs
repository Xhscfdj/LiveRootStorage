using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace FastFluentFilesFolders.Services
{
    public class WindowsIconProvider : IIconProvider
    {
        private readonly IconCache _iconCache;

        public WindowsIconProvider(IconCache iconCache)
        {
            _iconCache = iconCache;
        }

        private static string BuildCacheKey(string path, bool isFolder, uint size)
        {
            return $"winrt:{path.ToLowerInvariant()}_{(isFolder ? "dir" : "file")}_{size}";
        }

        public async Task<ImageSource?> GetIconAsync(string path, bool isFolder, Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue, uint size = 24)
        {
            if (string.IsNullOrEmpty(path)) return null;

            string cacheKey = BuildCacheKey(path, isFolder, size);
            if (_iconCache.TryGet(cacheKey, out var cached))
                return cached;

            ImageSource? result;
            if (isFolder && Directory.Exists(path))
                result = await GetFolderIconAsync(path, dispatcherQueue, size);
            else
                result = await GetFileIconAsync(path, dispatcherQueue, size);

            if (result != null)
                _iconCache.Set(cacheKey, result);

            return result;
        }

        private static async Task<ImageSource?> GetFolderIconAsync(string folderPath, Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue, uint size = 24)
        {
            var tcs = new TaskCompletionSource<ImageSource?>();
            dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var folder = await StorageFolder.GetFolderFromPathAsync(folderPath);
                    using var thumbnail = await folder.GetThumbnailAsync(ThumbnailMode.SingleItem, size, ThumbnailOptions.UseCurrentScale);
                    if (thumbnail == null || thumbnail.Size == 0)
                    {
                        tcs.SetResult(null);
                        return;
                    }
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(thumbnail);
                    tcs.SetResult(bitmap);
                }
                catch
                {
                    tcs.SetResult(null);
                }
            });
            return await tcs.Task;
        }

        private static async Task<ImageSource?> GetFileIconAsync(string filePath, Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue, uint size = 24)
        {
            var tcs = new TaskCompletionSource<ImageSource?>();
            dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(filePath);
                    using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, size, ThumbnailOptions.UseCurrentScale);
                    if (thumbnail == null || thumbnail.Size == 0)
                    {
                        tcs.SetResult(null);
                        return;
                    }
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(thumbnail);
                    tcs.SetResult(bitmap);
                }
                catch
                {
                    tcs.SetResult(null);
                }
            });
            return await tcs.Task;
        }
    }
}
