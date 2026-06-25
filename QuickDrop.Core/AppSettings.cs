using System.Text.Json;

namespace QuickDrop.Core;

public sealed class AppSettings
{
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; set; } = Environment.MachineName;

    public int ReceiverPort { get; set; } = QuickDropConstants.DefaultReceiverPort;

    public bool StartWithWindows { get; set; }

    public bool ShowNotifications { get; set; } = true;

    public bool AcceptIncomingTransfers { get; set; } = true;

    public List<ManualPeerEndpoint> ManualPeers { get; set; } = [];

    public static AppSettings Load()
    {
        QuickDropPaths.EnsureDirectories();
        if (!File.Exists(QuickDropPaths.SettingsPath))
        {
            var created = new AppSettings();
            created.Save();
            return created;
        }

        try
        {
            var json = File.ReadAllText(QuickDropPaths.SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions.Default);
            if (settings is null)
            {
                throw new InvalidDataException("Settings file is empty.");
            }

            if (string.IsNullOrWhiteSpace(settings.DeviceId))
            {
                settings.DeviceId = Guid.NewGuid().ToString("N");
            }

            if (string.IsNullOrWhiteSpace(settings.DisplayName))
            {
                settings.DisplayName = Environment.MachineName;
            }

            if (settings.ReceiverPort <= 0)
            {
                settings.ReceiverPort = QuickDropConstants.DefaultReceiverPort;
            }

            settings.ManualPeers ??= [];
            foreach (var peer in settings.ManualPeers)
            {
                if (peer.Port <= 0)
                {
                    peer.Port = settings.ReceiverPort;
                }
            }

            return settings;
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load settings. Recreating defaults.", ex);
            var fallback = new AppSettings();
            fallback.Save();
            return fallback;
        }
    }

    public void Save()
    {
        QuickDropPaths.EnsureDirectories();
        var json = JsonSerializer.Serialize(this, JsonOptions.Indented);
        File.WriteAllText(QuickDropPaths.SettingsPath, json);
    }
}

public sealed class ManualPeerEndpoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Host { get; set; } = "";

    public int Port { get; set; } = QuickDropConstants.DefaultReceiverPort;

    public string Label { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public string DisplayText
    {
        get
        {
            var host = string.IsNullOrWhiteSpace(Host) ? "(empty)" : Host;
            var text = $"{host}:{Port}";
            return string.IsNullOrWhiteSpace(Label) ? text : $"{Label} ({text})";
        }
    }

    public ManualPeerEndpoint Copy() => new()
    {
        Id = Id,
        Host = Host,
        Port = Port,
        Label = Label,
        Enabled = Enabled
    };
}
