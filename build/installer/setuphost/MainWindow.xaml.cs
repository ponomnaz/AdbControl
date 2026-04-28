using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace AdbControl.SetupHost;

public partial class MainWindow : Window
{
    private readonly string _defaultInstallPath;
    private InstallerStage _stage = InstallerStage.Welcome;
    private bool _isInstalling;
    private int _lastExitCode;
    private string? _lastLogPath;

    public MainWindow()
    {
        InitializeComponent();

        _defaultInstallPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "AdbControl");

        InstallPathTextBox.Text = _defaultInstallPath;
        SetStage(InstallerStage.Welcome);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isInstalling)
        {
            System.Windows.MessageBox.Show(
                this,
                "Установка ещё не завершилась. Дождись окончания процесса.",
                "Установка AdbControl",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    private void BrowseInstallPath_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Выбери папку для установки AdbControl",
            SelectedPath = Directory.Exists(InstallPathTextBox.Text)
                ? InstallPathTextBox.Text
                : _defaultInstallPath,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK &&
            !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            InstallPathTextBox.Text = dialog.SelectedPath;
        }
    }

    private async void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        switch (_stage)
        {
            case InstallerStage.Welcome:
                SetStage(InstallerStage.Options);
                break;
            case InstallerStage.Options:
                await InstallAsync();
                break;
            case InstallerStage.Completed:
                Close();
                break;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstalling)
        {
            return;
        }

        switch (_stage)
        {
            case InstallerStage.Options:
                SetStage(InstallerStage.Welcome);
                break;
            case InstallerStage.Completed:
                SetStage(InstallerStage.Options);
                break;
        }
    }

    private void SecondaryActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstalling)
        {
            return;
        }

        if (_stage == InstallerStage.Completed && _lastExitCode is not 0 and not 3010)
        {
            if (!string.IsNullOrWhiteSpace(_lastLogPath) && File.Exists(_lastLogPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _lastLogPath,
                    UseShellExecute = true
                });
            }

            return;
        }

        if (_stage == InstallerStage.Completed && _lastExitCode is 0 or 3010)
        {
            var exePath = Path.Combine(InstallPathTextBox.Text.Trim(), "AdbControl.exe");
            if (File.Exists(exePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                });
            }
        }

        Close();
    }

    private async Task InstallAsync()
    {
        var installPath = InstallPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(installPath))
        {
            System.Windows.MessageBox.Show(
                this,
                "Выбери папку установки.",
                "Установка AdbControl",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!HasEmbeddedInstallerPayload())
        {
            System.Windows.MessageBox.Show(
                this,
                "Внутренние файлы установщика не найдены.",
                "Установка AdbControl",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        _isInstalling = true;
        SetStage(InstallerStage.Installing);
        InstallingStatusTextBlock.Text = "Запуск установки...";
        InstallingDetailsTextBlock.Text = "Подготовка установки.";

        var shortcutFlag = DesktopShortcutCheckBox.IsChecked == true ? "1" : "0";
        var logPath = Path.Combine(Path.GetTempPath(), $"AdbControl-Setup-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        _lastLogPath = logPath;
        var extractedMsiPath = ExtractInstallerPayload();

        if (string.IsNullOrWhiteSpace(extractedMsiPath))
        {
            _lastExitCode = -1;
            CompletedTitleTextBlock.Text = "Не удалось установить AdbControl";
            CompletedSubtitleTextBlock.Text = "Внутренний пакет установки не найден.";
            CompletedDetailsTextBlock.Text = "Попробуй заново собрать установщик.";
            CompletedDetailsTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            _isInstalling = false;
            SetStage(InstallerStage.Completed);
            return;
        }

        var arguments =
            $"/i \"{extractedMsiPath}\" " +
            $"INSTALLFOLDER=\"{installPath}\" " +
            $"INSTALLDESKTOPSHORTCUT={shortcutFlag} " +
            "REBOOT=ReallySuppress /qn " +
            $"/L*v \"{logPath}\"";

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "msiexec.exe",
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };

            InstallingStatusTextBlock.Text = "Идёт установка AdbControl...";
            InstallingDetailsTextBlock.Text = "Подождите, это может занять некоторое время.";

            process.Start();
            await process.WaitForExitAsync();

            _lastExitCode = process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _lastExitCode = 1223;
            CompletedTitleTextBlock.Text = "Установка отменена";
            CompletedSubtitleTextBlock.Text = "Нужны права администратора, чтобы установить AdbControl.";
            CompletedDetailsTextBlock.Text = "Нажми «Назад» и попробуй снова.";
            CompletedDetailsTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");
            _isInstalling = false;
            SetStage(InstallerStage.Completed);
            return;
        }
        catch (Exception ex)
        {
            _lastExitCode = -1;
            CompletedTitleTextBlock.Text = "Не удалось установить AdbControl";
            CompletedSubtitleTextBlock.Text = "Попробуй запустить установку ещё раз.";
            CompletedDetailsTextBlock.Text = string.IsNullOrWhiteSpace(ex.Message)
                ? "Произошла ошибка при запуске установки."
                : ex.Message;
            CompletedDetailsTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            _isInstalling = false;
            SetStage(InstallerStage.Completed);
            return;
        }
        finally
        {
            TryDeleteFile(extractedMsiPath);
        }

        _isInstalling = false;
        ApplyCompletionState(installPath);
        SetStage(InstallerStage.Completed);
    }

    private void ApplyCompletionState(string installPath)
    {
        CompletedDetailsTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");

        if (_lastExitCode is 0 or 3010)
        {
            CompletedTitleTextBlock.Text = "Установка завершена";
            CompletedSubtitleTextBlock.Text = _lastExitCode == 3010
                ? "AdbControl установлен. Может понадобиться перезагрузка."
                : "AdbControl установлен и готов к запуску.";

            CompletedDetailsTextBlock.Text = installPath;
            return;
        }

        CompletedTitleTextBlock.Text = "Не удалось установить AdbControl";
        CompletedSubtitleTextBlock.Text = _lastExitCode == 1603
            ? "Проверь права администратора и попробуй снова."
            : "Попробуй ещё раз. Если ошибка повторится, открой журнал установки.";
        CompletedDetailsTextBlock.Text = "Открой журнал установки, если нужно посмотреть подробности.";
        CompletedDetailsTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
    }

    private void SetStage(InstallerStage stage)
    {
        _stage = stage;

        WelcomePage.Visibility = stage == InstallerStage.Welcome ? Visibility.Visible : Visibility.Collapsed;
        OptionsPage.Visibility = stage == InstallerStage.Options ? Visibility.Visible : Visibility.Collapsed;
        InstallingPage.Visibility = stage == InstallerStage.Installing ? Visibility.Visible : Visibility.Collapsed;
        CompletedPage.Visibility = stage == InstallerStage.Completed ? Visibility.Visible : Visibility.Collapsed;

        StepWelcomeCard.Background = stage == InstallerStage.Welcome
            ? new SolidColorBrush(ColorFromHex("#132438"))
            : new SolidColorBrush(ColorFromHex("#111827"));
        StepOptionsCard.Background = stage == InstallerStage.Options
            ? new SolidColorBrush(ColorFromHex("#132438"))
            : new SolidColorBrush(ColorFromHex("#111827"));
        StepInstallCard.Background = stage is InstallerStage.Installing or InstallerStage.Completed
            ? new SolidColorBrush(ColorFromHex("#132438"))
            : new SolidColorBrush(ColorFromHex("#111827"));

        SetBadgeState(StepWelcomeBadge, stage == InstallerStage.Welcome);
        SetBadgeState(StepOptionsBadge, stage == InstallerStage.Options);
        SetBadgeState(StepInstallBadge, stage is InstallerStage.Installing or InstallerStage.Completed);

        switch (stage)
        {
            case InstallerStage.Welcome:
                FooterStatusTextBlock.Text = HasEmbeddedInstallerPayload()
                    ? string.Empty
                    : "Файлы установщика повреждены.";
                BackButton.Visibility = Visibility.Collapsed;
                ActionButton.Visibility = Visibility.Visible;
                ActionButton.Content = "Далее";
                ActionButton.IsEnabled = HasEmbeddedInstallerPayload();
                SecondaryActionButton.Content = "Закрыть";
                SecondaryActionButton.Visibility = Visibility.Visible;
                break;

            case InstallerStage.Options:
                FooterStatusTextBlock.Text = string.Empty;
                BackButton.Visibility = Visibility.Visible;
                BackButton.IsEnabled = true;
                ActionButton.Visibility = Visibility.Visible;
                ActionButton.Content = "Установить";
                ActionButton.IsEnabled = true;
                SecondaryActionButton.Content = "Закрыть";
                SecondaryActionButton.Visibility = Visibility.Visible;
                break;

            case InstallerStage.Installing:
                FooterStatusTextBlock.Text = string.Empty;
                BackButton.Visibility = Visibility.Visible;
                BackButton.IsEnabled = false;
                ActionButton.Visibility = Visibility.Collapsed;
                SecondaryActionButton.Content = "Закрыть";
                SecondaryActionButton.IsEnabled = false;
                SecondaryActionButton.Visibility = Visibility.Visible;
                break;

            case InstallerStage.Completed:
                FooterStatusTextBlock.Text = string.Empty;
                BackButton.Visibility = Visibility.Visible;
                BackButton.IsEnabled = _lastExitCode is not 0 and not 3010;
                ActionButton.Visibility = Visibility.Visible;
                ActionButton.Content = "Готово";
                ActionButton.IsEnabled = true;
                SecondaryActionButton.IsEnabled = true;
                SecondaryActionButton.Content = _lastExitCode switch
                {
                    0 or 3010 => "Запустить AdbControl",
                    _ when !string.IsNullOrWhiteSpace(_lastLogPath) => "Открыть журнал",
                    _ => "Закрыть"
                };
                SecondaryActionButton.Visibility = Visibility.Visible;
                break;
        }
    }

    private static void SetBadgeState(Border badge, bool isActive)
    {
        badge.Background = new SolidColorBrush(ColorFromHex(isActive ? "#10B981" : "#1E293B"));

        if (badge.Child is TextBlock text)
        {
            text.Foreground = new SolidColorBrush(ColorFromHex(isActive ? "#FFFFFF" : "#CBD5E1"));
        }
    }

    private static System.Windows.Media.Color ColorFromHex(string hex)
    {
        return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
    }

    private static bool HasEmbeddedInstallerPayload()
    {
        return GetPayloadResourceName() is not null;
    }

    private static string? ExtractInstallerPayload()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = GetPayloadResourceName();
        if (resourceName is null)
        {
            return null;
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        var targetDirectory = Path.Combine(Path.GetTempPath(), "AdbControl.Setup");
        Directory.CreateDirectory(targetDirectory);

        var targetPath = Path.Combine(targetDirectory, $"AdbControl.Setup.{Guid.NewGuid():N}.msi");
        using var fileStream = File.Create(targetPath);
        stream.CopyTo(fileStream);
        return targetPath;
    }

    private static string? GetPayloadResourceName()
    {
        return Assembly.GetExecutingAssembly()
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("AdbControl.Setup.msi", StringComparison.OrdinalIgnoreCase));
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Ignore cleanup failures for temp MSI payloads.
        }
    }

    private enum InstallerStage
    {
        Welcome,
        Options,
        Installing,
        Completed
    }
}
