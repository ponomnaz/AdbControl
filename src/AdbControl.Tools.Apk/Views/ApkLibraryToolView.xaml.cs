using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Microsoft.Win32;
using AdbControl.Tools.Apk.ViewModels;

namespace AdbControl.Tools.Apk.Views;

public partial class ApkLibraryToolView : UserControl
{
    public ApkLibraryToolView()
    {
        InitializeComponent();
    }

    private async void OnChooseFilesClick(object sender, RoutedEventArgs e)
    {
        await ChooseFilesAsync();
    }

    private async void OnDropZoneClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        await ChooseFilesAsync();
    }

    private async Task ChooseFilesAsync()
    {
        if (DataContext is not ApkLibraryToolViewModel viewModel)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Загрузить APK",
            Filter = "APK (*.apk)|*.apk",
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            await viewModel.ImportFilesAsync(dialog.FileNames);
        }
    }

    private void OnRootPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        ClearSelectionsIfNeeded(e.OriginalSource as DependencyObject);
    }

    private void OnDropZoneDragEnter(object sender, DragEventArgs e)
    {
        UpdateDropState(e);
    }

    private void OnDropZoneDragOver(object sender, DragEventArgs e)
    {
        UpdateDropState(e);
    }

    private void OnDropZoneDragLeave(object sender, DragEventArgs e)
    {
        if (DataContext is ApkLibraryToolViewModel viewModel)
        {
            viewModel.IsDropActive = false;
        }
    }

    private async void OnDropZoneDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not ApkLibraryToolViewModel viewModel)
        {
            return;
        }

        viewModel.IsDropActive = false;

        if (!TryGetDroppedApkFiles(e, out var filePaths))
        {
            return;
        }

        await viewModel.ImportFilesAsync(filePaths);
    }

    private void UpdateDropState(DragEventArgs e)
    {
        if (DataContext is not ApkLibraryToolViewModel viewModel)
        {
            return;
        }

        var isValid = TryGetDroppedApkFiles(e, out _);
        e.Effects = isValid ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        viewModel.IsDropActive = isValid;
    }

    private static bool TryGetDroppedApkFiles(DragEventArgs e, out string[] filePaths)
    {
        filePaths = [];

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
        {
            return false;
        }

        filePaths = files
            .Where(static file => string.Equals(Path.GetExtension(file), ".apk", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return filePaths.Length > 0;
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
