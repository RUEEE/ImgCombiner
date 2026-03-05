using ImageMagick;
using ImgCombiner.ViewModels;
using ImgCombiner.Services;
using System.IO;

namespace ImgCombiner.Services;

public sealed class ImageTranscodeService : IImageTranscodeService
{
    public async Task TranscodeAsync(
        string sourcePath,
        TargetImageFormat targetFormat,
        int jpegQuality,
        bool deleteSourceToRecycleBin,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("源文件不存在", sourcePath);

        var srcInfo = new FileInfo(sourcePath);
        var srcMtimeUtc = srcInfo.LastWriteTimeUtc;

        var targetExt = targetFormat switch
        {
            TargetImageFormat.Png => ".png",
            TargetImageFormat.Jpg => ".jpg",
            TargetImageFormat.Webp => ".webp",
            _ => ".png"
        };

        var targetPath = Path.ChangeExtension(sourcePath, targetExt);

        // 源已是目标格式时，上层应跳过；这里防御性 return
        if (Path.GetFullPath(targetPath).Equals(Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
            return;

        targetPath = EnsureUniqueByAppendingOnes(targetPath);

        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            using var image = new MagickImage(sourcePath);

            switch (targetFormat)
            {
                case TargetImageFormat.Png:
                    image.Format = MagickFormat.Png;
                    image.Write(targetPath);
                    break;

                case TargetImageFormat.Jpg:
                    // JPG：默认白底 + 去 alpha
                    image.BackgroundColor = MagickColors.White;
                    image.Alpha(AlphaOption.Remove);
                    image.Format = MagickFormat.Jpeg;
                    image.Quality = Math.Clamp((uint)jpegQuality, 1, 100);
                    image.Write(targetPath);
                    break;

                case TargetImageFormat.Webp:
                    // WebP：可保留透明；quality 同 slider
                    image.Format = MagickFormat.WebP;
                    image.Quality = Math.Clamp((uint)jpegQuality, 1, 100);
                    image.Write(targetPath);
                    break;

                default:
                    throw new NotSupportedException($"不支持的目标格式：{targetFormat}");
            }
        }, ct);

        File.SetLastWriteTimeUtc(targetPath, srcMtimeUtc);
    }

    private static string EnsureUniqueByAppendingOnes(string path)
    {
        if (!File.Exists(path)) return path;

        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        var candidate = Path.Combine(dir, $"{name}(1){ext}");
        while (File.Exists(candidate))
        {
            name = Path.GetFileNameWithoutExtension(candidate);
            candidate = Path.Combine(dir, $"{name}(1){ext}");
        }
        return candidate;
    }
}