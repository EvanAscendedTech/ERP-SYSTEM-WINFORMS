namespace ERPSystem.WinForms.Controls;

public class HomeControl : UserControl
{
    private readonly Label _companyNameLabel;

    public HomeControl(string companyName)
    {
        Dock = DockStyle.Fill;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        _companyNameLabel = new Label
        {
            Text = string.IsNullOrWhiteSpace(companyName) ? "Company" : companyName,
            Dock = DockStyle.Top,
            Font = new Font(Font.FontFamily, 18, FontStyle.Bold),
            Height = 42
        };

        var sections = new[] { "Quotes", "Production", "Inspection", "Shipping" };
        for (var index = 0; index < sections.Length; index++)
        {
            root.Controls.Add(BuildWorkflowList(sections[index]), index % 2, (index / 2) + 1);
        }

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        Controls.Add(root);
        Controls.Add(_companyNameLabel);
    }

    public void UpdateCompanyName(string companyName)
    {
        _companyNameLabel.Text = string.IsNullOrWhiteSpace(companyName) ? "Company" : companyName;
    }

    private static Control BuildWorkflowList(string sectionName)
    {
        var group = new GroupBox { Text = sectionName, Dock = DockStyle.Fill, Padding = new Padding(8) };
        var list = new ListBox { Dock = DockStyle.Fill };
        list.Items.Add($"{sectionName} queue");
        list.Items.Add($"{sectionName} waiting actions");
        list.Items.Add($"{sectionName} completed recently");
        group.Controls.Add(list);
        return group;
    }
}
