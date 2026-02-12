using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Controls;

public class ShippingControl : UserControl
{
    public ShippingControl()
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

        var label = new Label
        {
            Text = "On-time delivery is tracked by promised ship date/time vs actual ship date/time.",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        };

        var shipments = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        shipments.Columns.Add("job", "Job");
        shipments.Columns.Add("promised", "Promised Ship (UTC)");
        shipments.Columns.Add("actual", "Actual Ship (UTC)");
        shipments.Columns.Add("status", "On Time");

        foreach (var shipment in OperationalMetrics.GetRecentShipments().OrderByDescending(x => x.ActualShipUtc))
        {
            shipments.Rows.Add(
                shipment.JobNumber,
                shipment.PromisedShipUtc.ToString("yyyy-MM-dd HH:mm"),
                shipment.ActualShipUtc.ToString("yyyy-MM-dd HH:mm"),
                shipment.IsOnTime ? "Yes" : "No");
        }

        root.Controls.Add(label, 0, 0);
        root.Controls.Add(shipments, 0, 1);

        Controls.Add(root);
    }
}
