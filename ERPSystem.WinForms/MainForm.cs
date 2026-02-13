using System.Drawing;
using System.Windows.Forms;
using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms;

public interface IMainView
{
    void SetStatus(string statusText);
    void ShowPage(Control page);
}

public sealed class MainPresenter
{
    private readonly IMainView view;

    public MainPresenter(IMainView view)
    {
        this.view = view;
    }

    public void OnShortcutNew() => view.SetStatus("Shortcut triggered: Create new ERP document (Ctrl+N)");

    public void OnShortcutSave() => view.SetStatus("Shortcut triggered: Save ERP document (Ctrl+S)");
}

public partial class MainForm : Form, IMainView
{
    private const int ExpandedSidebarWidth = 230;
    private const int CollapsedSidebarWidth = 72;

    private readonly Dictionary<string, Control> pages = new();
    private readonly ThemeManager themeManager = new();
    private readonly MainPresenter presenter;
    private readonly QuoteRepository quoteRepository;
    private readonly ProductionRepository productionRepository;
    private readonly JobFlowService jobFlowService = new();

    private bool sidebarCollapsed;

    public MainForm()
    {
        presenter = new MainPresenter(this);

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ERPSystem.WinForms",
            "erp_system.db");

        quoteRepository = new QuoteRepository(dbPath);
        productionRepository = new ProductionRepository(dbPath);

        InitializeComponent();
        DoubleBuffered = true;

        themeManager.ThemeChanged += (_, _) => ApplyTheme();

        WireEvents();
        InitializePages();
        ApplyTheme();
        ActivatePage("Dashboard");
    }

    public void SetStatus(string statusText) => statusLabel.Text = statusText;

    public void ShowPage(Control page)
    {
        mainContentPanel.SuspendLayout();
        mainContentPanel.Controls.Clear();
        page.Dock = DockStyle.Fill;
        mainContentPanel.Controls.Add(page);
        mainContentPanel.ResumeLayout();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.N))
        {
            presenter.OnShortcutNew();
            return true;
        }

        if (keyData == (Keys.Control | Keys.S))
        {
            presenter.OnShortcutSave();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void WireEvents()
    {
        themeToggleButton.Click += (_, _) => themeManager.ToggleTheme();
        sidebarToggleButton.Click += (_, _) => ToggleSidebar();

        dashboardButton.Click += (_, _) => ActivatePage("Dashboard");
        quotesButton.Click += (_, _) => ActivatePage("Quotes");
        productionButton.Click += (_, _) => ActivatePage("Production");
        settingsButton.Click += (_, _) => ActivatePage("Settings");

        foreach (var button in new[] { dashboardButton, quotesButton, productionButton, settingsButton })
        {
            button.MouseEnter += (_, _) =>
            {
                if (!Equals(button.Tag, "active"))
                {
                    button.BackColor = themeManager.CurrentPalette.NavHover;
                }
            };
            button.MouseLeave += (_, _) =>
            {
                if (!Equals(button.Tag, "active"))
                {
                    button.BackColor = themeManager.CurrentPalette.Panel;
                }
            };

            toolTip.SetToolTip(button, $"Open {button.Text} page");
        }

        Resize += (_, _) =>
        {
            themeToggleButton.Left = topNavPanel.Width - themeToggleButton.Width - 10;
        };
    }

    private void InitializePages()
    {
        pages["Dashboard"] = new DashboardControl(quoteRepository, productionRepository, jobFlowService, ActivatePage);
        pages["Quotes"] = BuildPlaceholderPage("Quotes", "Quote creation, approvals, and customer pricing.");
        pages["Production"] = BuildPlaceholderPage("Production", "Work orders, machine queues, and schedules.");
        pages["Settings"] = BuildPlaceholderPage("Settings", "Company preferences, users, and integrations.");
    }

    private Control BuildPlaceholderPage(string title, string description)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };

        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 40,
            Font = new Font("Segoe UI", 18F, FontStyle.Bold)
        };

        var descriptionLabel = new Label
        {
            Text = description,
            Dock = DockStyle.Top,
            Height = 26,
            Font = new Font("Segoe UI", 10F)
        };

        var bindingHint = new Label
        {
            Text = "// ERP data bindings go here, e.g., quote grid bound to QuoteViewModel list.",
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font("Consolas", 9F),
            Padding = new Padding(0, 8, 0, 0)
        };

        panel.Controls.Add(bindingHint);
        panel.Controls.Add(descriptionLabel);
        panel.Controls.Add(titleLabel);

        return panel;
    }

    private void ActivatePage(string key)
    {
        if (!pages.TryGetValue(key, out var page))
        {
            return;
        }

        ShowPage(page);
        SetStatus($"Viewing {key}");
        MarkActiveButton(key);
        ApplyTheme();
    }

    private void MarkActiveButton(string key)
    {
        var map = new Dictionary<string, ModernButton>
        {
            ["Dashboard"] = dashboardButton,
            ["Quotes"] = quotesButton,
            ["Production"] = productionButton,
            ["Settings"] = settingsButton
        };

        foreach (var pair in map)
        {
            pair.Value.Tag = pair.Key == key ? "active" : "idle";
        }
    }

    private void ToggleSidebar()
    {
        var wrapper = (TableLayoutPanel)sidebarPanel.Parent!;
        sidebarCollapsed = !sidebarCollapsed;

        wrapper.ColumnStyles[0].Width = sidebarCollapsed ? CollapsedSidebarWidth : ExpandedSidebarWidth;
        foreach (var button in new[] { dashboardButton, quotesButton, productionButton, settingsButton })
        {
            button.TextAlign = sidebarCollapsed ? ContentAlignment.MiddleCenter : ContentAlignment.MiddleLeft;
            button.Padding = sidebarCollapsed ? Padding.Empty : new Padding(14, 0, 0, 0);
            button.Text = sidebarCollapsed ? button.Text[..1] : GetFullLabel(button);
            toolTip.SetToolTip(button, GetFullLabel(button));
        }

        SetStatus(sidebarCollapsed ? "Sidebar collapsed" : "Sidebar expanded");
    }

    private static string GetFullLabel(ModernButton button) => button.Name switch
    {
        nameof(dashboardButton) => "Dashboard",
        nameof(quotesButton) => "Quotes",
        nameof(productionButton) => "Production",
        nameof(settingsButton) => "Settings",
        _ => button.Text
    };

    private void ApplyTheme()
    {
        var palette = themeManager.CurrentPalette;

        BackColor = palette.Background;
        ForeColor = palette.TextPrimary;
        topNavPanel.BackColor = palette.Panel;
        sidebarPanel.BackColor = palette.Panel;
        mainContentPanel.BackColor = palette.Background;
        navButtonHost.BackColor = palette.Panel;

        appTitleLabel.ForeColor = palette.TextPrimary;
        statusStrip.BackColor = palette.Panel;
        statusLabel.ForeColor = palette.TextSecondary;

        themeManager.ApplyTheme(this);

        foreach (var button in new[] { dashboardButton, quotesButton, productionButton, settingsButton })
        {
            if (Equals(button.Tag, "active"))
            {
                button.BackColor = palette.Accent;
                button.ForeColor = Color.White;
            }
            else
            {
                button.BackColor = palette.Panel;
                button.ForeColor = palette.TextPrimary;
            }
        }

        if (mainContentPanel.Controls.Count > 0 && mainContentPanel.Controls[0] is DashboardControl dashboard)
        {
            dashboard.ApplyTheme(palette);
        }
    }
}
