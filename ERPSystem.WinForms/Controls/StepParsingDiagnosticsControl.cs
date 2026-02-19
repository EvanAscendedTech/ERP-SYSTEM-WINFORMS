using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Controls;

public sealed class StepParsingDiagnosticsControl : UserControl
{
    private readonly StepParsingDiagnosticsLog _diagnosticsLog;
    private readonly DataGridView _logGrid = new()
    {
        Dock = DockStyle.Fill,
        AutoGenerateColumns = false,
        ReadOnly = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false
    };

    private readonly TextBox _messageText = new() { Multiline = true, Dock = DockStyle.Fill, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _stackTraceText = new() { Multiline = true, Dock = DockStyle.Fill, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
    private readonly Label _summary = new() { Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.MiddleLeft };

    private readonly BindingSource _binding = new();
    private readonly List<StepParsingDiagnosticEntry> _entries = new();

    public StepParsingDiagnosticsControl(StepParsingDiagnosticsLog diagnosticsLog)
    {
        _diagnosticsLog = diagnosticsLog;
        Dock = DockStyle.Fill;

        BuildGridColumns();

        var clearButton = new Button { Text = "Clear Logs", AutoSize = true };
        clearButton.Click += (_, _) => _diagnosticsLog.Clear();

        var topBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(8, 6, 8, 6), FlowDirection = FlowDirection.LeftToRight };
        topBar.Controls.Add(clearButton);

        var detailTabs = new TabControl { Dock = DockStyle.Fill };
        detailTabs.TabPages.Add(new TabPage("Error Message") { Controls = { _messageText } });
        detailTabs.TabPages.Add(new TabPage("Stack Trace") { Controls = { _stackTraceText } });

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 220 };
        split.Panel1.Controls.Add(_logGrid);
        split.Panel2.Controls.Add(detailTabs);

        Controls.Add(split);
        Controls.Add(_summary);
        Controls.Add(topBar);

        _binding.DataSource = _entries;
        _logGrid.DataSource = _binding;
        _logGrid.SelectionChanged += (_, _) => RenderSelectedEntry();

        foreach (var entry in _diagnosticsLog.GetEntries())
        {
            _entries.Add(entry);
        }

        _diagnosticsLog.EntryAdded += OnEntryAdded;
        _diagnosticsLog.Cleared += OnCleared;
        UpdateSummary();
        RenderSelectedEntry();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _diagnosticsLog.EntryAdded -= OnEntryAdded;
            _diagnosticsLog.Cleared -= OnCleared;
        }

        base.Dispose(disposing);
    }

    private void BuildGridColumns()
    {
        _logGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(StepParsingDiagnosticEntry.TimestampUtc), HeaderText = "UTC Time", Width = 155 });
        _logGrid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(StepParsingDiagnosticEntry.IsSuccess), HeaderText = "OK", Width = 40 });
        _logGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(StepParsingDiagnosticEntry.FileName), HeaderText = "File", Width = 180 });
        _logGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(StepParsingDiagnosticEntry.FileSizeBytes), HeaderText = "Size", Width = 75 });
        _logGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(StepParsingDiagnosticEntry.ErrorCode), HeaderText = "Error Code", Width = 140 });
        _logGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(StepParsingDiagnosticEntry.Source), HeaderText = "Source", Width = 120 });
        _logGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(StepParsingDiagnosticEntry.FilePath), HeaderText = "Path", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
    }

    private void OnEntryAdded(object? sender, StepParsingDiagnosticEntry entry)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnEntryAdded(sender, entry)));
            return;
        }

        _entries.Insert(0, entry);
        _binding.ResetBindings(false);
        if (_entries.Count > 0)
        {
            _logGrid.ClearSelection();
            _logGrid.Rows[0].Selected = true;
        }

        UpdateSummary();
        RenderSelectedEntry();
    }

    private void OnCleared(object? sender, EventArgs eventArgs)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnCleared(sender, eventArgs)));
            return;
        }

        _entries.Clear();
        _binding.ResetBindings(false);
        _messageText.Text = string.Empty;
        _stackTraceText.Text = string.Empty;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var total = _entries.Count;
        var failures = _entries.Count(e => !e.IsSuccess);
        _summary.Text = $"Attempts: {total} | Failures: {failures}";
    }

    private void RenderSelectedEntry()
    {
        if (_logGrid.SelectedRows.Count == 0 || _logGrid.SelectedRows[0].DataBoundItem is not StepParsingDiagnosticEntry selected)
        {
            _messageText.Text = string.Empty;
            _stackTraceText.Text = string.Empty;
            return;
        }

        _messageText.Text = selected.Message;
        _stackTraceText.Text = selected.StackTrace;
    }
}
