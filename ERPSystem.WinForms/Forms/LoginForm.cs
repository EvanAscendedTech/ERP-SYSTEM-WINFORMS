using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Forms;

public class LoginForm : Form
{
    private readonly UserManagementRepository _userRepository;
    private readonly TextBox _username = new() { Width = 220, PlaceholderText = "Username" };
    private readonly TextBox _password = new() { Width = 220, PlaceholderText = "Password", UseSystemPasswordChar = true };
    private readonly TextBox _requestNote = new() { Width = 280, Height = 70, Multiline = true, PlaceholderText = "Request note for admin" };
    private readonly CheckBox _requestTerms = new() { Text = "I understand account creation is admin approved only.", AutoSize = true };

    public UserAccount? AuthenticatedUser { get; private set; }

    public LoginForm(UserManagementRepository userRepository)
    {
        _userRepository = userRepository;

        Text = "ERP Login";
        Width = 380;
        Height = 420;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 6
        };

        var title = new Label { Text = "Sign in to ERP", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        var fields = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        fields.Controls.Add(_username);
        fields.Controls.Add(_password);

        var loginButton = new Button { Text = "Login", AutoSize = true };
        loginButton.Click += async (_, _) => await AttemptLoginAsync();

        var requestGroup = new GroupBox { Text = "Need an account?", Dock = DockStyle.Fill, AutoSize = true };
        var requestLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        var requestButton = new Button { Text = "Submit Account Request", AutoSize = true };
        requestButton.Click += async (_, _) => await SubmitAccountRequestAsync();
        requestLayout.Controls.Add(new Label { Text = "Requested username", AutoSize = true });
        requestLayout.Controls.Add(new TextBox { Width = 280, Name = "RequestedUsernameTextBox", PlaceholderText = "Requested username" });
        requestLayout.Controls.Add(_requestNote);
        requestLayout.Controls.Add(_requestTerms);
        requestLayout.Controls.Add(requestButton);
        requestGroup.Controls.Add(requestLayout);

        AcceptButton = loginButton;

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(fields, 0, 1);
        layout.Controls.Add(loginButton, 0, 2);
        layout.Controls.Add(requestGroup, 0, 3);

        Controls.Add(layout);
    }

    private async Task AttemptLoginAsync()
    {
        var username = _username.Text.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            MessageBox.Show("Enter a username.", "Login", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var user = await _userRepository.FindByUsernameAsync(username);
        if (user is null || !user.IsActive || !AuthorizationService.VerifyPassword(user, _password.Text))
        {
            MessageBox.Show("Invalid credentials. Contact admin for account access.", "Login", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        user.IsOnline = true;
        AuthenticatedUser = user;
        DialogResult = DialogResult.OK;
        Close();
    }

    private async Task SubmitAccountRequestAsync()
    {
        var requestedUsername = Controls.Find("RequestedUsernameTextBox", true).OfType<TextBox>().FirstOrDefault()?.Text.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(requestedUsername))
        {
            MessageBox.Show("Enter a requested username.", "Account Request", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!_requestTerms.Checked)
        {
            MessageBox.Show("You must confirm the checkbox before submitting.", "Account Request", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await _userRepository.SaveAccountRequestAsync(new AccountRequest
        {
            RequestedUsername = requestedUsername,
            RequestNote = _requestNote.Text,
            TermsAccepted = true,
            RequestedUtc = DateTime.UtcNow
        });

        MessageBox.Show("Account request submitted.", "Account Request", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
