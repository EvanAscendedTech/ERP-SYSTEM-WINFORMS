using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Controls;

public class SettingsControl : UserControl, IRealtimeDataControl
{
    private readonly AppSettingsService _settingsService;
    private readonly bool _canManageSettings;
    private readonly Action<AppSettings>? _settingsChanged;
    private readonly Action<AppTheme>? _themeChanged;
    private readonly Func<Task>? _syncAction;
    private readonly Func<Task>? _saveAction;
    private readonly Func<string>? _lastSyncText;

    private readonly TextBox _companyNameInput = new() { Width = 320 };
    private readonly NumericUpDown _autoRefreshInput = new() { Minimum = 5, Maximum = 600, Width = 120 };
    private readonly CheckBox _notificationsInput = new() { Text = "Enable notifications", AutoSize = true };
    private readonly TextBox _archivePathInput = new() { Width = 320 };
    private readonly PictureBox _companyLogoPreview = new() { Width = 88, Height = 88, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
    private readonly Label _feedback = new() { AutoSize = true };
    private readonly Label _themeValueLabel = new() { AutoSize = true, Margin = new Padding(0, 8, 8, 0) };
    private readonly Button _toggleThemeButton = new() { AutoSize = true };
    private readonly Button _lastSyncButton = new() { AutoSize = true, Enabled = false };
    private readonly UsersControl _usersControl;
    private readonly AuditLogControl _auditLogControl;
    private readonly QuoteRepository _quoteRepository;
    private readonly NumericUpDown _shopRateInput = new() { Minimum = 0, Maximum = 10000, DecimalPlaces = 2, Increment = 1, Width = 140 };
    private readonly Label _quoteSettingsFeedback = new() { AutoSize = true };

    private AppSettings _settings = new();
    private AppTheme _currentTheme;

    public SettingsControl(
        AppSettingsService settingsService,
        UserManagementRepository userRepository,
        QuoteRepository quoteRepository,
        UserAccount currentUser,
        bool canManageSettings,
        Action<AppSettings>? settingsChanged = null,
        AppTheme currentTheme = AppTheme.Dark,
        Action<AppTheme>? themeChanged = null,
        Func<Task>? syncAction = null,
        Func<Task>? saveAction = null,
        Func<string>? lastSyncText = null)
    {
        _settingsService = settingsService;
        _canManageSettings = canManageSettings;
        _quoteRepository = quoteRepository;
        _settingsChanged = settingsChanged;
        _currentTheme = currentTheme;
        _themeChanged = themeChanged;
        _syncAction = syncAction;
        _saveAction = saveAction;
        _lastSyncText = lastSyncText;
        Dock = DockStyle.Fill;

        _usersControl = new UsersControl(userRepository, currentUser, () => { }) { Dock = DockStyle.Fill };
        _auditLogControl = new AuditLogControl(userRepository) { Dock = DockStyle.Fill };

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var generalTab = new TabPage("General Settings");
        var accessTab = new TabPage("User Access");
        var quoteSettingsTab = new TabPage("Quote Settings");
        var auditLogTab = new TabPage("Audit Log");

        generalTab.Controls.Add(BuildGeneralSettingsPanel());
        accessTab.Controls.Add(_usersControl);
        quoteSettingsTab.Controls.Add(BuildQuoteSettingsPanel());
        auditLogTab.Controls.Add(_auditLogControl);

        tabs.TabPages.Add(generalTab);
        tabs.TabPages.Add(accessTab);
        tabs.TabPages.Add(quoteSettingsTab);
        tabs.TabPages.Add(auditLogTab);

        Controls.Add(tabs);

        _ = LoadSettingsAsync();
        UpdateThemeControls();
        UpdateLastSyncButtonText();
    }

    private Control BuildGeneralSettingsPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var form = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12), ColumnCount = 2 };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddRow(form, 0, "Company Name", _companyNameInput);

        _toggleThemeButton.Click += (_, _) => ToggleTheme();
        var themeRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        themeRow.Controls.Add(_themeValueLabel);
        themeRow.Controls.Add(_toggleThemeButton);
        AddRow(form, 1, "Theme", themeRow);

        AddRow(form, 2, "Auto Refresh Seconds", _autoRefreshInput);
        AddRow(form, 3, "Archive Path", _archivePathInput);
        AddRow(form, 4, "", _notificationsInput);

        var logoRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var uploadLogoButton = new Button { Text = "Upload Company Logo", AutoSize = true, Enabled = _canManageSettings };
        uploadLogoButton.Click += (_, _) => UploadCompanyLogo();
        logoRow.Controls.Add(uploadLogoButton);
        logoRow.Controls.Add(_companyLogoPreview);
        AddRow(form, 5, "Company Logo", logoRow);

        var saveButton = new Button { Text = "Save Settings", AutoSize = true, Enabled = _canManageSettings };
        saveButton.Click += async (_, _) => await SaveSettingsAsync();

        form.Controls.Add(saveButton, 1, 6);
        form.Controls.Add(_feedback, 1, 7);

        var syncButton = new Button { Text = "Sync", AutoSize = true, Enabled = _syncAction is not null };
        syncButton.Click += async (_, _) => await RunSyncAsync();

        var manualSaveButton = new Button { Text = "Save", AutoSize = true, Enabled = _saveAction is not null };
        manualSaveButton.Click += async (_, _) => await RunManualSaveAsync();

        _lastSyncButton.Text = GetLastSyncText();

        var actionsRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(12, 8, 12, 12),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        actionsRow.FlowDirection = FlowDirection.RightToLeft;
        actionsRow.Controls.Add(_lastSyncButton);
        actionsRow.Controls.Add(manualSaveButton);
        actionsRow.Controls.Add(syncButton);

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

        panel.Controls.Add(actionsRow);
        panel.Controls.Add(form);
        return panel;
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
        _autoRefreshInput.Value = Math.Clamp(_settings.AutoRefreshSeconds, (int)_autoRefreshInput.Minimum, (int)_autoRefreshInput.Maximum);
        _archivePathInput.Text = _settings.DefaultArchivePath;
        _notificationsInput.Checked = _settings.EnableNotifications;
        SetLogoPreview(_settings.CompanyLogo);

        _currentTheme = ParseTheme(_settings.Theme);
        UpdateThemeControls();
        UpdateLastSyncButtonText();
    }

    private void ToggleTheme()
    {
        _currentTheme = _currentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        _settings.Theme = _currentTheme == AppTheme.Dark ? "Dark" : "Light";
        UpdateThemeControls();
        _themeChanged?.Invoke(_currentTheme);
    }

    private void UpdateThemeControls()
    {
        _themeValueLabel.Text = _currentTheme == AppTheme.Dark ? "Dark" : "Light";
        _toggleThemeButton.Text = _currentTheme == AppTheme.Dark ? "Switch to Light" : "Switch to Dark";
        _toggleThemeButton.Enabled = _canManageSettings;
    }

    private static AppTheme ParseTheme(string? theme)
    {
        return string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase) ? AppTheme.Dark : AppTheme.Light;
    }

    private void UploadCompanyLogo()
    {
        using var picker = new OpenFileDialog { Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp", Multiselect = false };
        if (picker.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _settings.CompanyLogo = File.ReadAllBytes(picker.FileName);
        SetLogoPreview(_settings.CompanyLogo);
    }

    private void SetLogoPreview(byte[]? logoBytes)
    {
        _companyLogoPreview.Image?.Dispose();
        _companyLogoPreview.Image = null;
        if (logoBytes is null || logoBytes.Length == 0)
        {
            return;
        }

        using var stream = new MemoryStream(logoBytes);
        var image = Image.FromStream(stream);
        _companyLogoPreview.Image = new Bitmap(image);
    }

    private async Task SaveSettingsAsync()
    {
        _settings.CompanyName = _companyNameInput.Text.Trim();
        _settings.Theme = _currentTheme == AppTheme.Dark ? "Dark" : "Light";
        _settings.AutoRefreshSeconds = (int)_autoRefreshInput.Value;
        _settings.DefaultArchivePath = _archivePathInput.Text.Trim();
        _settings.EnableNotifications = _notificationsInput.Checked;

        await _settingsService.SaveAsync(_settings);
        _settingsChanged?.Invoke(_settings);
        _themeChanged?.Invoke(_currentTheme);
        _feedback.Text = "Settings saved.";
    }


    private Control BuildQuoteSettingsPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        var form = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        form.Controls.Add(new Label { Text = "Shop Hourly Rate", AutoSize = true, Margin = new Padding(0, 8, 8, 0) }, 0, 0);
        form.Controls.Add(_shopRateInput, 1, 0);

        var saveButton = new Button { Text = "Save Quote Settings", AutoSize = true, Enabled = _canManageSettings };
        saveButton.Click += async (_, _) =>
        {
            await _quoteRepository.SaveShopHourlyRateAsync(_shopRateInput.Value);
            _quoteSettingsFeedback.Text = "Quote settings saved.";
        };

        form.Controls.Add(saveButton, 1, 1);
        form.Controls.Add(_quoteSettingsFeedback, 1, 2);
        panel.Controls.Add(form);

        _ = LoadQuoteSettingsAsync();
        return panel;
    }

    private async Task LoadQuoteSettingsAsync()
    {
        _shopRateInput.Value = Math.Clamp(await _quoteRepository.GetShopHourlyRateAsync(), (decimal)_shopRateInput.Minimum, (decimal)_shopRateInput.Maximum);
        await _auditLogControl.RefreshDataAsync(fromFailSafeCheckpoint);
    }

    private async Task RunSyncAsync()
    {
        if (_syncAction is null)
        {
            return;
        }

        await _syncAction();
        UpdateLastSyncButtonText();
    }

    private async Task RunManualSaveAsync()
    {
        if (_saveAction is null)
        {
            return;
        }

        await _saveAction();
        UpdateLastSyncButtonText();
    }

    private string GetLastSyncText()
    {
        return _lastSyncText?.Invoke() ?? "Last Sync: n/a";
    }

    private void UpdateLastSyncButtonText()
    {
        _lastSyncButton.Text = GetLastSyncText();
    }

    public async Task RefreshDataAsync(bool fromFailSafeCheckpoint)
    {
        await LoadSettingsAsync();
        await _usersControl.RefreshDataAsync(fromFailSafeCheckpoint);
        _shopRateInput.Value = Math.Clamp(await _quoteRepository.GetShopHourlyRateAsync(), (decimal)_shopRateInput.Minimum, (decimal)_shopRateInput.Maximum);
        await _auditLogControl.RefreshDataAsync(fromFailSafeCheckpoint);
    }
}
