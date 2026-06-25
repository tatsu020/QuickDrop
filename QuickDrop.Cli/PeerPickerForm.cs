using QuickDrop.Core;

namespace QuickDrop.Cli;

public sealed class PeerPickerForm : Form
{
    private readonly ListBox _listBox = new();
    private readonly IReadOnlyList<PeerInfo> _peers;

    public PeerInfo? SelectedPeer { get; private set; }

    public PeerPickerForm(IReadOnlyList<PeerInfo> peers)
    {
        _peers = peers;
        Text = "QuickDrop 送信先";
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        Size = new Size(420, 360);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        _listBox.Dock = DockStyle.Fill;
        _listBox.DisplayMember = nameof(PeerInfo.MenuTitle);
        foreach (var peer in peers)
        {
            _listBox.Items.Add(peer);
        }

        if (_listBox.Items.Count > 0)
        {
            _listBox.SelectedIndex = 0;
        }

        _listBox.DoubleClick += (_, _) => AcceptSelection();
        root.Controls.Add(_listBox, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        var send = new Button { Text = "送信", DialogResult = DialogResult.OK, Width = 96 };
        send.Click += (_, _) => AcceptSelection();
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Width = 96 };
        buttons.Controls.Add(send);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 1);

        Controls.Add(root);
        AcceptButton = send;
        CancelButton = cancel;
    }

    private void AcceptSelection()
    {
        SelectedPeer = _listBox.SelectedItem as PeerInfo ?? _peers.FirstOrDefault();
        DialogResult = SelectedPeer is null ? DialogResult.Cancel : DialogResult.OK;
        Close();
    }
}
