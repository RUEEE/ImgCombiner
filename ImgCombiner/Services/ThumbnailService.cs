using System.Windows.Media.Imaging;

namespace ImgCombiner.Services;

public sealed class ThumbnailService : IThumbnailService
{
    public Task<BitmapSource> LoadThumbnailAsync(string path, int maxPixels, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path);

            // 限制解码尺寸，避免大图预览占内存
            bmp.DecodePixelWidth = maxPixels;
            bmp.EndInit();
            bmp.Freeze();
            return (BitmapSource)bmp;
        }, ct);
    }
}