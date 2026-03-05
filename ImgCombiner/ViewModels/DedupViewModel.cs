using ImgCombiner.Services;
using ImgCombiner;
using ImgCombiner.Services;
using ImgCombiner.ViewModels;
using System.Collections.ObjectModel;
using System.IO;

namespace ImgCombiner.ViewModels;

public sealed class DedupViewModel : ObservableObject
{
    private const string CrossPageKey = "__CROSS__";

    private readonly IFolderScanService _scan;
    private readonly IThumbnailService _thumb;
    private readonly IRecycleBinService _recycle;
    private readonly IImageSignatureService _sig; // 仍用于元数据补齐/显示等
    private readonly IDedupGroupingService _grouping;

    public ObservableCollection<string> SelectedFolders { get; } = new();

    private bool _isRecursive = true;
    public bool IsRecursive { get => _isRecursive; set => SetProperty(ref _isRecursive, value); }

    private bool _matchWithinEachFolderOnly;
    public bool MatchWithinEachFolderOnly { get => _matchWithinEachFolderOnly; set => SetProperty(ref _matchWithinEachFolderOnly, value); }

    private int _threshold = 200; // 4x4 曼哈顿阈值
    public int Threshold { get => _threshold; set => SetProperty(ref _threshold, value); }

    // 固定参数：ratio eps 和 dhash阈值
    private const double RatioEps = 0.05;
    private const int DHashHammingThreshold = 24;

    public IReadOnlyList<string> KeepStrategies { get; } = new[] { "按尺寸最大" };
    private string _selectedKeepStrategy = "按尺寸最大";
    public string SelectedKeepStrategy { get => _selectedKeepStrategy; set => SetProperty(ref _selectedKeepStrategy, value); }

    private readonly List<FolderResultPageViewModel> _pages = new();
    private int _pageIndex;

    public bool HasPages => _pages.Count > 0;
    public bool CanPrevPage => _pageIndex > 0;
    public bool CanNextPage => _pageIndex + 1 < _pages.Count;
    public string PageText => _pages.Count == 0 ? "第 0/0 页" : $"第 {_pageIndex + 1}/{_pages.Count} 页";
    public string CurrentPageTitle => _pages.Count == 0 ? "" : _pages[_pageIndex].Title;

