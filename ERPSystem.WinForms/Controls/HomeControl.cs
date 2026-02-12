using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Controls;

public class HomeControl : UserControl
{
    private readonly Label _companyNameLabel;

    public HomeControl(string companyName, Action<string> openSection)
    {
        Dock = DockStyle.Fill;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), RowCount = 5 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        _companyNameLabel = new Label
        {
            Text = string.IsNullOrWhiteSpace(companyName) ? "Company" : companyName,
            Dock = DockStyle.Top,
            Font = new Font(Font.FontFamily, 18, FontStyle.Bold),
            Height = 42
        };

        var onTimeGraph = BuildMetricGraph("On-time delivery (30 day)", [95, 92, 97, 93]);
        var qualityGraph = BuildMetricGraph("Quality metric (30 day)", [98, 99, 97, 100]);

        root.Controls.Add(_companyNameLabel, 0, 0);
        root.SetColumnSpan(_companyNameLabel, 2);
        root.Controls.Add(onTimeGraph, 0, 1);
        root.Controls.Add(qualityGraph, 1, 1);

        var sections = new[] { "Quotes", "Production", "Inspection", "Shipping", "Quality", "Performance" };
        for (var index = 0; index < sections.Length; index++)
        {
            root.Controls.Add(BuildWorkflowList(sections[index], openSection), index % 2, (index / 2) + 2);
        }

        Controls.Add(root);
    }

    public void UpdateCompanyName(string companyName)
    {
        _companyNameLabel.Text = string.IsNullOrWhiteSpace(companyName) ? "Company" : companyName;
    }

    private static Control BuildWorkflowList(string sectionName, Action<string> openSection)
    {
        var group = new GroupBox { Text = sectionName, Dock = DockStyle.Fill, Padding = new Padding(8) };
        var list = new ListBox { Dock = DockStyle.Fill };
        list.Items.Add($"Open {sectionName} items");
        list.Items.Add($"Queued {sectionName} items");
        list.Items.Add($"Recent {sectionName} activity");
        list.DoubleClick += (_, _) =>
        {
            if (list.SelectedItem is not null)
            {
                openSection(sectionName);
            }
        };
        group.Controls.Add(list);
        return group;
    }

    private static Control BuildMetricGraph(string title, IReadOnlyList<int> values)
    {
        var group = new GroupBox { Text = title, Dock = DockStyle.Fill };
        var chart = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        foreach (var value in values)
        {
            var bar = new Panel { Width = 40, Height = Math.Max(10, value), BackColor = Color.SeaGreen, Margin = new Padding(8, 100 - Math.Min(value, 100), 8, 0) };
            chart.Controls.Add(bar);
        }

        group.Controls.Add(chart);
        return group;
    }
}
