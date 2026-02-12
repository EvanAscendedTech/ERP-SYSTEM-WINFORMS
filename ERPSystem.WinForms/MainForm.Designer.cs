using System.Drawing;
using System.Windows.Forms;

namespace ERPSystem.WinForms;

public partial class MainForm
{
    private System.ComponentModel.IContainer components = null!;

    private TableLayoutPanel rootLayout = null!;
    private Panel topNavPanel = null!;
    private Panel sidebarPanel = null!;
    private Panel mainContentPanel = null!;
    private StatusStrip statusStrip = null!;
    private ToolStripStatusLabel statusLabel = null!;

    private Label appTitleLabel = null!;
    private ModernButton themeToggleButton = null!;
    private ModernButton sidebarToggleButton = null!;
    private ToolTip toolTip = null!;

    private Panel navButtonHost = null!;
    private ModernButton dashboardButton = null!;
    private ModernButton quotesButton = null!;
    private ModernButton productionButton = null!;
    private ModernButton settingsButton = null!;

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        toolTip = new ToolTip(components);

        rootLayout = new TableLayoutPanel();
        topNavPanel = new Panel();
        sidebarPanel = new Panel();
        mainContentPanel = new Panel();
        statusStrip = new StatusStrip();
        statusLabel = new ToolStripStatusLabel();

        appTitleLabel = new Label();
        themeToggleButton = new ModernButton();
        sidebarToggleButton = new ModernButton();

        navButtonHost = new Panel();
        dashboardButton = new ModernButton();
        quotesButton = new ModernButton();
        productionButton = new ModernButton();
        settingsButton = new ModernButton();

        SuspendLayout();

        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Text = "ERP System - Modern UI";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1024, 680);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;

        rootLayout.Dock = DockStyle.Fill;
        rootLayout.ColumnCount = 1;
        rootLayout.RowCount = 3;
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));

        topNavPanel.Dock = DockStyle.Fill;
        topNavPanel.Padding = new Padding(12, 10, 12, 10);

        appTitleLabel.Text = "Lightweight ERP";
        appTitleLabel.AutoSize = true;
        appTitleLabel.Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point);
        appTitleLabel.Location = new Point(52, 13);

        sidebarToggleButton.Text = "≡";
        sidebarToggleButton.Size = new Size(32, 28);
        sidebarToggleButton.Location = new Point(10, 10);
        sidebarToggleButton.CornerRadius = 5;

        themeToggleButton.Text = "Toggle Theme";
        themeToggleButton.Size = new Size(124, 32);
        themeToggleButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        themeToggleButton.Location = new Point(Width - 160, 8);
        themeToggleButton.CornerRadius = 6;

        toolTip.SetToolTip(sidebarToggleButton, "Collapse / Expand sidebar");
        toolTip.SetToolTip(themeToggleButton, "Switch between dark and light themes");

        topNavPanel.Controls.Add(appTitleLabel);
        topNavPanel.Controls.Add(sidebarToggleButton);
        topNavPanel.Controls.Add(themeToggleButton);

        var contentWrapper = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        contentWrapper.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230F));
        contentWrapper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        sidebarPanel.Dock = DockStyle.Fill;
        sidebarPanel.Padding = new Padding(10);

        navButtonHost.Dock = DockStyle.Fill;

        ConfigureNavButton(dashboardButton, "Dashboard", 10);
        ConfigureNavButton(quotesButton, "Quotes", 58);
        ConfigureNavButton(productionButton, "Production", 106);
        ConfigureNavButton(settingsButton, "Settings", 154);

        navButtonHost.Controls.Add(dashboardButton);
        navButtonHost.Controls.Add(quotesButton);
        navButtonHost.Controls.Add(productionButton);
        navButtonHost.Controls.Add(settingsButton);
        sidebarPanel.Controls.Add(navButtonHost);

        mainContentPanel.Dock = DockStyle.Fill;
        mainContentPanel.Padding = new Padding(8);

        contentWrapper.Controls.Add(sidebarPanel, 0, 0);
        contentWrapper.Controls.Add(mainContentPanel, 1, 0);

        statusStrip.Dock = DockStyle.Fill;
        statusStrip.SizingGrip = false;
        statusStrip.Items.Add(statusLabel);
        statusLabel.Text = "Ready";

        rootLayout.Controls.Add(topNavPanel, 0, 0);
        rootLayout.Controls.Add(contentWrapper, 0, 1);
        rootLayout.Controls.Add(statusStrip, 0, 2);

        Controls.Add(rootLayout);

        ResumeLayout(false);
    }

    private static void ConfigureNavButton(ModernButton button, string text, int top)
    {
        button.Name = $"{char.ToLowerInvariant(text[0])}{text[1..]}Button";
        button.Text = text;
        button.Size = new Size(210, 40);
        button.Location = new Point(0, top);
        button.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        button.TextAlign = ContentAlignment.MiddleLeft;
        button.Padding = new Padding(14, 0, 0, 0);
        button.CornerRadius = 5;
    }
}
