namespace ERPSystem.WinForms;

public sealed class QuotesControl : UserControl
{
    private readonly DataGridView _quotesGrid;

    public QuotesControl()
    {
        Dock = DockStyle.Fill;
        DoubleBuffered = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(20)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var title = new Label
        {
            Text = "Quotes",
            Font = new Font("Segoe UI", 19F, FontStyle.Bold),
            AutoSize = true
        };

        var subtitle = new Label
        {
            Text = "Pipeline snapshot (placeholder grid styling for repository-bound data).",
            Font = new Font("Segoe UI", 10F),
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 16),
            Tag = "secondary"
        };

        _quotesGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            ReadOnly = true,
            RowHeadersVisible = false
        };

        ConfigureGrid();

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(subtitle, 0, 1);
        root.Controls.Add(_quotesGrid, 0, 2);

        Controls.Add(root);
    }

    private void ConfigureGrid()
    {
        _quotesGrid.Columns.Add("quoteNo", "Quote #");
        _quotesGrid.Columns.Add("customer", "Customer");
        _quotesGrid.Columns.Add("status", "Status");
        _quotesGrid.Columns.Add("owner", "Owner");
        _quotesGrid.Columns.Add("amount", "Amount");

        _quotesGrid.Rows.Add("Q-2026-001", "Contoso Manufacturing", "Draft", "A. Smith", "$24,000");
        _quotesGrid.Rows.Add("Q-2026-002", "Northwind Systems", "Pending Approval", "J. Lee", "$11,750");
        _quotesGrid.Rows.Add("Q-2026-003", "Fabrikam Tools", "Won", "R. Kumar", "$39,100");
    }
}
