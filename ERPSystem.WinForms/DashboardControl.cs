using System.Drawing;
using System.Windows.Forms;

namespace ERPSystem.WinForms;

public sealed class DashboardControl : UserControl
{
    private readonly Label titleLabel;
    private readonly Label subTitleLabel;
    private readonly TableLayoutPanel cardsLayout;

    public DashboardControl()
    {
        DoubleBuffered = true;
        Dock = DockStyle.Fill;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        titleLabel = new Label
        {
            AutoSize = true,
            Text = "Dashboard",
            Font = new Font("Segoe UI", 18F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 0, 0, 4)
        };

        subTitleLabel = new Label
        {
            AutoSize = true,
            Text = "Real-time snapshot of quote, production and workflow KPIs.",
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0, 0, 0, 12)
        };

        cardsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            AutoSize = false
        };
        cardsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        cardsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        cardsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        cardsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        cardsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        cardsLayout.Controls.Add(CreateMetricCard("Open Quotes", "26"), 0, 0);
        cardsLayout.Controls.Add(CreateMetricCard("Pending Approvals", "7"), 1, 0);
        cardsLayout.Controls.Add(CreateMetricCard("Production Jobs", "15"), 2, 0);
        cardsLayout.Controls.Add(CreateMetricCard("On-time Delivery", "96%"), 0, 1);
        cardsLayout.Controls.Add(CreateMetricCard("Machine Utilization", "82%"), 1, 1);
        cardsLayout.Controls.Add(CreateMetricCard("Scrap Rate", "1.7%"), 2, 1);

        root.Controls.Add(titleLabel, 0, 0);
        root.Controls.Add(subTitleLabel, 0, 1);
        root.Controls.Add(cardsLayout, 0, 2);

        Controls.Add(root);
    }

    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = palette.Background;
        ForeColor = palette.TextPrimary;
        titleLabel.ForeColor = palette.TextPrimary;
        subTitleLabel.ForeColor = palette.TextSecondary;

        foreach (Control card in cardsLayout.Controls)
        {
            card.BackColor = palette.Panel;
            card.ForeColor = palette.TextPrimary;

            foreach (Control child in card.Controls)
            {
                child.ForeColor = child.Tag?.ToString() == "secondary"
                    ? palette.TextSecondary
                    : palette.TextPrimary;
            }
        }
    }

    private static Panel CreateMetricCard(string metric, string value)
    {
        var panel = new Panel
        {
            Margin = new Padding(8),
            Padding = new Padding(14),
            Dock = DockStyle.Fill
        };

        var valueLabel = new Label
        {
            Text = value,
            Font = new Font("Segoe UI", 20F, FontStyle.Bold, GraphicsUnit.Point),
            Dock = DockStyle.Top,
            Height = 44
        };

        var metricLabel = new Label
        {
            Text = metric,
            Dock = DockStyle.Top,
            Tag = "secondary",
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point)
        };

        panel.Controls.Add(metricLabel);
        panel.Controls.Add(valueLabel);

        return panel;
    }
}
