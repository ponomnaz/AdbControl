using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AdbControl.Shell.Behaviors;
using AdbControl.Tools.Console.ViewModels;

namespace AdbControl.Tools.Console.Views;

public partial class AdbConsoleToolView : UserControl
{
    private const double BottomTolerance = 1.5;
    private const double ScrollDeltaTolerance = 0.01;

    private ScrollViewer? _scrollViewer;
    private bool _followTail = true;
    private bool _isSyncingViewport;
    private bool _isViewportSyncScheduled;
    private double _manualVerticalOffset;

    public AdbConsoleToolView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachScrollViewer();
        CommandInputTextBox.Focus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachScrollViewer();
    }

    private void OnOutputTextChanged(object sender, TextChangedEventArgs e)
    {
        ScheduleViewportSync();
    }

    private void OnCommandInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not AdbConsoleToolViewModel viewModel)
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Enter)
        {
            if (viewModel.ExecuteCommand.CanExecute(null))
            {
                viewModel.ExecuteCommand.Execute(null);
            }

            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Up)
        {
            if (viewModel.TryRecallPreviousCommand())
            {
                CommandInputTextBox.CaretIndex = CommandInputTextBox.Text.Length;
            }

            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Down)
        {
            if (viewModel.TryRecallNextCommand())
            {
                CommandInputTextBox.CaretIndex = CommandInputTextBox.Text.Length;
            }

            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L)
        {
            if (viewModel.ClearOutputCommand.CanExecute(null))
            {
                viewModel.ClearOutputCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    private void AttachScrollViewer()
    {
        if (_scrollViewer is not null)
        {
            return;
        }

        _scrollViewer = VisualTreeSearch.FindDescendant<ScrollViewer>(OutputTextBox);
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

}
