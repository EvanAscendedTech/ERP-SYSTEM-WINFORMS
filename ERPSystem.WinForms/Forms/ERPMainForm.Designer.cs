namespace ERPSystem.WinForms.Forms;

public partial class ERPMainForm
{
    private TableLayoutPanel rootLayout = null!;
    private Panel headerPanel = null!;
    private Panel navPanel = null!;
    private Panel mainContentPanel = null!;

    private Label lblAppTitle = null!;
    private Label lblSection = null!;
    private ModernButton btnThemeToggle = null!;

    private FlowLayoutPanel navButtonsPanel = null!;
    private ModernButton btnDashboard = null!;
    private ModernButton btnQuotes = null!;
    private ModernButton btnProduction = null!;
    private ModernButton btnUsers = null!;
    private ModernButton btnSettings = null!;

    private void InitializeComponent()
    {
        rootLayout = new TableLayoutPanel();
        headerPanel = new Panel();
        navPanel = new Panel();
        mainContentPanel = new Panel();

        lblAppTitle = new Label();
        lblSection = new Label();
        btnThemeToggle = new ModernButton();

        navButtonsPanel = new FlowLayoutPanel();
        btnDashboard = new ModernButton();
        btnQuotes = new ModernButton();
        btnProduction = new ModernButton();
        btnUsers = new ModernButton();
        btnSettings = new ModernButton();

        SuspendLayout();

        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Text = "ERP System";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 700);

        rootLayout.Dock = DockStyle.Fill;
        rootLayout.RowCount = 2;
        rootLayout.ColumnCount = 2;
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        headerPanel.Dock = DockStyle.Fill;
        headerPanel.Padding = new Padding(16, 12, 16, 12);

        lblAppTitle.AutoSize = true;
        lblAppTitle.Text = "ERP Command Center";
        lblAppTitle.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        lblAppTitle.Location = new Point(16, 15);

        lblSection.AutoSize = true;
        lblSection.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
        lblSection.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblSection.Location = new Point(540, 20);

        btnThemeToggle.Text = "☾ Dark";
        btnThemeToggle.Size = new Size(96, 34);
        btnThemeToggle.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnThemeToggle.Location = new Point(660, 14);
        btnThemeToggle.CornerRadius = 5;

        headerPanel.Resize += (_, _) =>
        {
            btnThemeToggle.Left = headerPanel.Width - btnThemeToggle.Width - 16;
            lblSection.Left = btnThemeToggle.Left - lblSection.Width - 16;
        };

        headerPanel.Controls.Add(lblAppTitle);
        headerPanel.Controls.Add(lblSection);
        headerPanel.Controls.Add(btnThemeToggle);

        navPanel.Dock = DockStyle.Fill;
        navPanel.Padding = new Padding(12);

        navButtonsPanel.Dock = DockStyle.Fill;
        navButtonsPanel.FlowDirection = FlowDirection.TopDown;
        navButtonsPanel.WrapContents = false;

        ConfigureNavButton(btnDashboard, "Dashboard");
        ConfigureNavButton(btnQuotes, "Quotes");
        ConfigureNavButton(btnProduction, "Production");
        ConfigureNavButton(btnUsers, "Users");
        ConfigureNavButton(btnSettings, "Settings");

        navButtonsPanel.Controls.Add(btnDashboard);
        navButtonsPanel.Controls.Add(btnQuotes);
        navButtonsPanel.Controls.Add(btnProduction);
        navButtonsPanel.Controls.Add(btnUsers);
        navButtonsPanel.Controls.Add(btnSettings);
        navPanel.Controls.Add(navButtonsPanel);

        mainContentPanel.Dock = DockStyle.Fill;
        mainContentPanel.Padding = new Padding(12);

        rootLayout.Controls.Add(navPanel, 0, 0);
        rootLayout.SetRowSpan(navPanel, 2);
        rootLayout.Controls.Add(headerPanel, 1, 0);
        rootLayout.Controls.Add(mainContentPanel, 1, 1);

        Controls.Add(rootLayout);

        ResumeLayout(false);
    }

    private static void ConfigureNavButton(ModernButton button, string text)
    {
        button.Text = text;
        button.Width = 192;
        button.Height = 42;
        button.Margin = new Padding(0, 0, 0, 10);
        button.TextAlign = ContentAlignment.MiddleLeft;
        button.Padding = new Padding(14, 0, 0, 0);
        button.CornerRadius = 5;
    }
}
