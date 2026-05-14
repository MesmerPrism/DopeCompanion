using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using DopeCompanion.Core.Models;
using DopeCompanion.Core.Services;

namespace DopeCompanion.App;

internal sealed class MediaProjectionStreamService : IDisposable
{
    private const int DefaultPort = 8787;
    private const string DefaultMakepadPackage = "makepad-example-particles-xr";
    private const string DefaultAppLabel = "Rusty Dope XR";

    private static readonly (string Key, string Value)[] CapturePropDefaults =
    [
        ("debug.rustydope.xr_capture_dataset_enabled", "0"),
        ("debug.rustydope.xr_capture_mode", "native_plus_custom_overlay"),
        ("debug.rustydope.xr_capture_pixels", "0"),
        ("debug.rustydope.xr_capture_camera_pixels", "0"),
        ("debug.rustydope.xr_capture_depth", "0")
    ];

    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly string _artifactPath;
    private readonly string _artifactTempPath;

    private CancellationTokenSource? _pollCancellationTokenSource;
    private Task? _pollTask;
    private Process? _bridgeProcess;
    private Task? _stdoutTask;
    private Task? _stderrTask;
    private string _selector = string.Empty;
    private string _rustyDopeRoot = string.Empty;
    private string _lastBridgeOutputLine = string.Empty;
    private bool _disposed;
    private byte[]? _latestImageBytes;
    private int _latestBridgeFrameCount;

    public MediaProjectionStreamService(int port = DefaultPort, string? artifactPath = null)
    {
        Port = port > 0 ? port : DefaultPort;
        _artifactPath = string.IsNullOrWhiteSpace(artifactPath)
            ? Path.Combine(CompanionOperatorDataLayout.ScreenshotsRootPath, "cast-preview", "media-projection-latest.png")
            : Path.GetFullPath(artifactPath);
        _artifactTempPath = _artifactPath + ".tmp";
    }

    public event EventHandler? StateChanged;

    public int Port { get; }

    public string BridgeUrl => $"http://127.0.0.1:{Port}/";

    public int LatestWidth { get; private set; }

    public int LatestHeight { get; private set; }

    public DateTimeOffset? LatestFrameReceivedAtUtc { get; private set; }

    public string LatestArtifactPath => _artifactPath;

    public byte[]? LatestImageBytes => _latestImageBytes;

    public OperationOutcomeKind Level { get; private set; } = OperationOutcomeKind.Preview;

    public string Summary { get; private set; } = "MediaProjection idle.";

    public string Detail { get; private set; } = "Start MediaProjection to stream Rusty-DOPE composite capture frames through the desktop bridge.";

    public string LastBridgeOutputLine => _lastBridgeOutputLine;

    public bool IsRunning
        => _bridgeProcess is { HasExited: false } ||
           _pollTask is { IsCompleted: false };

