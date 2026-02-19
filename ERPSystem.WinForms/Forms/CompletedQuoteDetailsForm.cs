using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Forms;

public class CompletedQuoteDetailsForm : Form
{
    private readonly Quote _quote;
    private readonly Func<QuoteLineItem, Control> _modelPreviewFactory;
    private readonly DataGridView _lineItemsGrid = new();
    private readonly Panel _previewHost = new() { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) };
    private readonly TableLayoutPanel _metadataTable = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };

    public CompletedQuoteAction RequestedAction { get; private set; } = CompletedQuoteAction.None;

    public CompletedQuoteDetailsForm(Quote quote, bool canManage, Func<QuoteLineItem, Control> modelPreviewFactory)
    {
        _quote = quote;
        _modelPreviewFactory = modelPreviewFactory;

        Text = $"Completed Quote #{quote.Id} Details";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(920, 620);
        Size = new Size(1200, 760);
        FormBorderStyle = FormBorderStyle.Sizable;

        var headerTitle = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 0, 6, 0),
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Text = $"Quote #{quote.Id} • {quote.CustomerName} • {quote.LineItems.Count} line item(s)"
        };

        var headerPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 58,
            ColumnCount = 2,
            Padding = new Padding(12, 10, 12, 10)
        };
        headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerPanel.Controls.Add(headerTitle, 0, 0);

        var actionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };

        if (canManage)
        {
            var deleteButton = new Button { Text = "Delete", AutoSize = true, BackColor = Color.Firebrick, ForeColor = Color.White };
            deleteButton.Click += (_, _) =>
            {
                RequestedAction = CompletedQuoteAction.Delete;
                Close();
            };
            actionPanel.Controls.Add(deleteButton);

            var editButton = new Button { Text = "Edit", AutoSize = true };
            editButton.Click += (_, _) =>
            {
                RequestedAction = CompletedQuoteAction.Edit;
                Close();
            };
            actionPanel.Controls.Add(editButton);
        }

        headerPanel.Controls.Add(actionPanel, 1, 0);

        var summaryPanel = BuildSummaryPanel(quote);
        var contentPanel = BuildContentPanel();

        var closeButton = new Button
        {
            Text = "Close",
            AutoSize = true
        };
        closeButton.Click += (_, _) => Close();

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        footer.Controls.Add(closeButton);

        Controls.Add(contentPanel);
        Controls.Add(summaryPanel);
        Controls.Add(footer);
        Controls.Add(headerPanel);

        Load += (_, _) => BindLineItems();
    }

    private Control BuildSummaryPanel(Quote quote)
    {
        var summary = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 78,
            ColumnCount = 4,
            Padding = new Padding(12, 0, 12, 6)
        };

        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        summary.Controls.Add(BuildSummaryChip("Status", quote.Status.ToString()), 0, 0);
        summary.Controls.Add(BuildSummaryChip("Lifecycle ID", string.IsNullOrWhiteSpace(quote.LifecycleQuoteId) ? "N/A" : quote.LifecycleQuoteId), 1, 0);
        summary.Controls.Add(BuildSummaryChip("Completed", quote.CompletedUtc?.ToLocalTime().ToString("g") ?? "N/A"), 2, 0);
        summary.Controls.Add(BuildSummaryChip("Total", quote.MasterTotal.ToString("C2")), 3, 0);

        return summary;
    }

    private static Control BuildSummaryChip(string title, string value)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, BackColor = Color.FromArgb(245, 248, 252), Margin = new Padding(4), Padding = new Padding(8, 6, 8, 6) };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        panel.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        panel.Controls.Add(new Label { Text = value, Dock = DockStyle.Fill, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        return panel;
    }

    private Control BuildContentPanel()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Panel1MinSize = 330,
            Panel2MinSize = 420
        };
        split.SizeChanged += (_, _) => ApplySafeSplitterDistance(split);
        split.HandleCreated += (_, _) => ApplySafeSplitterDistance(split);

        ConfigureLineItemsGrid();
        split.Panel1.Controls.Add(_lineItemsGrid);

        var detailPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 6), BackColor = Color.FromArgb(248, 251, 255) };
        _metadataTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        _metadataTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        detailPanel.Controls.Add(_previewHost);
        detailPanel.Controls.Add(_metadataTable);
        split.Panel2.Controls.Add(detailPanel);

        return split;
    }

    private static void ApplySafeSplitterDistance(SplitContainer split)
    {
        if (split.Width <= 0)
        {
            return;
        }

        var availableWidth = split.Width - split.SplitterWidth;
        var minimumPanel1 = split.Panel1MinSize;
        var maximumPanel1 = Math.Max(minimumPanel1, availableWidth - split.Panel2MinSize);
        var preferredPanel1 = 420;
        split.SplitterDistance = Math.Clamp(preferredPanel1, minimumPanel1, maximumPanel1);
    }

    private void ConfigureLineItemsGrid()
    {
        _lineItemsGrid.Dock = DockStyle.Fill;
        _lineItemsGrid.AutoGenerateColumns = false;
        _lineItemsGrid.ReadOnly = true;
        _lineItemsGrid.AllowUserToAddRows = false;
        _lineItemsGrid.AllowUserToDeleteRows = false;
        _lineItemsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _lineItemsGrid.MultiSelect = false;
        _lineItemsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        _lineItemsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Description", DataPropertyName = nameof(QuoteLineItem.Description), FillWeight = 34 });
        _lineItemsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Drawing #", DataPropertyName = nameof(QuoteLineItem.DrawingNumber), FillWeight = 18 });
        _lineItemsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Revision", DataPropertyName = nameof(QuoteLineItem.Revision), FillWeight = 12 });
        _lineItemsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Qty", DataPropertyName = nameof(QuoteLineItem.Quantity), FillWeight = 10 });
        _lineItemsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Line Total", DataPropertyName = nameof(QuoteLineItem.LineItemTotal), FillWeight = 16, DefaultCellStyle = new DataGridViewCellStyle { Format = "C2" } });

        _lineItemsGrid.SelectionChanged += (_, _) => ShowSelectedLineItemDetails();
    }

    private void BindLineItems()
    {
        _lineItemsGrid.DataSource = _quote.LineItems.ToList();
        if (_lineItemsGrid.Rows.Count > 0)
        {
            _lineItemsGrid.Rows[0].Selected = true;
            ShowSelectedLineItemDetails();
            return;
        }

        _previewHost.Controls.Clear();
        _previewHost.Controls.Add(new Label
        {
            Text = "No line items attached to this completed quote.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.DimGray
        });
    }

    private void ShowSelectedLineItemDetails()
    {
        if (_lineItemsGrid.CurrentRow?.DataBoundItem is not QuoteLineItem lineItem)
        {
            return;
        }

        _metadataTable.SuspendLayout();
        _metadataTable.Controls.Clear();
        _metadataTable.RowStyles.Clear();

        AddMetadataRow("Drawing Name", lineItem.Description);
        AddMetadataRow("Drawing Number", lineItem.DrawingNumber);
        AddMetadataRow("Revision", lineItem.Revision);
        AddMetadataRow("Quantity", lineItem.Quantity.ToString("0.##"));
        AddMetadataRow("Line Total", lineItem.LineItemTotal.ToString("C2"));
        AddMetadataRow("3D Controls", "Rotate • Pan • Zoom");
        _metadataTable.ResumeLayout();

        _previewHost.Controls.Clear();
        var preview = _modelPreviewFactory(lineItem);
        preview.Dock = DockStyle.Fill;
        _previewHost.Controls.Add(preview);
    }

    private void AddMetadataRow(string label, string value)
    {
        var rowIndex = _metadataTable.RowCount++;
        _metadataTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _metadataTable.Controls.Add(new Label { Text = label, AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Margin = new Padding(0, 2, 8, 2) }, 0, rowIndex);
        _metadataTable.Controls.Add(new Label { Text = string.IsNullOrWhiteSpace(value) ? "N/A" : value, AutoSize = true, Margin = new Padding(0, 2, 0, 2) }, 1, rowIndex);
    }
}

public enum CompletedQuoteAction
{
    None,
    Edit,
    Delete
}
