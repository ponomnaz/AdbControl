namespace AdbControl.Application.Apk;

public sealed record ApkImportResult(
    IReadOnlyList<ApkLibraryEntry> ImportedEntries,
    IReadOnlyList<string> SkippedPaths);
