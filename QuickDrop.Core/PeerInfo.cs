using System.Globalization;

namespace QuickDrop.Core;

public sealed class PeerInfo
{
    public string Id { get; set; } = "";

    public string DeviceId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string MachineName { get; set; } = "";

    public string UserName { get; set; } = "";

    public string Source { get; set; } = "";

    public string Endpoint { get; set; } = "";

    public int Port { get; set; }

    public DateTimeOffset LastSeenUtc { get; set; }

    public bool IsReceiverConfirmed { get; set; }

    public string MenuTitleOverride { get; set; } = "";

    public string MenuTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(MenuTitleOverride))
            {
                return MenuTitleOverride;
            }

            var name = string.IsNullOrWhiteSpace(DisplayName) ? MachineName : DisplayName;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = Endpoint;
            }

            var route = string.IsNullOrWhiteSpace(Source) ? "Network" : Source;
            return $"{name} [{route}]";
        }
    }

    public string ToMenuLine()
    {
        var fields = new[]
        {
            Id,
            MenuTitle,
            Source,
            Endpoint,
            Port.ToString(CultureInfo.InvariantCulture),
            LastSeenUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
        };

        return string.Join('\t', fields.Select(EscapeMenuField));
    }

    public static bool TryParseMenuLine(string line, out PeerInfo peer)
    {
        peer = new PeerInfo();
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var fields = line.Split('\t').Select(UnescapeMenuField).ToArray();
        if (fields.Length < 6)
        {
            return false;
        }

        if (!int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
        {
            return false;
        }

        _ = long.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds);
        peer = new PeerInfo
        {
            Id = fields[0],
            DisplayName = fields[1],
            MenuTitleOverride = fields[1],
            Source = fields[2],
            Endpoint = fields[3],
            Port = port,
            LastSeenUtc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds),
            IsReceiverConfirmed = true
        };
        return true;
    }

    private static string EscapeMenuField(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    private static string UnescapeMenuField(string value)
    {
        var result = new List<char>(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                i++;
                result.Add(value[i] switch
                {
                    't' => '\t',
                    'r' => '\r',
                    'n' => '\n',
                    '\\' => '\\',
                    _ => value[i]
                });
                continue;
            }

            result.Add(value[i]);
        }

        return new string(result.ToArray());
    }
}
