using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Controls;

public class UsersControl : UserControl
{
    private readonly UserManagementRepository _userRepository;
    private readonly UserAccount _currentUser;
    private readonly Action _onUsersChanged;
    private readonly DataGridView _usersGrid = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly DataGridView _requestsGrid = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false };

    public UsersControl(UserManagementRepository userRepository, UserAccount currentUser, Action onUsersChanged)
    {
        _userRepository = userRepository;
        _currentUser = currentUser;
        _onUsersChanged = onUsersChanged;
        Dock = DockStyle.Fill;

        var canManageUsers = AuthorizationService.HasPermission(currentUser, UserPermission.ManageUsers);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        var addUserButton = new Button { Text = "Create User", AutoSize = true, Enabled = canManageUsers };
        var deactivateButton = new Button { Text = "Kick / Deactivate", AutoSize = true, Enabled = canManageUsers };
        var uploadIconButton = new Button { Text = "Upload My Icon", AutoSize = true };

        addUserButton.Click += async (_, _) => await CreateUserAsync();
        deactivateButton.Click += async (_, _) => await DeactivateSelectedAsync();
        uploadIconButton.Click += async (_, _) => await UploadCurrentUserIconAsync();

        actions.Controls.Add(addUserButton);
        actions.Controls.Add(deactivateButton);
        actions.Controls.Add(uploadIconButton);

        ConfigureUsersGrid();
        ConfigureRequestsGrid();

        root.Controls.Add(actions, 0, 0);
        root.Controls.Add(new GroupBox { Text = "Users", Dock = DockStyle.Fill, Controls = { _usersGrid } }, 0, 1);
        root.Controls.Add(new GroupBox { Text = "Account Requests", Dock = DockStyle.Fill, Controls = { _requestsGrid } }, 0, 2);

        Controls.Add(root);
        _ = ReloadAsync();
    }

    private void ConfigureUsersGrid()
    {
        _usersGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Id", DataPropertyName = nameof(UserAccount.Id), Width = 50 });
        _usersGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Username", DataPropertyName = nameof(UserAccount.Username), Width = 180 });
        _usersGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Display Name", DataPropertyName = nameof(UserAccount.DisplayName), Width = 200 });
        _usersGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Active", DataPropertyName = nameof(UserAccount.IsActive), Width = 70 });
        _usersGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Icon", DataPropertyName = nameof(UserAccount.IconPath), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
    }

    private void ConfigureRequestsGrid()
    {
        _requestsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Requested Username", DataPropertyName = nameof(AccountRequest.RequestedUsername), Width = 180 });
        _requestsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Note", DataPropertyName = nameof(AccountRequest.RequestNote), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _requestsGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Terms", DataPropertyName = nameof(AccountRequest.TermsAccepted), Width = 80 });
    }

    private async Task ReloadAsync()
    {
        _usersGrid.DataSource = (await _userRepository.GetUsersAsync()).ToList();
        _requestsGrid.DataSource = (await _userRepository.GetAccountRequestsAsync()).ToList();
    }

    private async Task CreateUserAsync()
    {
        var username = Prompt.Show("Username");
        if (string.IsNullOrWhiteSpace(username))
        {
            return;
        }

        var password = Prompt.Show("Password");
        if (string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var roleInput = Prompt.Show("Roles (comma separated: Operator, Foreman, Purchasing, Inspection, Shipping and Receiving, Admin, Quoting)");

        await _userRepository.SaveUserAsync(new UserAccount
        {
            Username = username.Trim(),
            DisplayName = username.Trim(),
            PasswordHash = AuthorizationService.HashPassword(password),
            IsActive = true,
            Roles = BuildRoles(roleInput)
        });

        await ReloadAsync();
        _onUsersChanged();
    }

    private async Task DeactivateSelectedAsync()
    {
        if (_usersGrid.CurrentRow?.DataBoundItem is not UserAccount selected)
        {
            return;
        }

        selected.IsActive = false;
        await _userRepository.SaveUserAsync(selected);
        await ReloadAsync();
        _onUsersChanged();
    }

    private async Task UploadCurrentUserIconAsync()
    {
        using var picker = new OpenFileDialog { Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp", Multiselect = false };
        if (picker.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _currentUser.IconPath = picker.FileName;
        await _userRepository.SaveUserAsync(_currentUser);
        _onUsersChanged();
        await ReloadAsync();
    }

    private static List<RoleDefinition> BuildRoles(string roleInput)
    {
        var entries = string.IsNullOrWhiteSpace(roleInput)
            ? new[] { "Operator" }
            : roleInput.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var roles = new List<RoleDefinition>();
        foreach (var name in entries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var permissions = new List<UserPermission>();
            if (name.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            {
                permissions.AddRange(Enum.GetValues<UserPermission>());
            }
            else
            {
                permissions.Add(UserPermission.ViewProduction);
                permissions.Add(UserPermission.ViewInspection);
                if (name.Equals("Purchasing", StringComparison.OrdinalIgnoreCase) || name.Equals("Quoting", StringComparison.OrdinalIgnoreCase))
                {
                    permissions.Add(UserPermission.ViewPricing);
                }
            }

            roles.Add(new RoleDefinition
            {
                Name = name,
                Permissions = permissions.Distinct().ToList()
            });
        }

        return roles;
    }

    private static class Prompt
    {
        public static string Show(string caption)
        {
            using var form = new Form { Width = 360, Height = 140, Text = caption, StartPosition = FormStartPosition.CenterParent };
            var box = new TextBox { Left = 12, Top = 12, Width = 320 };
            var ok = new Button { Text = "OK", Left = 252, Top = 44, Width = 80, DialogResult = DialogResult.OK };
            form.Controls.Add(box);
            form.Controls.Add(ok);
            form.AcceptButton = ok;
            return form.ShowDialog() == DialogResult.OK ? box.Text : string.Empty;
        }
    }
}
