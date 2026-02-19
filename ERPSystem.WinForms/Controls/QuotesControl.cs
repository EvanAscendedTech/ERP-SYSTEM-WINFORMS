using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Forms;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;
using System.Drawing.Drawing2D;

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
    private readonly DataGridView _completedQuotesGrid = new() { Dock = DockStyle.Top, Height = 210, AutoGenerateColumns = false, ReadOnly = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly Panel _completedQuoteDetailsHost = new() { Dock = DockStyle.Fill, Height = 0, Visible = false, Padding = new Padding(0, 8, 0, 0) };
    private readonly Label _completedQuoteDetailsTitle = new() { Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Padding = new Padding(8, 0, 0, 0) };
    private readonly Panel _completedQuoteDetailsViewport = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(248, 251, 255), Padding = new Padding(8), AutoScroll = true };
    private readonly Label _feedback = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _markWonButton;
    private readonly Button _passToPurchasingButton;
    private List<Quote> _activeQuotesCache = new();
    private string? _selectedCustomer;
    private int? _expandedCompletedQuoteId;
    private const int CompletedQuoteDetailsPanelHeight = 450;
    private const int CompletedQuoteGridStickyHeight = 210;

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
        _completedQuotesGrid.CellClick += async (_, e) => await HandleCompletedQuoteCellClickAsync(e.RowIndex, e.ColumnIndex);

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

        var quoteTablesSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.None,
            IsSplitterFixed = true,
            SplitterWidth = 2
        };

        var inProgressPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 6) };
        inProgressPanel.Controls.Add(_quotesGrid);
        inProgressPanel.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = "In-Progress Quotes",
            Font = new Font(Font, FontStyle.Bold)
        });

        _completedQuoteDetailsViewport.HorizontalScroll.Enabled = true;
        _completedQuoteDetailsViewport.HorizontalScroll.Visible = true;
        _completedQuoteDetailsViewport.VerticalScroll.Visible = true;

        _completedQuoteDetailsHost.Controls.Add(_completedQuoteDetailsViewport);
        _completedQuoteDetailsHost.Controls.Add(_completedQuoteDetailsTitle);

        var completedWorkPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) };
        completedWorkPanel.Controls.Add(_completedQuoteDetailsHost);
        completedWorkPanel.Controls.Add(_completedQuotesGrid);
        completedWorkPanel.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = "Completed Quotes",
            Font = new Font(Font, FontStyle.Bold)
        });

        quoteTablesSplit.Panel1.Controls.Add(inProgressPanel);
        quoteTablesSplit.Panel2.Controls.Add(completedWorkPanel);
        quoteTablesSplit.Resize += (_, _) =>
        {
            ApplySafePanelMinSizes(quoteTablesSplit, desiredPanel1MinSize: 160, desiredPanel2MinSize: 160);
            SetSafeSplitterDistance(quoteTablesSplit, quoteTablesSplit.Height / 2);
        };

        var topContent = new Panel { Dock = DockStyle.Fill };
        topContent.Controls.Add(quoteTablesSplit);
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

        Controls.Add(topContent);
        Controls.Add(bottomRightPanel);
        Controls.Add(_feedback);

        _ = LoadActiveQuotesAsync();
    }

    private static void SetSafeSplitterDistance(SplitContainer splitContainer, int preferredDistance)
    {
        var availableSize = splitContainer.Orientation == Orientation.Horizontal
            ? splitContainer.ClientSize.Height
            : splitContainer.ClientSize.Width;

        var minDistance = splitContainer.Panel1MinSize;
        var maxDistance = availableSize - splitContainer.Panel2MinSize - splitContainer.SplitterWidth;
        if (maxDistance < minDistance)
        {
            return;
        }

        var safeDistance = Math.Clamp(preferredDistance, minDistance, maxDistance);
        if (splitContainer.SplitterDistance != safeDistance)
        {
            splitContainer.SplitterDistance = safeDistance;
        }
    }

    private static void ApplySafePanelMinSizes(SplitContainer splitContainer, int desiredPanel1MinSize, int desiredPanel2MinSize)
    {
        var availableSize = splitContainer.Orientation == Orientation.Horizontal
            ? splitContainer.ClientSize.Height
            : splitContainer.ClientSize.Width;

        var availableWithoutSplitter = Math.Max(0, availableSize - splitContainer.SplitterWidth);
        var panel1Min = Math.Min(desiredPanel1MinSize, availableWithoutSplitter);
        var panel2Min = Math.Min(desiredPanel2MinSize, Math.Max(0, availableWithoutSplitter - panel1Min));

        if (splitContainer.Panel1MinSize != panel1Min)
        {
            splitContainer.Panel1MinSize = panel1Min;
        }

        if (splitContainer.Panel2MinSize != panel2Min)
        {
            splitContainer.Panel2MinSize = panel2Min;
        }
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
        grid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "Delete",
            HeaderText = string.Empty,
            Width = 70,
            Text = "Delete",
            UseColumnTextForButtonValue = true,
            FlatStyle = FlatStyle.Flat
        });
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

        grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || grid.Rows[e.RowIndex].DataBoundItem is not QuoteGridRow row)
            {
                return;
            }

            var columnName = grid.Columns[e.ColumnIndex].Name;
            if (columnName == "Expand" && row.RowType == QuoteGridRowType.QuoteSummary && row.QuoteId.HasValue)
            {
                e.Value = _expandedCompletedQuoteId == row.QuoteId ? "▲" : "▼";
                e.FormattingApplied = true;
                return;
            }

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
            RefreshActiveQuotesView();
            RefreshActionStateForSelection();
            _feedback.Text = $"Loaded {_activeQuotesCache.Count} active quotes.";
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

        var selectedCustomerCompletedQuotes = GetSelectedCustomerCompletedQuotes();

        var inProgressRows = BuildActiveViewRows(selectedCustomerInProgressQuotes);
        _quotesGrid.DataSource = inProgressRows;

        RefreshCompletedQuotesSection(selectedCustomerCompletedQuotes);
        RefreshActionStateForSelection();
        _customerHubLabel.Text = string.IsNullOrWhiteSpace(_selectedCustomer)
            ? "Customer Hub — pick a customer card to drill into quote details"
            : $"Customer Hub / {_selectedCustomer}";
    }


    private Quote[] GetSelectedCustomerCompletedQuotes()
    {
        return string.IsNullOrWhiteSpace(_selectedCustomer)
            ? Array.Empty<Quote>()
            : _activeQuotesCache
                .Where(q => q.Status == QuoteStatus.Completed
                    && string.Equals(NormalizeCustomerName(q.CustomerName), _selectedCustomer, StringComparison.OrdinalIgnoreCase))
                .ToArray();
    }

    private void RefreshCompletedQuotesSection(IReadOnlyCollection<Quote> completedQuotes)
    {
        if (_expandedCompletedQuoteId.HasValue && completedQuotes.All(q => q.Id != _expandedCompletedQuoteId.Value))
        {
            _expandedCompletedQuoteId = null;
        }

        var completedRows = BuildCompletedRows(completedQuotes);
        _completedQuotesGrid.DataSource = completedRows;
        _completedQuotesGrid.Refresh();
        RenderCompletedQuoteDetails();
    }

    private List<QuoteGridRow> BuildCompletedRows(IReadOnlyCollection<Quote> completedQuotes)
    {
        return BuildActiveViewRows(completedQuotes);
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
            FillColor = ResolveCustomerCardColor(_customerCardsPanel.Controls.Count),
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

    private async Task HandleCompletedQuoteCellClickAsync(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || rowIndex >= _completedQuotesGrid.Rows.Count)
        {
            return;
        }

        if (_completedQuotesGrid.Rows[rowIndex].DataBoundItem is not QuoteGridRow selectedRow || selectedRow.QuoteId is not int quoteId)
        {
            return;
        }

        var clickedColumnName = columnIndex >= 0 ? _completedQuotesGrid.Columns[columnIndex].Name : string.Empty;
        if (string.Equals(clickedColumnName, "Delete", StringComparison.OrdinalIgnoreCase))
        {
            await DeleteCompletedQuoteAsync(quoteId);
            return;
        }

        ToggleCompletedQuoteExpansion(quoteId);
    }

    private void ToggleCompletedQuoteExpansion(int quoteId)
    {
        var wasExpanded = _expandedCompletedQuoteId == quoteId;
        _expandedCompletedQuoteId = wasExpanded ? null : quoteId;
        var selectedCustomerCompletedQuotes = GetSelectedCustomerCompletedQuotes();
        RefreshCompletedQuotesSection(selectedCustomerCompletedQuotes);

        if (wasExpanded)
        {
            FocusCompletedQuoteRow(quoteId);
        }
    }

    private void FocusCompletedQuoteRow(int quoteId)
    {
        for (var rowIndex = 0; rowIndex < _completedQuotesGrid.Rows.Count; rowIndex++)
        {
            if (_completedQuotesGrid.Rows[rowIndex].DataBoundItem is not QuoteGridRow row
                || row.QuoteId != quoteId)
            {
                continue;
            }

            _completedQuotesGrid.ClearSelection();
            _completedQuotesGrid.Rows[rowIndex].Selected = true;
            if (_completedQuotesGrid.Columns.Count > 0)
            {
                _completedQuotesGrid.CurrentCell = _completedQuotesGrid.Rows[rowIndex].Cells[0];
            }

            _completedQuotesGrid.FirstDisplayedScrollingRowIndex = rowIndex;
            _completedQuotesGrid.Focus();
            return;
        }
    }

    private async Task DeleteCompletedQuoteAsync(int quoteId)
    {
        var quote = await _quoteRepository.GetQuoteAsync(quoteId);
        if (quote is null)
        {
            _feedback.Text = $"Quote {quoteId} no longer exists.";
            return;
        }

        var quoteIdentifier = string.IsNullOrWhiteSpace(quote.LifecycleQuoteId)
            ? $"Quote #{quote.Id}"
            : quote.LifecycleQuoteId;

        var confirm = MessageBox.Show(
            $"Are you sure you want to delete completed quote {quoteIdentifier} for {quote.CustomerName}?",
            "Delete Completed Quote",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (confirm != DialogResult.Yes)
        {
            _feedback.Text = "Completed quote deletion canceled.";
            return;
        }

        await _quoteRepository.DeleteQuoteAsync(quote.Id);
        await LogAuditAsync("Quotes", "Deleted completed quote", $"Quote #{quote.Id} deleted from completed list.");
        _feedback.Text = $"Completed quote {quote.Id} deleted.";
        _expandedCompletedQuoteId = null;
        await LoadActiveQuotesAsync();
    }

    private void RenderCompletedQuoteDetails()
    {
        _completedQuoteDetailsViewport.Controls.Clear();

        if (_expandedCompletedQuoteId is not int expandedQuoteId)
        {
            _completedQuoteDetailsHost.Visible = false;
            _completedQuoteDetailsHost.Height = 0;
            _completedQuoteDetailsTitle.Text = string.Empty;
            _completedQuotesGrid.Height = CompletedQuoteGridStickyHeight;
            return;
        }

        var quote = _activeQuotesCache.FirstOrDefault(q => q.Id == expandedQuoteId && q.Status == QuoteStatus.Completed);
        if (quote is null)
        {
            _expandedCompletedQuoteId = null;
            _completedQuoteDetailsHost.Visible = false;
            _completedQuoteDetailsHost.Height = 0;
            _completedQuotesGrid.Height = CompletedQuoteGridStickyHeight;
            return;
        }

        _completedQuoteDetailsTitle.Text = $"Expanded Quote #{quote.Id} — click the row toggle again to minimize";
        _completedQuoteDetailsViewport.Controls.Add(CreateLineItemsSocketView(quote));
        _completedQuoteDetailsHost.Height = CompletedQuoteDetailsPanelHeight;
        _completedQuoteDetailsHost.Visible = true;
        _completedQuotesGrid.Height = CompletedQuoteGridStickyHeight;
        _completedQuoteDetailsViewport.AutoScrollPosition = Point.Empty;
        _completedQuoteDetailsHost.ScrollControlIntoView(_completedQuoteDetailsViewport);
    }

    private Control CreateLineItemsSocketView(Quote quote)
    {
        var scrollHost = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            WrapContents = false,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(4, 4, 24, 8)
        };

        if (quote.LineItems.Count == 0)
        {
            scrollHost.Controls.Add(new Label
            {
                Text = "No line items attached to this completed quote.",
                Height = 40,
                Width = 760,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray
            });

            return scrollHost;
        }

        for (var index = 0; index < quote.LineItems.Count; index++)
        {
            var lineItem = quote.LineItems[index];
            scrollHost.Controls.Add(CreateLineItemPreviewCard(quote, lineItem, index + 1));
        }

        return scrollHost;
    }

    private Control CreateLineItemPreviewCard(Quote quote, QuoteLineItem lineItem, int displayIndex)
    {
        var card = new TableLayoutPanel
        {
            Width = 900,
            Height = 270,
            Margin = new Padding(0, 0, 0, 12),
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.White,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };

        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var infoPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            Padding = new Padding(12, 10, 12, 10),
            BackColor = Color.FromArgb(245, 248, 252)
        };
        for (var i = 0; i < infoPanel.RowCount; i++)
        {
            infoPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 12.5f));
        }

        infoPanel.Controls.Add(CreateMetadataLabel($"Line Item #{displayIndex}", bold: true), 0, 0);
        infoPanel.Controls.Add(CreateMetadataLabel($"Drawing Name: {lineItem.Description}"), 0, 1);
        infoPanel.Controls.Add(CreateMetadataLabel($"Drawing Number: {lineItem.DrawingNumber}"), 0, 2);
        infoPanel.Controls.Add(CreateMetadataLabel($"Customer: {quote.CustomerName}"), 0, 3);
        infoPanel.Controls.Add(CreateMetadataLabel($"Drawing Revision: {lineItem.Revision}"), 0, 4);
        infoPanel.Controls.Add(CreateMetadataLabel($"Qty: {lineItem.Quantity:0.##}"), 0, 5);
        infoPanel.Controls.Add(CreateMetadataLabel($"Line Total: {lineItem.LineItemTotal:C2}"), 0, 6);
        infoPanel.Controls.Add(CreateMetadataLabel("3D Controls: rotate / pan / zoom", bold: true), 0, 7);

        var viewerContainer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8), BackColor = Color.FromArgb(250, 251, 253) };
        viewerContainer.Controls.Add(CreateModelCell(lineItem));

        card.Controls.Add(infoPanel, 0, 0);
        card.Controls.Add(viewerContainer, 1, 0);
        return card;
    }

    private static Label CreateMetadataLabel(string text, bool bold = false)
        => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Font = bold ? new Font(SystemFonts.DefaultFont, FontStyle.Bold) : SystemFonts.DefaultFont
        };

    private Control CreateModelCell(QuoteLineItem lineItem)
    {
        var stepAttachment = FindLatestStepAttachment(lineItem);

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

        var viewer = new StepModelPreviewControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(4)
        };
        _ = viewer.LoadStepAttachmentAsync(stepAttachment, _quoteRepository.GetQuoteBlobContentAsync);
        return viewer;
    }

    private static QuoteBlobAttachment? FindLatestStepAttachment(QuoteLineItem lineItem)
    {
        return lineItem.BlobAttachments
            .Where(blob => blob.BlobType == QuoteBlobType.ThreeDModel
                && blob.LineItemId == lineItem.Id
                && (lineItem.QuoteId <= 0 || blob.QuoteId == lineItem.QuoteId)
                && (blob.Extension.Equals(".step", StringComparison.OrdinalIgnoreCase)
                    || blob.Extension.Equals(".stp", StringComparison.OrdinalIgnoreCase)
                    || blob.FileName.EndsWith(".step", StringComparison.OrdinalIgnoreCase)
                    || blob.FileName.EndsWith(".stp", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(blob => blob.UploadedUtc)
            .ThenByDescending(blob => blob.Id)
            .FirstOrDefault();
    }

    private Color ResolveCustomerCardColor(int customerIndex)
    {
        return customerIndex % 2 == 0
            ? Color.FromArgb(229, 241, 255)
            : Color.FromArgb(232, 246, 235);
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


}
