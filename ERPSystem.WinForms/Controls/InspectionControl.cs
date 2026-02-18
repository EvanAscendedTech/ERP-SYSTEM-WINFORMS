using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Controls;

public class InspectionControl : UserControl, IRealtimeDataControl
{
    private readonly ProductionRepository _productionRepository;
    private readonly JobFlowService _flowService;
    private readonly InspectionService _inspectionService;
    private readonly Action<string> _openSection;
    private readonly bool _isAdmin;
    private readonly bool _canEdit;
    private readonly DataGridView _jobsGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly Label _feedback = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };

    public InspectionControl(ProductionRepository productionRepository, JobFlowService flowService, InspectionService inspectionService, Models.UserAccount currentUser, Action<string> openSection, bool canEdit)
    {
        _productionRepository = productionRepository;
        _flowService = flowService;
        _inspectionService = inspectionService;
        _openSection = openSection;
        _canEdit = canEdit;
        _isAdmin = AuthorizationService.HasRole(currentUser, RoleCatalog.Administrator);
        Dock = DockStyle.Fill;

        ConfigureGrid();

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
        var refresh = new Button { Text = "Refresh", AutoSize = true };
        var startAndPass = new Button { Text = "Start + Pass Inspection", AutoSize = true, Enabled = _canEdit };
        var advance = new Button { Text = "Admin: Push Forward", AutoSize = true, Visible = _isAdmin };
        var rewind = new Button { Text = "Admin: Push Backward", AutoSize = true, Visible = _isAdmin };

        refresh.Click += async (_, _) => await LoadJobsAsync();
        startAndPass.Click += async (_, _) => await PassSelectedAsync();
        advance.Click += async (_, _) => await AdminMoveSelectedAsync(forward: true);
        rewind.Click += async (_, _) => await AdminMoveSelectedAsync(forward: false);

        actions.Controls.Add(refresh);
        actions.Controls.Add(startAndPass);
        actions.Controls.Add(advance);
        actions.Controls.Add(rewind);

        Controls.Add(_jobsGrid);
        Controls.Add(actions);
        Controls.Add(_feedback);

        _ = LoadJobsAsync();
    }

    private void ConfigureGrid()
    {
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Job #", DataPropertyName = nameof(ProductionJob.JobNumber), Width = 120 });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Product", DataPropertyName = nameof(ProductionJob.ProductName), Width = 240 });
        _jobsGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "QualityApproved", HeaderText = "Quality Approved", Width = 120 });
        _jobsGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "InspectionPassed", HeaderText = "Inspection Passed", Width = 120 });

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

            if (_jobsGrid.Columns[e.ColumnIndex].Name == "InspectionPassed")
            {
                e.Value = _flowService.IsInspectionPassed(job.JobNumber);
                e.FormattingApplied = true;
            }
        };
    }

    private async Task LoadJobsAsync()
    {
        var jobs = await _productionRepository.GetJobsAsync();
        _jobsGrid.DataSource = jobs.Where(x => _flowService.IsInModule(x.JobNumber, JobFlowService.WorkflowModule.Inspection)).OrderBy(x => x.JobNumber).ToList();
        _feedback.Text = "Inspection queue refreshed.";
    }

    private async Task PassSelectedAsync()
    {
        if (_jobsGrid.CurrentRow?.DataBoundItem is not ProductionJob selected)
        {
            _feedback.Text = "Select a job first.";
            return;
        }

        var started = _inspectionService.TryStartInspection(selected.JobNumber, selected.Status, "qa.user", out var startMessage);
        if (!started)
        {
            _feedback.Text = startMessage;
            return;
        }

        _flowService.TryPassInspection(selected, out var passMessage);
        _feedback.Text = $"{startMessage} {passMessage}";
        await LoadJobsAsync();

        if (_flowService.IsInspectionPassed(selected.JobNumber))
        {
            _openSection("Shipping");
        }
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

        _openSection(_flowService.GetCurrentModule(selected.JobNumber).ToString());
    }


    public async Task<bool> OpenFromDashboardAsync(string jobNumber, bool openDetails)
    {
        await LoadJobsAsync();

        var selectedJob = SelectJobRow(jobNumber);
        if (selectedJob is null)
        {
            _feedback.Text = $"Job {jobNumber} is not currently in the Inspection queue.";
            return false;
        }

        if (openDetails)
        {
            ShowJobDetails(selectedJob, "Inspection");
        }

        return true;
    }

    private ProductionJob? SelectJobRow(string jobNumber)
    {
        foreach (DataGridViewRow row in _jobsGrid.Rows)
        {
            if (row.DataBoundItem is ProductionJob job && string.Equals(job.JobNumber, jobNumber, StringComparison.OrdinalIgnoreCase))
            {
                row.Selected = true;
                _jobsGrid.CurrentCell = row.Cells[0];
                _jobsGrid.FirstDisplayedScrollingRowIndex = row.Index;
                return job;
            }
        }

        return null;
    }

    private void ShowJobDetails(ProductionJob job, string module)
    {
        MessageBox.Show(this,
            $"{module} snapshot details\n\nJob: {job.JobNumber}\nProduct: {job.ProductName}\nStatus: {job.Status}",
            $"{module} - {job.JobNumber}",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    public Task RefreshDataAsync(bool fromFailSafeCheckpoint) => LoadJobsAsync();

}
