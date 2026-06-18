using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AdbControl.Shell.ViewModels;

namespace AdbControl.Shell.Views;

public partial class ShellWindow
{
    private DispatcherTimer? _pendingOpenTimer;

    public ShellWindow()
    {
        InitializeComponent();
    }

    [DllImport("user32.dll")]
    private static extern int GetDoubleClickTime();

    private void OnNavigationItemPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToolNavigationItemViewModel navigationItem } ||
            DataContext is not ShellViewModel shellViewModel)
        {
            return;
        }

        // Handle the click ourselves so a double-click doesn't also trigger
        // single-click navigation in the main Shell.
        e.Handled = true;
        CancelPendingOpen();

        if (e.ClickCount >= 2)
        {
            OpenDetachedWindow(shellViewModel, navigationItem);
            return;
        }

        // Defer single-click navigation until we're sure a second click isn't coming.
        _pendingOpenTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime())
        };
        _pendingOpenTimer.Tick += (_, _) =>
        {
            CancelPendingOpen();
            navigationItem.OpenCommand.Execute(null);
        };
        _pendingOpenTimer.Start();
    }

    private void CancelPendingOpen()
    {
        _pendingOpenTimer?.Stop();
        _pendingOpenTimer = null;
    }

    private void OpenDetachedWindow(ShellViewModel shellViewModel, ToolNavigationItemViewModel navigationItem)
    {
        var detachedTool = shellViewModel.CreateDetachedToolContent(navigationItem.Id);
        var window = new DetachedToolWindow
        {
            Title = detachedTool.Registration.Tool.Title,
            DataContext = detachedTool.ContentViewModel,
            Icon = Icon
        };

        PositionDetachedWindow(window);
        window.Show();
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
