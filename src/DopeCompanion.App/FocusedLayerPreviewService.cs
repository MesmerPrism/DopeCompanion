using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using DopeCompanion.Core.Models;
using DopeCompanion.Core.Services;
using OpenCvSharp;

namespace DopeCompanion.App;

internal sealed class FocusedLayerPreviewService : IDisposable
{
    private static readonly byte[] LegacyStreamMagic = [(byte)'D', (byte)'L', (byte)'Y', (byte)'R'];
    private static readonly byte[] EncodedStreamMagic = [(byte)'D', (byte)'L', (byte)'Y', (byte)'2'];

    private const int DefaultPort = 38971;
    private const int LegacyHeaderSizeBytes = 28;
    private const int EncodedHeaderSizeBytes = 36;
    private const int MaximumPayloadBytes = 8 * 1024 * 1024;
    private const int CodecH264AnnexB = 1;
    private const int MediaCodecBufferFlagKeyFrame = 1;
    private const int MediaCodecBufferFlagCodecConfig = 2;

    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly Func<string, bool, CancellationToken, Task<OperationOutcome>> _configureReverseAsync;
    private readonly string _artifactPath;
    private readonly string _artifactTempPath;
    private readonly string _h264ArtifactPath;
    private readonly string _h264ArtifactTempPath;