    public async Task<OperationOutcome> StartAsync(string selector, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(selector))
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "MediaProjection blocked.",
                "A Quest selector is required before the companion can start the Rusty-DOPE composite capture bridge.");
        }

        var rustyDopeRoot = AppAssetLocator.TryResolveRustyDopeRoot();
        if (string.IsNullOrWhiteSpace(rustyDopeRoot))
        {
            return new OperationOutcome(
                OperationOutcomeKind.Failure,
                "MediaProjection bridge unavailable.",
                "Set DOPE_RUSTY_DOPE_ROOT or keep Rusty-DOPE at C:\\Users\\<user>\\source\\repos\\Rusty-DOPE so the companion can find scripts\\composite-capture-bridge.py.",
                Endpoint: selector);
        }

        var scriptPath = Path.Combine(rustyDopeRoot, "scripts", "composite-capture-bridge.py");
        if (!File.Exists(scriptPath))
        {
            return new OperationOutcome(
                OperationOutcomeKind.Failure,
                "MediaProjection bridge unavailable.",
                $"Rusty-DOPE was found at {rustyDopeRoot}, but scripts\\composite-capture-bridge.py is missing.",
                Endpoint: selector);
        }

        var normalizedSelector = selector.Trim();
        var shouldRestart = false;

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            shouldRestart = !IsRunning ||
                !string.Equals(_selector, normalizedSelector, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_rustyDopeRoot, rustyDopeRoot, StringComparison.OrdinalIgnoreCase);
            if (shouldRestart)
            {
                await StopCoreAsync(resetCaptureProps: true, CancellationToken.None).ConfigureAwait(false);

                _selector = normalizedSelector;
                _rustyDopeRoot = rustyDopeRoot;
                _latestImageBytes = null;
                _latestBridgeFrameCount = 0;
                _lastBridgeOutputLine = string.Empty;
                LatestWidth = 0;
                LatestHeight = 0;
                LatestFrameReceivedAtUtc = null;
                DeleteArtifactIfPresent(_artifactPath, _artifactTempPath);

                var startOutcome = StartBridgeProcess(rustyDopeRoot, scriptPath, normalizedSelector);
                if (startOutcome.Kind == OperationOutcomeKind.Failure)
                {
                    SetState(startOutcome.Kind, startOutcome.Summary, startOutcome.Detail);
                    return startOutcome;
                }

                _pollCancellationTokenSource = new CancellationTokenSource();
                _pollTask = Task.Run(() => PollBridgeLoopAsync(_pollCancellationTokenSource.Token));

                SetState(
                    OperationOutcomeKind.Preview,
                    "MediaProjection bridge starting.",
                    $"Started Rusty-DOPE composite capture bridge for {normalizedSelector} on {BridgeUrl}. Waiting for the first frame.");
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }

        return new OperationOutcome(
            shouldRestart ? OperationOutcomeKind.Success : OperationOutcomeKind.Preview,
            "MediaProjection bridge ready.",
            $"Rusty-DOPE composite capture bridge is serving {BridgeUrl}. The companion is polling latest.png for the overlay surface.",
            Endpoint: normalizedSelector,
            Items: [_artifactPath]);
    }

    public async Task<OperationOutcome> StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsRunning && string.IsNullOrWhiteSpace(_selector))
            {
                SetState(OperationOutcomeKind.Preview, "MediaProjection already stopped.", BuildIdleDetail());
                return new OperationOutcome(
                    OperationOutcomeKind.Preview,
                    "MediaProjection already stopped.",
                    BuildIdleDetail(),
                    Items: [_artifactPath]);
            }

            var selector = _selector;
            await StopCoreAsync(resetCaptureProps: true, cancellationToken).ConfigureAwait(false);
            SetState(OperationOutcomeKind.Preview, "MediaProjection stopped.", BuildIdleDetail());
            return new OperationOutcome(
                OperationOutcomeKind.Success,
                "MediaProjection stopped.",
                string.IsNullOrWhiteSpace(selector)
                    ? BuildIdleDetail()
                    : $"Stopped the Rusty-DOPE MediaProjection bridge for {selector}. {BuildIdleDetail()}".Trim(),
                Endpoint: string.IsNullOrWhiteSpace(selector) ? null : selector,
                Items: [_artifactPath]);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }

        _httpClient.Dispose();
        _lifecycleGate.Dispose();
    }

    private OperationOutcome StartBridgeProcess(string rustyDopeRoot, string scriptPath, string selector)
    {
        try
        {
            var python = ResolvePythonInvocation();
            var startInfo = new ProcessStartInfo
            {
                FileName = python.FileName,
                WorkingDirectory = rustyDopeRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var argument in python.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add("--serial");
            startInfo.ArgumentList.Add(selector);
            startInfo.ArgumentList.Add("--skip-build");
            startInfo.ArgumentList.Add("--port");
            startInfo.ArgumentList.Add(Port.ToString(CultureInfo.InvariantCulture));
            var makepadPackage = ResolveMakepadPackage();
            if (!string.IsNullOrWhiteSpace(makepadPackage))
            {
                startInfo.ArgumentList.Add("--makepad-package");
                startInfo.ArgumentList.Add(makepadPackage);
            }

            startInfo.ArgumentList.Add("--package-name");
            startInfo.ArgumentList.Add("com.tillh.rustydopexr");
            var appLabel = ResolveAppLabel();
            if (!string.IsNullOrWhiteSpace(appLabel))
            {
                startInfo.ArgumentList.Add("--app-label");
                startInfo.ArgumentList.Add(appLabel);
            }

            _bridgeProcess = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            _bridgeProcess.Exited += OnBridgeProcessExited;

            if (!_bridgeProcess.Start())
            {
                return new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "MediaProjection bridge failed.",
                    "The Python bridge process did not start.",
                    Endpoint: selector);
            }

            _stdoutTask = Task.Run(() => ReadBridgeOutputAsync(_bridgeProcess.StandardOutput, isError: false));
            _stderrTask = Task.Run(() => ReadBridgeOutputAsync(_bridgeProcess.StandardError, isError: true));

            return new OperationOutcome(
                OperationOutcomeKind.Success,
                "MediaProjection bridge launched.",
                $"Started {Path.GetFileName(scriptPath)} with {python.FileName} for {selector}.",
                Endpoint: selector);
        }
        catch (Exception ex)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Failure,
                "MediaProjection bridge failed.",
                ex.Message,
                Endpoint: selector);
        }
    }

    private async Task PollBridgeLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_bridgeProcess is { HasExited: true } process)
                {
                    SetState(
                        LatestFrameReceivedAtUtc is null ? OperationOutcomeKind.Warning : OperationOutcomeKind.Preview,
                        "MediaProjection bridge exited.",
                        $"Rusty-DOPE composite capture bridge exited with code {process.ExitCode}. Last frame count: {_latestBridgeFrameCount}. {FormatLastBridgeOutputDetail()}".Trim());
                    return;
                }

                var bridgeState = await TryReadBridgeStateAsync(cancellationToken).ConfigureAwait(false);
                if (bridgeState.LastError is { Length: > 0 } error && LatestFrameReceivedAtUtc is null)
                {
                    SetState(
                        OperationOutcomeKind.Warning,
                        "MediaProjection waiting for frames.",
                        $"Bridge is serving {BridgeUrl}, but no composite frame is available yet. {error}");
                }

                if (bridgeState.FrameCount <= _latestBridgeFrameCount)
                {
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var bytes = await _httpClient.GetByteArrayAsync(
                    $"{BridgeUrl}latest.png?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                    cancellationToken).ConfigureAwait(false);
                if (bytes.Length == 0)
                {
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                SaveLatestFrame(bytes);
                _latestBridgeFrameCount = bridgeState.FrameCount;
                SetState(
                    OperationOutcomeKind.Success,
                    "MediaProjection live.",
                    $"Rusty-DOPE composite capture frame {_latestBridgeFrameCount} is streaming on {BridgeUrl}. Latest frame saved to {CompanionOperatorDataLayout.NormalizeHostVisiblePath(_artifactPath)}.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (LatestFrameReceivedAtUtc is null)
                {
                    SetState(
                        OperationOutcomeKind.Preview,
                        "MediaProjection waiting for bridge.",
                        $"Polling {BridgeUrl}latest.png. {ex.Message}");
                }
            }

            try
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task<BridgeStateSnapshot> TryReadBridgeStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await _httpClient.GetStreamAsync(
                $"{BridgeUrl}state.json?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            var frameCount = root.TryGetProperty("frame_count", out var frameElement) && frameElement.TryGetInt32(out var parsedFrameCount)
                ? parsedFrameCount
                : 0;
            var lastError = root.TryGetProperty("last_error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String
                ? errorElement.GetString()
                : null;
            return new BridgeStateSnapshot(frameCount, lastError ?? string.Empty);
        }
        catch
        {
            return new BridgeStateSnapshot(_latestBridgeFrameCount, string.Empty);
        }
    }

    private async Task ReadBridgeOutputAsync(StreamReader reader, bool isError)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                _lastBridgeOutputLine = trimmed;
                if (isError || trimmed.Contains("warning:", StringComparison.OrdinalIgnoreCase))
                {
                    SetState(
                        LatestFrameReceivedAtUtc is null ? OperationOutcomeKind.Warning : OperationOutcomeKind.Preview,
                        "MediaProjection bridge advisory.",
                        trimmed);
                }
            }
        }
        catch
        {
        }
    }

    private async Task StopCoreAsync(bool resetCaptureProps, CancellationToken cancellationToken)
    {
        var selector = _selector;
        var rustyDopeRoot = _rustyDopeRoot;
        var pollCts = _pollCancellationTokenSource;
        var pollTask = _pollTask;
        var process = _bridgeProcess;

        _pollCancellationTokenSource = null;
        _pollTask = null;
        _bridgeProcess = null;
        _stdoutTask = null;
        _stderrTask = null;
        _selector = string.Empty;

        if (pollCts is not null)
        {
            try
            {
                pollCts.Cancel();
            }
            catch
            {
            }
        }

        if (process is not null)
        {
            try
            {
                process.Exited -= OnBridgeProcessExited;
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                using var exitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                exitCts.CancelAfter(TimeSpan.FromSeconds(2));
                await process.WaitForExitAsync(exitCts.Token).ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        if (pollTask is not null)
        {
            try
            {
                await pollTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        pollCts?.Dispose();

        if (resetCaptureProps && !string.IsNullOrWhiteSpace(selector) && !string.IsNullOrWhiteSpace(rustyDopeRoot))
        {
            await ResetCapturePropsAsync(rustyDopeRoot, selector, cancellationToken).ConfigureAwait(false);
        }
    }

    private void OnBridgeProcessExited(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        SetState(
            LatestFrameReceivedAtUtc is null ? OperationOutcomeKind.Warning : OperationOutcomeKind.Preview,
            "MediaProjection bridge exited.",
            $"The Rusty-DOPE composite capture bridge process exited. {FormatLastBridgeOutputDetail()}".Trim());
    }

    private void SaveLatestFrame(byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_artifactPath) ?? ".");
        File.WriteAllBytes(_artifactTempPath, bytes);
        File.Move(_artifactTempPath, _artifactPath, overwrite: true);
        _latestImageBytes = bytes;
        LatestFrameReceivedAtUtc = DateTimeOffset.UtcNow;
        if (TryReadPngSize(bytes, out var width, out var height))
        {
            LatestWidth = width;
            LatestHeight = height;
        }
    }

    private async Task ResetCapturePropsAsync(string rustyDopeRoot, string selector, CancellationToken cancellationToken)
    {
        var adbPath = ResolveAdbExecutable(rustyDopeRoot);
        if (string.IsNullOrWhiteSpace(adbPath))
        {
            return;
        }

        foreach (var (key, value) in CapturePropDefaults)
        {
            try
            {
                await RunProcessAsync(
                    adbPath,
                    ["-s", selector, "shell", "setprop", key, value],
                    rustyDopeRoot,
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private static async Task RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return;
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void SetState(OperationOutcomeKind level, string summary, string detail)
    {
        Level = level;
        Summary = summary;
        Detail = detail;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MediaProjectionStreamService));
        }
    }

    private string BuildIdleDetail()
        => "Start MediaProjection to stream Rusty-DOPE composite capture frames through the desktop bridge.";

    private string FormatLastBridgeOutputDetail()
        => string.IsNullOrWhiteSpace(_lastBridgeOutputLine)
            ? string.Empty
            : $"Last bridge output: {_lastBridgeOutputLine}";

    private static bool TryReadPngSize(byte[] png, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (png.Length < 24 ||
            png[0] != 0x89 ||
            png[1] != 0x50 ||
            png[2] != 0x4E ||
            png[3] != 0x47)
        {
            return false;
        }

        width = ReadBigEndianInt32(png, 16);
        height = ReadBigEndianInt32(png, 20);
        return width > 0 && height > 0;
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset)
        => (bytes[offset] << 24) |
           (bytes[offset + 1] << 16) |
           (bytes[offset + 2] << 8) |
           bytes[offset + 3];

    private static void DeleteArtifactIfPresent(params string[] paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }

    private static PythonInvocation ResolvePythonInvocation()
    {
        var configuredPython = Environment.GetEnvironmentVariable("PYTHON");
        if (TryResolveExecutable(configuredPython, out var configuredPath))
        {
            return new PythonInvocation(configuredPath, []);
        }

        foreach (var candidate in new[] { "python.exe", "python", "py.exe", "py" })
        {
            if (!TryResolveExecutable(candidate, out var path))
            {
                continue;
            }

            var fileName = Path.GetFileName(path);
            return IsPythonLauncher(fileName)
                ? new PythonInvocation(path, ["-3"])
                : new PythonInvocation(path, []);
        }

        return new PythonInvocation("python", []);
    }

    private static string ResolveMakepadPackage()
        => ResolveOptionalEnvironmentValue("DOPE_MEDIA_PROJECTION_MAKEPAD_PACKAGE") ?? DefaultMakepadPackage;

    private static string ResolveAppLabel()
        => ResolveOptionalEnvironmentValue("DOPE_MEDIA_PROJECTION_APP_LABEL") ?? DefaultAppLabel;

    private static string? ResolveOptionalEnvironmentValue(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? ResolveAdbExecutable(string rustyDopeRoot)
    {
        var localAdb = Path.Combine(rustyDopeRoot, ".makepad-android-sdk", "platform-tools", "adb.exe");
        if (File.Exists(localAdb))
        {
            return localAdb;
        }

        return TryResolveExecutable("adb.exe", out var adbPath) || TryResolveExecutable("adb", out adbPath)
            ? adbPath
            : null;
    }

    private static bool TryResolveExecutable(string? candidate, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var trimmed = candidate.Trim().Trim('"');
        if (Path.IsPathRooted(trimmed) && File.Exists(trimmed))
        {
            path = Path.GetFullPath(trimmed);
            return true;
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        string[] suffixes = OperatingSystem.IsWindows()
            ? ((Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            : [string.Empty];
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var suffix in suffixes)
            {
                var fileName = trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                    ? trimmed
                    : trimmed + suffix;
                var fullPath = Path.Combine(directory, fileName);
                if (File.Exists(fullPath))
                {
                    path = fullPath;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsPythonLauncher(string fileName)
        => string.Equals(fileName, "py", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(fileName, "py.exe", StringComparison.OrdinalIgnoreCase);

    private sealed record PythonInvocation(string FileName, IReadOnlyList<string> Arguments);

    private sealed record BridgeStateSnapshot(int FrameCount, string LastError);
}
