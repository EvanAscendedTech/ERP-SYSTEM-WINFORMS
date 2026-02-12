namespace ERPSystem.WinForms.Controls;

public class ProductionControl : UserControl
{
    public ProductionControl()
    {
        Dock = DockStyle.Fill;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

        var heading = new Label
        {
            Text = "Production Planning - machine utilization and in-process job progress",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        };

        var machineGroup = new GroupBox { Text = "Machine utilization calendar (daily blocks)", Dock = DockStyle.Fill };
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
        scheduleGrid.Columns.Add("utilization", "Utilization %");
        scheduleGrid.Columns.Add("dayBlock", "Today's Scheduled Blocks");
        scheduleGrid.Rows.Add("Haas VF-2 - CNC Mill", "75%", "07:00-10:00 JOB-1452 | 13:00-16:00 JOB-1466");
        scheduleGrid.Rows.Add("Mazak QT-200 - CNC Lathe", "50%", "09:00-13:00 JOB-1454");
        scheduleGrid.Rows.Add("Wire EDM 01", "87%", "06:30-11:30 JOB-1458 | 12:00-14:00 JOB-1462");
        scheduleGrid.Rows.Add("CMM Station A", "62%", "08:00-12:00 JOB-1461 | 14:00-15:00 JOB-1466");
        scheduleGrid.Rows.Add("Deburr / Secondary Cell", "45%", "10:00-12:00 JOB-1452 | 13:00-14:00 JOB-1458");
        machineGroup.Controls.Add(scheduleGrid);

        var productionJobsGroup = new GroupBox { Text = "Jobs currently in production", Dock = DockStyle.Fill };
        var jobsPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, Padding = new Padding(8) };
        jobsPanel.RowStyles.Clear();
        AddProductionProgress(jobsPanel, "JOB-1452", "Valve body", 4, 5, true);
        AddProductionProgress(jobsPanel, "JOB-1454", "Drive shaft", 2, 4, false);
        AddProductionProgress(jobsPanel, "JOB-1458", "Heat sink", 3, 5, true);
        AddProductionProgress(jobsPanel, "JOB-1461", "Housing plate", 1, 4, false);
        productionJobsGroup.Controls.Add(jobsPanel);

        var inspectionGroup = new GroupBox { Text = "Inspection packet handoff", Dock = DockStyle.Fill };
        inspectionGroup.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "Line items can move to Inspection when each selected flow stage is complete. Inspection supports upload/download of forms and line-item packet data."
        });

        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(machineGroup, 0, 1);
        root.Controls.Add(productionJobsGroup, 0, 2);
        root.Controls.Add(inspectionGroup, 0, 2);

        var lowerPanel = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 740 };
        lowerPanel.Panel1.Controls.Add(productionJobsGroup);
        lowerPanel.Panel2.Controls.Add(inspectionGroup);
        root.Controls.Remove(productionJobsGroup);
        root.Controls.Remove(inspectionGroup);
        root.Controls.Add(lowerPanel, 0, 2);

        Controls.Add(root);
    }

    private static void AddProductionProgress(TableLayoutPanel jobsPanel, string jobNumber, string partName, int completedStages, int totalStages, bool includePostProcessing)
    {
        var card = new GroupBox { Dock = DockStyle.Top, Height = 96, Text = $"{jobNumber} - {partName}" };

        var stageLabel = includePostProcessing
            ? "Ordered Material / Production Started / Production Finished / Post Processing Started / Post Processing Finished"
            : "Ordered Material / Production Started / Production Finished / Post Processing (N/A) / Post Processing Finished (N/A)";

        var progress = new ProgressBar
        {
            Dock = DockStyle.Top,
            Minimum = 0,
            Maximum = totalStages,
            Value = Math.Min(completedStages, totalStages),
            Height = 24,
            Style = ProgressBarStyle.Continuous
        };

        var status = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Text = $"{completedStages}/{totalStages} stages complete ({(completedStages * 100) / totalStages}%)"
        };

        var stages = new Label { Dock = DockStyle.Top, AutoSize = true, Text = stageLabel };

        card.Controls.Add(stages);
        card.Controls.Add(status);
        card.Controls.Add(progress);

        jobsPanel.Controls.Add(card);
    }
}
