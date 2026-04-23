using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AdbControl.Tools.Logcat.ViewModels;

namespace AdbControl.Tools.Logcat.Views;

public partial class DeviceLogcatToolView : UserControl
{
    private DeviceLogcatToolViewModel? _viewModel;
    private ScrollViewer? _scrollViewer;
    private bool _followTail = true;

    public DeviceLogcatToolView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        AttachScrollViewer();
        SyncViewModel(DataContext as DeviceLogcatToolViewModel);
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        DetachScrollViewer();
        SyncViewModel(null);
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        SyncViewModel(e.NewValue as DeviceLogcatToolViewModel);
    }

    private void SyncViewModel(DeviceLogcatToolViewModel? nextViewModel)
    {
        if (ReferenceEquals(_viewModel, nextViewModel))
        {
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.VisibleLines.CollectionChanged -= OnVisibleLinesChanged;
        }

        _viewModel = nextViewModel;

        if (_viewModel is not null)
        {
            _viewModel.VisibleLines.CollectionChanged += OnVisibleLinesChanged;
        }
    }

    private void OnVisibleLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_followTail || _scrollViewer is null)
        {
            return;
        }

        Dispatcher.BeginInvoke(() => _scrollViewer?.ScrollToEnd());
    }

    private void AttachScrollViewer()
    {
        if (_scrollViewer is not null)
        {
            return;
        }

        _scrollViewer = FindVisualChild<ScrollViewer>(LogLinesListBox);
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged += OnScrollViewerScrollChanged;
        }
    }

    private void DetachScrollViewer()
    {
        if (_scrollViewer is null)
        {
            return;
        }

        _scrollViewer.ScrollChanged -= OnScrollViewerScrollChanged;
        _scrollViewer = null;
    }

    private void OnScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        _followTail = e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - 2;
    }

    private static T? FindVisualChild<T>(DependencyObject? root)
        where T : DependencyObject
    {
        if (root is null)
        {
            return null;
        }

        var childrenCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childrenCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
