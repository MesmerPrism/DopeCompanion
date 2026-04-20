using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DopeCompanion.Core.Models;
using DopeCompanion.Core.Services;

namespace DopeCompanion.App;

internal sealed class QuestDisplayCastService : IDisposable
{
    private const string DefaultWindowTitle = "DOPE Companion Cast · Display 0";
    private const int WindowReadyAttempts = 40;
    private const int WindowReadyDelayMilliseconds = 150;
    private Process? _process;
    private int? _exitCode;
    private string? _activeSelector;
    private nint _windowHandle;
    private WindowLayoutBounds? _requestedWindowBounds;
    private bool _isRestarting;

    public event EventHandler? StateChanged;

    public bool IsRunning
    {
        get
        {
            RefreshState();
            return _process is { HasExited: false };
        }
    }

    public bool IsRestarting => _isRestarting;

    public string CaptureSourceLabel => "Display 0 · default stereo mirror";

    public string WindowTitle => DefaultWindowTitle;

    public string ToolingPath => ScrcpyExecutableLocator.TryLocate(AppContext.BaseDirectory) ?? "scrcpy.exe not found";

    public OperationOutcomeKind Level
    {
        get
        {
            RefreshState();
            if (IsRestarting)
            {
                return OperationOutcomeKind.Preview;
            }

            if (IsRunning)
            {
                return TryRefreshWindowHandle()
                    ? OperationOutcomeKind.Success
                    : OperationOutcomeKind.Warning;
            }

            return _exitCode.HasValue && _exitCode.Value != 0
                ? OperationOutcomeKind.Warning
                : OperationOutcomeKind.Preview;
        }
    }

    public string Summary
    {
        get
        {
            RefreshState();
            if (IsRestarting)
            {
                return "Reloading Display 0 cast.";
            }

            if (IsRunning)
            {
                return TryRefreshWindowHandle()
                    ? $"Casting {CaptureSourceLabel}."
                    : "Display 0 cast window not visible.";
            }

            return _exitCode.HasValue && _exitCode.Value != 0
                ? "Display 0 cast ended unexpectedly."
                : "Display 0 cast idle.";
        }
    }

    public string Detail
    {
        get
        {
            RefreshState();
            if (IsRestarting)
            {
                return $"scrcpy is restarting for {_activeSelector} so the Display 0 feed can adopt the new window bounds.";
            }

            if (IsRunning)
            {
                return TryRefreshWindowHandle()
                    ? $"scrcpy is streaming {CaptureSourceLabel} for {_activeSelector}. Window title `{DefaultWindowTitle}`."
                    : $"scrcpy is still running for {_activeSelector}, but no visible `{DefaultWindowTitle}` window is available. Restart the cast if the mirror never appears.";
            }

            if (_exitCode.HasValue && _exitCode.Value != 0)
            {
                return $"scrcpy exited with code {_exitCode.Value}. Start the cast again after confirming the Quest selector and local scrcpy runtime.";
            }

            return $"Start the cast to open the Quest stereo mirror in a separate scrcpy window. Tooling path: {ToolingPath}.";
        }
    }

    public Task<OperationOutcome> StartDisplay0Async(string selector, CancellationToken cancellationToken = default)
        => StartDisplay0CoreAsync(selector, cancellationToken);

    public Task<OperationOutcome> StartDisplay0Async(
        string selector,
        WindowLayoutBounds initialWindowBounds,
        CancellationToken cancellationToken = default)
        => StartDisplay0CoreAsync(selector, initialWindowBounds, cancellationToken);

    public bool TryGetWindowHandle(out nint windowHandle)
    {
        RefreshState();
        if (!TryRefreshWindowHandle())
        {
            windowHandle = 0;
            return false;
        }

        windowHandle = _windowHandle;
        return true;
    }

    public bool TryGetWindowBounds(out WindowLayoutBounds bounds)
    {
        RefreshState();
        if (!TryRefreshWindowHandle() ||
            _windowHandle == 0 ||
            !NativeMethods.GetWindowRect(_windowHandle, out var rect))
        {
            bounds = new WindowLayoutBounds(0, 0, 0, 0);
            return false;
        }

        bounds = new WindowLayoutBounds(
            rect.Left,
            rect.Top,
            Math.Max(1, rect.Right - rect.Left),
            Math.Max(1, rect.Bottom - rect.Top));
        return true;
    }

