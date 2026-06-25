using System.Net.Sockets;

namespace QuickDrop.Core;

public sealed class TransferClient
{
    private readonly AppSettings _settings;

    public TransferClient(AppSettings settings)
    {
        _settings = settings;
    }

    public async Task<TransferResponse> SendAsync(PeerInfo peer, IReadOnlyList<string> selectedPaths, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        await using var package = await FilePackaging.CreateAsync(selectedPaths, cancellationToken).ConfigureAwait(false);
        using var client = new TcpClient();
        await client.ConnectAsync(peer.Endpoint, peer.Port, cancellationToken).ConfigureAwait(false);
        await using var stream = client.GetStream();

        var header = new ProtocolHeader
        {
            Type = "Transfer",
            SenderDeviceId = _settings.DeviceId,
            SenderDisplayName = _settings.DisplayName,
            TransferId = Guid.NewGuid().ToString("N"),
            PackageName = package.PackageName,
            ExtractMode = package.ExtractMode,
            ItemCount = package.ItemCount,
            ArchiveBytes = package.ArchiveBytes,
            ArchiveSha256 = package.ArchiveSha256,
            MachineName = Environment.MachineName
        };

        await ProtocolIO.WriteJsonAsync(stream, header, cancellationToken).ConfigureAwait(false);
        await using (var file = File.OpenRead(package.ArchivePath))
        {
            var buffer = new byte[1024 * 128];
            long sent = 0;
            int read;
            while ((read = await file.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                sent += read;
                progress?.Report(package.ArchiveBytes == 0 ? 1 : sent / (double)package.ArchiveBytes);
            }
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        return await ProtocolIO.ReadJsonAsync<TransferResponse>(stream, cancellationToken).ConfigureAwait(false);
    }

    public static Task<PeerInfo?> ProbeAsync(string endpoint, int port, TimeSpan timeout, CancellationToken cancellationToken) =>
        ProbeAsync(endpoint, port, timeout, settings: null, cancellationToken);

    public static async Task<PeerInfo?> ProbeAsync(string endpoint, int port, TimeSpan timeout, AppSettings? settings, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(endpoint, port, timeoutCts.Token).ConfigureAwait(false);
            await using var stream = client.GetStream();
            await ProtocolIO.WriteJsonAsync(stream, new ProtocolHeader
            {
                Type = "Ping",
                SenderDeviceId = settings?.DeviceId ?? "",
                SenderDisplayName = settings?.DisplayName ?? "",
                ReceiverPort = settings?.ReceiverPort ?? QuickDropConstants.DefaultReceiverPort,
                MachineName = Environment.MachineName
            }, timeoutCts.Token).ConfigureAwait(false);
            var pong = await ProtocolIO.ReadJsonAsync<ProtocolHeader>(stream, timeoutCts.Token).ConfigureAwait(false);
            if (pong.Type != "Pong" || string.IsNullOrWhiteSpace(pong.SenderDeviceId))
            {
                return null;
            }

            return new PeerInfo
            {
                DeviceId = pong.SenderDeviceId,
                DisplayName = pong.SenderDisplayName,
                MachineName = pong.MachineName,
                Endpoint = endpoint,
                Port = pong.ReceiverPort > 0 ? pong.ReceiverPort : port,
                IsReceiverConfirmed = true,
                LastSeenUtc = DateTimeOffset.UtcNow
            };
        }
        catch
        {
            return null;
        }
    }
}
