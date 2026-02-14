namespace ERPSystem.WinForms.Forms;

public partial class ERPMainForm
{
    private TableLayoutPanel rootLayout = null!;
    private Panel headerPanel = null!;
    private Panel tabStripPanel = null!;
    private Panel mainContentPanel = null!;

    private Label lblAppTitle = null!;
    private Label lblSection = null!;
    private ModernButton btnThemeToggle = null!;

    private FlowLayoutPanel navButtonsPanel = null!;
    private ModernButton btnDashboard = null!;
    private ModernButton btnQuotes = null!;
    private ModernButton btnProduction = null!;
    private ModernButton btnQuality = null!;
    private ModernButton btnInspection = null!;
    private ModernButton btnShipping = null!;
    private ModernButton btnUsers = null!;
    private ModernButton btnSettings = null!;

    private void InitializeComponent()
    {
        rootLayout = new TableLayoutPanel();
        headerPanel = new Panel();
        tabStripPanel = new Panel();
        mainContentPanel = new Panel();

        lblAppTitle = new Label();
        lblSection = new Label();
        btnThemeToggle = new ModernButton();

        navButtonsPanel = new FlowLayoutPanel();
        btnDashboard = new ModernButton();
        btnQuotes = new ModernButton();
        btnProduction = new ModernButton();
        btnQuality = new ModernButton();
        btnInspection = new ModernButton();
        btnShipping = new ModernButton();
        btnUsers = new ModernButton();
        btnSettings = new ModernButton();

        SuspendLayout();

        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Text = "ERP System";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1200, 760);
        WindowState = FormWindowState.Maximized;

        rootLayout.Dock = DockStyle.Fill;
        rootLayout.RowCount = 3;
        rootLayout.ColumnCount = 1;
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        headerPanel.Dock = DockStyle.Fill;
        headerPanel.Padding = new Padding(20, 12, 20, 12);

        lblAppTitle.AutoSize = true;
        lblAppTitle.Text = "ERP Command Center";
        lblAppTitle.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
        lblAppTitle.Location = new Point(20, 17);

        lblSection.AutoSize = true;
        lblSection.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        lblSection.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblSection.Location = new Point(540, 22);

        btnThemeToggle.Text = "☾ Dark";
        btnThemeToggle.Size = new Size(108, 36);
        btnThemeToggle.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnThemeToggle.Location = new Point(660, 15);
        btnThemeToggle.CornerRadius = 8;

        headerPanel.Resize += (_, _) =>
        {
            btnThemeToggle.Left = headerPanel.Width - btnThemeToggle.Width - 20;
            lblSection.Left = btnThemeToggle.Left - lblSection.Width - 16;
        };

        headerPanel.Controls.Add(lblAppTitle);
        headerPanel.Controls.Add(lblSection);
        headerPanel.Controls.Add(btnThemeToggle);

        tabStripPanel.Dock = DockStyle.Fill;
        tabStripPanel.Padding = new Padding(12, 10, 12, 10);

        navButtonsPanel.Dock = DockStyle.Fill;
        navButtonsPanel.FlowDirection = FlowDirection.LeftToRight;
        navButtonsPanel.WrapContents = false;
        navButtonsPanel.AutoScroll = true;

        ConfigureNavButton(btnDashboard, "Dashboard");
        ConfigureNavButton(btnQuotes, "Quotes");
        ConfigureNavButton(btnProduction, "Production");
        ConfigureNavButton(btnQuality, "Quality");
        ConfigureNavButton(btnInspection, "Inspection");
        ConfigureNavButton(btnShipping, "Shipping");
        ConfigureNavButton(btnUsers, "Users");
        ConfigureNavButton(btnSettings, "Settings");

        navButtonsPanel.Controls.Add(btnDashboard);
        navButtonsPanel.Controls.Add(btnQuotes);
        navButtonsPanel.Controls.Add(btnProduction);
        navButtonsPanel.Controls.Add(btnQuality);
        navButtonsPanel.Controls.Add(btnInspection);
        navButtonsPanel.Controls.Add(btnShipping);
        navButtonsPanel.Controls.Add(btnUsers);
        navButtonsPanel.Controls.Add(btnSettings);
        tabStripPanel.Controls.Add(navButtonsPanel);

        mainContentPanel.Dock = DockStyle.Fill;
        mainContentPanel.Padding = new Padding(14);

        rootLayout.Controls.Add(headerPanel, 0, 0);
        rootLayout.Controls.Add(tabStripPanel, 0, 1);
        rootLayout.Controls.Add(mainContentPanel, 0, 2);

        Controls.Add(rootLayout);

        ResumeLayout(false);
    }

    private static void ConfigureNavButton(ModernButton button, string text)
    {
        button.Text = text;
        button.Width = 142;
        button.Height = 40;
        button.Margin = new Padding(0, 0, 10, 0);
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.Padding = new Padding(0);
        button.CornerRadius = 10;
    }
}