    public bool TryMoveWindow(WindowLayoutBounds bounds)
    {
        RefreshState();
        if (!TryRefreshWindowHandle() || _windowHandle == 0)
        {
            return false;
        }

        var moved = NativeMethods.MoveWindow(
            _windowHandle,
            bounds.X,
            bounds.Y,
            Math.Max(1, bounds.Width),
            Math.Max(1, bounds.Height),
            true);
        if (moved)
        {
            _requestedWindowBounds = NormalizeWindowBounds(bounds);
        }

        return moved;
    }

    public async Task<OperationOutcome> RestartDisplay0Async(WindowLayoutBounds bounds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RefreshState();

        if (string.IsNullOrWhiteSpace(_activeSelector))
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Display 0 cast reload blocked.",
                "The cast does not have a remembered Quest selector yet. Start the cast once before resizing it.");
        }

        _requestedWindowBounds = NormalizeWindowBounds(bounds);
        _isRestarting = true;
        StateChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            if (IsRunning)
            {
                await StopAsync(cancellationToken).ConfigureAwait(false);
            }

            return await StartDisplay0CoreAsync(_activeSelector, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _isRestarting = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool TryMinimizeWindow()
    {
        RefreshState();
        if (!TryRefreshWindowHandle() || _windowHandle == 0)
        {
            return false;
        }

        return NativeMethods.ShowWindow(_windowHandle, NativeMethods.SwMinimize);
    }

    public bool TryRestoreWindow()
    {
        RefreshState();
        if (!TryRefreshWindowHandle() || _windowHandle == 0)
        {
            return false;
        }

        return NativeMethods.ShowWindow(_windowHandle, NativeMethods.SwRestore);
    }

    public Task<OperationOutcome> StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RefreshState();

        if (_process is null || _process.HasExited)
        {
            return Task.FromResult(new OperationOutcome(
                OperationOutcomeKind.Preview,
                "Display 0 cast is already stopped.",
                $"No active scrcpy process is running. Tooling path: {ToolingPath}."));
        }

        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            return Task.FromResult(new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Display 0 cast stop failed.",
                ex.Message,
                Endpoint: _activeSelector));
        }

        RefreshState();
        StateChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(new OperationOutcome(
            OperationOutcomeKind.Success,
            "Stopped Display 0 cast.",
            "The managed scrcpy process was terminated.",
            Endpoint: _activeSelector));
    }

    public void Dispose()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
        finally
        {
            _process.Dispose();
            _process = null;
            _windowHandle = 0;
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        RefreshState();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private Task<OperationOutcome> StartDisplay0CoreAsync(string selector, CancellationToken cancellationToken)
        => StartDisplay0CoreAsync(selector, null, cancellationToken);

    private async Task<OperationOutcome> StartDisplay0CoreAsync(string selector, WindowLayoutBounds? initialWindowBounds, CancellationToken cancellationToken)
    {
        if (initialWindowBounds is not null)
        {
            _requestedWindowBounds = NormalizeWindowBounds(initialWindowBounds);
        }

        RefreshState();
        if (IsRunning)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Success,
                $"Casting {CaptureSourceLabel}.",
                $"The scrcpy window is already live for {_activeSelector}.",
                Endpoint: _activeSelector);
        }

        if (string.IsNullOrWhiteSpace(selector))
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Display 0 cast blocked.",
                "Connect the Quest first so the cast service has a live ADB selector.");
        }

        var scrcpyPath = ScrcpyExecutableLocator.TryLocate(AppContext.BaseDirectory);
        if (string.IsNullOrWhiteSpace(scrcpyPath))
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Display 0 cast blocked.",
                "scrcpy.exe was not found. Run guided setup or `dope-companion tooling install-official` to refresh the managed cast runtime, keep the Quest Multi Stream tooling cache on this machine, or set DOPE_SCRCPY_EXE.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = scrcpyPath,
            WorkingDirectory = Path.GetDirectoryName(scrcpyPath) ?? AppContext.BaseDirectory,
            UseShellExecute = true,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        foreach (var argument in BuildArguments(selector.Trim(), _requestedWindowBounds))
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        if (!process.Start())
        {
            process.Dispose();
            return new OperationOutcome(
                OperationOutcomeKind.Failure,
                "Display 0 cast failed to start.",
                "scrcpy did not open a new process.");
        }

        _process = process;
        _activeSelector = selector.Trim();
        _exitCode = null;
        _windowHandle = 0;
        process.Exited += OnProcessExited;
        StateChanged?.Invoke(this, EventArgs.Empty);

        var windowReady = await WaitForVisibleWindowAsync(cancellationToken).ConfigureAwait(false);
        RefreshState();
        if (!windowReady)
        {
            try
            {
                if (_process is { HasExited: false })
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            RefreshState();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Display 0 cast failed to appear.",
                $"scrcpy launched for {_activeSelector}, but no visible `{DefaultWindowTitle}` window appeared. Confirm the Quest is awake, then start the cast again.",
                Endpoint: _activeSelector);
        }

        ActivateCastWindow();
        StateChanged?.Invoke(this, EventArgs.Empty);
        return new OperationOutcome(
            OperationOutcomeKind.Success,
            $"Started {CaptureSourceLabel} cast.",
            $"Visible scrcpy window `{DefaultWindowTitle}` opened for {_activeSelector}.",
            Endpoint: _activeSelector);
    }

    private void RefreshState()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (_process.HasExited)
            {
                _exitCode = _process.ExitCode;
                _process.Exited -= OnProcessExited;
                _process.Dispose();
                _process = null;
                _windowHandle = 0;
            }
        }
        catch
        {
            _process = null;
            _windowHandle = 0;
        }
    }

    private async Task<bool> WaitForVisibleWindowAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < WindowReadyAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshState();
            if (_process is null)
            {
                return false;
            }

            if (TryRefreshWindowHandle())
            {
                return true;
            }

            await Task.Delay(WindowReadyDelayMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private bool TryRefreshWindowHandle()
    {
        if (_process is null)
        {
            _windowHandle = 0;
            return false;
        }

        var titledWindow = NativeMethods.FindWindow(null, DefaultWindowTitle);
        if (titledWindow != 0 && NativeMethods.IsWindowVisible(titledWindow))
        {
            _windowHandle = titledWindow;
            return true;
        }

        if (_windowHandle != 0 &&
            NativeMethods.IsWindow(_windowHandle) &&
            NativeMethods.IsWindowVisible(_windowHandle))
        {
            return true;
        }

        try
        {
            _process.Refresh();
            if (_process.MainWindowHandle != 0 &&
                NativeMethods.IsWindowVisible(_process.MainWindowHandle))
            {
                _windowHandle = _process.MainWindowHandle;
                return true;
            }
        }
        catch (InvalidOperationException)
        {
            _windowHandle = 0;
            return false;
        }

        _windowHandle = 0;
        return false;
    }

    private void ActivateCastWindow()
    {
        if (!TryRefreshWindowHandle() || _windowHandle == 0)
        {
            return;
        }

        if (NativeMethods.IsIconic(_windowHandle))
        {
            NativeMethods.ShowWindow(_windowHandle, NativeMethods.SwRestore);
        }
        else
        {
            NativeMethods.ShowWindow(_windowHandle, NativeMethods.SwShow);
        }

        NativeMethods.BringWindowToTop(_windowHandle);
        NativeMethods.SetForegroundWindow(_windowHandle);
    }

    private static IReadOnlyList<string> BuildArguments(string selector, WindowLayoutBounds? initialWindowBounds)
    {
        var arguments = new List<string>
        {
            $"--serial={selector}",
            $"--window-title={DefaultWindowTitle}",
            "--display-id=0",
            "--max-size=1344",
            "--video-bit-rate=20M",
            "--max-fps=30",
            "--disable-screensaver",
            "--no-audio",
            "--no-control",
            "--window-borderless"
        };

        if (initialWindowBounds is { } bounds)
        {
            arguments.Add($"--window-x={bounds.X}");
            arguments.Add($"--window-y={bounds.Y}");
            arguments.Add($"--window-width={bounds.Width}");
            arguments.Add($"--window-height={bounds.Height}");
        }

        return arguments;
    }

    private static WindowLayoutBounds NormalizeWindowBounds(WindowLayoutBounds bounds)
        => new(
            bounds.X,
            bounds.Y,
            Math.Max(1, bounds.Width),
            Math.Max(1, bounds.Height));

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern nint FindWindow(string? className, string? windowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(nint windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(nint windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BringWindowToTop(nint windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(nint windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(nint windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(nint windowHandle, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(nint windowHandle, out RECT rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MoveWindow(nint windowHandle, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);

        public const int SwShow = 5;
        public const int SwMinimize = 6;
        public const int SwRestore = 9;
    }
}
