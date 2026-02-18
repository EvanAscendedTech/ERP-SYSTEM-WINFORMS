using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Controls;

public class UsersControl : UserControl, IRealtimeDataControl
{
    private readonly UserManagementRepository _userRepository;
    private readonly UserAccount _currentUser;
    private readonly Action _onUsersChanged;
    private readonly Action<string>? _openAuditForUser;

    private readonly FlowLayoutPanel _userCardsPanel = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        WrapContents = false,
        FlowDirection = FlowDirection.TopDown,
        Padding = new Padding(8)
    };

    private readonly DataGridView _requestsGrid = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false };

    public UsersControl(UserManagementRepository userRepository, UserAccount currentUser, Action onUsersChanged, Action<string>? openAuditForUser = null)
    {
        _userRepository = userRepository;
        _currentUser = currentUser;
        _onUsersChanged = onUsersChanged;
        _openAuditForUser = openAuditForUser;
        Dock = DockStyle.Fill;

        var canManageUsers = AuthorizationService.HasPermission(currentUser, UserPermission.ManageUsers);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 35));

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        var addUserButton = new Button { Text = "Add User", AutoSize = true, Enabled = canManageUsers, Font = new Font(Font, FontStyle.Bold) };
        var uploadIconButton = new Button { Text = "Upload My Icon", AutoSize = true };

        addUserButton.Click += async (_, _) => await OpenUserEditorForNewAsync();
        uploadIconButton.Click += async (_, _) => await UploadCurrentUserIconAsync();

        actions.Controls.Add(addUserButton);
        actions.Controls.Add(uploadIconButton);

        ConfigureRequestsGrid();

        root.Controls.Add(actions, 0, 0);
        root.Controls.Add(new GroupBox { Text = "User Access", Dock = DockStyle.Fill, Controls = { _userCardsPanel } }, 0, 1);
        root.Controls.Add(new GroupBox { Text = "Account Requests", Dock = DockStyle.Fill, Controls = { _requestsGrid } }, 0, 2);

        Controls.Add(root);
        _ = ReloadAsync();
    }

    private void ConfigureRequestsGrid()
    {
        _requestsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Requested Username", DataPropertyName = nameof(AccountRequest.RequestedUsername), Width = 180 });
        _requestsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Note", DataPropertyName = nameof(AccountRequest.RequestNote), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _requestsGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Terms", DataPropertyName = nameof(AccountRequest.TermsAccepted), Width = 80 });
    }

    private async Task ReloadAsync()
    {
        var users = (await _userRepository.GetUsersAsync()).OrderBy(u => u.DisplayName).ThenBy(u => u.Username).ToList();
        RenderUserCards(users);
        _requestsGrid.DataSource = (await _userRepository.GetAccountRequestsAsync()).ToList();
    }

    private void RenderUserCards(IReadOnlyList<UserAccount> users)
    {
        _userCardsPanel.SuspendLayout();
        _userCardsPanel.Controls.Clear();

        foreach (var user in users)
        {
            _userCardsPanel.Controls.Add(BuildUserCard(user));
        }

        _userCardsPanel.ResumeLayout();
    }

    private Control BuildUserCard(UserAccount user)
    {
        var card = new Panel
        {
            Width = Math.Max(_userCardsPanel.ClientSize.Width - 32, 700),
            Height = 130,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = user.IsActive ? Color.White : Color.FromArgb(245, 245, 245),
            Cursor = Cursors.Hand,
            Tag = user.Username
        };

        var icon = new PictureBox
        {
            Left = 10,
            Top = 16,
            Width = 92,
            Height = 92,
            BorderStyle = BorderStyle.FixedSingle,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.White
        };
        SetPicture(icon, user.IconBlob);

        var name = new Label
        {
            Left = 116,
            Top = 16,
            AutoSize = true,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Text = user.DisplayName
        };

        var username = new Label
        {
            Left = 116,
            Top = 42,
            AutoSize = true,
            ForeColor = Color.DimGray,
            Text = $"@{user.Username}"
        };

        var roles = new Label
        {
            Left = 116,
            Top = 64,
            AutoSize = true,
            ForeColor = Color.FromArgb(64, 64, 64),
            Text = $"Roles: {string.Join(", ", user.Roles.Select(r => r.Name))}"
        };

        var status = new Label
        {
            Left = 116,
            Top = 88,
            AutoSize = true,
            ForeColor = user.IsActive ? Color.ForestGreen : Color.Firebrick,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Text = user.IsActive ? "Active" : "Inactive"
        };

        var editButton = new Button { Text = "Edit", Width = 86, Height = 32, Left = card.Width - 380, Top = 45 };
        var toggleButton = new Button { Text = user.IsActive ? "Set Inactive" : "Set Active", Width = 100, Height = 32, Left = card.Width - 290, Top = 45 };
        var deleteButton = new Button { Text = "Delete", Width = 86, Height = 32, Left = card.Width - 186, Top = 45 };
        var auditButton = new Button { Text = "Audit", Width = 80, Height = 32, Left = card.Width - 96, Top = 45 };

        var canManageUsers = AuthorizationService.HasPermission(_currentUser, UserPermission.ManageUsers);
        editButton.Enabled = canManageUsers;
        toggleButton.Enabled = canManageUsers;
        deleteButton.Enabled = canManageUsers;

        editButton.Click += async (_, _) => await OpenUserEditorAsync(user);
        toggleButton.Click += async (_, _) => await ToggleUserActiveAsync(user);
        deleteButton.Click += async (_, _) => await DeleteUserAsync(user);
        auditButton.Click += (_, _) => _openAuditForUser?.Invoke(user.Username);

        void OpenAudit(object? _, EventArgs __) => _openAuditForUser?.Invoke(user.Username);
        card.Click += OpenAudit;
        name.Click += OpenAudit;
        username.Click += OpenAudit;
        roles.Click += OpenAudit;
        status.Click += OpenAudit;
        icon.Click += OpenAudit;

        card.Controls.Add(icon);
        card.Controls.Add(name);
        card.Controls.Add(username);
        card.Controls.Add(roles);
        card.Controls.Add(status);
        card.Controls.Add(editButton);
        card.Controls.Add(toggleButton);
        card.Controls.Add(deleteButton);
        card.Controls.Add(auditButton);
        return card;
    }

    private async Task OpenUserEditorForNewAsync()
    {
        using var editor = new UserEditorDialog(_userRepository, null);
        if (editor.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var existingUsers = await _userRepository.GetUsersAsync();
        if (existingUsers.Any(u => string.Equals(u.Username, editor.User.Username, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("A user with this username already exists.", "Duplicate Username", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await _userRepository.SaveUserAsync(editor.User);
        await WriteAuditAsync("Created user", $"Created account for {editor.User.Username}.");
        await ReloadAsync();
        _onUsersChanged();
    }

    private async Task OpenUserEditorAsync(UserAccount selected)
    {
        using var editor = new UserEditorDialog(_userRepository, selected);
        if (editor.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var allUsers = await _userRepository.GetUsersAsync();
        var conflictingUser = allUsers.FirstOrDefault(u => u.Id != selected.Id && string.Equals(u.Username, editor.User.Username, StringComparison.OrdinalIgnoreCase));
        if (conflictingUser is not null)
        {
            MessageBox.Show("Another user already has this username.", "Duplicate Username", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await _userRepository.SaveUserAsync(editor.User);

        if (!string.IsNullOrWhiteSpace(editor.TemporaryPasswordIssued))
        {
            await _userRepository.IssueTemporaryPasswordAsync(editor.User.Id, editor.TemporaryPasswordIssued);
            await WriteAuditAsync("Issued temporary password", $"Issued temporary password for {editor.User.Username}; reset required on next login.");
            MessageBox.Show($"Temporary password for {editor.User.Username}: {editor.TemporaryPasswordIssued}", "Temporary Password Issued", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        await WriteAuditAsync("Edited user", $"Updated account settings for {editor.User.Username}.");

        await ReloadAsync();
        _onUsersChanged();
    }

    private async Task ToggleUserActiveAsync(UserAccount selected)
    {
        selected.IsActive = !selected.IsActive;
        await _userRepository.SaveUserAsync(selected);
        await WriteAuditAsync("Changed user status", $"Set {selected.Username} to {(selected.IsActive ? "Active" : "Inactive")}.");
        await ReloadAsync();
        _onUsersChanged();
    }

    private async Task DeleteUserAsync(UserAccount selected)
    {
        var result = MessageBox.Show(
            $"Delete user '{selected.Username}'? This action cannot be undone.",
            "Confirm Delete",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
        {
            return;
        }

        await _userRepository.DeleteUserAsync(selected.Id);
        await WriteAuditAsync("Deleted user", $"Deleted account for {selected.Username}.");
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

    private async Task WriteAuditAsync(string action, string details)
    {
        await _userRepository.WriteAuditLogAsync(new AuditLogEntry
        {
            OccurredUtc = DateTime.UtcNow,
            Username = _currentUser.Username,
            RoleSnapshot = UserManagementRepository.BuildRoleSnapshot(_currentUser),
            Module = "Admin/User Access",
            Action = action,
            Details = details
        });
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

    private static void SetPicture(PictureBox pictureBox, byte[]? icon)
    {
        pictureBox.Image?.Dispose();
        pictureBox.Image = null;
        if (icon is null || icon.Length == 0)
        {
            return;
        }

        using var stream = new MemoryStream(icon);
        using var image = Image.FromStream(stream);
        pictureBox.Image = new Bitmap(image);
    }

    private sealed class UserEditorDialog : Form
    {
        private readonly CheckedListBox _roleList = new() { Width = 320, Height = 120, CheckOnClick = true };
        private readonly TextBox _username = new() { Width = 320 };
        private readonly TextBox _displayName = new() { Width = 320 };
        private readonly PictureBox _profilePreview = new() { Width = 92, Height = 92, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
        private readonly ListBox _passwordRequests = new() { Width = 320, Height = 110 };
        private readonly UserManagementRepository _repo;
        private readonly bool _isEdit;

        public UserAccount User { get; private set; }
        public string? TemporaryPasswordIssued { get; private set; }

        public UserEditorDialog(UserManagementRepository repo, UserAccount? existing)
        {
            _repo = repo;
            _isEdit = existing is not null;
            User = existing is null ? new UserAccount
            {
                IsActive = true,
                Roles = [AuthorizationService.BuildRole(RoleCatalog.ProductionEmployee)]
            } : Clone(existing);

            Width = 430;
            Height = 620;
            StartPosition = FormStartPosition.CenterParent;
            Text = _isEdit ? $"Edit {existing!.Username}" : "Add User";

            var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
            layout.Controls.Add(new Label { Text = "Username", AutoSize = true });
            _username.Text = User.Username;
            _username.Enabled = !_isEdit;
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

            var issueTempPasswordButton = new Button { Text = "Issue Temporary Password", AutoSize = true, Enabled = _isEdit };
            issueTempPasswordButton.Click += (_, _) => IssueTempPassword();
            layout.Controls.Add(issueTempPasswordButton);

            layout.Controls.Add(new Label { Text = "Password reset requests", AutoSize = true });
            layout.Controls.Add(_passwordRequests);

            var save = new Button { Text = "Save", AutoSize = true };
            save.Click += (_, _) =>
            {
                if (!BuildUser())
                {
                    return;
                }

                DialogResult = DialogResult.OK;
                Close();
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
