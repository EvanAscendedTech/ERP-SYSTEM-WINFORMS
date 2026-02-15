using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;
using System.Globalization;
using System.Text;

namespace ERPSystem.WinForms.Forms;

public class QuoteDraftForm : Form
{
    private sealed class BlobUploadSectionState
    {
        public required QuoteBlobType BlobType { get; init; }
        public required string Title { get; init; }
        public required Label DropZoneLabel { get; init; }
        public required DataGridView UploadGrid { get; init; }
        public required Dictionary<string, int> RowByFilePath { get; init; }
        public List<QuoteBlobAttachment> Attachments { get; } = new();
    }

    private readonly QuoteRepository _quoteRepository;
    private readonly BlobImportService _blobImportService;
    private readonly bool _canViewPricing;
    private readonly string _uploadedBy;
    private readonly ComboBox _customerPicker = new() { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _customerQuoteNumber = new() { Width = 220, PlaceholderText = "Customer quote number" };
    private readonly TextBox _customerAddress = new() { Width = 320, PlaceholderText = "Customer address" };
    private readonly TextBox _pricingAdjustment = new() { Width = 160, PlaceholderText = "Pricing adjustment" };
    private readonly TextBox _quoteLifecycleId = new() { Width = 220, ReadOnly = true };
    private readonly NumericUpDown _lineCount = new() { Minimum = 1, Maximum = 20, Value = 1, Width = 100 };
    private readonly FlowLayoutPanel _lineItemsPanel = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
    private readonly Quote? _editingQuote;

    public int CreatedQuoteId { get; private set; }
    public bool WasDeleted { get; private set; }

    public QuoteDraftForm(QuoteRepository quoteRepository, bool canViewPricing, string uploadedBy, Quote? editingQuote = null)
    {
        _quoteRepository = quoteRepository;
        _canViewPricing = canViewPricing;
        _uploadedBy = uploadedBy;
        _editingQuote = editingQuote;
        _quoteLifecycleId.Text = string.IsNullOrWhiteSpace(editingQuote?.LifecycleQuoteId) ? GenerateLifecycleQuoteId() : editingQuote.LifecycleQuoteId;

        _blobImportService = new BlobImportService((quoteId, lineItemId, lifecycleId, blobType, fileName, extension, fileSizeBytes, sha256, uploadedByValue, uploadedUtc, blobData) =>
        {
            return Task.FromResult(new QuoteBlobAttachment
            {
                QuoteId = quoteId,
                LineItemId = lineItemId,
                LifecycleId = lifecycleId,
                BlobType = blobType,
                FileName = fileName,
                Extension = extension,
                ContentType = extension,
                FileSizeBytes = fileSizeBytes,
                Sha256 = sha256,
                UploadedBy = uploadedByValue,
                UploadedUtc = uploadedUtc,
                BlobData = blobData
            });
        });
        _blobImportService.UploadProgressChanged += OnBlobUploadProgressChanged;

        Text = _editingQuote is null ? $"New Quote Draft - {_quoteLifecycleId.Text}" : $"Edit In-Process Quote #{_editingQuote.Id} - {_quoteLifecycleId.Text}";
        Width = 1200;
        Height = 760;
        WindowState = FormWindowState.Maximized;
        StartPosition = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var headerContainer = new Panel { Dock = DockStyle.Top, Height = 42 };
        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = false, WrapContents = true };
        var createCustomerButton = new Button { Text = "Create New Customer", AutoSize = true };
        var generatePdfButton = new Button { Text = "Generate Quote PDF", AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        createCustomerButton.Click += (_, _) => OpenCustomerCreation();
        generatePdfButton.Click += async (_, _) => await GenerateQuotePdfAsync();

        header.Controls.Add(new Label { Text = "Customer:", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        header.Controls.Add(_customerPicker);
        header.Controls.Add(_customerAddress);
        header.Controls.Add(_customerQuoteNumber);
        header.Controls.Add(new Label { Text = "Lifecycle ID", AutoSize = true, Margin = new Padding(10, 8, 0, 0) });
        header.Controls.Add(_quoteLifecycleId);
        header.Controls.Add(new Label { Text = "Adjustment", AutoSize = true, Margin = new Padding(8, 8, 0, 0) });
        header.Controls.Add(_pricingAdjustment);
        header.Controls.Add(new Label { Text = "Line items:", AutoSize = true, Margin = new Padding(8, 8, 0, 0) });
        header.Controls.Add(_lineCount);
        header.Controls.Add(createCustomerButton);

        headerContainer.Controls.Add(header);
        headerContainer.Controls.Add(generatePdfButton);
        generatePdfButton.Location = new Point(headerContainer.Width - generatePdfButton.Width - 6, 6);
        headerContainer.Resize += (_, _) =>
        {
            generatePdfButton.Location = new Point(headerContainer.Width - generatePdfButton.Width - 6, 6);
        };

        _lineCount.ValueChanged += (_, _) => SyncLineItems((int)_lineCount.Value);
        SyncLineItems((int)_lineCount.Value);

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

        root.Controls.Add(headerContainer, 0, 0);
        root.Controls.Add(_lineItemsPanel, 0, 1);
        root.Controls.Add(buttons, 0, 2);

        Controls.Add(root);
        _ = LoadCustomersAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _blobImportService.UploadProgressChanged -= OnBlobUploadProgressChanged;
        }

        base.Dispose(disposing);
    }

    private static string GenerateLifecycleQuoteId()
    {
        return $"Q-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
    }

    private async Task LoadCustomersAsync()
    {
        var customers = await _quoteRepository.GetCustomersAsync();
        _customerPicker.DataSource = customers.ToList();
        _customerPicker.DisplayMember = nameof(Customer.DisplayLabel);
        _customerPicker.ValueMember = nameof(Customer.Id);

        if (_editingQuote is not null)
        {
            PopulateFromQuote(_editingQuote);
        }
    }

    private void PopulateFromQuote(Quote quote)
    {
        _customerPicker.SelectedValue = quote.CustomerId;
        _quoteLifecycleId.Text = quote.LifecycleQuoteId;
        _lineCount.Value = Math.Max(1, Math.Min((int)_lineCount.Maximum, quote.LineItems.Count));
        SyncLineItems((int)_lineCount.Value);

        for (var index = 0; index < quote.LineItems.Count && index < _lineItemsPanel.Controls.Count; index++)
        {
            PopulateLineItem((GroupBox)_lineItemsPanel.Controls[index], quote.LineItems[index]);
        }
    }

    private void PopulateLineItem(GroupBox group, QuoteLineItem line)
    {
        var table = group.Controls.OfType<TableLayoutPanel>().First();
        var fields = table.Controls.OfType<FlowLayoutPanel>().First();
        var uploadFlow = table.Controls.OfType<FlowLayoutPanel>().Skip(1).First();

        var textboxes = fields.Controls.OfType<TextBox>().ToList();
        if (textboxes.Count > 0) textboxes[0].Text = line.Description;
        if (textboxes.Count > 1) textboxes[1].Text = line.LeadTimeDays.ToString();

        var unitPrice = textboxes.FirstOrDefault(t => t.Name == "UnitPrice");
        if (unitPrice is not null) unitPrice.Text = line.UnitPrice.ToString();

        var metadata = ParseMetadata(line.Notes);
        if (metadata.TryGetValue("Customer quote #", out var quoteNumber))
        {
            _customerQuoteNumber.Text = quoteNumber;
        }

        if (metadata.TryGetValue("Customer address", out var address))
        {
            _customerAddress.Text = address;
        }

        if (metadata.TryGetValue("Pricing adjustment", out var adjustment))
        {
            _pricingAdjustment.Text = adjustment;
        }

        foreach (var section in uploadFlow.Controls.OfType<Panel>().Select(p => p.Tag).OfType<BlobUploadSectionState>())
        {
            foreach (var attachment in line.BlobAttachments.Where(a => a.BlobType == section.BlobType))
            {
                section.Attachments.Add(attachment);
                var rowIndex = section.UploadGrid.Rows.Add(attachment.FileName, BlobUploadStatus.Done.ToString());
                section.UploadGrid.Rows[rowIndex].Tag = attachment.FileName;
            }
        }
    }

    private void OpenCustomerCreation()
    {
        using var customerForm = new Form { Width = 500, Height = 340, Text = "Create Customer", StartPosition = FormStartPosition.CenterParent };
        customerForm.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "Use the CRM section to create customers and load multiple contacts."
        });
        customerForm.ShowDialog(this);
    }

