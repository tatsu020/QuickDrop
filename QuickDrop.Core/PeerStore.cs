using System.Text;

namespace QuickDrop.Core;

public static class PeerStore
{
    public static void SaveMenuPeers(IEnumerable<PeerInfo> peers)
    {
        QuickDropPaths.EnsureDirectories();
        var freshPeers = peers
            .Where(p => p.IsReceiverConfirmed)
            .Where(p => !string.IsNullOrWhiteSpace(p.Id))
            .Where(p => !string.IsNullOrWhiteSpace(p.Endpoint))
            .Where(p => p.Port > 0)
            .OrderBy(p => p.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.MenuTitle, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var lines = freshPeers.Select(p => p.ToMenuLine());
        File.WriteAllLines(QuickDropPaths.PeersMenuPath, lines, Encoding.UTF8);
    }

    public static IReadOnlyList<PeerInfo> LoadMenuPeers()
    {
        try
        {
            if (!File.Exists(QuickDropPaths.PeersMenuPath))
            {
                return Array.Empty<PeerInfo>();
            }

            return File.ReadAllLines(QuickDropPaths.PeersMenuPath, Encoding.UTF8)
                .Select(line => PeerInfo.TryParseMenuLine(line, out var peer) ? peer : null)
                .Where(peer => peer is not null)
                .Cast<PeerInfo>()
                .Where(peer => DateTimeOffset.UtcNow - peer.LastSeenUtc < TimeSpan.FromMinutes(5))
                .ToArray();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load peer menu cache.", ex);
            return Array.Empty<PeerInfo>();
        }
    }
}
