namespace ERPSystem.WinForms;

public sealed class DashboardControl : UserControl
{
    public DashboardControl()
    {
        DoubleBuffered = true;
        Dock = DockStyle.Fill;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(20)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var title = new Label
        {
            Text = "Dashboard",
            Font = new Font("Segoe UI", 19F, FontStyle.Bold),
            AutoSize = true
        };

        var subtitle = new Label
        {
            Text = "Operational summary across quotes, production and users.",
            Font = new Font("Segoe UI", 10F),
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 16),
            Tag = "secondary"
        };

        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2
        };

        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        cards.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        cards.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

        cards.Controls.Add(CreateCard("Open Quotes", "42"), 0, 0);
        cards.Controls.Add(CreateCard("In Production", "18"), 1, 0);
        cards.Controls.Add(CreateCard("Active Users", "9"), 2, 0);
        cards.Controls.Add(CreateCard("On-time Delivery", "95%"), 0, 1);
        cards.Controls.Add(CreateCard("Inspection Queue", "6"), 1, 1);
        cards.Controls.Add(CreateCard("Archive This Month", "114"), 2, 1);

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(subtitle, 0, 1);
        root.Controls.Add(cards, 0, 2);
        Controls.Add(root);
    }


    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = palette.Background;
        ForeColor = palette.TextPrimary;
    }

    private static Panel CreateCard(string metric, string value)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(8),
            Padding = new Padding(14)
        };

        var valueLabel = new Label
        {
            Text = value,
            Font = new Font("Segoe UI", 22F, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 46
        };

        var metricLabel = new Label
        {
            Text = metric,
            Font = new Font("Segoe UI", 10F),
            Dock = DockStyle.Top,
            Tag = "secondary"
        };

        panel.Controls.Add(metricLabel);
        panel.Controls.Add(valueLabel);

        return panel;
    }
}
