using System.ComponentModel;
using System.Diagnostics;
using AdbControl.Application.Devices;
using AdbControl.Application.Diagnostics;
using AdbControl.Core.Devices;
using AdbControl.Infrastructure.Persistence;

namespace AdbControl.Infrastructure.Devices;

public sealed class AdbScreenshotService : IDeviceScreenshotService
{
    /// <summary>Заголовок PNG. Поток от устройства не всегда начинается с него — см. <see cref="FindPngStart"/>.</summary>
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly AppDataPaths _paths;
    private readonly CommandTraceJournal _commandTraceJournal;

    public AdbScreenshotService(AppDataPaths paths, CommandTraceJournal commandTraceJournal)
    {
        _paths = paths;
        _commandTraceJournal = commandTraceJournal;
    }

    public string ScreenshotsDirectory => Path.Combine(_paths.ExportsDirectory, "screenshots");

    public async Task<ScreenshotBatchResult> CaptureAsync(
        IReadOnlyList<TvDeviceProfile> devices,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ScreenshotsDirectory);

        var filePaths = new List<string>();

        foreach (var device in devices)
        {
            if (AdbTarget.Resolve(device) is not { } target)
            {
                continue;
            }

            var filePath = await CaptureOneAsync(device, target, cancellationToken);
            if (filePath is not null)
            {
                filePaths.Add(filePath);
            }
        }

        return new ScreenshotBatchResult(
            devices.Count,
            filePaths.Count,
            devices.Count - filePaths.Count,
            filePaths);
    }

    private async Task<string?> CaptureOneAsync(
        TvDeviceProfile device,
        string target,
        CancellationToken cancellationToken)
    {
        var arguments = $"-s {target} exec-out screencap -p";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "adb",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            try
            {
                process.Start();
            }
            catch (Win32Exception)
            {
                await RecordAsync(arguments, "adb.exe не найден. Добавь platform-tools в PATH.", true, cancellationToken);
                return null;
            }

            // Читаем именно BaseStream: StandardOutput декодирует текст и портит PNG.
            using var buffer = new MemoryStream();
            var copyTask = process.StandardOutput.BaseStream.CopyToAsync(buffer, cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await copyTask;
            var errorText = await errorTask;
            await process.WaitForExitAsync(cancellationToken);

            stopwatch.Stop();

            if (process.ExitCode != 0)
            {
                await RecordAsync(arguments, errorText, true, cancellationToken);
                return null;
            }

            var payload = buffer.ToArray();
            var pngStart = FindPngStart(payload);

            if (pngStart < 0)
            {
                await RecordAsync(
                    arguments,
                    $"В ответе нет PNG ({payload.Length} байт). Устройство не отдало снимок экрана.",
                    true,
                    cancellationToken);
                return null;
            }

            var filePath = BuildUniqueFilePath(device.DisplayName, DateTimeOffset.Now);
            await File.WriteAllBytesAsync(
                filePath,
                payload.AsMemory(pngStart),
                cancellationToken);

            var skipped = pngStart > 0
                ? $", отброшено {pngStart} байт мусора перед PNG"
                : string.Empty;

            await RecordAsync(
                arguments,
                $"{payload.Length - pngStart} байт{skipped}\n{filePath}",
                false,
                cancellationToken,
                (int)stopwatch.ElapsedMilliseconds);

            return filePath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RecordAsync(arguments, exception.Message, true, CancellationToken.None);
            return null;
        }
    }

    /// <summary>
    /// Некоторые прошивки печатают в stdout служебные строки перед изображением
    /// (например «Init wrapper sys mutex successful»), поэтому файл начинается не с PNG.
    /// Ищем сигнатуру, а не доверяем началу потока.
    /// </summary>
    private static int FindPngStart(ReadOnlySpan<byte> payload)
    {
        return payload.IndexOf(PngSignature);
    }

    private string BuildUniqueFilePath(string deviceName, DateTimeOffset timestamp)
    {
        var baseName = $"{timestamp:yyyy-MM-dd_HH-mm-ss}_{SanitizeFileName(deviceName)}";
        var filePath = Path.Combine(ScreenshotsDirectory, $"{baseName}.png");

        // Снимок сразу с нескольких устройств попадает в одну секунду — разводим суффиксом.
        var index = 2;
        while (File.Exists(filePath))
        {
            filePath = Path.Combine(ScreenshotsDirectory, $"{baseName}_{index++}.png");
        }

        return filePath;
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(value
            .Select(character => invalidChars.Contains(character) ? '_' : character)
            .ToArray())
            .Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? "device" : cleaned;
    }

    private async Task RecordAsync(
        string arguments,
        string message,
        bool isError,
        CancellationToken cancellationToken,
        int? durationMs = null)
    {
        try
        {
            await _commandTraceJournal.RecordAsync(
                new CommandTraceEntry(
                    Guid.NewGuid(),
                    DateTimeOffset.Now,
                    $"adb {arguments}",
                    isError ? string.Empty : message,
                    isError ? message : string.Empty,
                    isError ? 1 : 0,
                    isError,
                    durationMs),
                cancellationToken);
        }
        catch
        {
            // Журнал не должен мешать съёмке.
        }
    }
}
