using ImgCombiner.Services;
using ImgCombiner.ViewModels;
using ImgCombiner.Services;

namespace ImgCombiner.ViewModels;

public sealed class MainViewModel
{
    public ConvertViewModel Convert { get; }
    public DedupViewModel Dedup { get; }

    public MainViewModel()
    {
        // 这里直接 new 服务，后续可替换为 DI
        var scan = new FolderScanService();
        var thumb = new ThumbnailService();
        var recycle = new RecycleBinService();
        var sig = new ImageSignatureService();
        var grouping = new DedupGroupingService(sig);

        Convert = new ConvertViewModel(scan, thumb, recycle, new ImageTranscodeService());

        Convert = new ConvertViewModel(scan, thumb, recycle, new ImageTranscodeService());
        Dedup = new DedupViewModel(scan, thumb, recycle, sig, grouping);
    }
}