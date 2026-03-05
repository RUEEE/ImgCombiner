using ImgCombiner;
using ImgCombiner.ViewModels;
using System.Collections.ObjectModel;

namespace ImgCombiner.ViewModels;

public sealed class FolderResultPageViewModel : ObservableObject
{
    public string PageKey { get; }          // folder path 或 "__CROSS__"
    public string Title { get; }            // 显示标题
    public ObservableCollection<DedupGroupViewModel> Groups { get; } = new();

    public FolderResultPageViewModel(string pageKey, string title)
    {
        PageKey = pageKey;
        Title = title;
    }

    public int GroupCount => Groups.Count;
}