    private void SyncLineItems(int targetCount)
    {
        var currentCount = _lineItemsPanel.Controls.Count;

        if (targetCount > currentCount)
        {
            for (var i = currentCount + 1; i <= targetCount; i++)
            {
                _lineItemsPanel.Controls.Add(BuildLineItemCard(i, _canViewPricing));
            }

            return;
        }

        if (targetCount < currentCount)
        {
            for (var i = currentCount - 1; i >= targetCount; i--)
            {
                _lineItemsPanel.Controls.RemoveAt(i);
            }
        }
    }

    private Control BuildLineItemCard(int lineIndex, bool canViewPricing)
    {
        var group = new GroupBox { Text = $"Line Item {lineIndex}", Width = 1100, Height = 420 };
        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(6)
        };
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var fieldsLayout = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        fieldsLayout.Controls.Add(new TextBox { Width = 200, PlaceholderText = "Description", Name = "Description" });
        fieldsLayout.Controls.Add(new TextBox { Width = 120, PlaceholderText = "Lead time" });

        var pricing = new TextBox { Width = 120, PlaceholderText = "Unit price", Name = "UnitPrice", Visible = canViewPricing };
        fieldsLayout.Controls.Add(pricing);

        fieldsLayout.Controls.Add(new TextBox { Width = 120, PlaceholderText = "Production hrs" });
        fieldsLayout.Controls.Add(new TextBox { Width = 120, PlaceholderText = "Setup hrs" });

