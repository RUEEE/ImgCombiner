using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace ImgCombiner.Converters;

/// <summary>
/// 将完整路径缩短显示：优先保留最后 N 级目录，且尽量使显示长度 <= MaxLen。
/// 输出类似：...\XXX\YYY
/// </summary>
public sealed class FolderPathShortenerConverter : IValueConverter
{
    public int MaxLen { get; set; } = 20;
    public int MaxLevels { get; set; } = 3; // N>1，默认保留最后3级（可调）

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var full = value as string;
        if (string.IsNullOrWhiteSpace(full)) return "";

        full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // 拆分目录段（忽略盘符根）
        var parts = full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .ToList();

        if (parts.Count == 0) return full;

        // 从最后往前拼，最多 MaxLevels 段
        var tailParts = parts.TakeLast(Math.Min(MaxLevels, parts.Count)).ToList();
        var shown = string.Join(Path.DirectorySeparatorChar, tailParts);

        // 如果还太长：继续截断前部，并加 ...\
        if (shown.Length > MaxLen)
        {
            // 保留后半部分，让末尾更可读
            // 预留 4 个字符用于 "...\"
            var keep = Math.Max(1, MaxLen - 4);
            if (shown.Length > keep)
                shown = shown.Substring(shown.Length - keep, keep);

            shown = "...\\" + shown;
        }
        else if (parts.Count > tailParts.Count)
        {
            shown = "...\\" + shown;
        }

        return shown;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}