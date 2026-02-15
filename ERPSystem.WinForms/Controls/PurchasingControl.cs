using System.Diagnostics;
using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Controls;

public class PurchasingControl : UserControl, IRealtimeDataControl
{
    private readonly QuoteRepository _quoteRepository;
    private readonly ProductionRepository _productionRepository;
    private readonly UserManagementRepository _userRepository;
    private readonly Action<string> _openSection;
    private readonly string _actorUserId;
    private readonly int _currentUserId;
    private readonly bool _canEdit;
    private readonly DataGridView _quotesGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly DataGridView _lineItemsGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly DataGridView _technicalDocsGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly DataGridView _purchaseDocsGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly Label _checklistLabel = new() { Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _feedback = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
    private readonly SplitContainer _mainSplit = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
    private readonly SplitContainer _docsSplit = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
    private bool _restoringLayout;

    public PurchasingControl(QuoteRepository quoteRepository, ProductionRepository productionRepository, UserManagementRepository userRepository, Models.UserAccount currentUser, Action<string> openSection, bool canEdit)
    {
        _quoteRepository = quoteRepository;
        _productionRepository = productionRepository;
        _userRepository = userRepository;
        _actorUserId = currentUser.Username;
        _currentUserId = currentUser.Id;
        _openSection = openSection;
        _canEdit = canEdit;
        Dock = DockStyle.Fill;

        ConfigureGrids();

        var actionsPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
        var refreshButton = new Button { Text = "Refresh Purchasing", AutoSize = true };
        var passToProductionButton = new Button { Text = "Pass to Production", AutoSize = true, Enabled = _canEdit };

        refreshButton.Click += async (_, _) => await LoadPurchasingQuotesAsync();
        passToProductionButton.Click += async (_, _) => await PassToProductionAsync();

        actionsPanel.Controls.Add(refreshButton);
        actionsPanel.Controls.Add(passToProductionButton);

        ConfigureSafeSplitterDistance(_docsSplit, preferredDistance: 190, panel1MinSize: 130, panel2MinSize: 130);

        _docsSplit.Panel1.Controls.Add(_technicalDocsGrid);
        _docsSplit.Panel1.Controls.Add(new Label
        {
            Text = "Technical documentation from quote (double-click to open)",
            Dock = DockStyle.Top,
            Height = 24,
            Font = new Font(Font, FontStyle.Bold)
        });

        var purchaseDocsHeaderPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 30,
            Padding = new Padding(0),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        var uploadPurchaseDocButton = new Button { Text = "Upload Purchase Documentation", AutoSize = true, Enabled = _canEdit };
        var removePurchaseDocButton = new Button { Text = "Remove Selected", AutoSize = true, Enabled = _canEdit };

        uploadPurchaseDocButton.Click += async (_, _) => await UploadPurchaseDocumentAsync();
        removePurchaseDocButton.Click += async (_, _) => await DeleteSelectedPurchaseDocumentAsync();

        purchaseDocsHeaderPanel.Controls.Add(new Label
        {
            Text = "Purchase documentation (required before Production)",
            Width = 300,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold)
        });
        purchaseDocsHeaderPanel.Controls.Add(uploadPurchaseDocButton);
        purchaseDocsHeaderPanel.Controls.Add(removePurchaseDocButton);

        _docsSplit.Panel2.Controls.Add(_purchaseDocsGrid);
        _docsSplit.Panel2.Controls.Add(purchaseDocsHeaderPanel);

        var rightPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 0, 0, 0),
            ColumnCount = 1,
            RowCount = 4
        };
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 170f));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        rightPanel.Controls.Add(_checklistLabel, 0, 0);
        rightPanel.Controls.Add(new Label
        {
            Text = "Items to purchase",
            Dock = DockStyle.Fill,
            Height = 24,
            Font = new Font(Font, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);
        rightPanel.Controls.Add(_lineItemsGrid, 0, 2);
        rightPanel.Controls.Add(_docsSplit, 0, 3);

        ConfigureSafeSplitterDistance(_mainSplit, preferredDistance: 560, panel1MinSize: 350, panel2MinSize: 320);

        _mainSplit.Panel1.Controls.Add(_quotesGrid);
        _mainSplit.Panel1.Controls.Add(new Label
        {
            Text = "Purchasing queue (Won quotes passed from Quotes)",
            Dock = DockStyle.Top,
            Height = 24,
            Font = new Font(Font, FontStyle.Bold)
        });

        _mainSplit.Panel2.Controls.Add(rightPanel);

        Controls.Add(_mainSplit);
        Controls.Add(actionsPanel);
        Controls.Add(_feedback);

        _quotesGrid.SelectionChanged += (_, _) => ShowSelectedQuoteDocuments();
        _technicalDocsGrid.CellDoubleClick += async (_, args) => await OpenBlobFromGridAsync(_technicalDocsGrid, args.RowIndex);
        _purchaseDocsGrid.CellDoubleClick += async (_, args) => await OpenBlobFromGridAsync(_purchaseDocsGrid, args.RowIndex);
        _mainSplit.SplitterMoved += async (_, _) => await SaveLayoutPreferenceAsync();
        _docsSplit.SplitterMoved += async (_, _) => await SaveLayoutPreferenceAsync();
        Load += async (_, _) => await RestoreLayoutPreferenceAsync();

        _ = LoadPurchasingQuotesAsync();
    }

    private async Task RestoreLayoutPreferenceAsync()
    {
        try
        {
            var layout = await _userRepository.GetPurchasingLayoutAsync(_currentUserId);
            if (layout is null)
            {
                return;
            }

            _restoringLayout = true;
            ApplySafeSplitterDistance(_mainSplit, GetDistanceFromProportion(_mainSplit, layout.LeftPanelProportion), _mainSplit.Panel1MinSize, _mainSplit.Panel2MinSize);
            ApplySafeSplitterDistance(_docsSplit, GetDistanceFromProportion(_docsSplit, layout.RightTopPanelProportion), _docsSplit.Panel1MinSize, _docsSplit.Panel2MinSize);
        }
        catch
        {
            // Ignore malformed preference values and keep defaults.
        }
        finally
        {
            _restoringLayout = false;
        }
    }

    private async Task SaveLayoutPreferenceAsync()
    {
        if (_restoringLayout || !IsHandleCreated)
        {
            return;
        }

        var preference = new PurchasingLayoutSetting
        {
            UserId = _currentUserId,
            LeftPanelProportion = GetPanelProportion(_mainSplit),
            RightTopPanelProportion = GetPanelProportion(_docsSplit)
        };
        preference.RightBottomPanelProportion = Math.Clamp(1d - preference.RightTopPanelProportion, 0.01d, 0.99d);

        await _userRepository.SavePurchasingLayoutAsync(preference);
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

    private static double GetPanelProportion(SplitContainer splitContainer)
    {
        var totalSize = splitContainer.Orientation == Orientation.Vertical
            ? splitContainer.ClientSize.Width
            : splitContainer.ClientSize.Height;

        if (totalSize <= splitContainer.SplitterWidth)
        {
            return 0.5d;
        }

        var availableForPanels = totalSize - splitContainer.SplitterWidth;
        if (availableForPanels <= 0)
        {
            return 0.5d;
        }

        return Math.Clamp(splitContainer.SplitterDistance / (double)availableForPanels, 0.01d, 0.99d);
    }

    private static int GetDistanceFromProportion(SplitContainer splitContainer, double proportion)
    {
        var totalSize = splitContainer.Orientation == Orientation.Vertical
            ? splitContainer.ClientSize.Width
            : splitContainer.ClientSize.Height;

        if (totalSize <= splitContainer.SplitterWidth)
        {
            return splitContainer.SplitterDistance;
        }

        var availableForPanels = totalSize - splitContainer.SplitterWidth;
        var clampedProportion = Math.Clamp(proportion, 0.01d, 0.99d);
        return (int)Math.Round(availableForPanels * clampedProportion);
    }

    private void ConfigureGrids()
    {
        _quotesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Quote #", DataPropertyName = nameof(Quote.Id), Width = 80 });
        _quotesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Lifecycle", DataPropertyName = nameof(Quote.LifecycleQuoteId), Width = 150 });
        _quotesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Customer", DataPropertyName = nameof(Quote.CustomerName), Width = 210 });
        _quotesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Passed To Purchasing", DataPropertyName = nameof(Quote.PassedToPurchasingUtc), Width = 180 });

        _lineItemsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Line #", DataPropertyName = nameof(PurchasingLineItemRow.DisplayLineNumber), Width = 58 });
        _lineItemsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Description", DataPropertyName = nameof(PurchasingLineItemRow.Description), Width = 190 });
        _lineItemsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Qty", DataPropertyName = nameof(PurchasingLineItemRow.Quantity), Width = 80 });
        _lineItemsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Drawing", DataPropertyName = nameof(PurchasingLineItemRow.DrawingNumber), Width = 120 });
        _lineItemsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Purchase Doc", DataPropertyName = nameof(PurchasingLineItemRow.PurchaseDocumentationStatus), Width = 130 });

        _technicalDocsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "File", DataPropertyName = nameof(QuoteBlobAttachment.FileName), Width = 210 });
        _technicalDocsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Type", DataPropertyName = nameof(QuoteBlobAttachment.BlobType), Width = 110 });
        _technicalDocsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Uploaded By", DataPropertyName = nameof(QuoteBlobAttachment.UploadedBy), Width = 130 });
        _technicalDocsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Uploaded", DataPropertyName = nameof(QuoteBlobAttachment.UploadedUtc), Width = 150 });

        _purchaseDocsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "File", DataPropertyName = nameof(QuoteBlobAttachment.FileName), Width = 210 });
        _purchaseDocsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Line Item", DataPropertyName = nameof(QuoteBlobAttachment.LineItemId), Width = 70 });
        _purchaseDocsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Uploaded By", DataPropertyName = nameof(QuoteBlobAttachment.UploadedBy), Width = 130 });
        _purchaseDocsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Uploaded", DataPropertyName = nameof(QuoteBlobAttachment.UploadedUtc), Width = 150 });
    }

    private async Task LoadPurchasingQuotesAsync()
    {
        var selectedQuoteId = GetSelectedQuote()?.Id;
        var quotes = await _quoteRepository.GetPurchasingQuotesAsync();
        var jobs = await _productionRepository.GetJobsAsync();
        var inProductionQuoteIds = jobs.Where(x => x.SourceQuoteId.HasValue).Select(x => x.SourceQuoteId!.Value).ToHashSet();

        var queue = quotes.Where(q => !inProductionQuoteIds.Contains(q.Id)).ToList();
        _quotesGrid.DataSource = queue;
        _feedback.Text = $"Loaded {queue.Count} quotes in Purchasing.";

        if (_quotesGrid.Rows.Count > 0)
        {
            var targetRow = _quotesGrid.Rows.Cast<DataGridViewRow>()
                .FirstOrDefault(row => row.DataBoundItem is Quote quote && quote.Id == selectedQuoteId)
                ?? _quotesGrid.Rows[0];
            targetRow.Selected = true;
            _quotesGrid.CurrentCell = targetRow.Cells[0];
        }

        ShowSelectedQuoteDocuments();
    }

    private Quote? GetSelectedQuote() => _quotesGrid.CurrentRow?.DataBoundItem as Quote;

    private QuoteLineItem? GetSelectedLineItem()
    {
        var quote = GetSelectedQuote();
        var rowModel = _lineItemsGrid.CurrentRow?.DataBoundItem as PurchasingLineItemRow;
        if (quote is null || rowModel is null)
        {
            return null;
        }

        return quote.LineItems.FirstOrDefault(li => li.Id == rowModel.LineItemId);
    }

    private void ShowSelectedQuoteDocuments()
    {
        var selected = GetSelectedQuote();
        if (selected is null)
        {
            _lineItemsGrid.DataSource = new List<PurchasingLineItemRow>();
            _technicalDocsGrid.DataSource = new List<QuoteBlobAttachment>();
            _purchaseDocsGrid.DataSource = new List<QuoteBlobAttachment>();
            _checklistLabel.Text = "Checklist: Select a quote to review docs.";
            return;
        }

        var lineItems = selected.LineItems
            .Select((line, index) => new PurchasingLineItemRow
            {
                LineItemId = line.Id,
                DisplayLineNumber = index + 1,
                Description = line.Description,
                DrawingNumber = line.DrawingNumber,
                Quantity = line.Quantity,
                PurchaseDocumentationStatus = line.BlobAttachments.Any(x => x.BlobType == QuoteBlobType.PurchaseDocumentation) ? "✅ Uploaded" : "⬜ Missing"
            })
            .ToList();

        var allBlobs = selected.LineItems.SelectMany(x => x.BlobAttachments).ToList();
        var technical = allBlobs.Where(x => x.BlobType == QuoteBlobType.Technical).OrderByDescending(x => x.UploadedUtc).ToList();
        var purchasingDocs = allBlobs.Where(x => x.BlobType == QuoteBlobType.PurchaseDocumentation).OrderByDescending(x => x.UploadedUtc).ToList();
        var missingDocCount = selected.LineItems.Count(line => line.BlobAttachments.All(blob => blob.BlobType != QuoteBlobType.PurchaseDocumentation));

        _lineItemsGrid.DataSource = lineItems;
        _technicalDocsGrid.DataSource = technical;
        _purchaseDocsGrid.DataSource = purchasingDocs;
        _checklistLabel.Text = missingDocCount == 0
            ? "Checklist: ✅ All purchasable items include purchase documentation. Ready to pass to Production."
            : $"Checklist: ☐ {missingDocCount} line item(s) still missing purchase documentation.";

        if (_lineItemsGrid.Rows.Count > 0)
        {
            _lineItemsGrid.Rows[0].Selected = true;
            _lineItemsGrid.CurrentCell = _lineItemsGrid.Rows[0].Cells[0];
        }
    }

    private async Task UploadPurchaseDocumentAsync()
    {
        var quote = GetSelectedQuote();
        if (quote is null)
        {
            _feedback.Text = "Select a Purchasing quote first.";
            return;
        }

        var targetLine = GetSelectedLineItem();
        if (targetLine is null)
        {
            _feedback.Text = "Select an item in 'Items to purchase' before uploading documentation.";
            return;
        }

        using var picker = new OpenFileDialog
        {
            Title = $"Upload purchase documentation for Q{quote.Id} / Line {targetLine.Id}",
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

        _feedback.Text = $"Uploaded purchase documentation for quote {quote.Id}, line item {targetLine.Id}.";
        await LoadPurchasingQuotesAsync();
    }

    private async Task DeleteSelectedPurchaseDocumentAsync()
    {
        var selectedBlob = _purchaseDocsGrid.CurrentRow?.DataBoundItem as QuoteBlobAttachment;
        if (selectedBlob is null)
        {
            _feedback.Text = "Select a purchase documentation file to remove.";
            return;
        }

        await _quoteRepository.DeleteQuoteLineItemFileAsync(selectedBlob.Id);
        _feedback.Text = $"Removed purchase documentation '{selectedBlob.FileName}'.";
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

        var missingDocumentation = quote.LineItems
            .Where(line => line.BlobAttachments.All(blob => blob.BlobType != QuoteBlobType.PurchaseDocumentation))
            .ToList();

        if (missingDocumentation.Count > 0)
        {
            _feedback.Text = $"Upload required purchase documentation for all line items before moving to Production. Missing: {missingDocumentation.Count} item(s).";
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

    private async Task OpenBlobFromGridAsync(DataGridView grid, int rowIndex)
    {
        if (rowIndex < 0)
        {
            return;
        }

        if (grid.Rows[rowIndex].DataBoundItem is not QuoteBlobAttachment attachment)
        {
            return;
        }

        var blobBytes = attachment.BlobData.Length > 0
            ? attachment.BlobData
            : await _quoteRepository.GetQuoteBlobContentAsync(attachment.Id);
        if (blobBytes.Length == 0)
        {
            _feedback.Text = $"Unable to open '{attachment.FileName}' because no file data is available.";
            return;
        }

        var extension = string.IsNullOrWhiteSpace(attachment.Extension) ? Path.GetExtension(attachment.FileName) : attachment.Extension;
        var tempFileName = $"erp-quote-{attachment.Id}-{Guid.NewGuid():N}{extension}";
        var tempPath = Path.Combine(Path.GetTempPath(), tempFileName);
        await File.WriteAllBytesAsync(tempPath, blobBytes);

        Process.Start(new ProcessStartInfo
        {
            FileName = tempPath,
            UseShellExecute = true
        });

        _feedback.Text = $"Opened '{attachment.FileName}' from blob storage.";
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

    private sealed class PurchasingLineItemRow
    {
        public int LineItemId { get; init; }
        public int DisplayLineNumber { get; init; }
        public string Description { get; init; } = string.Empty;
        public string DrawingNumber { get; init; } = string.Empty;
        public decimal Quantity { get; init; }
        public string PurchaseDocumentationStatus { get; init; } = string.Empty;
    }
}
