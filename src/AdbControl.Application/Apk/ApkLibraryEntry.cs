namespace AdbControl.Application.Apk;

public sealed record ApkLibraryEntry(
    Guid Id,
    string DisplayName,
    string StoredFileName,
    string FilePath,
    long FileSizeBytes,
    DateTimeOffset ImportedAt);
