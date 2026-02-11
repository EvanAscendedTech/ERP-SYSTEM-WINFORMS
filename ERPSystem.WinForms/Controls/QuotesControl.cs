using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Controls;

public class QuotesControl : UserControl
{
    private readonly QuoteRepository _quoteRepository;

    private readonly NumericUpDown _quoteIdInput = new() { Minimum = 0, Maximum = int.MaxValue, Width = 120 };
    private readonly ComboBox _customerInput = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly ComboBox _statusInput = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly DataGridView _lineItemsGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false };
    private readonly Label _feedback = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };

    public QuotesControl(QuoteRepository quoteRepository)
    {
        _quoteRepository = quoteRepository;
        Dock = DockStyle.Fill;

        ConfigureStatusInput();
        ConfigureLineItemGrid();
        _ = LoadCustomersAsync();

        var topPanel = BuildHeaderPanel();
        var actionsPanel = BuildActionsPanel();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(topPanel, 0, 0);
        root.Controls.Add(actionsPanel, 0, 1);
        root.Controls.Add(_lineItemsGrid, 0, 2);
        root.Controls.Add(_feedback, 0, 3);

        Controls.Add(root);
    }

    private Control BuildHeaderPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            Padding = new Padding(8)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        panel.Controls.Add(new Label { Text = "Quote ID", Margin = new Padding(0, 8, 6, 0), AutoSize = true }, 0, 0);
        panel.Controls.Add(_quoteIdInput, 1, 0);
        panel.Controls.Add(new Label { Text = "Customer", Margin = new Padding(16, 8, 6, 0), AutoSize = true }, 2, 0);
        panel.Controls.Add(_customerInput, 3, 0);
        panel.Controls.Add(new Label { Text = "Status", Margin = new Padding(0, 8, 6, 0), AutoSize = true }, 0, 1);
        panel.Controls.Add(_statusInput, 1, 1);

        return panel;
    }

    private Control BuildActionsPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(8)
        };

        var addRow = new Button { Text = "Add Line", AutoSize = true };
        addRow.Click += (_, _) => _lineItemsGrid.Rows.Add();

        var saveQuote = new Button { Text = "Save Quote", AutoSize = true };
        saveQuote.Click += async (_, _) => await SaveQuoteAsync();

        var loadQuote = new Button { Text = "Load Quote", AutoSize = true };
        loadQuote.Click += async (_, _) => await LoadQuoteAsync();

        var markWon = new Button { Text = "Mark Won", AutoSize = true };
        markWon.Click += async (_, _) => await UpdateStatusAsync(QuoteStatus.Won);

        var markLost = new Button { Text = "Mark Lost", AutoSize = true };
        markLost.Click += async (_, _) => await UpdateStatusAsync(QuoteStatus.Lost);

        var markExpired = new Button { Text = "Mark Expired", AutoSize = true };
        markExpired.Click += async (_, _) => await UpdateStatusAsync(QuoteStatus.Expired);

        panel.Controls.Add(addRow);
        panel.Controls.Add(saveQuote);
        panel.Controls.Add(loadQuote);
        panel.Controls.Add(markWon);
        panel.Controls.Add(markLost);
        panel.Controls.Add(markExpired);

        return panel;
    }

    private void ConfigureStatusInput()
    {
        _statusInput.DataSource = Enum.GetValues(typeof(QuoteStatus));
    }

    private async Task LoadCustomersAsync()
    {
        try
        {
            var customers = await _quoteRepository.GetCustomersAsync();
            _customerInput.DataSource = customers.ToList();
            _customerInput.DisplayMember = nameof(Customer.DisplayLabel);
            _customerInput.ValueMember = nameof(Customer.Id);

            if (customers.Count == 0)
            {
                ShowFeedback("No customers found. Existing quotes will still load using legacy names.");
            }
        }
        catch (Exception ex)
        {
            ShowFeedback($"Unable to load customers: {ex.Message}");
        }
    }

    private void ConfigureLineItemGrid()
    {
        _lineItemsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Description", HeaderText = "Description", Width = 500 });
        _lineItemsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Quantity", HeaderText = "Quantity", Width = 120 });
        _lineItemsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "UnitPrice", HeaderText = "Unit Price", Width = 120 });
        _lineItemsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LeadTimeDays", HeaderText = "Lead Time (days)", Width = 120 });
        _lineItemsGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "RequiresGForce", HeaderText = "G-Force", Width = 80 });
        _lineItemsGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "RequiresSecondary", HeaderText = "Secondary", Width = 90 });
        _lineItemsGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "RequiresPlating", HeaderText = "Plating", Width = 80 });
        _lineItemsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Files", HeaderText = "Associated Files (semicolon separated)", Width = 320 });
        _lineItemsGrid.AllowUserToAddRows = false;
    }

    private async Task SaveQuoteAsync()
    {
        try
        {
            var quote = BuildQuoteFromInputs();
            if (quote is null)
            {
                return;
            }

            var savedId = await _quoteRepository.SaveQuoteAsync(quote);
            _quoteIdInput.Value = savedId;
            ShowFeedback($"Quote {savedId} saved with {quote.LineItems.Count} line items.");
        }
        catch (Exception ex)
        {
            ShowFeedback($"Save failed: {ex.Message}");
        }
    }

    private async Task LoadQuoteAsync()
    {
        try
        {
            var quoteId = (int)_quoteIdInput.Value;
            if (quoteId <= 0)
            {
                ShowFeedback("Enter a quote ID greater than zero before loading.");
                return;
            }

            var quote = await _quoteRepository.GetQuoteAsync(quoteId);
            if (quote is null)
            {
                ShowFeedback($"Quote {quoteId} does not exist.");
                return;
            }

            PopulateInputs(quote);
            ShowFeedback($"Loaded quote {quote.Id} ({quote.Status}).");
        }
        catch (Exception ex)
        {
            ShowFeedback($"Load failed: {ex.Message}");
        }
    }

    private async Task UpdateStatusAsync(QuoteStatus nextStatus)
    {
        var quoteId = (int)_quoteIdInput.Value;
        if (quoteId <= 0)
        {
            ShowFeedback("Enter a quote ID greater than zero before changing status.");
            return;
        }

        var updated = await _quoteRepository.UpdateStatusAsync(quoteId, nextStatus);
        if (!updated)
        {
            ShowFeedback($"Status transition to {nextStatus} failed for quote {quoteId}. Allowed only from InProgress.");
            return;
        }

        await LoadQuoteAsync();
    }

    private Quote? BuildQuoteFromInputs()
    {
        if (_customerInput.SelectedItem is not Customer customer)
        {
            ShowFeedback("Select a customer before saving the quote.");
            return null;
        }

        var quote = new Quote
        {
            Id = (int)_quoteIdInput.Value,
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            Status = (QuoteStatus)(_statusInput.SelectedItem ?? QuoteStatus.InProgress)
        };

        foreach (DataGridViewRow row in _lineItemsGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var description = row.Cells[0].Value?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            var quantity = decimal.TryParse(row.Cells[1].Value?.ToString(), out var parsedQty) ? parsedQty : 1m;
            var unitPrice = decimal.TryParse(row.Cells[2].Value?.ToString(), out var parsedPrice) ? parsedPrice : 0m;
            var leadTimeDays = int.TryParse(row.Cells[3].Value?.ToString(), out var parsedLeadTime) ? parsedLeadTime : 0;
            var requiresGForce = ParseCheckCell(row.Cells[4].Value);
            var requiresSecondary = ParseCheckCell(row.Cells[5].Value);
            var requiresPlating = ParseCheckCell(row.Cells[6].Value);
            var filesText = row.Cells[7].Value?.ToString() ?? string.Empty;

            quote.LineItems.Add(new QuoteLineItem
            {
                Description = description,
                Quantity = quantity,
                UnitPrice = unitPrice,
                LeadTimeDays = leadTimeDays,
                RequiresGForce = requiresGForce,
                RequiresSecondaryProcessing = requiresSecondary,
                RequiresPlating = requiresPlating,
                AssociatedFiles = filesText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            });
        }

        return quote;
    }

    private void PopulateInputs(Quote quote)
    {
        _quoteIdInput.Value = quote.Id;
        _statusInput.SelectedItem = quote.Status;

        if (TrySelectCustomer(quote.CustomerId) || TrySelectCustomerByName(quote.CustomerName))
        {
            // customer selected
        }
        else
        {
            ShowFeedback($"Quote references legacy customer '{quote.CustomerName}'. Review and resave to link it.");
        }

        _lineItemsGrid.Rows.Clear();
        foreach (var item in quote.LineItems)
        {
            _lineItemsGrid.Rows.Add(
                item.Description,
                item.Quantity,
                item.UnitPrice,
                item.LeadTimeDays,
                item.RequiresGForce,
                item.RequiresSecondaryProcessing,
                item.RequiresPlating,
                string.Join(';', item.AssociatedFiles));
        }
    }

    private bool TrySelectCustomer(int customerId)
    {
        if (customerId <= 0 || _customerInput.DataSource is not IEnumerable<Customer> customers)
        {
            return false;
        }

        var customer = customers.FirstOrDefault(c => c.Id == customerId);
        if (customer is null)
        {
            return false;
        }

        _customerInput.SelectedItem = customer;
        return true;
    }

    private bool TrySelectCustomerByName(string customerName)
    {
        if (string.IsNullOrWhiteSpace(customerName) || _customerInput.DataSource is not IEnumerable<Customer> customers)
        {
            return false;
        }

        var customer = customers.FirstOrDefault(c => string.Equals(c.Name, customerName, StringComparison.OrdinalIgnoreCase));
        if (customer is null)
        {
            return false;
        }

        _customerInput.SelectedItem = customer;
        return true;
    }


    private static bool ParseCheckCell(object? value)
    {
        return value switch
        {
            bool boolean => boolean,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => false
        };
    }

    private void ShowFeedback(string message)
    {
        _feedback.Text = message;
    }
}
