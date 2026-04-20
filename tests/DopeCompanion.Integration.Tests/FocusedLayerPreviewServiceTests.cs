using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using DopeCompanion.App;
using DopeCompanion.Core.Models;

namespace DopeCompanion.Integration.Tests;

public sealed class FocusedLayerPreviewServiceTests
{
    [Fact]
    public async Task Service_receives_direct_preview_packet_and_writes_latest_frame_artifact()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "dope-companion-focused-layer-preview-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var artifactPath = Path.Combine(tempRoot, "focused-layer-preview.png");
        var port = ReserveFreeTcpPort();

        try
        {
            using var service = new FocusedLayerPreviewService(
                port,
                artifactPath,
                (selector, removeMapping, cancellationToken) => Task.FromResult(new OperationOutcome(
                    OperationOutcomeKind.Success,
                    removeMapping ? "Reverse cleared." : "Reverse ready.",
                    $"Mock reverse action for {selector}.")));

            var startOutcome = await service.StartAsync("test-selector");
            Assert.NotEqual(OperationOutcomeKind.Failure, startOutcome.Kind);

            var payload = CreatePngPayload();
            var packet = BuildPacket(layerMode: 0, width: 1, height: 1, payload);

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            await client.GetStream().WriteAsync(packet);
            await client.GetStream().FlushAsync();

            await WaitForConditionAsync(
                () => service.LatestFrameReceivedAtUtc is not null && File.Exists(artifactPath),
                TimeSpan.FromSeconds(5));

            Assert.NotNull(service.LatestFrameReceivedAtUtc);
            Assert.Equal(1, service.LatestWidth);
            Assert.Equal(1, service.LatestHeight);
            Assert.Equal(0, service.LatestLayerMode);
            Assert.Equal(OperationOutcomeKind.Success, service.Level);
            Assert.Contains("Composite", service.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(artifactPath));
            Assert.Equal(payload, await File.ReadAllBytesAsync(artifactPath));
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static byte[] BuildPacket(int layerMode, int width, int height, byte[] payload)
    {
        var packet = new byte[28 + payload.Length];
        packet[0] = (byte)'D';
        packet[1] = (byte)'L';
        packet[2] = (byte)'Y';
        packet[3] = (byte)'R';
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4, sizeof(int)), layerMode);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8, sizeof(int)), width);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(12, sizeof(int)), height);
        BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(16, sizeof(long)), DateTime.UtcNow.Ticks);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(24, sizeof(int)), payload.Length);
        Buffer.BlockCopy(payload, 0, packet, 28, payload.Length);
        return packet;
    }

    private static async Task WaitForConditionAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Condition was not satisfied before the timeout elapsed.");
    }

    private static int ReserveFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static byte[] CreatePngPayload()
        => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2G7R0AAAAASUVORK5CYII=");
}
