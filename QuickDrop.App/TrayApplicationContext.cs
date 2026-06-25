using QuickDrop.Core;

namespace QuickDrop.App;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SynchronizationContext _uiContext;
    private readonly AppSettings _settings;
    private readonly ReceiverServer _receiver;
    private readonly PeerDiscoveryService _discovery;
    private readonly NotifyIcon _notifyIcon;
    private DashboardForm? _dashboard;

    public TrayApplicationContext()
    {
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _settings = AppSettings.Load();
        _discovery = new PeerDiscoveryService(_settings);
        _receiver = new ReceiverServer(_settings, NotifyIncomingTransfer, _discovery.AddDirectPeer);
        _discovery.PeersChanged += (_, _) => _dashboard?.RefreshPeers();

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "QuickDrop",
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowDashboard();

        try
        {
            _receiver.Start();
            _discovery.Start();
            PeerStore.SaveMenuPeers(_discovery.GetSnapshot());
        }
        catch (Exception ex)
        {
            Log.Error("Failed to start QuickDrop.", ex);
            MessageBox.Show(ex.Message, "QuickDrop の起動に失敗しました", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ExitThread();
        }
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => PopulateContextMenu(menu);
        PopulateContextMenu(menu);
        return menu;
    }

    private void PopulateContextMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();
        menu.Items.Add("QuickDrop を開く", null, (_, _) => ShowDashboard());
        menu.Items.Add("保存先フォルダーを開く", null, (_, _) => ExplorerActions.OpenDownloads(_settings));
        menu.Items.Add(BuildDownloadFolderMenu());
        menu.Items.Add(new ToolStripSeparator());

        var startupItem = new ToolStripMenuItem("PC起動時に自動実行")
        {
            Checked = StartupManager.IsEnabled(),
            CheckOnClick = true
        };
        startupItem.Click += (_, _) => ToggleStartup(startupItem.Checked);
        menu.Items.Add(startupItem);

        menu.Items.Add("送信先IPを追加...", null, async (_, _) => await AddManualPeerAsync());
        menu.Items.Add(BuildManualPeerMenu());
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("右クリックメニューを登録", null, (_, _) => InstallExplorerMenu());
        menu.Items.Add("ログを開く", null, (_, _) => ExplorerActions.OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, async (_, _) => await ExitAsync());
    }

    private ToolStripMenuItem BuildManualPeerMenu()
    {
        var parent = new ToolStripMenuItem("登録済み送信先IP");
        ManualPeerEndpoint[] manualPeers;
        lock (_settings)
        {
            manualPeers = _settings.ManualPeers.Select(peer => peer.Copy()).ToArray();
        }

        if (manualPeers.Length == 0)
        {
            parent.DropDownItems.Add(new ToolStripMenuItem("未登録") { Enabled = false });
            return parent;
        }

        foreach (var peer in manualPeers.OrderBy(peer => peer.DisplayText, StringComparer.CurrentCultureIgnoreCase))
        {
            var item = new ToolStripMenuItem(peer.DisplayText) { Checked = peer.Enabled };
            item.DropDownItems.Add("有効/無効を切り替え", null, (_, _) => ToggleManualPeer(peer.Id));
            item.DropDownItems.Add("削除", null, (_, _) => RemoveManualPeer(peer.Id));
            parent.DropDownItems.Add(item);
        }

        return parent;
    }

    private ToolStripMenuItem BuildDownloadFolderMenu()
    {
        var parent = new ToolStripMenuItem("保存先フォルダー設定");
        var isManual = !string.IsNullOrWhiteSpace(_settings.DownloadDirectoryOverride);
        var currentPath = QuickDropPaths.GetDownloadsDirectory(_settings);
        parent.DropDownItems.Add(new ToolStripMenuItem($"現在: {currentPath}") { Enabled = false });
        if (isManual)
        {
            parent.DropDownItems.Add(new ToolStripMenuItem($"自動検出: {QuickDropPaths.AutomaticDownloadsDirectory}") { Enabled = false });
        }

        parent.DropDownItems.Add("変更...", null, (_, _) => ChangeDownloadFolder());
        parent.DropDownItems.Add(new ToolStripMenuItem("自動検出に戻す", null, (_, _) => ResetDownloadFolder()) { Enabled = isManual });
        return parent;
    }

    private void ShowDashboard()
    {
        if (_dashboard is null || _dashboard.IsDisposed)
        {
            _dashboard = new DashboardForm(_settings, _discovery);
            _dashboard.FormClosed += (_, _) => _dashboard = null;
        }

        _dashboard.Show();
        _dashboard.WindowState = FormWindowState.Normal;
        _dashboard.Activate();
        _dashboard.RefreshPeers();
    }

    private void NotifyIncomingTransfer(string sender, string savedPath)
    {
        _uiContext.Post(_ =>
        {
            if (_settings.ShowNotifications)
            {
                _notifyIcon.BalloonTipTitle = "QuickDrop 受信完了";
                _notifyIcon.BalloonTipText = $"{sender} から受信しました: {savedPath}";
                _notifyIcon.ShowBalloonTip(5000);
            }

            _dashboard?.RefreshPeers();
        }, null);
    }

    private void ToggleStartup(bool enabled)
    {
        try
        {
            StartupManager.SetEnabled(enabled);
            _settings.StartWithWindows = enabled;
            _settings.Save();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to update startup setting.", ex);
            MessageBox.Show(ex.Message, "自動実行設定に失敗しました", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ChangeDownloadFolder()
    {
        var currentPath = QuickDropPaths.GetDownloadsDirectory(_settings);
        using var dialog = new FolderBrowserDialog
        {
            Description = "QuickDrop で受信したファイルの保存先フォルダーを選択してください。",
            SelectedPath = Directory.Exists(currentPath) ? currentPath : QuickDropPaths.AutomaticDownloadsDirectory,
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        lock (_settings)
        {
            _settings.DownloadDirectoryOverride = dialog.SelectedPath;
            _settings.Save();
        }

        NotifyDownloadFolderChanged("保存先フォルダーを変更しました。");
    }

    private void ResetDownloadFolder()
    {
        lock (_settings)
        {
            _settings.DownloadDirectoryOverride = "";
            _settings.Save();
        }

        NotifyDownloadFolderChanged("保存先フォルダーを自動検出に戻しました。");
    }

    private void NotifyDownloadFolderChanged(string message)
    {
        QuickDropPaths.EnsureDirectories(_settings);
        _notifyIcon.BalloonTipTitle = "QuickDrop";
        _notifyIcon.BalloonTipText = $"{message}\n{QuickDropPaths.GetDownloadsDirectory(_settings)}";
        _notifyIcon.ShowBalloonTip(3000);
        _dashboard?.RefreshDownloadFolder();
    }

    private async Task AddManualPeerAsync()
    {
        using var dialog = new ManualPeerDialog(_settings.ReceiverPort);
        if (dialog.ShowDialog() != DialogResult.OK || dialog.Endpoint is null)
        {
            return;
        }

        lock (_settings)
        {
            var existing = _settings.ManualPeers.FirstOrDefault(peer =>
                string.Equals(peer.Host, dialog.Endpoint.Host, StringComparison.OrdinalIgnoreCase) &&
                peer.Port == dialog.Endpoint.Port);
            if (existing is not null)
            {
                existing.Label = dialog.Endpoint.Label;
                existing.Enabled = true;
            }
            else
            {
                _settings.ManualPeers.Add(dialog.Endpoint);
            }

            _settings.Save();
        }

        _notifyIcon.BalloonTipTitle = "QuickDrop";
        _notifyIcon.BalloonTipText = "送信先IPを追加しました。疎通できると送信先に表示されます。";
        _notifyIcon.ShowBalloonTip(3000);
        await _discovery.ProbeManualPeersAsync(CancellationToken.None);
        _dashboard?.RefreshPeers();
    }

    private void ToggleManualPeer(string id)
    {
        ManualPeerEndpoint? changedPeer = null;
        lock (_settings)
        {
            var peer = _settings.ManualPeers.FirstOrDefault(peer => peer.Id == id);
            if (peer is null)
            {
                return;
            }

            peer.Enabled = !peer.Enabled;
            changedPeer = peer.Copy();
            _settings.Save();
        }

        if (changedPeer is null)
        {
            return;
        }

        if (changedPeer.Enabled)
        {
            _ = _discovery.ProbeManualPeersAsync(CancellationToken.None);
        }
        else
        {
            _discovery.RemoveManualPeerEntries(changedPeer.Host, changedPeer.Port);
        }
    }

    private void RemoveManualPeer(string id)
    {
        ManualPeerEndpoint? removedPeer = null;
        lock (_settings)
        {
            removedPeer = _settings.ManualPeers.FirstOrDefault(peer => peer.Id == id)?.Copy();
            _settings.ManualPeers.RemoveAll(peer => peer.Id == id);
            _settings.Save();
        }

        if (removedPeer is not null)
        {
            _discovery.RemoveManualPeerEntries(removedPeer.Host, removedPeer.Port);
            _dashboard?.RefreshPeers();
        }
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

    private async Task ExitAsync()
    {
        await _receiver.DisposeAsync().ConfigureAwait(false);
        await _discovery.DisposeAsync().ConfigureAwait(false);
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
