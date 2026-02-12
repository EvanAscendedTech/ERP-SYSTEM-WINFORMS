using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Controls;

public class QualityControl : UserControl
{
    public QualityControl()
    {
        Dock = DockStyle.Fill;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var note = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = "Quality events tracked per job. Customer-escape failures impact performance quality score; internal catches are informational only."
        };

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        grid.Columns.Add("job", "Job");
        grid.Columns.Add("date", "Recorded (UTC)");
        grid.Columns.Add("type", "Failure Type");
        grid.Columns.Add("detail", "Description");

        foreach (var metric in OperationalMetrics.GetRecentQualityEvents().OrderByDescending(x => x.RecordedUtc))
        {
            var type = metric.IsCustomerEscape ? "Customer Escape" : "Internal (caught in production)";
            grid.Rows.Add(metric.JobNumber, metric.RecordedUtc.ToString("yyyy-MM-dd HH:mm"), type, metric.FailureDescription);
        }

        root.Controls.Add(note, 0, 0);
        root.Controls.Add(grid, 0, 1);

        Controls.Add(root);
    }
}
