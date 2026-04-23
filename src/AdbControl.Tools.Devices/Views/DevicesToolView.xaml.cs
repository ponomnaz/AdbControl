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

        ClearSelectionsIfNeeded(e.OriginalSource as DependencyObject);
        viewModel.CancelActiveAliasEdit();
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

    private void ClearSelectionsIfNeeded(DependencyObject? source)
    {
        if (source is null || ShouldKeepSelection(source))
        {
            return;
        }

        foreach (var listBox in FindVisualChildren<ListBox>(this))
        {
            if (listBox.SelectedItems.Count > 0)
            {
                listBox.UnselectAll();
            }
        }
    }

    private bool ShouldKeepSelection(DependencyObject source)
    {
        for (var current = source; current is not null && !ReferenceEquals(current, this); current = GetParentElement(current))
        {
            if (current is ListBoxItem or ScrollBar or ButtonBase or TextBoxBase or Selector or TabItem)
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

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        var childrenCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childrenCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T target)
            {
                yield return target;
            }

            foreach (var nested in FindVisualChildren<T>(child))
            {
                yield return nested;
            }
        }
    }
}
