using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Controls;

public class ProductionControl : UserControl
{
    private readonly ProductionRepository _repository;
    private readonly DataGridView _jobsGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false };
    private readonly Label _feedback = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };

    public ProductionControl(ProductionRepository repository)
    {
        _repository = repository;
        Dock = DockStyle.Fill;

        ConfigureGrid();

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8) };
        var add = new Button { Text = "Add Job", AutoSize = true };
        add.Click += (_, _) => _jobsGrid.Rows.Add("JOB-", "", 0, 0, DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd"), ProductionJobStatus.Planned);

        var save = new Button { Text = "Save Jobs", AutoSize = true };
        save.Click += async (_, _) => await SaveJobsAsync();

        var refresh = new Button { Text = "Refresh", AutoSize = true };
        refresh.Click += async (_, _) => await LoadJobsAsync();

        toolbar.Controls.Add(add);
        toolbar.Controls.Add(save);
        toolbar.Controls.Add(refresh);

        Controls.Add(_jobsGrid);
        Controls.Add(toolbar);
        Controls.Add(_feedback);

        _ = LoadJobsAsync();
    }

    private void ConfigureGrid()
    {
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Job Number", Width = 140 });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Product", Width = 220 });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Planned", Width = 90 });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Produced", Width = 90 });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Due Date (yyyy-MM-dd)", Width = 150 });
        var statusColumn = new DataGridViewComboBoxColumn
        {
            HeaderText = "Status",
            Width = 140,
            DataSource = Enum.GetValues(typeof(ProductionJobStatus))
        };
        _jobsGrid.Columns.Add(statusColumn);
    }

    private async Task SaveJobsAsync()
    {
        var count = 0;
        foreach (DataGridViewRow row in _jobsGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var jobNumber = row.Cells[0].Value?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(jobNumber))
            {
                continue;
            }

            _ = int.TryParse(row.Cells[2].Value?.ToString(), out var planned);
            _ = int.TryParse(row.Cells[3].Value?.ToString(), out var produced);
            _ = DateTime.TryParse(row.Cells[4].Value?.ToString(), out var dueDate);

            var job = new ProductionJob
            {
                JobNumber = jobNumber,
                ProductName = row.Cells[1].Value?.ToString()?.Trim() ?? string.Empty,
                PlannedQuantity = planned,
                ProducedQuantity = produced,
                DueDateUtc = dueDate == default ? DateTime.UtcNow.AddDays(1) : dueDate.ToUniversalTime(),
                Status = row.Cells[5].Value is ProductionJobStatus status ? status : ProductionJobStatus.Planned
            };

            await _repository.SaveJobAsync(job);
            count++;
        }

        _feedback.Text = $"Saved {count} production jobs.";
        await LoadJobsAsync();
    }

    private async Task LoadJobsAsync()
    {
        _jobsGrid.Rows.Clear();
        var jobs = await _repository.GetJobsAsync();
        foreach (var job in jobs)
        {
            _jobsGrid.Rows.Add(job.JobNumber, job.ProductName, job.PlannedQuantity, job.ProducedQuantity, job.DueDateUtc.ToString("yyyy-MM-dd"), job.Status);
        }

        _feedback.Text = $"Loaded {jobs.Count} jobs.";
    }
}
