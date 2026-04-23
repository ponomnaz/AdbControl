using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AdbControl.Tools.Logcat.Views;

public partial class DeviceLogcatToolView : UserControl
{
    private const double BottomTolerance = 1.5;
    private const double ScrollDeltaTolerance = 0.01;

    private ScrollViewer? _scrollViewer;
    private bool _followTail = true;
    private bool _isSyncingViewport;
    private bool _isViewportSyncScheduled;
    private double _manualVerticalOffset;

    public DeviceLogcatToolView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        AttachScrollViewer();
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        DetachScrollViewer();
    }

    private void OnLogOutputTextChanged(object sender, TextChangedEventArgs e)
    {
        ScheduleViewportSync();
    }

    private void AttachScrollViewer()
    {
        if (_scrollViewer is not null)
        {
            return;
        }

        _scrollViewer = FindVisualChild<ScrollViewer>(LogOutputTextBox);
        if (_scrollViewer is not null)
        {
            _followTail = IsAtBottom(_scrollViewer);
            _manualVerticalOffset = _scrollViewer.VerticalOffset;
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
        if (_scrollViewer is null || _isSyncingViewport)
        {
            return;
        }

        if (Math.Abs(e.VerticalChange) < ScrollDeltaTolerance &&
            Math.Abs(e.ExtentHeightChange) < ScrollDeltaTolerance)
        {
            return;
        }

        if (Math.Abs(e.ExtentHeightChange) < ScrollDeltaTolerance)
        {
            _followTail = IsAtBottom(_scrollViewer);
            if (!_followTail)
            {
                _manualVerticalOffset = _scrollViewer.VerticalOffset;
            }

            return;
        }

        if (_followTail)
        {
            ScheduleViewportSync();
            return;
        }

        _manualVerticalOffset = Math.Min(_manualVerticalOffset, _scrollViewer.ScrollableHeight);
        ScheduleViewportSync();
    }

    private void ScheduleViewportSync()
    {
        if (_scrollViewer is null || _isViewportSyncScheduled)
        {
            return;
        }

        _isViewportSyncScheduled = true;

        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() =>
            {
                _isViewportSyncScheduled = false;

                if (_scrollViewer is null)
                {
                    return;
                }

                _isSyncingViewport = true;
                try
                {
                    if (_followTail)
                    {
                        _scrollViewer.ScrollToVerticalOffset(_scrollViewer.ScrollableHeight);
                        return;
                    }

                    var targetOffset = Math.Min(_manualVerticalOffset, _scrollViewer.ScrollableHeight);
                    _scrollViewer.ScrollToVerticalOffset(targetOffset);
                }
                finally
                {
                    _isSyncingViewport = false;
                }
            }));
    }

    private static bool IsAtBottom(ScrollViewer scrollViewer)
    {
        return scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - BottomTolerance;
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
