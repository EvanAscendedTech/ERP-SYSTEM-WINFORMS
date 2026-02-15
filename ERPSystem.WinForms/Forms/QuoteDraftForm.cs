using System.Globalization;
using System.Security.Cryptography;
using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Forms;

public class QuoteDraftForm : Form
{
    private const int DefaultLineItemWidth = 860;
    private const int DefaultLineItemHeight = 320;
    private const int MinimumLineItemWidth = 620;
    private const int MinimumLineItemHeight = 280;
    private const int MinimumBlobAreaWidth = 210;
    private const int MinimumBlobAreaHeight = 110;
    private const float MinScaledFontSize = 8f;
    private const float MaxScaledFontSize = 13f;
    private const int ResizeGripSize = 16;
    private const int MinimumTextBoxHeight = 28;
    private readonly QuoteRepository _quoteRepository;
    private readonly bool _canViewPricing;
    private readonly string _uploadedBy;
    private readonly ComboBox _customerPicker = new() { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _customerPartPo = new() { Width = 220, PlaceholderText = "Customer Part PO" };
    private readonly TextBox _quoteLifecycleId = new() { Width = 220, ReadOnly = true };
    private readonly FlowLayoutPanel _lineItemsPanel = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(4) };
    private readonly Label _totalHoursValue = new() { AutoSize = true, Text = "0.00" };
    private readonly Label _masterTotalValue = new() { AutoSize = true, Text = "$0.00" };
    private readonly Quote? _editingQuote;
    private readonly List<LineItemCard> _lineItemCards = new();
    private decimal _shopHourlyRate;

    public int CreatedQuoteId { get; private set; }
    public bool WasDeleted { get; private set; }

