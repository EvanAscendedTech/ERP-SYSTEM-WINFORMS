using System.Diagnostics;
using System.Security.Cryptography;
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
    private readonly TableLayoutPanel _requirementsChecklistPanel = new() { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 1, Padding = new Padding(0, 4, 0, 4) };
    private readonly Button _passToProductionButton;
    private readonly Dictionary<string, ChecklistResolution> _checklistState = new(StringComparer.OrdinalIgnoreCase);
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
        _passToProductionButton = new Button { Text = "Pass to Production", AutoSize = true, Enabled = false };

        refreshButton.Click += async (_, _) => await LoadPurchasingQuotesAsync();
        _passToProductionButton.Click += async (_, _) => await PassToProductionAsync();

        actionsPanel.Controls.Add(refreshButton);
        actionsPanel.Controls.Add(_passToProductionButton);

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
            Text = "Purchase documentation (double-click to open)",
            Width = 300,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold)
        });
        purchaseDocsHeaderPanel.Controls.Add(uploadPurchaseDocButton);
        purchaseDocsHeaderPanel.Controls.Add(removePurchaseDocButton);

        _docsSplit.Panel2.Controls.Add(_purchaseDocsGrid);
        _docsSplit.Panel2.Controls.Add(purchaseDocsHeaderPanel);

        var checklistHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        checklistHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        checklistHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        checklistHost.Controls.Add(new Label
        {
            Text = "Step-by-step purchasing checklist",
            Dock = DockStyle.Fill,
            Height = 24,
            Font = new Font(Font, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        checklistHost.Controls.Add(_requirementsChecklistPanel, 0, 1);

        var rightPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 0, 0, 0),
            ColumnCount = 1,
            RowCount = 6
        };
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 170f));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 230f));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
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
        rightPanel.Controls.Add(checklistHost, 0, 3);
        rightPanel.Controls.Add(new Label
        {
            Text = "Reference documents",
            Dock = DockStyle.Fill,
            Height = 24,
            Font = new Font(Font, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 4);
        rightPanel.Controls.Add(_docsSplit, 0, 5);

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
        _lineItemsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Requirements", DataPropertyName = nameof(PurchasingLineItemRow.RequirementSummary), Width = 220 });

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
            _requirementsChecklistPanel.Controls.Clear();
            _requirementsChecklistPanel.RowStyles.Clear();
            _checklistLabel.Text = "Checklist: Select a quote to review docs.";
            UpdatePassToProductionState();
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
                RequirementSummary = BuildRequirementSummary(line)
            })
            .ToList();

        var allBlobs = selected.LineItems.SelectMany(x => x.BlobAttachments).ToList();
        var technical = allBlobs.Where(x => x.BlobType == QuoteBlobType.Technical).OrderByDescending(x => x.UploadedUtc).ToList();
        var purchasingDocs = allBlobs.Where(x => x.BlobType == QuoteBlobType.PurchaseDocumentation).OrderByDescending(x => x.UploadedUtc).ToList();

        _lineItemsGrid.DataSource = lineItems;
        _technicalDocsGrid.DataSource = technical;
        _purchaseDocsGrid.DataSource = purchasingDocs;

        RenderDynamicChecklist(selected);
    }

    private string BuildRequirementSummary(QuoteLineItem line)
    {
        var requirements = BuildRequirementsForLine(line);
        return requirements.Count == 0 ? "No flagged docs" : string.Join(", ", requirements.Select(x => x.DisplayName));
    }

    private static List<RequirementDefinition> BuildRequirementsForLine(QuoteLineItem line)
    {
        var requirements = new List<RequirementDefinition>();

        if (line.RequiresDfars)
        {
            requirements.Add(new RequirementDefinition("DfarsCompliance", "Confirm DFARS compliance (Yes/No)", RequirementInputType.YesNo));
        }

        if (line.RequiresMaterialTestReport)
        {
            requirements.Add(new RequirementDefinition("MaterialTestReport", "Upload material test report", RequirementInputType.FileUpload));
        }

        if (line.RequiresCertificateOfConformance)
        {
            requirements.Add(new RequirementDefinition("CertificateOfConformance", "Upload certificate of conformance", RequirementInputType.FileUpload));
        }

        if (line.RequiresSecondaryOperations)
        {
            requirements.Add(new RequirementDefinition("PostOperationStatus", "Confirm post-operation status (Yes/No)", RequirementInputType.YesNo));
        }

        return requirements;
    }

    private void RenderDynamicChecklist(Quote quote)
    {
        _requirementsChecklistPanel.SuspendLayout();
        _requirementsChecklistPanel.Controls.Clear();
        _requirementsChecklistPanel.RowStyles.Clear();

        var requirements = quote.LineItems
            .SelectMany((line, lineIndex) => BuildRequirementsForLine(line)
                .Select(requirement => new ChecklistRowModel(
                    line,
                    lineIndex + 1,
                    requirement,
                    BuildStateKey(quote.Id, line.Id, requirement.Key))))
            .ToList();

        if (requirements.Count == 0)
        {
            _requirementsChecklistPanel.RowCount = 1;
            _requirementsChecklistPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            _requirementsChecklistPanel.Controls.Add(new Label
            {
                Text = "Step 1: No quote requirement flags were set. This quote is ready when base purchasing docs are complete.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            _checklistLabel.Text = "Checklist: ✅ No quote-driven requirements were flagged.";
            _requirementsChecklistPanel.ResumeLayout(true);
            UpdatePassToProductionState();
            return;
        }

        _requirementsChecklistPanel.RowCount = requirements.Count;

        for (var i = 0; i < requirements.Count; i++)
        {
            _requirementsChecklistPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 92f));
            var rowModel = requirements[i];
            var row = BuildChecklistRow(quote, rowModel, i + 1);
            _requirementsChecklistPanel.Controls.Add(row, 0, i);
        }

        _requirementsChecklistPanel.ResumeLayout(true);
        UpdateChecklistHeader(quote, requirements);
        UpdatePassToProductionState();
    }

    private Control BuildChecklistRow(Quote quote, ChecklistRowModel rowModel, int stepNumber)
    {
        var rowPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 86,
            ColumnCount = 3,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 6),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
            BackColor = Color.FromArgb(247, 250, 254)
        };
        rowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56f));
        rowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24f));
        rowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
        rowPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 55f));
        rowPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 45f));

        var title = new Label
        {
            Text = $"Step {stepNumber}: Line {rowModel.DisplayLineNumber} - {rowModel.Requirement.DisplayName}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold)
        };

        var actionHost = new Panel { Dock = DockStyle.Fill };

        if (rowModel.Requirement.InputType == RequirementInputType.FileUpload)
        {
            var uploadButton = new Button
            {
                Text = "Upload file",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Enabled = _canEdit
            };
            uploadButton.Click += async (_, _) =>
            {
                await UploadRequirementDocumentAsync(quote, rowModel.LineItem, rowModel.Requirement);
                SetState(rowModel.StateKey, new ChecklistResolution(ChecklistResolutionType.Uploaded, "Uploaded"));
                UpdateChecklistAfterAction(quote);
            };
            actionHost.Controls.Add(uploadButton);
        }
        else
        {
            var yesNoSelector = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 200,
                Enabled = _canEdit
            };
            yesNoSelector.Items.Add("Select response...");
            yesNoSelector.Items.Add("Yes");
            yesNoSelector.Items.Add("No");
            yesNoSelector.SelectedIndex = 0;
            yesNoSelector.SelectedIndexChanged += (_, _) =>
            {
                if (yesNoSelector.SelectedIndex == 1)
                {
                    SetState(rowModel.StateKey, new ChecklistResolution(ChecklistResolutionType.ConfirmedYes, "Confirmed: Yes"));
                }
                else if (yesNoSelector.SelectedIndex == 2)
                {
                    SetState(rowModel.StateKey, new ChecklistResolution(ChecklistResolutionType.ConfirmedNo, "Confirmed: No"));
                }
                else
                {
                    SetState(rowModel.StateKey, new ChecklistResolution(ChecklistResolutionType.Pending, "Pending"));
                }

                UpdateChecklistAfterAction(quote);
            };
            actionHost.Controls.Add(yesNoSelector);
        }

        var instructionLabel = new Label
        {
            Text = rowModel.Requirement.InputType == RequirementInputType.FileUpload
                ? "Action: Upload the required document"
                : "Action: Record Yes/No response",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var statusLabel = new Label
        {
            Text = GetState(rowModel.StateKey).Label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.MidnightBlue
        };

        rowPanel.Controls.Add(title, 0, 0);
        rowPanel.SetColumnSpan(title, 3);
        rowPanel.Controls.Add(instructionLabel, 0, 1);
        rowPanel.Controls.Add(actionHost, 1, 1);
        rowPanel.Controls.Add(statusLabel, 2, 1);

        return rowPanel;
    }

    private async Task UploadRequirementDocumentAsync(Quote quote, QuoteLineItem line, RequirementDefinition requirement)
    {
        using var picker = new OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
            Title = $"{requirement.DisplayName} for Q{quote.Id} / Line {line.Id}",
            Multiselect = false,
            CheckFileExists = true,
            RestoreDirectory = true
        };

        if (picker.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(picker.FileName))
        {
            return;
        }

        var bytes = await File.ReadAllBytesAsync(picker.FileName);
        using var sha = SHA256.Create();
        var digest = sha.ComputeHash(bytes);
        var extension = Path.GetExtension(picker.FileName);
        var prefixedName = $"{requirement.Key}-{Path.GetFileName(picker.FileName)}";

        await _quoteRepository.InsertQuoteLineItemFileAsync(
            quote.Id,
            line.Id,
            quote.LifecycleQuoteId,
            QuoteBlobType.PurchaseDocumentation,
            prefixedName,
            extension,
            bytes.LongLength,
            digest,
            _actorUserId,
            DateTime.UtcNow,
            bytes);

        _feedback.Text = $"{requirement.DisplayName} uploaded for quote {quote.Id}, line {line.Id}.";
        await LoadPurchasingQuotesAsync();
    }

    private void UpdateChecklistAfterAction(Quote quote)
    {
        var allRows = quote.LineItems
            .SelectMany(line => BuildRequirementsForLine(line).Select(requirement => BuildStateKey(quote.Id, line.Id, requirement.Key)))
            .ToList();

        var completed = allRows.Count(IsChecklistRowSatisfied);
        var total = allRows.Count;

        _checklistLabel.Text = total == 0
            ? "Checklist: ✅ No quote-driven requirements were flagged."
            : $"Checklist: Step {Math.Min(completed + 1, total)}/{total} • Completed {completed}/{total}";

        foreach (Control panel in _requirementsChecklistPanel.Controls)
        {
            if (panel is not TableLayoutPanel row || row.Controls.Count == 0)
            {
                continue;
            }

            if (row.Controls.OfType<Label>().LastOrDefault() is Label status)
            {
                var rowTitle = row.Controls.OfType<Label>().FirstOrDefault()?.Text ?? string.Empty;
                var stateKey = FindStateKeyByTitle(quote, rowTitle);
                if (!string.IsNullOrWhiteSpace(stateKey))
                {
                    status.Text = GetState(stateKey).Label;
                }
            }
        }

        UpdatePassToProductionState();
    }

    private void UpdateChecklistHeader(Quote quote, IReadOnlyCollection<ChecklistRowModel> rows)
    {
        var satisfied = rows.Count(row => IsChecklistRowSatisfied(row.StateKey));
        _checklistLabel.Text = rows.Count == 0
            ? "Checklist: ✅ No quote-driven requirements were flagged."
            : $"Checklist: Step {Math.Min(satisfied + 1, rows.Count)}/{rows.Count} • Completed {satisfied}/{rows.Count}";
    }

    private string? FindStateKeyByTitle(Quote quote, string title)
    {
        foreach (var (line, index) in quote.LineItems.Select((line, lineIndex) => (line, lineIndex + 1)))
        {
            foreach (var requirement in BuildRequirementsForLine(line))
            {
                if (title.Contains($"Line {index} - {requirement.DisplayName}", StringComparison.OrdinalIgnoreCase))
                {
                    return BuildStateKey(quote.Id, line.Id, requirement.Key);
                }
            }
        }

        return null;
    }

    private ChecklistResolution GetState(string key)
        => _checklistState.TryGetValue(key, out var state) ? state : new ChecklistResolution(ChecklistResolutionType.Pending, "Pending");

    private void SetState(string key, ChecklistResolution resolution)
        => _checklistState[key] = resolution;

    private bool IsChecklistRowSatisfied(string stateKey)
    {
        var state = GetState(stateKey);
        return state.Type is ChecklistResolutionType.Uploaded or ChecklistResolutionType.ConfirmedYes or ChecklistResolutionType.ConfirmedNo;
    }

    private bool AreAllRequirementsSatisfied(Quote quote)
    {
        var requirementKeys = quote.LineItems
            .SelectMany(line => BuildRequirementsForLine(line).Select(requirement => BuildStateKey(quote.Id, line.Id, requirement.Key)))
            .ToList();

        return requirementKeys.All(IsChecklistRowSatisfied);
    }

    private static string BuildStateKey(int quoteId, int lineId, string requirementKey)
        => $"{quoteId}:{lineId}:{requirementKey}";

    private void UpdatePassToProductionState()
    {
        var quote = GetSelectedQuote();
        var enabled = _canEdit && quote is not null && quote.Status == QuoteStatus.Won && AreAllRequirementsSatisfied(quote);
        _passToProductionButton.Enabled = enabled;
    }

    private async Task UploadPurchaseDocumentAsync()
    {
        var quote = GetSelectedQuote();
        var targetLine = GetSelectedLineItem();
        if (quote is null || targetLine is null)
        {
            _feedback.Text = "Select an item in 'Items to purchase' before uploading documentation.";
            return;
        }

        using var picker = new OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
            Title = $"Upload purchase documentation for Q{quote.Id} / Line {targetLine.Id}",
            Multiselect = false,
            CheckFileExists = true,
            RestoreDirectory = true
        };

        if (picker.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(picker.FileName))
        {
            return;
        }

        var filePath = picker.FileName;
        var bytes = await File.ReadAllBytesAsync(filePath);
        using var sha = SHA256.Create();
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

        if (!AreAllRequirementsSatisfied(quote))
        {
            _feedback.Text = "Complete each checklist step (upload, inventory select, or not required) before moving to Production.";
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
        public string RequirementSummary { get; init; } = string.Empty;
    }

    private sealed record RequirementDefinition(string Key, string DisplayName, RequirementInputType InputType);
    private sealed record ChecklistResolution(ChecklistResolutionType Type, string Label);
    private enum RequirementInputType
    {
        FileUpload,
        YesNo
    }

    private enum ChecklistResolutionType
    {
        Pending,
        Uploaded,
        ConfirmedYes,
        ConfirmedNo
    }

    private sealed record ChecklistRowModel(QuoteLineItem LineItem, int DisplayLineNumber, RequirementDefinition Requirement, string StateKey);
}
