using System.Text.Json;
using AdbControl.Application.Apk;

namespace AdbControl.Infrastructure.Persistence;

public sealed class LocalApkLibraryService : IApkLibraryService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _metadataFilePath;
    private readonly string _storageDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LocalApkLibraryService(AppDataPaths paths)
    {
        _metadataFilePath = Path.Combine(paths.SettingsDirectory, "apk-library.json");
        _storageDirectory = paths.ApksDirectory;
    }

    public async Task<IReadOnlyList<ApkLibraryEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var records = await ReadRecordsUnsafeAsync(cancellationToken);
            return records
                .Select(BuildEntry)
                .Where(static entry => File.Exists(entry.FilePath))
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ApkImportResult> ImportAsync(IReadOnlyList<string> sourcePaths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_storageDirectory);

            var records = (await ReadRecordsUnsafeAsync(cancellationToken)).ToList();
            var importedEntries = new List<ApkLibraryEntry>();
            var skippedPaths = new List<string>();

            foreach (var sourcePath in sourcePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsSupportedApkPath(sourcePath))
                {
                    skippedPaths.Add(sourcePath);
                    continue;
                }

                var sourceFile = new FileInfo(sourcePath);
                if (!sourceFile.Exists)
                {
                    skippedPaths.Add(sourcePath);
                    continue;
                }

                var id = Guid.NewGuid();
                var storedFileName = $"{id:N}{sourceFile.Extension}";
                var destinationPath = Path.Combine(_storageDirectory, storedFileName);
                File.Copy(sourceFile.FullName, destinationPath, overwrite: false);

                var record = new ApkLibraryRecord(
                    id,
                    sourceFile.Name,
                    storedFileName,
                    sourceFile.Length,
                    DateTimeOffset.Now);

                records.Add(record);
                importedEntries.Add(BuildEntry(record));
            }

            await WriteRecordsUnsafeAsync(records, cancellationToken);
            return new ApkImportResult(importedEntries, skippedPaths);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> DeleteAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryIds);

        if (entryIds.Count == 0)
        {
            return 0;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var idSet = entryIds.ToHashSet();
            var records = (await ReadRecordsUnsafeAsync(cancellationToken)).ToList();
            var removedRecords = records.Where(record => idSet.Contains(record.Id)).ToArray();

            if (removedRecords.Length == 0)
            {
                return 0;
            }

            foreach (var record in removedRecords)
            {
                var filePath = Path.Combine(_storageDirectory, record.StoredFileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }

            records.RemoveAll(record => idSet.Contains(record.Id));
            await WriteRecordsUnsafeAsync(records, cancellationToken);
            return removedRecords.Length;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<ApkLibraryRecord>> ReadRecordsUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_metadataFilePath))
        {
            return [];
        }

        await using var stream = new FileStream(_metadataFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        try
        {
            var records = await JsonSerializer.DeserializeAsync<List<ApkLibraryRecord>>(stream, SerializerOptions, cancellationToken);
            if (records is null)
            {
                return [];
            }

            return records
                .Where(static record => record.Id != Guid.Empty &&
                                        !string.IsNullOrWhiteSpace(record.DisplayName) &&
                                        !string.IsNullOrWhiteSpace(record.StoredFileName))
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task WriteRecordsUnsafeAsync(IReadOnlyList<ApkLibraryRecord> records, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_metadataFilePath)!);
        Directory.CreateDirectory(_storageDirectory);

        await using var stream = new FileStream(_metadataFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(stream, records, SerializerOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private ApkLibraryEntry BuildEntry(ApkLibraryRecord record)
    {
        return new ApkLibraryEntry(
            record.Id,
            record.DisplayName,
            record.StoredFileName,
            Path.Combine(_storageDirectory, record.StoredFileName),
            record.FileSizeBytes,
            record.ImportedAt);
    }

    private static bool IsSupportedApkPath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               string.Equals(Path.GetExtension(path), ".apk", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ApkLibraryRecord(
        Guid Id,
        string DisplayName,
        string StoredFileName,
        long FileSizeBytes,
        DateTimeOffset ImportedAt);
}
