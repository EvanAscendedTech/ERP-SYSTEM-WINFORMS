using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Controls;

public class QualityControl : UserControl, IRealtimeDataControl
{
    private readonly ProductionRepository _productionRepository;
    private readonly JobFlowService _flowService;
    private readonly Action<string> _openSection;
    private readonly bool _isAdmin;
    private readonly DataGridView _jobsGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly Label _feedback = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };

    public QualityControl(ProductionRepository productionRepository, JobFlowService flowService, Models.UserAccount currentUser, Action<string> openSection)
    {
        _productionRepository = productionRepository;
        _flowService = flowService;
        _openSection = openSection;
        _isAdmin = currentUser.Roles.Any(r => string.Equals(r.Name, "Admin", StringComparison.OrdinalIgnoreCase)
                                           || string.Equals(r.Name, "Administrator", StringComparison.OrdinalIgnoreCase));
        Dock = DockStyle.Fill;

        ConfigureGrid();

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
        var refresh = new Button { Text = "Refresh", AutoSize = true };
        var approve = new Button { Text = "Approve for Inspection", AutoSize = true };
        var advance = new Button { Text = "Admin: Push Forward", AutoSize = true, Visible = _isAdmin };
        var rewind = new Button { Text = "Admin: Push Backward", AutoSize = true, Visible = _isAdmin };

        refresh.Click += async (_, _) => await LoadJobsAsync();
        approve.Click += async (_, _) => await ApproveSelectedAsync();
        advance.Click += async (_, _) => await AdminMoveSelectedAsync(forward: true);
        rewind.Click += async (_, _) => await AdminMoveSelectedAsync(forward: false);

        actions.Controls.Add(refresh);
        actions.Controls.Add(approve);
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
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Production Status", DataPropertyName = nameof(ProductionJob.Status), Width = 140 });
        _jobsGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Approved", HeaderText = "Quality Approved", Width = 120 });

        _jobsGrid.CellFormatting += (_, e) =>
        {
            if (_jobsGrid.Columns[e.ColumnIndex].Name == "Approved"
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
        _jobsGrid.DataSource = jobs.Where(x => _flowService.IsInModule(x.JobNumber, JobFlowService.WorkflowModule.Quality)).OrderBy(x => x.JobNumber).ToList();
        _feedback.Text = "Quality queue refreshed.";
    }

    private async Task ApproveSelectedAsync()
    {
        if (_jobsGrid.CurrentRow?.DataBoundItem is not ProductionJob selected)
        {
            _feedback.Text = "Select a job first.";
            return;
        }

        _flowService.TryApproveQuality(selected, out var message);
        _feedback.Text = message;
        await LoadJobsAsync();

        if (_flowService.IsQualityApproved(selected.JobNumber))
        {
            _openSection("Inspection");
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
            _feedback.Text = $"Job {jobNumber} is not currently in the Quality queue.";
            return false;
        }

        if (openDetails)
        {
            ShowJobDetails(selectedJob, "Quality");
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
