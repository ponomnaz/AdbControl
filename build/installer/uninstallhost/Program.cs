using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

var invocation = UninstallInvocation.Parse(args);
var installDirectory = invocation.InstallDirectory ??
    AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

if (!invocation.IsDetached)
{
    var detachedExecutable = TryLaunchDetachedCopy(installDirectory);
    if (!string.IsNullOrWhiteSpace(detachedExecutable))
    {
        return;
    }
}

var installedProduct = FindInstalledProduct(installDirectory);
var embeddedProductCode = GetEmbeddedProductCode();

var confirm = MessageBox.Show(
    "Удалить AdbControl с этого компьютера?",
    "Удаление AdbControl",
    MessageBoxButtons.YesNo,
    MessageBoxIcon.Question);

if (confirm != DialogResult.Yes)
{
    return;
}

var productCode = installedProduct?.ProductCode;
if (string.IsNullOrWhiteSpace(productCode))
{
    productCode = embeddedProductCode;
}

var uninstallExitCode = 1605;

if (!string.IsNullOrWhiteSpace(productCode))
{
    try
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = $"/x {productCode} REBOOT=ReallySuppress",
                UseShellExecute = true,
                Verb = "runas"
            }
        };

        process.Start();
        process.WaitForExit();
        uninstallExitCode = process.ExitCode;
    }
    catch (Exception ex)
    {
        MessageBox.Show(
            $"Не удалось запустить удаление AdbControl.\r\n\r\n{ex.Message}",
            "Удаление AdbControl",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
        return;
    }
}

if (uninstallExitCode is 0 or 1605 or 1614 or 3010)
{
    ScheduleCleanup(installDirectory, invocation.DetachedExecutablePath);

    MessageBox.Show(
        "Удаление AdbControl запущено. Оставшиеся ярлыки и папка будут убраны автоматически.",
        "Удаление AdbControl",
        MessageBoxButtons.OK,
        MessageBoxIcon.Information);
    return;
}

MessageBox.Show(
    $"Не удалось удалить AdbControl. Код: {uninstallExitCode}.",
    "Удаление AdbControl",
    MessageBoxButtons.OK,
    MessageBoxIcon.Error);

return;

static string? GetEmbeddedProductCode()
{
    return Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => string.Equals(attribute.Key, "AdbControlProductCode", StringComparison.Ordinal))
        ?.Value
        ?.Trim();
}

static InstalledProductInfo? FindInstalledProduct(string installDirectory)
{
    foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            var match = FindInstalledProductInHive(hive, view, installDirectory);
            if (match is not null)
            {
                return match;
            }
        }
    }

    return null;
}

static InstalledProductInfo? FindInstalledProductInHive(RegistryHive hive, RegistryView view, string installDirectory)
{
    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
    using var uninstallKey = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
    if (uninstallKey is null)
    {
        return null;
    }

    foreach (var subKeyName in uninstallKey.GetSubKeyNames())
    {
        using var subKey = uninstallKey.OpenSubKey(subKeyName);
        if (subKey is null)
        {
            continue;
        }

        var displayName = subKey.GetValue("DisplayName") as string;
        if (!string.Equals(displayName, "AdbControl", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var installLocation = subKey.GetValue("InstallLocation") as string;
        if (!string.IsNullOrWhiteSpace(installLocation) &&
            PathsEqual(installLocation, installDirectory))
        {
            return new InstalledProductInfo(
                ExtractProductCode(subKey.GetValue("UninstallString") as string),
                installLocation);
        }

        var displayIcon = subKey.GetValue("DisplayIcon") as string;
        if (!string.IsNullOrWhiteSpace(displayIcon) &&
            PathsEqual(Path.GetDirectoryName(displayIcon) ?? string.Empty, installDirectory))
        {
            return new InstalledProductInfo(
                ExtractProductCode(subKey.GetValue("UninstallString") as string),
                installDirectory);
        }
    }

    return null;
}

static bool PathsEqual(string left, string right)
{
    var normalizedLeft = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    var normalizedRight = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
}

static string? ExtractProductCode(string? uninstallString)
{
    if (string.IsNullOrWhiteSpace(uninstallString))
    {
        return null;
    }

    var match = Regex.Match(uninstallString, @"\{[0-9A-F\-]+\}", RegexOptions.IgnoreCase);
    return match.Success ? match.Value.Trim() : null;
}

static string? TryLaunchDetachedCopy(string installDirectory)
{
    try
    {
        var currentExecutable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExecutable) || !File.Exists(currentExecutable))
        {
            return null;
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), "AdbControl.Uninstall");
        Directory.CreateDirectory(tempDirectory);

        var detachedExecutable = Path.Combine(tempDirectory, $"AdbControl.Uninstall.{Guid.NewGuid():N}.exe");
        File.Copy(currentExecutable, detachedExecutable, overwrite: true);

        Process.Start(new ProcessStartInfo
        {
            FileName = detachedExecutable,
            Arguments = $"--run-uninstall \"{installDirectory}\" --detached-path \"{detachedExecutable}\"",
            UseShellExecute = true
        });

        return detachedExecutable;
    }
    catch
    {
        return null;
    }
}

static void ScheduleCleanup(string installDirectory, string? detachedExecutablePath)
{
    var cleanupTargets = new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "AdbControl.lnk"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "AdbControl.lnk"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "AdbControl"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "AdbControl"),
        installDirectory,
        detachedExecutablePath ?? string.Empty
    };

    var script = new StringBuilder();
    script.AppendLine("Start-Sleep -Seconds 2");
    foreach (var target in cleanupTargets
        .Where(static target => !string.IsNullOrWhiteSpace(target))
        .Distinct(StringComparer.OrdinalIgnoreCase))
    {
        script.AppendLine($"Remove-Item -LiteralPath '{EscapeSingleQuotedPowerShell(target)}' -Force -Recurse -ErrorAction SilentlyContinue");
    }

    Process.Start(new ProcessStartInfo
    {
        FileName = "powershell.exe",
        Arguments = $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -Command \"{EscapeDoubleQuotedArgument(script.ToString())}\"",
        UseShellExecute = false,
        CreateNoWindow = true
    });
}

static string EscapeSingleQuotedPowerShell(string value)
{
    return value.Replace("'", "''", StringComparison.Ordinal);
}

static string EscapeDoubleQuotedArgument(string value)
{
    return value
        .Replace("`", "``", StringComparison.Ordinal)
        .Replace("\"", "`\"", StringComparison.Ordinal)
        .Replace("\r", string.Empty, StringComparison.Ordinal)
        .Replace("\n", "; ", StringComparison.Ordinal);
}

internal sealed record InstalledProductInfo(string? ProductCode, string InstallLocation);

internal sealed record UninstallInvocation(bool IsDetached, string? InstallDirectory, string? DetachedExecutablePath)
{
    public static UninstallInvocation Parse(string[] args)
    {
        var isDetached = false;
        string? installDirectory = null;
        string? detachedExecutablePath = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--run-uninstall":
                    isDetached = true;
                    if (index + 1 < args.Length)
                    {
                        installDirectory = args[++index];
                    }
                    break;

                case "--detached-path":
                    if (index + 1 < args.Length)
                    {
                        detachedExecutablePath = args[++index];
                    }
                    break;
            }
        }

        return new UninstallInvocation(isDetached, installDirectory, detachedExecutablePath);
    }
}
