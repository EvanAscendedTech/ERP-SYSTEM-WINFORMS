namespace ERPSystem.WinForms.Controls;

public class ProductionControl : UserControl
{
    public ProductionControl()
    {
        Dock = DockStyle.Fill;

        var root = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 300,
            Padding = new Padding(8)
        };

        var machinesGroup = new GroupBox { Text = "Machines", Dock = DockStyle.Fill };
        var machinesList = new ListBox { Dock = DockStyle.Fill };
        machinesList.Items.AddRange([
            "Haas VF-2 - CNC Mill",
            "Mazak QT-200 - CNC Lathe",
            "Wire EDM 01",
            "CMM Station A",
            "Deburr / Secondary Cell"
        ]);
        machinesGroup.Controls.Add(machinesList);

        var scheduleGroup = new GroupBox { Text = "Schedule (Placeholder)", Dock = DockStyle.Fill };
        var scheduleGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        scheduleGrid.Columns.Add("machine", "Machine");
        scheduleGrid.Columns.Add("job", "Job");
        scheduleGrid.Columns.Add("start", "Start");
        scheduleGrid.Columns.Add("end", "End");
        scheduleGrid.Rows.Add("Haas VF-2", "JOB-1452", "07:00", "10:00");
        scheduleGrid.Rows.Add("Mazak QT-200", "JOB-1454", "10:00", "13:00");
        scheduleGrid.Rows.Add("Wire EDM 01", "JOB-1458", "13:30", "16:30");
        scheduleGroup.Controls.Add(scheduleGrid);

        root.Panel1.Controls.Add(machinesGroup);
        root.Panel2.Controls.Add(scheduleGroup);

        Controls.Add(root);
    }
}
