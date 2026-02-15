using System.Drawing.Printing;
using System.Text;
using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Forms;

public class ArchivedQuoteDetailForm : Form
{
    private readonly ArchivedQuote _quote;

    public ArchivedQuoteDetailForm(ArchivedQuote quote)
    {
        _quote = quote;
        Text = $"Archived Quote #{quote.OriginalQuoteId} (Read-Only)";
        Width = 1000;
        Height = 700;
        StartPosition = FormStartPosition.CenterParent;

        var info = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Top,
            Height = 140,
            ScrollBars = ScrollBars.Vertical,
            Text = BuildSummaryText(quote)
        };

        var linesGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false
        };

        linesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Description", DataPropertyName = nameof(QuoteLineItem.Description), Width = 240 });
        linesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Qty", DataPropertyName = nameof(QuoteLineItem.Quantity), Width = 80 });
        linesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Unit Price", DataPropertyName = nameof(QuoteLineItem.UnitPrice), Width = 90 });
        linesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Lead Time", DataPropertyName = nameof(QuoteLineItem.LeadTimeDays), Width = 90 });
        linesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Notes", DataPropertyName = nameof(QuoteLineItem.Notes), Width = 300 });
        linesGrid.DataSource = quote.LineItems;

        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var closeButton = new Button { Text = "Close", AutoSize = true };
        var exportButton = new Button { Text = "Export", AutoSize = true };
        var printButton = new Button { Text = "Print", AutoSize = true };
        closeButton.Click += (_, _) => Close();
        exportButton.Click += (_, _) => Export();
        printButton.Click += (_, _) => PrintSummary();

        actions.Controls.Add(closeButton);
        actions.Controls.Add(printButton);
        actions.Controls.Add(exportButton);

        Controls.Add(linesGrid);
        Controls.Add(info);
        Controls.Add(actions);
    }

    private void Export()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export archived quote",
            Filter = "Text Files|*.txt",
            FileName = $"archived-quote-{_quote.OriginalQuoteId}.txt"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, BuildExportText(_quote));
        MessageBox.Show(this, "Archived quote exported.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void PrintSummary()
    {
        using var printDocument = new PrintDocument();
        var text = BuildExportText(_quote);
        printDocument.PrintPage += (_, e) =>
        {
            e.Graphics.DrawString(text, Font, Brushes.Black, new RectangleF(40, 40, e.MarginBounds.Width, e.MarginBounds.Height));
        };

        using var preview = new PrintPreviewDialog { Document = printDocument, Width = 900, Height = 700 };
        preview.ShowDialog(this);
    }

    private static string BuildSummaryText(ArchivedQuote quote)
        => $"Archive # {quote.ArchiveId}\r\nQuote # {quote.OriginalQuoteId}\r\nLifecycle ID: {quote.LifecycleQuoteId}\r\nCustomer: {quote.CustomerName}\r\nStatus: {quote.Status}\r\nCreated: {quote.CreatedUtc:g}\r\nCompleted: {quote.CompletedUtc:g}\r\nArchived: {quote.ArchivedUtc:g}";

    private static string BuildExportText(ArchivedQuote quote)
    {
        var builder = new StringBuilder();
        builder.AppendLine(BuildSummaryText(quote));
        builder.AppendLine();
        builder.AppendLine("Line Items");
        builder.AppendLine("----------");
        foreach (var line in quote.LineItems)
        {
            builder.AppendLine($"- {line.Description} | Qty: {line.Quantity} | Unit Price: {line.UnitPrice} | Lead Time: {line.LeadTimeDays}");
            if (!string.IsNullOrWhiteSpace(line.Notes))
            {
                builder.AppendLine($"  Notes: {line.Notes}");
            }

            if (line.AssociatedFiles.Count > 0)
            {
                builder.AppendLine($"  Files: {string.Join(", ", line.AssociatedFiles)}");
            }
        }

        return builder.ToString();
    }
}
