using System.Globalization;
using System.Security.Cryptography;
using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Forms;

public class QuoteDraftForm : Form
{
    private readonly QuoteRepository _quoteRepository;
    private readonly bool _canViewPricing;
    private readonly string _uploadedBy;
    private readonly ComboBox _customerPicker = new() { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _customerAddress = new() { Width = 300, ReadOnly = true };
    private readonly TextBox _customerPartPo = new() { Width = 220, PlaceholderText = "Customer Part PO" };
    private readonly TextBox _cycleTime = new() { Width = 120, PlaceholderText = "Cycle Time" };
    private readonly TextBox _ipNotes = new() { Width = 180, PlaceholderText = "IP Fields" };
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

        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            AutoScroll = true,
            Margin = new Padding(0, 0, 0, 8)
        };
        header.Controls.Add(new Label { Text = "Customer Name", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        header.Controls.Add(_customerPicker);
        header.Controls.Add(new Label { Text = "Customer Address", AutoSize = true, Margin = new Padding(10, 8, 0, 0) });
        header.Controls.Add(_customerAddress);
        header.Controls.Add(new Label { Text = "Customer Part PO", AutoSize = true, Margin = new Padding(10, 8, 0, 0) });
        header.Controls.Add(_customerPartPo);
        header.Controls.Add(new Label { Text = "Cycle Time", AutoSize = true, Margin = new Padding(10, 8, 0, 0) });
        header.Controls.Add(_cycleTime);
        header.Controls.Add(new Label { Text = "IP Fields", AutoSize = true, Margin = new Padding(10, 8, 0, 0) });
        header.Controls.Add(_ipNotes);
        header.Controls.Add(new Label { Text = "Lifecycle ID", AutoSize = true, Margin = new Padding(10, 8, 0, 0) });
        header.Controls.Add(_quoteLifecycleId);

        var addLineButton = new Button { Text = "Add Line Item", AutoSize = true, Margin = new Padding(20, 4, 0, 0) };
        addLineButton.Click += (_, _) => AddLineItemCard();
        header.Controls.Add(addLineButton);

        var totalsPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = true };
        totalsPanel.Controls.Add(new Label { Text = "Total Hours:", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        totalsPanel.Controls.Add(_totalHoursValue);
        totalsPanel.Controls.Add(new Label { Text = "Master Quote Total:", AutoSize = true, Margin = new Padding(20, 8, 0, 0), Visible = _canViewPricing });
        _masterTotalValue.Visible = _canViewPricing;
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

        _customerPicker.SelectedIndexChanged += (_, _) => OnCustomerSelected();
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

    private void OnCustomerSelected()
    {
        if (_customerPicker.SelectedItem is Customer customer)
        {
            _customerAddress.Text = customer.Address;
        }
    }

    private void PopulateFromQuote(Quote quote)
    {
        _customerPicker.SelectedValue = quote.CustomerId;
        _customerPartPo.Text = ParseMetadata(quote.LineItems.FirstOrDefault()?.Notes).GetValueOrDefault("Customer Part PO", string.Empty);
        _cycleTime.Text = ParseMetadata(quote.LineItems.FirstOrDefault()?.Notes).GetValueOrDefault("Cycle Time", string.Empty);
        _ipNotes.Text = ParseMetadata(quote.LineItems.FirstOrDefault()?.Notes).GetValueOrDefault("IP Fields", string.Empty);
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
            Padding = new Padding(10),
            BackColor = index % 2 == 0 ? Color.FromArgb(245, 248, 252) : Color.FromArgb(234, 243, 250),
            MinimumSize = new Size(860, 430)
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = false, ColumnCount = 1, RowCount = 6 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        layout.Controls.Add(title, 0, 0);

        var topFields = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, Dock = DockStyle.Top };
        for (var i = 0; i < 4; i++) topFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        var drawingNumber = NewDecimalDisabledField(model.DrawingNumber);
        var drawingName = NewDecimalDisabledField(model.DrawingName);
        var revision = NewDecimalDisabledField(model.Revision);
        var removeButton = new Button { Text = "Remove Line Item", AutoSize = true, BackColor = Color.FromArgb(214, 77, 77), ForeColor = Color.White };
        removeButton.Click += async (_, _) => await RemoveLineItemAsync(model, cardPanel);

        topFields.Controls.Add(NewFieldPanel("Drawing Number", drawingNumber), 0, 0);
        topFields.Controls.Add(NewFieldPanel("Drawing Name", drawingName), 1, 0);
        topFields.Controls.Add(NewFieldPanel("Revision", revision), 2, 0);
        topFields.Controls.Add(new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Right, Controls = { removeButton } }, 3, 0);
        layout.Controls.Add(topFields, 0, 1);

        var drawingDocs = BuildBlobArea(model, QuoteBlobType.Technical, "Drawings (PDF / STEP)");
        var modelDocs = BuildBlobArea(model, QuoteBlobType.ThreeDModel, "3D Models");
        var docsRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top, Margin = new Padding(0, 6, 0, 6) };
        docsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        docsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        docsRow.Controls.Add(drawingDocs.SectionPanel, 0, 0);
        docsRow.Controls.Add(modelDocs.SectionPanel, 1, 0);
        layout.Controls.Add(docsRow, 0, 2);

        var costsRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 5, Dock = DockStyle.Top, Margin = new Padding(0, 6, 0, 6) };
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
        layout.Controls.Add(costsRow, 0, 3);

