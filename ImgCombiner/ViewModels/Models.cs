using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;

namespace ImgCombiner.ViewModels;

public enum TargetImageFormat { Png, Jpg, Webp }
public enum JpegQualityMode { Low, Medium, High, Custom }

public sealed class FileNodeViewModel : ObservableObject
{
    public string DisplayName { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }

    public ObservableCollection<FileNodeViewModel> Children { get; } = new();

    private bool _isAlreadyTargetFormat;
    public bool IsAlreadyTargetFormat { get => _isAlreadyTargetFormat; set => SetProperty(ref _isAlreadyTargetFormat, value); }

    private bool _hasError;
    public bool HasError { get => _hasError; set => SetProperty(ref _hasError, value); }

    public FileNodeViewModel(string displayName, string fullPath, bool isDirectory)
    {
        DisplayName = displayName;
        FullPath = fullPath;
        IsDirectory = isDirectory;
    }
}

public sealed class DedupImageItemViewModel : ObservableObject
{
    public string Path { get; }
    public string FileName => System.IO.Path.GetFileName(Path);

    private BitmapSource? _thumbnail;
    public BitmapSource? Thumbnail { get => _thumbnail; set => SetProperty(ref _thumbnail, value); }

    public int Width { get; set; }
    public int Height { get; set; }
    public long FileSize { get; set; }

    public string Meta => $"{Width}x{Height}  {FileSize / 1024} KB";

    public DedupImageItemViewModel(string path) => Path = path;
}

public sealed class DedupGroupViewModel : ObservableObject
{
    public string Title { get; }
    public ObservableCollection<DedupImageItemViewModel> Items { get; } = new();

    private bool _isActionEnabled = true;
    public bool IsActionEnabled { get => _isActionEnabled; set => SetProperty(ref _isActionEnabled, value); }

    public DedupGroupViewModel(string title) => Title = title;
}