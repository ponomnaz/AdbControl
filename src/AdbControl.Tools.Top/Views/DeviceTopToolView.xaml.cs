using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AdbControl.Tools.Top.ViewModels;

namespace AdbControl.Tools.Top.Views;

public partial class DeviceTopToolView : UserControl
{
    private DeviceTopToolViewModel? _viewModel;

    public DeviceTopToolView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Detach();

        if (DataContext is DeviceTopToolViewModel viewModel)
        {
            _viewModel = viewModel;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        RebuildColumns();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Detach();
    }

    private void Detach()
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeviceTopToolViewModel.Columns))
        {
            RebuildColumns();
        }
    }

    /// <summary>
    /// Колонки строятся по тому, что отдало устройство: набор различается между
    /// прошивками, поэтому задать их в разметке заранее нельзя.
    /// </summary>
    private void RebuildColumns()
    {
        ProcessGrid.Columns.Clear();

        if (_viewModel is null)
        {
            return;
        }

        for (var index = 0; index < _viewModel.Columns.Count; index++)
        {
            ProcessGrid.Columns.Add(new GridViewColumn
            {
                Header = _viewModel.Columns[index],
                DisplayMemberBinding = new Binding($"Values[{index}]")
            });
        }
    }
}
