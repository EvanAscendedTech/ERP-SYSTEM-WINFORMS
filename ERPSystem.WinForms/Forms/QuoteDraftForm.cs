using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ERPSystem.WinForms.Controls;
using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Forms;

public class QuoteDraftForm : Form
{
    private const int DefaultLineItemWidth = 860;
    private const int DefaultLineItemHeight = 420;
    private const int MinimumLineItemWidth = 620;
    private const int MinimumLineItemHeight = 390;
    private const int MaximumLineItemHeight = 460;
    private const int MinimumBlobAreaWidth = 170;
    private const int MinimumBlobAreaHeight = 70;
    private const int FixedBlobAreaHeight = 178;
    private const int CollapsedViewerHeight = 220;
    private const int MinimumTextBoxHeight = 24;
    private const int StandardGap = 8;
    private static readonly Color[] LineItemColorCycle =
    {
        Color.FromArgb(232, 245, 233),
        Color.FromArgb(227, 242, 253),
        Color.FromArgb(243, 229, 245)
    };
    private readonly QuoteRepository _quoteRepository;
    private readonly bool _canViewPricing;
    private readonly string _uploadedBy;
    private readonly ComboBox _customerPicker = new() { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _customerPartPo = new() { Width = 220, PlaceholderText = "Customer Part PO" };
    private readonly TextBox _quoteLifecycleId = new() { Width = 220, ReadOnly = true };
    private readonly FlowLayoutPanel _lineItemsPanel = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(4), Margin = Padding.Empty };
    private readonly Label _totalHoursValue = new() { AutoSize = true, Text = "0.00" };
    private readonly Label _masterTotalValue = new() { AutoSize = true, Text = "$0.00" };
    private readonly Quote? _editingQuote;
    private readonly UserAccount? _actorUser;
    private readonly List<LineItemCard> _lineItemCards = new();
    private readonly StepFileParser _stepFileParser = new();
    private readonly SolidModelFileTypeDetector _solidModelTypeDetector = new();
    private readonly StepParsingDiagnosticsLog _stepParsingDiagnosticsLog;
    private decimal _shopHourlyRate;

    public int CreatedQuoteId { get; private set; }
    public bool WasDeleted { get; private set; }

