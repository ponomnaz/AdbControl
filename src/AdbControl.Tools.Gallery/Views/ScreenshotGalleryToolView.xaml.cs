using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AdbControl.Tools.Gallery.ViewModels;

namespace AdbControl.Tools.Gallery.Views;

public partial class ScreenshotGalleryToolView : UserControl
{
    private const double MinZoom = 0.1;
    private const double MaxZoom = 8.0;
    private const double ZoomStep = 1.15;

    private ScreenshotGalleryToolViewModel? _viewModel;

    /// <summary>Ноль — «вписать в панель», иначе множитель к натуральному размеру.</summary>
    private double _zoom;

    private Point _panOrigin;
    private bool _isPanning;

    public ScreenshotGalleryToolView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as ScreenshotGalleryToolViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        ApplyFit();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Новый снимок открывается вписанным: масштаб от предыдущего к нему отношения не имеет.
        if (e.PropertyName == nameof(ScreenshotGalleryToolViewModel.Preview))
        {
            ApplyFit();
        }
    }

    private void OnEntryDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Двойной щелчок по пустому месту списка ничего не открывает.
        if (ItemsControl.ContainerFromElement(EntryList, (DependencyObject)e.OriginalSource) is not ListBoxItem)
        {
            return;
        }

        _viewModel?.ActivateSelected();
    }

    private void OnPreviewWheel(object sender, MouseWheelEventArgs e)
    {
        if (_viewModel?.Preview is null)
        {
            return;
        }

        var anchor = e.GetPosition(PreviewImage);
        var previous = _zoom > 0 ? _zoom : CalculateFitZoom();
        var next = Math.Clamp(
            e.Delta > 0 ? previous * ZoomStep : previous / ZoomStep,
            MinZoom,
            MaxZoom);

        ApplyZoom(next);

        // Точка под курсором должна остаться на месте, иначе картинка «убегает».
        PreviewScroll.UpdateLayout();
        var scale = next / previous;
        PreviewScroll.ScrollToHorizontalOffset(PreviewScroll.HorizontalOffset + (anchor.X * (scale - 1)));
        PreviewScroll.ScrollToVerticalOffset(PreviewScroll.VerticalOffset + (anchor.Y * (scale - 1)));

        e.Handled = true;
    }

    private void OnPreviewDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel?.Preview is null)
        {
            return;
        }

        if (_zoom > 0)
        {
            ApplyFit();
        }
        else
        {
            ApplyZoom(1.0);
        }

        e.Handled = true;
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Тащить есть смысл только когда картинка не помещается целиком.
        if (_zoom <= 0 || PreviewScroll.ScrollableWidth <= 0 && PreviewScroll.ScrollableHeight <= 0)
        {
            return;
        }

        _panOrigin = e.GetPosition(PreviewScroll);
        _isPanning = PreviewScroll.CaptureMouse();
        PreviewScroll.Cursor = Cursors.SizeAll;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            StopPanning();
            return;
        }

        var current = e.GetPosition(PreviewScroll);
        PreviewScroll.ScrollToHorizontalOffset(PreviewScroll.HorizontalOffset - (current.X - _panOrigin.X));
        PreviewScroll.ScrollToVerticalOffset(PreviewScroll.VerticalOffset - (current.Y - _panOrigin.Y));
        _panOrigin = current;
    }

    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        StopPanning();
    }

    private void StopPanning()
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        PreviewScroll.ReleaseMouseCapture();
        PreviewScroll.Cursor = Cursors.Arrow;
    }

    /// <summary>Вписывание: размер задаёт панель, полосы прокрутки не нужны.</summary>
    private void ApplyFit()
    {
        _zoom = 0;

        PreviewImage.Width = double.NaN;
        PreviewImage.Height = double.NaN;
        PreviewImage.Stretch = System.Windows.Media.Stretch.Uniform;
        PreviewImage.StretchDirection = StretchDirection.DownOnly;

        PreviewScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        PreviewScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;

        UpdateZoomText();
    }

    private void ApplyZoom(double zoom)
    {
        if (_viewModel?.Preview is not { } image)
        {
            return;
        }

        _zoom = zoom;

        PreviewImage.Stretch = System.Windows.Media.Stretch.Uniform;
        PreviewImage.StretchDirection = StretchDirection.Both;
        PreviewImage.Width = image.PixelWidth * zoom;
        PreviewImage.Height = image.PixelHeight * zoom;

        PreviewScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        PreviewScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

        UpdateZoomText();
    }

    /// <summary>Какому множителю соответствует текущее вписывание — от него считается первый шаг колеса.</summary>
    private double CalculateFitZoom()
    {
        if (_viewModel?.Preview is not { } image || image.PixelWidth == 0 || image.PixelHeight == 0)
        {
            return 1.0;
        }

        var available = new Size(
            Math.Max(1, PreviewScroll.ViewportWidth - PreviewImage.Margin.Left - PreviewImage.Margin.Right),
            Math.Max(1, PreviewScroll.ViewportHeight - PreviewImage.Margin.Top - PreviewImage.Margin.Bottom));

        // Вписывание не увеличивает: мелкий снимок показан один к одному.
        return Math.Min(1.0, Math.Min(available.Width / image.PixelWidth, available.Height / image.PixelHeight));
    }

    private void UpdateZoomText()
    {
        if (_viewModel?.Preview is null)
        {
            ZoomText.Text = string.Empty;
            return;
        }

        var effective = _zoom > 0 ? _zoom : CalculateFitZoom();
        ZoomText.Text = _zoom > 0
            ? $"{effective * 100:F0}%"
            : $"вписано · {effective * 100:F0}%";
    }
}