        var supportDocsRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Top, Margin = new Padding(0, 6, 0, 6) };
        supportDocsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        supportDocsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        supportDocsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        var materialDocs = BuildBlobArea(model, QuoteBlobType.MaterialPricing, "Material Blob Area");
        var toolingDocs = BuildBlobArea(model, QuoteBlobType.ToolingDocumentation, "Tooling Blob Area");
        var postOpDocs = BuildBlobArea(model, QuoteBlobType.PostOpPricing, "Secondary Operations Blob Area");
        supportDocsRow.Controls.Add(materialDocs.SectionPanel, 0, 0);
        supportDocsRow.Controls.Add(toolingDocs.SectionPanel, 1, 0);
        supportDocsRow.Controls.Add(postOpDocs.SectionPanel, 2, 0);
        layout.Controls.Add(supportDocsRow, 0, 4);

        var footer = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Dock = DockStyle.Top };
        var totalBox = new TextBox { ReadOnly = true, Width = 180, Text = model.LineItemTotal.ToString("0.00", CultureInfo.CurrentCulture) };
        footer.Controls.Add(new Label { Text = "Line Item Total", AutoSize = true, Margin = new Padding(0, 8, 8, 0), Font = new Font(Font, FontStyle.Bold) });
        footer.Controls.Add(totalBox);
        layout.Controls.Add(footer);

        cardPanel.Controls.Add(layout);

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
            Total = totalBox,
            BlobLists = new Dictionary<QuoteBlobType, ListBox>
            {
                [QuoteBlobType.Technical] = drawingDocs.List,
                [QuoteBlobType.ThreeDModel] = modelDocs.List,
                [QuoteBlobType.MaterialPricing] = materialDocs.List,
                [QuoteBlobType.ToolingDocumentation] = toolingDocs.List,
                [QuoteBlobType.PostOpPricing] = postOpDocs.List
            }
        };

        RefreshBlobLists(card);
        return card;
    }

    private BlobArea BuildBlobArea(QuoteLineItem model, QuoteBlobType blobType, string title)
    {
        var panel = new Panel
        {
            AutoSize = false,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(6),
            Margin = new Padding(3),
            Dock = DockStyle.Fill,
            MinimumSize = new Size(260, 180)
        };
        var titleLabel = new Label { Text = title, AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Dock = DockStyle.Top };
        var upload = new Button { Text = "Upload File…", AutoSize = true, BackColor = Color.FromArgb(52, 152, 219), ForeColor = Color.White };
        var delete = new Button { Text = "Delete", AutoSize = true };
        var download = new Button { Text = "Download", AutoSize = true };

        var dropZone = new Label
        {
            Text = "Drop files here to upload",
            Dock = DockStyle.Top,
            Height = 34,
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(235, 244, 255),
            AllowDrop = true,
            Margin = new Padding(0, 6, 0, 2)
        };

        upload.Click += async (_, _) => await UploadBlobAsync(model, blobType);
        delete.Click += async (_, _) => await DeleteBlobAsync(model, blobType, list.SelectedItem as QuoteBlobAttachment);
        download.Click += async (_, _) => await DownloadBlobAsync(list.SelectedItem as QuoteBlobAttachment);
        dropZone.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                e.Effect = DragDropEffects.Copy;
            }
        };
        dropZone.DragDrop += async (_, e) => await HandleBlobDropAsync(model, blobType, e.Data?.GetData(DataFormats.FileDrop) as string[]);

        buttons.Controls.Add(upload);
        buttons.Controls.Add(delete);
        buttons.Controls.Add(download);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(titleLabel, 0, 0);
        content.Controls.Add(list, 0, 1);
        content.Controls.Add(dropZone, 0, 2);
        content.Controls.Add(buttons, 0, 3);
        panel.Controls.Add(content);

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
            list.DataSource = null;
            list.DisplayMember = nameof(QuoteBlobAttachment.FileName);
            list.DataSource = card.Model.BlobAttachments.Where(x => x.BlobType == blobType).OrderBy(x => x.FileName).ToList();
        }
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
        => new() { Width = 220, Text = value };

    private static TextBox NewNumericField(decimal value)
    {
        var box = new TextBox { Width = 180, Text = value.ToString("0.00", CultureInfo.CurrentCulture) };
        box.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.' && e.KeyChar != ',')
            {
                e.Handled = true;
            }
        };

        return box;
    }

    private static FlowLayoutPanel NewFieldPanel(string label, Control input)
    {
        var flow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        flow.Controls.Add(new Label { Text = label, AutoSize = true });
        flow.Controls.Add(input);
        return flow;
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
            card.Container.Width = Math.Max(860, _lineItemsPanel.ClientSize.Width - 28);
            card.Container.Height = Math.Max(card.Container.MinimumSize.Height, card.Container.PreferredSize.Height);
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
            line.Notes = BuildLineNotes(_customerPartPo.Text.Trim(), _cycleTime.Text.Trim(), _ipNotes.Text.Trim());
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

    private static string BuildLineNotes(string customerPartPo, string cycleTime, string ipFields)
        => $"Customer Part PO: {customerPartPo}\nCycle Time: {cycleTime}\nIP Fields: {ipFields}";

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
        public required ListBox List { get; init; }
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
        public required TextBox Total { get; init; }
        public required Dictionary<QuoteBlobType, ListBox> BlobLists { get; init; }
    }
}
