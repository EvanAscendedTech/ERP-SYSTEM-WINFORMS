using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Controls;

public class ShippingControl : UserControl, IRealtimeDataControl
{
    private readonly ProductionRepository _productionRepository;
    private readonly JobFlowService _flowService;
    private readonly Action<string> _openSection;
    private readonly bool _isAdmin;
    private readonly bool _canEdit;
    private readonly DataGridView _jobsGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly Label _feedback = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };

    public ShippingControl(ProductionRepository productionRepository, JobFlowService flowService, Models.UserAccount currentUser, Action<string> openSection, bool canEdit)
    {
        _productionRepository = productionRepository;
        _flowService = flowService;
        _openSection = openSection;
        _canEdit = canEdit;
        _isAdmin = AuthorizationService.HasRole(currentUser, RoleCatalog.Administrator);
        Dock = DockStyle.Fill;

        ConfigureGrid();

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
        var refresh = new Button { Text = "Refresh", AutoSize = true };
        var markShipped = new Button { Text = "Mark Shipped", AutoSize = true, Enabled = _canEdit };
        var rewind = new Button { Text = "Admin: Push Backward", AutoSize = true, Visible = _isAdmin };

        refresh.Click += async (_, _) => await LoadJobsAsync();
        markShipped.Click += async (_, _) => await MarkSelectedShippedAsync();
        rewind.Click += async (_, _) => await AdminMoveBackwardAsync();

        actions.Controls.Add(refresh);
        actions.Controls.Add(markShipped);
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
        _jobsGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "InspectionPassed", HeaderText = "Inspection Passed", Width = 120 });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ShippedUtc", HeaderText = "Shipped (UTC)", Width = 180 });

        _jobsGrid.CellFormatting += (_, e) =>
        {
            if (_jobsGrid.Rows[e.RowIndex].DataBoundItem is not ProductionJob job)
            {
                return;
            }

            if (_jobsGrid.Columns[e.ColumnIndex].Name == "InspectionPassed")
            {
                e.Value = _flowService.IsInspectionPassed(job.JobNumber);
                e.FormattingApplied = true;
            }

            if (_jobsGrid.Columns[e.ColumnIndex].Name == "ShippedUtc")
            {
                e.Value = _flowService.GetShippedUtc(job.JobNumber)?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;
                e.FormattingApplied = true;
            }
        };
    }

    private async Task LoadJobsAsync()
    {
        var jobs = await _productionRepository.GetJobsAsync();
        _jobsGrid.DataSource = jobs.Where(x => _flowService.IsInModule(x.JobNumber, JobFlowService.WorkflowModule.Shipping)).OrderBy(x => x.JobNumber).ToList();
        _feedback.Text = "Shipping queue refreshed.";
    }

    private async Task MarkSelectedShippedAsync()
    {
        if (_jobsGrid.CurrentRow?.DataBoundItem is not ProductionJob selected)
        {
            _feedback.Text = "Select a job first.";
            return;
        }

        _flowService.TryMarkShipped(selected, out var message);
        _feedback.Text = message;
        await LoadJobsAsync();
    }

    private async Task AdminMoveBackwardAsync()
    {
        if (_jobsGrid.CurrentRow?.DataBoundItem is not ProductionJob selected)
        {
            _feedback.Text = "Select a job first.";
            return;
        }

        var moved = _flowService.TryRewindModule(selected, out var message);
        _feedback.Text = message;
        await LoadJobsAsync();

        if (moved)
        {
            _openSection(_flowService.GetCurrentModule(selected.JobNumber).ToString());
        }
    }


    public async Task<bool> OpenFromDashboardAsync(string jobNumber, bool openDetails)
    {
        await LoadJobsAsync();

        var selectedJob = SelectJobRow(jobNumber);
        if (selectedJob is null)
        {
            _feedback.Text = $"Job {jobNumber} is not currently in the Shipping queue.";
            return false;
        }

        if (openDetails)
        {
            ShowJobDetails(selectedJob, "Shipping");
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
