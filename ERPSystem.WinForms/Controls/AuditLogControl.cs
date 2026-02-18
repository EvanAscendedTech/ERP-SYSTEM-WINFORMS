using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Controls;

public class AuditLogControl : UserControl, IRealtimeDataControl
{
    private readonly UserManagementRepository _userRepository;
    private readonly ComboBox _userFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly DateTimePicker _fromDate = new() { Width = 130, Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker _toDate = new() { Width = 130, Format = DateTimePickerFormat.Short };
    private readonly CheckBox _enableDateFilter = new() { Text = "Date filter", AutoSize = true };
    private readonly Button _refreshButton = new() { Text = "Refresh", AutoSize = true };
    private readonly DataGridView _auditGrid = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };

    public AuditLogControl(UserManagementRepository userRepository)
    {
        _userRepository = userRepository;
        Dock = DockStyle.Fill;

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        actions.Controls.Add(new Label { Text = "Filter by user", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        actions.Controls.Add(_userFilter);
        actions.Controls.Add(_enableDateFilter);
        actions.Controls.Add(new Label { Text = "From", AutoSize = true, Margin = new Padding(8, 8, 0, 0) });
        actions.Controls.Add(_fromDate);
        actions.Controls.Add(new Label { Text = "To", AutoSize = true, Margin = new Padding(8, 8, 0, 0) });
        actions.Controls.Add(_toDate);
        actions.Controls.Add(_refreshButton);

        _userFilter.SelectedIndexChanged += async (_, _) => await LoadAuditAsync();
        _enableDateFilter.CheckedChanged += async (_, _) => await LoadAuditAsync();
        _fromDate.ValueChanged += async (_, _) => { if (_enableDateFilter.Checked) await LoadAuditAsync(); };
        _toDate.ValueChanged += async (_, _) => { if (_enableDateFilter.Checked) await LoadAuditAsync(); };
        _refreshButton.Click += async (_, _) => await LoadAuditAsync();

        ConfigureGrid();

        Controls.Add(_auditGrid);
        Controls.Add(actions);

        _ = InitializeAsync();
    }

    public async Task FocusUserAsync(string username)
    {
        var target = _userFilter.Items.Cast<object>().Select(i => i.ToString()).FirstOrDefault(u => string.Equals(u, username, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(target))
        {
            _userFilter.SelectedItem = target;
        }

        await LoadAuditAsync();
    }

    private async Task InitializeAsync()
    {
        var users = await _userRepository.GetUsersAsync();
        _userFilter.Items.Clear();
        _userFilter.Items.Add("All Users");
        foreach (var username in users.Select(u => u.Username).OrderBy(x => x))
        {
            _userFilter.Items.Add(username);
        }

        _fromDate.Value = DateTime.Today.AddDays(-30);
        _toDate.Value = DateTime.Today;
        _userFilter.SelectedIndex = 0;
        await LoadAuditAsync();
    }

    private void ConfigureGrid()
    {
        _auditGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "When", DataPropertyName = "Occurred", Width = 150 });
        _auditGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "User", DataPropertyName = nameof(AuditLogEntry.Username), Width = 130 });
        _auditGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Role", DataPropertyName = nameof(AuditLogEntry.RoleSnapshot), Width = 170 });
        _auditGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Module", DataPropertyName = nameof(AuditLogEntry.Module), Width = 120 });
        _auditGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Action", DataPropertyName = nameof(AuditLogEntry.Action), Width = 190 });
        _auditGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Details", DataPropertyName = nameof(AuditLogEntry.Details), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
    }

    private async Task LoadAuditAsync()
    {
        var selectedUser = _userFilter.SelectedItem?.ToString();
        DateTime? fromUtc = null;
        DateTime? toUtc = null;
        if (_enableDateFilter.Checked)
        {
            fromUtc = _fromDate.Value.Date.ToUniversalTime();
            toUtc = _toDate.Value.Date.AddDays(1).AddTicks(-1).ToUniversalTime();
        }

        var logs = await _userRepository.GetAuditLogEntriesAsync(
            selectedUser == "All Users" ? null : selectedUser,
            fromUtc,
            toUtc);

        _auditGrid.DataSource = logs.Select(log => new
        {
            Occurred = log.OccurredUtc.ToLocalTime().ToString("g"),
            log.Username,
            log.RoleSnapshot,
            log.Module,
            log.Action,
            log.Details
        }).ToList();
    }

    public Task RefreshDataAsync(bool fromFailSafeCheckpoint) => LoadAuditAsync();
}
