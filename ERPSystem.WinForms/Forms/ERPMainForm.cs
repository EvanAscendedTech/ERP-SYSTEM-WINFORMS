using ERPSystem.WinForms.Controls;
using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Forms;

public partial class ERPMainForm : Form
{
    private static readonly TimeSpan FailSafeInterval = TimeSpan.FromMinutes(2.5);

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
    private long _lastSeenRealtimeEventId;
    private bool _syncTickRunning;
    private bool _refreshRunning;

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
            ["Users"] = btnUsers,
            ["Settings"] = btnSettings
        };

        _themeManager.ThemeChanged += (_, _) => ApplyTheme();
        btnUsers.Visible = _currentUser.Roles.Any(r => string.Equals(r.Name, "Admin", StringComparison.OrdinalIgnoreCase) || string.Equals(r.Name, "Administrator", StringComparison.OrdinalIgnoreCase));
        WireEvents();
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

                UpdateSyncClockText(tickNow);
            }
            finally
            {
                _syncTickRunning = false;
            }
        };

        UpdateSyncClockText(now);
        _syncClockTimer.Start();

        FormClosed += (_, _) => _syncClockTimer.Stop();
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

    private void WireEvents()
    {
        btnDashboard.Click += (_, _) => LoadSection("Dashboard");
        btnQuotes.Click += (_, _) => LoadSection("Quotes");
        btnProduction.Click += (_, _) => LoadSection("Production");
        btnCRM.Click += (_, _) => LoadSection("CRM");
        btnQuality.Click += (_, _) => LoadSection("Quality");
        btnInspection.Click += (_, _) => LoadSection("Inspection");
        btnShipping.Click += (_, _) => LoadSection("Shipping");
        btnUsers.Click += (_, _) => LoadSection("Users");
        btnSettings.Click += (_, _) => LoadSection("Settings");

        btnThemeToggle.Click += (_, _) =>
        {
            _themeManager.ToggleTheme();
            btnThemeToggle.Text = _themeManager.CurrentTheme == AppTheme.Dark ? "☾ Dark" : "☀ Light";
        };
    }

    private void LoadSection(string key)
    {
        var control = CreateControlForKey(key);
        mainContentPanel.SuspendLayout();
        mainContentPanel.Controls.Clear();
        control.Dock = DockStyle.Fill;
        mainContentPanel.Controls.Add(control);
        mainContentPanel.ResumeLayout();

        lblSection.Text = key;
        MarkActiveButton(key);
        ApplyTheme();
    }

    private UserControl CreateControlForKey(string key)
    {
        return key switch
        {
            "Dashboard" => new DashboardControl(_quoteRepo, _prodRepo, _jobFlow, LoadSection),
            "Quotes" => new Controls.QuotesControl(_quoteRepo, _prodRepo, _currentUser, LoadSection),
            "Production" => new ProductionControl(_prodRepo, _jobFlow, _currentUser, LoadSection),
            "CRM" => new CRMControl(_quoteRepo),
            "Quality" => new QualityControl(_prodRepo, _jobFlow, _currentUser, LoadSection),
            "Inspection" => new InspectionControl(_prodRepo, _jobFlow, _inspection, _currentUser, LoadSection),
            "Shipping" => new ShippingControl(_prodRepo, _jobFlow, _currentUser, LoadSection),
            "Users" => new UsersControl(_userRepo, _currentUser, () => { }),
            "Settings" => new SettingsControl(_settings, canManageSettings: true, companyNameChanged: name => lblAppTitle.Text = $"{name} Command Center"),
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
        lblSyncClock.ForeColor = palette.TextSecondary;
        lblSaveClock.ForeColor = palette.TextSecondary;

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
