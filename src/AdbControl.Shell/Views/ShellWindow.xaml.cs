using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AdbControl.Shell.ViewModels;

namespace AdbControl.Shell.Views;

public partial class ShellWindow
{
    public ShellWindow()
    {
        InitializeComponent();
    }

    private void OnNavigationItemMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToolNavigationItemViewModel navigationItem } ||
            DataContext is not ShellViewModel shellViewModel)
        {
            return;
        }

        var detachedTool = shellViewModel.CreateDetachedToolContent(navigationItem.Id);
        var window = new DetachedToolWindow
        {
            Owner = this,
            Title = detachedTool.Registration.Tool.Title,
            DataContext = detachedTool.ContentViewModel,
            Icon = Icon
        };

        window.Show();
        e.Handled = true;
    }
}
