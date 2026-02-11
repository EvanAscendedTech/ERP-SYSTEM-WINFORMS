using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Controls;

public class SettingsControl : UserControl
{
    private readonly AppSettingsService _settingsService;
    private readonly UserManagementRepository _userRepository;

    private readonly ComboBox _theme = new() { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _notifications = new() { Text = "Enable Notifications", AutoSize = true };
    private readonly NumericUpDown _autoRefresh = new() { Minimum = 5, Maximum = 3600, Width = 90 };
    private readonly TextBox _archivePath = new() { Width = 250 };

    private readonly TextBox _username = new() { Width = 130 };
    private readonly TextBox _displayName = new() { Width = 130 };
    private readonly ComboBox _role = new() { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DataGridView _usersGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false };
    private readonly Label _feedback = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };

    public SettingsControl(AppSettingsService settingsService, UserManagementRepository userRepository)
    {
        _settingsService = settingsService;
        _userRepository = userRepository;

        Dock = DockStyle.Fill;

        _theme.Items.AddRange(["Light", "Dark", "Blue"]);
        _theme.SelectedIndex = 0;
        _role.Items.AddRange(["Operator", "Inspector", "Manager", "Administrator"]);
        _role.SelectedIndex = 0;

        ConfigureUsersGrid();

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 180 };
        split.Panel1.Controls.Add(BuildSettingsPanel());
        split.Panel2.Controls.Add(BuildUsersPanel());

        Controls.Add(split);
        Controls.Add(_feedback);

        _ = LoadStateAsync();
    }

    private Control BuildSettingsPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), AutoSize = true };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        panel.Controls.Add(new Label { Text = "Theme", AutoSize = true }, 0, 0);
        panel.Controls.Add(_theme, 1, 0);
        panel.Controls.Add(_notifications, 1, 1);
        panel.Controls.Add(new Label { Text = "Auto Refresh (s)", AutoSize = true }, 0, 2);
        panel.Controls.Add(_autoRefresh, 1, 2);
        panel.Controls.Add(new Label { Text = "Default Archive Path", AutoSize = true }, 0, 3);
        panel.Controls.Add(_archivePath, 1, 3);

        var saveSettings = new Button { Text = "Save Settings", AutoSize = true };
        saveSettings.Click += async (_, _) => await SaveSettingsAsync();
        panel.Controls.Add(saveSettings, 1, 4);

        return panel;
    }

    private Control BuildUsersPanel()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8) };
        var saveUser = new Button { Text = "Save User", AutoSize = true };
        saveUser.Click += async (_, _) => await SaveUserAsync();

        toolbar.Controls.Add(new Label { Text = "Username", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        toolbar.Controls.Add(_username);
        toolbar.Controls.Add(new Label { Text = "Display", AutoSize = true, Margin = new Padding(8, 8, 6, 0) });
        toolbar.Controls.Add(_displayName);
        toolbar.Controls.Add(new Label { Text = "Role", AutoSize = true, Margin = new Padding(8, 8, 6, 0) });
        toolbar.Controls.Add(_role);
        toolbar.Controls.Add(saveUser);

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_usersGrid, 0, 1);

        return root;
    }

    private void ConfigureUsersGrid()
    {
        _usersGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Username", Width = 140 });
        _usersGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Display Name", Width = 180 });
        _usersGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Roles", Width = 260 });
        _usersGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Permissions", Width = 420 });
    }

    private async Task LoadStateAsync()
    {
        var settings = await _settingsService.LoadAsync();
        _theme.SelectedItem = settings.Theme;
        _notifications.Checked = settings.EnableNotifications;
        _autoRefresh.Value = settings.AutoRefreshSeconds;
        _archivePath.Text = settings.DefaultArchivePath;

        await RefreshUsersAsync();
    }

    private async Task SaveSettingsAsync()
    {
        var settings = new AppSettings
        {
            Theme = _theme.SelectedItem?.ToString() ?? "Light",
            EnableNotifications = _notifications.Checked,
            AutoRefreshSeconds = (int)_autoRefresh.Value,
            DefaultArchivePath = _archivePath.Text.Trim()
        };

        await _settingsService.SaveAsync(settings);
        _feedback.Text = "Settings saved.";
    }

    private async Task SaveUserAsync()
    {
        if (string.IsNullOrWhiteSpace(_username.Text))
        {
            _feedback.Text = "Username is required.";
            return;
        }

        var role = BuildRole(_role.SelectedItem?.ToString() ?? "Operator");
        var user = new UserAccount
        {
            Username = _username.Text.Trim(),
            DisplayName = _displayName.Text.Trim(),
            PasswordHash = "change-me",
            IsActive = true,
            Roles = new List<RoleDefinition> { role }
        };

        await _userRepository.SaveUserAsync(user);
        _feedback.Text = $"User {user.Username} saved.";
        await RefreshUsersAsync();
    }

    private async Task RefreshUsersAsync()
    {
        _usersGrid.Rows.Clear();
        var users = await _userRepository.GetUsersAsync();
        foreach (var user in users)
        {
            var roles = string.Join(", ", user.Roles.Select(r => r.Name));
            var permissions = string.Join(", ", user.Roles.SelectMany(r => r.Permissions).Distinct());
            _usersGrid.Rows.Add(user.Username, user.DisplayName, roles, permissions);
        }
    }

    private static RoleDefinition BuildRole(string roleName)
    {
        return roleName switch
        {
            "Administrator" => new RoleDefinition { Name = roleName, Permissions = Enum.GetValues<UserPermission>().ToList() },
            "Manager" => new RoleDefinition
            {
                Name = roleName,
                Permissions = [UserPermission.ViewProduction, UserPermission.ManageProduction, UserPermission.ViewInspection, UserPermission.ViewArchive, UserPermission.ManageUsers, UserPermission.ManageSettings]
            },
            "Inspector" => new RoleDefinition
            {
                Name = roleName,
                Permissions = [UserPermission.ViewInspection, UserPermission.ManageInspection, UserPermission.ViewArchive]
            },
            _ => new RoleDefinition
            {
                Name = roleName,
                Permissions = [UserPermission.ViewProduction, UserPermission.ViewInspection]
            }
        };
    }
}
