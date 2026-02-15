using ERPSystem.WinForms;
using ERPSystem.WinForms.Controls;
using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Forms;

public partial class ERPMainForm : Form
{
    private static readonly TimeSpan FailSafeInterval = TimeSpan.FromMinutes(2.5);
    private static readonly TimeSpan OnlineInactivityThreshold = TimeSpan.FromMinutes(5);

    private readonly QuoteRepository _quoteRepo;
    private readonly ProductionRepository _prodRepo;
    private readonly UserManagementRepository _userRepo;
    private readonly AppSettingsService _settings;
    private readonly InspectionService _inspection;
    private readonly ArchiveService _archive;
    private readonly RealtimeDataService _realtimeData;
    private readonly JobFlowService _jobFlow = new();
    private readonly ThemeManager _themeManager = new();
    private readonly Models.UserAccount _currentUser;

    private readonly Dictionary<string, ModernButton> _navButtons;
    private readonly System.Windows.Forms.Timer _syncClockTimer = new();
    private DateTime _nextFailSafeAt;
    private DateTime _lastAutosaveAt;
    private DateTime _lastRefreshAt;
    private DateTime _lastRealtimePollAt;
    private DateTime _lastPresenceRefreshAt;
    private long _lastSeenRealtimeEventId;
    private bool _syncTickRunning;
    private bool _refreshRunning;
    private Models.AppSettings _appSettings = new();

    public ERPMainForm(QuoteRepository quoteRepo, ProductionRepository prodRepo, UserManagementRepository userRepo,
               AppSettingsService settings, InspectionService inspection, ArchiveService archive, RealtimeDataService realtimeData, Models.UserAccount currentUser)
    {
        _quoteRepo = quoteRepo;
        _prodRepo = prodRepo;
        _userRepo = userRepo;
        _settings = settings;
        _inspection = inspection;
        _archive = archive;
        _realtimeData = realtimeData;
        _currentUser = currentUser;

        InitializeComponent();
        DoubleBuffered = true;

        _navButtons = new Dictionary<string, ModernButton>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dashboard"] = btnDashboard,
            ["Quotes"] = btnQuotes,
            ["Production"] = btnProduction,
            ["CRM"] = btnCRM,
            ["Quality"] = btnQuality,
            ["Inspection"] = btnInspection,
            ["Shipping"] = btnShipping,
            ["Settings"] = btnSettingsMenu
        };

        _themeManager.ThemeChanged += (_, _) => ApplyTheme();
        WireEvents();
        _ = LoadAndApplySettingsAsync();
        InitializeSyncClock();

