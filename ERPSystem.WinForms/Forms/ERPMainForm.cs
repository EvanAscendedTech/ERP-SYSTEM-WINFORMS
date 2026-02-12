using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Forms;

public partial class ERPMainForm : Form
{
    private readonly QuoteRepository _quoteRepo;
    private readonly ProductionRepository _prodRepo;
    private readonly UserManagementRepository _userRepo;
    private readonly AppSettingsService _settings;
    private readonly InspectionService _inspection;
    private readonly ArchiveService _archive;
    private readonly ThemeManager _themeManager = new();

    private readonly Dictionary<string, ModernButton> _navButtons;

    public ERPMainForm(QuoteRepository quoteRepo, ProductionRepository prodRepo, UserManagementRepository userRepo,
               AppSettingsService settings, InspectionService inspection, ArchiveService archive)
    {
        _quoteRepo = quoteRepo;
        _prodRepo = prodRepo;
        _userRepo = userRepo;
        _settings = settings;
        _inspection = inspection;
        _archive = archive;

        InitializeComponent();
        DoubleBuffered = true;

        _navButtons = new Dictionary<string, ModernButton>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dashboard"] = btnDashboard,
            ["Quotes"] = btnQuotes,
            ["Production"] = btnProduction,
            ["Users"] = btnUsers,
            ["Settings"] = btnSettings
        };

        _themeManager.ThemeChanged += (_, _) => ApplyTheme();
        WireEvents();

        LoadSection("Dashboard");
        ApplyTheme();
    }

    private void WireEvents()
    {
        btnDashboard.Click += (_, _) => LoadSection("Dashboard");
        btnQuotes.Click += (_, _) => LoadSection("Quotes");
        btnProduction.Click += (_, _) => LoadSection("Production");
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
            "Dashboard" => new DashboardControl(),
            "Quotes" => new QuotesControl(),
            "Production" => BuildPlaceholder("Production", "Production queue and scheduling will render here."),
            "Users" => BuildPlaceholder("Users", "User administration and permissions will render here."),
            "Settings" => BuildPlaceholder("Settings", "Application settings and preferences will render here."),
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
        navPanel.BackColor = palette.Panel;
        mainContentPanel.BackColor = palette.Background;

        foreach (var button in _navButtons.Values)
        {
            var active = Equals(button.Tag, "active");
            button.OverrideBaseColor = active ? palette.Accent : palette.Panel;
            button.OverrideBorderColor = active ? palette.Accent : palette.Border;
            button.ForeColor = active ? Color.White : palette.TextPrimary;
            button.Invalidate();
        }
    }
}
