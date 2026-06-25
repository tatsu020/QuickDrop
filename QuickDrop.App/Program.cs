namespace QuickDrop.App;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, "QuickDrop.App.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("QuickDrop は既に起動しています。", "QuickDrop", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }    
}
