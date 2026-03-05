using ImgCombiner.ViewModels;
namespace ImgCombiner.Services;

public interface IImageTranscodeService
{
    Task TranscodeAsync(string sourcePath, TargetImageFormat targetFormat, int jpegQuality, bool deleteSourceToRecycleBin, CancellationToken ct);
}
