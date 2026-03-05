using ImgCombiner.Services;
using ImgCombiner;
using ImgCombiner.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;

namespace ImgCombiner.ViewModels;

public sealed class ConvertViewModel : ObservableObject
{
    private readonly IFolderScanService _scan;
    private readonly IThumbnailService _thumb;
    private readonly IRecycleBinService _recycle;
    private readonly IImageTranscodeService _transcode;

    public ObservableCollection<string> SelectedFolders { get; } = new();
    public ObservableCollection<FileNodeViewModel> RootNodes { get; } = new();

    public IReadOnlyList<TargetImageFormat> TargetFormats { get; } = new[] { TargetImageFormat.Png, TargetImageFormat.Jpg, TargetImageFormat.Webp };

    private TargetImageFormat _selectedTargetFormat = TargetImageFormat.Png;
    public TargetImageFormat SelectedTargetFormat
    {
        get => _selectedTargetFormat;
        set
        {
            if (SetProperty(ref _selectedTargetFormat, value))
            {
                OnPropertyChanged(nameof(IsJpegSelected));
                OnPropertyChanged(nameof(IsQualityRelevant));
                RebuildTreeFlags();
            }
        }
    }

    public bool IsJpegSelected => SelectedTargetFormat == TargetImageFormat.Jpg;

    // quality 对 jpg/webp 都相关
    public bool IsQualityRelevant => SelectedTargetFormat is TargetImageFormat.Jpg or TargetImageFormat.Webp;

    public IReadOnlyList<JpegQualityMode> JpegQualityModes { get; } =
        new[] { JpegQualityMode.Low, JpegQualityMode.Medium, JpegQualityMode.High, JpegQualityMode.Custom };

    private JpegQualityMode _selectedJpegQualityMode = JpegQualityMode.High;
    public JpegQualityMode SelectedJpegQualityMode
    {
        get => _selectedJpegQualityMode;
        set
        {
            if (SetProperty(ref _selectedJpegQualityMode, value))
            {
                OnPropertyChanged(nameof(IsCustomQualityMode));
                if (value != JpegQualityMode.Custom)
                {
                    JpegQuality = value switch
                    {
                        JpegQualityMode.Low => 60,
                        JpegQualityMode.Medium => 80,
                        _ => 92
                    };
                }
            }
        }
    }

    public bool IsCustomQualityMode => SelectedJpegQualityMode == JpegQualityMode.Custom;

    private int _jpegQuality = 92;
    public int JpegQuality { get => _jpegQuality; set => SetProperty(ref _jpegQuality, value); }

    private bool _isRecursive = true;
    public bool IsRecursive { get => _isRecursive; set => SetProperty(ref _isRecursive, value); }

    private bool _hideAlreadyTargetFormat;
    public bool HideAlreadyTargetFormat
    {
        get => _hideAlreadyTargetFormat;
        set
        {
            if (SetProperty(ref _hideAlreadyTargetFormat, value))
                RebuildTreeFlags();
        }
    }

