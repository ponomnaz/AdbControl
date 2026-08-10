using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AdbControl.Application.Remote;
using AdbControl.Tools.Remote.ViewModels;

namespace AdbControl.Tools.Remote.Views;

public sealed class ScrcpyWindowHost : HwndHost
{
    #region Win32

    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;

    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsClipChildren = 0x02000000;
    private const int WsExNoactivate = 0x08000000;

    private const int SwShownoactivate = 4;

    private const uint SwpNomove = 0x0002;
    private const uint SwpNosize = 0x0001;
    private const uint SwpNozorder = 0x0004;
    private const uint SwpFramechanged = 0x0020;

    private const int WmSize = 0x0005;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    #endregion

    private IntPtr _hostHwnd;
    private IntPtr _scrcpyHwnd;
    private Process? _scrcpyProcess;
    private CancellationTokenSource? _attachCts;

    /// <summary>Fires on the UI thread when the scrcpy window has been embedded.</summary>
    public event EventHandler? WindowAttached;

    public void StartScrcpy(string targetId)
    {
        StopScrcpy();

        var scrcpyExe = ResolveScrcpyPath();
        if (scrcpyExe is null)
        {
            return;
        }

        var windowTitle = BuildWindowTitle(targetId);

        try
        {
            _scrcpyProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = scrcpyExe,
                    // Start off-screen so the window is never visible before reparenting.
                    Arguments = $"-s {targetId} --window-title \"{windowTitle}\" --stay-awake --no-audio --video-bit-rate=8M --window-x -32000 --window-y -32000",
                    UseShellExecute = false,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };
            _scrcpyProcess.Exited += OnScrcpyExited;
            _scrcpyProcess.Start();

            _attachCts = new CancellationTokenSource();
            _ = WaitAndAttachAsync(windowTitle, _attachCts.Token);
        }
        catch
        {
            CleanupProcess();
        }
    }

    public void StopScrcpy()
    {
        _attachCts?.Cancel();
        _attachCts?.Dispose();
        _attachCts = null;
        _scrcpyHwnd = IntPtr.Zero;
        CleanupProcess();
    }

    private async Task WaitAndAttachAsync(string windowTitle, CancellationToken cancellationToken)
    {
        try
        {
            var deadline = TimeSpan.FromSeconds(25);
            var elapsed = TimeSpan.Zero;
            var poll = TimeSpan.FromMilliseconds(200);

            while (elapsed < deadline && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(poll, cancellationToken);
                elapsed += poll;

                var hwnd = FindWindow(null, windowTitle);
                if (hwnd == IntPtr.Zero)
                {
                    continue;
                }

                // Give D3D11 extra time to finish initialization before reparenting.
                await Task.Delay(800, cancellationToken);

                _scrcpyHwnd = hwnd;
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                    () => AttachScrcpyWindow(hwnd));
                return;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void AttachScrcpyWindow(IntPtr scrcpyHwnd)
    {
        if (_hostHwnd == IntPtr.Zero || scrcpyHwnd == IntPtr.Zero)
        {
            return;
        }

        SetParent(scrcpyHwnd, _hostHwnd);

        SetWindowLong(scrcpyHwnd, GwlStyle, WsChild | WsVisible | WsClipChildren);
        SetWindowLong(scrcpyHwnd, GwlExStyle, WsExNoactivate);

        SetWindowPos(scrcpyHwnd, IntPtr.Zero, 0, 0, 0, 0,
            SwpNomove | SwpNosize | SwpNozorder | SwpFramechanged);

        ResizeScrcpyWindow();

        ShowWindow(scrcpyHwnd, SwShownoactivate);

        WindowAttached?.Invoke(this, EventArgs.Empty);
    }

    private void ResizeScrcpyWindow()
    {
        if (_scrcpyHwnd == IntPtr.Zero || _hostHwnd == IntPtr.Zero)
        {
            return;
        }

        var (dpiX, dpiY) = GetDpiScale();
        var w = Math.Max((int)(ActualWidth * dpiX), 1);
        var h = Math.Max((int)(ActualHeight * dpiY), 1);

        MoveWindow(_scrcpyHwnd, 0, 0, w, h, true);

        var lParam = (IntPtr)((h << 16) | (w & 0xFFFF));
        SendMessage(_scrcpyHwnd, WmSize, IntPtr.Zero, lParam);
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        var (dpiX, dpiY) = GetDpiScale();
        var w = Math.Max((int)(ActualWidth * dpiX), 1);
        var h = Math.Max((int)(ActualHeight * dpiY), 1);

        _hostHwnd = CreateWindowEx(
            0, "static", string.Empty,
            WsChild | WsVisible | WsClipChildren,
            0, 0, w, h,
            hwndParent.Handle,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        return new HandleRef(this, _hostHwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        StopScrcpy();
        DestroyWindow(hwnd.Handle);
        _hostHwnd = IntPtr.Zero;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);

        if (_hostHwnd != IntPtr.Zero)
        {
            var (dpiX, dpiY) = GetDpiScale();
            var w = Math.Max((int)(sizeInfo.NewSize.Width * dpiX), 1);
            var h = Math.Max((int)(sizeInfo.NewSize.Height * dpiY), 1);
            MoveWindow(_hostHwnd, 0, 0, w, h, true);
        }

        ResizeScrcpyWindow();
    }

    private (double dpiX, double dpiY) GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
        {
            return (1.0, 1.0);
        }

        var m = source.CompositionTarget.TransformToDevice;
        return (m.M11, m.M22);
    }

    private void OnScrcpyExited(object? sender, EventArgs e)
    {
        _scrcpyHwnd = IntPtr.Zero;
        CleanupProcess();
    }

    private void CleanupProcess()
    {
        if (_scrcpyProcess is null)
        {
            return;
        }

        _scrcpyProcess.Exited -= OnScrcpyExited;

        try
        {
            if (!_scrcpyProcess.HasExited)
            {
                _scrcpyProcess.Kill(entireProcessTree: true);
            }
        }
        catch { }

        _scrcpyProcess.Dispose();
        _scrcpyProcess = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopScrcpy();
        }

        base.Dispose(disposing);
    }

    internal static string? ResolveScrcpyPath()
    {
        var paths = new[]
        {
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process)
        };

        return paths
            .Where(p => !string.IsNullOrEmpty(p))
            .SelectMany(p => p!.Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Select(dir => dir.Trim())
            .Where(dir => !string.IsNullOrEmpty(dir))
            .Select(dir => Path.Combine(dir, "scrcpy.exe"))
            .FirstOrDefault(File.Exists);
    }

    internal static string BuildWindowTitle(string targetId) =>
        $"AdbControl-Remote-{targetId}";
}
