namespace ERPSystem.WinForms.Forms;

public partial class ERPMainForm
{
    private TableLayoutPanel rootLayout = null!;
    private Panel headerPanel = null!;
    private Panel tabStripPanel = null!;
    private Panel mainContentPanel = null!;

    private PictureBox picCompanyLogo = null!;
    private Label lblAppTitle = null!;
    private ModernButton btnSettingsMenu = null!;
    private ModernButton btnBack = null!;
    private ModernButton btnForward = null!;
    private FlowLayoutPanel onlineUsersPanel = null!;

    private FlowLayoutPanel navButtonsPanel = null!;
    private ModernButton btnDashboard = null!;
    private ModernButton btnQuotes = null!;
    private ModernButton btnPurchasing = null!;
    private ModernButton btnProduction = null!;
    private ModernButton btnCRM = null!;
    private ModernButton btnInspection = null!;
    private ModernButton btnShipping = null!;

    private void InitializeComponent()
    {
        rootLayout = new TableLayoutPanel();
        headerPanel = new Panel();
        tabStripPanel = new Panel();
        mainContentPanel = new Panel();

        picCompanyLogo = new PictureBox();
        lblAppTitle = new Label();
        btnSettingsMenu = new ModernButton();
        btnBack = new ModernButton();
        btnForward = new ModernButton();
        onlineUsersPanel = new FlowLayoutPanel();

        navButtonsPanel = new FlowLayoutPanel();
        btnDashboard = new ModernButton();
        btnQuotes = new ModernButton();
        btnPurchasing = new ModernButton();
        btnProduction = new ModernButton();
        btnCRM = new ModernButton();
        btnInspection = new ModernButton();
        btnShipping = new ModernButton();

        SuspendLayout();

        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Text = "INGNITON";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1200, 760);
        WindowState = FormWindowState.Maximized;

        rootLayout.Dock = DockStyle.Fill;
        rootLayout.RowCount = 3;
        rootLayout.ColumnCount = 1;
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        headerPanel.Dock = DockStyle.Fill;
        headerPanel.Padding = new Padding(20, 10, 20, 10);

        picCompanyLogo.Size = new Size(54, 54);
        picCompanyLogo.Location = new Point(20, 16);
        picCompanyLogo.SizeMode = PictureBoxSizeMode.Zoom;

        lblAppTitle.AutoSize = true;
        lblAppTitle.Text = "ERP Command Center";
        lblAppTitle.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
        lblAppTitle.Location = new Point(84, 16);

        btnSettingsMenu.Text = "Settings";
        btnSettingsMenu.Size = new Size(120, 36);
        btnSettingsMenu.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnSettingsMenu.Location = new Point(648, 14);
        btnSettingsMenu.CornerRadius = 8;

        btnBack.Text = "◀";
        btnBack.Size = new Size(42, 36);
        btnBack.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnBack.Location = new Point(556, 14);
        btnBack.CornerRadius = 8;

        btnForward.Text = "▶";
        btnForward.Size = new Size(42, 36);
        btnForward.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnForward.Location = new Point(602, 14);
        btnForward.CornerRadius = 8;

        onlineUsersPanel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        onlineUsersPanel.AutoScroll = true;
        onlineUsersPanel.WrapContents = false;
        onlineUsersPanel.FlowDirection = FlowDirection.LeftToRight;
        onlineUsersPanel.Location = new Point(430, 42);
        onlineUsersPanel.Size = new Size(420, 30);

        headerPanel.Resize += (_, _) =>
        {
            btnSettingsMenu.Left = headerPanel.Width - btnSettingsMenu.Width - 20;
            btnForward.Left = btnSettingsMenu.Left - btnForward.Width - 8;
            btnBack.Left = btnForward.Left - btnBack.Width - 8;
            onlineUsersPanel.Left = btnBack.Left - onlineUsersPanel.Width - 20;
            onlineUsersPanel.Width = btnSettingsMenu.Left - onlineUsersPanel.Left - 20;
        };

        headerPanel.Controls.Add(picCompanyLogo);
        headerPanel.Controls.Add(lblAppTitle);
        headerPanel.Controls.Add(btnBack);
        headerPanel.Controls.Add(btnForward);
        headerPanel.Controls.Add(btnSettingsMenu);
        headerPanel.Controls.Add(onlineUsersPanel);

        tabStripPanel.Dock = DockStyle.Fill;
        tabStripPanel.Padding = new Padding(12, 6, 12, 6);

        navButtonsPanel.Dock = DockStyle.Fill;
        navButtonsPanel.FlowDirection = FlowDirection.LeftToRight;
        navButtonsPanel.WrapContents = false;
        navButtonsPanel.AutoScroll = false;

        ConfigureNavButton(btnDashboard, "Dashboard");
        ConfigureNavButton(btnQuotes, "Quotes");
        ConfigureNavButton(btnPurchasing, "Purchasing");
        ConfigureNavButton(btnProduction, "Production");
        ConfigureNavButton(btnCRM, "CRM");
        ConfigureNavButton(btnInspection, "Inspection");
        ConfigureNavButton(btnShipping, "Shipping");

        navButtonsPanel.Controls.Add(btnDashboard);
        navButtonsPanel.Controls.Add(btnQuotes);
        navButtonsPanel.Controls.Add(btnPurchasing);
        navButtonsPanel.Controls.Add(btnProduction);
        navButtonsPanel.Controls.Add(btnInspection);
        navButtonsPanel.Controls.Add(btnShipping);
        navButtonsPanel.Controls.Add(btnCRM);

        tabStripPanel.Resize += (_, _) =>
        {
            var spacing = 8;
            var availableWidth = Math.Max(700, tabStripPanel.ClientSize.Width - tabStripPanel.Padding.Horizontal);
            var buttonCount = navButtonsPanel.Controls.Count;
            var width = Math.Max(92, (availableWidth - (spacing * (buttonCount - 1))) / buttonCount);

            foreach (Control control in navButtonsPanel.Controls)
            {
                if (control is ModernButton navButton)
                {
                    navButton.Width = width;
                    navButton.Margin = new Padding(0, 0, spacing, 0);
                }
            }

            if (navButtonsPanel.Controls.Count > 0)
            {
                navButtonsPanel.Controls[^1].Margin = new Padding(0);
            }
        };

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
        button.Height = 34;
        button.Margin = new Padding(0, 0, 10, 0);
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.Padding = new Padding(0);
        button.CornerRadius = 8;
    }
}
