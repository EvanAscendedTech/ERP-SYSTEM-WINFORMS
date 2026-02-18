using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Forms;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;
using System.Text;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ERPSystem.WinForms.Controls;

public class QuotesControl : UserControl, IRealtimeDataControl
{
    private const int QuoteExpiryDays = 60;
    private const int NearExpiryThresholdDays = 2;

    private readonly QuoteRepository _quoteRepository;
    private readonly ProductionRepository _productionRepository;
    private readonly UserManagementRepository _userRepository;
    private readonly Action<string> _openSection;
    private readonly UserAccount _currentUser;
    private readonly FlowLayoutPanel _customerCardsPanel = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8, 4, 8, 8) };
    private readonly Label _customerHubLabel = new() { Dock = DockStyle.Top, Height = 30, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Padding = new Padding(8, 0, 0, 0) };
    private readonly DataGridView _quotesGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly DataGridView _completedQuotesGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly DataGridView _expiredQuotesGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly Label _feedback = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _markWonButton;
    private readonly Button _passToPurchasingButton;
    private List<Quote> _activeQuotesCache = new();
    private string? _selectedCustomer;
    private int _cascadeWindowCounter;

    public QuotesControl(QuoteRepository quoteRepository, ProductionRepository productionRepository, UserManagementRepository userRepository, UserAccount currentUser, Action<string> openSection)
    {
        _quoteRepository = quoteRepository;
        _productionRepository = productionRepository;
        _userRepository = userRepository;
        _openSection = openSection;
        _currentUser = currentUser;
        Dock = DockStyle.Fill;

        ConfigureQuotesGrid(_quotesGrid, includeLifecycleColumn: true);
        ConfigureCompletedQuotesGrid(_completedQuotesGrid);
        ConfigureQuotesGrid(_expiredQuotesGrid, includeLifecycleColumn: false);

        var actionsPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
        var refreshButton = new Button { Text = "Refresh Active Quotes", AutoSize = true };
        var newQuoteButton = new Button { Text = "Create New Quote", AutoSize = true };
        var deleteQuoteButton = new Button { Text = "Delete Quote", AutoSize = true };
        var markCompletedButton = new Button { Text = "Mark as Completed", AutoSize = true };
        _markWonButton = new Button { Text = "Mark as Won", AutoSize = true };
        _passToPurchasingButton = new Button { Text = "Pass to Purchasing", AutoSize = true, Enabled = false };
        var archivedQuotesButton = new Button { Text = "Archived Quotes", AutoSize = true };

        refreshButton.Click += async (_, _) => await LoadActiveQuotesAsync();
        newQuoteButton.Click += async (_, _) => await CreateNewQuoteAsync();
        deleteQuoteButton.Click += async (_, _) => await DeleteSelectedQuoteAsync();
        markCompletedButton.Click += async (_, _) => await MarkSelectedCompletedAsync();
        _markWonButton.Click += async (_, _) => await MarkSelectedWonAsync();
        _passToPurchasingButton.Click += async (_, _) => await PassSelectedToPurchasingAsync();
        _quotesGrid.SelectionChanged += (_, _) => RefreshActionStateForSelection();
        archivedQuotesButton.Click += (_, _) => OpenArchivedQuotesWindow();
        _quotesGrid.CellDoubleClick += async (_, _) => await OpenSelectedQuoteDetailsAsync();
        _completedQuotesGrid.CellClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                OpenCompletedQuoteCascadeFromRow(e.RowIndex);
            }
        };

        actionsPanel.Controls.Add(refreshButton);
        actionsPanel.Controls.Add(newQuoteButton);
        actionsPanel.Controls.Add(deleteQuoteButton);
        actionsPanel.Controls.Add(markCompletedButton);
        actionsPanel.Controls.Add(_markWonButton);
        actionsPanel.Controls.Add(_passToPurchasingButton);

        var bottomRightPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        bottomRightPanel.Controls.Add(archivedQuotesButton);

        var topContent = new Panel { Dock = DockStyle.Fill };
        topContent.Controls.Add(_quotesGrid);
        topContent.Controls.Add(new Panel
        {
            Dock = DockStyle.Top,
            Height = 164,
            Controls =
            {
                _customerCardsPanel,
                _customerHubLabel
            }
        });
        topContent.Controls.Add(actionsPanel);

        var completedWorkPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 6) };
        completedWorkPanel.Controls.Add(_completedQuotesGrid);
        completedWorkPanel.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = "Completed Quotes",
            Font = new Font(Font, FontStyle.Bold)
        });

        var archivePanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) };
        archivePanel.Controls.Add(_expiredQuotesGrid);
        archivePanel.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = "Expired Quotes Archive (60+ days)",
            Font = new Font(Font, FontStyle.Bold)
        });

        var lowerPanel = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.None,
            SplitterDistance = 180,
            Panel1MinSize = 120,
            Panel2MinSize = 120
        };
        lowerPanel.Panel1.Controls.Add(completedWorkPanel);
        lowerPanel.Panel2.Controls.Add(archivePanel);

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
        split.Panel2.Controls.Add(lowerPanel);

        Controls.Add(split);
        Controls.Add(bottomRightPanel);
        Controls.Add(_feedback);

        _ = LoadActiveQuotesAsync();
    }

    private void OpenArchivedQuotesWindow()
    {
        using var archived = new ArchivedQuotesForm(_quoteRepository);
        archived.ShowDialog(this);
    }

    private void ConfigureQuotesGrid(DataGridView grid, bool includeLifecycleColumn)
    {
        grid.DefaultCellStyle.Font = new Font(grid.Font, FontStyle.Bold);
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "QuoteId", HeaderText = "Quote #", DataPropertyName = nameof(QuoteGridRow.QuoteIdDisplay), Width = 90, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Customer", HeaderText = "Customer", DataPropertyName = nameof(QuoteGridRow.CustomerDisplay), Width = 240, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", DataPropertyName = nameof(QuoteGridRow.StatusDisplay), Width = 110, ReadOnly = true });
        if (includeLifecycleColumn)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Lifecycle", HeaderText = "Lifecycle Stage", DataPropertyName = nameof(QuoteGridRow.LifecycleStageDisplay), Width = 190, ReadOnly = true });
        }

        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "QuotedAt", HeaderText = "Quoted", DataPropertyName = nameof(QuoteGridRow.QuotedAtDisplay), Width = 180, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "TimeSinceQuoted", HeaderText = "Timeframe Since Quoted", DataPropertyName = nameof(QuoteGridRow.TimeSinceQuotedDisplay), Width = 220, ReadOnly = true });
        grid.RowPrePaint += (_, e) =>
        {
            if (e.RowIndex < 0 || grid.Rows[e.RowIndex].DataBoundItem is not QuoteGridRow row)
            {
                return;
            }

            var statusColor = row.Status switch
            {
                QuoteStatus.Won => Color.FromArgb(192, 255, 192),
                QuoteStatus.Completed => Color.FromArgb(225, 205, 255),
                QuoteStatus.Lost => Color.FromArgb(255, 200, 200),
                QuoteStatus.InProgress when row.DaysUntilExpiry <= NearExpiryThresholdDays => Color.FromArgb(255, 200, 200),
                QuoteStatus.InProgress => Color.FromArgb(255, 251, 184),
                _ => Color.White
            };
            grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = statusColor;
        };
    }


    private void ConfigureCompletedQuotesGrid(DataGridView grid)
    {
        grid.Columns.Clear();
        grid.DefaultCellStyle.Font = new Font(grid.Font, FontStyle.Bold);
        var expandColumn = new DataGridViewButtonColumn
        {
            Name = "Expand",
            HeaderText = string.Empty,
            Width = 44,
            Text = "▼",
            UseColumnTextForButtonValue = true,
            FlatStyle = FlatStyle.Flat
        };
        grid.Columns.Add(expandColumn);
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "QuoteId", HeaderText = "Quote #", DataPropertyName = nameof(QuoteGridRow.QuoteIdDisplay), Width = 90, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Customer", HeaderText = "Customer", DataPropertyName = nameof(QuoteGridRow.CustomerDisplay), Width = 220, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", DataPropertyName = nameof(QuoteGridRow.StatusDisplay), Width = 110, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Lifecycle", HeaderText = "Lifecycle Stage", DataPropertyName = nameof(QuoteGridRow.LifecycleStageDisplay), Width = 180, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "QuotedAt", HeaderText = "Quoted", DataPropertyName = nameof(QuoteGridRow.QuotedAtDisplay), Width = 160, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "TimeSinceQuoted", HeaderText = "Time Since Quoted", DataPropertyName = nameof(QuoteGridRow.TimeSinceQuotedDisplay), Width = 170, ReadOnly = true });

        grid.RowPrePaint += (_, e) =>
        {
            if (e.RowIndex < 0 || grid.Rows[e.RowIndex].DataBoundItem is not QuoteGridRow row)
            {
                return;
            }

            var statusColor = row.Status == QuoteStatus.Completed
                ? Color.FromArgb(225, 205, 255)
                : Color.White;
            grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = statusColor;
        };
    }

    private async Task CreateNewQuoteAsync()
    {
        var draft = new QuoteDraftForm(_quoteRepository, AuthorizationService.HasPermission(_currentUser, UserPermission.ViewPricing), _currentUser.Username);
        if (draft.ShowDialog(this) == DialogResult.OK)
        {
            _feedback.Text = $"Created quote {draft.CreatedQuoteId}.";
            await LogAuditAsync("Quotes", "Created quote", $"Quote #{draft.CreatedQuoteId} created.");
            await LoadActiveQuotesAsync();
        }
    }

    private async Task LoadActiveQuotesAsync()
    {
        try
        {
            await AutoMoveStaleQuotesToLostAsync();
            await AutoArchiveExpiredQuotesAsync();

            var allQuotes = await _quoteRepository.GetQuotesAsync();
            _activeQuotesCache = allQuotes.Where(q => q.Status != QuoteStatus.Expired && !q.PassedToPurchasingUtc.HasValue).ToList();
            var expiredQuotes = allQuotes.Where(q => q.Status == QuoteStatus.Expired)
                .OrderByDescending(q => q.ExpiredUtc ?? q.LastUpdatedUtc)
                .Select(CreateQuoteRow)
                .ToList();

            RefreshActiveQuotesView();
            _expiredQuotesGrid.DataSource = expiredQuotes;
            RefreshActionStateForSelection();
            _feedback.Text = $"Loaded {_activeQuotesCache.Count} quotes and {expiredQuotes.Count} archived quotes.";
        }
        catch (Exception ex)
        {
            _feedback.Text = $"Unable to load active quotes: {ex.Message}";
        }
    }

    private async Task OpenSelectedQuoteDetailsAsync()
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

        if (fullQuote.Status is QuoteStatus.Lost or QuoteStatus.Expired)
        {
            _feedback.Text = "Only active quotes (Open/In-Process, Completed, or Won) can be opened from this screen.";
            return;
        }

        await LogAuditAsync("Quotes", "Reviewed quote", $"Opened quote #{fullQuote.Id} for review/edit.");
        using var draft = new QuoteDraftForm(_quoteRepository, AuthorizationService.HasPermission(_currentUser, UserPermission.ViewPricing), _currentUser.Username, fullQuote);
        if (draft.ShowDialog(this) == DialogResult.OK)
        {
            _feedback.Text = draft.WasDeleted
                ? $"Quote {fullQuote.Id} deleted."
                : $"Quote {fullQuote.Id} updated.";
            await LogAuditAsync("Quotes", draft.WasDeleted ? "Deleted quote" : "Updated quote", $"Quote #{fullQuote.Id} {(draft.WasDeleted ? "deleted" : "updated")}.");
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

        var selected = await _quoteRepository.GetQuoteAsync(selectedId);
        if (selected is null)
        {
            _feedback.Text = $"Quote {selectedId} no longer exists.";
            return;
        }

        var quoteIdentifier = string.IsNullOrWhiteSpace(selected.LifecycleQuoteId)
            ? $"Quote #{selected.Id}"
            : selected.LifecycleQuoteId;

        var confirm = MessageBox.Show(
            $"Are you sure you want to delete {quoteIdentifier} for {selected.CustomerName}?",
            "Delete Quote",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (confirm != DialogResult.Yes)
        {
            _feedback.Text = "Quote deletion canceled.";
            return;
        }

        try
        {
            await _quoteRepository.DeleteQuoteAsync(selected.Id);
            await LogAuditAsync("Quotes", "Deleted quote", $"Quote #{selected.Id} deleted.");
            _feedback.Text = $"Quote {selected.Id} deleted.";
            await LoadActiveQuotesAsync();
        }
        catch (Exception ex)
        {
            _feedback.Text = $"Unable to delete quote: {ex.Message}";
        }
    }


    private async Task MarkSelectedCompletedAsync()
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

        if (fullQuote.Status != QuoteStatus.InProgress)
        {
            _feedback.Text = $"Only InProgress quotes can be marked Completed. Current status: {fullQuote.Status}.";
            return;
        }

        var result = await _quoteRepository.UpdateStatusAsync(selectedId, QuoteStatus.Completed, _currentUser.Username);
        _feedback.Text = result.Success
            ? $"Quote {selectedId} moved to Completed and is now awaiting customer confirmation."
            : result.Message;

        await LoadActiveQuotesAsync();
    }

    private async Task MarkSelectedWonAsync()
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

        if (fullQuote.Status != QuoteStatus.Completed)
        {
            _feedback.Text = $"Only Completed quotes can be moved to Won. Current status: {fullQuote.Status}.";
            return;
        }

        var result = await _quoteRepository.UpdateStatusAsync(selectedId, QuoteStatus.Won, _currentUser.Username);
        _feedback.Text = result.Success
            ? $"Quote {selectedId} moved to Won and can now be passed to Purchasing."
            : result.Message;

        await LoadActiveQuotesAsync();
        SelectQuoteRow(selectedId);
        RefreshActionStateForSelection();
    }

    private async Task PassSelectedToPurchasingAsync()
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

        if (fullQuote.Status != QuoteStatus.Won)
        {
            _feedback.Text = $"Only Won quotes can be passed to Purchasing. Current status: {fullQuote.Status}.";
            return;
        }

        _selectedCustomer = null;

        var result = await _quoteRepository.PassToPurchasingAsync(fullQuote.Id, _currentUser.Username);
        if (!result.Success)
        {
            _feedback.Text = result.Message;
            return;
        }

        await _quoteRepository.ResetLastInteractionOnQuoteAsync(fullQuote.CustomerId);
        _feedback.Text = result.Message;
        await LoadActiveQuotesAsync();
        _openSection("Purchasing");
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

        await OpenSelectedQuoteDetailsAsync();
        return true;
    }

    private bool SelectQuoteRow(int quoteId)
    {
        var quote = _activeQuotesCache.FirstOrDefault(q => q.Id == quoteId);
        if (quote is not null)
        {
            _selectedCustomer = NormalizeCustomerName(quote.CustomerName);
            RefreshActiveQuotesView();
        }

        foreach (DataGridViewRow row in _quotesGrid.Rows)
        {
            if (row.DataBoundItem is QuoteGridRow quoteRow && quoteRow.QuoteId == quoteId)
            {
                row.Selected = true;
                _quotesGrid.CurrentCell = row.Cells[0];
                _quotesGrid.FirstDisplayedScrollingRowIndex = row.Index;
                RefreshActionStateForSelection();
                return true;
            }
        }

        return false;
    }

    public Task RefreshDataAsync(bool fromFailSafeCheckpoint) => LoadActiveQuotesAsync();

    private async Task AutoMoveStaleQuotesToLostAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-QuoteExpiryDays);
        var completed = await _quoteRepository.GetQuotesByStatusAsync(QuoteStatus.Completed);
        var won = await _quoteRepository.GetQuotesByStatusAsync(QuoteStatus.Won);

        foreach (var quote in completed.Concat(won).Where(q => q.LastUpdatedUtc <= cutoff))
        {
            await _quoteRepository.UpdateStatusAsync(quote.Id, QuoteStatus.Lost, "system.expiration");
        }
    }

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
        return quotes
            .OrderByDescending(q => q.CreatedUtc)
            .Select(CreateQuoteRow)
            .ToList();
    }

    private static QuoteGridRow CreateQuoteRow(Quote quote)
    {
        var elapsed = DateTime.UtcNow - quote.CreatedUtc;
        return new QuoteGridRow
        {
            QuoteId = quote.Id,
            RowType = QuoteGridRowType.QuoteSummary,
            QuoteIdDisplay = quote.Id.ToString(),
            CustomerDisplay = quote.CustomerName.ToUpperInvariant(),
            Status = quote.Status,
            StatusDisplay = quote.Status.ToString().ToUpperInvariant(),
            LifecycleStageDisplay = quote.Status switch
            {
                QuoteStatus.InProgress => "CREATED / UNFINISHED",
                QuoteStatus.Completed => "FINISHED / COMPLETED",
                QuoteStatus.Won => "CUSTOMER CONFIRMED / READY FOR PURCHASING",
                QuoteStatus.Lost => "FINISHED / LOST",
                QuoteStatus.Expired => "EXPIRED",
                _ => "UNKNOWN"
            },
            QuotedAtDisplay = quote.CreatedUtc.ToLocalTime().ToString("g"),
            TimeSinceQuotedDisplay = $"{elapsed.Days} DAYS ({Math.Max(0, elapsed.Hours)}H)",
            DaysUntilExpiry = Math.Max(0, QuoteExpiryDays - elapsed.Days)
        };
    }

    private void RefreshActiveQuotesView()
    {
        var customerGroups = _activeQuotesCache
            .Where(q => q.Status is QuoteStatus.InProgress or QuoteStatus.Completed)
            .GroupBy(q => NormalizeCustomerName(q.CustomerName))
            .OrderBy(g => g.Key)
            .ToList();

        if (string.IsNullOrWhiteSpace(_selectedCustomer) && customerGroups.Count > 0)
        {
            _selectedCustomer = customerGroups[0].Key;
        }
        else if (!string.IsNullOrWhiteSpace(_selectedCustomer)
            && customerGroups.All(group => !string.Equals(group.Key, _selectedCustomer, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedCustomer = customerGroups.FirstOrDefault()?.Key;
        }

        BuildCustomerCards();

        var selectedCustomerInProgressQuotes = string.IsNullOrWhiteSpace(_selectedCustomer)
            ? Array.Empty<Quote>()
            : _activeQuotesCache
                .Where(q => q.Status == QuoteStatus.InProgress
                    && string.Equals(NormalizeCustomerName(q.CustomerName), _selectedCustomer, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        var selectedCustomerCompletedQuotes = string.IsNullOrWhiteSpace(_selectedCustomer)
            ? Array.Empty<Quote>()
            : _activeQuotesCache
                .Where(q => q.Status == QuoteStatus.Completed
                    && string.Equals(NormalizeCustomerName(q.CustomerName), _selectedCustomer, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        _quotesGrid.DataSource = BuildActiveViewRows(selectedCustomerInProgressQuotes);
        _completedQuotesGrid.DataSource = BuildActiveViewRows(selectedCustomerCompletedQuotes);
        RefreshActionStateForSelection();
        _customerHubLabel.Text = string.IsNullOrWhiteSpace(_selectedCustomer)
            ? "Customer Hub — pick a customer card to drill into quote details"
            : $"Customer Hub / {_selectedCustomer}";
    }


    private void RefreshActionStateForSelection()
    {
        if (_quotesGrid.CurrentRow?.DataBoundItem is not QuoteGridRow row)
        {
            _markWonButton.Enabled = false;
            _passToPurchasingButton.Enabled = false;
            return;
        }

        var hasQuote = row.QuoteId.HasValue;
        _markWonButton.Enabled = hasQuote && row.Status == QuoteStatus.Completed;
        _passToPurchasingButton.Enabled = hasQuote && row.Status == QuoteStatus.Won;
    }

    private void BuildCustomerCards()
    {
        _customerCardsPanel.SuspendLayout();
        _customerCardsPanel.Controls.Clear();

        var customerGroups = _activeQuotesCache
            .Where(q => q.Status is QuoteStatus.InProgress or QuoteStatus.Completed)
            .GroupBy(q => NormalizeCustomerName(q.CustomerName))
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var customerGroup in customerGroups)
        {
            var inProgressCount = customerGroup.Count(q => q.Status == QuoteStatus.InProgress);
            var completedCount = customerGroup.Count(q => q.Status == QuoteStatus.Completed);
            _customerCardsPanel.Controls.Add(CreateCustomerCard(customerGroup.Key, inProgressCount, completedCount));
        }

        _customerCardsPanel.Invalidate(true);

        _customerCardsPanel.ResumeLayout();
    }

    private Control CreateCustomerCard(string customerName, int inProgressCount, int completedCount)
    {
        var card = new CustomerCardPanel
        {
            Width = 280,
            Height = 94,
            Margin = new Padding(0, 0, 12, 0),
            FillColor = ResolveCustomerCardColor(customerName),
            Cursor = Cursors.Hand
        };

        var cardLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = Color.Transparent
        };
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 44));

        var titleLabel = new Label
        {
            Text = customerName,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 8, 12, 0),
            Font = new Font(Font, FontStyle.Bold),
            AutoEllipsis = true
        };

        var statsLabel = new Label
        {
            Text = $"In Progress: {inProgressCount}   Completed: {completedCount}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 12, 8),
            ForeColor = Color.FromArgb(48, 64, 85),
            Font = new Font(Font.FontFamily, Font.Size - 0.2f, FontStyle.Regular)
        };

        void selectCustomer(object? _, EventArgs __)
        {
            _selectedCustomer = customerName;
            RefreshActiveQuotesView();
        }

        card.Click += selectCustomer;
        titleLabel.Click += selectCustomer;
        statsLabel.Click += selectCustomer;
        cardLayout.Click += selectCustomer;
        cardLayout.Controls.Add(titleLabel, 0, 0);
        cardLayout.Controls.Add(statsLabel, 0, 1);
        card.Controls.Add(cardLayout);
        return card;
    }

    private void OpenCompletedQuoteCascadeFromRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _completedQuotesGrid.Rows.Count)
        {
            return;
        }

        if (_completedQuotesGrid.Rows[rowIndex].DataBoundItem is not QuoteGridRow selectedRow || selectedRow.QuoteId is not int quoteId)
        {
            return;
        }

        var quote = _activeQuotesCache.FirstOrDefault(q => q.Id == quoteId && q.Status == QuoteStatus.Completed);
        if (quote is null)
        {
            return;
        }

        OpenLineItemCascadeWindow(quote);
    }

    private void OpenLineItemCascadeWindow(Quote quote)
    {
        using var lineItemWindow = new Form
        {
            Text = $"Quote #{quote.Id} Line Items",
            Width = 980,
            Height = 620,
            StartPosition = FormStartPosition.Manual,
            MinimizeBox = false,
            MaximizeBox = true
        };

        var owner = FindForm();
        var offset = (_cascadeWindowCounter % 8) * 24;
        _cascadeWindowCounter++;
        if (owner is not null)
        {
            lineItemWindow.Location = new Point(owner.Location.X + 60 + offset, owner.Location.Y + 60 + offset);
        }

        var body = CreateLineItemsNestedTable(quote.LineItems);
        body.Dock = DockStyle.Top;
        body.Margin = new Padding(0);

        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(8)
        };
        scrollHost.Controls.Add(body);
        lineItemWindow.Controls.Add(scrollHost);

        if (owner is null)
        {
            lineItemWindow.ShowDialog();
            return;
        }

        lineItemWindow.ShowDialog(owner);
    }

    private static Control CreateLineItemsNestedTable(IReadOnlyList<QuoteLineItem> lineItems)
    {
        var container = new Panel
        {
            Width = 772,
            AutoSize = false,
            Height = lineItems.Count == 0 ? 40 : (lineItems.Count * 184) + 36,
            Padding = new Padding(8),
            BackColor = Color.FromArgb(248, 251, 255)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = lineItems.Count + 1,
            AutoSize = false
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        AddHeaderLabel(layout, "Line Item", 0);
        AddHeaderLabel(layout, "Qty", 1);
        AddHeaderLabel(layout, "Line Total", 2);
        AddHeaderLabel(layout, "3D Model (STEP)", 3);

        if (lineItems.Count == 0)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            var noItems = new Label { Text = "No line items.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray };
            layout.Controls.Add(noItems, 0, 1);
            layout.SetColumnSpan(noItems, 4);
        }
        else
        {
            for (var index = 0; index < lineItems.Count; index++)
            {
                var lineItem = lineItems[index];
                var rowIndex = index + 1;
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));

                layout.Controls.Add(CreateBodyLabel($"{lineItem.DrawingNumber} {lineItem.Description}"), 0, rowIndex);
                layout.Controls.Add(CreateBodyLabel(lineItem.Quantity.ToString("0.##")), 1, rowIndex);
                layout.Controls.Add(CreateBodyLabel(lineItem.LineItemTotal.ToString("C2")), 2, rowIndex);
                layout.Controls.Add(CreateModelCell(lineItem), 3, rowIndex);
            }
        }

        container.Controls.Add(layout);
        return container;
    }

    private static Control CreateModelCell(QuoteLineItem lineItem)
    {
        var stepAttachment = lineItem.BlobAttachments.FirstOrDefault(blob =>
            blob.BlobType == QuoteBlobType.ThreeDModel
            && (blob.Extension.Equals(".step", StringComparison.OrdinalIgnoreCase)
                || blob.Extension.Equals(".stp", StringComparison.OrdinalIgnoreCase)
                || blob.FileName.EndsWith(".step", StringComparison.OrdinalIgnoreCase)
                || blob.FileName.EndsWith(".stp", StringComparison.OrdinalIgnoreCase))
            && blob.BlobData.Length > 0);

        if (stepAttachment is null)
        {
            return new Label
            {
                Text = "No STEP file",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.DimGray
            };
        }

        var viewer = new StepModelViewerControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(4)
        };
        viewer.LoadStep(stepAttachment.BlobData);
        return viewer;
    }

    private static Label CreateBodyLabel(string value)
        => new()
        {
            Text = value,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 6, 0),
            AutoEllipsis = true
        };

    private static void AddHeaderLabel(TableLayoutPanel table, string text, int columnIndex)
    {
        table.Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 2, 0)
        }, columnIndex, 0);
    }

    private Color ResolveCustomerCardColor(string customerName)
    {
        var palette = new[]
        {
            Color.FromArgb(229, 241, 255),
            Color.FromArgb(232, 246, 235),
            Color.FromArgb(255, 242, 224),
            Color.FromArgb(243, 235, 255),
            Color.FromArgb(255, 236, 240),
            Color.FromArgb(237, 246, 248)
        };

        var hash = Math.Abs(customerName.GetHashCode());
        var baseColor = palette[hash % palette.Length];
        return string.Equals(_selectedCustomer, customerName, StringComparison.OrdinalIgnoreCase)
            ? ControlPaint.Light(baseColor, 0.10f)
            : baseColor;
    }

    private sealed class CustomerCardPanel : Panel
    {
        private const int CornerRadius = 14;
        private Color _fillColor = Color.FromArgb(236, 240, 245);

        public Color FillColor
        {
            get => _fillColor;
            set
            {
                _fillColor = value;
                Invalidate();
            }
        }

        public CustomerCardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Padding = new Padding(1);
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            UpdateRoundedRegion();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateRoundedRegion();
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var cardPath = CreateRoundedRectanglePath(ClientRectangle, CornerRadius);

            using var fillBrush = new SolidBrush(FillColor);
            e.Graphics.FillPath(fillBrush, cardPath);

            var topHighlight = Rectangle.Inflate(ClientRectangle, -1, -1);
            topHighlight.Height = Math.Max(18, topHighlight.Height / 3);
            using var highlightPath = CreateRoundedRectanglePath(topHighlight, CornerRadius - 1);
            using var highlightBrush = new LinearGradientBrush(topHighlight, Color.FromArgb(105, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.Vertical);
            e.Graphics.FillPath(highlightBrush, highlightPath);

            using var outerPen = new Pen(Color.FromArgb(145, Color.White), 1.2f);
            e.Graphics.DrawPath(outerPen, cardPath);

            var innerBounds = Rectangle.Inflate(ClientRectangle, -1, -1);
            using var innerPath = CreateRoundedRectanglePath(innerBounds, CornerRadius - 1);
            using var innerPen = new Pen(Color.FromArgb(110, 132, 145), 1f);
            e.Graphics.DrawPath(innerPen, innerPath);
        }

        private void UpdateRoundedRegion()
        {
            using var rounded = CreateRoundedRectanglePath(ClientRectangle, CornerRadius);
            Region = new Region(rounded);
        }

        private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
        {
            var diameter = Math.Max(2, radius * 2);
            var rect = Rectangle.Inflate(bounds, -1, -1);
            var path = new GraphicsPath();

            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }


    private async Task LogAuditAsync(string module, string action, string details)
    {
        await _userRepository.WriteAuditLogAsync(new AuditLogEntry
        {
            OccurredUtc = DateTime.UtcNow,
            Username = _currentUser.Username,
            RoleSnapshot = UserManagementRepository.BuildRoleSnapshot(_currentUser),
            Module = module,
            Action = action,
            Details = details
        });
    }

    private static string NormalizeCustomerName(string? customerName)
        => string.IsNullOrWhiteSpace(customerName) ? "Unknown Customer" : customerName.Trim();

    private int? TryGetSelectedQuoteId()
    {
        if (_quotesGrid.CurrentRow?.DataBoundItem is QuoteGridRow { IsCustomerHeader: false, QuoteId: int id })
        {
            return id;
        }

        if (_expiredQuotesGrid.CurrentRow?.DataBoundItem is QuoteGridRow { IsCustomerHeader: false, QuoteId: int expiredId })
        {
            return expiredId;
        }

        return null;
    }

    private sealed class QuoteGridRow
    {
        public QuoteGridRowType RowType { get; init; }
        public int? QuoteId { get; init; }
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
        QuoteSummary
    }

    private sealed class StepModelViewerControl : UserControl
    {
        private readonly WebView2 _webView;
        private readonly Label _statusLabel;
        private byte[] _stepBytes = Array.Empty<byte>();
        private bool _initialized;
        private static readonly string HtmlShell = BuildHtmlShell();

        public StepModelViewerControl()
        {
            BackColor = Color.FromArgb(248, 250, 252);

            _webView = new WebView2
            {
                Dock = DockStyle.Fill,
                Visible = false,
                DefaultBackgroundColor = Color.FromArgb(248, 250, 252)
            };

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.DimGray,
                Text = "Loading 3D viewer..."
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Enlarge", null, (_, _) => OpenEnlargedViewer());
            ContextMenuStrip = contextMenu;
            _webView.ContextMenuStrip = contextMenu;

            Controls.Add(_webView);
            Controls.Add(_statusLabel);

            Resize += (_, _) => _ = ResizeRendererAsync();
        }

        public void LoadStep(byte[] stepBytes)
        {
            _stepBytes = stepBytes;
            _ = LoadStepInternalAsync();
        }

        private async Task LoadStepInternalAsync()
        {
            if (_stepBytes.Length == 0)
            {
                _statusLabel.Text = "STEP model unavailable";
                _statusLabel.Visible = true;
                _webView.Visible = false;
                return;
            }

            _statusLabel.Text = "Loading 3D viewer...";
            _statusLabel.Visible = true;

            try
            {
                await EnsureInitializedAsync();
                var base64 = Convert.ToBase64String(_stepBytes);
                var payload = System.Text.Json.JsonSerializer.Serialize(base64);
                var script = $"window.renderStepFromBase64({payload});";
                var result = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                var rendered = string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);
                if (!rendered)
                {
                    _statusLabel.Text = "Unable to render STEP geometry";
                    _statusLabel.Visible = true;
                    _webView.Visible = false;
                    return;
                }

                _webView.Visible = true;
                _statusLabel.Visible = false;
            }
            catch
            {
                _statusLabel.Text = "WebView2 runtime unavailable for STEP preview";
                _statusLabel.Visible = true;
                _webView.Visible = false;
            }
        }

        private async Task EnsureInitializedAsync()
        {
            if (_initialized)
            {
                return;
            }

            await _webView.EnsureCoreWebView2Async();
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.NavigateToString(HtmlShell);
            _initialized = true;
            await Task.Delay(200);
        }

        private async Task ResizeRendererAsync()
        {
            if (!_initialized || !_webView.Visible)
            {
                return;
            }

            await _webView.CoreWebView2.ExecuteScriptAsync("window.resizeRenderer?.();");
        }

        private void OpenEnlargedViewer()
        {
            if (_stepBytes.Length == 0)
            {
                return;
            }

            using var enlargedWindow = new Form
            {
                Text = "STEP Model Viewer",
                Width = 1200,
                Height = 840,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = true
            };

            var closeButton = new Button
            {
                Text = "Close",
                AutoSize = true,
                Anchor = AnchorStyles.Right | AnchorStyles.Top
            };
            closeButton.Click += (_, _) => enlargedWindow.Close();

            var topBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 42,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8, 8, 8, 0)
            };
            topBar.Controls.Add(closeButton);

            var enlargedViewer = new StepModelViewerControl
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(8)
            };
            enlargedViewer.LoadStep(_stepBytes);

            enlargedWindow.Controls.Add(enlargedViewer);
            enlargedWindow.Controls.Add(topBar);

            var owner = FindForm();
            if (owner is null)
            {
                enlargedWindow.ShowDialog();
                return;
            }

            enlargedWindow.ShowDialog(owner);
        }

        private static string BuildHtmlShell()
        {
            var html = """
<!doctype html>
<html>
<head>
  <meta charset="utf-8" />
  <style>
    html, body, #viewport { margin:0; padding:0; width:100%; height:100%; background:#f8fafc; overflow:hidden; }
  </style>
</head>
<body>
  <div id="viewport"></div>
  <script src="https://cdn.jsdelivr.net/npm/three@0.160.0/build/three.min.js"></script>
  <script src="https://cdn.jsdelivr.net/npm/three@0.160.0/examples/js/controls/OrbitControls.js"></script>
  <script src="https://cdn.jsdelivr.net/npm/occt-import-js@0.0.23/dist/occt-import-js.js"></script>
  <script>
    const viewport = document.getElementById('viewport');
    const scene = new THREE.Scene();
    scene.background = new THREE.Color('#f8fafc');

    const camera = new THREE.PerspectiveCamera(50, 1, 0.01, 5000);
    camera.position.set(1.2, 0.9, 1.5);

    const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false });
    renderer.setPixelRatio(window.devicePixelRatio || 1);
    viewport.appendChild(renderer.domElement);

    const controls = new THREE.OrbitControls(camera, renderer.domElement);
    controls.enablePan = true;
    controls.enableZoom = true;
    controls.target.set(0, 0, 0);

    scene.add(new THREE.HemisphereLight(0xffffff, 0x6f7a88, 0.95));
    const keyLight = new THREE.DirectionalLight(0xffffff, 0.75);
    keyLight.position.set(3, 6, 5);
    scene.add(keyLight);

    let activeMeshRoot = null;
    let occtModulePromise = null;

    function base64ToUint8Array(base64) {
      const raw = atob(base64);
      const bytes = new Uint8Array(raw.length);
      for (let i = 0; i < raw.length; i++) {
        bytes[i] = raw.charCodeAt(i);
      }
      return bytes;
    }

    function clearScene() {
      if (!activeMeshRoot) {
        return;
      }

      scene.remove(activeMeshRoot);
      activeMeshRoot.traverse(node => {
        if (node.geometry) {
          node.geometry.dispose();
        }

        if (node.material) {
          if (Array.isArray(node.material)) {
            node.material.forEach(material => material.dispose());
          } else {
            node.material.dispose();
          }
        }
      });

      activeMeshRoot = null;
    }

    async function getOcctModule() {
      if (!occtModulePromise) {
        occtModulePromise = occtimportjs();
      }

      return occtModulePromise;
    }

    function fitCameraToObject(object3D) {
      const box = new THREE.Box3().setFromObject(object3D);
      const size = box.getSize(new THREE.Vector3());
      const center = box.getCenter(new THREE.Vector3());
      const maxDim = Math.max(size.x, size.y, size.z, 0.001);
      const distance = maxDim * 2.4;

      controls.target.copy(center);
      camera.position.set(center.x + distance, center.y + distance * 0.7, center.z + distance);
      camera.near = Math.max(maxDim / 1000, 0.001);
      camera.far = Math.max(maxDim * 200, 500);
      camera.updateProjectionMatrix();
      controls.update();
    }

    function buildMeshGroup(result) {
      const group = new THREE.Group();

      for (const meshData of result.meshes || []) {
        const attrs = meshData.attributes || {};
        const positions = attrs.position && attrs.position.array ? attrs.position.array : null;
        if (!positions || positions.length === 0) {
          continue;
        }

        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));

        if (attrs.normal && attrs.normal.array && attrs.normal.array.length > 0) {
          geometry.setAttribute('normal', new THREE.Float32BufferAttribute(attrs.normal.array, 3));
        } else {
          geometry.computeVertexNormals();
        }

        if (meshData.index && meshData.index.array && meshData.index.array.length > 0) {
          geometry.setIndex(meshData.index.array);
        }

        const color = (meshData.color && meshData.color.length >= 3)
          ? new THREE.Color(meshData.color[0], meshData.color[1], meshData.color[2])
          : new THREE.Color('#4a78bb');

        const material = new THREE.MeshStandardMaterial({
          color,
          roughness: 0.55,
          metalness: 0.08,
          side: THREE.DoubleSide
        });

        const mesh = new THREE.Mesh(geometry, material);
        group.add(mesh);
      }

      return group;
    }

    async function renderStepFromBase64(base64) {
      if (!base64 || base64.length === 0) {
        clearScene();
        return false;
      }

      try {
        const occt = await getOcctModule();
        const bytes = base64ToUint8Array(base64);
        const parsed = occt.ReadStepFile(bytes, null);
        const group = buildMeshGroup(parsed || {});
        if (group.children.length === 0) {
          clearScene();
          return false;
        }

        clearScene();
        activeMeshRoot = group;
        scene.add(activeMeshRoot);
        fitCameraToObject(activeMeshRoot);
        return true;
      } catch {
        clearScene();
        return false;
      }
    }

    function resizeRenderer() {
      const width = Math.max(viewport.clientWidth, 1);
      const height = Math.max(viewport.clientHeight, 1);
      renderer.setSize(width, height, false);
      camera.aspect = width / height;
      camera.updateProjectionMatrix();
    }

    window.renderStepFromBase64 = renderStepFromBase64;
    window.resizeRenderer = resizeRenderer;

    function animate() {
      requestAnimationFrame(animate);
      controls.update();
      renderer.render(scene, camera);
    }

    resizeRenderer();
    animate();
    window.addEventListener('resize', resizeRenderer);
  </script>
</body>
</html>
""";

            return html;
        }
    }

}
