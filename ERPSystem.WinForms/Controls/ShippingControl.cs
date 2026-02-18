using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Controls;

public class ShippingControl : UserControl, IRealtimeDataControl
{
    private readonly ProductionRepository _productionRepository;
    private readonly JobFlowService _flowService;
    private readonly Action<string> _openSection;
    private readonly UserManagementRepository? _userRepository;
    private readonly UserAccount _currentUser;
    private readonly DataGridView _archiveGrid = new() { Dock = DockStyle.Bottom, Height = 170, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly bool _isAdmin;
    private readonly bool _canEdit;
    private readonly DataGridView _jobsGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly Label _feedback = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };

    public ShippingControl(ProductionRepository productionRepository, JobFlowService flowService, UserManagementRepository userRepository, Models.UserAccount currentUser, Action<string> openSection, bool canEdit)
    {
        _productionRepository = productionRepository;
        _flowService = flowService;
        _openSection = openSection;
        _userRepository = userRepository;
        _currentUser = currentUser;
        _canEdit = canEdit;
        _isAdmin = AuthorizationService.HasRole(currentUser, RoleCatalog.Administrator);
        Dock = DockStyle.Fill;

        ConfigureGrid();

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
        var refresh = new Button { Text = "Refresh", AutoSize = true };
        var markShipped = new Button { Text = "Mark Shipped", AutoSize = true, Enabled = _canEdit };
        var rewind = new Button { Text = "Admin: Override to Purchasing", AutoSize = true, Visible = _isAdmin };
        var delete = new Button { Text = "Admin: Delete Job", AutoSize = true, Visible = _isAdmin };
        var restore = new Button { Text = $"Admin: Restore from Shipping Archive", AutoSize = true, Visible = _isAdmin };

        refresh.Click += async (_, _) => await LoadJobsAsync();
        markShipped.Click += async (_, _) => await MarkSelectedShippedAsync();
        rewind.Click += async (_, _) => await AdminMoveBackwardAsync();
        delete.Click += async (_, _) => await DeleteSelectedAsync();
        restore.Click += async (_, _) => await RestoreSelectedAsync();

        actions.Controls.Add(refresh);
        actions.Controls.Add(markShipped);
        actions.Controls.Add(rewind);
        actions.Controls.Add(delete);
        actions.Controls.Add(restore);

        ConfigureArchiveGrid();
        Controls.Add(_jobsGrid);
        Controls.Add(_archiveGrid);
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
        var archived = await _productionRepository.GetArchivedWorkflowJobsAsync(JobFlowService.WorkflowModule.Shipping);
        _archiveGrid.DataSource = archived.ToList();
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

        string message;
        var moved = _isAdmin
            ? _flowService.TryMoveToModule(selected, JobFlowService.WorkflowModule.Production, bypassValidation: true, out message)
            : _flowService.TryRewindModule(selected, out message);
        _feedback.Text = message;
        await LoadJobsAsync();

        if (moved)
        {
            _openSection("Purchasing");
            if (_userRepository is not null)
            {
                await _userRepository.WriteAuditLogAsync(new AuditLogEntry
                {
                    OccurredUtc = DateTime.UtcNow, Username = _currentUser.Username, RoleSnapshot = UserManagementRepository.BuildRoleSnapshot(_currentUser),
                    Module = "Shipping", Action = "Admin module override", Details = $"Moved job {selected.JobNumber} from Shipping to Purchasing."
                });
            }
        }
    }


    private void ConfigureArchiveGrid()
    {
        _archiveGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Archive #", DataPropertyName = nameof(ArchivedWorkflowJob.ArchiveId), Width = 80 });
        _archiveGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Job #", DataPropertyName = nameof(ArchivedWorkflowJob.JobNumber), Width = 120 });
        _archiveGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Product", DataPropertyName = nameof(ArchivedWorkflowJob.ProductName), Width = 220 });
        _archiveGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Archived (UTC)", DataPropertyName = nameof(ArchivedWorkflowJob.ArchivedUtc), Width = 150 });
    }

    private async Task DeleteSelectedAsync()
    {
        if (!_isAdmin)
        {
            _feedback.Text = "Only Administrators can delete jobs.";
            return;
        }

        if (_jobsGrid.CurrentRow?.DataBoundItem is not ProductionJob selected)
        {
            _feedback.Text = "Select a job first.";
            return;
        }

        await _productionRepository.ArchiveAndDeleteJobAsync(selected.JobNumber, JobFlowService.WorkflowModule.Shipping, _currentUser.Username);
        _flowService.RemoveJobState(selected.JobNumber);
        if (_userRepository is not null)
        {
            await _userRepository.WriteAuditLogAsync(new AuditLogEntry
            {
                OccurredUtc = DateTime.UtcNow,
                Username = _currentUser.Username,
                RoleSnapshot = UserManagementRepository.BuildRoleSnapshot(_currentUser),
                Module = "Shipping",
                Action = "Deleted job",
                Details = $"Deleted shipping job {selected.JobNumber}; archived to Shipping archive."
            });
        }

        await LoadJobsAsync();
    }

    private async Task RestoreSelectedAsync()
    {
        if (!_isAdmin)
        {
            _feedback.Text = "Only Administrators can restore archived jobs.";
            return;
        }

        if (_archiveGrid.CurrentRow?.DataBoundItem is not ArchivedWorkflowJob archived)
        {
            _feedback.Text = "Select an archived job first.";
            return;
        }

        var result = await _productionRepository.RestoreArchivedWorkflowJobAsync(archived.ArchiveId);
        if (result.Success && _userRepository is not null)
        {
            await _userRepository.WriteAuditLogAsync(new AuditLogEntry
            {
                OccurredUtc = DateTime.UtcNow,
                Username = _currentUser.Username,
                RoleSnapshot = UserManagementRepository.BuildRoleSnapshot(_currentUser),
                Module = "Shipping",
                Action = "Restored archived job",
                Details = $"Restored shipping job {archived.JobNumber} from Shipping archive."
            });
        }

        _feedback.Text = result.Message;
        await LoadJobsAsync();
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
