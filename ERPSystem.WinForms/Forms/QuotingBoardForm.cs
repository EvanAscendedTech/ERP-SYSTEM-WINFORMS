using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Forms;

public class QuotingBoardForm : Form
{
    private readonly QuoteRepository _quoteRepository;

    private readonly NumericUpDown _quoteIdInput = new() { Minimum = 0, Maximum = int.MaxValue, Width = 120 };
    private readonly TextBox _customerNameInput = new() { Width = 220 };
    private readonly ComboBox _statusInput = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly DataGridView _lineItemsGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false };
    private readonly Label _feedback = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };

    public QuotingBoardForm(QuoteRepository quoteRepository)
    {
        _quoteRepository = quoteRepository;

        Text = "Quoting Board";
        Width = 1000;
        Height = 650;
        StartPosition = FormStartPosition.CenterScreen;

        ConfigureStatusInput();
        ConfigureLineItemGrid();

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
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Padding = new Padding(12),
            AutoSize = true
        };

        panel.Controls.Add(new Label { Text = "Quote ID", AutoSize = true, Margin = new Padding(6, 8, 6, 0) });
        panel.Controls.Add(_quoteIdInput);
        panel.Controls.Add(new Label { Text = "Customer", AutoSize = true, Margin = new Padding(14, 8, 6, 0) });
        panel.Controls.Add(_customerNameInput);
        panel.Controls.Add(new Label { Text = "Status", AutoSize = true, Margin = new Padding(14, 8, 6, 0) });
        panel.Controls.Add(_statusInput);

        return panel;
    }

    private Control BuildActionsPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Padding = new Padding(12, 0, 12, 12),
            AutoSize = true
        };

        var addRow = new Button { Text = "Add Line Item", AutoSize = true };
        addRow.Click += (_, _) => _lineItemsGrid.Rows.Add(string.Empty, 1m, string.Empty);

        var saveQuote = new Button { Text = "Save Quote", AutoSize = true };
        saveQuote.Click += async (_, _) => await SaveQuoteAsync();

        var loadQuote = new Button { Text = "Load Quote", AutoSize = true };
        loadQuote.Click += async (_, _) => await LoadQuoteAsync();

        var markWon = new Button { Text = "Mark Won", AutoSize = true };
        markWon.Click += async (_, _) => await UpdateStatusAsync(QuoteStatus.Won);

        var markLost = new Button { Text = "Mark Lost", AutoSize = true };
        markLost.Click += async (_, _) => await UpdateStatusAsync(QuoteStatus.Lost);

        panel.Controls.Add(addRow);
        panel.Controls.Add(saveQuote);
        panel.Controls.Add(loadQuote);
        panel.Controls.Add(markWon);
        panel.Controls.Add(markLost);

        return panel;
    }

    private void ConfigureStatusInput()
    {
        _statusInput.DataSource = Enum.GetValues(typeof(QuoteStatus));
    }

    private void ConfigureLineItemGrid()
    {
        _lineItemsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Description",
            HeaderText = "Description",
            Width = 500
        });
        _lineItemsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Quantity",
            HeaderText = "Quantity",
            Width = 120
        });
        _lineItemsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Files",
            HeaderText = "Associated Files (semicolon separated)",
            Width = 320
        });
        _lineItemsGrid.AllowUserToAddRows = false;
    }

    private async Task SaveQuoteAsync()
    {
        try
        {
            var quote = BuildQuoteFromInputs();
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

    private Quote BuildQuoteFromInputs()
    {
        var quote = new Quote
        {
            Id = (int)_quoteIdInput.Value,
            CustomerName = _customerNameInput.Text.Trim(),
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

            var quantityText = row.Cells[1].Value?.ToString();
            var quantity = decimal.TryParse(quantityText, out var parsed) ? parsed : 1m;
            var filesText = row.Cells[2].Value?.ToString() ?? string.Empty;

            quote.LineItems.Add(new QuoteLineItem
            {
                Description = description,
                Quantity = quantity,
                AssociatedFiles = filesText
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList()
            });
        }

        return quote;
    }

    private void PopulateInputs(Quote quote)
    {
        _quoteIdInput.Value = quote.Id;
        _customerNameInput.Text = quote.CustomerName;
        _statusInput.SelectedItem = quote.Status;

        _lineItemsGrid.Rows.Clear();
        foreach (var lineItem in quote.LineItems)
        {
            _lineItemsGrid.Rows.Add(
                lineItem.Description,
                lineItem.Quantity,
                string.Join(";", lineItem.AssociatedFiles));
        }
    }

    private void ShowFeedback(string message)
    {
        _feedback.Text = message;
    }
}
