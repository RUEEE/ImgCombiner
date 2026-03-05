using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace ImgCombiner.Converters;

/// <summary>
/// 文件路径短显示：...\dir1\dir2\file.ext，并尽量不超过 MaxLen
/// </summary>
public sealed class FilePathShortenerConverter : IValueConverter
{
    public int MaxLen { get; set; } = 50;
    public int MaxLevels { get; set; } = 2; // 保留最后N级目录 + 文件名

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var full = value as string;
        if (string.IsNullOrWhiteSpace(full)) return "";

        full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var file = Path.GetFileName(full);
        var dir = Path.GetDirectoryName(full) ?? "";

        var parts = dir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       .Where(p => !string.IsNullOrWhiteSpace(p))
                       .ToList();

        var tailDirs = parts.TakeLast(Math.Min(MaxLevels, parts.Count)).ToList();
        var shown = (tailDirs.Count > 0)
            ? $"...\\{string.Join("\\", tailDirs)}\\{file}"
            : file;

        if (shown.Length > MaxLen)
        {
            var keep = Math.Max(1, MaxLen - 4); // 预留 ...\
            shown = shown.Substring(shown.Length - keep, keep);
            shown = "...\\" + shown;
        }

        return shown;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}