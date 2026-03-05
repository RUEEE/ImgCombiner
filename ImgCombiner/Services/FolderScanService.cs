using System.IO;

namespace ImgCombiner.Services;

public sealed class FolderScanService : IFolderScanService
{
    private static readonly HashSet<string> _exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp"
    };

    public IReadOnlyList<string> ScanImages(string folder, bool recursive)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return Array.Empty<string>();

        var opt = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };

        return Directory.EnumerateFiles(folder, "*.*", opt)
            .Where(p => _exts.Contains(Path.GetExtension(p)))
            .ToList();
    }
}