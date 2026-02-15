using System.Text;
using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Forms;

public class ArchivedQuotesForm : Form
{
    private readonly QuoteRepository _quoteRepository;
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly TextBox _searchBox = new() { Width = 320, PlaceholderText = "Search archived quotes" };
    private readonly ComboBox _fieldFilter = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 26, TextAlign = ContentAlignment.MiddleLeft };
    private List<ArchivedQuoteSummary> _all = new();

    public ArchivedQuotesForm(QuoteRepository quoteRepository)
    {
        _quoteRepository = quoteRepository;
        Text = "Archived Quotes";
        Width = 1100;
        Height = 700;
        StartPosition = FormStartPosition.CenterParent;

        ConfigureGrid();

        _fieldFilter.Items.AddRange(["All Fields", "Customer", "Quote ID", "Lifecycle ID", "Status"]);
        _fieldFilter.SelectedIndex = 0;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8) };
        var refreshButton = new Button { Text = "Refresh", AutoSize = true };
        var openButton = new Button { Text = "Open Read-Only", AutoSize = true };
        refreshButton.Click += async (_, _) => await LoadArchivedQuotesAsync();
        openButton.Click += async (_, _) => await OpenSelectedAsync();
        _searchBox.TextChanged += (_, _) => ApplyFilter();
        _fieldFilter.SelectedIndexChanged += (_, _) => ApplyFilter();
        _grid.CellDoubleClick += async (_, _) => await OpenSelectedAsync();

        top.Controls.Add(new Label { Text = "Filter:", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        top.Controls.Add(_fieldFilter);
        top.Controls.Add(_searchBox);
        top.Controls.Add(refreshButton);
        top.Controls.Add(openButton);

        Controls.Add(_grid);
        Controls.Add(top);
        Controls.Add(_status);

        _ = LoadArchivedQuotesAsync();
    }

    private void ConfigureGrid()
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Archive #", DataPropertyName = nameof(ArchivedQuoteRow.ArchiveId), Width = 80 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Quote #", DataPropertyName = nameof(ArchivedQuoteRow.OriginalQuoteId), Width = 80 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Lifecycle ID", DataPropertyName = nameof(ArchivedQuoteRow.LifecycleQuoteId), Width = 180 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Customer", DataPropertyName = nameof(ArchivedQuoteRow.CustomerName), Width = 230 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Status", DataPropertyName = nameof(ArchivedQuoteRow.Status), Width = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Created", DataPropertyName = nameof(ArchivedQuoteRow.CreatedUtc), Width = 140 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Completed", DataPropertyName = nameof(ArchivedQuoteRow.CompletedUtc), Width = 140 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Archived", DataPropertyName = nameof(ArchivedQuoteRow.ArchivedUtc), Width = 140 });
    }

    private async Task LoadArchivedQuotesAsync()
    {
        try
        {
            _all = (await _quoteRepository.GetArchivedQuotesAsync()).ToList();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            _status.Text = $"Unable to load archived quotes: {ex.Message}";
        }
    }

    private void ApplyFilter()
    {
        var term = _searchBox.Text.Trim();
        IEnumerable<ArchivedQuoteSummary> filtered = _all;

        if (!string.IsNullOrWhiteSpace(term))
        {
            var normalized = term.ToLowerInvariant();
            filtered = _fieldFilter.SelectedIndex switch
            {
                1 => filtered.Where(q => q.CustomerName.Contains(term, StringComparison.OrdinalIgnoreCase)),
                2 => filtered.Where(q => q.OriginalQuoteId.ToString().Contains(normalized)),
                3 => filtered.Where(q => q.LifecycleQuoteId.Contains(term, StringComparison.OrdinalIgnoreCase)),
                4 => filtered.Where(q => q.Status.ToString().Contains(term, StringComparison.OrdinalIgnoreCase)),
                _ => filtered.Where(q => BuildSearchText(q).Contains(normalized))
            };
        }

        var rows = filtered
            .OrderByDescending(q => q.ArchivedUtc)
            .Select(q => new ArchivedQuoteRow
            {
                ArchiveId = q.ArchiveId,
                OriginalQuoteId = q.OriginalQuoteId,
                LifecycleQuoteId = q.LifecycleQuoteId,
                CustomerName = q.CustomerName,
                Status = q.Status.ToString(),
                CreatedUtc = q.CreatedUtc.ToLocalTime().ToString("g"),
                CompletedUtc = q.CompletedUtc?.ToLocalTime().ToString("g") ?? string.Empty,
                ArchivedUtc = q.ArchivedUtc.ToLocalTime().ToString("g")
            })
            .ToList();

        _grid.DataSource = rows;
        _status.Text = $"Showing {rows.Count} archived quotes.";
    }

    private static string BuildSearchText(ArchivedQuoteSummary q)
    {
        var builder = new StringBuilder();
        builder.Append(q.ArchiveId).Append(' ')
            .Append(q.OriginalQuoteId).Append(' ')
            .Append(q.LifecycleQuoteId).Append(' ')
            .Append(q.CustomerName).Append(' ')
            .Append(q.Status).Append(' ')
            .Append(q.CustomerId);
        return builder.ToString().ToLowerInvariant();
    }

    private async Task OpenSelectedAsync()
    {
        if (_grid.CurrentRow?.DataBoundItem is not ArchivedQuoteRow row)
        {
            _status.Text = "Select an archived quote first.";
            return;
        }

        var archive = await _quoteRepository.GetArchivedQuoteAsync(row.ArchiveId);
        if (archive is null)
        {
            _status.Text = "Archived quote was not found.";
            return;
        }

        using var detail = new ArchivedQuoteDetailForm(archive);
        detail.ShowDialog(this);
    }

    private sealed class ArchivedQuoteRow
    {
        public int ArchiveId { get; init; }
        public int OriginalQuoteId { get; init; }
        public string LifecycleQuoteId { get; init; } = string.Empty;
        public string CustomerName { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string CreatedUtc { get; init; } = string.Empty;
        public string CompletedUtc { get; init; } = string.Empty;
        public string ArchivedUtc { get; init; } = string.Empty;
    }
}
