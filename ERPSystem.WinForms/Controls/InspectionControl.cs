namespace ERPSystem.WinForms.Controls;

public class InspectionControl : UserControl
{
    public InspectionControl()
    {
        Dock = DockStyle.Fill;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "Inspection Module",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        };

        var as9102Placeholder = new GroupBox { Text = "AS9102 Section (Placeholder)", Dock = DockStyle.Fill };
        as9102Placeholder.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "FAI package checklist and ballooned drawing workflow will be implemented here."
        });

        var qaSignoff = new CheckBox
        {
            Text = "QA Signoff",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(as9102Placeholder, 0, 1);
        root.Controls.Add(qaSignoff, 0, 2);

        Controls.Add(root);
    }
}
