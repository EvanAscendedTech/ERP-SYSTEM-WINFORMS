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
        var addUserButton = new Button { Text = "Add User", AutoSize = true, Enabled = canManageUsers };
        var editUserButton = new Button { Text = "Edit Selected User", AutoSize = true, Enabled = canManageUsers };
        var deactivateButton = new Button { Text = "Toggle Active", AutoSize = true, Enabled = canManageUsers };
        var uploadIconButton = new Button { Text = "Upload My Icon", AutoSize = true };

        addUserButton.Click += async (_, _) => await OpenUserEditorForNewAsync();
        editUserButton.Click += async (_, _) => await OpenUserEditorForSelectedAsync();
        deactivateButton.Click += async (_, _) => await ToggleSelectedActiveAsync();
        uploadIconButton.Click += async (_, _) => await UploadCurrentUserIconAsync();

        actions.Controls.Add(addUserButton);
        actions.Controls.Add(editUserButton);
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
        _usersGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Username", DataPropertyName = nameof(UserAccount.Username), Width = 160 });
        _usersGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Display Name", DataPropertyName = nameof(UserAccount.DisplayName), Width = 190 });
        _usersGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Roles", DataPropertyName = "Roles", Width = 230 });
        _usersGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Status", DataPropertyName = "Status", Width = 120 });
        _usersGrid.Columns.Add(new DataGridViewImageColumn { HeaderText = "Profile", DataPropertyName = nameof(UserAccount.IconBlob), Width = 52, ImageLayout = DataGridViewImageCellLayout.Zoom });
        _usersGrid.Columns.Add(new DataGridViewButtonColumn { HeaderText = "", Text = "Edit", UseColumnTextForButtonValue = true, Width = 70 });
        _usersGrid.CellContentClick += async (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != _usersGrid.Columns.Count - 1)
            {
                return;
            }

            await OpenUserEditorForSelectedAsync(e.RowIndex);
        };
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

        var dot = user.IsActive ? "🟢" : "⚪";
        return new
        {
            user.Id,
            user.Username,
            user.DisplayName,
            Roles = string.Join(", ", user.Roles.Select(r => r.Name)),
            Status = $"{dot} {(user.IsActive ? "Active" : "Inactive")}",
            IconBlob = icon
        };
    }

    private async Task OpenUserEditorForNewAsync()
    {
        using var editor = new UserEditorDialog(_userRepository, null);
        if (editor.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await _userRepository.SaveUserAsync(editor.User);
        await _userRepository.WriteAuditLogAsync(new AuditLogEntry
        {
            OccurredUtc = DateTime.UtcNow,
            Username = _currentUser.Username,
            RoleSnapshot = UserManagementRepository.BuildRoleSnapshot(_currentUser),
            Module = "Admin/User Access",
            Action = "Created user",
            Details = $"Created account for {editor.User.Username}."
        });
        await ReloadAsync();
        _onUsersChanged();
    }

    private async Task OpenUserEditorForSelectedAsync(int? rowIndex = null)
    {
        var row = rowIndex.HasValue ? _usersGrid.Rows[rowIndex.Value] : _usersGrid.CurrentRow;
        var username = row?.Cells[1].Value?.ToString();
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

        using var editor = new UserEditorDialog(_userRepository, selected);
        if (editor.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await _userRepository.SaveUserAsync(editor.User);

        if (!string.IsNullOrWhiteSpace(editor.TemporaryPasswordIssued))
        {
            await _userRepository.IssueTemporaryPasswordAsync(editor.User.Id, editor.TemporaryPasswordIssued);
            MessageBox.Show($"Temporary password for {editor.User.Username}: {editor.TemporaryPasswordIssued}", "Temporary Password Issued", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        await _userRepository.WriteAuditLogAsync(new AuditLogEntry
        {
            OccurredUtc = DateTime.UtcNow,
            Username = _currentUser.Username,
            RoleSnapshot = UserManagementRepository.BuildRoleSnapshot(_currentUser),
            Module = "Admin/User Access",
            Action = "Edited user",
            Details = $"Updated account settings for {editor.User.Username}."
        });

        await ReloadAsync();
        _onUsersChanged();
    }

    private async Task ToggleSelectedActiveAsync()
    {
        var username = _usersGrid.CurrentRow?.Cells[1].Value?.ToString();
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

        selected.IsActive = !selected.IsActive;
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

    private sealed class UserEditorDialog : Form
    {
        private readonly CheckedListBox _roleList = new() { Width = 320, Height = 120, CheckOnClick = true };
        private readonly TextBox _username = new() { Width = 320 };
        private readonly TextBox _displayName = new() { Width = 320 };
        private readonly PictureBox _profilePreview = new() { Width = 64, Height = 64, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
        private readonly ListBox _passwordRequests = new() { Width = 320, Height = 110 };
        private readonly UserManagementRepository _repo;

        public UserAccount User { get; private set; }
        public string? TemporaryPasswordIssued { get; private set; }

        public UserEditorDialog(UserManagementRepository repo, UserAccount? existing)
        {
            _repo = repo;
            User = existing is null ? new UserAccount
            {
                IsActive = true,
                Roles = [AuthorizationService.BuildRole(RoleCatalog.ProductionEmployee)]
            } : Clone(existing);

            Width = 430;
            Height = 620;
            StartPosition = FormStartPosition.CenterParent;
            Text = existing is null ? "Add User" : $"Edit {existing.Username}";

            var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
            layout.Controls.Add(new Label { Text = "Username", AutoSize = true });
            _username.Text = User.Username;
            layout.Controls.Add(_username);

            layout.Controls.Add(new Label { Text = "Display Name", AutoSize = true });
            _displayName.Text = User.DisplayName;
            layout.Controls.Add(_displayName);

            layout.Controls.Add(new Label { Text = "Roles (multi-select)", AutoSize = true });
            var selectedRoles = new HashSet<string>(User.Roles.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var role in RoleCatalog.AccountLevels)
            {
                var idx = _roleList.Items.Add(role);
                _roleList.SetItemChecked(idx, selectedRoles.Contains(role));
            }
            layout.Controls.Add(_roleList);

            var iconRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            var uploadIcon = new Button { Text = "Upload Profile Picture", AutoSize = true };
            uploadIcon.Click += (_, _) => UploadIcon();
            iconRow.Controls.Add(uploadIcon);
            iconRow.Controls.Add(_profilePreview);
            layout.Controls.Add(iconRow);
            SetPreview(User.IconBlob);

            var issueTempPasswordButton = new Button { Text = "Issue Temporary Password", AutoSize = true };
            issueTempPasswordButton.Click += (_, _) => IssueTempPassword();
            layout.Controls.Add(issueTempPasswordButton);

            layout.Controls.Add(new Label { Text = "Password reset requests", AutoSize = true });
            layout.Controls.Add(_passwordRequests);

            var save = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
            save.Click += (_, e) =>
            {
                if (!BuildUser())
                {
                    e.Cancel = true;
                }
            };
            layout.Controls.Add(save);

            Controls.Add(layout);
            AcceptButton = save;
            _ = LoadPasswordResetRequestsAsync();
        }

        private async Task LoadPasswordResetRequestsAsync()
        {
            _passwordRequests.Items.Clear();
            if (User.Id <= 0)
            {
                _passwordRequests.Items.Add("No requests yet.");
                return;
            }

            var requests = await _repo.GetPasswordResetRequestsAsync(User.Id);
            if (requests.Count == 0)
            {
                _passwordRequests.Items.Add("No requests.");
                return;
            }

            foreach (var req in requests)
            {
                _passwordRequests.Items.Add($"{req.RequestedUtc.ToLocalTime():g} - {(req.IsResolved ? "Resolved" : "Open")} - {req.Note}");
            }
        }

        private void IssueTempPassword()
        {
            TemporaryPasswordIssued = $"Temp{Guid.NewGuid().ToString("N")[..8]}!";
            MessageBox.Show("Temporary password will be applied on save.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private bool BuildUser()
        {
            var username = _username.Text.Trim();
            var displayName = _displayName.Text.Trim();
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(displayName))
            {
                MessageBox.Show("Username and display name are required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            var selectedRoles = _roleList.CheckedItems.Cast<string>().ToList();
            if (selectedRoles.Count == 0)
            {
                selectedRoles.Add(RoleCatalog.ProductionEmployee);
            }

            User.Username = username;
            User.DisplayName = displayName;
            User.Roles = BuildRoles(selectedRoles);
            if (string.IsNullOrWhiteSpace(User.PasswordHash))
            {
                User.PasswordHash = AuthorizationService.HashPassword($"{username}!123");
            }

            if (!string.IsNullOrWhiteSpace(TemporaryPasswordIssued))
            {
                User.PasswordHash = AuthorizationService.HashPassword(TemporaryPasswordIssued);
                User.MustResetPassword = true;
                User.TemporaryPasswordIssuedUtc = DateTime.UtcNow;
            }

            return true;
        }

        private void UploadIcon()
        {
            using var picker = new OpenFileDialog { Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp", Multiselect = false };
            if (picker.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            User.IconBlob = File.ReadAllBytes(picker.FileName);
            User.IconPath = string.Empty;
            SetPreview(User.IconBlob);
        }

        private void SetPreview(byte[]? icon)
        {
            _profilePreview.Image?.Dispose();
            _profilePreview.Image = null;
            if (icon is null || icon.Length == 0)
            {
                return;
            }

            using var stream = new MemoryStream(icon);
            using var image = Image.FromStream(stream);
            _profilePreview.Image = new Bitmap(image);
        }

        private static UserAccount Clone(UserAccount user)
        {
            return new UserAccount
            {
                Id = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                PasswordHash = user.PasswordHash,
                IsActive = user.IsActive,
                IconPath = user.IconPath,
                IconBlob = user.IconBlob,
                IsOnline = user.IsOnline,
                LastActivityUtc = user.LastActivityUtc,
                MustResetPassword = user.MustResetPassword,
                TemporaryPasswordIssuedUtc = user.TemporaryPasswordIssuedUtc,
                Roles = user.Roles.Select(r => new RoleDefinition { Id = r.Id, Name = r.Name, Permissions = r.Permissions.ToList() }).ToList()
            };
        }
    }

    public Task RefreshDataAsync(bool fromFailSafeCheckpoint) => ReloadAsync();
}
