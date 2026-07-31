using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AdbControl.Tools.Devices.ViewModels;

namespace AdbControl.Tools.Devices.Views;

public partial class DeviceConnectToolView
{
    public DeviceConnectToolView()
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

    private void OnAliasEditorIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.Visibility != Visibility.Visible)
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
            textBox.DataContext is not DiscoveredDeviceItemViewModel row ||
            !row.IsEditingAlias ||
            DataContext is not DeviceConnectToolViewModel viewModel)
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
            textBox.DataContext is not DiscoveredDeviceItemViewModel row ||
            DataContext is not DeviceConnectToolViewModel viewModel)
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

    private void OnPairingCodeEditorLoaded(object sender, RoutedEventArgs e)
    {
        FocusEditor(sender);
    }

    private void OnPairingCodeEditorIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { Visibility: Visibility.Visible })
        {
            FocusEditor(sender);
        }
    }

    private void OnPairingCodeEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox ||
            textBox.DataContext is not DiscoveredDeviceItemViewModel row ||
            DataContext is not DeviceConnectToolViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (viewModel.SubmitPairingCommand.CanExecute(row))
            {
                viewModel.SubmitPairingCommand.Execute(row);
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (viewModel.CancelPairingCommand.CanExecute(row))
            {
                viewModel.CancelPairingCommand.Execute(row);
            }

            e.Handled = true;
        }
    }

    private static void FocusEditor(object sender)
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

    private void OnRenameMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement { DataContext: DiscoveredDeviceItemViewModel row } } } ||
            DataContext is not DeviceConnectToolViewModel viewModel)
        {
            return;
        }

        if (viewModel.BeginEditAliasCommand.CanExecute(row))
        {
            viewModel.BeginEditAliasCommand.Execute(row);
        }
    }
}
