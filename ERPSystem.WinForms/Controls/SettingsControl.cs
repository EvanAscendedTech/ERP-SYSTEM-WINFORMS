using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Controls;

public class SettingsControl : UserControl
{
    private readonly AppSettingsService _settingsService;
    private readonly bool _canManageSettings;
    private readonly Action<string>? _companyNameChanged;

    private readonly TextBox _companyNameInput = new() { Width = 320 };
    private readonly NumericUpDown _autoRefreshInput = new() { Minimum = 5, Maximum = 600, Width = 120 };
    private readonly CheckBox _notificationsInput = new() { Text = "Enable notifications", AutoSize = true };
    private readonly TextBox _themeInput = new() { Width = 160 };
    private readonly TextBox _archivePathInput = new() { Width = 320 };
    private readonly Label _feedback = new() { AutoSize = true };

    private AppSettings _settings = new();

    public SettingsControl(AppSettingsService settingsService, bool canManageSettings, Action<string>? companyNameChanged = null)
    {
        _settingsService = settingsService;
        _canManageSettings = canManageSettings;
        _companyNameChanged = companyNameChanged;
        Dock = DockStyle.Fill;

        var form = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12), ColumnCount = 2 };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddRow(form, 0, "Company Name", _companyNameInput);
        AddRow(form, 1, "Theme", _themeInput);
        AddRow(form, 2, "Auto Refresh Seconds", _autoRefreshInput);
        AddRow(form, 3, "Archive Path", _archivePathInput);
        AddRow(form, 4, "", _notificationsInput);

        var saveButton = new Button { Text = "Save Settings", AutoSize = true, Enabled = _canManageSettings };
        saveButton.Click += async (_, _) => await SaveSettingsAsync();

        form.Controls.Add(saveButton, 1, 5);
        form.Controls.Add(_feedback, 1, 6);

        if (!_canManageSettings)
        {
            _feedback.Text = "Settings are read-only. Administrator access required.";
            foreach (Control c in form.Controls)
            {
                if (c is TextBox or NumericUpDown or CheckBox)
                {
                    c.Enabled = false;
                }
            }
        }

        Controls.Add(form);
        _ = LoadSettingsAsync();
    }

    private static void AddRow(TableLayoutPanel panel, int row, string label, Control control)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = label, Margin = new Padding(0, 8, 8, 0), AutoSize = true }, 0, row);
        panel.Controls.Add(control, 1, row);
    }

    private async Task LoadSettingsAsync()
    {
        _settings = await _settingsService.LoadAsync();
        _companyNameInput.Text = _settings.CompanyName;
        _themeInput.Text = _settings.Theme;
        _autoRefreshInput.Value = Math.Clamp(_settings.AutoRefreshSeconds, (int)_autoRefreshInput.Minimum, (int)_autoRefreshInput.Maximum);
        _archivePathInput.Text = _settings.DefaultArchivePath;
        _notificationsInput.Checked = _settings.EnableNotifications;
    }

    private async Task SaveSettingsAsync()
    {
        _settings.CompanyName = _companyNameInput.Text.Trim();
        _settings.Theme = _themeInput.Text.Trim();
        _settings.AutoRefreshSeconds = (int)_autoRefreshInput.Value;
        _settings.DefaultArchivePath = _archivePathInput.Text.Trim();
        _settings.EnableNotifications = _notificationsInput.Checked;

        await _settingsService.SaveAsync(_settings);
        _companyNameChanged?.Invoke(_settings.CompanyName);
        _feedback.Text = "Settings saved.";
    }
}
