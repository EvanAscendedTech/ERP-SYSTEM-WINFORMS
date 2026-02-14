using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Forms;

public class QuoteDraftForm : Form
{
    private readonly QuoteRepository _quoteRepository;
    private readonly bool _canViewPricing;
    private readonly ComboBox _customerPicker = new() { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _customerQuoteNumber = new() { Width = 220, PlaceholderText = "Customer quote number" };
    private readonly TextBox _quoteLifecycleId = new() { Width = 220, ReadOnly = true };
    private readonly NumericUpDown _lineCount = new() { Minimum = 1, Maximum = 20, Value = 1, Width = 100 };
    private readonly FlowLayoutPanel _lineItemsPanel = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

    public int CreatedQuoteId { get; private set; }

    public QuoteDraftForm(QuoteRepository quoteRepository, bool canViewPricing)
    {
        _quoteRepository = quoteRepository;
        _canViewPricing = canViewPricing;
        _quoteLifecycleId.Text = GenerateLifecycleQuoteId();

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

    private static Control BuildLineItemCard(int lineIndex, bool canViewPricing)
    {
        var group = new GroupBox { Text = $"Line Item {lineIndex}", Width = 1000, Height = 200 };
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill };
        layout.Controls.Add(new TextBox { Width = 200, PlaceholderText = "Description", Name = "Description" });
        layout.Controls.Add(new TextBox { Width = 120, PlaceholderText = "Lead time" });

        var pricing = new TextBox { Width = 120, PlaceholderText = "Unit price", Name = "UnitPrice", Visible = canViewPricing };
        layout.Controls.Add(pricing);

        layout.Controls.Add(new TextBox { Width = 120, PlaceholderText = "Production hrs" });
        layout.Controls.Add(new TextBox { Width = 120, PlaceholderText = "Setup hrs" });

        layout.Controls.Add(BuildBlobUploadButton("Upload technical", QuoteBlobType.Technical));
        layout.Controls.Add(BuildBlobUploadButton("Upload material pricing", QuoteBlobType.MaterialPricing));
        layout.Controls.Add(BuildBlobUploadButton("Upload post op pricing", QuoteBlobType.PostOpPricing));
        group.Controls.Add(layout);
        return group;
    }

    private static Button BuildBlobUploadButton(string text, QuoteBlobType blobType)
    {
        var button = new Button { Text = text, AutoSize = true, Tag = new List<QuoteBlobAttachment>() };
        button.Click += (_, _) =>
        {
            using var picker = new OpenFileDialog { Multiselect = true, Title = text };
            if (picker.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            var blobs = (List<QuoteBlobAttachment>)button.Tag;
            foreach (var file in picker.FileNames)
            {
                blobs.Add(new QuoteBlobAttachment
                {
                    BlobType = blobType,
                    FileName = Path.GetFileName(file),
                    ContentType = Path.GetExtension(file),
                    BlobData = File.ReadAllBytes(file),
                    UploadedUtc = DateTime.UtcNow
                });
            }
        };
        return button;
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
            var panel = item.Controls.OfType<FlowLayoutPanel>().First();
            var textboxes = panel.Controls.OfType<TextBox>().ToList();
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

            line.BlobAttachments = panel.Controls.OfType<Button>()
                .Where(btn => btn.Tag is List<QuoteBlobAttachment>)
                .SelectMany(btn => (List<QuoteBlobAttachment>)btn.Tag)
                .ToList();

            quote.LineItems.Add(line);
        }

        CreatedQuoteId = await _quoteRepository.SaveQuoteAsync(quote);
        DialogResult = DialogResult.OK;
        Close();
    }
}
