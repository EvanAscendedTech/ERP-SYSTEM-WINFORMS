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
    private readonly DataGridView _jobsGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly Label _feedback = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };

    public ProductionControl(ProductionRepository productionRepository, JobFlowService flowService, Models.UserAccount currentUser, Action<string> openSection)
    {
        _productionRepository = productionRepository;
        _flowService = flowService;
        _openSection = openSection;
        _isAdmin = currentUser.Roles.Any(r => string.Equals(r.Name, "Admin", StringComparison.OrdinalIgnoreCase)
                                           || string.Equals(r.Name, "Administrator", StringComparison.OrdinalIgnoreCase));
        Dock = DockStyle.Fill;

        ConfigureGrid();

        var actionsPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
        var refreshButton = new Button { Text = "Refresh Jobs", AutoSize = true };
        var startButton = new Button { Text = "Start Production", AutoSize = true };
        var completeButton = new Button { Text = "Complete Production", AutoSize = true };
        var qualityButton = new Button { Text = "Move to Quality", AutoSize = true };
        var openDetailsButton = new Button { Text = "Open Production View", AutoSize = true };
        var advanceButton = new Button { Text = "Admin: Push Forward", AutoSize = true, Visible = _isAdmin };
        var rewindButton = new Button { Text = "Admin: Push Backward", AutoSize = true, Visible = _isAdmin };

        refreshButton.Click += async (_, _) => await LoadJobsAsync();
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

        Controls.Add(_jobsGrid);
        Controls.Add(actionsPanel);
        Controls.Add(_feedback);

        _ = LoadJobsAsync();
    }

    private void ConfigureGrid()
    {
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "JobNumber", HeaderText = "Job #", DataPropertyName = nameof(ProductionJob.JobNumber), Width = 120 });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Product", HeaderText = "Product", DataPropertyName = nameof(ProductionJob.ProductName), Width = 220 });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Production Status", DataPropertyName = nameof(ProductionJob.Status), Width = 140 });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Planned", HeaderText = "Planned Qty", DataPropertyName = nameof(ProductionJob.PlannedQuantity), Width = 110 });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Produced", HeaderText = "Produced Qty", DataPropertyName = nameof(ProductionJob.ProducedQuantity), Width = 110 });
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
        _feedback.Text = $"Loaded {queuedJobs.Count} production jobs.";
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

    public Task RefreshDataAsync(bool fromFailSafeCheckpoint) => LoadJobsAsync();

}
