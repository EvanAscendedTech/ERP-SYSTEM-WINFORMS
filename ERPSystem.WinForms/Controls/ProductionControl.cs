using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Controls;

public class ProductionControl : UserControl, IRealtimeDataControl
{
    private readonly ProductionRepository _productionRepository;
    private readonly JobFlowService _flowService;
    private readonly UserManagementRepository _userRepository;
    private readonly UserAccount _currentUser;
    private readonly Action<string> _openSection;
    private readonly bool _isAdmin;
    private readonly bool _canEdit;
    private readonly bool _isProductionEmployee;
    private readonly bool _isProductionManager;

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly Label _feedback = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };

    private readonly DataGridView _jobsGrid = new() { Dock = DockStyle.Top, Height = 200, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly ListBox _unassignedJobsList = new() { Dock = DockStyle.Fill };
    private readonly ListBox _machinesList = new() { Dock = DockStyle.Fill, AllowDrop = true };
    private readonly Panel _scheduleCanvas = new() { Dock = DockStyle.Fill, BackColor = Color.WhiteSmoke };
    private readonly SplitContainer _productionSplit = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 320 };
    private readonly DataGridView _archiveGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };

    private readonly DataGridView _machinesGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly Button _addMachineButton = new() { Text = "Add New Machine", AutoSize = true };
    private static readonly string[] MachineTypes =
    {
        "Mill",
        "Lathe",
        "Swiss Lathe",
        "Horizontal Machining Center",
        "Vertical Machining Center",
        "5-Axis Machining Center",
        "Boring Mill",
        "Drill Press",
        "Tapping Center",
        "Wire EDM",
        "Sinker EDM",
        "Laser Cutter",
        "Plasma Cutter",
        "Waterjet Cutter",
        "Router",
        "Surface Grinder",
        "Cylindrical Grinder",
        "Centerless Grinder",
        "Saw",
        "Broach",
        "Hob",
        "Gear Shaper",
        "Press Brake",
        "Hydraulic Press",
        "Mechanical Press",
        "Turret Punch",
        "Roll Former",
        "Deburring",
        "Welding Cell",
        "CMM",
        "Other"
    };

    private string? _selectedMachineCode;
    private List<MachineSchedule> _selectedMachineSchedules = new();

    public ProductionControl(ProductionRepository productionRepository, UserManagementRepository userRepository, JobFlowService flowService, Models.UserAccount currentUser, Action<string> openSection, bool canEdit)
    {
        _productionRepository = productionRepository;
        _flowService = flowService;
        _userRepository = userRepository;
        _currentUser = currentUser;
        _openSection = openSection;
        _canEdit = canEdit;
        _isAdmin = AuthorizationService.HasRole(currentUser, RoleCatalog.Administrator);
        _isProductionEmployee = AuthorizationService.HasRole(currentUser, RoleCatalog.ProductionEmployee);
        _isProductionManager = AuthorizationService.HasRole(currentUser, RoleCatalog.ProductionManager) || _isAdmin;
        Dock = DockStyle.Fill;

        BuildProductionTab();
        BuildMachinesTab();

        Controls.Add(_tabs);
        Controls.Add(_feedback);

        _ = LoadJobsAsync();
        _ = LoadMachinesAsync();
    }

    private void BuildProductionTab()
    {
        var productionTab = new TabPage("Production");

        ConfigureGrid();

        var actionsPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
        var refreshButton = new Button { Text = "Refresh Jobs", AutoSize = true };
        var startButton = new Button { Text = "Start Production", AutoSize = true, Enabled = _isProductionManager };
        var completeButton = new Button { Text = "Complete Production", AutoSize = true, Enabled = _isProductionManager };
        var requestInspectionButton = new Button { Text = "Request Inspection", AutoSize = true, Enabled = _isProductionEmployee && !_isProductionManager, Visible = _isProductionEmployee && !_isProductionManager };
        var openDetailsButton = new Button { Text = "Open Technical Documentation", AutoSize = true };
        var advanceButton = new Button { Text = _isAdmin ? "Admin Override: Move to Inspection" : "Done → Inspection", AutoSize = true, Enabled = _isProductionManager };
        var rewindButton = new Button { Text = _isAdmin ? "Admin Override: Return to Purchasing" : "Return to Queue", AutoSize = true, Enabled = _isProductionManager };
        var deleteButton = new Button { Text = "Admin: Delete Job", AutoSize = true, Visible = _isAdmin };
        var restoreButton = new Button { Text = "Admin: Restore from Production Archive", AutoSize = true, Visible = _isAdmin };

        refreshButton.Click += async (_, _) => await SafeUiActionAsync(() => RefreshDataAsync(false));
        startButton.Click += async (_, _) => await SafeUiActionAsync(StartSelectedJobAsync);
        completeButton.Click += async (_, _) => await SafeUiActionAsync(CompleteSelectedJobAsync);
        requestInspectionButton.Click += async (_, _) => await SafeUiActionAsync(RequestInspectionAsync);
        openDetailsButton.Click += (_, _) => OpenSelectedProductionWindow();
        advanceButton.Click += async (_, _) => await SafeUiActionAsync(() => MoveSelectedToInspectionAsync(isManagerAction: true));
        rewindButton.Click += async (_, _) => await SafeUiActionAsync(() => AdminMoveSelectedAsync(forward: false));
        deleteButton.Click += async (_, _) => await SafeUiActionAsync(DeleteSelectedJobAsync);
        restoreButton.Click += async (_, _) => await SafeUiActionAsync(RestoreSelectedArchivedJobAsync);

        actionsPanel.Controls.Add(refreshButton);
        actionsPanel.Controls.Add(startButton);
        actionsPanel.Controls.Add(completeButton);
        actionsPanel.Controls.Add(requestInspectionButton);
        actionsPanel.Controls.Add(openDetailsButton);
        actionsPanel.Controls.Add(advanceButton);
        actionsPanel.Controls.Add(rewindButton);
        actionsPanel.Controls.Add(deleteButton);
        actionsPanel.Controls.Add(restoreButton);

        var topSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 470 };
        var jobsPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var machinesPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        jobsPanel.Controls.Add(_unassignedJobsList);
        jobsPanel.Controls.Add(new Label { Text = "Unassigned Jobs", Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI", 10F, FontStyle.Bold) });

        machinesPanel.Controls.Add(_machinesList);
        machinesPanel.Controls.Add(new Label { Text = "Machines (double-click to view schedule)", Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI", 10F, FontStyle.Bold) });

        topSplit.Panel1.Controls.Add(jobsPanel);
        topSplit.Panel2.Controls.Add(machinesPanel);

        _productionSplit.Panel1.Controls.Add(topSplit);
        _productionSplit.Panel1.Controls.Add(_jobsGrid);
        _productionSplit.Panel1.Controls.Add(actionsPanel);
        _productionSplit.Panel2.Controls.Add(_scheduleCanvas);
        _productionSplit.Panel2.Controls.Add(new Label { Text = "Machine Schedule", Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI", 10F, FontStyle.Bold) });

        _scheduleCanvas.Paint += ScheduleCanvasOnPaint;
        _productionSplit.Panel2Collapsed = true;

        _unassignedJobsList.MouseDown += UnassignedJobsListOnMouseDown;
        _machinesList.DoubleClick += async (_, _) => await OpenSelectedMachineScheduleAsync();
        _machinesList.DragEnter += MachinesListOnDragEnter;
        _machinesList.DragDrop += async (_, e) => await MachinesListOnDragDropAsync(e);

        var archivePanel = new Panel { Dock = DockStyle.Bottom, Height = 190, Padding = new Padding(8) };
        ConfigureArchiveGrid();
        archivePanel.Controls.Add(_archiveGrid);
        archivePanel.Controls.Add(new Label { Text = "Production Archive (visible to all, restore is Administrator only)", Dock = DockStyle.Top, Height = 24, Font = new Font("Segoe UI", 9F, FontStyle.Bold) });

        productionTab.Controls.Add(_productionSplit);
        productionTab.Controls.Add(archivePanel);
        _tabs.TabPages.Add(productionTab);
    }

    private void BuildMachinesTab()
    {
        var machinesTab = new TabPage("Machines");
        _machinesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Machine ID", DataPropertyName = nameof(Machine.MachineCode), Width = 180, ReadOnly = true });
        _machinesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Machine Description", DataPropertyName = nameof(Machine.Description), Width = 300 });
        _machinesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Daily Capacity (h)", DataPropertyName = nameof(Machine.DailyCapacityHours), Width = 140 });
        _machinesGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            HeaderText = "Machine Type",
            DataPropertyName = nameof(Machine.MachineType),
            Width = 220,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            DataSource = MachineTypes.ToList()
        });

        _machinesGrid.ReadOnly = !_canEdit;
        _machinesGrid.CellEndEdit += async (_, e) => await SaveEditedMachineAsync(e.RowIndex);
        _machinesGrid.DataError += (_, _) => { };

        _addMachineButton.Enabled = _canEdit;
        _addMachineButton.Click += async (_, _) => await OpenAddMachineDialogAsync();

        var actionsPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(8), FlowDirection = FlowDirection.LeftToRight };
        actionsPanel.Controls.Add(_addMachineButton);

        machinesTab.Controls.Add(_machinesGrid);
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
        _jobsGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "InspectionRequested", HeaderText = "Inspection Requested", Width = 130 });

        _jobsGrid.CellFormatting += (_, e) =>
        {
            if (_jobsGrid.Rows[e.RowIndex].DataBoundItem is not ProductionJob job)
            {
                return;
            }

            if (_jobsGrid.Columns[e.ColumnIndex].Name == "QualityApproved")
            {
                e.Value = _flowService.IsQualityApproved(job.JobNumber);
                e.FormattingApplied = true;
            }

            if (_jobsGrid.Columns[e.ColumnIndex].Name == "InspectionRequested")
            {
                e.Value = _flowService.IsInspectionRequested(job.JobNumber);
                e.FormattingApplied = true;
            }
        };
    }

    private void ConfigureArchiveGrid()
    {
        _archiveGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Archive #", DataPropertyName = nameof(ArchivedWorkflowJob.ArchiveId), Width = 80 });
        _archiveGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Job #", DataPropertyName = nameof(ArchivedWorkflowJob.JobNumber), Width = 120 });
        _archiveGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Product", DataPropertyName = nameof(ArchivedWorkflowJob.ProductName), Width = 220 });
        _archiveGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Archived By", DataPropertyName = nameof(ArchivedWorkflowJob.ArchivedByUserId), Width = 120 });
        _archiveGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Archived (UTC)", DataPropertyName = nameof(ArchivedWorkflowJob.ArchivedUtc), Width = 160 });
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

        var archived = await _productionRepository.GetArchivedWorkflowJobsAsync(JobFlowService.WorkflowModule.Production);
        _archiveGrid.DataSource = archived.ToList();

        _feedback.Text = $"Loaded {queuedJobs.Count} production jobs in queue.";
    }

    private async Task LoadMachinesAsync()
    {
        var machines = await _productionRepository.GetMachinesAsync();
        _machinesGrid.DataSource = machines;
        _machinesList.DataSource = machines;
        _machinesList.DisplayMember = nameof(Machine.MachineCode);
    }

    private async Task OpenAddMachineDialogAsync()
    {
        var nextMachineId = await _productionRepository.GenerateNextMachineCodeAsync();

        using var form = new Form
        {
            Text = "Add New Machine",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12)
        };

        var idBox = new TextBox { Width = 180, Text = nextMachineId, ReadOnly = true };
        var descriptionBox = new TextBox { Width = 320 };
        var capacityInput = new NumericUpDown { Width = 120, Minimum = 0, Maximum = 24, Value = 8 };
        var typePicker = new ComboBox { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
        typePicker.Items.AddRange(MachineTypes);
        typePicker.SelectedItem = "Other";

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 5, Dock = DockStyle.Fill };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label { Text = "Machine ID", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(idBox, 1, 0);
        layout.Controls.Add(new Label { Text = "Description", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(descriptionBox, 1, 1);
        layout.Controls.Add(new Label { Text = "Daily Capacity", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        layout.Controls.Add(capacityInput, 1, 2);
        layout.Controls.Add(new Label { Text = "Machine Type", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        layout.Controls.Add(typePicker, 1, 3);

        var buttonsPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var saveButton = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttonsPanel.Controls.Add(saveButton);
        buttonsPanel.Controls.Add(cancelButton);

        layout.Controls.Add(buttonsPanel, 0, 4);
        layout.SetColumnSpan(buttonsPanel, 2);

        form.AcceptButton = saveButton;
        form.CancelButton = cancelButton;
        form.Controls.Add(layout);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var description = descriptionBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(description))
        {
            _feedback.Text = "Machine description is required.";
            return;
        }

        var machineType = typePicker.SelectedItem?.ToString() ?? "Other";

        await _productionRepository.SaveMachineAsync(new Machine
        {
            MachineCode = idBox.Text.Trim(),
            Description = description,
            DailyCapacityHours = (int)capacityInput.Value,
            MachineType = machineType
        });

        _feedback.Text = $"Saved machine {idBox.Text.Trim()}.";
        await LoadMachinesAsync();
    }

    private async Task SaveEditedMachineAsync(int rowIndex)
    {
        if (!_canEdit || rowIndex < 0 || rowIndex >= _machinesGrid.Rows.Count)
        {
            return;
        }

        if (_machinesGrid.Rows[rowIndex].DataBoundItem is not Machine machine)
        {
            return;
        }

        machine.Description = machine.Description.Trim();
        machine.MachineType = string.IsNullOrWhiteSpace(machine.MachineType) ? "Other" : machine.MachineType.Trim();
        machine.DailyCapacityHours = Math.Clamp(machine.DailyCapacityHours, 0, 24);

        await _productionRepository.SaveMachineAsync(machine);
        _feedback.Text = $"Updated machine {machine.MachineCode}.";
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
            _productionSplit.Panel2Collapsed = false;
            _productionSplit.SplitterDistance = Math.Max(260, Height / 2);
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
        _productionSplit.Panel2Collapsed = false;
        _productionSplit.SplitterDistance = Math.Max(260, Height / 2);
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
        if (!_isProductionManager)
        {
            _feedback.Text = "Only Production Managers can start production.";
            return;
        }

        if (_jobsGrid.CurrentRow?.DataBoundItem is not ProductionJob selected)
        {
            _feedback.Text = "Select a job first.";
            return;
        }

        var result = await _productionRepository.StartJobAsync(selected.JobNumber, QuoteStatus.Won, selected.SourceQuoteId ?? 0, "system.user");
        _feedback.Text = result.Message;
        if (!result.Success)
        {
            await LoadJobsAsync();
            return;
        }

        await _userRepository.WriteAuditLogAsync(new AuditLogEntry
        {
            OccurredUtc = DateTime.UtcNow,
            Username = _currentUser.Username,
            RoleSnapshot = UserManagementRepository.BuildRoleSnapshot(_currentUser),
            Module = "Production",
            Action = "Started production job",
            Details = $"Started job {selected.JobNumber}."
        });
        await LoadJobsAsync();
    }

    private async Task CompleteSelectedJobAsync()
    {
        if (!_isProductionManager)
        {
            _feedback.Text = "Only Production Managers can complete production.";
            return;
        }

        if (_jobsGrid.CurrentRow?.DataBoundItem is not ProductionJob selected)
        {
            _feedback.Text = "Select a job first.";
            return;
        }

        var result = await _productionRepository.CompleteJobAsync(selected.JobNumber, "system.user");
        _feedback.Text = result.Message;
        if (!result.Success)
        {
            await LoadJobsAsync();
            return;
        }

        await _userRepository.WriteAuditLogAsync(new AuditLogEntry
        {
            OccurredUtc = DateTime.UtcNow,
            Username = _currentUser.Username,
            RoleSnapshot = UserManagementRepository.BuildRoleSnapshot(_currentUser),
            Module = "Production",
            Action = "Completed production job",
            Details = $"Completed job {selected.JobNumber}."
        });
        await LoadJobsAsync();
    }

    private async Task RequestInspectionAsync()
    {
        if (!_isProductionEmployee || _isProductionManager)
        {
            _feedback.Text = "Only Production Employees can request inspection.";
            return;
        }

        await MoveSelectedToInspectionAsync(isManagerAction: false);
    }

    private async Task MoveSelectedToInspectionAsync(bool isManagerAction)
    {
        if (_jobsGrid.CurrentRow?.DataBoundItem is not ProductionJob selected)
        {
            _feedback.Text = "Select a job first.";
            return;
        }

        bool moved;
        string message;
        if (isManagerAction)
        {
            moved = _flowService.TryMoveToInspectionFromProduction(selected, out message);
        }
        else
        {
            moved = _flowService.TryRequestInspection(selected, out message);
        }

        _feedback.Text = message;
        await LoadJobsAsync();

        if (moved && isManagerAction)
        {
            if (_isAdmin)
            {
                await _userRepository.WriteAuditLogAsync(new AuditLogEntry
                {
                    OccurredUtc = DateTime.UtcNow,
                    Username = _currentUser.Username,
                    RoleSnapshot = UserManagementRepository.BuildRoleSnapshot(_currentUser),
                    Module = "Production",
                    Action = "Admin module override",
                    Details = $"Moved job {selected.JobNumber} from Production to Inspection."
                });
            }

            _openSection("Inspection");
        }
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
            Text = $"Technical Documentation - {selected.JobNumber}",
            Width = 780,
            Height = 520,
            StartPosition = FormStartPosition.CenterParent
        };

        var details = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            Text = $"Technical documentation\n\nJob: {selected.JobNumber}\nQuote Lifecycle: {selected.QuoteLifecycleId}\nDue Date: {selected.DueDateUtc:u}\nStatus: {selected.Status}\n\nUse this view to access drawings, specs, and process notes.",
            Font = new Font("Segoe UI", 10F)
        };

        window.Controls.Add(details);
        window.ShowDialog(this);
    }

    private async Task AdminMoveSelectedAsync(bool forward)
    {
        if (!_isProductionManager)
        {
            _feedback.Text = "Only Production Managers can move jobs between modules.";
            return;
        }

        if (_jobsGrid.CurrentRow?.DataBoundItem is not ProductionJob selected)
        {
            _feedback.Text = "Select a job first.";
            return;
        }

        string message;
        bool moved;
        if (_isAdmin)
        {
            moved = _flowService.TryMoveToModule(
                selected,
                forward ? JobFlowService.WorkflowModule.Inspection : JobFlowService.WorkflowModule.Production,
                bypassValidation: true,
                out message);
        }
        else
        {
            moved = forward
                ? _flowService.TryAdvanceModule(selected, out message)
                : _flowService.TryRewindModule(selected, out message);
        }

        _feedback.Text = message;
        await LoadJobsAsync();

        if (!moved)
        {
            return;
        }

        await _userRepository.WriteAuditLogAsync(new AuditLogEntry
        {
            OccurredUtc = DateTime.UtcNow,
            Username = _currentUser.Username,
            RoleSnapshot = UserManagementRepository.BuildRoleSnapshot(_currentUser),
            Module = "Production",
            Action = "Admin module override",
            Details = $"Moved job {selected.JobNumber} to {(forward ? "Inspection" : "Purchasing")} from Production."
        });

        if (forward)
        {
            _openSection("Inspection");
        }
        else
        {
            _openSection("Purchasing");
        }
    }

    private async Task DeleteSelectedJobAsync()
    {
        if (!_isAdmin)
        {
            _feedback.Text = "Only Administrators can delete production jobs.";
            return;
        }

        if (_jobsGrid.CurrentRow?.DataBoundItem is not ProductionJob selected)
        {
            _feedback.Text = "Select a job first.";
            return;
        }

        await _productionRepository.ArchiveAndDeleteJobAsync(selected.JobNumber, JobFlowService.WorkflowModule.Production, _currentUser.Username);
        _flowService.RemoveJobState(selected.JobNumber);
        await _userRepository.WriteAuditLogAsync(new AuditLogEntry
        {
            OccurredUtc = DateTime.UtcNow,
            Username = _currentUser.Username,
            RoleSnapshot = UserManagementRepository.BuildRoleSnapshot(_currentUser),
            Module = "Production",
            Action = "Deleted job",
            Details = $"Deleted production job {selected.JobNumber}; archived to Production archive."
        });

        _feedback.Text = $"Deleted and archived job {selected.JobNumber}.";
        await LoadJobsAsync();
    }

    private async Task RestoreSelectedArchivedJobAsync()
    {
        if (!_isAdmin)
        {
            _feedback.Text = "Only Administrators can restore from archive.";
            return;
        }

        if (_archiveGrid.CurrentRow?.DataBoundItem is not ArchivedWorkflowJob archived)
        {
            _feedback.Text = "Select an archived job first.";
            return;
        }

        var result = await _productionRepository.RestoreArchivedWorkflowJobAsync(archived.ArchiveId);
        if (result.Success)
        {
            var restored = new ProductionJob
            {
                JobNumber = archived.JobNumber,
                Status = archived.Status
            };
            _flowService.TryMoveToModule(restored, JobFlowService.WorkflowModule.Production, bypassValidation: true, out _);

            await _userRepository.WriteAuditLogAsync(new AuditLogEntry
            {
                OccurredUtc = DateTime.UtcNow,
                Username = _currentUser.Username,
                RoleSnapshot = UserManagementRepository.BuildRoleSnapshot(_currentUser),
                Module = "Production",
                Action = "Restored archived job",
                Details = $"Restored production job {archived.JobNumber} from Production archive."
            });
        }

        _feedback.Text = result.Message;
        await LoadJobsAsync();
    }

    private async Task SafeUiActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _feedback.Text = $"Action failed: {ex.Message}";
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
