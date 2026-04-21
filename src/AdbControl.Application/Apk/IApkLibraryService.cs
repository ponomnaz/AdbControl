namespace AdbControl.Application.Apk;

public interface IApkLibraryService
{
    Task<IReadOnlyList<ApkLibraryEntry>> GetEntriesAsync(CancellationToken cancellationToken = default);

    Task<ApkImportResult> ImportAsync(IReadOnlyList<string> sourcePaths, CancellationToken cancellationToken = default);

    Task<int> DeleteAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken = default);
}
