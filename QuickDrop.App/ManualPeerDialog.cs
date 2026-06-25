using QuickDrop.Core;

namespace QuickDrop.App;

public sealed class ManualPeerDialog : Form
{
    private readonly TextBox _hostText = new();
    private readonly NumericUpDown _portInput = new();
    private readonly TextBox _labelText = new();

    public ManualPeerEndpoint? Endpoint { get; private set; }

    public ManualPeerDialog(int defaultPort)
    {
        Text = "送信先IPを追加";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        Size = new Size(420, 230);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 2,
            RowCount = 4
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(new Label { Text = "IP/ホスト", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _hostText.Dock = DockStyle.Fill;
        _hostText.PlaceholderText = "例: 100.64.0.10";
        root.Controls.Add(_hostText, 1, 0);

        root.Controls.Add(new Label { Text = "ポート", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        _portInput.Dock = DockStyle.Left;
        _portInput.Minimum = 1;
        _portInput.Maximum = 65535;
        _portInput.Value = defaultPort;
        root.Controls.Add(_portInput, 1, 1);

        root.Controls.Add(new Label { Text = "名前", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        _labelText.Dock = DockStyle.Fill;
        _labelText.PlaceholderText = "任意";
        root.Controls.Add(_labelText, 1, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        var add = new Button { Text = "追加", Width = 96, DialogResult = DialogResult.OK };
        add.Click += (_, _) => Accept();
        var cancel = new Button { Text = "キャンセル", Width = 96, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(add);
        buttons.Controls.Add(cancel);
        root.SetColumnSpan(buttons, 2);
        root.Controls.Add(buttons, 0, 3);

        Controls.Add(root);
        AcceptButton = add;
        CancelButton = cancel;
    }

    private void Accept()
    {
        var host = _hostText.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            MessageBox.Show("IPアドレスまたはホスト名を入力してください。", "QuickDrop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        if (TrySplitHostAndPort(host, out var parsedHost, out var parsedPort))
        {
            host = parsedHost;
            _portInput.Value = parsedPort;
        }

        Endpoint = new ManualPeerEndpoint
        {
            Host = host,
            Port = (int)_portInput.Value,
            Label = _labelText.Text.Trim(),
            Enabled = true
        };
    }

    private static bool TrySplitHostAndPort(string value, out string host, out int port)
    {
        host = value;
        port = QuickDropConstants.DefaultReceiverPort;
        var separator = value.LastIndexOf(':');
        if (separator <= 0 || separator == value.Length - 1)
        {
            return false;
        }

        if (value.Count(c => c == ':') > 1)
        {
            return false;
        }

        if (!int.TryParse(value[(separator + 1)..], out var parsedPort) || parsedPort <= 0 || parsedPort > 65535)
        {
            return false;
        }

        host = value[..separator].Trim();
        port = parsedPort;
        return !string.IsNullOrWhiteSpace(host);
    }
}