    public QuoteDraftForm(QuoteRepository quoteRepository, bool canViewPricing, string uploadedBy, Quote? editingQuote = null)
    {
        _quoteRepository = quoteRepository;
        _canViewPricing = canViewPricing;
        _uploadedBy = uploadedBy;
        _editingQuote = editingQuote;
        _quoteLifecycleId.Text = string.IsNullOrWhiteSpace(editingQuote?.LifecycleQuoteId) ? GenerateLifecycleQuoteId() : editingQuote.LifecycleQuoteId;

        Text = _editingQuote is null ? $"New Quote Draft - {_quoteLifecycleId.Text}" : $"Edit In-Process Quote #{_editingQuote.Id} - {_quoteLifecycleId.Text}";
        Width = 1300;
        Height = 780;
        MinimumSize = new Size(1100, 700);
        WindowState = FormWindowState.Maximized;
        StartPosition = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10) };
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
            Margin = new Padding(0, 0, 0, 8)
        };
        for (var i = 0; i < header.ColumnCount; i++)
        {
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        }
        for (var i = 0; i < header.RowCount; i++)
        {
            header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var addLineButton = new Button { Text = "Add Line Item", AutoSize = true, Anchor = AnchorStyles.Left };
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

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var saveButton = new Button { Text = "Save Quote", AutoSize = true };
        saveButton.Click += async (_, _) => await SaveQuoteAsync();
        buttons.Controls.Add(saveButton);

        if (_editingQuote is not null)
        {
            var deleteButton = new Button { Text = "Delete Quote", AutoSize = true, BackColor = Color.Firebrick, ForeColor = Color.White };
            deleteButton.Click += async (_, _) => await DeleteQuoteAsync();
            buttons.Controls.Add(deleteButton);
        }

        root.Controls.Add(header, 0, 0);
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
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(6),
            BackColor = index % 2 == 0 ? Color.FromArgb(245, 248, 252) : Color.FromArgb(234, 243, 250),
            MinimumSize = new Size(MinimumLineItemWidth, MinimumLineItemHeight)
        };
        cardPanel.SuspendLayout();

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = false, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        layout.SuspendLayout();
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        layout.Controls.Add(title, 0, 0);

        var detailsGrid = new TableLayoutPanel { AutoSize = false, ColumnCount = 4, Dock = DockStyle.Fill, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 2, 0, 2) };
        detailsGrid.RowCount = 1;
        detailsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        for (var i = 0; i < 4; i++) detailsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        var drawingNumber = NewDecimalDisabledField(model.DrawingNumber);
        var drawingName = NewDecimalDisabledField(model.DrawingName);
        var revision = NewDecimalDisabledField(model.Revision);
        var removeButton = new Button { Text = "Remove Line Item", AutoSize = true, BackColor = Color.FromArgb(214, 77, 77), ForeColor = Color.White };
        removeButton.Click += async (_, _) => await RemoveLineItemAsync(model, cardPanel);

        detailsGrid.Controls.Add(NewFieldPanel("Drawing Number", drawingNumber), 0, 0);
        detailsGrid.Controls.Add(NewFieldPanel("Drawing Name", drawingName), 1, 0);
        detailsGrid.Controls.Add(NewFieldPanel("Revision", revision), 2, 0);
        var removeButtonHost = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        removeButtonHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        removeButtonHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        removeButtonHost.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 0);
        removeButton.Dock = DockStyle.Bottom;
        removeButtonHost.Controls.Add(removeButton, 0, 1);
        detailsGrid.Controls.Add(removeButtonHost, 3, 0);

        var drawingDocs = BuildBlobArea(model, QuoteBlobType.Technical, "Drawings (PDF / STEP)");
        var modelDocs = BuildBlobArea(model, QuoteBlobType.ThreeDModel, "3D Models");

        var costsRow = new TableLayoutPanel { AutoSize = false, ColumnCount = 5, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2), RowCount = 1 };
        costsRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        for (var i = 0; i < 5; i++) costsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

        var productionHours = NewNumericField(model.ProductionHours);
        var setupHours = NewNumericField(model.SetupHours);
        var materialCost = NewNumericField(model.MaterialCost);
        var toolingCost = NewNumericField(model.ToolingCost);
        var secondaryCost = NewNumericField(model.SecondaryOperationsCost);

        foreach (var box in new[] { productionHours, setupHours, materialCost, toolingCost, secondaryCost })
        {
            box.TextChanged += (_, _) => RecalculateQuoteTotals();
        }

        costsRow.Controls.Add(NewFieldPanel("Production Hours", productionHours), 0, 0);
        costsRow.Controls.Add(NewFieldPanel("Setup Hours", setupHours), 1, 0);
        costsRow.Controls.Add(NewFieldPanel("Material Cost", materialCost), 2, 0);
        costsRow.Controls.Add(NewFieldPanel("Tooling Cost", toolingCost), 3, 0);
        costsRow.Controls.Add(NewFieldPanel("Secondary Operations Cost", secondaryCost), 4, 0);
        var materialDocs = BuildBlobArea(model, QuoteBlobType.MaterialPricing, "Material");
        var toolingDocs = BuildBlobArea(model, QuoteBlobType.ToolingDocumentation, "Tooling");
        var postOpDocs = BuildBlobArea(model, QuoteBlobType.PostOpPricing, "Post-Operation");

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
            RowCount = 3,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        contentGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        contentGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        contentGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var detailsSection = BuildCompactSection("Details", detailsGrid);
        var costsSection = BuildCompactSection("Costs", costsRow);

        var blobGrid = new TableLayoutPanel
        {
            AutoSize = false,
            ColumnCount = 5,
            RowCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 0, 2),
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        for (var i = 0; i < 5; i++)
        {
            blobGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
        }

        blobGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        blobGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        blobGrid.Controls.Add(drawingDocs.SectionPanel, 0, 0);
        blobGrid.Controls.Add(modelDocs.SectionPanel, 1, 0);
        blobGrid.Controls.Add(materialDocs.SectionPanel, 2, 0);
        blobGrid.Controls.Add(toolingDocs.SectionPanel, 3, 0);
        blobGrid.Controls.Add(postOpDocs.SectionPanel, 4, 0);

        var attachmentsSection = BuildCompactSection("Attachments", blobGrid);

        contentGrid.Controls.Add(detailsSection, 0, 0);
        contentGrid.Controls.Add(costsSection, 0, 1);
        contentGrid.Controls.Add(attachmentsSection, 0, 2);
        contentScroller.Controls.Add(contentGrid);
        layout.Controls.Add(contentScroller, 0, 1);

        var footer = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        var totalBox = new TextBox
        {
            ReadOnly = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(6, 3, 0, 3),
            MinimumSize = new Size(0, MinimumTextBoxHeight),
            Text = model.LineItemTotal.ToString("0.00", CultureInfo.CurrentCulture)
        };
        footer.Controls.Add(new Label { Text = "Line Item Total", AutoSize = true, Anchor = AnchorStyles.Left, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        footer.Controls.Add(totalBox, 1, 0);
        layout.Controls.Add(footer, 0, 2);

        cardPanel.Controls.Add(layout);
        var resizeGrip = CreateResizeGrip(cardPanel);
        cardPanel.Controls.Add(resizeGrip);
        resizeGrip.BringToFront();
        layout.ResumeLayout(true);
        cardPanel.ResumeLayout(true);

        var baseFonts = new Dictionary<Control, float>();
        CaptureBaseFonts(cardPanel, baseFonts);

        var card = new LineItemCard
        {
            Container = cardPanel,
            BaseSize = new Size(DefaultLineItemWidth, DefaultLineItemHeight),
            ResizeGrip = resizeGrip,
            BaseFonts = baseFonts,
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
            Total = totalBox,
            BlobLists = new Dictionary<QuoteBlobType, ListView>
            {
                [QuoteBlobType.Technical] = drawingDocs.List,
                [QuoteBlobType.ThreeDModel] = modelDocs.List,
                [QuoteBlobType.MaterialPricing] = materialDocs.List,
                [QuoteBlobType.ToolingDocumentation] = toolingDocs.List,
                [QuoteBlobType.PostOpPricing] = postOpDocs.List
            }
        };

        RefreshBlobLists(card);
        ApplyCardScaling(card);
        return card;
    }

    private Panel CreateResizeGrip(Panel cardPanel)
    {
        var grip = new Panel
        {
            Size = new Size(ResizeGripSize, ResizeGripSize),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Cursor = Cursors.SizeNWSE,
            BackColor = Color.Transparent,
            Location = new Point(cardPanel.ClientSize.Width - ResizeGripSize - 2, cardPanel.ClientSize.Height - ResizeGripSize - 2)
        };

        Point dragStart = Point.Empty;
        Size sizeStart = Size.Empty;

        grip.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.SlateGray, 1f);
            e.Graphics.DrawLine(pen, 4, ResizeGripSize - 1, ResizeGripSize - 1, 4);
            e.Graphics.DrawLine(pen, 8, ResizeGripSize - 1, ResizeGripSize - 1, 8);
            e.Graphics.DrawLine(pen, 12, ResizeGripSize - 1, ResizeGripSize - 1, 12);
        };

        grip.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            dragStart = Cursor.Position;
            sizeStart = cardPanel.Size;
            grip.Capture = true;
        };

        grip.MouseMove += (_, _) =>
        {
            if (!grip.Capture) return;

            var cursor = Cursor.Position;
            var widthDelta = cursor.X - dragStart.X;
            var heightDelta = cursor.Y - dragStart.Y;
            var newWidth = Math.Max(cardPanel.MinimumSize.Width, sizeStart.Width + widthDelta);
            var newHeight = Math.Max(cardPanel.MinimumSize.Height, sizeStart.Height + heightDelta);
            cardPanel.Size = new Size(newWidth, newHeight);
            cardPanel.Parent?.PerformLayout();
        };

        grip.MouseUp += (_, _) =>
        {
            if (!grip.Capture) return;
            grip.Capture = false;
            if (_lineItemCards.FirstOrDefault(x => x.Container == cardPanel) is { } resizedCard)
            {
                resizedCard.IsUserResized = true;
                ApplyCardScaling(resizedCard);
            }
        };

        return grip;
    }

    private static void CaptureBaseFonts(Control parent, IDictionary<Control, float> map)
    {
        map[parent] = parent.Font.Size;
        foreach (Control child in parent.Controls)
        {
            CaptureBaseFonts(child, map);
        }
    }

    private static void ApplyFontScale(Control parent, IReadOnlyDictionary<Control, float> baseFonts, float scale)
    {
        if (baseFonts.TryGetValue(parent, out var baseSize))
        {
            var scaled = Math.Clamp(baseSize * scale, MinScaledFontSize, MaxScaledFontSize);
            parent.Font = new Font(parent.Font.FontFamily, scaled, parent.Font.Style);
        }

        foreach (Control child in parent.Controls)
        {
            ApplyFontScale(child, baseFonts, scale);
        }
    }

    private static void ApplyCardScaling(LineItemCard card)
    {
        var widthScale = card.Container.Width / (float)card.BaseSize.Width;
        var heightScale = card.Container.Height / (float)card.BaseSize.Height;
        var scale = Math.Clamp(Math.Min(widthScale, heightScale), 0.8f, 1.25f);

        ApplyFontScale(card.Container, card.BaseFonts, scale);

        foreach (var blobList in card.BlobLists.Values)
        {
            blobList.Font = new Font(blobList.Font.FontFamily, Math.Clamp(9f * scale, MinScaledFontSize, MaxScaledFontSize), blobList.Font.Style);
        }
    }

    private BlobArea BuildBlobArea(QuoteLineItem model, QuoteBlobType blobType, string title)
    {
        var panel = new Panel
        {
            AutoSize = false,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(3),
            Margin = new Padding(1),
            Dock = DockStyle.Fill,
            MinimumSize = new Size(MinimumBlobAreaWidth, MinimumBlobAreaHeight)
        };
        panel.SuspendLayout();
        var titleLabel = new Label { Text = title, AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        var list = new ListView { Dock = DockStyle.Fill, View = View.List, HeaderStyle = ColumnHeaderStyle.None };

        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 2, 0, 0) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        var upload = new Button { Text = "Upload", Dock = DockStyle.Fill, BackColor = Color.FromArgb(52, 152, 219), ForeColor = Color.White, Margin = new Padding(0, 0, 2, 0) };
        var delete = new Button { Text = "Delete", Dock = DockStyle.Fill, Margin = new Padding(2, 0, 2, 0) };
        var download = new Button { Text = "Download", Dock = DockStyle.Fill, Margin = new Padding(2, 0, 0, 0) };

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

        buttons.Controls.Add(upload, 0, 0);
        buttons.Controls.Add(delete, 1, 0);
        buttons.Controls.Add(download, 2, 0);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(titleLabel, 0, 0);
        content.Controls.Add(list, 0, 1);
        content.Controls.Add(buttons, 0, 2);
        panel.Controls.Add(content);
        panel.ResumeLayout(true);

        return new BlobArea { SectionPanel = panel, List = list };
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
        }

        model.BlobAttachments.Add(attachment);
        RefreshAllBlobLists();
    }

    private async Task DeleteBlobAsync(QuoteLineItem model, QuoteBlobType blobType, QuoteBlobAttachment? selected)
    {
        if (selected is null || selected.BlobType != blobType) return;
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
                list.Items.Add(new ListViewItem(attachment.FileName) { Tag = attachment });
            }

            list.EndUpdate();
        }
    }

    private Panel BuildCompactSection(string title, Control content)
    {
        var sectionPanel = new Panel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 0, 0, 3), Padding = Padding.Empty };
        var sectionGrid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
        sectionGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sectionGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sectionGrid.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 2) }, 0, 0);
        sectionGrid.Controls.Add(content, 0, 1);
        sectionPanel.Controls.Add(sectionGrid);
        return sectionPanel;
    }

    private async Task RemoveLineItemAsync(QuoteLineItem model, Control container)
    {
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

    private static TableLayoutPanel NewFieldPanel(string label, Control input)
    {
        var field = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(1),
            ColumnCount = 1,
            RowCount = 2
        };
        field.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        field.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
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
            _lineItemCards[i].Container.BackColor = i % 2 == 0 ? Color.FromArgb(245, 248, 252) : Color.FromArgb(234, 243, 250);
        }
    }

    private void ResizeCards()
    {
        foreach (var card in _lineItemCards)
        {
            var maximumWidth = Math.Max(MinimumLineItemWidth, _lineItemsPanel.ClientSize.Width - 28);
            var proportionalHeight = Math.Max(
                card.Container.MinimumSize.Height,
                (int)Math.Round(_lineItemsPanel.ClientSize.Height / 3f));
            if (!card.IsUserResized)
            {
                card.Container.Width = maximumWidth;
                card.Container.Height = proportionalHeight;
            }
            else
            {
                card.Container.Width = Math.Min(card.Container.Width, maximumWidth);
                card.Container.Height = Math.Max(proportionalHeight, card.Container.Height);
            }

            card.ResizeGrip.Location = new Point(card.Container.ClientSize.Width - ResizeGripSize - 2, card.Container.ClientSize.Height - ResizeGripSize - 2);
            ApplyCardScaling(card);
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

            var lineTotal = ((card.Model.ProductionHours + card.Model.SetupHours) * _shopHourlyRate)
                            + card.Model.MaterialCost
                            + card.Model.ToolingCost
                            + card.Model.SecondaryOperationsCost;

            card.Model.LineItemTotal = lineTotal;
            card.Total.Text = lineTotal.ToString("0.00", CultureInfo.CurrentCulture);

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
            line.Quantity = 1;
            line.UnitPrice = line.LineItemTotal;
            line.Notes = BuildLineNotes(_customerPartPo.Text.Trim());
            quote.LineItems.Add(line);
        }

        quote.MasterTotal = quote.LineItems.Sum(x => x.LineItemTotal);

        try
        {
            CreatedQuoteId = await _quoteRepository.SaveQuoteAsync(quote);
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

        await _quoteRepository.DeleteQuoteAsync(_editingQuote.Id);
        WasDeleted = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed class BlobArea
    {
        public required Panel SectionPanel { get; init; }
        public required ListView List { get; init; }
    }

    private sealed class LineItemCard
    {
        public required Panel Container { get; init; }
        public required Size BaseSize { get; init; }
        public required Panel ResizeGrip { get; init; }
        public required IReadOnlyDictionary<Control, float> BaseFonts { get; init; }
        public bool IsUserResized { get; set; }
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
        public required TextBox Total { get; init; }
        public required Dictionary<QuoteBlobType, ListView> BlobLists { get; init; }
    }
}
