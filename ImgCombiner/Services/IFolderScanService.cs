namespace ImgCombiner.Services;

public interface IFolderScanService
{
    IReadOnlyList<string> ScanImages(string folder, bool recursive);
}