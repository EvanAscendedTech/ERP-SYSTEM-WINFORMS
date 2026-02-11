using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Forms;
using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Controls;

public class QuotesControl : UserControl
{
    private readonly QuoteRepository _quoteRepository;
    private readonly DataGridView _quotesGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly Label _feedback = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };

    public QuotesControl(QuoteRepository quoteRepository)
    {
        _quoteRepository = quoteRepository;
        Dock = DockStyle.Fill;

        ConfigureQuotesGrid();

        var actionsPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
        var refreshButton = new Button { Text = "Refresh Active Quotes", AutoSize = true };
        var openQuotePacketButton = new Button { Text = "Open Quote Packet", AutoSize = true };

        refreshButton.Click += async (_, _) => await LoadActiveQuotesAsync();
        openQuotePacketButton.Click += async (_, _) => await OpenSelectedQuotePacketAsync();

        actionsPanel.Controls.Add(refreshButton);
        actionsPanel.Controls.Add(openQuotePacketButton);

        Controls.Add(_quotesGrid);
        Controls.Add(actionsPanel);
        Controls.Add(_feedback);

        _ = LoadActiveQuotesAsync();
    }

    private void ConfigureQuotesGrid()
    {
        _quotesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "QuoteId", HeaderText = "Quote #", DataPropertyName = nameof(Quote.Id), Width = 80 });
        _quotesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Customer", HeaderText = "Customer", DataPropertyName = nameof(Quote.CustomerName), Width = 240 });
        _quotesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", DataPropertyName = nameof(Quote.Status), Width = 120 });
        _quotesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "QuotedAt", HeaderText = "Quoted", DataPropertyName = nameof(Quote.CreatedUtc), Width = 180 });
        _quotesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "TimeSinceQuoted", HeaderText = "Timeframe Since Quoted", Width = 220 });
        _quotesGrid.CellFormatting += (_, e) =>
        {
            if (_quotesGrid.Columns[e.ColumnIndex].Name == "TimeSinceQuoted"
                && _quotesGrid.Rows[e.RowIndex].DataBoundItem is Quote quote)
            {
                var elapsed = DateTime.UtcNow - quote.CreatedUtc;
                e.Value = $"{elapsed.Days} days ({Math.Max(0, elapsed.Hours)}h)";
                e.FormattingApplied = true;
            }
        };
    }

    private async Task LoadActiveQuotesAsync()
    {
        try
        {
            var activeQuotes = await _quoteRepository.GetActiveQuotesAsync();
            _quotesGrid.DataSource = activeQuotes.OrderByDescending(q => q.CreatedUtc).ToList();
            _feedback.Text = $"Loaded {activeQuotes.Count} active quotes.";
        }
        catch (Exception ex)
        {
            _feedback.Text = $"Unable to load active quotes: {ex.Message}";
        }
    }

    private async Task OpenSelectedQuotePacketAsync()
    {
        if (_quotesGrid.CurrentRow?.DataBoundItem is not Quote selected)
        {
            _feedback.Text = "Select a quote row first.";
            return;
        }

        var fullQuote = await _quoteRepository.GetQuoteAsync(selected.Id);
        if (fullQuote is null)
        {
            _feedback.Text = $"Quote {selected.Id} was not found.";
            return;
        }

        using var packetWindow = new QuotePacketForm(fullQuote);
        if (packetWindow.ShowDialog(this) == DialogResult.OK)
        {
            await _quoteRepository.SaveQuoteAsync(fullQuote);
            _feedback.Text = $"Quote packet for quote {fullQuote.Id} saved to database.";
            await LoadActiveQuotesAsync();
        }
        else
        {
            _feedback.Text = "Quote packet closed without saving.";
        }
    }
}
