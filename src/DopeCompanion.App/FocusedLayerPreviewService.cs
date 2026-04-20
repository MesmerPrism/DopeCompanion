using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using DopeCompanion.Core.Models;
using DopeCompanion.Core.Services;

namespace DopeCompanion.App;

internal sealed class FocusedLayerPreviewService : IDisposable
{
    private static readonly byte[] StreamMagic = [(byte)'D', (byte)'L', (byte)'Y', (byte)'R'];

    private const int DefaultPort = 38971;
    private const int HeaderSizeBytes = 28;
    private const int MaximumPayloadBytes = 8 * 1024 * 1024;

    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly Func<string, bool, CancellationToken, Task<OperationOutcome>> _configureReverseAsync;
    private readonly string _artifactPath;
    private readonly string _artifactTempPath;

    private CancellationTokenSource? _listenerCancellationTokenSource;
    private TcpListener? _listener;
    private Task? _listenerTask;
    private string _selector = string.Empty;
    private bool _disposed;

    public FocusedLayerPreviewService(
        int port = DefaultPort,
        string? artifactPath = null,
        Func<string, bool, CancellationToken, Task<OperationOutcome>>? configureReverseAsync = null)
    {
        Port = port > 0 ? port : DefaultPort;
        _artifactPath = string.IsNullOrWhiteSpace(artifactPath)
            ? Path.Combine(CompanionOperatorDataLayout.ScreenshotsRootPath, "cast-preview", "focused-layer-preview-latest.png")
            : Path.GetFullPath(artifactPath);
        _artifactTempPath = _artifactPath + ".tmp";
        _configureReverseAsync = configureReverseAsync ?? ((selector, removeMapping, cancellationToken) =>
            ConfigureReversePortAsync(selector, Port, removeMapping, cancellationToken));
    }

    public event EventHandler? StateChanged;

    public int Port { get; }

    public int LatestLayerMode { get; private set; } = -1;

    public int LatestWidth { get; private set; }

    public int LatestHeight { get; private set; }

    public DateTimeOffset? LatestFrameReceivedAtUtc { get; private set; }

    public string LatestArtifactPath => _artifactPath;

    public OperationOutcomeKind Level { get; private set; } = OperationOutcomeKind.Preview;

    public string Summary { get; private set; } = "Focused layer preview idle.";

    public string Detail { get; private set; } = "Start Display 0 cast to listen for direct Quest layer preview frames.";

    public bool IsRunning => _listenerTask is { IsCompleted: false };