    private bool _deleteSourceToRecycleBin;
    public bool DeleteSourceToRecycleBin { get => _deleteSourceToRecycleBin; set => SetProperty(ref _deleteSourceToRecycleBin, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set { if (SetProperty(ref _isBusy, value)) { OnPropertyChanged(nameof(CanStartConvert)); } } }

    public bool CanStartConvert => !IsBusy && RootNodes.Count > 0;

    private double _progressPercent;
    public double ProgressPercent { get => _progressPercent; set => SetProperty(ref _progressPercent, value); }

    private string _progressText = "等待操作";
    public string ProgressText { get => _progressText; set => SetProperty(ref _progressText, value); }

    private string _fileSummary = "尚未扫描";
    public string FileSummary { get => _fileSummary; set => SetProperty(ref _fileSummary, value); }

    private BitmapSource? _previewImage;
    public BitmapSource? PreviewImage { get => _previewImage; set => SetProperty(ref _previewImage, value); }

    private string _previewInfo = "";
    public string PreviewInfo { get => _previewInfo; set => SetProperty(ref _previewInfo, value); }

    private FileNodeViewModel? _selectedNode;
    public FileNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (SetProperty(ref _selectedNode, value))
                _ = LoadPreviewAsync(value);
        }
    }

    private CancellationTokenSource? _cts;

    public AsyncRelayCommand AddFolderCommand { get; }
    public RelayCommand<string> RemoveFolderCommand { get; }
    public AsyncRelayCommand StartConvertCommand { get; }
    public RelayCommand CancelCommand { get; }

    public ConvertViewModel(IFolderScanService scan, IThumbnailService thumb, IRecycleBinService recycle, IImageTranscodeService transcode)
    {
        _scan = scan;
        _thumb = thumb;
        _recycle = recycle;
        _transcode = transcode;

        AddFolderCommand = new AsyncRelayCommand(AddFolderAsync);
        RemoveFolderCommand = new RelayCommand<string>(RemoveFolder);
        StartConvertCommand = new AsyncRelayCommand(StartConvertAsync);
        CancelCommand = new RelayCommand(() => _cts?.Cancel());
    }

    private async Task AddFolderAsync()
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog();
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        if (!SelectedFolders.Contains(dlg.SelectedPath))
            SelectedFolders.Add(dlg.SelectedPath);

        await RescanAsync();
    }

    private void RemoveFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        SelectedFolders.Remove(folder);
        _ = RescanAsync();
    }

    private async Task RescanAsync()
    {
        RootNodes.Clear();
        PreviewImage = null;
        PreviewInfo = "";

        if (SelectedFolders.Count == 0)
        {
            FileSummary = "尚未选择文件夹";
            return;
        }

        IsBusy = true;
        ProgressText = "扫描中...";
        ProgressPercent = 0;

        try
        {
            var allFiles = new List<string>();
            foreach (var folder in SelectedFolders)
            {
                var files = await Task.Run(() => _scan.ScanImages(folder, IsRecursive));
                allFiles.AddRange(files);
            }

            var roots = BuildTree(allFiles);
            foreach (var r in roots) RootNodes.Add(r);

            var need = FlattenNodes(RootNodes).Count(n => !n.IsDirectory && !n.IsAlreadyTargetFormat);
            var already = FlattenNodes(RootNodes).Count(n => !n.IsDirectory && n.IsAlreadyTargetFormat);

            FileSummary = $"已扫描：{allFiles.Count} 个图片文件；需要转换：{need}；已是目标格式：{already}";
        }
        finally
        {
            ProgressText = "等待操作";
            ProgressPercent = 0;
            IsBusy = false;
            OnPropertyChanged(nameof(CanStartConvert));
        }
    }

    private List<FileNodeViewModel> BuildTree(List<string> files)
    {
        var rootMap = new Dictionary<string, FileNodeViewModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var rootFolder in SelectedFolders)
        {
            if (!rootMap.ContainsKey(rootFolder))
                rootMap[rootFolder] = new FileNodeViewModel(Path.GetFileName(rootFolder), rootFolder, isDirectory: true);
        }

        foreach (var f in files)
        {
            var alreadyTarget = IsAlreadyTarget(f);
            if (HideAlreadyTargetFormat && alreadyTarget)
                continue;

            var rootFolder = SelectedFolders.FirstOrDefault(sf => IsUnder(sf, f));
            if (rootFolder is null) continue;

            var rootNode = rootMap[rootFolder];
            var rel = Path.GetRelativePath(rootFolder, f);
            var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var current = rootNode;
            var runningPath = rootFolder;

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                runningPath = Path.Combine(runningPath, part);

                var isLast = i == parts.Length - 1;
                if (isLast)
                {
                    var fileNode = new FileNodeViewModel(part, runningPath, isDirectory: false)
                    {
                        IsAlreadyTargetFormat = alreadyTarget
                    };
                    current.Children.Add(fileNode);
                }
                else
                {
                    var next = current.Children.FirstOrDefault(x => x.IsDirectory && x.DisplayName.Equals(part, StringComparison.OrdinalIgnoreCase));
                    if (next is null)
                    {
                        next = new FileNodeViewModel(part, runningPath, isDirectory: true);
                        current.Children.Add(next);
                    }
                    current = next;
                }
            }
        }

        return rootMap.Values.ToList();
    }

    private static bool IsUnder(string root, string path)
    {
        var rp = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fp = Path.GetFullPath(path);
        return fp.StartsWith(rp, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsAlreadyTarget(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return SelectedTargetFormat switch
        {
            TargetImageFormat.Png => ext == ".png",
            TargetImageFormat.Jpg => ext is ".jpg" or ".jpeg",
            TargetImageFormat.Webp => ext == ".webp",
            _ => false
        };
    }

    private void RebuildTreeFlags() => _ = RescanAsync();

    private async Task LoadPreviewAsync(FileNodeViewModel? node)
    {
        if (node is null || node.IsDirectory || !File.Exists(node.FullPath))
        {
            PreviewImage = null;
            PreviewInfo = "";
            return;
        }

        try
        {
            var bmp = await _thumb.LoadThumbnailAsync(node.FullPath, maxPixels: 1400);
            PreviewImage = bmp;

            var fi = new FileInfo(node.FullPath);
            PreviewInfo =
                $"路径：{node.FullPath}\n" +
                $"大小：{fi.Length / 1024} KB\n" +
                $"修改时间：{fi.LastWriteTime}\n" +
                $"目标格式：{SelectedTargetFormat}\n" +
                $"状态：{(node.IsAlreadyTargetFormat ? "已是目标格式（灰色显示）" : "需要转换")}";
        }
        catch (Exception ex)
        {
            PreviewImage = null;
            PreviewInfo = $"预览失败：{ex.Message}";
        }
    }

    private async Task StartConvertAsync()
    {
        var fileNodes = FlattenNodes(RootNodes)
            .Where(x => !x.IsDirectory)
            .Where(x => !x.IsAlreadyTargetFormat)
            .ToList();

        var files = fileNodes.Select(n => n.FullPath).Where(File.Exists).ToList();

        if (files.Count == 0)
        {
            ProgressText = "没有需要转换的文件";
            return;
        }

        IsBusy = true;
        _cts = new CancellationTokenSource();

        int done = 0, fail = 0, deleted = 0;

        try
        {
            foreach (var f in files)
            {
                _cts.Token.ThrowIfCancellationRequested();

                ProgressPercent = done * 100.0 / files.Count;
                ProgressText = $"转换中：{Path.GetFileName(f)} ({done + 1}/{files.Count})";

                try
                {
                    await _transcode.TranscodeAsync(
                        sourcePath: f,
                        targetFormat: SelectedTargetFormat,
                        jpegQuality: JpegQuality,
                        deleteSourceToRecycleBin: DeleteSourceToRecycleBin,
                        ct: _cts.Token);

                    if (DeleteSourceToRecycleBin && File.Exists(f))
                    {
                        _recycle.SendToRecycleBin(f);
                        deleted++;
                    }
                }
                catch
                {
                    fail++;
                }
                finally
                {
                    done++;
                }
            }

            ProgressPercent = 100;
            ProgressText = $"完成：总计 {files.Count}；失败 {fail}；已回收站删除源文件 {deleted}";
            await RescanAsync();
        }
        catch (OperationCanceledException)
        {
            ProgressText = "已取消";
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            ProgressPercent = 0;
            IsBusy = false;
        }
    }

    private static IEnumerable<FileNodeViewModel> FlattenNodes(IEnumerable<FileNodeViewModel> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            foreach (var c in FlattenNodes(n.Children))
                yield return c;
        }
    }
}