    public QuoteDraftForm(
        QuoteRepository quoteRepository,
        bool canViewPricing,
        string uploadedBy,
        StepParsingDiagnosticsLog? stepParsingDiagnosticsLog = null,
        Quote? editingQuote = null,
        UserAccount? actorUser = null)
    {
        _quoteRepository = quoteRepository;
        _canViewPricing = canViewPricing;
        _uploadedBy = uploadedBy;
        _stepParsingDiagnosticsLog = stepParsingDiagnosticsLog ?? new StepParsingDiagnosticsLog();
        _editingQuote = editingQuote;
        _actorUser = actorUser;
        _quoteLifecycleId.Text = string.IsNullOrWhiteSpace(editingQuote?.LifecycleQuoteId) ? GenerateLifecycleQuoteId() : editingQuote.LifecycleQuoteId;

        Text = _editingQuote is null ? $"New Quote Draft - {_quoteLifecycleId.Text}" : $"Edit In-Process Quote #{_editingQuote.Id} - {_quoteLifecycleId.Text}";
        Width = 1300;
        Height = 780;
        MinimumSize = new Size(1100, 700);
        WindowState = FormWindowState.Maximized;
        StartPosition = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 6)
        };
        for (var i = 0; i < header.ColumnCount; i++)
        {
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        }
        for (var i = 0; i < header.RowCount; i++)
        {
            header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var addLineButton = BuildCompactIconButton("➕ Add", Color.FromArgb(37, 99, 235));
        addLineButton.Anchor = AnchorStyles.Left;
        addLineButton.Click += (_, _) => AddLineItemCard();
        header.Controls.Add(NewFieldPanel("Customer", _customerPicker), 0, 0);
        header.Controls.Add(NewFieldPanel("Customer Part PO", _customerPartPo), 1, 0);
        header.Controls.Add(NewFieldPanel("Lifecycle ID", _quoteLifecycleId), 2, 0);
        header.Controls.Add(addLineButton, 3, 0);

        var totalsPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = true, Padding = new Padding(0, 4, 0, 4) };
        totalsPanel.Controls.Add(new Label { Text = "In-Process Quote Summary:", AutoSize = true, Margin = new Padding(0, 8, 8, 0), Font = new Font(Font, FontStyle.Bold) });
        totalsPanel.Controls.Add(new Label { Text = "Total Hours:", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        totalsPanel.Controls.Add(_totalHoursValue);
        totalsPanel.Controls.Add(new Label { Text = "Total Cost:", AutoSize = true, Margin = new Padding(20, 8, 0, 0) });
        totalsPanel.Controls.Add(_masterTotalValue);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Margin = new Padding(0, StandardGap, 0, 0) };
        var saveButton = BuildCompactIconButton("💾 Save", Color.FromArgb(22, 163, 74));
        saveButton.Click += async (_, _) => await SaveQuoteAsync();
        buttons.Controls.Add(saveButton);

        if (_editingQuote is not null)
        {
            var deleteButton = BuildCompactIconButton("🗑 Delete", Color.Firebrick);
            deleteButton.Click += async (_, _) => await DeleteQuoteAsync();
            buttons.Controls.Add(deleteButton);
        }

        var quoteInfoSection = BuildSectionGroup("Quote Header", header);
        root.Controls.Add(quoteInfoSection, 0, 0);
        root.Controls.Add(_lineItemsPanel, 0, 1);
        root.Controls.Add(totalsPanel, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        Controls.Add(root);

        _lineItemsPanel.Resize += (_, _) => ResizeCards();
        Resize += (_, _) => ResizeCards();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        _shopHourlyRate = await _quoteRepository.GetShopHourlyRateAsync();
        await LoadCustomersAsync();
    }

    private static string GenerateLifecycleQuoteId() => $"Q-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

    private async Task LoadCustomersAsync()
    {
        var customers = await _quoteRepository.GetCustomersAsync();
        _customerPicker.DataSource = customers.ToList();
        _customerPicker.DisplayMember = nameof(Customer.Name);
        _customerPicker.ValueMember = nameof(Customer.Id);

        if (_editingQuote is not null)
        {
            PopulateFromQuote(_editingQuote);
            return;
        }

        if (_lineItemCards.Count == 0)
        {
            AddLineItemCard();
        }

        RecalculateQuoteTotals();
    }

    private void PopulateFromQuote(Quote quote)
    {
        _customerPicker.SelectedValue = quote.CustomerId;
        _customerPartPo.Text = ParseMetadata(quote.LineItems.FirstOrDefault()?.Notes).GetValueOrDefault("Customer Part PO", string.Empty);
        _shopHourlyRate = quote.ShopHourlyRateSnapshot > 0 ? quote.ShopHourlyRateSnapshot : _shopHourlyRate;

        _lineItemsPanel.Controls.Clear();
        _lineItemCards.Clear();
        foreach (var line in quote.LineItems)
        {
            AddLineItemCard(line);
        }

        if (_lineItemCards.Count == 0)
        {
            AddLineItemCard();
        }

        RecalculateQuoteTotals();
    }

    private void AddLineItemCard(QuoteLineItem? source = null)
    {
        var model = source ?? new QuoteLineItem();
        if (source is not null)
        {
            model = new QuoteLineItem
            {
                Id = source.Id,
                QuoteId = source.QuoteId,
                Description = source.Description,
                DrawingNumber = source.DrawingNumber,
                DrawingName = source.DrawingName,
                Revision = source.Revision,
                ProductionHours = source.ProductionHours,
                SetupHours = source.SetupHours,
                MaterialCost = source.MaterialCost,
                ToolingCost = source.ToolingCost,
                SecondaryOperationsCost = source.SecondaryOperationsCost,
                Quantity = source.Quantity,
                UnitPrice = source.UnitPrice,
                LineItemTotal = source.LineItemTotal,
                BlobAttachments = source.BlobAttachments.Select(x => new QuoteBlobAttachment
                {
                    Id = x.Id,
                    QuoteId = x.QuoteId,
                    LineItemId = x.LineItemId,
                    LifecycleId = x.LifecycleId,
                    BlobType = x.BlobType,
                    FileName = x.FileName,
                    Extension = x.Extension,
                    ContentType = x.ContentType,
                    FileSizeBytes = x.FileSizeBytes,
                    Sha256 = x.Sha256,
                    UploadedBy = x.UploadedBy,
                    StorageRelativePath = x.StorageRelativePath,
                    BlobData = x.BlobData,
                    UploadedUtc = x.UploadedUtc
                }).ToList()
            };
        }

        var card = BuildCard(model, _lineItemCards.Count);
        _lineItemCards.Add(card);
        _lineItemsPanel.Controls.Add(card.Container);
        RenumberLineItems();
        ResizeCards();
        RecalculateQuoteTotals();
    }

    private LineItemCard BuildCard(QuoteLineItem model, int index)
    {
        var cardPanel = new Panel
        {
            AutoSize = false,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 0, 6),
            Padding = new Padding(6),
            BackColor = GetLineItemBackgroundColor(index),
            MinimumSize = new Size(MinimumLineItemWidth, MinimumLineItemHeight),
            MaximumSize = new Size(int.MaxValue, MaximumLineItemHeight),
            Height = DefaultLineItemHeight
        };
        cardPanel.SuspendLayout();

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = false, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty, Padding = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var contentScroller = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        var contentGrid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 5,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        for (var i = 0; i < contentGrid.RowCount; i++)
        {
            contentGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var headerRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 4)
        };
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Anchor = AnchorStyles.Left, Margin = Padding.Empty };

        var actionButtons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
        var doneButton = BuildCompactIconButton("✅ Done", Color.FromArgb(22, 163, 74));
        var editButton = BuildCompactIconButton("✏️ Edit", Color.FromArgb(37, 99, 235));
        editButton.Visible = false;
        var removeButton = BuildCompactIconButton("🗑 Remove", Color.FromArgb(214, 77, 77));
        actionButtons.Controls.Add(doneButton);
        actionButtons.Controls.Add(editButton);
        actionButtons.Controls.Add(removeButton);

        headerRow.Controls.Add(title, 0, 0);
        headerRow.Controls.Add(actionButtons, 1, 0);
        layout.Controls.Add(headerRow, 0, 0);

        var detailsGrid = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Top, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 4) };
        detailsGrid.RowCount = 1;
        detailsGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (var i = 0; i < 3; i++) detailsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));

        var drawingNumber = NewDecimalDisabledField(model.DrawingNumber);
        var drawingName = NewDecimalDisabledField(model.DrawingName);
        var revision = NewDecimalDisabledField(model.Revision);

        detailsGrid.Controls.Add(NewFieldPanel("Drawing Number", drawingNumber), 0, 0);
        detailsGrid.Controls.Add(NewFieldPanel("Drawing Name", drawingName), 1, 0);
        detailsGrid.Controls.Add(NewFieldPanel("Revision", revision), 2, 0);

        var costsRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 8, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 4), RowCount = 1 };
        costsRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (var i = 0; i < 8; i++) costsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12.5f));

        var productionHours = NewNumericField(model.ProductionHours);
        var setupHours = NewNumericField(model.SetupHours);
        var materialCost = NewNumericField(model.MaterialCost);
        var toolingCost = NewNumericField(model.ToolingCost);
        var secondaryCost = NewNumericField(model.SecondaryOperationsCost);
        var quantity = NewNumericField(model.Quantity <= 0 ? 1 : model.Quantity);
        var totalBox = NewReadOnlySummaryField(model.LineItemTotal);
        var perPieceBox = NewReadOnlySummaryField(model.UnitPrice);

        foreach (var box in new[] { productionHours, setupHours, materialCost, toolingCost, secondaryCost, quantity })
        {
            box.TextChanged += (_, _) => RecalculateQuoteTotals();
        }

        costsRow.Controls.Add(NewFieldPanel("Production Hours", productionHours), 0, 0);
        costsRow.Controls.Add(NewFieldPanel("Setup Hours", setupHours), 1, 0);
        costsRow.Controls.Add(NewFieldPanel("Material Cost", materialCost), 2, 0);
        costsRow.Controls.Add(NewFieldPanel("Tooling Cost", toolingCost), 3, 0);
        costsRow.Controls.Add(NewFieldPanel("Secondary Ops", secondaryCost), 4, 0);
        costsRow.Controls.Add(NewFieldPanel("Quantity", quantity), 5, 0);
        costsRow.Controls.Add(NewFieldPanel("Line Item Total", totalBox), 6, 0);
        costsRow.Controls.Add(NewFieldPanel("Per-Piece", perPieceBox), 7, 0);

        var productionFlagsGrid = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            Dock = DockStyle.Top,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 4)
        };
        productionFlagsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        productionFlagsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        productionFlagsGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        productionFlagsGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var requiresDfars = new CheckBox { Text = "Requires DFARS", AutoSize = true, Checked = model.RequiresDfars, Margin = new Padding(0, 0, 4, 0) };
        var requiresMaterialTestReport = new CheckBox { Text = "Requires Material Test Report", AutoSize = true, Checked = model.RequiresMaterialTestReport, Margin = new Padding(0, 0, 4, 0) };
        var requiresCertificateOfConformance = new CheckBox { Text = "Requires Certificate of Conformance", AutoSize = true, Checked = model.RequiresCertificateOfConformance, Margin = new Padding(0, 0, 4, 0) };
        var requiresSecondaryOperations = new CheckBox { Text = "Requires Secondary Operations", AutoSize = true, Checked = model.RequiresSecondaryOperations, Margin = Padding.Empty };

        productionFlagsGrid.Controls.Add(requiresDfars, 0, 0);
        productionFlagsGrid.Controls.Add(requiresMaterialTestReport, 1, 0);
        productionFlagsGrid.Controls.Add(requiresCertificateOfConformance, 0, 1);
        productionFlagsGrid.Controls.Add(requiresSecondaryOperations, 1, 1);

        var attachmentsTabs = BuildAttachmentsTabs(new[]
        {
            (QuoteBlobType.Technical, "Drawings (PDF)"),
            (QuoteBlobType.ThreeDModel, "3D Models (STEP)"),
            (QuoteBlobType.MaterialPricing, "Materials (PDF)"),
            (QuoteBlobType.ToolingDocumentation, "Tooling (PDF)"),
            (QuoteBlobType.PostOpPricing, "Secondary Operations (PDF)")
        }, model);

        contentGrid.Controls.Add(BuildCompactSection("Details", detailsGrid), 0, 0);
        contentGrid.Controls.Add(BuildCompactSection("Costs", costsRow), 0, 1);
        contentGrid.Controls.Add(BuildCompactSection("Production Flags", productionFlagsGrid), 0, 2);
        contentGrid.Controls.Add(BuildCompactSection("Attachments", attachmentsTabs.TabControl), 0, 3);
        contentScroller.Controls.Add(contentGrid);
        layout.Controls.Add(contentScroller, 0, 1);

        var summaryView = BuildDoneSummaryView();
        summaryView.Container.Visible = false;
        layout.Controls.Add(summaryView.Container, 0, 1);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 0),
            Padding = Padding.Empty
        };
        footer.Controls.Add(new Label { Text = "Live totals update on edit", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 8, 0, 0) });
        layout.Controls.Add(footer, 0, 2);

        cardPanel.Controls.Add(layout);
        layout.ResumeLayout(true);
        cardPanel.ResumeLayout(true);

        var card = new LineItemCard
        {
            Container = cardPanel,
            Title = title,
            Model = model,
            DrawingNumber = drawingNumber,
            DrawingName = drawingName,
            Revision = revision,
            ProductionHours = productionHours,
            SetupHours = setupHours,
            MaterialCost = materialCost,
            ToolingCost = toolingCost,
            SecondaryCost = secondaryCost,
            Quantity = quantity,
            Total = totalBox,
            PerPiecePrice = perPieceBox,
            RequiresDfars = requiresDfars,
            RequiresMaterialTestReport = requiresMaterialTestReport,
            RequiresCertificateOfConformance = requiresCertificateOfConformance,
            RequiresSecondaryOperations = requiresSecondaryOperations,
            BlobLists = attachmentsTabs.BlobLists,
            BlobActionButtons = attachmentsTabs.ActionButtons,
            DoneButton = doneButton,
            EditButton = editButton,
            EditView = contentScroller,
            DoneView = summaryView.Container,
            StepViewer = summaryView.Viewer,
            SummaryDrawingNumber = summaryView.DrawingNumber,
            SummaryDrawingName = summaryView.DrawingName,
            SummaryRevision = summaryView.Revision,
            SummaryQuantity = summaryView.Quantity,
            SummaryLineTotal = summaryView.LineTotal,
            SummaryPerPiece = summaryView.PerPiece
        };

        removeButton.Click += async (_, _) => await RemoveLineItemAsync(model, cardPanel);
        doneButton.Click += async (_, _) => await SetLineItemDoneStateAsync(card, true);
        editButton.Click += async (_, _) => await SetLineItemDoneStateAsync(card, false);
        summaryView.ExpandButton.Click += (_, _) => ExpandModelViewer(card);

        RefreshBlobLists(card);
        return card;
    }

    private (TabControl TabControl, Dictionary<QuoteBlobType, ListView> BlobLists, Dictionary<QuoteBlobType, Button[]> ActionButtons) BuildAttachmentsTabs((QuoteBlobType Type, string Title)[] definitions, QuoteLineItem model)
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Point(12, 4),
            MinimumSize = new Size(0, FixedBlobAreaHeight),
            MaximumSize = new Size(int.MaxValue, FixedBlobAreaHeight)
        };

        var lists = new Dictionary<QuoteBlobType, ListView>();
        var buttonsByType = new Dictionary<QuoteBlobType, Button[]>();

        foreach (var (type, title) in definitions)
        {
            var blobArea = BuildBlobArea(model, type, title);
            var page = new TabPage(title) { Padding = new Padding(4), Margin = Padding.Empty };
            blobArea.SectionPanel.Dock = DockStyle.Fill;
            page.Controls.Add(blobArea.SectionPanel);
            tabs.TabPages.Add(page);
            lists[type] = blobArea.List;
            buttonsByType[type] = blobArea.ActionButtons;
        }

        return (tabs, lists, buttonsByType);
    }

    private BlobArea BuildBlobArea(QuoteLineItem model, QuoteBlobType blobType, string title)
    {
        _ = title;
        var panel = new Panel
        {
            AutoSize = false,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(StandardGap / 2),
            Margin = new Padding(StandardGap / 2),
            Dock = DockStyle.Fill,
            MinimumSize = new Size(MinimumBlobAreaWidth, MinimumBlobAreaHeight),
            MaximumSize = new Size(int.MaxValue, FixedBlobAreaHeight),
            Height = FixedBlobAreaHeight
        };
        panel.SuspendLayout();
        var list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            Activation = ItemActivation.Standard,
            MultiSelect = false,
            FullRowSelect = true,
            GridLines = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            HideSelection = false,
            MinimumSize = new Size(0, 52)
        };
        list.Columns.Add("File", 180);
        list.Columns.Add("Size", 90, HorizontalAlignment.Right);

        list.Resize += (_, _) => ResizeBlobListColumns(list);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 0)
        };
        var upload = BuildCompactIconButton("📤 Upload", Color.FromArgb(52, 152, 219));
        var delete = BuildCompactIconButton("🗑 Delete", Color.FromArgb(107, 114, 128));
        var download = BuildCompactIconButton("📥 Download", Color.FromArgb(71, 85, 105));

        upload.Click += async (_, _) => await UploadBlobAsync(model, blobType);
        delete.Click += async (_, _) => await DeleteBlobAsync(model, blobType, list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as QuoteBlobAttachment : null);
        download.Click += async (_, _) => await DownloadBlobAsync(list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as QuoteBlobAttachment : null);
        list.AllowDrop = true;
        list.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                e.Effect = DragDropEffects.Copy;
            }
        };
        list.DragDrop += async (_, e) => await HandleBlobDropAsync(model, blobType, e.Data?.GetData(DataFormats.FileDrop) as string[]);

        buttons.Controls.Add(upload);
        buttons.Controls.Add(delete);
        buttons.Controls.Add(download);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(list, 0, 0);
        content.Controls.Add(buttons, 0, 1);
        panel.Controls.Add(content);
        panel.ResumeLayout(true);

        return new BlobArea { SectionPanel = panel, List = list, ActionButtons = new[] { upload, delete, download } };
    }

    private DoneSummaryView BuildDoneSummaryView()
    {
        var container = new Panel { Dock = DockStyle.Fill, Padding = new Padding(2), Margin = Padding.Empty };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));

        var viewerPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
        var viewer = new StepModelPreviewControl(_stepParsingDiagnosticsLog, new StepToGlbConverter(_quoteRepository)) { Dock = DockStyle.Fill, Height = CollapsedViewerHeight };
        var expandButton = BuildCompactIconButton("⛶ Expand", Color.FromArgb(71, 85, 105));
        expandButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        expandButton.Location = new Point(Math.Max(4, viewerPanel.Width - 96), 4);
        expandButton.BringToFront();
        viewerPanel.Controls.Add(viewer);
        viewerPanel.Controls.Add(expandButton);
        if (expandButton.Parent is not null)
        {
            expandButton.Parent.Resize += (_, _) =>
            {
                expandButton.Location = new Point(Math.Max(4, viewerPanel.ClientSize.Width - expandButton.Width - 4), 4);
            };
        }

        var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        Label ValueLabel() => new() { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) };
        var drawingNumber = ValueLabel();
        var drawingName = ValueLabel();
        var revision = ValueLabel();
        var quantity = ValueLabel();
        var lineTotal = ValueLabel();
        var perPiece = ValueLabel();

        AddSummaryRow(details, 0, "Drawing #", drawingNumber);
        AddSummaryRow(details, 1, "Drawing Name", drawingName);
        AddSummaryRow(details, 2, "Revision", revision);
        AddSummaryRow(details, 3, "Quantity", quantity);
        AddSummaryRow(details, 4, "Line Item Total", lineTotal);
        AddSummaryRow(details, 5, "Per-Piece", perPiece);

        layout.Controls.Add(viewerPanel, 0, 0);
        layout.Controls.Add(details, 1, 0);
        container.Controls.Add(layout);

        return new DoneSummaryView
        {
            Container = container,
            Viewer = viewer,
            ExpandButton = expandButton,
            DrawingNumber = drawingNumber,
            DrawingName = drawingName,
            Revision = revision,
            Quantity = quantity,
            LineTotal = lineTotal,
            PerPiece = perPiece
        };
    }

    private static void AddSummaryRow(TableLayoutPanel details, int rowIndex, string label, Label value)
    {
        details.RowCount = Math.Max(details.RowCount, rowIndex + 1);
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.Controls.Add(new Label { Text = label + ":", AutoSize = true, Margin = new Padding(0, 0, 8, 6) }, 0, rowIndex);
        details.Controls.Add(value, 1, rowIndex);
    }

    private async Task SetLineItemDoneStateAsync(LineItemCard card, bool isDone)
    {
        card.IsDone = isDone;
        card.EditView.Visible = !isDone;
        card.DoneView.Visible = isDone;
        card.DoneButton.Visible = !isDone;
        card.EditButton.Visible = isDone;

        if (isDone)
        {
            card.DoneButton.Parent?.Controls.SetChildIndex(card.EditButton, 0);
            await UpdateDoneSummaryAsync(card);
        }
        else
        {
            card.DoneButton.Parent?.Controls.SetChildIndex(card.DoneButton, 0);
        }

        card.Container.MinimumSize = new Size(MinimumLineItemWidth, isDone ? 280 : MinimumLineItemHeight);
        card.Container.MaximumSize = new Size(int.MaxValue, isDone ? 340 : MaximumLineItemHeight);
        ResizeCards();
    }

    private async Task UpdateDoneSummaryAsync(LineItemCard card)
    {
        card.SummaryDrawingNumber.Text = string.IsNullOrWhiteSpace(card.Model.DrawingNumber) ? "-" : card.Model.DrawingNumber;
        card.SummaryDrawingName.Text = string.IsNullOrWhiteSpace(card.Model.DrawingName) ? "-" : card.Model.DrawingName;
        card.SummaryRevision.Text = string.IsNullOrWhiteSpace(card.Model.Revision) ? "-" : card.Model.Revision;
        card.SummaryQuantity.Text = card.Model.Quantity.ToString("0.##", CultureInfo.CurrentCulture);
        card.SummaryLineTotal.Text = card.Model.LineItemTotal.ToString("C2", CultureInfo.CurrentCulture);
        card.SummaryPerPiece.Text = card.Model.UnitPrice.ToString("C2", CultureInfo.CurrentCulture);

        var stepAttachment = FindLatestStepAttachment(card.Model);
        if (stepAttachment is not null)
        {
            await card.StepViewer.LoadStepAttachmentAsync(stepAttachment, _quoteRepository.GetQuoteBlobContentAsync);
        }
        else
        {
            card.StepViewer.LoadStep([]);
        }
    }

    private static QuoteBlobAttachment? FindLatestStepAttachment(QuoteLineItem model)
    {
        return model.BlobAttachments
            .Where(blob => blob.BlobType == QuoteBlobType.ThreeDModel && IsPreviewableCadFile(blob))
            .OrderByDescending(blob => blob.UploadedUtc)
            .ThenByDescending(blob => blob.Id)
            .FirstOrDefault();
    }

    private static bool IsPreviewableCadFile(QuoteBlobAttachment blob)
    {
        var ext = blob.Extension ?? string.Empty;
        return ext.Equals(".step", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".stp", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".sldprt", StringComparison.OrdinalIgnoreCase)
               || blob.FileName.EndsWith(".step", StringComparison.OrdinalIgnoreCase)
               || blob.FileName.EndsWith(".stp", StringComparison.OrdinalIgnoreCase)
               || blob.FileName.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase);
    }

    private async void ExpandModelViewer(LineItemCard card)
    {
        var stepAttachment = FindLatestStepAttachment(card.Model);
        if (stepAttachment is null)
        {
            MessageBox.Show("No previewable CAD model found for this line item.", "3D Model", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var stepData = stepAttachment.BlobData.Length > 0 ? stepAttachment.BlobData : await _quoteRepository.GetQuoteBlobContentAsync(stepAttachment.Id);
        if (stepData.Length == 0)
        {
            MessageBox.Show("CAD model data is unavailable for this line item.", "3D Model", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var viewerForm = new Form
        {
            Text = $"Line Item Viewer - {card.Model.DrawingNumber}",
            Width = 1200,
            Height = 800,
            StartPosition = FormStartPosition.CenterParent,
            WindowState = FormWindowState.Maximized
        };
        var viewer = new StepModelPreviewControl(_stepParsingDiagnosticsLog, new StepToGlbConverter(_quoteRepository)) { Dock = DockStyle.Fill };
        viewerForm.FormClosed += (_, _) => viewer.ClearPreview();

        stepAttachment.BlobData = stepData;
        await viewer.LoadStepAttachmentAsync(stepAttachment, _quoteRepository.GetQuoteBlobContentAsync);
        viewerForm.Controls.Add(viewer);
        viewerForm.ShowDialog(this);
    }

    private async Task HandleBlobDropAsync(QuoteLineItem model, QuoteBlobType blobType, IEnumerable<string>? filePaths)
    {
        if (filePaths is null) return;

        foreach (var filePath in filePaths.Where(File.Exists))
        {
            await UploadBlobAsync(model, blobType, filePath);
        }
    }

    private async Task UploadBlobAsync(QuoteLineItem model, QuoteBlobType blobType)
    {
        using var picker = new OpenFileDialog
        {
            Title = "Select file",
            Filter = "All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (picker.ShowDialog(this) != DialogResult.OK) return;

        await UploadBlobAsync(model, blobType, picker.FileName);
    }

    private async Task UploadBlobAsync(QuoteLineItem model, QuoteBlobType blobType, string filePath)
    {
        if (!File.Exists(filePath)) return;

        var fileName = Path.GetFileName(filePath);
        if (blobType == QuoteBlobType.ThreeDModel)
        {
            var extension = Path.GetExtension(fileName);
            if (!SolidModelFileTypeDetector.IsKnownSolidExtension(extension))
            {
                MessageBox.Show("3D model uploads must be a known solid format (.step, .stp, .sldprt, .iges, .igs, .brep, .stl, .obj, .x_t, .x_b).", "Invalid 3D Model", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!await IsReadableModelFileAsync(filePath))
            {
                MessageBox.Show("The selected 3D model appears to be corrupted, unreadable, or mismatched to its file type.", "Invalid 3D Model", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        var existing = model.BlobAttachments.FirstOrDefault(x => x.BlobType == blobType && string.Equals(x.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (existing.Id > 0) await _quoteRepository.DeleteQuoteLineItemFileAsync(existing.Id);
            model.BlobAttachments.Remove(existing);
        }

        var bytes = await File.ReadAllBytesAsync(filePath);
        using var sha = SHA256.Create();
        var attachment = new QuoteBlobAttachment
        {
            QuoteId = _editingQuote?.Id ?? 0,
            LineItemId = model.Id,
            LifecycleId = _quoteLifecycleId.Text,
            BlobType = blobType,
            FileName = fileName,
            Extension = Path.GetExtension(filePath),
            ContentType = Path.GetExtension(filePath),
            FileSizeBytes = bytes.LongLength,
            Sha256 = sha.ComputeHash(bytes),
            UploadedBy = _uploadedBy,
            UploadedUtc = DateTime.UtcNow,
            StorageRelativePath = filePath,
            BlobData = bytes
        };

        if (_editingQuote is not null && model.Id > 0)
        {
            attachment = await _quoteRepository.InsertQuoteLineItemFileAsync(
                _editingQuote.Id,
                model.Id,
                _quoteLifecycleId.Text,
                blobType,
                attachment.FileName,
                attachment.Extension,
                attachment.FileSizeBytes,
                attachment.Sha256,
                _uploadedBy,
                attachment.UploadedUtc,
                bytes);

            if (blobType == QuoteBlobType.ThreeDModel)
            {
                await ValidateStoredStepBlobAsync(attachment);
            }
        }

        model.BlobAttachments.Add(attachment);
        RefreshAllBlobLists();
    }

    private async Task DeleteBlobAsync(QuoteLineItem model, QuoteBlobType blobType, QuoteBlobAttachment? selected)
    {
        if (selected is null || selected.BlobType != blobType) return;

        var confirm = MessageBox.Show(
            $"Delete '{selected.FileName}' from this section?",
            "Delete Attachment",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        if (selected.Id > 0) await _quoteRepository.DeleteQuoteLineItemFileAsync(selected.Id);
        model.BlobAttachments.Remove(selected);
        RefreshAllBlobLists();
    }

    private async Task DownloadBlobAsync(QuoteBlobAttachment? attachment)
    {
        if (attachment is null) return;

        using var saveDialog = new SaveFileDialog
        {
            FileName = attachment.FileName,
            Filter = "All Files (*.*)|*.*"
        };

        if (saveDialog.ShowDialog(this) != DialogResult.OK) return;
        var data = attachment.BlobData.Length > 0 ? attachment.BlobData : await _quoteRepository.GetQuoteBlobContentAsync(attachment.Id);
        await File.WriteAllBytesAsync(saveDialog.FileName, data);
    }

    private async Task<bool> IsReadableModelFileAsync(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        long fileSizeBytes = 0;

        try
        {
            var fileInfo = new FileInfo(filePath);
            fileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0;
            if (fileInfo.Length <= 0)
            {
                _stepParsingDiagnosticsLog.RecordAttempt(
                    fileName,
                    filePath,
                    fileSizeBytes,
                    isSuccess: false,
                    errorCode: "missing-file-data",
                    failureCategory: "file",
                    message: "No readable payload bytes found for the selected STEP upload.",
                    diagnosticDetails: $"fileSize={fileSizeBytes}",
                    stackTrace: StepParsingDiagnosticsLog.BuildCallSiteTrace(),
                    source: "quote-upload");
                return false;
            }

            const int readSize = 1024;
            await using var stream = File.OpenRead(filePath);
            var buffer = new byte[Math.Min(readSize, (int)Math.Min(fileInfo.Length, readSize))];
            var bytesRead = await stream.ReadAsync(buffer);
            if (bytesRead <= 0)
            {
                return false;
            }

            var fileBytes = await File.ReadAllBytesAsync(filePath);
            var detection = _solidModelTypeDetector.Detect(fileBytes, filePath);
            if (!detection.IsKnownType)
            {
                _stepParsingDiagnosticsLog.RecordAttempt(
                    fileName,
                    filePath,
                    fileSizeBytes,
                    isSuccess: false,
                    errorCode: "unsupported-format",
                    failureCategory: "format",
                    message: "The selected model extension/signature is not recognized as a supported solid format.",
                    diagnosticDetails: $"detection={detection.FileType}, ext={detection.NormalizedExtension}, source={detection.DetectionSource}",
                    stackTrace: StepParsingDiagnosticsLog.BuildCallSiteTrace(),
                    source: "quote-upload");
                return false;
            }

            if (detection.FileType != SolidModelFileType.Step)
            {
                return true;
            }

            var report = _stepFileParser.Parse(fileBytes);
            if (!report.IsSuccess)
            {
                _stepParsingDiagnosticsLog.RecordAttempt(
                    fileName,
                    filePath,
                    fileSizeBytes,
                    isSuccess: false,
                    errorCode: report.ErrorCode,
                    failureCategory: report.FailureCategory,
                    message: BuildParseFailureMessage(fileName, filePath, report.ErrorCode, report.Message, "quote-upload"),
                    diagnosticDetails: BuildParseDiagnosticDetails(report),
                    stackTrace: StepParsingDiagnosticsLog.BuildCallSiteTrace(),
                    source: "quote-upload");
            }

            return report.IsSuccess;
        }
        catch (Exception ex)
        {
            _stepParsingDiagnosticsLog.RecordAttempt(
                fileName,
                filePath,
                fileSizeBytes,
                isSuccess: false,
                errorCode: "step-parse-exception",
                failureCategory: "exception",
                message: ex.Message,
                diagnosticDetails: ex.GetType().FullName,
                stackTrace: StepParsingDiagnosticsLog.BuildStackTrace(ex),
                source: "quote-upload");
            return false;
        }
    }


    private async Task ValidateStoredStepBlobAsync(QuoteBlobAttachment attachment)
    {
        var extension = attachment.Extension ?? Path.GetExtension(attachment.FileName ?? string.Empty);
        if (!extension.Equals(".step", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".stp", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var storedData = await _quoteRepository.GetQuoteBlobContentAsync(attachment.Id);
        if (storedData.Length == 0)
        {
            _stepParsingDiagnosticsLog.RecordAttempt(
                attachment.FileName,
                attachment.StorageRelativePath,
                attachment.FileSizeBytes,
                isSuccess: false,
                errorCode: "missing-blob-data",
                failureCategory: "blob",
                message: BuildParseFailureMessage(attachment.FileName, attachment.StorageRelativePath, "missing-blob-data", "Uploaded STEP blob content could not be loaded from the database.", "blob-parse-validation"),
                diagnosticDetails: $"attachmentId={attachment.Id}",
                stackTrace: StepParsingDiagnosticsLog.BuildCallSiteTrace(),
                source: "blob-parse-validation");
            return;
        }

        var report = _stepFileParser.Parse(storedData);
        if (!report.IsSuccess)
        {
            _stepParsingDiagnosticsLog.RecordAttempt(
                attachment.FileName,
                attachment.StorageRelativePath,
                storedData.LongLength,
                isSuccess: false,
                errorCode: report.ErrorCode,
                failureCategory: report.FailureCategory,
                message: BuildParseFailureMessage(attachment.FileName, attachment.StorageRelativePath, report.ErrorCode, report.Message, "blob-parse-validation"),
                diagnosticDetails: BuildParseDiagnosticDetails(report),
                stackTrace: StepParsingDiagnosticsLog.BuildCallSiteTrace(),
                source: "blob-parse-validation");
            return;
        }
    }

    private static string BuildParseDiagnosticDetails(StepParseReport report)
    {
        var schema = string.IsNullOrWhiteSpace(report.SchemaName) ? "unknown" : report.SchemaName;
        var topEntities = report.DistinctEntityTypes.Count == 0
            ? "none"
            : string.Join(", ", report.DistinctEntityTypes.OrderByDescending(x => x.Value).Take(6).Select(x => $"{x.Key}:{x.Value}"));
        return $"category={report.FailureCategory}; schema={schema}; entities={report.EntityCount}; surfaces={report.SurfaceEntityCount}; solids={report.SolidEntityCount}; details={report.DiagnosticDetails}; top={topEntities}";
    }

    private string BuildParseFailureMessage(string? fileName, string? filePath, string errorCode, string reason, string source)
    {
        var attempts = _stepParsingDiagnosticsLog.GetEntries().Count(entry =>
            !entry.IsSuccess
            && string.Equals(entry.FileName, fileName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.Source, source, StringComparison.OrdinalIgnoreCase)) + 1;
        var contextPath = string.IsNullOrWhiteSpace(filePath) ? "in-memory-blob" : filePath;
        return $"Parse failed: {reason} (error={errorCode}, attempts={attempts}, context=source:{source}; path:{contextPath})";
    }

    private void RefreshAllBlobLists()
    {
        foreach (var card in _lineItemCards)
        {
            RefreshBlobLists(card);
        }
    }

    private void RefreshBlobLists(LineItemCard card)
    {
        foreach (var (blobType, list) in card.BlobLists)
        {
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var attachment in card.Model.BlobAttachments.Where(x => x.BlobType == blobType).OrderBy(x => x.FileName))
            {
                var item = new ListViewItem(attachment.FileName) { Tag = attachment };
                item.SubItems.Add(FormatFileSize(attachment.FileSizeBytes));
                list.Items.Add(item);
            }

            ResizeBlobListColumns(list);
            list.EndUpdate();

            if (card.BlobActionButtons.TryGetValue(blobType, out var actionButtons))
            {
                foreach (var button in actionButtons)
                {
                    button.Enabled = !card.IsDone;
                }
            }
        }

        if (card.IsDone)
        {
            _ = UpdateDoneSummaryAsync(card);
        }
    }


    private static string FormatFileSize(long bytes)
    {
        var units = new[] { "B", "KB", "MB", "GB" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }

    private static void ResizeBlobListColumns(ListView list)
    {
        if (list.Columns.Count < 2)
        {
            return;
        }

        const int sizeColWidth = 90;
        var fileColumn = Math.Max(120, list.ClientSize.Width - sizeColWidth - 4);
        list.Columns[0].Width = fileColumn;
        list.Columns[1].Width = sizeColWidth;
    }

    private static Button BuildCompactIconButton(string text, Color backColor)
        => new()
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6, 3, 6, 3),
            Margin = new Padding(4, 0, 4, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor,
            ForeColor = Color.White,
            MinimumSize = new Size(74, 26)
        };

    private Control BuildSectionGroup(string title, Control body)
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Text = title,
            Padding = new Padding(StandardGap),
            Margin = new Padding(0, 0, 0, 6)
        };
        body.Dock = DockStyle.Fill;
        group.Controls.Add(body);
        return group;
    }

    private Panel BuildCompactSection(string title, Control content)
    {
        var sectionPanel = new Panel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 0, 0, 6), Padding = Padding.Empty };
        var sectionGrid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
        sectionGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sectionGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sectionGrid.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 1) }, 0, 0);
        sectionGrid.Controls.Add(content, 0, 1);
        sectionPanel.Controls.Add(sectionGrid);
        return sectionPanel;
    }

    private async Task RemoveLineItemAsync(QuoteLineItem model, Control container)
    {
        var confirm = MessageBox.Show("Remove this line item?", "Confirm Remove", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        if (model.Id > 0)
        {
            await _quoteRepository.DeleteQuoteLineItemAsync(model.Id);
        }

        var card = _lineItemCards.FirstOrDefault(x => ReferenceEquals(x.Container, container));
        if (card is not null)
        {
            _lineItemCards.Remove(card);
        }

        _lineItemsPanel.Controls.Remove(container);
        RenumberLineItems();
        ResizeCards();
        RecalculateQuoteTotals();
    }

    private static TextBox NewDecimalDisabledField(string value)
        => new()
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(0, MinimumTextBoxHeight),
            Margin = new Padding(0, 2, 0, 0),
            Text = value
        };

    private static TextBox NewNumericField(decimal value)
    {
        var box = new TextBox
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(0, MinimumTextBoxHeight),
            Margin = new Padding(0, 2, 0, 0),
            Text = value.ToString("0.00", CultureInfo.CurrentCulture)
        };
        box.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.' && e.KeyChar != ',')
            {
                e.Handled = true;
            }
        };

        return box;
    }


    private static TextBox NewReadOnlySummaryField(decimal value)
        => new()
        {
            ReadOnly = true,
            Dock = DockStyle.Fill,
            MinimumSize = new Size(0, MinimumTextBoxHeight),
            Margin = new Padding(0, 2, 0, 0),
            Text = value.ToString("0.00", CultureInfo.CurrentCulture),
            BackColor = Color.WhiteSmoke
        };

    private static TableLayoutPanel NewFieldPanel(string label, Control input)
    {
        var field = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(1),
            ColumnCount = 1,
            RowCount = 2
        };
        field.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        field.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        input.Dock = DockStyle.Fill;
        field.Controls.Add(new Label { Text = label, AutoSize = true, Margin = Padding.Empty }, 0, 0);
        field.Controls.Add(input, 0, 1);
        return field;
    }

    private void RenumberLineItems()
    {
        for (var i = 0; i < _lineItemCards.Count; i++)
        {
            _lineItemCards[i].Title.Text = $"Line Item {i + 1}";
            _lineItemCards[i].Container.BackColor = GetLineItemBackgroundColor(i);
        }
    }

    private static Color GetLineItemBackgroundColor(int index)
        => LineItemColorCycle[index % LineItemColorCycle.Length];

    private void ResizeCards()
    {
        foreach (var card in _lineItemCards)
        {
            card.Container.Width = Math.Max(MinimumLineItemWidth, _lineItemsPanel.ClientSize.Width - (_lineItemsPanel.Padding.Horizontal + 10));
            var targetHeight = card.IsDone ? 300 : DefaultLineItemHeight;
            card.Container.Height = Math.Clamp(targetHeight, card.Container.MinimumSize.Height, card.Container.MaximumSize.Height);
        }
    }

    private void RecalculateQuoteTotals()
    {
        decimal masterTotal = 0;
        decimal totalHours = 0;

        foreach (var card in _lineItemCards)
        {
            card.Model.DrawingNumber = card.DrawingNumber.Text.Trim();
            card.Model.DrawingName = card.DrawingName.Text.Trim();
            card.Model.Revision = card.Revision.Text.Trim();
            card.Model.ProductionHours = ParseDecimal(card.ProductionHours.Text);
            card.Model.SetupHours = ParseDecimal(card.SetupHours.Text);
            card.Model.MaterialCost = ParseDecimal(card.MaterialCost.Text);
            card.Model.ToolingCost = ParseDecimal(card.ToolingCost.Text);
            card.Model.SecondaryOperationsCost = ParseDecimal(card.SecondaryCost.Text);
            card.Model.Quantity = Math.Max(1m, ParseDecimal(card.Quantity.Text));

            var baseCost = ((card.Model.ProductionHours + card.Model.SetupHours) * _shopHourlyRate)
                           + card.Model.MaterialCost
                           + card.Model.ToolingCost
                           + card.Model.SecondaryOperationsCost;
            var lineTotal = baseCost * card.Model.Quantity;
            var perPiecePrice = card.Model.Quantity > 0 ? lineTotal / card.Model.Quantity : 0m;

            card.Model.LineItemTotal = lineTotal;
            card.Model.UnitPrice = perPiecePrice;
            card.Total.Text = lineTotal.ToString("0.00", CultureInfo.CurrentCulture);
            card.PerPiecePrice.Text = perPiecePrice.ToString("0.00", CultureInfo.CurrentCulture);
            card.Quantity.Text = card.Model.Quantity.ToString("0.##", CultureInfo.CurrentCulture);

            if (card.IsDone)
            {
                _ = UpdateDoneSummaryAsync(card);
            }

            totalHours += card.Model.ProductionHours + card.Model.SetupHours;
            masterTotal += lineTotal;
        }

        _totalHoursValue.Text = totalHours.ToString("0.00", CultureInfo.CurrentCulture);
        _masterTotalValue.Text = masterTotal.ToString("C", CultureInfo.CurrentCulture);
    }

    private static decimal ParseDecimal(string value)
        => decimal.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out var result) ? result : 0m;

    private async Task SaveQuoteAsync()
    {
        if (_customerPicker.SelectedItem is not Customer customer)
        {
            MessageBox.Show("Customer is required.", "Quote", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        RecalculateQuoteTotals();

        var quote = new Quote
        {
            Id = _editingQuote?.Id ?? 0,
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            LifecycleQuoteId = _quoteLifecycleId.Text,
            ShopHourlyRateSnapshot = _shopHourlyRate,
            Status = _editingQuote?.Status ?? QuoteStatus.InProgress,
            CreatedUtc = _editingQuote?.CreatedUtc ?? DateTime.UtcNow,
            LastUpdatedUtc = DateTime.UtcNow
        };

        foreach (var card in _lineItemCards)
        {
            var line = card.Model;
            line.Description = string.IsNullOrWhiteSpace(line.DrawingName) ? card.Title.Text : line.DrawingName;
            line.Quantity = Math.Max(1m, line.Quantity);
            line.UnitPrice = line.Quantity > 0 ? line.LineItemTotal / line.Quantity : 0m;
            line.RequiresDfars = card.RequiresDfars.Checked;
            line.RequiresMaterialTestReport = card.RequiresMaterialTestReport.Checked;
            line.RequiresCertificateOfConformance = card.RequiresCertificateOfConformance.Checked;
            line.RequiresSecondaryOperations = card.RequiresSecondaryOperations.Checked;
            line.Notes = BuildLineNotes(_customerPartPo.Text.Trim());
            quote.LineItems.Add(line);
        }

        quote.MasterTotal = quote.LineItems.Sum(x => x.LineItemTotal);

        try
        {
            CreatedQuoteId = _actorUser is null
                ? await _quoteRepository.SaveQuoteAsync(quote)
                : await _quoteRepository.SaveQuoteAsync(quote, _actorUser);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save quote: {ex.Message}", "Quote", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string BuildLineNotes(string customerPartPo)
        => $"Customer Part PO: {customerPartPo}";

    private static Dictionary<string, string> ParseMetadata(string? notes)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(notes)) return metadata;

        foreach (var line in notes.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0 || separator >= line.Length - 1) continue;
            metadata[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return metadata;
    }

    private async Task DeleteQuoteAsync()
    {
        if (_editingQuote is null) return;

        var confirm = MessageBox.Show($"Delete quote #{_editingQuote.Id}? This cannot be undone.", "Delete Quote", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        if (_actorUser is null)
        {
            await _quoteRepository.DeleteQuoteAsync(_editingQuote.Id);
        }
        else
        {
            await _quoteRepository.DeleteQuoteAsync(_editingQuote.Id, _actorUser);
        }
        WasDeleted = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed class BlobArea
    {
        public required Panel SectionPanel { get; init; }
        public required ListView List { get; init; }
        public required Button[] ActionButtons { get; init; }
    }

    private sealed class DoneSummaryView
    {
        public required Panel Container { get; init; }
        public required StepModelPreviewControl Viewer { get; init; }
        public required Button ExpandButton { get; init; }
        public required Label DrawingNumber { get; init; }
        public required Label DrawingName { get; init; }
        public required Label Revision { get; init; }
        public required Label Quantity { get; init; }
        public required Label LineTotal { get; init; }
        public required Label PerPiece { get; init; }
    }

    private sealed class LineItemCard
    {
        public required Panel Container { get; init; }
        public required Label Title { get; init; }
        public required QuoteLineItem Model { get; init; }
        public required TextBox DrawingNumber { get; init; }
        public required TextBox DrawingName { get; init; }
        public required TextBox Revision { get; init; }
        public required TextBox ProductionHours { get; init; }
        public required TextBox SetupHours { get; init; }
        public required TextBox MaterialCost { get; init; }
        public required TextBox ToolingCost { get; init; }
        public required TextBox SecondaryCost { get; init; }
        public required TextBox Quantity { get; init; }
        public required TextBox Total { get; init; }
        public required TextBox PerPiecePrice { get; init; }
        public required CheckBox RequiresDfars { get; init; }
        public required CheckBox RequiresMaterialTestReport { get; init; }
        public required CheckBox RequiresCertificateOfConformance { get; init; }
        public required CheckBox RequiresSecondaryOperations { get; init; }
        public required Dictionary<QuoteBlobType, ListView> BlobLists { get; init; }
        public required Dictionary<QuoteBlobType, Button[]> BlobActionButtons { get; init; }
        public required Button DoneButton { get; init; }
        public required Button EditButton { get; init; }
        public required Control EditView { get; init; }
        public required Control DoneView { get; init; }
        public required StepModelPreviewControl StepViewer { get; init; }
        public required Label SummaryDrawingNumber { get; init; }
        public required Label SummaryDrawingName { get; init; }
        public required Label SummaryRevision { get; init; }
        public required Label SummaryQuantity { get; init; }
        public required Label SummaryLineTotal { get; init; }
        public required Label SummaryPerPiece { get; init; }
        public bool IsDone { get; set; }
    }

}
