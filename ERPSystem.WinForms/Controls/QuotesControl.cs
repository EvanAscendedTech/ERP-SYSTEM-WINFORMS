using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Forms;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Controls;

public class QuotesControl : UserControl, IRealtimeDataControl
{
    private const int QuoteExpiryDays = 60;
    private const int NearExpiryThresholdDays = 2;

    private readonly QuoteRepository _quoteRepository;
    private readonly ProductionRepository _productionRepository;
    private readonly Action<string> _openSection;
    private readonly UserAccount _currentUser;
    private readonly DataGridView _quotesGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly DataGridView _expiredQuotesGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly Label _feedback = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
    private readonly HashSet<string> _expandedCustomers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _expandedQuotes = new();
    private readonly Dictionary<int, Quote> _expandedQuoteCache = new();

    public QuotesControl(QuoteRepository quoteRepository, ProductionRepository productionRepository, UserAccount currentUser, Action<string> openSection)
    {
        _quoteRepository = quoteRepository;
        _productionRepository = productionRepository;
        _openSection = openSection;
        _currentUser = currentUser;
        Dock = DockStyle.Fill;

        ConfigureQuotesGrid(_quotesGrid, includeLifecycleColumn: true);
        ConfigureQuotesGrid(_expiredQuotesGrid, includeLifecycleColumn: false);

        var actionsPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
        var refreshButton = new Button { Text = "Refresh Active Quotes", AutoSize = true };
        var newQuoteButton = new Button { Text = "Create New Quote", AutoSize = true };
        var openQuotePacketButton = new Button { Text = "Open Quote Packet", AutoSize = true };
        var editQuoteButton = new Button { Text = "Edit In-Process Quote", AutoSize = true };
        var deleteQuoteButton = new Button { Text = "Delete In-Process Quote", AutoSize = true };
        var passToProductionButton = new Button { Text = "Pass to Production", AutoSize = true };

        refreshButton.Click += async (_, _) => await LoadActiveQuotesAsync();
        newQuoteButton.Click += async (_, _) => await CreateNewQuoteAsync();
        openQuotePacketButton.Click += async (_, _) => await OpenSelectedQuotePacketAsync();
        editQuoteButton.Click += async (_, _) => await EditSelectedQuoteAsync();
        deleteQuoteButton.Click += async (_, _) => await DeleteSelectedQuoteAsync();
        passToProductionButton.Click += async (_, _) => await PassSelectedToProductionAsync();
        _quotesGrid.CellDoubleClick += async (_, _) => await EditSelectedQuoteAsync();
        _quotesGrid.CellContentClick += async (_, e) => await HandleQuotesGridContentClickAsync(e);
        _quotesGrid.CellBeginEdit += HandleQuotesGridCellBeginEdit;
        _quotesGrid.CellEndEdit += HandleQuotesGridCellEndEdit;

        actionsPanel.Controls.Add(refreshButton);
        actionsPanel.Controls.Add(newQuoteButton);
        actionsPanel.Controls.Add(openQuotePacketButton);
        actionsPanel.Controls.Add(editQuoteButton);
        actionsPanel.Controls.Add(deleteQuoteButton);
        actionsPanel.Controls.Add(passToProductionButton);

        var topContent = new Panel { Dock = DockStyle.Fill };
        topContent.Controls.Add(_quotesGrid);
        topContent.Controls.Add(actionsPanel);

        var archivePanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
        archivePanel.Controls.Add(_expiredQuotesGrid);
        archivePanel.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = "Expired Quotes Archive (60+ days)",
            Font = new Font(Font, FontStyle.Bold)
        });

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.Panel2,
            SplitterDistance = 430,
            Panel1MinSize = 300,
            Panel2MinSize = 140
        };
        split.Panel1.Controls.Add(topContent);
        split.Panel2.Controls.Add(archivePanel);

        Controls.Add(split);
        Controls.Add(_feedback);

        _ = LoadActiveQuotesAsync();
    }

    private void ConfigureQuotesGrid(DataGridView grid, bool includeLifecycleColumn)
    {
        grid.DefaultCellStyle.Font = new Font(grid.Font, FontStyle.Bold);
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Expander", HeaderText = "", DataPropertyName = nameof(QuoteGridRow.ExpanderDisplay), Width = 40, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "QuoteId", HeaderText = "Quote #", DataPropertyName = nameof(QuoteGridRow.QuoteIdDisplay), Width = 80 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Customer", HeaderText = "Customer", DataPropertyName = nameof(QuoteGridRow.CustomerDisplay), Width = 280 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", DataPropertyName = nameof(QuoteGridRow.StatusDisplay), Width = 120 });
        if (includeLifecycleColumn)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Lifecycle", HeaderText = "Lifecycle Stage", DataPropertyName = nameof(QuoteGridRow.LifecycleStageDisplay), Width = 180 });
        }

        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "QuotedAt", HeaderText = "Quoted", DataPropertyName = nameof(QuoteGridRow.QuotedAtDisplay), Width = 180 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "TimeSinceQuoted", HeaderText = "Timeframe Since Quoted", DataPropertyName = nameof(QuoteGridRow.TimeSinceQuotedDisplay), Width = 220 });
        grid.RowPrePaint += (_, e) =>
        {
            if (e.RowIndex < 0 || grid.Rows[e.RowIndex].DataBoundItem is not QuoteGridRow row)
            {
                return;
            }

            if (row.IsCustomerHeader)
            {
                grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.FromArgb(226, 230, 236);
                grid.Rows[e.RowIndex].DefaultCellStyle.ForeColor = Color.Black;
                grid.Rows[e.RowIndex].DefaultCellStyle.Font = new Font(grid.Font, FontStyle.Bold);
                return;
            }

            if (row.RowType == QuoteGridRowType.QuoteDetail)
            {
                grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.FromArgb(214, 236, 255);
                grid.Rows[e.RowIndex].DefaultCellStyle.ForeColor = Color.Black;
                grid.Rows[e.RowIndex].DefaultCellStyle.Font = new Font(grid.Font, FontStyle.Bold);
                return;
            }

            var statusColor = row.Status switch
            {
                QuoteStatus.Won => Color.FromArgb(192, 255, 192),
                QuoteStatus.InProgress when row.DaysUntilExpiry <= NearExpiryThresholdDays => Color.FromArgb(255, 200, 200),
                QuoteStatus.InProgress => Color.FromArgb(255, 251, 184),
                _ => Color.White
            };
            grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = statusColor;
        };
    }

    private async Task CreateNewQuoteAsync()
    {
        var draft = new QuoteDraftForm(_quoteRepository, AuthorizationService.HasPermission(_currentUser, UserPermission.ViewPricing), _currentUser.Username);
        if (draft.ShowDialog(this) == DialogResult.OK)
        {
            _feedback.Text = $"Created quote {draft.CreatedQuoteId}.";
            await LoadActiveQuotesAsync();
        }
    }

    private async Task LoadActiveQuotesAsync()
    {
        try
        {
            await AutoArchiveExpiredQuotesAsync();

            var allQuotes = await _quoteRepository.GetQuotesAsync();
            var topQuotes = allQuotes.Where(q => q.Status != QuoteStatus.Expired).ToList();
            var expiredQuotes = allQuotes.Where(q => q.Status == QuoteStatus.Expired)
                .OrderByDescending(q => q.ExpiredUtc ?? q.LastUpdatedUtc)
                .Select(CreateQuoteRow)
                .ToList();

            await SavePendingExpandedQuoteChangesAsync();

            _quotesGrid.DataSource = BuildActiveViewRows(topQuotes);
            _expiredQuotesGrid.DataSource = expiredQuotes;
            _feedback.Text = $"Loaded {topQuotes.Count} active/finished quotes and {expiredQuotes.Count} archived quotes.";
        }
        catch (Exception ex)
        {
            _feedback.Text = $"Unable to load active quotes: {ex.Message}";
        }
    }

    private async Task OpenSelectedQuotePacketAsync()
    {
        if (TryGetSelectedQuoteId() is not int selectedId)
        {
            _feedback.Text = "Select a quote row first.";
            return;
        }

        var fullQuote = await _quoteRepository.GetQuoteAsync(selectedId);
        if (fullQuote is null)
        {
            _feedback.Text = $"Quote {selectedId} was not found.";
            return;
        }

        using var packetWindow = new QuotePacketForm(fullQuote);
        if (packetWindow.ShowDialog(this) == DialogResult.OK)
        {
            await _quoteRepository.SaveQuoteAsync(fullQuote);
            _feedback.Text = $"Quote packet for quote {fullQuote.Id} saved to database.";
            await LoadActiveQuotesAsync();
        }
        else
        {
            _feedback.Text = "Quote packet closed without saving.";
        }
    }

    private async Task EditSelectedQuoteAsync()
    {
        if (TryGetSelectedQuoteId() is not int selectedId)
        {
            _feedback.Text = "Select a quote row first.";
            return;
        }

        var fullQuote = await _quoteRepository.GetQuoteAsync(selectedId);
        if (fullQuote is null)
        {
            _feedback.Text = $"Quote {selectedId} was not found.";
            return;
        }

        if (fullQuote.Status != QuoteStatus.InProgress)
        {
            _feedback.Text = "Only in-process quotes can be edited from this screen.";
            return;
        }

        using var draft = new QuoteDraftForm(_quoteRepository, AuthorizationService.HasPermission(_currentUser, UserPermission.ViewPricing), _currentUser.Username, fullQuote);
        if (draft.ShowDialog(this) == DialogResult.OK)
        {
            _feedback.Text = draft.WasDeleted
                ? $"Quote {fullQuote.Id} deleted."
                : $"Quote {fullQuote.Id} updated.";
            await LoadActiveQuotesAsync();
        }
    }

    private async Task DeleteSelectedQuoteAsync()
    {
        if (TryGetSelectedQuoteId() is not int selectedId)
        {
            _feedback.Text = "Select a quote first.";
            return;
        }

        try
        {
            await _quoteRepository.DeleteQuoteAsync(selectedId);
            _feedback.Text = $"Quote {selectedId} deleted.";
            await LoadActiveQuotesAsync();
        }
        catch (Exception ex)
        {
            _feedback.Text = $"Unable to delete quote: {ex.Message}";
        }
    }

    private async Task PassSelectedToProductionAsync()
    {
        if (TryGetSelectedQuoteId() is not int selectedId)
        {
            _feedback.Text = "Select a quote first.";
            return;
        }

        var fullQuote = await _quoteRepository.GetQuoteAsync(selectedId);
        if (fullQuote is null)
        {
            _feedback.Text = "Quote could not be loaded.";
            return;
        }

        fullQuote.Status = QuoteStatus.Won;
        fullQuote.WonUtc = DateTime.UtcNow;
        await _quoteRepository.SaveQuoteAsync(fullQuote);

        var firstLine = fullQuote.LineItems.FirstOrDefault();
        await _productionRepository.SaveJobAsync(new ProductionJob
        {
            JobNumber = $"JOB-{fullQuote.Id:0000}",
            QuoteLifecycleId = fullQuote.LifecycleQuoteId,
            ProductName = firstLine?.Description ?? $"Quote {fullQuote.Id}",
            PlannedQuantity = (int)Math.Max(1, firstLine?.Quantity ?? 1),
            ProducedQuantity = 0,
            DueDateUtc = DateTime.UtcNow.AddDays(Math.Max(1, firstLine?.LeadTimeDays ?? 7)),
            SourceQuoteId = fullQuote.Id,
            Status = ProductionJobStatus.Planned
        });

        await _quoteRepository.ResetLastInteractionOnQuoteAsync(fullQuote.CustomerId);
        _feedback.Text = $"Quote {fullQuote.Id} passed to production and archived as won.";
        await LoadActiveQuotesAsync();
        _openSection("Production");
    }


    public async Task<bool> OpenFromDashboardAsync(int quoteId, bool openDetails)
    {
        await LoadActiveQuotesAsync();

        var selected = SelectQuoteRow(quoteId);
        if (!selected)
        {
            _feedback.Text = $"Quote {quoteId} is not currently in the active queue.";
            return false;
        }

        if (!openDetails)
        {
            _feedback.Text = $"Quote {quoteId} selected from dashboard snapshot.";
            return true;
        }

        await OpenSelectedQuotePacketAsync();
        return true;
    }

    private bool SelectQuoteRow(int quoteId)
    {
        foreach (DataGridViewRow row in _quotesGrid.Rows)
        {
            if (row.DataBoundItem is QuoteGridRow quoteRow && quoteRow.QuoteId == quoteId)
            {
                row.Selected = true;
                _quotesGrid.CurrentCell = row.Cells[0];
                _quotesGrid.FirstDisplayedScrollingRowIndex = row.Index;
                return true;
            }
        }

        return false;
    }

    public Task RefreshDataAsync(bool fromFailSafeCheckpoint) => LoadActiveQuotesAsync();

    private async Task AutoArchiveExpiredQuotesAsync()
    {
        var inProgress = await _quoteRepository.GetQuotesByStatusAsync(QuoteStatus.InProgress);
        var cutoff = DateTime.UtcNow.AddDays(-QuoteExpiryDays);

        foreach (var quote in inProgress.Where(q => q.CreatedUtc <= cutoff))
        {
            await _quoteRepository.UpdateStatusAsync(quote.Id, QuoteStatus.Expired, "system.expiration");
        }
    }

    private List<QuoteGridRow> BuildActiveViewRows(IReadOnlyCollection<Quote> quotes)
    {
        var rows = new List<QuoteGridRow>();
        var quotesByCustomer = quotes
            .GroupBy(q => string.IsNullOrWhiteSpace(q.CustomerName) ? "Unknown Customer" : q.CustomerName)
            .OrderBy(g => g.Key);

        foreach (var customerGroup in quotesByCustomer)
        {
            var openQuoteCount = customerGroup.Count(q => q.Status == QuoteStatus.InProgress);
            var totalQuoteCount = customerGroup.Count();

            rows.Add(new QuoteGridRow
            {
                IsCustomerHeader = true,
                RowType = QuoteGridRowType.CustomerHeader,
                CustomerGroupKey = customerGroup.Key,
                ExpanderDisplay = _expandedCustomers.Contains(customerGroup.Key) ? "▾" : "▸",
                CustomerDisplay = customerGroup.Key.ToUpperInvariant(),
                StatusDisplay = $"OPEN: {openQuoteCount} / TOTAL: {totalQuoteCount}",
                LifecycleStageDisplay = "ACCOUNT SOCKET"
            });

            if (_expandedCustomers.Contains(customerGroup.Key))
            {
                rows.AddRange(BuildQuoteRows(customerGroup
                    .OrderByDescending(q => q.CreatedUtc)
                    .Select(CreateQuoteRow)
                    .Select(row =>
                    {
                        row.CustomerDisplay = $"   ↳ {row.CustomerDisplay}";
                        return row;
                    })));
            }
        }

        return rows;
    }

    private static QuoteGridRow CreateQuoteRow(Quote quote)
    {
        var elapsed = DateTime.UtcNow - quote.CreatedUtc;
        return new QuoteGridRow
        {
            QuoteId = quote.Id,
            RowType = QuoteGridRowType.QuoteSummary,
            ExpanderDisplay = "▸",
            QuoteIdDisplay = quote.Id.ToString(),
            CustomerDisplay = quote.CustomerName.ToUpperInvariant(),
            Status = quote.Status,
            StatusDisplay = quote.Status.ToString().ToUpperInvariant(),
            LifecycleStageDisplay = quote.Status switch
            {
                QuoteStatus.InProgress => "CREATED / UNFINISHED",
                QuoteStatus.Won => "FINISHED / PASSED",
                QuoteStatus.Lost => "FINISHED / LOST",
                QuoteStatus.Expired => "EXPIRED",
                _ => "UNKNOWN"
            },
            QuotedAtDisplay = quote.CreatedUtc.ToLocalTime().ToString("g"),
            TimeSinceQuotedDisplay = $"{elapsed.Days} DAYS ({Math.Max(0, elapsed.Hours)}H)",
            DaysUntilExpiry = Math.Max(0, QuoteExpiryDays - elapsed.Days)
        };
    }

    private IEnumerable<QuoteGridRow> BuildQuoteRows(IEnumerable<QuoteGridRow> quoteRows)
    {
        foreach (var row in quoteRows)
        {
            row.ExpanderDisplay = row.QuoteId.HasValue && _expandedQuotes.Contains(row.QuoteId.Value) ? "▾" : "▸";
            yield return row;

            if (row.QuoteId is int quoteId && _expandedQuotes.Contains(quoteId))
            {
                yield return BuildQuoteDetailRow(quoteId);
            }
        }
    }

    private QuoteGridRow BuildQuoteDetailRow(int quoteId)
    {
        _expandedQuoteCache.TryGetValue(quoteId, out var quote);
        quote ??= new Quote();

        return new QuoteGridRow
        {
            RowType = QuoteGridRowType.QuoteDetail,
            QuoteId = quoteId,
            ExpanderDisplay = string.Empty,
            QuoteIdDisplay = $"DETAILS: {quoteId}",
            CustomerDisplay = quote.CustomerName.ToUpperInvariant(),
            StatusDisplay = quote.Status.ToString().ToUpperInvariant(),
            LifecycleStageDisplay = quote.LifecycleQuoteId.ToUpperInvariant(),
            QuotedAtDisplay = quote.CreatedUtc == default ? string.Empty : quote.CreatedUtc.ToLocalTime().ToString("g").ToUpperInvariant(),
            TimeSinceQuotedDisplay = "EDIT CUSTOMER / STATUS / LIFECYCLE HERE"
        };
    }

    private async Task HandleQuotesGridContentClickAsync(DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        if (_quotesGrid.Columns[e.ColumnIndex].Name != "Expander" || _quotesGrid.Rows[e.RowIndex].DataBoundItem is not QuoteGridRow row)
        {
            return;
        }

        if (row.RowType == QuoteGridRowType.CustomerHeader && !string.IsNullOrWhiteSpace(row.CustomerGroupKey))
        {
            if (!_expandedCustomers.Add(row.CustomerGroupKey))
            {
                _expandedCustomers.Remove(row.CustomerGroupKey);
            }

            await LoadActiveQuotesAsync();
            return;
        }

        if (row.RowType != QuoteGridRowType.QuoteSummary || row.QuoteId is not int quoteId)
        {
            return;
        }

        if (_expandedQuotes.Contains(quoteId))
        {
            await SaveExpandedQuoteChangesAsync(quoteId);
            _expandedQuotes.Remove(quoteId);
        }
        else
        {
            var quote = await _quoteRepository.GetQuoteAsync(quoteId);
            if (quote is null)
            {
                _feedback.Text = $"QUOTE {quoteId} COULD NOT BE LOADED.";
                return;
            }

            _expandedQuoteCache[quoteId] = quote;
            _expandedQuotes.Add(quoteId);
        }

        await LoadActiveQuotesAsync();
    }

    private void HandleQuotesGridCellBeginEdit(object? sender, DataGridViewCellCancelEventArgs e)
    {
        if (e.RowIndex < 0 || _quotesGrid.Rows[e.RowIndex].DataBoundItem is not QuoteGridRow row)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = row.RowType != QuoteGridRowType.QuoteDetail || !IsEditableDetailColumn(_quotesGrid.Columns[e.ColumnIndex].Name);
    }

    private void HandleQuotesGridCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _quotesGrid.Rows[e.RowIndex].DataBoundItem is not QuoteGridRow row || row.QuoteId is not int quoteId)
        {
            return;
        }

        if (!_expandedQuoteCache.TryGetValue(quoteId, out var quote))
        {
            return;
        }

        var columnName = _quotesGrid.Columns[e.ColumnIndex].Name;
        var value = _quotesGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString()?.Trim() ?? string.Empty;
        switch (columnName)
        {
            case "Customer":
                quote.CustomerName = value;
                break;
            case "Status":
                if (Enum.TryParse<QuoteStatus>(value, true, out var status))
                {
                    quote.Status = status;
                }
                break;
            case "Lifecycle":
                quote.LifecycleQuoteId = value;
                break;
        }
    }

    private static bool IsEditableDetailColumn(string? columnName)
        => columnName is "Customer" or "Status" or "Lifecycle";

    private async Task SaveExpandedQuoteChangesAsync(int quoteId)
    {
        if (!_expandedQuoteCache.TryGetValue(quoteId, out var quote))
        {
            return;
        }

        quote.LastUpdatedUtc = DateTime.UtcNow;
        await _quoteRepository.SaveQuoteAsync(quote);
    }

    private async Task SavePendingExpandedQuoteChangesAsync()
    {
        foreach (var quoteId in _expandedQuotes.ToList())
        {
            await SaveExpandedQuoteChangesAsync(quoteId);
        }
    }

    private int? TryGetSelectedQuoteId()
    {
        if (_quotesGrid.CurrentRow?.DataBoundItem is QuoteGridRow { IsCustomerHeader: false, QuoteId: int id })
        {
            return id;
        }

        return null;
    }

    private sealed class QuoteGridRow
    {
        public QuoteGridRowType RowType { get; init; }
        public int? QuoteId { get; init; }
        public string CustomerGroupKey { get; init; } = string.Empty;
        public string ExpanderDisplay { get; set; } = string.Empty;
        public string QuoteIdDisplay { get; init; } = string.Empty;
        public string CustomerDisplay { get; set; } = string.Empty;
        public QuoteStatus Status { get; init; }
        public string StatusDisplay { get; init; } = string.Empty;
        public string LifecycleStageDisplay { get; init; } = string.Empty;
        public string QuotedAtDisplay { get; init; } = string.Empty;
        public string TimeSinceQuotedDisplay { get; init; } = string.Empty;
        public bool IsCustomerHeader { get; init; }
        public int DaysUntilExpiry { get; init; }
    }

    private enum QuoteGridRowType
    {
        QuoteSummary,
        QuoteDetail,
        CustomerHeader
    }

}
