using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using AdbControl.Tools.Remote.ViewModels;

namespace AdbControl.Tools.Remote.Views;

public partial class RemoteView : UserControl
{
    /// <summary>Сколько держать кнопку, прежде чем нажатия пойдут повторами.</summary>
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(450);

    /// <summary>Шаг повтора. Реже, чем у клавиатуры: каждое нажатие — команда телевизору.</summary>
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(160);

    private readonly DispatcherTimer _repeatTimer;
    private RemoteViewModel? _viewModel;
    private Button? _heldButton;

    public RemoteView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        ScrcpyHost.WindowAttached += OnWindowAttached;
        Unloaded += OnUnloaded;

        _repeatTimer = new DispatcherTimer { Interval = RepeatDelay };
        _repeatTimer.Tick += OnRepeatTick;
    }

    // ── удержание кнопки ─────────────────────────────────────────────────

    private void OnRemoteButtonPressed(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        // Само нажатие уже отправит сам Button: у него ClickMode=Press.
        _heldButton = button;
        _repeatTimer.Interval = RepeatDelay;
        _repeatTimer.Start();
    }

    private void OnRemoteButtonReleased(object sender, MouseEventArgs e)
    {
        StopRepeat();
    }

    private void OnRepeatTick(object? sender, EventArgs e)
    {
        // Кнопку могли отпустить за пределами окна — событие отпускания тогда не придёт.
        if (_heldButton is null || Mouse.LeftButton != MouseButtonState.Pressed)
        {
            StopRepeat();
            return;
        }

        _repeatTimer.Interval = RepeatInterval;

        if (_heldButton.Command is { } command && command.CanExecute(_heldButton.CommandParameter))
        {
            command.Execute(_heldButton.CommandParameter);
        }
    }

    private void StopRepeat()
    {
        _repeatTimer.Stop();
        _heldButton = null;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        // DataContextChanged with null fires before Unloaded when switching tabs.
        // Stop mirroring here while we still have the VM reference.
        if (e.NewValue is null)
        {
            var leaving = _viewModel;
            _viewModel = null;
            StopMirroring(leaving);
            return;
        }

        _viewModel = e.NewValue as RemoteViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(RemoteViewModel.IsMirroring) or nameof(RemoteViewModel.TargetId)))
        {
            return;
        }

        if (_viewModel is null)
        {
            return;
        }

        if (_viewModel.IsMirroring && _viewModel.TargetId is not null)
        {
            // ScrcpyHost stays Hidden so WPF loading panel is visible (no airspace conflict).
            ScrcpyHost.Visibility = Visibility.Hidden;
            LoadingPanel.Visibility = Visibility.Visible;
            ScrcpyHost.StartScrcpy(_viewModel.TargetId);
        }
        else
        {
            ScrcpyHost.Visibility = Visibility.Hidden;
            LoadingPanel.Visibility = Visibility.Collapsed;
            ScrcpyHost.StopScrcpy();
        }
    }

    private void OnWindowAttached(object? sender, EventArgs e)
    {
        // Scrcpy is embedded — switch from WPF loading panel to the live Win32 window.
        LoadingPanel.Visibility = Visibility.Collapsed;
        ScrcpyHost.Visibility = Visibility.Visible;
        _viewModel?.NotifyWindowAttached();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopRepeat();

        // Fallback in case DataContextChanged didn't fire first.
        StopMirroring(_viewModel);
    }

    private void StopMirroring(RemoteViewModel? vm)
    {
        ScrcpyHost.StopScrcpy();
        ScrcpyHost.Visibility = Visibility.Hidden;
        LoadingPanel.Visibility = Visibility.Collapsed;
        vm?.StopMirroringCommand.Execute(null);
    }
}

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility.Collapsed;
    }
}