    public ObservableCollection<DedupGroupViewModel> CurrentPageGroups { get; } = new();

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanFind));
                OnPropertyChanged(nameof(CanApplyDelete));
            }
        }
    }

    public bool CanFind => !IsBusy && SelectedFolders.Count > 0;
    public bool CanApplyDelete => !IsBusy && EnumerateAllGroups().Any(g => g.IsActionEnabled);

    private double _progressPercent;
    public double ProgressPercent { get => _progressPercent; set => SetProperty(ref _progressPercent, value); }

    private string _progressText = "等待操作";
    public string ProgressText { get => _progressText; set => SetProperty(ref _progressText, value); }

    public string SummaryText => $"结果页：{_pages.Count}（按文件夹分页）";

    private CancellationTokenSource? _cts;

    public AsyncRelayCommand AddFolderCommand { get; }
    public RelayCommand<string> RemoveFolderCommand { get; }
    public AsyncRelayCommand FindDuplicatesCommand { get; }
    public AsyncRelayCommand PrevPageCommand { get; }
    public AsyncRelayCommand NextPageCommand { get; }
    public AsyncRelayCommand ApplyDeleteCommand { get; }
    public RelayCommand CancelCommand { get; }

    public RelayCommand SelectAllGroupsCommand { get; }
    public RelayCommand SelectNoneGroupsCommand { get; }

    public DedupViewModel(
        IFolderScanService scan, IThumbnailService thumb, IRecycleBinService recycle,
        IImageSignatureService sig, IDedupGroupingService grouping)
    {
        _scan = scan;
        _thumb = thumb;
        _recycle = recycle;
        _sig = sig;
        _grouping = grouping;

        AddFolderCommand = new AsyncRelayCommand(AddFolderAsync);
        RemoveFolderCommand = new RelayCommand<string>(RemoveFolder);
        FindDuplicatesCommand = new AsyncRelayCommand(FindDuplicatesAsync);

        PrevPageCommand = new AsyncRelayCommand(async () =>
        {
            if (!CanPrevPage) return;
            _pageIndex--;
            RefreshPage();
            if (_cts is null) _cts = new CancellationTokenSource();
            await LoadThumbnailsForCurrentPageAsync(_cts.Token);
        }, () => CanPrevPage);

        NextPageCommand = new AsyncRelayCommand(async () =>
        {
            if (!CanNextPage) return;
            _pageIndex++;
            RefreshPage();
            if (_cts is null) _cts = new CancellationTokenSource();
            await LoadThumbnailsForCurrentPageAsync(_cts.Token);
        }, () => CanNextPage);

        ApplyDeleteCommand = new AsyncRelayCommand(ApplyDeleteAsync);
        CancelCommand = new RelayCommand(() => _cts?.Cancel());

        SelectAllGroupsCommand = new RelayCommand(() =>
        {
            foreach (var g in CurrentPageGroups) g.IsActionEnabled = true;
            OnPropertyChanged(nameof(CanApplyDelete));
        }, () => CurrentPageGroups.Count > 0 && !IsBusy);

        SelectNoneGroupsCommand = new RelayCommand(() =>
        {
            foreach (var g in CurrentPageGroups) g.IsActionEnabled = false;
            OnPropertyChanged(nameof(CanApplyDelete));
        }, () => CurrentPageGroups.Count > 0 && !IsBusy);
    }

    private IEnumerable<DedupGroupViewModel> EnumerateAllGroups()
        => _pages.SelectMany(p => p.Groups);

    private async Task AddFolderAsync()
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog();
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        if (!SelectedFolders.Contains(dlg.SelectedPath))
            SelectedFolders.Add(dlg.SelectedPath);

        OnPropertyChanged(nameof(CanFind));
    }

    private void RemoveFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        SelectedFolders.Remove(folder);
        OnPropertyChanged(nameof(CanFind));
    }

    private async Task FindDuplicatesAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            _pages.Clear();
            CurrentPageGroups.Clear();
            _pageIndex = 0;
            RaisePageProps();

            ProgressText = "扫描文件...";
            ProgressPercent = 0;

            var allPaths = new List<string>();
            foreach (var folder in SelectedFolders)
                allPaths.AddRange(await Task.Run(() => _scan.ScanImages(folder, IsRecursive), ct));

            if (allPaths.Count == 0)
            {
                ProgressText = "未找到图片文件";
                return;
            }

            // 按直接父目录分组
            var folderMap = allPaths
                .Select(p => new { Folder = Path.GetDirectoryName(p) ?? "", Path = p })
                .Where(x => !string.IsNullOrWhiteSpace(x.Folder))
                .GroupBy(x => x.Folder, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            if (MatchWithinEachFolderOnly)
            {
                int done = 0;
                foreach (var kv in folderMap.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                {
                    ct.ThrowIfCancellationRequested();
                    done++;

                    var folder = kv.Key;
                    var paths = kv.Value;
                    if (paths.Count < 2) continue;

                    ProgressText = $"匹配（仅文件夹内） {done}/{folderMap.Count}";
                    ProgressPercent = done * 100.0 / Math.Max(1, folderMap.Count);
                    var prog = new Progress<(double percent, string text)>(x =>
                    {
                        ProgressPercent = x.percent;
                        ProgressText = x.text;
                    });
                    var groups = await Task.Run(() =>
                        _grouping.FindSimilarGroups(paths, Threshold, RatioEps, DHashHammingThreshold, prog, ct), ct);

                    if (groups.Count == 0) continue;

                    var page = new FolderResultPageViewModel(folder, $"文件夹：{folder}");
                    foreach (var g in groups.OrderByDescending(x => x.Count))
                    {
                        var vm = new DedupGroupViewModel($"高概率同一图片（{g.Count} 张）");
                        foreach (var p in g) vm.Items.Add(new DedupImageItemViewModel(p));
                        page.Groups.Add(vm);
                    }
                    _pages.Add(page);
                }
            }
            else
            {
                ProgressText = $"匹配（跨文件夹，阈值 {Threshold}，ratioEps {RatioEps}，dHash<= {DHashHammingThreshold}）...";
                ProgressPercent = 0;
                var prog = new Progress<(double percent, string text)>(x =>
                {
                    ProgressPercent = x.percent;
                    ProgressText = x.text;
                });

                var groups = await Task.Run(() =>
                    _grouping.FindSimilarGroups(allPaths, Threshold, RatioEps, DHashHammingThreshold, prog, ct), ct);

                // 归类为：单文件夹页 / 跨文件夹页
                var folderPages = new Dictionary<string, FolderResultPageViewModel>(StringComparer.OrdinalIgnoreCase);
                FolderResultPageViewModel? cross = null;

                foreach (var g in groups.OrderByDescending(x => x.Count))
                {
                    ct.ThrowIfCancellationRequested();

                    var folders = g.Select(p => Path.GetDirectoryName(p) ?? "")
                                   .Distinct(StringComparer.OrdinalIgnoreCase)
                                   .ToList();

                    var vm = new DedupGroupViewModel($"高概率同一图片（{g.Count} 张）");
                    foreach (var p in g) vm.Items.Add(new DedupImageItemViewModel(p));

                    if (folders.Count == 1)
                    {
                        var folder = folders[0];
                        if (!folderPages.TryGetValue(folder, out var page))
                        {
                            page = new FolderResultPageViewModel(folder, $"文件夹：{folder}");
                            folderPages[folder] = page;
                        }
                        page.Groups.Add(vm);
                    }
                    else
                    {
                        cross ??= new FolderResultPageViewModel(CrossPageKey, "跨文件夹相似组");
                        cross.Groups.Add(vm);
                    }
                }

                foreach (var page in folderPages.Values.OrderBy(p => p.PageKey, StringComparer.OrdinalIgnoreCase))
                    _pages.Add(page);
                if (cross is not null && cross.Groups.Count > 0)
                    _pages.Add(cross);
            }

            RefreshPage();
            await LoadThumbnailsForCurrentPageAsync(ct);

            ProgressText = $"完成：页数 {_pages.Count}";
            ProgressPercent = 0;
            RaisePageProps();
        }
        catch (OperationCanceledException)
        {
            ProgressText = "已取消";
        }
        finally
        {
            IsBusy = false;
            RaisePageProps();
        }
    }

    private void RefreshPage()
    {
        CurrentPageGroups.Clear();
        if (_pages.Count == 0) { RaisePageProps(); return; }

        foreach (var g in _pages[_pageIndex].Groups)
            CurrentPageGroups.Add(g);

        RaisePageProps();
    }

    private async Task LoadThumbnailsForCurrentPageAsync(CancellationToken ct)
    {
        foreach (var group in CurrentPageGroups)
        {
            foreach (var item in group.Items)
            {
                ct.ThrowIfCancellationRequested();
                if (item.Thumbnail is not null && item.Width > 0 && item.Height > 0 && item.FileSize > 0) continue;

                try
                {
                    // 缩略图（慢）只对当前页加载
                    item.Thumbnail ??= await _thumb.LoadThumbnailAsync(item.Path, maxPixels: 400, ct);

                    // 元数据（快）
                    try
                    {
                        var meta = _sig.ReadMetaFast(item.Path);
                        item.Width = meta.Width;
                        item.Height = meta.Height;
                    }
                    catch { }

                    try
                    {
                        var fi = new FileInfo(item.Path);
                        item.FileSize = fi.Exists ? fi.Length : 0;
                    }
                    catch { }

                    item.OnPropertyChanged(nameof(item.Meta));
                }
                catch { }
            }
        }
    }

    private static int FormatRank(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" => 3,
            ".webp" => 2,
            ".jpg" or ".jpeg" => 1,
            ".bmp" => 0,
            _ => 0
        };
    }

    private static string PickKeepPathByRule(IEnumerable<DedupImageItemViewModel> items)
    {
        return items
            .OrderByDescending(it => (long)it.Width * it.Height)
            .ThenByDescending(it => FormatRank(it.Path))   // png > webp > others
            .ThenByDescending(it => it.FileSize)
            .ThenBy(it => it.Path, StringComparer.OrdinalIgnoreCase)
            .First()
            .Path;
    }

    // 用 MagickImageInfo 快速补齐（不加载缩略图）
    private bool TryFillMetaFast(DedupImageItemViewModel it)
    {
        try
        {
            if (it.FileSize <= 0)
            {
                var fi = new FileInfo(it.Path);
                if (!fi.Exists) return false;
                it.FileSize = fi.Length;
            }

            if (it.Width <= 0 || it.Height <= 0)
            {
                var meta = _sig.ReadMetaFast(it.Path);
                it.Width = meta.Width;
                it.Height = meta.Height;
            }

            it.OnPropertyChanged(nameof(it.Meta));
            return it.Width > 0 && it.Height > 0 && it.FileSize > 0;
        }
        catch { return false; }
    }

    private async Task ApplyDeleteAsync()
    {
        // (1) 全局生效：所有页里的组
        var selectedGroups = EnumerateAllGroups().Where(g => g.IsActionEnabled).ToList();
        if (selectedGroups.Count == 0) return;

        // 删除前：快速补齐元数据（不依赖缩略图是否加载完成）
        int failMeta = 0;
        foreach (var g in selectedGroups)
        {
            foreach (var it in g.Items)
            {
                if (it.Width > 0 && it.Height > 0 && it.FileSize > 0) continue;
                if (!TryFillMetaFast(it)) failMeta++;
            }
        }

        if (failMeta > 0)
        {
            var r = System.Windows.MessageBox.Show(
                $"有 {failMeta} 个文件无法读取元数据（可能已损坏/被占用/无权限）。\n" +
                $"继续执行将跳过这些文件，且可能影响保留规则。\n\n是否继续？",
                "元数据读取失败",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (r != System.Windows.MessageBoxResult.Yes)
                return;
        }

        // 组装待删除列表
        var toDelete = new List<string>();

        foreach (var g in selectedGroups)
        {
            // 只用元数据齐全的条目参与保留规则
            var valid = g.Items.Where(it => it.Width > 0 && it.Height > 0 && it.FileSize > 0).ToList();
            if (valid.Count <= 1) continue;

            var keepPath = PickKeepPathByRule(valid);

            foreach (var it in valid)
            {
                if (!string.Equals(it.Path, keepPath, StringComparison.OrdinalIgnoreCase))
                    toDelete.Add(it.Path);
            }
        }

        toDelete = toDelete.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (toDelete.Count == 0) return;

        var msg = $"将把 {toDelete.Count} 个文件移入回收站（全局范围：所有已勾选相似组；每组保留 1 张）。是否继续？";
        if (System.Windows.MessageBox.Show(msg, "确认删除", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning)
            != System.Windows.MessageBoxResult.Yes)
            return;

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            int done = 0, fail = 0;
            int total = toDelete.Count;

            foreach (var p in toDelete)
            {
                ct.ThrowIfCancellationRequested();

                try { _recycle.SendToRecycleBin(p); }
                catch { fail++; }

                done++;
                if (done % 10 == 0 || done == total)
                {
                    ProgressPercent = done * 100.0 / Math.Max(1, total);
                    ProgressText = $"删除中 {done}/{total}（失败 {fail}）";
                    await Task.Delay(1);
                }
            }

            ProgressText = $"删除完成：{done}（失败 {fail}）";
            ProgressPercent = 0;

            await FindDuplicatesAsync();
        }
        catch (OperationCanceledException)
        {
            ProgressText = "已取消";
        }
        finally
        {
            IsBusy = false;
            RaisePageProps();
        }
    }

    private void RaisePageProps()
    {
        OnPropertyChanged(nameof(HasPages));
        OnPropertyChanged(nameof(CanPrevPage));
        OnPropertyChanged(nameof(CanNextPage));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(CurrentPageTitle));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(CanApplyDelete));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }
}