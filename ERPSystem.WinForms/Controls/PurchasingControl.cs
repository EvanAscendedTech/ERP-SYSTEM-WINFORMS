using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Controls;

public class PurchasingControl : UserControl, IRealtimeDataControl
{
    private readonly QuoteRepository _quoteRepository;
    private readonly ProductionRepository _productionRepository;
    private readonly Action<string> _openSection;
    private readonly string _actorUserId;
    private readonly DataGridView _quotesGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly DataGridView _technicalDocsGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly DataGridView _purchaseDocsGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly Label _checklistLabel = new() { Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _feedback = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };

    public PurchasingControl(QuoteRepository quoteRepository, ProductionRepository productionRepository, Models.UserAccount currentUser, Action<string> openSection)
    {
        _quoteRepository = quoteRepository;
        _productionRepository = productionRepository;
        _actorUserId = currentUser.Username;
        _openSection = openSection;
        Dock = DockStyle.Fill;

        ConfigureGrids();

        var actionsPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
        var refreshButton = new Button { Text = "Refresh Purchasing", AutoSize = true };
        var uploadPurchaseDocButton = new Button { Text = "Upload Purchase Doc", AutoSize = true };
        var passToProductionButton = new Button { Text = "Pass to Production", AutoSize = true };

        refreshButton.Click += async (_, _) => await LoadPurchasingQuotesAsync();
        uploadPurchaseDocButton.Click += async (_, _) => await UploadPurchaseDocumentAsync();
        passToProductionButton.Click += async (_, _) => await PassToProductionAsync();

        actionsPanel.Controls.Add(refreshButton);
        actionsPanel.Controls.Add(uploadPurchaseDocButton);
        actionsPanel.Controls.Add(passToProductionButton);

        var docsSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };
        ConfigureSafeSplitterDistance(docsSplit, preferredDistance: 190, panel1MinSize: 130, panel2MinSize: 130);

        docsSplit.Panel1.Controls.Add(_technicalDocsGrid);
        docsSplit.Panel1.Controls.Add(new Label
        {
            Text = "Technical documentation from quote",
            Dock = DockStyle.Top,
            Height = 24,
            Font = new Font(Font, FontStyle.Bold)
        });

        docsSplit.Panel2.Controls.Add(_purchaseDocsGrid);
        docsSplit.Panel2.Controls.Add(new Label
        {
            Text = "Purchase documentation (required before Production)",
            Dock = DockStyle.Top,
            Height = 24,
            Font = new Font(Font, FontStyle.Bold)
        });

