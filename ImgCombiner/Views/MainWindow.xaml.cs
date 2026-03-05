using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ImgCombiner.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        // 注册内置转换器资源（简化 XAML）
        InitializeComponent();

    }
    private void DedupListBox_ForceScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ListBox lb) return;
        var sv = FindDescendantScrollViewer(lb);
        if (sv is null) return;
        // WPF 的 MouseWheel Delta 通常是 120 的倍数
        // 这里按“行”滚动（不强制一项一项），会非常稳定
        if (e.Delta < 0) sv.LineDown();
        else sv.LineUp();
        e.Handled = true;
    }
    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            var found = FindDescendantScrollViewer(child);
            if (found is not null) return found;
        }
        return null;
    }
    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        // 将选中项交给 VM（避免在 TreeView 上做复杂绑定）
        if (DataContext is ViewModels.MainViewModel vm)
            vm.Convert.SelectedNode = e.NewValue as ViewModels.FileNodeViewModel;
    }
}