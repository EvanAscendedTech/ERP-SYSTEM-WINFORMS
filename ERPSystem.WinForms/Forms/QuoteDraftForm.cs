using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using System.Globalization;

namespace ERPSystem.WinForms.Forms;

public class QuoteDraftForm : Form
{
    private readonly QuoteRepository _quoteRepository;
    private readonly bool _canViewPricing;
    private readonly ComboBox _customerPicker = new() { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _customerAddress = new() { Width = 300, ReadOnly = true };
    private readonly TextBox _customerPartPo = new() { Width = 220, PlaceholderText = "Customer Part PO" };
    private readonly TextBox _cycleTime = new() { Width = 120, PlaceholderText = "Cycle Time" };
    private readonly TextBox _ipNotes = new() { Width = 180, PlaceholderText = "IP Fields" };
    private readonly TextBox _quoteLifecycleId = new() { Width = 220, ReadOnly = true };
    private readonly FlowLayoutPanel _lineItemsPanel = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
    private readonly Label _totalHoursValue = new() { AutoSize = true, Text = "0.00" };
    private readonly Label _masterTotalValue = new() { AutoSize = true, Text = "$0.00" };
    private readonly Quote? _editingQuote;
    private decimal _shopHourlyRate;

    public int CreatedQuoteId { get; private set; }
    public bool WasDeleted { get; private set; }

    public QuoteDraftForm(QuoteRepository quoteRepository, bool canViewPricing, string uploadedBy, Quote? editingQuote = null)
    {
        _quoteRepository = quoteRepository;
        _canViewPricing = canViewPricing;
        _editingQuote = editingQuote;
        _quoteLifecycleId.Text = string.IsNullOrWhiteSpace(editingQuote?.LifecycleQuoteId) ? GenerateLifecycleQuoteId() : editingQuote.LifecycleQuoteId;

        Text = _editingQuote is null ? $"New Quote Draft - {_quoteLifecycleId.Text}" : $"Edit In-Process Quote #{_editingQuote.Id} - {_quoteLifecycleId.Text}";
        Width = 1300;
        Height = 780;
        WindowState = FormWindowState.Maximized;
        StartPosition = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        header.Controls.Add(new Label { Text = "Customer Name", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        header.Controls.Add(_customerPicker);
        header.Controls.Add(new Label { Text = "Customer Address", AutoSize = true, Margin = new Padding(10, 8, 0, 0) });
        header.Controls.Add(_customerAddress);
        header.Controls.Add(new Label { Text = "Customer Part PO", AutoSize = true, Margin = new Padding(10, 8, 0, 0) });
        header.Controls.Add(_customerPartPo);
        header.Controls.Add(new Label { Text = "Cycle Time", AutoSize = true, Margin = new Padding(10, 8, 0, 0) });
        header.Controls.Add(_cycleTime);
        header.Controls.Add(new Label { Text = "IP Fields", AutoSize = true, Margin = new Padding(10, 8, 0, 0) });
        header.Controls.Add(_ipNotes);
        header.Controls.Add(new Label { Text = "Lifecycle ID", AutoSize = true, Margin = new Padding(10, 8, 0, 0) });
        header.Controls.Add(_quoteLifecycleId);

        var addLineButton = new Button { Text = "Add Line Item", AutoSize = true, Margin = new Padding(20, 4, 0, 0) };
        addLineButton.Click += (_, _) => AddLineItemCard();
        header.Controls.Add(addLineButton);

        var totalsPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = true };
        totalsPanel.Controls.Add(new Label { Text = "Total Hours:", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        totalsPanel.Controls.Add(_totalHoursValue);
        totalsPanel.Controls.Add(new Label { Text = "Master Quote Total:", AutoSize = true, Margin = new Padding(20, 8, 0, 0), Visible = _canViewPricing });
        _masterTotalValue.Visible = _canViewPricing;
        totalsPanel.Controls.Add(_masterTotalValue);

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

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_lineItemsPanel, 0, 1);
        root.Controls.Add(totalsPanel, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        Controls.Add(root);

        _customerPicker.SelectedIndexChanged += (_, _) => OnCustomerSelected();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        _shopHourlyRate = await _quoteRepository.GetShopHourlyRateAsync();
        await LoadCustomersAsync();
    }

    private static string GenerateLifecycleQuoteId() => $"Q-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

    private async Task LoadCustomersAsync()
    {
        var customers = await _quoteRepository.GetCustomersAsync();
        _customerPicker.DataSource = customers.ToList();
        _customerPicker.DisplayMember = nameof(Customer.Name);
        _customerPicker.ValueMember = nameof(Customer.Id);

        if (_editingQuote is not null)
        {
            PopulateFromQuote(_editingQuote);
            return;
        }

        AddLineItemCard();
    }

    private void OnCustomerSelected()
    {
        if (_customerPicker.SelectedItem is Customer customer)
        {
            _customerAddress.Text = customer.Address;
        }
    }

    private void PopulateFromQuote(Quote quote)
    {
        _customerPicker.SelectedValue = quote.CustomerId;
        _customerPartPo.Text = ParseMetadata(quote.LineItems.FirstOrDefault()?.Notes).GetValueOrDefault("Customer Part PO", string.Empty);
        _cycleTime.Text = ParseMetadata(quote.LineItems.FirstOrDefault()?.Notes).GetValueOrDefault("Cycle Time", string.Empty);
        _ipNotes.Text = ParseMetadata(quote.LineItems.FirstOrDefault()?.Notes).GetValueOrDefault("IP Fields", string.Empty);
        _shopHourlyRate = quote.ShopHourlyRateSnapshot > 0 ? quote.ShopHourlyRateSnapshot : _shopHourlyRate;

        _lineItemsPanel.Controls.Clear();
        foreach (var line in quote.LineItems)
        {
            AddLineItemCard(line);
        }

        if (_lineItemsPanel.Controls.Count == 0)
        {
            AddLineItemCard();
        }

        RecalculateQuoteTotals();
    }

    private void AddLineItemCard(QuoteLineItem? source = null)
    {
        var group = new GroupBox { Text = $"Line Item {_lineItemsPanel.Controls.Count + 1}", Width = 1210, Height = 180, AutoSize = false };
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 3, Padding = new Padding(6) };
        for (var i = 0; i < 5; i++) fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

        var drawingNumber = NewField("Drawing Number", source?.DrawingNumber);
        var drawingName = NewField("Drawing Name", source?.DrawingName);
        var revision = NewField("Revision", source?.Revision);
        var productionHours = NewField("Production Hours", source?.ProductionHours.ToString(CultureInfo.CurrentCulture));
        var setupHours = NewField("Setup Hours", source?.SetupHours.ToString(CultureInfo.CurrentCulture));
        var materialCost = NewField("Material Cost", source?.MaterialCost.ToString(CultureInfo.CurrentCulture));
        var toolingCost = NewField("Tooling Cost", source?.ToolingCost.ToString(CultureInfo.CurrentCulture));
        var secondaryCost = NewField("Secondary Operations Cost", source?.SecondaryOperationsCost.ToString(CultureInfo.CurrentCulture));
        var lineTotal = NewField("Line Item Total", source?.LineItemTotal.ToString(CultureInfo.CurrentCulture), readOnly: true);

        productionHours.Name = "ProductionHours";
        setupHours.Name = "SetupHours";
        materialCost.Name = "MaterialCost";
        toolingCost.Name = "ToolingCost";
        secondaryCost.Name = "SecondaryOperationsCost";
        lineTotal.Name = "LineItemTotal";

        drawingNumber.Name = "DrawingNumber";
        drawingName.Name = "DrawingName";
        revision.Name = "Revision";

        foreach (var box in new[] { productionHours, setupHours, materialCost, toolingCost, secondaryCost })
        {
            box.TextChanged += (_, _) => RecalculateQuoteTotals();
        }

        AddControl(fields, 0, 0, "Drawing Number", drawingNumber);
        AddControl(fields, 1, 0, "Drawing Name", drawingName);
        AddControl(fields, 2, 0, "Revision", revision);
        AddControl(fields, 3, 0, "Production Hours", productionHours);
        AddControl(fields, 4, 0, "Setup Hours", setupHours);
        AddControl(fields, 0, 1, "Material Cost", materialCost);
        AddControl(fields, 1, 1, "Tooling Cost", toolingCost);
        AddControl(fields, 2, 1, "Secondary Operations Cost", secondaryCost);
        AddControl(fields, 3, 1, "Line Item Total", lineTotal);

        var removeButton = new Button { Text = "Remove", AutoSize = true };
        removeButton.Click += (_, _) =>
        {
            _lineItemsPanel.Controls.Remove(group);
            RenumberLineItems();
            RecalculateQuoteTotals();
        };
        fields.Controls.Add(removeButton, 4, 1);

        group.Controls.Add(fields);
        _lineItemsPanel.Controls.Add(group);
        RecalculateQuoteTotals();
    }

    private static TextBox NewField(string name, string? value = null, bool readOnly = false) => new() { Width = 180, Name = name.Replace(" ", string.Empty), Text = value ?? string.Empty, ReadOnly = readOnly };

    private static void AddControl(TableLayoutPanel panel, int col, int row, string label, Control input)
    {
        var flow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        flow.Controls.Add(new Label { Text = label, AutoSize = true });
        flow.Controls.Add(input);
        panel.Controls.Add(flow, col, row);
    }

    private void RenumberLineItems()
    {
        for (var i = 0; i < _lineItemsPanel.Controls.Count; i++)
        {
            if (_lineItemsPanel.Controls[i] is GroupBox gb)
            {
                gb.Text = $"Line Item {i + 1}";
            }
        }
    }

    private void RecalculateQuoteTotals()
    {
        decimal masterTotal = 0;
        decimal totalHours = 0;
        foreach (var group in _lineItemsPanel.Controls.OfType<GroupBox>())
        {
            var production = GetDecimal(group, "ProductionHours");
            var setup = GetDecimal(group, "SetupHours");
            var material = GetDecimal(group, "MaterialCost");
            var tooling = GetDecimal(group, "ToolingCost");
            var secondary = GetDecimal(group, "SecondaryOperationsCost");

            var lineTotal = ((production + setup) * _shopHourlyRate) + material + tooling + secondary;
            totalHours += production + setup;
            masterTotal += lineTotal;

            SetText(group, "LineItemTotal", lineTotal.ToString("0.00", CultureInfo.CurrentCulture));
        }

        _totalHoursValue.Text = totalHours.ToString("0.00", CultureInfo.CurrentCulture);
        _masterTotalValue.Text = masterTotal.ToString("C", CultureInfo.CurrentCulture);
    }

    private static decimal GetDecimal(GroupBox group, string name)
    {
        var text = group.Controls.OfType<TableLayoutPanel>().SelectMany(t => t.Controls.OfType<FlowLayoutPanel>()).SelectMany(f => f.Controls.OfType<TextBox>()).FirstOrDefault(t => t.Name == name)?.Text;
        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out var value) ? value : 0m;
    }

    private static string GetText(GroupBox group, string name)
    {
        return group.Controls.OfType<TableLayoutPanel>().SelectMany(t => t.Controls.OfType<FlowLayoutPanel>()).SelectMany(f => f.Controls.OfType<TextBox>()).FirstOrDefault(t => t.Name == name)?.Text?.Trim() ?? string.Empty;
    }

    private static void SetText(GroupBox group, string name, string value)
    {
        var box = group.Controls.OfType<TableLayoutPanel>().SelectMany(t => t.Controls.OfType<FlowLayoutPanel>()).SelectMany(f => f.Controls.OfType<TextBox>()).FirstOrDefault(t => t.Name == name);
        if (box is not null) box.Text = value;
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
            ShopHourlyRateSnapshot = _shopHourlyRate,
            Status = _editingQuote?.Status ?? QuoteStatus.InProgress,
            CreatedUtc = _editingQuote?.CreatedUtc ?? DateTime.UtcNow,
            LastUpdatedUtc = DateTime.UtcNow
        };

        foreach (var group in _lineItemsPanel.Controls.OfType<GroupBox>())
        {
            var line = new QuoteLineItem
            {
                Description = string.IsNullOrWhiteSpace(GetText(group, "DrawingName")) ? group.Text : GetText(group, "DrawingName"),
                DrawingNumber = GetText(group, "DrawingNumber"),
                DrawingName = GetText(group, "DrawingName"),
                Revision = GetText(group, "Revision"),
                ProductionHours = GetDecimal(group, "ProductionHours"),
                SetupHours = GetDecimal(group, "SetupHours"),
                MaterialCost = GetDecimal(group, "MaterialCost"),
                ToolingCost = GetDecimal(group, "ToolingCost"),
                SecondaryOperationsCost = GetDecimal(group, "SecondaryOperationsCost"),
                LineItemTotal = GetDecimal(group, "LineItemTotal"),
                Quantity = 1,
                UnitPrice = GetDecimal(group, "LineItemTotal"),
                Notes = BuildLineNotes(_customerPartPo.Text.Trim(), _cycleTime.Text.Trim(), _ipNotes.Text.Trim())
            };

            quote.LineItems.Add(line);
        }

        quote.MasterTotal = quote.LineItems.Sum(x => x.LineItemTotal);

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

    private static string BuildLineNotes(string customerPartPo, string cycleTime, string ipFields)
    {
        return $"Customer Part PO: {customerPartPo}\nCycle Time: {cycleTime}\nIP Fields: {ipFields}";
    }

    private static Dictionary<string, string> ParseMetadata(string? notes)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(notes)) return metadata;

        foreach (var line in notes.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0 || separator >= line.Length - 1) continue;
            metadata[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return metadata;
    }

    private async Task DeleteQuoteAsync()
    {
        if (_editingQuote is null) return;

        var confirm = MessageBox.Show($"Delete quote #{_editingQuote.Id}? This cannot be undone.", "Delete Quote", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        await _quoteRepository.DeleteQuoteAsync(_editingQuote.Id);
        WasDeleted = true;
        DialogResult = DialogResult.OK;
        Close();
    }
}
