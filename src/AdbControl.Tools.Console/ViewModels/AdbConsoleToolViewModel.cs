using System.Text;
using AdbControl.Application.Common;
using AdbControl.Application.Terminal;

namespace AdbControl.Tools.Console.ViewModels;

public sealed class AdbConsoleToolViewModel : ObservableObject, IDisposable
{
    private readonly IAdbConsoleService _adbConsoleService;
    private readonly StringBuilder _outputBuilder = new();
    private readonly List<string> _history = [];
    private CancellationTokenSource? _executionCts;
    private string _commandInput = string.Empty;
    private string _outputText = string.Empty;
    private string _historyDraft = string.Empty;
    private int _historyIndex;
    private bool _isBusy;
    private bool _isDisposed;
    private bool _isApplyingHistory;

    public AdbConsoleToolViewModel(IAdbConsoleService adbConsoleService)
    {
        _adbConsoleService = adbConsoleService;
        _historyIndex = 0;

        ExecuteCommand = new RelayCommand(
            () => _ = ExecuteCurrentCommandAsync(),
            () => CanExecuteCommand());
        ClearOutputCommand = new RelayCommand(
            ClearOutput,
            () => CanClearOutput());
        StopCommand = new RelayCommand(
            StopExecution,
            () => CanStopExecution());
    }

    public RelayCommand ExecuteCommand { get; }

    public RelayCommand ClearOutputCommand { get; }

    public RelayCommand StopCommand { get; }

    public string CommandInput
    {
        get => _commandInput;
        set
        {
            if (!SetProperty(ref _commandInput, value))
            {
                return;
            }

            if (!_isApplyingHistory && _historyIndex >= _history.Count)
            {
                _historyDraft = value;
            }

            NotifyCommandStateChanged();
        }
    }

    public string OutputText
    {
        get => _outputText;
        private set => SetProperty(ref _outputText, value);
    }

    public bool HasOutput => !string.IsNullOrWhiteSpace(OutputText);

    public string EmptyStateMessage => _isBusy
        ? "Команда выполняется..."
        : "Введи ADB-команду и нажми Enter.";

    public bool TryRecallPreviousCommand()
    {
        if (_history.Count == 0)
        {
            return false;
        }

        if (_historyIndex >= _history.Count)
        {
            _historyDraft = CommandInput;
            _historyIndex = _history.Count - 1;
        }
        else if (_historyIndex > 0)
        {
            _historyIndex--;
        }

        ApplyHistoryEntry(_history[_historyIndex]);
        return true;
    }

    public bool TryRecallNextCommand()
    {
        if (_history.Count == 0)
        {
            return false;
        }

        if (_historyIndex < _history.Count - 1)
        {
            _historyIndex++;
            ApplyHistoryEntry(_history[_historyIndex]);
            return true;
        }

        _historyIndex = _history.Count;
        ApplyHistoryEntry(_historyDraft);
        return true;
    }

    private async Task ExecuteCurrentCommandAsync()
    {
        if (!CanExecuteCommand())
        {
            return;
        }

        var rawCommand = CommandInput.Trim();
        AddToHistory(rawCommand);
        CommandInput = string.Empty;

        _executionCts?.Dispose();
        _executionCts = new CancellationTokenSource();

        try
        {
            _isBusy = true;
            OnPropertyChanged(nameof(EmptyStateMessage));
            NotifyCommandStateChanged();

            var result = await _adbConsoleService.ExecuteAsync(rawCommand, _executionCts.Token);
            AppendTranscriptEntry(result);
        }
        catch (Exception ex)
        {
            AppendLocalError(rawCommand, ex.Message);
        }
        finally
        {
            _executionCts?.Dispose();
            _executionCts = null;
            _isBusy = false;
            OnPropertyChanged(nameof(EmptyStateMessage));
            NotifyCommandStateChanged();
        }
    }

    private void StopExecution()
    {
        _executionCts?.Cancel();
    }

    private void ClearOutput()
    {
        _outputBuilder.Clear();
        OutputText = string.Empty;
        OnPropertyChanged(nameof(HasOutput));
        OnPropertyChanged(nameof(EmptyStateMessage));
        NotifyCommandStateChanged();
    }

    private void AppendTranscriptEntry(AdbConsoleCommandResult result)
    {
        var block = new StringBuilder();
        block.Append('[')
            .Append(DateTimeOffset.Now.ToString("HH:mm:ss"))
            .Append("] > ")
            .Append(result.CommandText);

        var stdout = NormalizeOutput(result.Stdout);
        var stderr = NormalizeOutput(result.Stderr);

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            block.AppendLine();
            block.Append(stdout);
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            block.AppendLine();
            block.Append(stderr);
        }

        if (!result.Started && string.IsNullOrWhiteSpace(stderr))
        {
            block.AppendLine();
            block.Append("Команда не выполнена.");
        }
        else if (result.Started && result.ExitCode != 0 && string.IsNullOrWhiteSpace(stderr))
        {
            block.AppendLine();
            block.Append($"Код выхода: {result.ExitCode}");
        }

        AppendBlock(block.ToString().TrimEnd());
    }

    private void AppendLocalError(string rawCommand, string message)
    {
        var displayCommand = string.IsNullOrWhiteSpace(rawCommand)
            ? "adb"
            : rawCommand.Trim();

        AppendBlock($"[{DateTimeOffset.Now:HH:mm:ss}] > {displayCommand}{Environment.NewLine}{message}".TrimEnd());
    }

    private void AppendBlock(string block)
    {
        if (_outputBuilder.Length > 0)
        {
            _outputBuilder.AppendLine();
            _outputBuilder.AppendLine();
        }

        _outputBuilder.Append(block);
        OutputText = _outputBuilder.ToString();
        OnPropertyChanged(nameof(HasOutput));
        OnPropertyChanged(nameof(EmptyStateMessage));
        NotifyCommandStateChanged();
    }

    private void AddToHistory(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        _history.Add(command);
        _historyIndex = _history.Count;
        _historyDraft = string.Empty;
    }

    private void ApplyHistoryEntry(string value)
    {
        _isApplyingHistory = true;
        try
        {
            CommandInput = value;
        }
        finally
        {
            _isApplyingHistory = false;
        }
    }

    private bool CanExecuteCommand()
    {
        return !_isDisposed &&
               !_isBusy &&
               !string.IsNullOrWhiteSpace(CommandInput);
    }

    private bool CanClearOutput()
    {
        return !_isDisposed && HasOutput;
    }

    private bool CanStopExecution()
    {
        return !_isDisposed && _isBusy && _executionCts is not null;
    }

    private void NotifyCommandStateChanged()
    {
        ExecuteCommand.NotifyCanExecuteChanged();
        ClearOutputCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    private static string NormalizeOutput(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("\r\n", "\n").TrimEnd();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _executionCts?.Cancel();
        _executionCts?.Dispose();
        _executionCts = null;
        NotifyCommandStateChanged();
    }
}
