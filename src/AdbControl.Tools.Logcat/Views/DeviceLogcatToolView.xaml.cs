using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using AdbControl.Shell.Behaviors;

namespace AdbControl.Tools.Logcat.Views;

public partial class DeviceLogcatToolView : UserControl
{
    /// <summary>Насколько близко к низу считается «пользователь смотрит хвост».</summary>
    private const double BottomTolerance = 4.0;

    private ScrollViewer? _scrollViewer;
    private INotifyCollectionChanged? _lines;
    private bool _followTail = true;
    private bool _isAutoScrolling;

    public DeviceLogcatToolView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Attach();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Detach();
    }

    private void Attach()
    {
        if (_lines is null && LogOutputList.ItemsSource is INotifyCollectionChanged lines)
        {
            _lines = lines;
            _lines.CollectionChanged += OnLinesChanged;
        }

        if (_scrollViewer is not null)
        {
            return;
        }

        // Прокрутка живёт внутри шаблона списка и появляется только после его построения.
        LogOutputList.ApplyTemplate();
        _scrollViewer = VisualTreeSearch.FindDescendant<ScrollViewer>(LogOutputList);

        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged += OnScrollChanged;
        }
    }

    private void Detach()
    {
        if (_lines is not null)
        {
            _lines.CollectionChanged -= OnLinesChanged;
            _lines = null;
        }

        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer = null;
        }
    }

    /// <summary>
    /// Поведение консоли: пока пользователь у самого низа, список едет за хвостом;
    /// стоит прокрутить вверх — новые строки больше не утаскивают вид.
    /// </summary>
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_scrollViewer is null || _isAutoScrolling)
        {
            return;
        }

        // Изменение высоты содержимого — это приход новых строк, а не действие мышью.
        if (Math.Abs(e.ExtentHeightChange) > 0.01)
        {
            return;
        }

        _followTail = _scrollViewer.VerticalOffset >= _scrollViewer.ScrollableHeight - BottomTolerance;
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_scrollViewer is null)
        {
            Attach();
        }

        if (!_followTail || _scrollViewer is null)
        {
            return;
        }

        _isAutoScrolling = true;
        try
        {
            _scrollViewer.ScrollToEnd();
        }
        finally
        {
            _isAutoScrolling = false;
        }
    }

}
