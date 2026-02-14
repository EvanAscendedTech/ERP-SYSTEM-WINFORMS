using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Forms;

public class QuoteDraftForm : Form
{
    private readonly QuoteRepository _quoteRepository;
    private readonly TextBox _customerName = new() { Width = 320, PlaceholderText = "Customer name" };
    private readonly TextBox _customerQuoteNumber = new() { Width = 320, PlaceholderText = "Customer quote number" };
    private readonly NumericUpDown _lineCount = new() { Minimum = 1, Maximum = 20, Value = 1, Width = 100 };
    private readonly FlowLayoutPanel _lineItemsPanel = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

    public int CreatedQuoteId { get; private set; }

    public QuoteDraftForm(QuoteRepository quoteRepository)
    {
        _quoteRepository = quoteRepository;

        Text = $"New Quote Draft - {GenerateQuoteFileNumber()}";
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

        header.Controls.Add(_customerName);
        header.Controls.Add(_customerQuoteNumber);
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
    }

    private static string GenerateQuoteFileNumber()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        return $"QF-{new string(Enumerable.Range(0, 6).Select(_ => chars[random.Next(chars.Length)]).ToArray())}";
    }

    private void OpenCustomerCreation()
    {
        using var customerForm = new Form { Width = 500, Height = 340, Text = "Create Customer", StartPosition = FormStartPosition.CenterParent };
        customerForm.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "CRM customer creation form placeholder:\naddress, payment terms, contacts, NDA and legal documents can be collected here."
        });
        customerForm.ShowDialog(this);
    }

    private void RenderLineItems(int count)
    {
        _lineItemsPanel.Controls.Clear();
        for (var i = 1; i <= count; i++)
        {
            _lineItemsPanel.Controls.Add(BuildLineItemCard(i));
        }
    }

    private static Control BuildLineItemCard(int lineIndex)
    {
        var group = new GroupBox { Text = $"Line Item {lineIndex}", Width = 900, Height = 160 };
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill };
        layout.Controls.Add(new TextBox { Width = 200, PlaceholderText = "Description", Name = "Description" });
        layout.Controls.Add(new TextBox { Width = 120, PlaceholderText = "Lead time" });
        layout.Controls.Add(new TextBox { Width = 120, PlaceholderText = "Production hrs" });
        layout.Controls.Add(new TextBox { Width = 120, PlaceholderText = "Setup hrs" });
        layout.Controls.Add(new TextBox { Width = 160, PlaceholderText = "Assigned machine" });
        layout.Controls.Add(new Button { Text = "Upload Technical info", AutoSize = true });
        layout.Controls.Add(new Button { Text = "Upload Material pricing", AutoSize = true });
        layout.Controls.Add(new Button { Text = "Upload Post-op pricing", AutoSize = true });
        group.Controls.Add(layout);
        return group;
    }

    private async Task SaveQuoteAsync()
    {
        if (string.IsNullOrWhiteSpace(_customerName.Text))
        {
            MessageBox.Show("Customer is required.", "Quote", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var quote = new Quote
        {
            CustomerName = _customerName.Text.Trim(),
            Status = QuoteStatus.InProgress,
            CreatedUtc = DateTime.UtcNow,
            LastUpdatedUtc = DateTime.UtcNow
        };

        foreach (GroupBox item in _lineItemsPanel.Controls.OfType<GroupBox>())
        {
            var description = item.Controls.OfType<FlowLayoutPanel>().SelectMany(panel => panel.Controls.OfType<TextBox>()).FirstOrDefault()?.Text;
            quote.LineItems.Add(new QuoteLineItem
            {
                Description = string.IsNullOrWhiteSpace(description) ? $"Line {quote.LineItems.Count + 1}" : description,
                Quantity = 1,
                UnitPrice = 0,
                LeadTimeDays = 7,
                Notes = $"Customer quote #: {_customerQuoteNumber.Text.Trim()}"
            });
        }

        CreatedQuoteId = await _quoteRepository.SaveQuoteAsync(quote);
        DialogResult = DialogResult.OK;
        Close();
    }
}
