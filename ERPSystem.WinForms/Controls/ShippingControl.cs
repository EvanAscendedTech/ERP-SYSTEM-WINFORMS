using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Controls;

public class ShippingControl : UserControl
{
    private readonly ProductionRepository _productionRepository;
    private readonly JobFlowService _flowService;
    private readonly DataGridView _jobsGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly Label _feedback = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };

    public ShippingControl(ProductionRepository productionRepository, JobFlowService flowService)
    {
        _productionRepository = productionRepository;
        _flowService = flowService;
        Dock = DockStyle.Fill;

        ConfigureGrid();

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
        var refresh = new Button { Text = "Refresh", AutoSize = true };
        var markShipped = new Button { Text = "Mark Shipped", AutoSize = true };

        refresh.Click += async (_, _) => await LoadJobsAsync();
        markShipped.Click += async (_, _) => await MarkSelectedShippedAsync();

        actions.Controls.Add(refresh);
        actions.Controls.Add(markShipped);

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
        _jobsGrid.DataSource = jobs.Where(x => _flowService.IsInspectionPassed(x.JobNumber)).OrderBy(x => x.JobNumber).ToList();
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
}