        var rightPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0) };
        rightPanel.Controls.Add(docsSplit);
        rightPanel.Controls.Add(_checklistLabel);

        var mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };
        ConfigureSafeSplitterDistance(mainSplit, preferredDistance: 560, panel1MinSize: 350, panel2MinSize: 320);

        mainSplit.Panel1.Controls.Add(_quotesGrid);
        mainSplit.Panel1.Controls.Add(new Label
        {
            Text = "Purchasing queue (Won quotes passed from Quotes)",
            Dock = DockStyle.Top,
            Height = 24,
            Font = new Font(Font, FontStyle.Bold)
        });

        mainSplit.Panel2.Controls.Add(rightPanel);

        Controls.Add(mainSplit);
        Controls.Add(actionsPanel);
        Controls.Add(_feedback);

        _quotesGrid.SelectionChanged += (_, _) => ShowSelectedQuoteDocuments();

        _ = LoadPurchasingQuotesAsync();
    }

    private static void ConfigureSafeSplitterDistance(SplitContainer splitContainer, int preferredDistance, int panel1MinSize, int panel2MinSize)
    {
        splitContainer.SplitterMoved += (_, _) => ApplySafeSplitterDistance(splitContainer, splitContainer.SplitterDistance, panel1MinSize, panel2MinSize);
        splitContainer.SizeChanged += (_, _) => ApplySafeSplitterDistance(splitContainer, preferredDistance, panel1MinSize, panel2MinSize);
        splitContainer.HandleCreated += (_, _) => ApplySafeSplitterDistance(splitContainer, preferredDistance, panel1MinSize, panel2MinSize);
    }

    private static void ApplySafeSplitterDistance(SplitContainer splitContainer, int preferredDistance, int requestedPanel1MinSize, int requestedPanel2MinSize)
    {
        var totalSize = splitContainer.Orientation == Orientation.Vertical
            ? splitContainer.ClientSize.Width
            : splitContainer.ClientSize.Height;

        if (totalSize <= splitContainer.SplitterWidth)
        {
            return;
        }

        var availableForPanels = totalSize - splitContainer.SplitterWidth;
        var totalRequestedMinSize = requestedPanel1MinSize + requestedPanel2MinSize;
        var scale = totalRequestedMinSize > availableForPanels
            ? availableForPanels / (double)totalRequestedMinSize
            : 1d;

        var panel1MinSize = Math.Clamp((int)Math.Floor(requestedPanel1MinSize * scale), 0, Math.Max(availableForPanels - 1, 0));
        var panel2MinSize = Math.Clamp((int)Math.Floor(requestedPanel2MinSize * scale), 0, Math.Max(availableForPanels - panel1MinSize, 0));

        splitContainer.Panel1MinSize = panel1MinSize;
        splitContainer.Panel2MinSize = panel2MinSize;

        var minDistance = splitContainer.Panel1MinSize;
        var maxDistance = totalSize - splitContainer.Panel2MinSize - splitContainer.SplitterWidth;

        if (maxDistance < minDistance)
        {
            return;
        }

        splitContainer.SplitterDistance = Math.Clamp(preferredDistance, minDistance, maxDistance);
    }

    private void ConfigureGrids()
    {
        _quotesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Quote #", DataPropertyName = nameof(Quote.Id), Width = 80 });
        _quotesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Lifecycle", DataPropertyName = nameof(Quote.LifecycleQuoteId), Width = 150 });
        _quotesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Customer", DataPropertyName = nameof(Quote.CustomerName), Width = 210 });
        _quotesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Passed To Purchasing", DataPropertyName = nameof(Quote.PassedToPurchasingUtc), Width = 180 });

        _technicalDocsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "File", DataPropertyName = nameof(QuoteBlobAttachment.FileName), Width = 210 });
        _technicalDocsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Type", DataPropertyName = nameof(QuoteBlobAttachment.BlobType), Width = 110 });
        _technicalDocsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Uploaded By", DataPropertyName = nameof(QuoteBlobAttachment.UploadedBy), Width = 130 });
        _technicalDocsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Uploaded", DataPropertyName = nameof(QuoteBlobAttachment.UploadedUtc), Width = 150 });

        _purchaseDocsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "File", DataPropertyName = nameof(QuoteBlobAttachment.FileName), Width = 240 });
        _purchaseDocsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Uploaded By", DataPropertyName = nameof(QuoteBlobAttachment.UploadedBy), Width = 140 });
        _purchaseDocsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Uploaded", DataPropertyName = nameof(QuoteBlobAttachment.UploadedUtc), Width = 170 });
    }

    private async Task LoadPurchasingQuotesAsync()
    {
        var quotes = await _quoteRepository.GetPurchasingQuotesAsync();
        var jobs = await _productionRepository.GetJobsAsync();
        var inProductionQuoteIds = jobs.Where(x => x.SourceQuoteId.HasValue).Select(x => x.SourceQuoteId!.Value).ToHashSet();

        var queue = quotes.Where(q => !inProductionQuoteIds.Contains(q.Id)).ToList();
        _quotesGrid.DataSource = queue;
        _feedback.Text = $"Loaded {queue.Count} quotes in Purchasing.";

        if (_quotesGrid.Rows.Count > 0)
        {
            _quotesGrid.Rows[0].Selected = true;
            _quotesGrid.CurrentCell = _quotesGrid.Rows[0].Cells[0];
        }

        ShowSelectedQuoteDocuments();
    }

    private Quote? GetSelectedQuote() => _quotesGrid.CurrentRow?.DataBoundItem as Quote;

    private void ShowSelectedQuoteDocuments()
    {
        var selected = GetSelectedQuote();
        if (selected is null)
        {
            _technicalDocsGrid.DataSource = new List<QuoteBlobAttachment>();
            _purchaseDocsGrid.DataSource = new List<QuoteBlobAttachment>();
            _checklistLabel.Text = "Checklist: Select a quote to review docs.";
            return;
        }

        var allBlobs = selected.LineItems.SelectMany(x => x.BlobAttachments).ToList();
        var technical = allBlobs.Where(x => x.BlobType == QuoteBlobType.Technical).ToList();
        var purchasingDocs = allBlobs.Where(x => x.BlobType == QuoteBlobType.PurchaseDocumentation).ToList();

        _technicalDocsGrid.DataSource = technical;
        _purchaseDocsGrid.DataSource = purchasingDocs;
        _checklistLabel.Text = purchasingDocs.Count > 0
            ? "Checklist: ✅ Purchase documentation uploaded. Ready to pass to Production."
            : "Checklist: ☐ Upload purchase documentation (PDF/PO) before passing to Production.";
    }

    private async Task UploadPurchaseDocumentAsync()
    {
        var quote = GetSelectedQuote();
        if (quote is null)
        {
            _feedback.Text = "Select a Purchasing quote first.";
            return;
        }

        var targetLine = quote.LineItems.FirstOrDefault();
        if (targetLine is null)
        {
            _feedback.Text = "Selected quote has no line item to attach purchase documentation.";
            return;
        }

        using var picker = new OpenFileDialog
        {
            Title = $"Upload purchase documentation for Q{quote.Id}",
            Filter = "PDF or docs (*.pdf;*.doc;*.docx;*.xlsx;*.xls)|*.pdf;*.doc;*.docx;*.xlsx;*.xls|All Files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (picker.ShowDialog(this) != DialogResult.OK)
        {
            _feedback.Text = "Purchase document upload canceled.";
            return;
        }

        var filePath = picker.FileName;
        var bytes = await File.ReadAllBytesAsync(filePath);
        using var sha = System.Security.Cryptography.SHA256.Create();
        var digest = sha.ComputeHash(bytes);
        var extension = Path.GetExtension(filePath);

        await _quoteRepository.InsertQuoteLineItemFileAsync(
            quote.Id,
            targetLine.Id,
            quote.LifecycleQuoteId,
            QuoteBlobType.PurchaseDocumentation,
            Path.GetFileName(filePath),
            extension,
            bytes.LongLength,
            digest,
            _actorUserId,
            DateTime.UtcNow,
            bytes);

        _feedback.Text = $"Uploaded purchase documentation for quote {quote.Id}.";
        await LoadPurchasingQuotesAsync();
    }

    private async Task PassToProductionAsync()
    {
        var quote = GetSelectedQuote();
        if (quote is null)
        {
            _feedback.Text = "Select a Purchasing quote first.";
            return;
        }

        if (quote.Status != QuoteStatus.Won)
        {
            _feedback.Text = $"Only Won quotes can move to Production. Current status: {quote.Status}.";
            return;
        }

        if (!QuoteRepository.HasBlobType(quote, QuoteBlobType.PurchaseDocumentation))
        {
            _feedback.Text = "Upload purchase documentation (PDF/PO) before moving this quote to Production.";
            return;
        }

        var linkageValidation = await _quoteRepository.ValidateQuoteFileLinkageAsync(quote.Id);
        if (!linkageValidation.Success)
        {
            _feedback.Text = $"Cannot finalize Purchasing step: {linkageValidation.Message}";
            return;
        }

        var existing = (await _productionRepository.GetJobsAsync()).FirstOrDefault(x => x.SourceQuoteId == quote.Id);
        if (existing is null)
        {
            var lineItem = quote.LineItems.FirstOrDefault();
            var lifecycle = string.IsNullOrWhiteSpace(quote.LifecycleQuoteId) ? $"Q-{quote.Id}" : quote.LifecycleQuoteId;
            var job = new ProductionJob
            {
                JobNumber = $"JOB-{lifecycle}-{quote.Id}",
                ProductName = lineItem?.Description ?? quote.CustomerName,
                PlannedQuantity = Math.Max(1, Convert.ToInt32(lineItem?.Quantity ?? 1)),
                ProducedQuantity = 0,
                DueDateUtc = DateTime.UtcNow.AddDays(14),
                Status = ProductionJobStatus.Planned,
                SourceQuoteId = quote.Id,
                QuoteLifecycleId = lifecycle
            };

            await _productionRepository.SaveJobAsync(job);
            _feedback.Text = $"Quote {quote.Id} moved to Production queue as {job.JobNumber}. {linkageValidation.Message}";
        }
        else
        {
            _feedback.Text = $"Quote {quote.Id} is already linked to production job {existing.JobNumber}. {linkageValidation.Message}";
        }

        await LoadPurchasingQuotesAsync();
        _openSection("Production");
    }


    public async Task<bool> OpenFromDashboardAsync(int quoteId)
    {
        await LoadPurchasingQuotesAsync();

        foreach (DataGridViewRow row in _quotesGrid.Rows)
        {
            if (row.DataBoundItem is Quote quote && quote.Id == quoteId)
            {
                row.Selected = true;
                _quotesGrid.CurrentCell = row.Cells[0];
                _quotesGrid.FirstDisplayedScrollingRowIndex = row.Index;
                ShowSelectedQuoteDocuments();
                return true;
            }
        }

        _feedback.Text = $"Quote {quoteId} is not currently in the Purchasing queue.";
        return false;
    }

    public Task RefreshDataAsync(bool fromFailSafeCheckpoint) => LoadPurchasingQuotesAsync();
}
