using System.Windows.Media.Imaging;

namespace ImgCombiner.Services;

public interface IThumbnailService
{
    Task<BitmapSource> LoadThumbnailAsync(string path, int maxPixels, CancellationToken ct = default);
}