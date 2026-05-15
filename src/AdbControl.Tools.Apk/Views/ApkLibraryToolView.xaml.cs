using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

}
