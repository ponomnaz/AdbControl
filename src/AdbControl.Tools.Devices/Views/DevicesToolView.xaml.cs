using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AdbControl.Tools.Devices.ViewModels;

namespace AdbControl.Tools.Devices.Views;

public partial class DevicesToolView
{
    public DevicesToolView()
    {
        InitializeComponent();
    }

    private void OnAliasEditorLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        textBox.Dispatcher.BeginInvoke(() =>
        {
            textBox.Focus();
            textBox.SelectAll();
        });
    }

    private void OnAliasEditorLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox ||
            textBox.DataContext is not KnownDeviceRowViewModel row ||
            !row.IsEditingAlias ||
            DataContext is not DevicesToolViewModel viewModel)
        {
            return;
        }

        if (viewModel.CancelAliasCommand.CanExecute(row))
        {
            viewModel.CancelAliasCommand.Execute(row);
        }
    }

    private void OnAliasEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox ||
            textBox.DataContext is not KnownDeviceRowViewModel row ||
            DataContext is not DevicesToolViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (viewModel.SaveAliasCommand.CanExecute(row))
            {
                viewModel.SaveAliasCommand.Execute(row);
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (viewModel.CancelAliasCommand.CanExecute(row))
            {
                viewModel.CancelAliasCommand.Execute(row);
            }

            e.Handled = true;
        }
    }

    private void OnRootPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not DevicesToolViewModel viewModel)
        {
            return;
        }

        if (IsInsideAliasEditor(e.OriginalSource as DependencyObject))
        {
            return;
        }

        viewModel.CancelActiveAliasEdit();
    }

    private static bool IsInsideAliasEditor(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TextBox { DataContext: KnownDeviceRowViewModel { IsEditingAlias: true } })
            {
                return true;
            }
        }

        return false;
    }
}
