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
    private readonly Action<string> _openSection;
    private readonly UserAccount _currentUser;
    private readonly FlowLayoutPanel _customerCardsPanel = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8, 4, 8, 8) };
    private readonly Label _customerHubLabel = new() { Dock = DockStyle.Top, Height = 30, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Padding = new Padding(8, 0, 0, 0) };
    private readonly Button _backToHubButton = new() { Text = "← Back to Customers", AutoSize = true, Visible = false };
    private readonly DataGridView _quotesGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly DataGridView _expiredQuotesGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly Label _feedback = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
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
        var openQuotePacketButton = new Button { Text = "Open Quote Packet", AutoSize = true };
        var editQuoteButton = new Button { Text = "Open Quote Details", AutoSize = true };
        var deleteQuoteButton = new Button { Text = "Delete In-Process Quote", AutoSize = true };
        var passToProductionButton = new Button { Text = "Pass to Production", AutoSize = true };

        refreshButton.Click += async (_, _) => await LoadActiveQuotesAsync();
        newQuoteButton.Click += async (_, _) => await CreateNewQuoteAsync();
        openQuotePacketButton.Click += async (_, _) => await OpenSelectedQuotePacketAsync();
        editQuoteButton.Click += async (_, _) => await OpenSelectedQuoteDetailsAsync();
        deleteQuoteButton.Click += async (_, _) => await DeleteSelectedQuoteAsync();
        passToProductionButton.Click += async (_, _) => await PassSelectedToProductionAsync();
        _backToHubButton.Click += (_, _) =>
        {
            _selectedCustomer = null;
            RefreshActiveQuotesView();
        };
        _quotesGrid.CellDoubleClick += async (_, _) => await OpenSelectedQuoteDetailsAsync();

        actionsPanel.Controls.Add(refreshButton);
        actionsPanel.Controls.Add(newQuoteButton);
        actionsPanel.Controls.Add(openQuotePacketButton);
        actionsPanel.Controls.Add(editQuoteButton);
        actionsPanel.Controls.Add(deleteQuoteButton);
        actionsPanel.Controls.Add(passToProductionButton);
        actionsPanel.Controls.Add(_backToHubButton);

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
        Controls.Add(_feedback);

        _ = LoadActiveQuotesAsync();
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
            _activeQuotesCache = allQuotes.Where(q => q.Status != QuoteStatus.Expired).ToList();
            var expiredQuotes = allQuotes.Where(q => q.Status == QuoteStatus.Expired)
                .OrderByDescending(q => q.ExpiredUtc ?? q.LastUpdatedUtc)
                .Select(CreateQuoteRow)
                .ToList();

            RefreshActiveQuotesView();
            _expiredQuotesGrid.DataSource = expiredQuotes;
            _feedback.Text = $"Loaded {_activeQuotesCache.Count} active/finished quotes and {expiredQuotes.Count} archived quotes.";
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
            _feedback.Text = "Only active quotes (Open/In-Process or Won) can be opened from this screen.";
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

    private void RefreshActiveQuotesView()
    {
        BuildCustomerCards();

        var filteredQuotes = string.IsNullOrWhiteSpace(_selectedCustomer)
            ? Array.Empty<Quote>()
            : _activeQuotesCache.Where(q => string.Equals(NormalizeCustomerName(q.CustomerName), _selectedCustomer, StringComparison.OrdinalIgnoreCase)).ToArray();

        _quotesGrid.DataSource = BuildActiveViewRows(filteredQuotes);
        _backToHubButton.Visible = !string.IsNullOrWhiteSpace(_selectedCustomer);
        _customerHubLabel.Text = string.IsNullOrWhiteSpace(_selectedCustomer)
            ? "Customer Hub — pick a customer card to drill into quote details"
            : $"Customer Hub / {_selectedCustomer}";
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

        _customerCardsPanel.ResumeLayout();
    }

    private Control CreateCustomerCard(string customerName, IReadOnlyCollection<Quote> quotes)
    {
        var card = new CustomerCardPanel
        {
            Width = 250,
            Height = 116,
            Margin = new Padding(0, 0, 12, 0),
            FillColor = string.Equals(_selectedCustomer, customerName, StringComparison.OrdinalIgnoreCase)
                ? Color.FromArgb(214, 236, 255)
                : Color.FromArgb(236, 240, 245),
            Cursor = Cursors.Hand
        };

        var openCount = quotes.Count(q => q.Status == QuoteStatus.InProgress);
        var wonCount = quotes.Count(q => q.Status == QuoteStatus.Won);

        var nameLabel = new Label { Text = customerName, Dock = DockStyle.Top, Height = 44, Padding = new Padding(10, 12, 10, 4), Font = new Font(Font, FontStyle.Bold) };
        var summaryLabel = new Label
        {
            Text = $"Open: {openCount}    Total: {quotes.Count}    Won: {wonCount}",
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
