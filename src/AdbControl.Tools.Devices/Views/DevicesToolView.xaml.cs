using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
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

    private void OnRenameMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement { DataContext: KnownDeviceRowViewModel row } } } ||
            DataContext is not DevicesToolViewModel viewModel)
        {
            return;
        }

        if (viewModel.BeginEditAliasCommand.CanExecute(row))
        {
            viewModel.BeginEditAliasCommand.Execute(row);
        }
    }

    private static bool IsInsideAliasEditor(DependencyObject? source)
    {
        for (var current = source; current is not null; current = GetParentElement(current))
        {
            if (current is TextBox { DataContext: KnownDeviceRowViewModel { IsEditingAlias: true } })
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? GetParentElement(DependencyObject current)
    {
        return current switch
        {
            Visual or Visual3D => VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current),
            FrameworkContentElement frameworkContentElement => frameworkContentElement.Parent ?? LogicalTreeHelper.GetParent(frameworkContentElement),
            ContentElement contentElement => ContentOperations.GetParent(contentElement) ?? LogicalTreeHelper.GetParent(contentElement),
            _ => LogicalTreeHelper.GetParent(current)
        };
    }

}
