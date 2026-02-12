using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Controls;

public class QualityControl : UserControl
{
    private readonly ProductionRepository _productionRepository;
    private readonly JobFlowService _flowService;
    private readonly Action<string> _openSection;
    private readonly DataGridView _jobsGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly Label _feedback = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };

    public QualityControl(ProductionRepository productionRepository, JobFlowService flowService, Action<string> openSection)
    {
        _productionRepository = productionRepository;
        _flowService = flowService;
        _openSection = openSection;
        Dock = DockStyle.Fill;

        ConfigureGrid();

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
        var refresh = new Button { Text = "Refresh", AutoSize = true };
        var approve = new Button { Text = "Approve for Inspection", AutoSize = true };

        refresh.Click += async (_, _) => await LoadJobsAsync();
        approve.Click += async (_, _) => await ApproveSelectedAsync();

        actions.Controls.Add(refresh);
        actions.Controls.Add(approve);

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
        _jobsGrid.DataSource = jobs.Where(x => x.Status == ProductionJobStatus.Completed).OrderBy(x => x.JobNumber).ToList();
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
}
