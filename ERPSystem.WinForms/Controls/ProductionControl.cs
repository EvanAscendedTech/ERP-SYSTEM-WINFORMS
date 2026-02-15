using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Controls;

public class ProductionControl : UserControl, IRealtimeDataControl
{
    private readonly ProductionRepository _productionRepository;
    private readonly JobFlowService _flowService;
    private readonly Action<string> _openSection;
    private readonly bool _isAdmin;
    private readonly bool _canEdit;

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly Label _feedback = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };

    private readonly DataGridView _jobsGrid = new() { Dock = DockStyle.Top, Height = 200, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly ListBox _unassignedJobsList = new() { Dock = DockStyle.Fill };
    private readonly ListBox _machinesList = new() { Dock = DockStyle.Fill, AllowDrop = true };
    private readonly Panel _scheduleCanvas = new() { Dock = DockStyle.Fill, BackColor = Color.WhiteSmoke };
    private readonly SplitContainer _workflowSplit = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 320 };

    private readonly DataGridView _machinesGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly TextBox _machineIdInput = new() { Width = 180 };
    private readonly TextBox _machineDescriptionInput = new() { Width = 260 };
    private readonly NumericUpDown _machineCapacityInput = new() { Width = 120, Minimum = 0, Maximum = 24, Value = 8 };
    private readonly Button _editMachinesButton = new() { Text = "Edit", AutoSize = true, BackColor = Color.FromArgb(0, 120, 215), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    private readonly Button _addMachineButton = new() { Text = "Add Machine", AutoSize = true, Enabled = false };

    private bool _machineEditMode;
    private string? _selectedMachineCode;
    private List<MachineSchedule> _selectedMachineSchedules = new();

    public ProductionControl(ProductionRepository productionRepository, JobFlowService flowService, Models.UserAccount currentUser, Action<string> openSection, bool canEdit)
    {
        _productionRepository = productionRepository;
        _flowService = flowService;
        _openSection = openSection;
        _canEdit = canEdit;
        _isAdmin = currentUser.Roles.Any(r => string.Equals(r.Name, "Admin", StringComparison.OrdinalIgnoreCase)
                                           || string.Equals(r.Name, "Administrator", StringComparison.OrdinalIgnoreCase));
        Dock = DockStyle.Fill;

        BuildWorkflowTab();
        BuildMachinesTab();

        Controls.Add(_tabs);
        Controls.Add(_feedback);

        _ = LoadJobsAsync();
        _ = LoadMachinesAsync();
    }

    private void BuildWorkflowTab()
    {
        var workflowTab = new TabPage("Workflow");

        ConfigureGrid();

        var actionsPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
        var refreshButton = new Button { Text = "Refresh Jobs", AutoSize = true };
        var startButton = new Button { Text = "Start Production", AutoSize = true, Enabled = _canEdit };
        var completeButton = new Button { Text = "Complete Production", AutoSize = true, Enabled = _canEdit };
        var qualityButton = new Button { Text = "Move to Quality", AutoSize = true, Enabled = _canEdit };
        var openDetailsButton = new Button { Text = "Open Production View", AutoSize = true };
        var advanceButton = new Button { Text = "Admin: Push Forward", AutoSize = true, Visible = _isAdmin };
        var rewindButton = new Button { Text = "Admin: Push Backward", AutoSize = true, Visible = _isAdmin };

        refreshButton.Click += async (_, _) => await RefreshDataAsync(false);
        startButton.Click += async (_, _) => await StartSelectedJobAsync();
        completeButton.Click += async (_, _) => await CompleteSelectedJobAsync();
        qualityButton.Click += async (_, _) => await MoveSelectedToQualityAsync();
        openDetailsButton.Click += (_, _) => OpenSelectedProductionWindow();
        advanceButton.Click += async (_, _) => await AdminMoveSelectedAsync(forward: true);
        rewindButton.Click += async (_, _) => await AdminMoveSelectedAsync(forward: false);

        actionsPanel.Controls.Add(refreshButton);
        actionsPanel.Controls.Add(startButton);
        actionsPanel.Controls.Add(completeButton);
        actionsPanel.Controls.Add(qualityButton);
        actionsPanel.Controls.Add(openDetailsButton);
        actionsPanel.Controls.Add(advanceButton);
        actionsPanel.Controls.Add(rewindButton);

        var topSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 470 };
        var jobsPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var machinesPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        jobsPanel.Controls.Add(_unassignedJobsList);
        jobsPanel.Controls.Add(new Label { Text = "Unassigned Jobs", Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI", 10F, FontStyle.Bold) });

        machinesPanel.Controls.Add(_machinesList);
        machinesPanel.Controls.Add(new Label { Text = "Machines (double-click to view schedule)", Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI", 10F, FontStyle.Bold) });

        topSplit.Panel1.Controls.Add(jobsPanel);
        topSplit.Panel2.Controls.Add(machinesPanel);

        _workflowSplit.Panel1.Controls.Add(topSplit);
        _workflowSplit.Panel1.Controls.Add(_jobsGrid);
        _workflowSplit.Panel1.Controls.Add(actionsPanel);
        _workflowSplit.Panel2.Controls.Add(_scheduleCanvas);
        _workflowSplit.Panel2.Controls.Add(new Label { Text = "Machine Schedule", Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI", 10F, FontStyle.Bold) });

        _scheduleCanvas.Paint += ScheduleCanvasOnPaint;
        _workflowSplit.Panel2Collapsed = true;

        _unassignedJobsList.MouseDown += UnassignedJobsListOnMouseDown;
        _machinesList.DoubleClick += async (_, _) => await OpenSelectedMachineScheduleAsync();
        _machinesList.DragEnter += MachinesListOnDragEnter;
        _machinesList.DragDrop += async (_, e) => await MachinesListOnDragDropAsync(e);

        workflowTab.Controls.Add(_workflowSplit);
        _tabs.TabPages.Add(workflowTab);
    }

    private void BuildMachinesTab()
    {
        var machinesTab = new TabPage("Machines");
        _machinesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Machine ID", DataPropertyName = nameof(Machine.MachineCode), Width = 180 });
        _machinesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Machine Description", DataPropertyName = nameof(Machine.Description), Width = 300 });
        _machinesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Daily Capacity (h)", DataPropertyName = nameof(Machine.DailyCapacityHours), Width = 140 });

        var editorPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8), WrapContents = false };
        editorPanel.Controls.Add(new Label { Text = "Machine ID", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        editorPanel.Controls.Add(_machineIdInput);
        editorPanel.Controls.Add(new Label { Text = "Description", AutoSize = true, Margin = new Padding(12, 8, 6, 0) });
        editorPanel.Controls.Add(_machineDescriptionInput);
        editorPanel.Controls.Add(new Label { Text = "Daily Capacity", AutoSize = true, Margin = new Padding(12, 8, 6, 0) });
        editorPanel.Controls.Add(_machineCapacityInput);
        editorPanel.Controls.Add(_addMachineButton);

        _machineIdInput.Enabled = false;
        _machineDescriptionInput.Enabled = false;
        _machineCapacityInput.Enabled = false;

        _editMachinesButton.Enabled = _canEdit;
        _editMachinesButton.Click += (_, _) => ToggleMachineEditMode();
        _addMachineButton.Click += async (_, _) => await AddMachineAsync();

        var actionsPanel = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(8) };
        _editMachinesButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        _editMachinesButton.Location = new Point(actionsPanel.Width - 100, 10);
        actionsPanel.Resize += (_, _) => _editMachinesButton.Location = new Point(actionsPanel.Width - _editMachinesButton.Width - 8, 10);
        actionsPanel.Controls.Add(_editMachinesButton);

        machinesTab.Controls.Add(_machinesGrid);
        machinesTab.Controls.Add(editorPanel);
        machinesTab.Controls.Add(actionsPanel);

        _tabs.TabPages.Add(machinesTab);
    }

    private void ConfigureGrid()
    {
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "JobNumber", HeaderText = "Job #", DataPropertyName = nameof(ProductionJob.JobNumber), Width = 120 });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Product", HeaderText = "Product", DataPropertyName = nameof(ProductionJob.ProductName), Width = 220 });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Production Status", DataPropertyName = nameof(ProductionJob.Status), Width = 140 });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Planned", HeaderText = "Planned Qty", DataPropertyName = nameof(ProductionJob.PlannedQuantity), Width = 110 });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Duration", HeaderText = "Duration (h)", DataPropertyName = nameof(ProductionJob.EstimatedDurationHours), Width = 110 });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DueDate", HeaderText = "Due (UTC)", DataPropertyName = nameof(ProductionJob.DueDateUtc), Width = 180 });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "QuoteLifecycleId", HeaderText = "Quote Lifecycle #", DataPropertyName = nameof(ProductionJob.QuoteLifecycleId), Width = 150 });
        _jobsGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "QualityApproved", HeaderText = "Quality Approved", Width = 120 });

        _jobsGrid.CellFormatting += (_, e) =>
        {
            if (_jobsGrid.Columns[e.ColumnIndex].Name == "QualityApproved"
                && _jobsGrid.Rows[e.RowIndex].DataBoundItem is ProductionJob job)
            {
                e.Value = _flowService.IsQualityApproved(job.JobNumber);
                e.FormattingApplied = true;
            }
        };
    }

    private async Task LoadJobsAsync()
    {
        var jobs = await _productionRepository.GetJobsAsync();
        var queuedJobs = jobs.Where(x => _flowService.IsInModule(x.JobNumber, JobFlowService.WorkflowModule.Production))
            .OrderBy(x => x.DueDateUtc)
            .ToList();

        _jobsGrid.DataSource = queuedJobs;

        var machines = await _productionRepository.GetMachinesAsync();
        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var machine in machines)
        {
            var schedules = await _productionRepository.GetMachineSchedulesAsync(machine.MachineCode);
            foreach (var schedule in schedules)
            {
                assigned.Add(schedule.AssignedJobNumber);
            }
        }

        var unassigned = queuedJobs.Where(job => !assigned.Contains(job.JobNumber)).ToList();
        _unassignedJobsList.DataSource = unassigned;
        _unassignedJobsList.DisplayMember = nameof(ProductionJob.JobNumber);

        _feedback.Text = $"Loaded {queuedJobs.Count} production jobs.";
    }

    private async Task LoadMachinesAsync()
    {
        var machines = await _productionRepository.GetMachinesAsync();
        _machinesGrid.DataSource = machines;
        _machinesList.DataSource = machines;
        _machinesList.DisplayMember = nameof(Machine.MachineCode);
    }

    private void ToggleMachineEditMode()
    {
        _machineEditMode = !_machineEditMode;
        _machineIdInput.Enabled = _machineEditMode;
        _machineDescriptionInput.Enabled = _machineEditMode;
        _machineCapacityInput.Enabled = _machineEditMode;
        _addMachineButton.Enabled = _machineEditMode;
        _editMachinesButton.Text = _machineEditMode ? "Done" : "Edit";
    }

    private async Task AddMachineAsync()
    {
        var machineId = _machineIdInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(machineId))
        {
            _feedback.Text = "Machine ID is required.";
            return;
        }

        await _productionRepository.SaveMachineAsync(new Machine
        {
            MachineCode = machineId,
            Description = _machineDescriptionInput.Text.Trim(),
            DailyCapacityHours = (int)_machineCapacityInput.Value
        });

        _machineIdInput.Clear();
        _machineDescriptionInput.Clear();
        _machineCapacityInput.Value = 8;
        _feedback.Text = $"Saved machine {machineId}.";
        await LoadMachinesAsync();
    }

    private void UnassignedJobsListOnMouseDown(object? sender, MouseEventArgs e)
    {
        var index = _unassignedJobsList.IndexFromPoint(e.Location);
        if (index < 0 || _unassignedJobsList.Items[index] is not ProductionJob job)
        {
            return;
        }

        _unassignedJobsList.SelectedIndex = index;
        _unassignedJobsList.DoDragDrop(job, DragDropEffects.Move);
    }

    private void MachinesListOnDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(typeof(ProductionJob)) == true ? DragDropEffects.Move : DragDropEffects.None;
    }

    private async Task MachinesListOnDragDropAsync(DragEventArgs e)
    {
        if (e.Data?.GetData(typeof(ProductionJob)) is not ProductionJob job)
        {
            return;
        }

        var point = _machinesList.PointToClient(new Point(e.X, e.Y));
        var index = _machinesList.IndexFromPoint(point);
        if (index < 0 || _machinesList.Items[index] is not Machine machine)
        {
            _feedback.Text = "Drop the job on a machine.";
            return;
        }

        var result = await _productionRepository.AssignJobToMachineAsync(job.JobNumber, machine.MachineCode, job.EstimatedDurationHours);
        _feedback.Text = result.Message;

        if (result.Success)
        {
            await LoadJobsAsync();
            _selectedMachineCode = machine.MachineCode;
            _selectedMachineSchedules = (await _productionRepository.GetMachineSchedulesAsync(machine.MachineCode)).ToList();
            _workflowSplit.Panel2Collapsed = false;
            _workflowSplit.SplitterDistance = Math.Max(260, Height / 2);
            _scheduleCanvas.Invalidate();
        }
    }

    private async Task OpenSelectedMachineScheduleAsync()
    {
        if (_machinesList.SelectedItem is not Machine machine)
        {
            _feedback.Text = "Select a machine first.";
            return;
        }

        _selectedMachineCode = machine.MachineCode;
        _selectedMachineSchedules = (await _productionRepository.GetMachineSchedulesAsync(machine.MachineCode)).ToList();
        _workflowSplit.Panel2Collapsed = false;
        _workflowSplit.SplitterDistance = Math.Max(260, Height / 2);
        _scheduleCanvas.Invalidate();
        _feedback.Text = $"Loaded schedule for machine {machine.MachineCode}.";
    }

    private void ScheduleCanvasOnPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.WhiteSmoke);

        if (string.IsNullOrWhiteSpace(_selectedMachineCode))
        {
            g.DrawString("Double-click a machine to view its schedule.", Font, Brushes.DimGray, 12, 12);
            return;
        }

        var slots = _selectedMachineSchedules.Where(x => !x.IsMaintenanceWindow).OrderBy(x => x.ShiftStartUtc).ToList();
        if (slots.Count == 0)
        {
            g.DrawString($"{_selectedMachineCode}: no scheduled jobs.", Font, Brushes.DimGray, 12, 12);
            return;
        }

        var minDay = slots.Min(x => x.ShiftStartUtc).Date;
        var maxDay = slots.Max(x => x.ShiftEndUtc).Date.AddDays(1);
        var totalDays = Math.Max(1, (maxDay - minDay).Days);

        var left = 120;
        var top = 30;
        var width = Math.Max(200, _scheduleCanvas.ClientSize.Width - left - 20);
        var height = Math.Max(80, _scheduleCanvas.ClientSize.Height - top - 20);
        var dayWidth = width / (float)totalDays;

        for (var i = 0; i <= totalDays; i++)
        {
            var x = left + (i * dayWidth);
            g.DrawLine(Pens.LightGray, x, top, x, top + height);
            if (i < totalDays)
            {
                var day = minDay.AddDays(i);
                g.DrawString(day.ToString("MM-dd"), new Font(Font, FontStyle.Bold), Brushes.DimGray, x + 2, 8);
            }
        }

        g.DrawRectangle(Pens.Gray, left, top, width, height);

        var grouped = slots.GroupBy(x => x.AssignedJobNumber).OrderBy(x => x.Min(y => y.ShiftStartUtc)).ToList();
        var rowHeight = Math.Max(20, height / Math.Max(1, grouped.Count));

        for (var row = 0; row < grouped.Count; row++)
        {
            var color = Color.FromArgb(80 + (row * 20 % 130), 100 + (row * 30 % 120), 180);
            using var brush = new SolidBrush(color);
            var y = top + row * rowHeight + 2;
            g.DrawString(grouped[row].Key, Font, Brushes.Black, 8, y);

            foreach (var slot in grouped[row])
            {
                var startX = left + (float)(slot.ShiftStartUtc - minDay).TotalDays * dayWidth;
                var endX = left + (float)(slot.ShiftEndUtc - minDay).TotalDays * dayWidth;
                var rect = new RectangleF(startX, y, Math.Max(4, endX - startX), rowHeight - 6);
                g.FillRectangle(brush, rect);
                g.DrawRectangle(Pens.Black, rect.X, rect.Y, rect.Width, rect.Height);
            }
        }
    }

    private async Task StartSelectedJobAsync()
    {
        if (_jobsGrid.CurrentRow?.DataBoundItem is not ProductionJob selected)
        {
            _feedback.Text = "Select a job first.";
            return;
        }

        var result = await _productionRepository.StartJobAsync(selected.JobNumber, QuoteStatus.Won, selected.SourceQuoteId ?? 0, "system.user");
        _feedback.Text = result.Message;
        await LoadJobsAsync();
    }

    private async Task CompleteSelectedJobAsync()
    {
        if (_jobsGrid.CurrentRow?.DataBoundItem is not ProductionJob selected)
        {
            _feedback.Text = "Select a job first.";
            return;
        }

        var result = await _productionRepository.CompleteJobAsync(selected.JobNumber, "system.user");
        _feedback.Text = result.Message;
        await LoadJobsAsync();
    }

    private async Task MoveSelectedToQualityAsync()
    {
        if (_jobsGrid.CurrentRow?.DataBoundItem is not ProductionJob selected)
        {
            _feedback.Text = "Select a job first.";
            return;
        }

        if (selected.Status != ProductionJobStatus.Completed)
        {
            _feedback.Text = $"Job {selected.JobNumber} must be completed before Quality.";
            return;
        }

        _feedback.Text = $"Job {selected.JobNumber} is ready for Quality.";
        await LoadJobsAsync();
        _openSection("Quality");
    }

    private void OpenSelectedProductionWindow()
    {
        if (_jobsGrid.CurrentRow?.DataBoundItem is not ProductionJob selected)
        {
            _feedback.Text = "Select a job first.";
            return;
        }

        using var window = new Form
        {
            Text = $"Production View - {selected.JobNumber}",
            Width = 780,
            Height = 520,
            StartPosition = FormStartPosition.CenterParent
        };

        var details = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            Text = $"Production-only details\n\nJob: {selected.JobNumber}\nQuote Lifecycle: {selected.QuoteLifecycleId}\nDue Date: {selected.DueDateUtc:u}\nStatus: {selected.Status}\n\nInternal flow and technical BLOB data are accessible from this production view.",
            Font = new Font("Segoe UI", 10F)
        };

        window.Controls.Add(details);
        window.ShowDialog(this);
    }

    private async Task AdminMoveSelectedAsync(bool forward)
    {
        if (_jobsGrid.CurrentRow?.DataBoundItem is not ProductionJob selected)
        {
            _feedback.Text = "Select a job first.";
            return;
        }

        string message;
        var moved = forward
            ? _flowService.TryAdvanceModule(selected, out message)
            : _flowService.TryRewindModule(selected, out message);

        _feedback.Text = message;
        await LoadJobsAsync();

        if (!moved)
        {
            return;
        }

        var currentModule = _flowService.GetCurrentModule(selected.JobNumber);
        if (currentModule != JobFlowService.WorkflowModule.Production)
        {
            _openSection(currentModule.ToString());
        }
    }

    public async Task<bool> OpenFromDashboardAsync(string jobNumber, bool openDetails)
    {
        await LoadJobsAsync();

        var selected = SelectJobRow(jobNumber);
        if (!selected)
        {
            _feedback.Text = $"Job {jobNumber} is not currently in the Production queue.";
            return false;
        }

        if (openDetails)
        {
            OpenSelectedProductionWindow();
        }

        return true;
    }

    private bool SelectJobRow(string jobNumber)
    {
        foreach (DataGridViewRow row in _jobsGrid.Rows)
        {
            if (row.DataBoundItem is ProductionJob job && string.Equals(job.JobNumber, jobNumber, StringComparison.OrdinalIgnoreCase))
            {
                row.Selected = true;
                _jobsGrid.CurrentCell = row.Cells[0];
                _jobsGrid.FirstDisplayedScrollingRowIndex = row.Index;
                return true;
            }
        }

        return false;
    }

    public async Task RefreshDataAsync(bool fromFailSafeCheckpoint)
    {
        await LoadMachinesAsync();
        await LoadJobsAsync();
    }
}
