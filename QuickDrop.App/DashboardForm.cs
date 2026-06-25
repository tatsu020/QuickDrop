using QuickDrop.Core;

namespace QuickDrop.App;

public sealed class DashboardForm : Form
{
    private readonly AppSettings _settings;
    private readonly PeerDiscoveryService _discovery;
    private readonly DataGridView _peersGrid = new();
    private readonly TextBox _displayNameText = new();
    private readonly CheckBox _acceptTransfersCheck = new();
    private readonly CheckBox _notificationsCheck = new();
    private readonly System.Windows.Forms.Timer _timer = new();

    public DashboardForm(AppSettings settings, PeerDiscoveryService discovery)
    {
        _settings = settings;
        _discovery = discovery;
        Text = "QuickDrop";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 520);
        Size = new Size(920, 600);
        Icon = SystemIcons.Application;

        BuildLayout();
        _timer.Interval = 3000;
        _timer.Tick += (_, _) => RefreshPeers();
        _timer.Start();
    }

    public void RefreshPeers()
    {
        if (InvokeRequired)
        {
            BeginInvoke(RefreshPeers);
            return;
        }

        var rows = _discovery.GetSnapshot()
            .Select(peer => new
            {
                Device = peer.MenuTitle,
                Route = peer.Source,
                Address = $"{peer.Endpoint}:{peer.Port}",
                LastSeen = peer.LastSeenUtc.ToLocalTime().ToString("HH:mm:ss")
            })
            .ToArray();
        _peersGrid.DataSource = rows;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));

        root.Controls.Add(BuildSettingsPanel(), 0, 0);
        root.Controls.Add(BuildPeersGrid(), 0, 1);
        root.Controls.Add(BuildButtons(), 0, 2);
        Controls.Add(root);
    }

    private Control BuildSettingsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        panel.Controls.Add(new Label { Text = "表示名", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
        _displayNameText.Text = _settings.DisplayName;
        _displayNameText.Dock = DockStyle.Fill;
        panel.Controls.Add(_displayNameText, 1, 0);

        _acceptTransfersCheck.Text = "受信を許可";
        _acceptTransfersCheck.Checked = _settings.AcceptIncomingTransfers;
        _acceptTransfersCheck.Dock = DockStyle.Fill;
        panel.Controls.Add(_acceptTransfersCheck, 2, 0);

        _notificationsCheck.Text = "通知を表示";
        _notificationsCheck.Checked = _settings.ShowNotifications;
        _notificationsCheck.Dock = DockStyle.Fill;
        panel.Controls.Add(_notificationsCheck, 3, 0);

        var deviceLabel = new Label
        {
            Text = $"このPC: {Environment.MachineName} / TCP {_settings.ReceiverPort} / UDP {QuickDropConstants.DiscoveryPort}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        panel.SetColumnSpan(deviceLabel, 4);
        panel.Controls.Add(deviceLabel, 0, 1);
        return panel;
    }

    private Control BuildPeersGrid()
    {
        _peersGrid.Dock = DockStyle.Fill;
        _peersGrid.ReadOnly = true;
        _peersGrid.AllowUserToAddRows = false;
        _peersGrid.AllowUserToDeleteRows = false;
        _peersGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _peersGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _peersGrid.RowHeadersVisible = false;
        RefreshPeers();
        return _peersGrid;
    }

    private Control BuildButtons()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 12, 0, 0)
        };

        panel.Controls.Add(CreateButton("閉じる", (_, _) => Hide()));
        panel.Controls.Add(CreateButton("設定を保存", (_, _) => SaveSettings()));
        panel.Controls.Add(CreateButton("右クリックメニューを登録", (_, _) => InstallExplorerMenu()));
        panel.Controls.Add(CreateButton("Downloads を開く", (_, _) => ExplorerActions.OpenDownloads()));
        panel.Controls.Add(CreateButton("更新", (_, _) => RefreshPeers()));
        return panel;
    }

    private static Button CreateButton(string text, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 32,
            Margin = new Padding(8, 0, 0, 0)
        };
        button.Click += click;
        return button;
    }

    private void SaveSettings()
    {
        _settings.DisplayName = string.IsNullOrWhiteSpace(_displayNameText.Text)
            ? Environment.MachineName
            : _displayNameText.Text.Trim();
        _settings.AcceptIncomingTransfers = _acceptTransfersCheck.Checked;
        _settings.ShowNotifications = _notificationsCheck.Checked;
        _settings.Save();
        MessageBox.Show("設定を保存しました。", "QuickDrop", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void InstallExplorerMenu()
    {
        try
        {
            var result = ExplorerIntegrationInstaller.InstallFrom(AppContext.BaseDirectory);
            MessageBox.Show(result, "QuickDrop", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to install Explorer integration.", ex);
            MessageBox.Show(ex.Message, "右クリックメニュー登録に失敗しました", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }
}
