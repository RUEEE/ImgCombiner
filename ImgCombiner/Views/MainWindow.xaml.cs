using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ImgCombiner.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        // 注册内置转换器资源（简化 XAML）
        InitializeComponent();

    }

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        // 将选中项交给 VM（避免在 TreeView 上做复杂绑定）
        if (DataContext is ViewModels.MainViewModel vm)
            vm.Convert.SelectedNode = e.NewValue as ViewModels.FileNodeViewModel;
    }
}