using System.Diagnostics;
using System.Text.Json;

namespace QuickDrop.Core;

public static class TailscalePeerProvider
{
    public static async Task<IReadOnlyList<string>> GetOnlinePeerAddressesAsync(CancellationToken cancellationToken)
    {
        var tailscale = FindTailscaleExe();
        if (tailscale is null)
        {
            return Array.Empty<string>();
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = tailscale,
                Arguments = "status --json",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return Array.Empty<string>();
            }

            var jsonTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                return Array.Empty<string>();
            }

            using var document = JsonDocument.Parse(await jsonTask.ConfigureAwait(false));
            if (!document.RootElement.TryGetProperty("Peer", out var peers) || peers.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<string>();
            }

            var addresses = new List<string>();
            foreach (var peerProperty in peers.EnumerateObject())
            {
                var peer = peerProperty.Value;
                if (peer.TryGetProperty("Online", out var online) && online.ValueKind == JsonValueKind.False)
                {
                    continue;
                }

                if (!peer.TryGetProperty("TailscaleIPs", out var ips) || ips.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var ip in ips.EnumerateArray())
                {
                    var value = ip.GetString();
                    if (!string.IsNullOrWhiteSpace(value) && value.Contains('.', StringComparison.Ordinal))
                    {
                        addresses.Add(value);
                    }
                }
            }

            return addresses.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to query Tailscale peers.", ex);
            return Array.Empty<string>();
        }
    }

    private static string? FindTailscaleExe()
    {
        var programFilesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe");
        if (File.Exists(programFilesPath))
        {
            return programFilesPath;
        }

        return "tailscale.exe";
    }
}