    private CancellationTokenSource? _listenerCancellationTokenSource;
    private TcpListener? _listener;
    private Task? _listenerTask;
    private string _selector = string.Empty;
    private bool _disposed;
    private byte[]? _latestImageBytes;
    private byte[]? _latestCodecConfig;
    private readonly List<EncodedPacket> _currentGopPackets = [];

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
        _h264ArtifactPath = Path.ChangeExtension(_artifactPath, ".h264");
        _h264ArtifactTempPath = _h264ArtifactPath + ".tmp";
        _configureReverseAsync = configureReverseAsync ?? ((selector, removeMapping, cancellationToken) =>
            ConfigureReversePortAsync(selector, Port, removeMapping, cancellationToken));
    }

    public event EventHandler? StateChanged;

    public int Port { get; }

    public int LatestLayerMode { get; private set; } = -1;

    public int LatestWidth { get; private set; }

    public int LatestHeight { get; private set; }

    public DateTimeOffset? LatestFrameReceivedAtUtc { get; private set; }

    public DateTimeOffset? LatestDecodedFrameAtUtc { get; private set; }

    public string LatestArtifactPath => _artifactPath;

    public byte[]? LatestImageBytes => _latestImageBytes;

    public OperationOutcomeKind Level { get; private set; } = OperationOutcomeKind.Preview;

    public string Summary { get; private set; } = "Render View idle.";

    public string Detail { get; private set; } = "Start the cast surface to listen for direct Quest render-view frames.";

    public bool IsRunning => _listenerTask is { IsCompleted: false };

    public async Task<OperationOutcome> StartAsync(string selector, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(selector))
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Render View blocked.",
                "A Quest selector is required before the companion can bind adb reverse for the direct Quest render-view stream.");
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
                LatestLayerMode = -1;
                LatestWidth = 0;
                LatestHeight = 0;
                LatestFrameReceivedAtUtc = null;
                LatestDecodedFrameAtUtc = null;
                _latestImageBytes = null;
                _latestCodecConfig = null;
                _currentGopPackets.Clear();
                DeleteArtifactIfPresent(_artifactPath, _artifactTempPath, _h264ArtifactPath, _h264ArtifactTempPath);
                _listenerCancellationTokenSource = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Loopback, Port);
                _listener.Server.NoDelay = true;
                _listener.Start();
                _listenerTask = Task.Run(() => ListenLoopAsync(_listener, _listenerCancellationTokenSource.Token));

                SetState(
                    OperationOutcomeKind.Preview,
                    "Render View listening.",
                    $"Listening on 127.0.0.1:{Port} for direct Quest render-view frames from {normalizedSelector}.");
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
                "Render View listening with reverse advisory.",
                $"Listening on 127.0.0.1:{Port} for {normalizedSelector}. {reverseOutcome.Detail}".Trim());

            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Render View listening with reverse advisory.",
                $"The desktop listener is live, but adb reverse could not be confirmed for {normalizedSelector}. {reverseOutcome.Detail}".Trim(),
                Endpoint: normalizedSelector);
        }

        if (LatestFrameReceivedAtUtc is null)
        {
            SetState(
                OperationOutcomeKind.Preview,
                "Render View ready.",
                $"Listening on 127.0.0.1:{Port} and confirmed adb reverse for {normalizedSelector}. Waiting for the next Quest frame.");
        }

        return new OperationOutcome(
            shouldRestart ? OperationOutcomeKind.Success : OperationOutcomeKind.Preview,
            "Render View ready.",
            $"Listening on 127.0.0.1:{Port} and confirmed adb reverse for {normalizedSelector}. Waiting for the next Quest frame.",
            Endpoint: normalizedSelector,
            Items: [_artifactPath, _h264ArtifactPath]);
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
                    "Render View already stopped.",
                    BuildIdleDetail());

                return new OperationOutcome(
                    OperationOutcomeKind.Preview,
                    "Render View already stopped.",
                    BuildIdleDetail(),
                    Items: [_artifactPath, _h264ArtifactPath]);
            }

            var selector = _selector;
            await StopCoreAsync(removeReverseMapping: true, cancellationToken).ConfigureAwait(false);
            SetState(
                OperationOutcomeKind.Preview,
                "Render View stopped.",
                BuildIdleDetail());

            return new OperationOutcome(
                OperationOutcomeKind.Success,
                "Render View stopped.",
                string.IsNullOrWhiteSpace(selector)
                    ? BuildIdleDetail()
                    : $"Stopped listening for direct Quest render-view frames from {selector}. {BuildIdleDetail()}".Trim(),
                Endpoint: string.IsNullOrWhiteSpace(selector) ? null : selector,
                Items: [_artifactPath, _h264ArtifactPath]);
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
                    LatestFrameReceivedAtUtc is null ? "Render View stream connected." : Summary,
                    LatestFrameReceivedAtUtc is null
                        ? $"Quest connected to the Render View listener on 127.0.0.1:{Port}. Waiting for frame data."
                        : Detail);

                await ReceiveFramesAsync(client, cancellationToken).ConfigureAwait(false);

                if (!cancellationToken.IsCancellationRequested)
                {
                    SetState(
                        LatestFrameReceivedAtUtc is null ? OperationOutcomeKind.Preview : OperationOutcomeKind.Warning,
                        LatestFrameReceivedAtUtc is null ? "Render View waiting for stream." : "Render View stream disconnected.",
                        LatestFrameReceivedAtUtc is null
                            ? $"The listener is still bound to 127.0.0.1:{Port}, but the Quest has not delivered a frame yet."
                            : $"The direct Quest render-view connection closed. The last verified frame remains available at {_artifactPath}.");
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
                "Render View listener failed.",
                ex.Message);
        }
    }

    private async Task ReceiveFramesAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var stream = client.GetStream();
        var magic = new byte[4];

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!await TryReadExactlyAsync(stream, magic, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            if (magic.AsSpan().SequenceEqual(LegacyStreamMagic))
            {
                var remainder = new byte[LegacyHeaderSizeBytes - magic.Length];
                if (!await TryReadExactlyAsync(stream, remainder, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                var layerMode = BinaryPrimitives.ReadInt32LittleEndian(remainder.AsSpan(0, sizeof(int)));
                var width = BinaryPrimitives.ReadInt32LittleEndian(remainder.AsSpan(4, sizeof(int)));
                var height = BinaryPrimitives.ReadInt32LittleEndian(remainder.AsSpan(8, sizeof(int)));
                var ticks = BinaryPrimitives.ReadInt64LittleEndian(remainder.AsSpan(12, sizeof(long)));
                var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(remainder.AsSpan(20, sizeof(int)));

                ValidateFrameShape(width, height, payloadLength);

                var payload = GC.AllocateUninitializedArray<byte>(payloadLength);
                if (!await TryReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                HandleLegacyPngFrame(layerMode, width, height, ResolveTimestamp(ticks), payload);
                continue;
            }

            if (magic.AsSpan().SequenceEqual(EncodedStreamMagic))
            {
                var remainder = new byte[EncodedHeaderSizeBytes - magic.Length];
                if (!await TryReadExactlyAsync(stream, remainder, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                var codec = BinaryPrimitives.ReadInt32LittleEndian(remainder.AsSpan(0, sizeof(int)));
                var layerMode = BinaryPrimitives.ReadInt32LittleEndian(remainder.AsSpan(4, sizeof(int)));
                var width = BinaryPrimitives.ReadInt32LittleEndian(remainder.AsSpan(8, sizeof(int)));
                var height = BinaryPrimitives.ReadInt32LittleEndian(remainder.AsSpan(12, sizeof(int)));
                var ticks = BinaryPrimitives.ReadInt64LittleEndian(remainder.AsSpan(16, sizeof(long)));
                var flags = BinaryPrimitives.ReadInt32LittleEndian(remainder.AsSpan(24, sizeof(int)));
                var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(remainder.AsSpan(28, sizeof(int)));

                ValidateFrameShape(width, height, payloadLength);

                var payload = GC.AllocateUninitializedArray<byte>(payloadLength);
                if (!await TryReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                HandleEncodedFrame(codec, layerMode, width, height, ResolveTimestamp(ticks), flags, payload);
                continue;
            }

            throw new InvalidDataException("Focused layer preview packet magic mismatch.");
        }
    }

    private static void ValidateFrameShape(int width, int height, int payloadLength)
    {
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException($"Focused layer preview dimensions were invalid: {width}x{height}.");
        }

        if (payloadLength <= 0 || payloadLength > MaximumPayloadBytes)
        {
            throw new InvalidDataException($"Focused layer preview payload length {payloadLength} was out of range.");
        }
    }

    private void HandleLegacyPngFrame(int layerMode, int width, int height, DateTimeOffset receivedAtUtc, byte[] payload)
    {
        var normalizedPayload = NormalizePreviewPngPayload(payload);
        WriteBinaryArtifact(_artifactPath, _artifactTempPath, normalizedPayload);
        PublishFrame(
            layerMode,
            width,
            height,
            receivedAtUtc,
            normalizedPayload,
            $"Received {width} × {height} direct Quest PNG preview at {receivedAtUtc:HH:mm:ss} UTC on 127.0.0.1:{Port}. Latest frame saved to {_artifactPath}.");
    }

    private void HandleEncodedFrame(int codec, int layerMode, int width, int height, DateTimeOffset receivedAtUtc, int flags, byte[] payload)
    {
        if (codec != CodecH264AnnexB)
        {
            SetState(
                OperationOutcomeKind.Warning,
                "Render View codec unsupported.",
                $"Received codec id {codec} on 127.0.0.1:{Port}, but only H.264 Annex B is currently supported.");
            return;
        }

        if ((flags & MediaCodecBufferFlagCodecConfig) != 0)
        {
            _latestCodecConfig = payload.ToArray();
            SetState(
                LatestFrameReceivedAtUtc is null ? OperationOutcomeKind.Preview : Level,
                LatestFrameReceivedAtUtc is null ? "Focused layer H.264 stream configured." : Summary,
                $"Received H.264 codec configuration for {ResolveLayerLabel(layerMode)} at {receivedAtUtc:HH:mm:ss} UTC. Waiting for decodable frames.");
            return;
        }

        LatestLayerMode = layerMode;
        LatestWidth = width;
        LatestHeight = height;
        LatestFrameReceivedAtUtc = receivedAtUtc;

        if ((flags & MediaCodecBufferFlagKeyFrame) != 0)
        {
            _currentGopPackets.Clear();
        }

        if (_currentGopPackets.Count == 0 && (flags & MediaCodecBufferFlagKeyFrame) == 0)
        {
            SetState(
                OperationOutcomeKind.Preview,
                $"{ResolveLayerLabel(layerMode)} H.264 stream waiting for keyframe.",
                $"Received a dependent H.264 packet before the first keyframe for {ResolveLayerLabel(layerMode)}. Waiting for the next keyframe to rebuild a decodable GOP.");
            return;
        }

        _currentGopPackets.Add(new EncodedPacket(flags, payload.ToArray()));

        var artifactBytes = BuildH264Artifact();
        if (artifactBytes is null || artifactBytes.Length == 0)
        {
            SetState(
                LatestDecodedFrameAtUtc is null ? OperationOutcomeKind.Preview : OperationOutcomeKind.Success,
                $"{ResolveLayerLabel(layerMode)} H.264 stream live.",
                LatestDecodedFrameAtUtc is null
                    ? $"Received H.264 packet for {ResolveLayerLabel(layerMode)} at {receivedAtUtc:HH:mm:ss} UTC, but the GOP is not decodable yet."
                    : $"Received H.264 packet for {ResolveLayerLabel(layerMode)} at {receivedAtUtc:HH:mm:ss} UTC, but the latest GOP is not decodable yet. Showing the last decoded frame from {LatestDecodedFrameAtUtc:HH:mm:ss} UTC.");
            return;
        }

        WriteBinaryArtifact(_h264ArtifactPath, _h264ArtifactTempPath, artifactBytes);
        if (!TryDecodeLatestFrameFromH264Artifact(_h264ArtifactPath, out var decodedPng))
        {
            SetState(
                LatestDecodedFrameAtUtc is null ? OperationOutcomeKind.Warning : OperationOutcomeKind.Success,
                $"{ResolveLayerLabel(layerMode)} H.264 stream live.",
                LatestDecodedFrameAtUtc is null
                    ? $"Received {artifactBytes.Length:N0}-byte H.264 GOP for {ResolveLayerLabel(layerMode)} at {receivedAtUtc:HH:mm:ss} UTC, but the desktop decoder could not produce a preview frame yet. Latest GOP saved to {_h264ArtifactPath}."
                    : $"Received {artifactBytes.Length:N0}-byte H.264 GOP for {ResolveLayerLabel(layerMode)} at {receivedAtUtc:HH:mm:ss} UTC, but the desktop decoder could not produce a fresh preview frame yet. Showing the last decoded frame from {LatestDecodedFrameAtUtc:HH:mm:ss} UTC; latest GOP saved to {_h264ArtifactPath}.");
            return;
        }

        WriteBinaryArtifact(_artifactPath, _artifactTempPath, decodedPng);
        PublishFrame(
            layerMode,
            width,
            height,
            receivedAtUtc,
            decodedPng,
            $"Received {width} × {height} direct Quest H.264 preview at {receivedAtUtc:HH:mm:ss} UTC on 127.0.0.1:{Port}. Latest decoded frame saved to {_artifactPath}; latest GOP saved to {_h264ArtifactPath}.");
    }

    private byte[]? BuildH264Artifact()
    {
        if (_latestCodecConfig is null || _latestCodecConfig.Length == 0 || _currentGopPackets.Count == 0)
        {
            return null;
        }

        var totalLength = _latestCodecConfig.Length;
        foreach (var packet in _currentGopPackets)
        {
            totalLength += packet.Payload.Length;
        }

        var output = new byte[totalLength];
        var offset = 0;
        Buffer.BlockCopy(_latestCodecConfig, 0, output, offset, _latestCodecConfig.Length);
        offset += _latestCodecConfig.Length;

        foreach (var packet in _currentGopPackets)
        {
            Buffer.BlockCopy(packet.Payload, 0, output, offset, packet.Payload.Length);
            offset += packet.Payload.Length;
        }

        return output;
    }

    private static bool TryDecodeLatestFrameFromH264Artifact(string artifactPath, out byte[] pngPayload)
    {
        pngPayload = Array.Empty<byte>();
        if (!File.Exists(artifactPath))
        {
            return false;
        }

        try
        {
            using var capture = new VideoCapture(artifactPath, VideoCaptureAPIs.FFMPEG);
            if (!capture.IsOpened())
            {
                return false;
            }

            using var frame = new Mat();
            using var latestFrame = new Mat();
            while (capture.Read(frame))
            {
                if (!frame.Empty())
                {
                    frame.CopyTo(latestFrame);
                }
            }

            if (latestFrame.Empty())
            {
                return false;
            }

            Cv2.Flip(latestFrame, latestFrame, FlipMode.X);
            return Cv2.ImEncode(".png", latestFrame, out pngPayload);
        }
        catch
        {
            return false;
        }
    }

    private void PublishFrame(int layerMode, int width, int height, DateTimeOffset receivedAtUtc, byte[] imageBytes, string detail)
    {
        LatestLayerMode = layerMode;
        LatestWidth = width;
        LatestHeight = height;
        LatestFrameReceivedAtUtc = receivedAtUtc;
        LatestDecodedFrameAtUtc = receivedAtUtc;
        _latestImageBytes = imageBytes.ToArray();

        SetState(
            OperationOutcomeKind.Success,
            $"{ResolveLayerLabel(layerMode)} Render View live.",
            detail);
    }

    private static byte[] NormalizePreviewPngPayload(byte[] payload)
    {
        try
        {
            using var image = Cv2.ImDecode(payload, ImreadModes.Unchanged);
            if (image.Empty())
            {
                return payload;
            }

            Cv2.Flip(image, image, FlipMode.X);
            return Cv2.ImEncode(".png", image, out var normalized) ? normalized : payload;
        }
        catch
        {
            return payload;
        }
    }

    private static void WriteBinaryArtifact(string artifactPath, string tempPath, byte[] payload)
    {
        var directory = Path.GetDirectoryName(artifactPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(tempPath, payload);
        File.Move(tempPath, artifactPath, overwrite: true);
    }

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

    private void SetState(OperationOutcomeKind level, string summary, string detail)
    {
        Level = level;
        Summary = summary;
        Detail = detail;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private string BuildIdleDetail()
        => LatestFrameReceivedAtUtc is null
            ? "Start the cast surface to listen for direct Quest render-view frames."
            : $"The last received frame remains cached at {_artifactPath}.";

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FocusedLayerPreviewService));
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
            2 => "Pre-Brightness",
            3 => "Raw Brightness",
            4 => "Blurred Brightness",
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
                "Render View reverse unavailable.",
                "adb.exe could not be located for the Render View reverse mapping.",
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
                    ? "Render View reverse cleared."
                    : "Render View reverse ready.",
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
                ? "Render View reverse clear failed."
                : "Render View reverse failed.",
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

    private sealed record EncodedPacket(int Flags, byte[] Payload);
}
