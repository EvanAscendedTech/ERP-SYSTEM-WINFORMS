using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Controls;

public class PerformanceControl : UserControl
{
    public PerformanceControl()
    {
        Dock = DockStyle.Fill;

        var shipments = OperationalMetrics.GetShipmentsForLastDays(30);
        var qualityEvents = OperationalMetrics.GetQualityEventsForLastDays(30);
        var customerQualityFailures = qualityEvents
            .Where(x => x.IsCustomerEscape && shipments.Any(s => string.Equals(s.JobNumber, x.JobNumber, StringComparison.OrdinalIgnoreCase)))
            .Select(x => x.JobNumber)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var onTimeCount = shipments.Count(s => s.IsOnTime);
        var totalShipped = Math.Max(1, shipments.Count);
        var onTimePercent = onTimeCount * 100d / totalShipped;
        var qualityPercent = (totalShipped - customerQualityFailures) * 100d / totalShipped;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildSummaryCard("30-Day On-Time Delivery", $"{onTimePercent:0.0}%", $"{onTimeCount} on-time / {shipments.Count} shipped"), 0, 0);
        root.Controls.Add(BuildSummaryCard("30-Day Quality Score", $"{qualityPercent:0.0}%", $"{customerQualityFailures} customer failures against shipped jobs"), 1, 0);

        var details = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        details.Columns.Add("job", "Job");
        details.Columns.Add("shipDate", "Promised vs Actual Ship");
        details.Columns.Add("onTime", "On Time");
        details.Columns.Add("quality", "Quality Result");

        foreach (var shipment in shipments.OrderByDescending(x => x.ActualShipUtc))
        {
            var hasCustomerFailure = qualityEvents.Any(x => x.IsCustomerEscape && string.Equals(x.JobNumber, shipment.JobNumber, StringComparison.OrdinalIgnoreCase));
            details.Rows.Add(
                shipment.JobNumber,
                $"{shipment.PromisedShipUtc:yyyy-MM-dd HH:mm} / {shipment.ActualShipUtc:yyyy-MM-dd HH:mm}",
                shipment.IsOnTime ? "Yes" : "No",
                hasCustomerFailure ? "Bad quality score" : "Pass");
        }

        root.Controls.Add(details, 0, 1);
        root.SetColumnSpan(details, 2);

        Controls.Add(root);
    }

    private static Control BuildSummaryCard(string title, string value, string subtitle)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), BorderStyle = BorderStyle.FixedSingle };
        var titleLabel = new Label { Text = title, Dock = DockStyle.Top, AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
        var valueLabel = new Label { Text = value, Dock = DockStyle.Top, AutoSize = true, Font = new Font(SystemFonts.DefaultFont.FontFamily, 24, FontStyle.Bold), ForeColor = Color.DarkGreen };
        var subtitleLabel = new Label { Text = subtitle, Dock = DockStyle.Top, AutoSize = true, ForeColor = Color.DimGray };

        panel.Controls.Add(subtitleLabel);
        panel.Controls.Add(valueLabel);
        panel.Controls.Add(titleLabel);
        return panel;
    }
}
