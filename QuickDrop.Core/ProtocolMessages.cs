namespace QuickDrop.Core;

public sealed class BeaconMessage
{
    public string Protocol { get; set; } = QuickDropConstants.ProtocolName;

    public string DeviceId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string MachineName { get; set; } = "";

    public string UserName { get; set; } = "";

    public int Port { get; set; }
}

public sealed class ProtocolHeader
{
    public string Protocol { get; set; } = QuickDropConstants.ProtocolName;

    public string Type { get; set; } = "";

    public string SenderDeviceId { get; set; } = "";

    public string SenderDisplayName { get; set; } = "";

    public string TransferId { get; set; } = "";

    public string PackageName { get; set; } = "";

    public string ExtractMode { get; set; } = "";

    public int ItemCount { get; set; }

    public long ArchiveBytes { get; set; }

    public string ArchiveSha256 { get; set; } = "";

    public int ReceiverPort { get; set; }

    public string MachineName { get; set; } = "";
}

public sealed class TransferResponse
{
    public string Protocol { get; set; } = QuickDropConstants.ProtocolName;

    public bool Ok { get; set; }

    public string Message { get; set; } = "";

    public string SavedPath { get; set; } = "";
}
