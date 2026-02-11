namespace ERPSystem.WinForms.Controls;

public class ArchiveControl : UserControl
{
    public ArchiveControl()
    {
        Dock = DockStyle.Fill;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new Label
        {
            Text = "Completed / Lost Quotes and Jobs",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        };

        var archiveList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true
        };
        archiveList.Columns.Add("Type", 140);
        archiveList.Columns.Add("Identifier", 180);
        archiveList.Columns.Add("Status", 180);
        archiveList.Columns.Add("Closed On", 160);

        archiveList.Items.Add(new ListViewItem(["Quote", "Q-10021", "Lost", DateTime.Today.AddDays(-5).ToShortDateString()]));
        archiveList.Items.Add(new ListViewItem(["Quote", "Q-10017", "Won", DateTime.Today.AddDays(-13).ToShortDateString()]));
        archiveList.Items.Add(new ListViewItem(["Job", "JOB-1440", "Completed", DateTime.Today.AddDays(-2).ToShortDateString()]));

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(archiveList, 0, 1);
        Controls.Add(root);
    }
}