        LoadSection("Dashboard");
        ApplyTheme();
    }

    private void InitializeSyncClock()
    {
        var now = DateTime.Now;
        _nextFailSafeAt = now.Add(FailSafeInterval);
        _lastAutosaveAt = now;
        _lastRefreshAt = now;
        _lastRealtimePollAt = now;
        _lastPresenceRefreshAt = DateTime.MinValue;

        _ = InitializeRealtimeWatcherAsync();

        _syncClockTimer.Interval = 1000;
        _syncClockTimer.Tick += async (_, _) =>
        {
            if (_syncTickRunning)
            {
                return;
            }

            _syncTickRunning = true;
            try
            {
                var tickNow = DateTime.Now;
                if (tickNow >= _nextFailSafeAt)
                {
                    await ExecuteFailSafeCheckpointAsync();
                    tickNow = DateTime.Now;
                }

                if ((tickNow - _lastRealtimePollAt).TotalSeconds >= 2)
                {
                    await PollRealtimeChangesAsync();
                }

                await _userRepo.TouchUserActivityAsync(_currentUser.Id);
                if ((tickNow - _lastPresenceRefreshAt).TotalSeconds >= 10)
                {
                    await RefreshOnlineUsersAsync();
                    _lastPresenceRefreshAt = tickNow;
                }

                UpdateSyncClockText(tickNow);
            }
            finally
            {
                _syncTickRunning = false;
            }
        };

        UpdateSyncClockText(now);
        _syncClockTimer.Start();
        _ = RefreshOnlineUsersAsync();

        FormClosed += async (_, _) =>
        {
            _syncClockTimer.Stop();
            await _userRepo.SetOnlineStatusAsync(_currentUser.Id, false);
        };
    }

    private async Task InitializeRealtimeWatcherAsync()
    {
        await _realtimeData.InitializeDatabaseAsync();
        _lastSeenRealtimeEventId = await _realtimeData.GetLatestEventIdAsync();
    }

    private async Task ExecuteFailSafeCheckpointAsync()
    {
        CommitActiveInputs();
        await RefreshActiveSectionAsync(fromFailSafeCheckpoint: true);

        var now = DateTime.Now;
        _lastAutosaveAt = now;
        _lastRefreshAt = now;
        _nextFailSafeAt = now.Add(FailSafeInterval);
    }

    private async Task PollRealtimeChangesAsync()
    {
        _lastRealtimePollAt = DateTime.Now;
        var latestEventId = await _realtimeData.GetLatestEventIdAsync();
        if (latestEventId <= _lastSeenRealtimeEventId)
        {
            return;
        }

        _lastSeenRealtimeEventId = latestEventId;
        await RefreshActiveSectionAsync(fromFailSafeCheckpoint: false);
        _lastRefreshAt = DateTime.Now;
    }

    private async Task RefreshActiveSectionAsync(bool fromFailSafeCheckpoint)
    {
        if (_refreshRunning)
        {
            return;
        }

        if (mainContentPanel.Controls.Count == 0 || mainContentPanel.Controls[0] is not IRealtimeDataControl refreshableControl)
        {
            return;
        }

        _refreshRunning = true;
        try
        {
            await refreshableControl.RefreshDataAsync(fromFailSafeCheckpoint);
        }
        finally
        {
            _refreshRunning = false;
        }
    }

    private void CommitActiveInputs()
    {
        ValidateChildren();
        FlushBindingsRecursive(this);
    }

    private static void FlushBindingsRecursive(Control control)
    {
        foreach (Binding binding in control.DataBindings)
        {
            binding.WriteValue();
        }

        if (control is DataGridView grid && grid.IsCurrentCellInEditMode)
        {
            grid.EndEdit();
        }

        foreach (Control child in control.Controls)
        {
            FlushBindingsRecursive(child);
        }
    }

    private void UpdateSyncClockText(DateTime now)
    {
        var countdown = _nextFailSafeAt - now;
        if (countdown < TimeSpan.Zero)
        {
            countdown = TimeSpan.Zero;
        }

        var remaining = $"{countdown.Minutes:D2}:{countdown.Seconds:D2}";
        lblSyncClock.Text = $"Sync in {remaining}";
        lblSaveClock.Text = $"Save in {remaining} • Last sync {_lastRefreshAt:HH:mm:ss}";
    }

    private async Task RefreshOnlineUsersAsync()
    {
        await _userRepo.MarkUsersOfflineByInactivityAsync(OnlineInactivityThreshold);
        var users = await _userRepo.GetUsersAsync();
        var onlineUsers = users
            .Where(user => user.IsOnline)
            .OrderBy(user => user.DisplayName)
            .ToList();

        onlineUsersPanel.SuspendLayout();
        onlineUsersPanel.Controls.Clear();

        if (onlineUsers.Count == 0)
        {
            onlineUsersPanel.Controls.Add(new Label { Text = "Online: none", AutoSize = true, Margin = new Padding(0, 7, 0, 0) });
            onlineUsersPanel.ResumeLayout();
            return;
        }

        var panelWidth = Math.Max(onlineUsersPanel.Width, 200);
        var charBudget = Math.Max(10, panelWidth / Math.Max(onlineUsers.Count, 1) / 9);
        foreach (var user in onlineUsers)
        {
            onlineUsersPanel.Controls.Add(BuildOnlineUserChip(user, charBudget));
        }

        onlineUsersPanel.ResumeLayout();
    }

    private Control BuildOnlineUserChip(Models.UserAccount user, int maxChars)
    {
        var chip = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 2, 8, 2),
            Padding = new Padding(4, 2, 4, 2)
        };

        var icon = new PictureBox
        {
            Width = 20,
            Height = 20,
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 0, 4, 0),
            Image = CreateUserIconImage(user.IconBlob)
        };

        var displayName = user.DisplayName;
        if (displayName.Length > maxChars)
        {
            displayName = $"{displayName[..Math.Max(3, maxChars - 1)]}…";
        }

        var label = new Label
        {
            AutoSize = true,
            Text = $"{displayName} ({FormatLastActivity(user.LastActivityUtc)})",
            Margin = new Padding(0, 2, 0, 0)
        };

        var fontSize = Math.Max(6.8f, Math.Min(8.5f, 9.5f - (Math.Max(onlineUsersPanel.Controls.Count - 2, 0) * 0.25f)));
        label.Font = new Font("Segoe UI", fontSize, FontStyle.Regular);

        chip.Controls.Add(icon);
        chip.Controls.Add(label);
        return chip;
    }

    private static Image? CreateUserIconImage(byte[]? iconBytes)
    {
        if (iconBytes is null || iconBytes.Length == 0)
        {
            return null;
        }

        using var stream = new MemoryStream(iconBytes);
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private static string FormatLastActivity(DateTime? lastActivityUtc)
    {
        if (!lastActivityUtc.HasValue)
        {
            return "active now";
        }

        var ago = DateTime.UtcNow - lastActivityUtc.Value;
        if (ago.TotalMinutes < 1)
        {
            return "active now";
        }

        if (ago.TotalHours < 1)
        {
            return $"{Math.Max(1, (int)ago.TotalMinutes)}m ago";
        }

        return $"{Math.Max(1, (int)ago.TotalHours)}h ago";
    }

    private async Task LoadAndApplySettingsAsync()
    {
        _appSettings = await _settings.LoadAsync();
        ApplySettings(_appSettings);
    }

    private void ApplySettings(Models.AppSettings settings)
    {
        _appSettings = settings;
        ApplyThemeFromSettings(settings.Theme);
        lblAppTitle.Text = string.IsNullOrWhiteSpace(settings.CompanyName) ? "Company" : settings.CompanyName;
        picCompanyLogo.Image?.Dispose();
        picCompanyLogo.Image = null;
        if (settings.CompanyLogo is { Length: > 0 })
        {
            using var stream = new MemoryStream(settings.CompanyLogo);
            using var image = Image.FromStream(stream);
            picCompanyLogo.Image = new Bitmap(image);
        }
    }

    private void OnThemeChanged(AppTheme theme)
    {
        ApplyThemeFromSettings(theme == AppTheme.Dark ? "Dark" : "Light");
    }

    private void ApplyThemeFromSettings(string? themeName)
    {
        var targetTheme = string.Equals(themeName, "dark", StringComparison.OrdinalIgnoreCase) ? AppTheme.Dark : AppTheme.Light;
        if (_themeManager.CurrentTheme == targetTheme)
        {
            return;
        }

        _themeManager.ToggleTheme();
    }

    private void WireEvents()
    {
        btnDashboard.Click += (_, _) => LoadSection("Dashboard");
        btnQuotes.Click += (_, _) => LoadSection("Quotes");
        btnProduction.Click += (_, _) => LoadSection("Production");
        btnCRM.Click += (_, _) => LoadSection("CRM");
        btnQuality.Click += (_, _) => LoadSection("Quality");
        btnInspection.Click += (_, _) => LoadSection("Inspection");
        btnShipping.Click += (_, _) => LoadSection("Shipping");
        btnSettingsMenu.Click += (_, _) => LoadSection("Settings");
    }

    private void OpenDashboardTarget(DashboardNavigationTarget target)
    {
        LoadSection(target.SectionKey);

        if (!target.OpenDetails)
        {
            return;
        }

        BeginInvoke(async () => await OpenSectionDetailsAsync(target));
    }

    private async Task OpenSectionDetailsAsync(DashboardNavigationTarget target)
    {
        if (mainContentPanel.Controls.Count == 0)
        {
            return;
        }

        var activeControl = mainContentPanel.Controls[0];

        switch (activeControl)
        {
            case Controls.QuotesControl quotesControl when target.QuoteId.HasValue:
                await quotesControl.OpenFromDashboardAsync(target.QuoteId.Value, openDetails: true);
                break;
            case ProductionControl productionControl when !string.IsNullOrWhiteSpace(target.JobNumber):
                await productionControl.OpenFromDashboardAsync(target.JobNumber, openDetails: true);
                break;
            case QualityControl qualityControl when !string.IsNullOrWhiteSpace(target.JobNumber):
                await qualityControl.OpenFromDashboardAsync(target.JobNumber, openDetails: true);
                break;
            case InspectionControl inspectionControl when !string.IsNullOrWhiteSpace(target.JobNumber):
                await inspectionControl.OpenFromDashboardAsync(target.JobNumber, openDetails: true);
                break;
            case ShippingControl shippingControl when !string.IsNullOrWhiteSpace(target.JobNumber):
                await shippingControl.OpenFromDashboardAsync(target.JobNumber, openDetails: true);
                break;
        }
    }

    private void LoadSection(string key)
    {
        var control = CreateControlForKey(key);
        mainContentPanel.SuspendLayout();
        mainContentPanel.Controls.Clear();
        control.Dock = DockStyle.Fill;
        mainContentPanel.Controls.Add(control);
        mainContentPanel.ResumeLayout();

        MarkActiveButton(key);
        ApplyTheme();
    }

    private UserControl CreateControlForKey(string key)
    {
        return key switch
        {
            "Dashboard" => new DashboardControl(_quoteRepo, _prodRepo, _jobFlow, OpenDashboardTarget),
            "Quotes" => new Controls.QuotesControl(_quoteRepo, _prodRepo, _currentUser, LoadSection),
            "Production" => new ProductionControl(_prodRepo, _jobFlow, _currentUser, LoadSection),
            "CRM" => new CRMControl(_quoteRepo),
            "Quality" => new QualityControl(_prodRepo, _jobFlow, _currentUser, LoadSection),
            "Inspection" => new InspectionControl(_prodRepo, _jobFlow, _inspection, _currentUser, LoadSection),
            "Shipping" => new ShippingControl(_prodRepo, _jobFlow, _currentUser, LoadSection),
            "Settings" => new SettingsControl(
                _settings,
                _userRepo,
                _currentUser,
                canManageSettings: true,
                settingsChanged: ApplySettings,
                currentTheme: _themeManager.CurrentTheme,
                themeChanged: OnThemeChanged),
            _ => BuildPlaceholder("Not Found", "The requested section is not available.")
        };
    }

    private static UserControl BuildPlaceholder(string title, string text)
    {
        var panel = new UserControl { BackColor = Color.Transparent };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(24)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            Height = 44
        };

        var bodyLabel = new Label
        {
            Text = text,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 10F),
            Height = 28
        };

        layout.Controls.Add(titleLabel, 0, 0);
        layout.Controls.Add(bodyLabel, 0, 1);
        panel.Controls.Add(layout);

        return panel;
    }

    private void MarkActiveButton(string activeKey)
    {
        foreach (var pair in _navButtons)
        {
            pair.Value.Tag = pair.Key.Equals(activeKey, StringComparison.OrdinalIgnoreCase) ? "active" : "idle";
        }
    }

    private void ApplyTheme()
    {
        _themeManager.ApplyTheme(this);
        var palette = _themeManager.CurrentPalette;

        headerPanel.BackColor = palette.Panel;
        tabStripPanel.BackColor = palette.Panel;
        mainContentPanel.BackColor = palette.Background;
        lblAppTitle.ForeColor = palette.TextPrimary;
        lblSyncClock.ForeColor = palette.TextSecondary;
        lblSaveClock.ForeColor = palette.TextSecondary;
        foreach (Control c in onlineUsersPanel.Controls)
        {
            c.ForeColor = palette.TextSecondary;
        }

        foreach (var button in _navButtons.Values)
        {
            var active = Equals(button.Tag, "active");
            button.IsActive = active;
            button.OverrideBaseColor = active ? palette.Accent : palette.Panel;
            button.OverrideBorderColor = active ? palette.Accent : palette.Border;
            button.ForeColor = active ? Color.White : palette.TextPrimary;
            button.Invalidate();
        }
    }
}
