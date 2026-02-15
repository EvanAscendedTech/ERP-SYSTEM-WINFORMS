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
    private const string MoveToWonColumnName = "MoveToWon";

    private readonly QuoteRepository _quoteRepository;
    private readonly ProductionRepository _productionRepository;
    private readonly Action<string> _openSection;
    private readonly UserAccount _currentUser;
    private readonly FlowLayoutPanel _customerCardsPanel = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8, 4, 8, 8) };
    private readonly Label _customerHubLabel = new() { Dock = DockStyle.Top, Height = 30, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Padding = new Padding(8, 0, 0, 0) };
    private readonly DataGridView _quotesGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly DataGridView _expiredQuotesGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly Label _feedback = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _passToPurchasingButton;
    private List<Quote> _activeQuotesCache = new();
    private string? _selectedCustomer;

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
        var deleteQuoteButton = new Button { Text = "Delete Quote", AutoSize = true };
        var markCompletedButton = new Button { Text = "Mark Completed", AutoSize = true };
        _passToPurchasingButton = new Button { Text = "Pass to Purchasing", AutoSize = true, Enabled = false };
        var archivedQuotesButton = new Button { Text = "Archived Quotes", AutoSize = true };

        refreshButton.Click += async (_, _) => await LoadActiveQuotesAsync();
        newQuoteButton.Click += async (_, _) => await CreateNewQuoteAsync();
        deleteQuoteButton.Click += async (_, _) => await DeleteSelectedQuoteAsync();
        markCompletedButton.Click += async (_, _) => await MarkSelectedCompletedAsync();
        _passToPurchasingButton.Click += async (_, _) => await PassSelectedToPurchasingAsync();
        _quotesGrid.SelectionChanged += (_, _) => RefreshActionStateForSelection();
        _quotesGrid.CellContentClick += async (_, e) => await HandleQuotesGridCellContentClickAsync(e.RowIndex, e.ColumnIndex);
        archivedQuotesButton.Click += (_, _) => OpenArchivedQuotesWindow();
        _quotesGrid.CellDoubleClick += async (_, _) => await OpenSelectedQuoteDetailsAsync();

        actionsPanel.Controls.Add(refreshButton);
        actionsPanel.Controls.Add(newQuoteButton);
        actionsPanel.Controls.Add(deleteQuoteButton);
        actionsPanel.Controls.Add(markCompletedButton);
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
        if (includeLifecycleColumn)
        {
            grid.Columns.Add(new DataGridViewButtonColumn
            {
                Name = MoveToWonColumnName,
                HeaderText = "Actions",
                Text = "Move to Won",
                UseColumnTextForButtonValue = false,
                Width = 120,
                ReadOnly = true
            });

            grid.CellFormatting += (_, e) =>
            {
                if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != MoveToWonColumnName)
                {
                    return;
                }

                if (grid.Rows[e.RowIndex].DataBoundItem is not QuoteGridRow row)
                {
                    e.Value = string.Empty;
                    e.FormattingApplied = true;
                    return;
                }

                e.Value = row.Status == QuoteStatus.Completed ? "Move to Won" : string.Empty;
                e.FormattingApplied = true;
            };
        }
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
            _feedback.Text = $"Loaded {_activeQuotesCache.Count} active/finished quotes and {expiredQuotes.Count} archived quotes.";
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
            .GroupBy(q => NormalizeCustomerName(q.CustomerName))
            .OrderBy(g => g.Key)
            .ToList();

        if (string.IsNullOrWhiteSpace(_selectedCustomer) && customerGroups.Count > 0)
        {
            _selectedCustomer = customerGroups[0].Key;
        }

        BuildCustomerCards();

        var filteredQuotes = string.IsNullOrWhiteSpace(_selectedCustomer)
            ? Array.Empty<Quote>()
            : _activeQuotesCache.Where(q => string.Equals(NormalizeCustomerName(q.CustomerName), _selectedCustomer, StringComparison.OrdinalIgnoreCase)).ToArray();

        _quotesGrid.DataSource = BuildActiveViewRows(filteredQuotes);
        RefreshActionStateForSelection();
        _customerHubLabel.Text = string.IsNullOrWhiteSpace(_selectedCustomer)
            ? "Customer Hub — pick a customer card to drill into quote details"
            : $"Customer Hub / {_selectedCustomer}";
    }

    private async Task HandleQuotesGridCellContentClickAsync(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 0 || _quotesGrid.Columns[columnIndex].Name != MoveToWonColumnName)
        {
            return;
        }

        if (_quotesGrid.Rows[rowIndex].DataBoundItem is not QuoteGridRow row || row.Status != QuoteStatus.Completed || row.QuoteId is not int quoteId)
        {
            return;
        }

        _quotesGrid.CurrentCell = _quotesGrid.Rows[rowIndex].Cells[0];
        await MarkSelectedWonAsync();
        SelectQuoteRow(quoteId);
    }

    private void RefreshActionStateForSelection()
    {
        if (_quotesGrid.CurrentRow?.DataBoundItem is not QuoteGridRow row)
        {
            _passToPurchasingButton.Enabled = false;
            return;
        }

        _passToPurchasingButton.Enabled = row.Status == QuoteStatus.Won && row.QuoteId.HasValue;
    }

    private void BuildCustomerCards()
    {
        _customerCardsPanel.SuspendLayout();
        _customerCardsPanel.Controls.Clear();

        var customerGroups = _activeQuotesCache
            .GroupBy(q => NormalizeCustomerName(q.CustomerName))
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var customerGroup in customerGroups)
        {
            _customerCardsPanel.Controls.Add(CreateCustomerCard(customerGroup.Key, customerGroup.ToList()));
        }

        _customerCardsPanel.Invalidate(true);

        _customerCardsPanel.ResumeLayout();
    }

    private Control CreateCustomerCard(string customerName, IReadOnlyCollection<Quote> quotes)
    {
        var card = new CustomerCardPanel
        {
            Width = 250,
            Height = 116,
            Margin = new Padding(0, 0, 12, 0),
            FillColor = ResolveCustomerCardColor(customerName),
            Cursor = Cursors.Hand
        };

        var openCount = quotes.Count(q => q.Status == QuoteStatus.InProgress);
        var completedCount = quotes.Count(q => q.Status == QuoteStatus.Completed);
        var wonCount = quotes.Count(q => q.Status == QuoteStatus.Won);

        var nameLabel = new Label { Text = customerName, Dock = DockStyle.Top, Height = 44, Padding = new Padding(10, 12, 10, 4), Font = new Font(Font, FontStyle.Bold) };
        var summaryLabel = new Label
        {
            Text = $"Open: {openCount}    Completed: {completedCount}    Total: {quotes.Count}    Won: {wonCount}",
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 4, 10, 8),
            Font = new Font(Font.FontFamily, 9f, FontStyle.Regular)
        };

        void selectCustomer(object? _, EventArgs __)
        {
            _selectedCustomer = customerName;
            RefreshActiveQuotesView();
        }

        card.Click += selectCustomer;
        nameLabel.Click += selectCustomer;
        summaryLabel.Click += selectCustomer;
        card.Controls.Add(summaryLabel);
        card.Controls.Add(nameLabel);
        return card;
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

}
