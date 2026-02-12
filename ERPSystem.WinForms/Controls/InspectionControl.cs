namespace ERPSystem.WinForms.Controls;

public class InspectionControl : UserControl
{
    public InspectionControl()
    {
        Dock = DockStyle.Fill;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(12)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "Inspection Module",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        };

        var packetGrid = new GroupBox { Text = "Production / Inspection Packet by Line Item", Dock = DockStyle.Fill };
        var lineItemGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        lineItemGrid.Columns.Add("job", "Job");
        lineItemGrid.Columns.Add("line", "Line Item");
        lineItemGrid.Columns.Add("ready", "Ready For Inspection");
        lineItemGrid.Columns.Add("packet", "Packet Status");
        lineItemGrid.Rows.Add("JOB-1452", "Line 1", "Yes", "Awaiting final checklist");
        lineItemGrid.Rows.Add("JOB-1458", "Line 2", "Yes", "Inspection form uploaded");
        lineItemGrid.Rows.Add("JOB-1461", "Line 1", "No", "Still in production flow");
        packetGrid.Controls.Add(lineItemGrid);

        var formsPanel = new GroupBox { Text = "Inspection Forms & Files", Dock = DockStyle.Fill };
        var formsLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(8), WrapContents = false };
        formsLayout.Controls.Add(new Button { Text = "Upload Inspection Form", Width = 200, Height = 34 });
        formsLayout.Controls.Add(new Button { Text = "Download Inspection Packet", Width = 200, Height = 34 });
        formsLayout.Controls.Add(new Button { Text = "Upload Supporting Data", Width = 200, Height = 34 });
        formsLayout.Controls.Add(new Label { AutoSize = true, Text = "Attach certs, measurements, and line-item notes for complete traceability.", MaximumSize = new Size(220, 0) });
        formsPanel.Controls.Add(formsLayout);

        var flowRule = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "Only line items that complete all enabled production stages (including post-process when selected on quote) can be moved into inspection."
        };

        var qaSignoff = new CheckBox
        {
            Text = "QA Signoff",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };

        root.Controls.Add(title, 0, 0);
        root.SetColumnSpan(title, 2);
        root.Controls.Add(packetGrid, 0, 1);
        root.Controls.Add(formsPanel, 1, 1);
        root.Controls.Add(flowRule, 0, 2);
        root.Controls.Add(qaSignoff, 1, 2);

        Controls.Add(root);
    }
}
