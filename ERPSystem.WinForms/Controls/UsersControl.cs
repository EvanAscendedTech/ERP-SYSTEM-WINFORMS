using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Controls;

public class UsersControl : UserControl, IRealtimeDataControl
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
        var assignRolesButton = new Button { Text = "Assign Role(s)", AutoSize = true, Enabled = canManageUsers };
        var deactivateButton = new Button { Text = "Kick / Deactivate", AutoSize = true, Enabled = canManageUsers };
        var uploadIconButton = new Button { Text = "Upload My Icon", AutoSize = true };

        addUserButton.Click += async (_, _) => await CreateUserAsync();
        assignRolesButton.Click += async (_, _) => await AssignRolesToSelectedAsync();
        deactivateButton.Click += async (_, _) => await DeactivateSelectedAsync();
        uploadIconButton.Click += async (_, _) => await UploadCurrentUserIconAsync();

        actions.Controls.Add(addUserButton);
        actions.Controls.Add(assignRolesButton);
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
        _usersGrid.RowTemplate.Height = 36;
        _usersGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Id", DataPropertyName = nameof(UserAccount.Id), Width = 50 });
        _usersGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Username", DataPropertyName = nameof(UserAccount.Username), Width = 180 });
        _usersGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Display Name", DataPropertyName = nameof(UserAccount.DisplayName), Width = 200 });
        _usersGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Roles", DataPropertyName = "Roles", Width = 260 });
        _usersGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Active", DataPropertyName = nameof(UserAccount.IsActive), Width = 70 });
        _usersGrid.Columns.Add(new DataGridViewImageColumn { HeaderText = "Icon", DataPropertyName = nameof(UserAccount.IconBlob), Width = 52, ImageLayout = DataGridViewImageCellLayout.Zoom });
    }

    private void ConfigureRequestsGrid()
    {
        _requestsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Requested Username", DataPropertyName = nameof(AccountRequest.RequestedUsername), Width = 180 });
        _requestsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Note", DataPropertyName = nameof(AccountRequest.RequestNote), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _requestsGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Terms", DataPropertyName = nameof(AccountRequest.TermsAccepted), Width = 80 });
    }

    private async Task ReloadAsync()
    {
        _usersGrid.DataSource = (await _userRepository.GetUsersAsync()).Select(ToGridUser).ToList();
        _requestsGrid.DataSource = (await _userRepository.GetAccountRequestsAsync()).ToList();
    }

    private static object ToGridUser(UserAccount user)
    {
        Image? icon = null;
        if (user.IconBlob is { Length: > 0 })
        {
            using var stream = new MemoryStream(user.IconBlob);
            using var image = Image.FromStream(stream);
            icon = new Bitmap(image);
        }

        return new
        {
            user.Id,
            user.Username,
            user.DisplayName,
            Roles = string.Join(", ", user.Roles.Select(r => r.Name)),
            user.IsActive,
            IconBlob = icon
        };
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

        var selectedRoles = Prompt.SelectRoles("Assign role(s)", RoleCatalog.AccountLevels, new[] { RoleCatalog.ProductionEmployee });
        if (selectedRoles.Count == 0)
        {
            selectedRoles.Add(RoleCatalog.ProductionEmployee);
        }

        await _userRepository.SaveUserAsync(new UserAccount
        {
            Username = username.Trim(),
            DisplayName = username.Trim(),
            PasswordHash = AuthorizationService.HashPassword(password),
            IsActive = true,
            Roles = BuildRoles(selectedRoles)
        });

        await ReloadAsync();
        _onUsersChanged();
    }

    private async Task AssignRolesToSelectedAsync()
    {
        if (_usersGrid.CurrentRow?.Cells[1].Value?.ToString() is not { } username || string.IsNullOrWhiteSpace(username))
        {
            return;
        }

        var users = await _userRepository.GetUsersAsync();
        var selected = users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
        {
            return;
        }

        var chosen = Prompt.SelectRoles(
            $"Assign role(s) for {selected.Username}",
            RoleCatalog.AccountLevels,
            selected.Roles.Select(r => r.Name));

        if (chosen.Count == 0)
        {
            return;
        }

        selected.Roles = BuildRoles(chosen);
        await _userRepository.SaveUserAsync(selected);
        await ReloadAsync();
        _onUsersChanged();
    }

    private async Task DeactivateSelectedAsync()
    {
        if (_usersGrid.CurrentRow?.DataBoundItem is null)
        {
            return;
        }

        var username = _usersGrid.CurrentRow.Cells[1].Value?.ToString();
        if (string.IsNullOrWhiteSpace(username))
        {
            return;
        }

        var users = await _userRepository.GetUsersAsync();
        var selected = users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
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

        _currentUser.IconPath = string.Empty;
        _currentUser.IconBlob = File.ReadAllBytes(picker.FileName);
        await _userRepository.SaveUserAsync(_currentUser);
        _onUsersChanged();
        await ReloadAsync();
    }

    private static List<RoleDefinition> BuildRoles(IEnumerable<string> roleNames)
    {
        return roleNames
            .Select(RoleCatalog.NormalizeRoleName)
            .Where(role => role is not null)
            .Select(role => role!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(AuthorizationService.BuildRole)
            .ToList();
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

        public static List<string> SelectRoles(string caption, IEnumerable<string> allRoles, IEnumerable<string> selectedRoles)
        {
            using var form = new Form { Width = 380, Height = 320, Text = caption, StartPosition = FormStartPosition.CenterParent };
            var roleDropDown = new CheckedListBox { Left = 12, Top = 12, Width = 340, Height = 220, CheckOnClick = true };
            var selectedSet = new HashSet<string>(selectedRoles, StringComparer.OrdinalIgnoreCase);

            foreach (var role in allRoles)
            {
                var index = roleDropDown.Items.Add(role);
                roleDropDown.SetItemChecked(index, selectedSet.Contains(role));
            }

            var ok = new Button { Text = "OK", Left = 272, Top = 240, Width = 80, DialogResult = DialogResult.OK };
            form.Controls.Add(roleDropDown);
            form.Controls.Add(ok);
            form.AcceptButton = ok;

            if (form.ShowDialog() != DialogResult.OK)
            {
                return new List<string>();
            }

            return roleDropDown.CheckedItems.Cast<string>().ToList();
        }
    }

    public Task RefreshDataAsync(bool fromFailSafeCheckpoint) => ReloadAsync();
}