        var uploadLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true
        };

        uploadLayout.Controls.Add(BuildBlobUploadSection("Technical Files", QuoteBlobType.Technical));
        uploadLayout.Controls.Add(BuildBlobUploadSection("Material Pricing", QuoteBlobType.MaterialPricing));
        uploadLayout.Controls.Add(BuildBlobUploadSection("Post-Op Pricing", QuoteBlobType.PostOpPricing));

        container.Controls.Add(fieldsLayout, 0, 0);
        container.Controls.Add(uploadLayout, 0, 1);
        group.Controls.Add(container);
        return group;
    }

    private Control BuildBlobUploadSection(string title, QuoteBlobType blobType)
    {
        var uploadGrid = new DataGridView
        {
            Width = 312,
            Height = 188,
            Top = 108,
            Left = 6,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false
        };

        uploadGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "FileName", HeaderText = "File", Width = 150 });
        uploadGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", Width = 80 });
        uploadGrid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "Remove",
            HeaderText = "",
            Text = "Remove",
            UseColumnTextForButtonValue = true,
            Width = 70
        });

        var dropZone = new Label
        {
            Text = "Click or drag files here",
            AutoSize = false,
            Width = 312,
            Height = 58,
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
            Cursor = Cursors.Hand,
            Top = 26,
            Left = 6,
            AllowDrop = true
        };

        var state = new BlobUploadSectionState
        {
            BlobType = blobType,
            Title = title,
            DropZoneLabel = dropZone,
            UploadGrid = uploadGrid,
            RowByFilePath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        };

        var panel = new Panel
        {
            Width = 330,
            Height = 302,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(6),
            Tag = state
        };

        var sectionTitle = new Label { Text = title, AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };

        var uploadedLabel = new Label
        {
            Text = "Uploaded files:",
            AutoSize = true,
            Top = 88,
            Left = 6
        };

        dropZone.Click += async (_, _) => await BrowseAndQueueAsync(state);
        dropZone.DragEnter += DropZoneOnDragEnter;
        dropZone.DragDrop += async (_, e) => await DropZoneOnDragDropAsync(state, e);
        uploadGrid.CellDoubleClick += (_, e) => OpenBlobAction(state, e.RowIndex);
        uploadGrid.CellContentClick += async (_, e) => await RemoveBlobAsync(state, e);

        panel.Controls.Add(sectionTitle);
        panel.Controls.Add(dropZone);
        panel.Controls.Add(uploadedLabel);
        panel.Controls.Add(uploadGrid);
        return panel;
    }

    private async Task BrowseAndQueueAsync(BlobUploadSectionState state)
    {
        try
        {
            using var picker = new OpenFileDialog
            {
                Multiselect = true,
                Title = $"Upload {state.Title}",
                Filter = "PDF files (*.pdf)|*.pdf|STEP files (*.step;*.stp)|*.step;*.stp|IGES files (*.iges;*.igs)|*.iges;*.igs|All files (*.*)|*.*"
            };

            if (picker.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            await EnqueueFilesForSectionAsync(state, picker.FileNames);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to select files: {ex.Message}", "File Upload", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DropZoneOnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            e.Effect = DragDropEffects.Copy;
            return;
        }

        e.Effect = DragDropEffects.None;
    }

    private async Task DropZoneOnDragDropAsync(BlobUploadSectionState state, DragEventArgs e)
    {
        try
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files)
            {
                return;
            }

            await EnqueueFilesForSectionAsync(state, files);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to import dropped files: {ex.Message}", "File Upload", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task EnqueueFilesForSectionAsync(BlobUploadSectionState state, IEnumerable<string> filePaths)
    {
        var files = filePaths.Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            return;
        }

        EnsureRowsForFiles(state, files);

        var results = await _blobImportService.EnqueueFilesAsync(
            quoteId: 0,
            lineItemId: 0,
            lifecycleId: _quoteLifecycleId.Text,
            blobType: state.BlobType,
            filePaths: files,
            uploadedBy: _uploadedBy);

        foreach (var result in results)
        {
            if (result.IsSuccess && result.Attachment is not null)
            {
                state.Attachments.Add(result.Attachment);
            }
            else if (!result.IsSuccess)
            {
                MessageBox.Show($"Failed to upload {result.FileName}: {result.ErrorMessage}", "File Upload", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    private static void EnsureRowsForFiles(BlobUploadSectionState state, IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            if (state.RowByFilePath.ContainsKey(file))
            {
                continue;
            }

            var rowIndex = state.UploadGrid.Rows.Add(Path.GetFileName(file), BlobUploadStatus.Queued.ToString());
            state.RowByFilePath[file] = rowIndex;
            state.UploadGrid.Rows[rowIndex].Tag = file;
        }
    }

    private void OnBlobUploadProgressChanged(object? sender, BlobUploadProgressEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnBlobUploadProgressChanged(sender, e)));
            return;
        }

        foreach (var state in _lineItemsPanel.Controls
                     .OfType<GroupBox>()
                     .SelectMany(group => group.Controls.OfType<TableLayoutPanel>())
                     .SelectMany(table => table.Controls.OfType<FlowLayoutPanel>())
                     .SelectMany(flow => flow.Controls.OfType<Panel>())
                     .Select(panel => panel.Tag)
                     .OfType<BlobUploadSectionState>())
        {
            if (!state.RowByFilePath.TryGetValue(e.FilePath, out var rowIndex) || rowIndex < 0 || rowIndex >= state.UploadGrid.Rows.Count)
            {
                continue;
            }

            state.UploadGrid.Rows[rowIndex].Cells[1].Value = e.Status.ToString();
            if (e.Status == BlobUploadStatus.Failed && !string.IsNullOrWhiteSpace(e.ErrorMessage))
            {
                state.UploadGrid.Rows[rowIndex].Cells[1].Value = $"Failed";
                state.UploadGrid.Rows[rowIndex].ErrorText = e.ErrorMessage;
            }
        }
    }

    private async Task RemoveBlobAsync(BlobUploadSectionState state, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        if (!string.Equals(state.UploadGrid.Columns[e.ColumnIndex].Name, "Remove", StringComparison.Ordinal))
        {
            return;
        }

        if (e.RowIndex >= state.UploadGrid.Rows.Count)
        {
            return;
        }

        var filePath = state.UploadGrid.Rows[e.RowIndex].Tag as string;
        var fileName = state.UploadGrid.Rows[e.RowIndex].Cells[0].Value?.ToString() ?? string.Empty;
        var attachment = state.Attachments.FirstOrDefault(item => string.Equals(item.FileName, fileName, StringComparison.OrdinalIgnoreCase));

        if (attachment is not null && attachment.Id > 0)
        {
            try
            {
                await _quoteRepository.DeleteQuoteLineItemFileAsync(attachment.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to remove file: {ex.Message}", "File Upload", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        if (filePath is not null)
        {
            state.RowByFilePath.Remove(filePath);
        }

        if (attachment is not null)
        {
            state.Attachments.Remove(attachment);
        }

        state.UploadGrid.Rows.RemoveAt(e.RowIndex);
    }

    private static void OpenBlobAction(BlobUploadSectionState state, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= state.UploadGrid.Rows.Count || rowIndex >= state.Attachments.Count)
        {
            return;
        }

        var blob = state.Attachments[rowIndex];
        var isPdf = string.Equals(Path.GetExtension(blob.FileName), ".pdf", StringComparison.OrdinalIgnoreCase);

        if (isPdf)
        {
            var previewResult = MessageBox.Show(
                "Select Yes to preview this PDF in the system. Select No to download it.",
                "PDF Attachment",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (previewResult == DialogResult.Yes)
            {
                ShowPdfPreview(blob);
                return;
            }

            if (previewResult == DialogResult.Cancel)
            {
                return;
            }
        }

        using var saver = new SaveFileDialog
        {
            FileName = blob.FileName,
            Filter = "All files (*.*)|*.*"
        };

        if (saver.ShowDialog() == DialogResult.OK)
        {
            File.WriteAllBytes(saver.FileName, blob.BlobData);
        }
    }

    private static void ShowPdfPreview(QuoteBlobAttachment blob)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_{blob.FileName}");
        File.WriteAllBytes(tempPath, blob.BlobData);

        var previewForm = new Form
        {
            Text = $"PDF Preview - {blob.FileName}",
            Width = 900,
            Height = 700,
            StartPosition = FormStartPosition.CenterParent
        };

        var browser = new WebBrowser
        {
            Dock = DockStyle.Fill,
            Url = new Uri(tempPath)
        };

        previewForm.FormClosed += (_, _) =>
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best effort cleanup.
            }
        };

        previewForm.Controls.Add(browser);
        previewForm.ShowDialog();
    }

    private async Task DeleteQuoteAsync()
    {
        if (_editingQuote is null)
        {
            return;
        }

        var confirm = MessageBox.Show($"Delete quote #{_editingQuote.Id}? This cannot be undone.", "Delete Quote", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        try
        {
            await _quoteRepository.DeleteQuoteAsync(_editingQuote.Id);
            WasDeleted = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to delete quote: {ex.Message}", "Quote", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task SaveQuoteAsync()
    {
        if (_customerPicker.SelectedItem is not Customer customer)
        {
            MessageBox.Show("Customer is required.", "Quote", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var quote = new Quote
        {
            Id = _editingQuote?.Id ?? 0,
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            LifecycleQuoteId = _quoteLifecycleId.Text,
            Status = _editingQuote?.Status ?? QuoteStatus.InProgress,
            CreatedUtc = _editingQuote?.CreatedUtc ?? DateTime.UtcNow,
            LastUpdatedUtc = DateTime.UtcNow
        };

        foreach (GroupBox item in _lineItemsPanel.Controls.OfType<GroupBox>())
        {
            var table = item.Controls.OfType<TableLayoutPanel>().First();
            var fields = table.Controls.OfType<FlowLayoutPanel>().First();
            var uploadFlow = table.Controls.OfType<FlowLayoutPanel>().Skip(1).First();

            var textboxes = fields.Controls.OfType<TextBox>().ToList();
            var description = textboxes.FirstOrDefault()?.Text;
            var priceText = textboxes.FirstOrDefault(t => t.Name == "UnitPrice")?.Text;

            var line = new QuoteLineItem
            {
                Description = string.IsNullOrWhiteSpace(description) ? $"Line {quote.LineItems.Count + 1}" : description,
                Quantity = 1,
                UnitPrice = decimal.TryParse(priceText, out var unitPrice) ? unitPrice : 0,
                LeadTimeDays = 7,
                Notes = BuildLineNotes(_customerQuoteNumber.Text.Trim(), _customerAddress.Text.Trim(), _pricingAdjustment.Text.Trim())
            };

            line.BlobAttachments = uploadFlow.Controls.OfType<Panel>()
                .Select(control => control.Tag)
                .OfType<BlobUploadSectionState>()
                .SelectMany(section => section.Attachments)
                .ToList();

            quote.LineItems.Add(line);
        }

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

    private async Task GenerateQuotePdfAsync()
    {
        if (_customerPicker.SelectedItem is not Customer customer)
        {
            MessageBox.Show("Select a customer before generating the quote PDF.", "Quote PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var lineSummaries = _lineItemsPanel.Controls.OfType<GroupBox>()
            .Select(ReadLineSummary)
            .ToList();

        if (lineSummaries.Count == 0)
        {
            MessageBox.Show("Add at least one line item before generating the quote PDF.", "Quote PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var adjustment = decimal.TryParse(_pricingAdjustment.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var parsedAdjustment)
            ? parsedAdjustment
            : 0m;

        var lineTotal = lineSummaries.Sum(line => line.LineTotal);
        var grandTotal = lineTotal + adjustment;
        var leadTimeDays = lineSummaries.Max(line => line.LeadTimeDays);
        var quoteIdentifier = _editingQuote is not null ? $"#{_editingQuote.Id} ({_quoteLifecycleId.Text})" : _quoteLifecycleId.Text;
        var address = string.IsNullOrWhiteSpace(_customerAddress.Text) ? "Not provided" : _customerAddress.Text.Trim();

        var documentLines = new List<string>
        {
            "Quote",
            $"Customer Name: {customer.Name}",
            $"Customer Address: {address}",
            $"Quote ID: {quoteIdentifier}",
            $"Lead Time: {leadTimeDays} days",
            string.Empty,
            "Line Items",
            "Description | Quantity"
        };

        documentLines.AddRange(lineSummaries.Select(line => $"{line.Description} | {line.Quantity.ToString("0.##", CultureInfo.InvariantCulture)}"));
        documentLines.Add(string.Empty);
        documentLines.Add($"Total Price: {lineTotal:C}");
        documentLines.Add($"Pricing Adjustment: {adjustment:C}");
        documentLines.Add($"Sum Total: {grandTotal:C}");
        documentLines.Add(string.Empty);
        documentLines.Add("Quality Statement: We are committed to delivering consistent, traceable, and high-quality products that meet or exceed customer specifications.");

        using var saver = new SaveFileDialog
        {
            FileName = $"Quote_{_quoteLifecycleId.Text}.pdf",
            Filter = "PDF files (*.pdf)|*.pdf"
        };

        if (saver.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        try
        {
            var pdfBytes = BuildSimplePdf(documentLines);
            await File.WriteAllBytesAsync(saver.FileName, pdfBytes);
            MessageBox.Show("Quote PDF generated.", "Quote PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to generate PDF: {ex.Message}", "Quote PDF", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private (string Description, decimal Quantity, int LeadTimeDays, decimal LineTotal) ReadLineSummary(GroupBox item)
    {
        var table = item.Controls.OfType<TableLayoutPanel>().First();
        var fields = table.Controls.OfType<FlowLayoutPanel>().First();
        var textboxes = fields.Controls.OfType<TextBox>().ToList();
        var description = string.IsNullOrWhiteSpace(textboxes.FirstOrDefault()?.Text)
            ? item.Text
            : textboxes.First().Text.Trim();

        var leadTimeDays = int.TryParse(textboxes.Skip(1).FirstOrDefault()?.Text, out var parsedLeadTime)
            ? parsedLeadTime
            : 7;

        var quantity = 1m;
        var unitPriceText = textboxes.FirstOrDefault(t => t.Name == "UnitPrice")?.Text;
        var unitPrice = decimal.TryParse(unitPriceText, NumberStyles.Any, CultureInfo.CurrentCulture, out var parsedPrice)
            ? parsedPrice
            : 0m;

        return (description, quantity, leadTimeDays, quantity * unitPrice);
    }

    private static string BuildLineNotes(string customerQuoteNumber, string customerAddress, string adjustment)
    {
        return $"Customer quote #: {customerQuoteNumber}\nCustomer address: {customerAddress}\nPricing adjustment: {adjustment}";
    }

    private static Dictionary<string, string> ParseMetadata(string notes)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in notes.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0 || separator >= line.Length - 1)
            {
                continue;
            }

            metadata[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return metadata;
    }

    private static byte[] BuildSimplePdf(IReadOnlyCollection<string> lines)
    {
        var escapedLines = lines.Select(EscapePdfText).ToList();
        var y = 780;
        var content = new StringBuilder("BT\n/F1 12 Tf\n");
        foreach (var line in escapedLines)
        {
            content.AppendLine($"72 {y} Td ({line}) Tj");
            content.AppendLine("0 -18 Td");
            y -= 18;
        }

        content.Append("ET");
        var contentBytes = Encoding.ASCII.GetBytes(content.ToString());

        var objects = new List<string>
        {
            "1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n",
            "2 0 obj << /Type /Pages /Count 1 /Kids [3 0 R] >> endobj\n",
            "3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >> endobj\n",
            "4 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> endobj\n",
            $"5 0 obj << /Length {contentBytes.Length} >> stream\n{Encoding.ASCII.GetString(contentBytes)}\nendstream endobj\n"
        };

        using var stream = new MemoryStream();
        var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\n" };
        writer.Write("%PDF-1.4\n");
        writer.Flush();

        var offsets = new List<long> { 0 };
        foreach (var obj in objects)
        {
            offsets.Add(stream.Position);
            writer.Write(obj);
            writer.Flush();
        }

        var xrefPosition = stream.Position;
        writer.Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            writer.Write($"{offset:0000000000} 00000 n \n");
        }

        writer.Write($"trailer << /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefPosition}\n%%EOF");
        writer.Flush();
        return stream.ToArray();
    }

    private static string EscapePdfText(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
    }
}
