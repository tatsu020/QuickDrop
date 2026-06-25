using QuickDrop.Core;

namespace QuickDrop.Cli;

public static class CliProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h")
            {
                PrintHelp();
                return 0;
            }

            var command = args[0].ToLowerInvariant();
            return command switch
            {
                "list-peers" => ListPeers(),
                "send" => await SendAsync(args.Skip(1).ToArray(), showMessageBox: HasFlag(args, "--message-box")).ConfigureAwait(false),
                "pick-send" => await PickSendAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                _ => Fail($"Unknown command: {args[0]}", showMessageBox: false)
            };
        }
        catch (Exception ex)
        {
            Log.Error("QuickDrop CLI failed.", ex);
            return Fail(ex.Message, showMessageBox: HasFlag(args, "--message-box"));
        }
    }

    private static int ListPeers()
    {
        var peers = PeerStore.LoadMenuPeers();
        foreach (var peer in peers)
        {
            Console.WriteLine($"{peer.Id}\t{peer.MenuTitle}\t{peer.Endpoint}:{peer.Port}");
        }

        return peers.Count == 0 ? 2 : 0;
    }

    private static async Task<int> SendAsync(string[] args, bool showMessageBox)
    {
        var targetId = GetValue(args, "--target") ?? GetValue(args, "--target-id");
        if (string.IsNullOrWhiteSpace(targetId))
        {
            return Fail("--target is required.", showMessageBox);
        }

        var paths = LoadSelectedPaths(args);
        if (paths.Count == 0)
        {
            return Fail("No selected paths were supplied.", showMessageBox);
        }

        var peer = PeerStore.LoadMenuPeers().FirstOrDefault(p => string.Equals(p.Id, targetId, StringComparison.OrdinalIgnoreCase));
        if (peer is null)
        {
            return Fail("The selected QuickDrop device is no longer available.", showMessageBox);
        }

        var settings = AppSettings.Load();
        var client = new TransferClient(settings);
        Console.WriteLine($"Sending {paths.Count} item(s) to {peer.MenuTitle}...");
        var response = await client.SendAsync(peer, paths, null, CancellationToken.None).ConfigureAwait(false);
        if (!response.Ok)
        {
            return Fail(response.Message, showMessageBox);
        }

        Console.WriteLine(response.Message);
        return 0;
    }

    private static async Task<int> PickSendAsync(string[] args)
    {
        var paths = args.Where(arg => !string.IsNullOrWhiteSpace(arg)).ToArray();
        if (paths.Length == 0)
        {
            return Fail("No selected paths were supplied.", showMessageBox: true);
        }

        var peers = PeerStore.LoadMenuPeers();
        if (peers.Count == 0)
        {
            return Fail("QuickDrop の送信先が見つかりません。相手PCで QuickDrop を起動してください。", showMessageBox: true);
        }

        PeerInfo peer;
        if (peers.Count == 1)
        {
            peer = peers[0];
        }
        else
        {
            using var picker = new PeerPickerForm(peers);
            if (picker.ShowDialog() != DialogResult.OK || picker.SelectedPeer is null)
            {
                return 1;
            }

            peer = picker.SelectedPeer;
        }

        QuickDropPaths.EnsureDirectories();
        var tempFile = Path.Combine(QuickDropPaths.TempDirectory, $"paths-{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(tempFile, paths).ConfigureAwait(false);
        try
        {
            return await SendAsync(new[] { "--target", peer.Id, "--paths-file", tempFile }, showMessageBox: true).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(tempFile);
        }
    }

    private static IReadOnlyList<string> LoadSelectedPaths(string[] args)
    {
        var pathsFile = GetValue(args, "--paths-file");
        if (!string.IsNullOrWhiteSpace(pathsFile))
        {
            return File.ReadAllLines(pathsFile)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();
        }

        var pathsStart = Array.FindIndex(args, arg => arg == "--");
        if (pathsStart >= 0)
        {
            return args.Skip(pathsStart + 1).Where(arg => !string.IsNullOrWhiteSpace(arg)).ToArray();
        }

        return args.Where(arg => !arg.StartsWith("--", StringComparison.Ordinal)).ToArray();
    }

    private static string? GetValue(string[] args, string key)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static bool HasFlag(string[] args, string key) =>
        args.Any(arg => string.Equals(arg, key, StringComparison.OrdinalIgnoreCase));

    private static int Fail(string message, bool showMessageBox)
    {
        Console.Error.WriteLine(message);
        if (showMessageBox)
        {
            MessageBox.Show(message, "QuickDrop", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("QuickDrop.Cli");
        Console.WriteLine("  list-peers");
        Console.WriteLine("  send --target <peer-id> --paths-file <utf8-lines-file> [--message-box]");
        Console.WriteLine("  send --target <peer-id> -- <path1> <path2> ...");
        Console.WriteLine("  pick-send <path>");
    }

    private static void TryDelete(string path)
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
            // Nothing useful to do for temp cleanup here.
        }
    }
}
