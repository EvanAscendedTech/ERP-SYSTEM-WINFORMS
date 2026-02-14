using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

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
    private readonly TextBox _quoteLifecycleId = new() { Width = 220, ReadOnly = true };
    private readonly NumericUpDown _lineCount = new() { Minimum = 1, Maximum = 20, Value = 1, Width = 100 };
    private readonly FlowLayoutPanel _lineItemsPanel = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

    public int CreatedQuoteId { get; private set; }

    public QuoteDraftForm(QuoteRepository quoteRepository, bool canViewPricing, string uploadedBy)
    {
        _quoteRepository = quoteRepository;
        _canViewPricing = canViewPricing;
        _uploadedBy = uploadedBy;
        _quoteLifecycleId.Text = GenerateLifecycleQuoteId();

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

        Text = $"New Quote Draft - {_quoteLifecycleId.Text}";
        Width = 1200;
        Height = 760;
        WindowState = FormWindowState.Maximized;
        StartPosition = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        var createCustomerButton = new Button { Text = "Create New Customer", AutoSize = true };
        createCustomerButton.Click += (_, _) => OpenCustomerCreation();

        header.Controls.Add(new Label { Text = "Customer:", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        header.Controls.Add(_customerPicker);
        header.Controls.Add(_customerQuoteNumber);
        header.Controls.Add(new Label { Text = "Lifecycle ID", AutoSize = true, Margin = new Padding(10, 8, 0, 0) });
        header.Controls.Add(_quoteLifecycleId);
        header.Controls.Add(new Label { Text = "Line items:", AutoSize = true, Margin = new Padding(8, 8, 0, 0) });
        header.Controls.Add(_lineCount);
        header.Controls.Add(createCustomerButton);

        _lineCount.ValueChanged += (_, _) => RenderLineItems((int)_lineCount.Value);
        RenderLineItems((int)_lineCount.Value);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var saveButton = new Button { Text = "Save Quote", AutoSize = true };
        saveButton.Click += async (_, _) => await SaveQuoteAsync();
        buttons.Controls.Add(saveButton);

        root.Controls.Add(header, 0, 0);
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

    private void RenderLineItems(int count)
    {
        _lineItemsPanel.Controls.Clear();
        for (var i = 1; i <= count; i++)
        {
            _lineItemsPanel.Controls.Add(BuildLineItemCard(i, _canViewPricing));
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

    private async Task SaveQuoteAsync()
    {
        if (_customerPicker.SelectedItem is not Customer customer)
        {
            MessageBox.Show("Customer is required.", "Quote", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var quote = new Quote
        {
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            LifecycleQuoteId = _quoteLifecycleId.Text,
            Status = QuoteStatus.InProgress,
            CreatedUtc = DateTime.UtcNow,
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
                Notes = $"Customer quote #: {_customerQuoteNumber.Text.Trim()}"
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
}
