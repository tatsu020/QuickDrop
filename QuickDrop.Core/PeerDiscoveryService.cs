using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace QuickDrop.Core;

public sealed class PeerDiscoveryService : IAsyncDisposable
{
    private readonly AppSettings _settings;
    private readonly ConcurrentDictionary<string, PeerInfo> _peers = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;
    private UdpClient? _receiveClient;
    private UdpClient? _sendClient;
    private Task? _receiveLoop;
    private Task? _broadcastLoop;
    private Task? _tailscaleLoop;
    private Task? _manualLoop;
    private readonly SemaphoreSlim _manualProbeGate = new(1, 1);

    public event EventHandler? PeersChanged;

    public PeerDiscoveryService(AppSettings settings)
    {
        _settings = settings;
    }

    public IReadOnlyList<PeerInfo> GetSnapshot() =>
        _peers.Values
            .Where(peer => DateTimeOffset.UtcNow - peer.LastSeenUtc < TimeSpan.FromMinutes(5))
            .OrderBy(peer => peer.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(peer => peer.MenuTitle, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    public void Start()
    {
        if (_cts is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _receiveClient = new UdpClient();
        _receiveClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _receiveClient.Client.Bind(new IPEndPoint(IPAddress.Any, QuickDropConstants.DiscoveryPort));
        _sendClient = new UdpClient { EnableBroadcast = true };

        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        _broadcastLoop = Task.Run(() => BroadcastLoopAsync(_cts.Token));
        _tailscaleLoop = Task.Run(() => TailscaleProbeLoopAsync(_cts.Token));
        _manualLoop = Task.Run(() => ManualProbeLoopAsync(_cts.Token));
        Log.Info($"Discovery started on UDP port {QuickDropConstants.DiscoveryPort}.");
    }

    public void AddDirectPeer(PeerInfo peer)
    {
        if (peer.DeviceId == _settings.DeviceId)
        {
            return;
        }

        UpsertPeer(peer);
    }

    public void RemoveManualPeerEntries(string host, int port)
    {
        var removed = false;
        var suffix = $"|manual|{host}:{port}";
        foreach (var peer in _peers)
        {
            if (peer.Value.Source == "Manual" &&
                (string.Equals(peer.Value.Endpoint, host, StringComparison.OrdinalIgnoreCase) ||
                 peer.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            {
                removed |= _peers.TryRemove(peer.Key, out _);
            }
        }

        if (removed)
        {
            PeerStore.SaveMenuPeers(GetSnapshot());
            PeersChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task ProbeManualPeersAsync(CancellationToken cancellationToken)
    {
        if (!await _manualProbeGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            ManualPeerEndpoint[] manualPeers;
            lock (_settings)
            {
                manualPeers = _settings.ManualPeers.Select(peer => peer.Copy()).ToArray();
            }

            foreach (var endpoint in manualPeers.Where(peer => peer.Enabled && !string.IsNullOrWhiteSpace(peer.Host)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var port = endpoint.Port > 0 ? endpoint.Port : _settings.ReceiverPort;
                var probe = await TransferClient.ProbeAsync(endpoint.Host, port, TimeSpan.FromMilliseconds(1200), _settings, cancellationToken).ConfigureAwait(false);
                if (probe is null || probe.DeviceId == _settings.DeviceId)
                {
                    continue;
                }

                probe.Id = $"{probe.DeviceId}|manual|{endpoint.Host}:{port}";
                probe.Source = "Manual";
                if (!string.IsNullOrWhiteSpace(endpoint.Label))
                {
                    probe.MenuTitleOverride = $"{endpoint.Label} [{probe.DisplayName}] [Manual]";
                }

                UpsertPeer(probe);
            }
        }
        finally
        {
            _manualProbeGate.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _receiveClient is not null)
        {
            try
            {
                var result = await _receiveClient.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                var json = Encoding.UTF8.GetString(result.Buffer);
                var message = JsonSerializer.Deserialize<BeaconMessage>(json, JsonOptions.Default);
                if (message is null ||
                    message.Protocol != QuickDropConstants.ProtocolName ||
                    message.DeviceId == _settings.DeviceId)
                {
                    continue;
                }

                UpsertPeer(new PeerInfo
                {
                    Id = $"{message.DeviceId}|lan|{result.RemoteEndPoint.Address}",
                    DeviceId = message.DeviceId,
                    DisplayName = message.DisplayName,
                    MachineName = message.MachineName,
                    UserName = message.UserName,
                    Endpoint = result.RemoteEndPoint.Address.ToString(),
                    Port = message.Port,
                    Source = "LAN",
                    IsReceiverConfirmed = true,
                    LastSeenUtc = DateTimeOffset.UtcNow
                });
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error("Discovery receive loop failed.", ex);
            }
        }
    }

    private async Task BroadcastLoopAsync(CancellationToken cancellationToken)
    {
        var endpoint = new IPEndPoint(IPAddress.Broadcast, QuickDropConstants.DiscoveryPort);
        while (!cancellationToken.IsCancellationRequested && _sendClient is not null)
        {
            try
            {
                var message = new BeaconMessage
                {
                    DeviceId = _settings.DeviceId,
                    DisplayName = _settings.DisplayName,
                    MachineName = Environment.MachineName,
                    UserName = Environment.UserName,
                    Port = _settings.ReceiverPort
                };
                var bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions.Default);
                await _sendClient.SendAsync(bytes, endpoint, cancellationToken).ConfigureAwait(false);
                PrunePeers();
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error("Discovery broadcast loop failed.", ex);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task TailscaleProbeLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var addresses = await TailscalePeerProvider.GetOnlinePeerAddressesAsync(cancellationToken).ConfigureAwait(false);
                foreach (var address in addresses)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var probe = await TransferClient.ProbeAsync(address, _settings.ReceiverPort, TimeSpan.FromMilliseconds(900), _settings, cancellationToken).ConfigureAwait(false);
                    if (probe is null || probe.DeviceId == _settings.DeviceId)
                    {
                        continue;
                    }

                    probe.Id = $"{probe.DeviceId}|tailscale|{address}";
                    probe.Source = "Tailscale";
                    UpsertPeer(probe);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error("Tailscale probe loop failed.", ex);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ManualProbeLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ProbeManualPeersAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error("Manual peer probe loop failed.", ex);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void UpsertPeer(PeerInfo peer)
    {
        _peers[peer.Id] = peer;
        PeerStore.SaveMenuPeers(GetSnapshot());
        PeersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PrunePeers()
    {
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);
        var removed = false;
        foreach (var peer in _peers)
        {
            if (peer.Value.LastSeenUtc < cutoff)
            {
                removed |= _peers.TryRemove(peer.Key, out _);
            }
        }

        if (removed)
        {
            PeerStore.SaveMenuPeers(GetSnapshot());
            PeersChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        _receiveClient?.Dispose();
        _sendClient?.Dispose();
        foreach (var task in new[] { _receiveLoop, _broadcastLoop, _tailscaleLoop, _manualLoop }.Where(t => t is not null))
        {
            try
            {
                await task!.ConfigureAwait(false);
            }
            catch
            {
                // Shutdown should be quiet.
            }
        }

        _cts.Dispose();
        _manualProbeGate.Dispose();
    }
}
