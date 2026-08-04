using AdbControl.Application.Common;

namespace AdbControl.Tools.Gallery.ViewModels;

/// <summary>Строка списка: папка или снимок.</summary>
public sealed class GalleryEntryViewModel : ObservableObject
{
    private string _displayName;
    private string _editName = string.Empty;
    private bool _isEditing;

    public GalleryEntryViewModel(
        string fullPath,
        string displayName,
        bool isFolder,
        DateTimeOffset timestamp,
        long sizeBytes,
        string fileName)
    {
        FullPath = fullPath;
        _displayName = displayName;
        IsFolder = isFolder;
        Timestamp = timestamp;
        SizeBytes = sizeBytes;
        FileName = fileName;
    }

    public string FullPath { get; }

    public bool IsFolder { get; }

    public DateTimeOffset Timestamp { get; }

    public long SizeBytes { get; }

    /// <summary>Имя файла на диске — оно же показывается в свойствах.</summary>
    public string FileName { get; }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    /// <summary>Строка переименовывается по месту — как имена устройств.</summary>
    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    public string EditName
    {
        get => _editName;
        set => SetProperty(ref _editName, value);
    }

    /// <summary>Дата съёмки в узкой колонке: сегодняшние — только временем.</summary>
    public string TimestampText => IsFolder
        ? string.Empty
        : Timestamp.Date == DateTimeOffset.Now.Date
            ? Timestamp.ToString("HH:mm:ss")
            : Timestamp.ToString("dd.MM HH:mm");

    public string SizeText => IsFolder ? string.Empty : FormatSize(SizeBytes);

    public static string FormatSize(long bytes)
    {
        return bytes >= 1024 * 1024
            ? $"{bytes / 1024d / 1024d:F1} МБ"
            : $"{bytes / 1024d:F0} КБ";
    }
}