    public async Task<OperationOutcome> StartAsync(string selector, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(selector))
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Focused layer preview blocked.",
                "A Quest selector is required before the companion can bind adb reverse for the focused layer preview stream.");
        }

        var normalizedSelector = selector.Trim();
        var shouldRestart = false;

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            shouldRestart = !IsRunning || !string.Equals(_selector, normalizedSelector, StringComparison.OrdinalIgnoreCase);
            if (shouldRestart)
            {
                await StopCoreAsync(removeReverseMapping: true, CancellationToken.None).ConfigureAwait(false);

                _selector = normalizedSelector;
                _listenerCancellationTokenSource = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Loopback, Port);
                _listener.Server.NoDelay = true;
                _listener.Start();
                _listenerTask = Task.Run(() => ListenLoopAsync(_listener, _listenerCancellationTokenSource.Token));

                SetState(
                    OperationOutcomeKind.Preview,
                    "Focused layer preview listening.",
                    $"Listening on 127.0.0.1:{Port} for direct Quest layer preview frames from {normalizedSelector}.");
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }

        var reverseOutcome = await _configureReverseAsync(normalizedSelector, false, cancellationToken).ConfigureAwait(false);
        if (reverseOutcome.Kind is OperationOutcomeKind.Warning or OperationOutcomeKind.Failure)
        {
            SetState(
                OperationOutcomeKind.Warning,
                "Focused layer preview listening with reverse advisory.",
                $"Listening on 127.0.0.1:{Port} for {normalizedSelector}. {reverseOutcome.Detail}".Trim());

            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Focused layer preview listening with reverse advisory.",
                $"The desktop listener is live, but adb reverse could not be confirmed for {normalizedSelector}. {reverseOutcome.Detail}".Trim(),
                Endpoint: normalizedSelector);
        }

        if (LatestFrameReceivedAtUtc is null)
        {
            SetState(
                OperationOutcomeKind.Preview,
                "Focused layer preview ready.",
                $"Listening on 127.0.0.1:{Port} and confirmed adb reverse for {normalizedSelector}. Waiting for the next Quest frame.");
        }

        return new OperationOutcome(
            shouldRestart ? OperationOutcomeKind.Success : OperationOutcomeKind.Preview,
            "Focused layer preview ready.",
            $"Listening on 127.0.0.1:{Port} and confirmed adb reverse for {normalizedSelector}. Waiting for the next Quest frame.",
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
                SetState(
                    OperationOutcomeKind.Preview,
                    "Focused layer preview already stopped.",
                    BuildIdleDetail());

                return new OperationOutcome(
                    OperationOutcomeKind.Preview,
                    "Focused layer preview already stopped.",
                    BuildIdleDetail(),
                    Items: [_artifactPath]);
            }

            var selector = _selector;
            await StopCoreAsync(removeReverseMapping: true, cancellationToken).ConfigureAwait(false);
            SetState(
                OperationOutcomeKind.Preview,
                "Focused layer preview stopped.",
                BuildIdleDetail());

            return new OperationOutcome(
                OperationOutcomeKind.Success,
                "Focused layer preview stopped.",
                string.IsNullOrWhiteSpace(selector)
                    ? BuildIdleDetail()
                    : $"Stopped listening for direct Quest layer preview frames from {selector}. {BuildIdleDetail()}".Trim(),
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

        _lifecycleGate.Dispose();
    }

    private async Task StopCoreAsync(bool removeReverseMapping, CancellationToken cancellationToken)
    {
        var selector = _selector;
        var listenerTask = _listenerTask;
        var listener = _listener;
        var cancellationSource = _listenerCancellationTokenSource;

        _listenerTask = null;
        _listener = null;
        _listenerCancellationTokenSource = null;
        _selector = string.Empty;

        if (cancellationSource is not null)
        {
            cancellationSource.Cancel();
        }

        try
        {
            listener?.Stop();
        }
        catch
        {
        }

        if (listenerTask is not null)
        {
            try
            {
                await listenerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        cancellationSource?.Dispose();

        if (removeReverseMapping && !string.IsNullOrWhiteSpace(selector))
        {
            try
            {
                await _configureReverseAsync(selector, true, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private async Task ListenLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                client.NoDelay = true;
                SetState(
                    LatestFrameReceivedAtUtc is null ? OperationOutcomeKind.Preview : OperationOutcomeKind.Success,
                    LatestFrameReceivedAtUtc is null ? "Focused layer preview stream connected." : Summary,
                    LatestFrameReceivedAtUtc is null
                        ? $"Quest connected to the focused layer preview listener on 127.0.0.1:{Port}. Waiting for frame data."
                        : Detail);

                await ReceiveFramesAsync(client, cancellationToken).ConfigureAwait(false);

                if (!cancellationToken.IsCancellationRequested)
                {
                    SetState(
                        LatestFrameReceivedAtUtc is null ? OperationOutcomeKind.Preview : OperationOutcomeKind.Warning,
                        LatestFrameReceivedAtUtc is null ? "Focused layer preview waiting for stream." : "Focused layer preview stream disconnected.",
                        LatestFrameReceivedAtUtc is null
                            ? $"The listener is still bound to 127.0.0.1:{Port}, but the Quest has not delivered a frame yet."
                            : $"The direct Quest layer preview connection closed. The last verified frame remains available at {_artifactPath}.");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetState(
                OperationOutcomeKind.Warning,
                "Focused layer preview listener failed.",
                ex.Message);
        }
    }

    private async Task ReceiveFramesAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var stream = client.GetStream();
        var header = new byte[HeaderSizeBytes];

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!await TryReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            if (!header.AsSpan(0, StreamMagic.Length).SequenceEqual(StreamMagic))
            {
                throw new InvalidDataException("Focused layer preview packet magic mismatch.");
            }

            var layerMode = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, sizeof(int)));
            var width = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8, sizeof(int)));
            var height = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12, sizeof(int)));
            var ticks = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(16, sizeof(long)));
            var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(24, sizeof(int)));
            if (width <= 0 || height <= 0)
            {
                throw new InvalidDataException($"Focused layer preview dimensions were invalid: {width}x{height}.");
            }

            if (payloadLength <= 0 || payloadLength > MaximumPayloadBytes)
            {
                throw new InvalidDataException($"Focused layer preview payload length {payloadLength} was out of range.");
            }

            var payload = GC.AllocateUninitializedArray<byte>(payloadLength);
            if (!await TryReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            WritePreviewArtifact(payload);
            PublishFrame(
                layerMode,
                width,
                height,
                ResolveTimestamp(ticks));
        }
    }

    private static async Task<bool> TryReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken).ConfigureAwait(false);
            if (bytesRead <= 0)
            {
                return false;
            }

            offset += bytesRead;
        }

        return true;
    }

    private void PublishFrame(int layerMode, int width, int height, DateTimeOffset receivedAtUtc)
    {
        LatestLayerMode = layerMode;
        LatestWidth = width;
        LatestHeight = height;
        LatestFrameReceivedAtUtc = receivedAtUtc;

        SetState(
            OperationOutcomeKind.Success,
            $"{ResolveLayerLabel(layerMode)} preview live.",
            $"Received {width} × {height} direct Quest layer preview at {receivedAtUtc:HH:mm:ss} UTC on 127.0.0.1:{Port}. Latest frame saved to {_artifactPath}.");
    }

    private void WritePreviewArtifact(byte[] payload)
    {
        var directory = Path.GetDirectoryName(_artifactPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(_artifactTempPath, payload);
        File.Move(_artifactTempPath, _artifactPath, overwrite: true);
    }

    private void SetState(OperationOutcomeKind level, string summary, string detail)
    {
        Level = level;
        Summary = summary;
        Detail = detail;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private string BuildIdleDetail()
        => LatestFrameReceivedAtUtc is null
            ? "Start Display 0 cast to listen for direct Quest layer preview frames."
            : $"The last received frame remains cached at {_artifactPath}.";

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FocusedLayerPreviewService));
        }
    }

    private static DateTimeOffset ResolveTimestamp(long ticks)
    {
        try
        {
            if (ticks > DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks)
            {
                return new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc));
            }
        }
        catch
        {
        }

        return DateTimeOffset.UtcNow;
    }

    private static string ResolveLayerLabel(int layerMode)
        => layerMode switch
        {
            0 => "Composite",
            1 => "Raw Feed",
            2 => "Pre-Blur",
            3 => "Raw Strength",
            4 => "Blurred Strength",
            5 => "Depth",
            _ => $"Layer {layerMode}"
        };

    private static async Task<OperationOutcome> ConfigureReversePortAsync(string selector, int port, bool removeMapping, CancellationToken cancellationToken)
    {
        var adbPath = ResolveAdbPath();
        if (string.IsNullOrWhiteSpace(adbPath))
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Focused layer preview reverse unavailable.",
                "adb.exe could not be located for the focused layer preview reverse mapping.",
                Endpoint: selector);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = adbPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in removeMapping
                     ? new[] { "-s", selector, "reverse", "--remove", $"tcp:{port}" }
                     : new[] { "-s", selector, "reverse", $"tcp:{port}", $"tcp:{port}" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stdout = (await stdoutTask.ConfigureAwait(false)).Trim();
        var stderr = (await stderrTask.ConfigureAwait(false)).Trim();
        var detail = string.Join(
            " ",
            new[] { stdout, stderr }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();

        if (process.ExitCode == 0)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Success,
                removeMapping
                    ? "Focused layer preview reverse cleared."
                    : "Focused layer preview reverse ready.",
                string.IsNullOrWhiteSpace(detail)
                    ? removeMapping
                        ? $"Removed adb reverse tcp:{port} for {selector}."
                        : $"Bound adb reverse tcp:{port} -> tcp:{port} for {selector}."
                    : detail,
                Endpoint: selector);
        }

        return new OperationOutcome(
            OperationOutcomeKind.Warning,
            removeMapping
                ? "Focused layer preview reverse clear failed."
                : "Focused layer preview reverse failed.",
            string.IsNullOrWhiteSpace(detail)
                ? $"adb reverse exited with code {process.ExitCode} for {selector}."
                : detail,
            Endpoint: selector);
    }

    private static string? ResolveAdbPath()
    {
        var candidates = new List<string?>(8)
        {
            Environment.GetEnvironmentVariable("DOPE_ADB_EXE"),
            OfficialQuestToolingLayout.AdbExecutablePath,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Android",
                "Sdk",
                "platform-tools",
                "adb.exe")
        };

        foreach (var environmentVariable in new[] { "ANDROID_SDK_ROOT", "ANDROID_HOME" })
        {
            var value = Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                candidates.Add(Path.Combine(value, "platform-tools", "adb.exe"));
            }
        }

        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            candidates.Add(Path.Combine(entry, "adb.exe"));
            candidates.Add(Path.Combine(entry, "adb"));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(candidate.Trim().Trim('"'));
            }
            catch
            {
                continue;
            }

            if (seen.Add(fullPath) && File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }
}
