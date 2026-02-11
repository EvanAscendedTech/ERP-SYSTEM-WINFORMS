namespace ERPSystem.WinForms.Controls;

public class SettingsControl : UserControl
{
    public SettingsControl()
    {
        Dock = DockStyle.Fill;

        var settingsGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        settingsGrid.Columns.Add("key", "Setting Key");
        settingsGrid.Columns.Add("value", "Setting Value");

        settingsGrid.Rows.Add("Theme", "Light");
        settingsGrid.Rows.Add("AutoRefreshSeconds", "30");
        settingsGrid.Rows.Add("ArchivePath", "C:\\ERP\\Archive");
        settingsGrid.Rows.Add("EnableNotifications", "true");

        Controls.Add(settingsGrid);
    }
}
