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
            Title = detachedTool.Registration.Tool.Title,
            DataContext = detachedTool.ContentViewModel,
            Icon = Icon
        };

        PositionDetachedWindow(window);
        window.Show();
        e.Handled = true;
    }

    private void PositionDetachedWindow(Window window)
    {
        var sourceLeft = Left;
        var sourceTop = Top;
        var sourceWidth = ActualWidth > 0 ? ActualWidth : Width;
        var sourceHeight = ActualHeight > 0 ? ActualHeight : Height;

        var targetWidth = window.Width;
        var targetHeight = window.Height;

        window.Left = Math.Max(0, sourceLeft + ((sourceWidth - targetWidth) / 2d) + 28d);
        window.Top = Math.Max(0, sourceTop + ((sourceHeight - targetHeight) / 2d) + 28d);
    }
}
