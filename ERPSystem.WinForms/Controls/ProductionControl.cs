using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Controls;

public class ProductionControl : UserControl
{
    private readonly ProductionRepository _productionRepository;
    private readonly JobFlowService _flowService;
    private readonly Action<string> _openSection;
    private readonly DataGridView _jobsGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly Label _feedback = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };

    public ProductionControl(ProductionRepository productionRepository, JobFlowService flowService, Action<string> openSection)
    {
        _productionRepository = productionRepository;
        _flowService = flowService;
        _openSection = openSection;
        Dock = DockStyle.Fill;

        ConfigureGrid();

        var actionsPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
        var refreshButton = new Button { Text = "Refresh Jobs", AutoSize = true };
        var startButton = new Button { Text = "Start Production", AutoSize = true };
        var completeButton = new Button { Text = "Complete Production", AutoSize = true };
        var qualityButton = new Button { Text = "Move to Quality", AutoSize = true };

        refreshButton.Click += async (_, _) => await LoadJobsAsync();
        startButton.Click += async (_, _) => await StartSelectedJobAsync();
        completeButton.Click += async (_, _) => await CompleteSelectedJobAsync();
        qualityButton.Click += async (_, _) => await MoveSelectedToQualityAsync();

        actionsPanel.Controls.Add(refreshButton);
        actionsPanel.Controls.Add(startButton);
        actionsPanel.Controls.Add(completeButton);
        actionsPanel.Controls.Add(qualityButton);

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
        _jobsGrid.DataSource = jobs.OrderBy(x => x.DueDateUtc).ToList();
        _feedback.Text = $"Loaded {jobs.Count} production jobs.";
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
}
