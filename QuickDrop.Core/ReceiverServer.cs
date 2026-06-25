using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace QuickDrop.Core;

public sealed class ReceiverServer : IAsyncDisposable
{
    private readonly AppSettings _settings;
    private readonly Action<string, string>? _onTransferReceived;
    private readonly Action<PeerInfo>? _onPeerDiscovered;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public ReceiverServer(AppSettings settings, Action<string, string>? onTransferReceived = null, Action<PeerInfo>? onPeerDiscovered = null)
    {
        _settings = settings;
        _onTransferReceived = onTransferReceived;
        _onPeerDiscovered = onPeerDiscovered;
    }

    public void Start()
    {
        if (_listener is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, _settings.ReceiverPort);
        _listener.Start();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        Log.Info($"Receiver listening on TCP port {_settings.ReceiverPort}.");
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error("Receiver accept loop failed.", ex);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;
        try
        {
            await using var stream = client.GetStream();
            var header = await ProtocolIO.ReadJsonAsync<ProtocolHeader>(stream, cancellationToken).ConfigureAwait(false);
            if (header.Type == "Ping")
            {
                RememberPingSender(client, header);
                await ProtocolIO.WriteJsonAsync(stream, CreatePong(), cancellationToken).ConfigureAwait(false);
                return;
            }

            if (header.Type != "Transfer")
            {
                throw new InvalidDataException($"Unknown protocol message type: {header.Type}");
            }

            if (!_settings.AcceptIncomingTransfers)
            {
                await ProtocolIO.WriteJsonAsync(stream, new TransferResponse
                {
                    Ok = false,
                    Message = "Incoming transfers are disabled on this device."
                }, cancellationToken).ConfigureAwait(false);
                return;
            }

            var savedPath = await ReceiveArchiveAsync(stream, header, cancellationToken).ConfigureAwait(false);
            var message = $"Saved to {savedPath}";
            await ProtocolIO.WriteJsonAsync(stream, new TransferResponse
            {
                Ok = true,
                Message = message,
                SavedPath = savedPath
            }, cancellationToken).ConfigureAwait(false);

            Log.Info($"Received transfer from {header.SenderDisplayName}: {savedPath}");
            _onTransferReceived?.Invoke(header.SenderDisplayName, savedPath);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to receive QuickDrop transfer.", ex);
            try
            {
                await using var stream = client.GetStream();
                await ProtocolIO.WriteJsonAsync(stream, new TransferResponse
                {
                    Ok = false,
                    Message = ex.Message
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // The sender may already be gone.
            }
        }
    }

    private ProtocolHeader CreatePong() => new()
    {
        Type = "Pong",
        SenderDeviceId = _settings.DeviceId,
        SenderDisplayName = _settings.DisplayName,
        ReceiverPort = _settings.ReceiverPort,
        MachineName = Environment.MachineName
    };

    private void RememberPingSender(TcpClient client, ProtocolHeader header)
    {
        if (string.IsNullOrWhiteSpace(header.SenderDeviceId) ||
            string.Equals(header.SenderDeviceId, _settings.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (client.Client.RemoteEndPoint is not IPEndPoint remoteEndPoint)
        {
            return;
        }

        var endpoint = remoteEndPoint.Address.ToString();
        var port = header.ReceiverPort > 0 ? header.ReceiverPort : QuickDropConstants.DefaultReceiverPort;
        _onPeerDiscovered?.Invoke(new PeerInfo
        {
            Id = $"{header.SenderDeviceId}|direct|{endpoint}:{port}",
            DeviceId = header.SenderDeviceId,
            DisplayName = header.SenderDisplayName,
            MachineName = header.MachineName,
            Endpoint = endpoint,
            Port = port,
            Source = "Direct",
            IsReceiverConfirmed = true,
            LastSeenUtc = DateTimeOffset.UtcNow
        });
    }

    private static async Task<string> ReceiveArchiveAsync(Stream stream, ProtocolHeader header, CancellationToken cancellationToken)
    {
        if (header.ArchiveBytes < 0)
        {
            throw new InvalidDataException("Invalid archive size.");
        }

        QuickDropPaths.EnsureDirectories();
        var archivePath = Path.Combine(QuickDropPaths.TempDirectory, $"incoming-{Guid.NewGuid():N}.zip");
        try
        {
            await using (var output = File.Create(archivePath))
            using (var sha = SHA256.Create())
            {
                var buffer = new byte[1024 * 128];
                long remaining = header.ArchiveBytes;
                while (remaining > 0)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        throw new EndOfStreamException("Sender disconnected before the archive finished.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    sha.TransformBlock(buffer, 0, read, null, 0);
                    remaining -= read;
                }

                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                var actualHash = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
                if (!string.Equals(actualHash, header.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Archive hash did not match.");
                }
            }

            return await FilePackaging.ExtractAsync(archivePath, header, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (File.Exists(archivePath))
                {
                    File.Delete(archivePath);
                }
            }
            catch
            {
                // Temp cleanup can wait.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
        }

        _listener?.Stop();
        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch
            {
                // Shutdown should be quiet.
            }
        }

        _cts?.Dispose();
    }
}
