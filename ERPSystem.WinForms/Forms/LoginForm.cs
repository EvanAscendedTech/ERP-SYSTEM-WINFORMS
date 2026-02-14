using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Forms;

public class LoginForm : Form
{
    private readonly UserManagementRepository _userRepository;
    private readonly TextBox _username = new() { Width = 340, PlaceholderText = "Username" };
    private readonly TextBox _password = new() { Width = 340, PlaceholderText = "Password", UseSystemPasswordChar = true };
    private readonly TextBox _requestNote = new() { Width = 340, Height = 78, Multiline = true, PlaceholderText = "Request note for admin" };
    private readonly CheckBox _requestTerms = new() { Text = "I understand account creation is admin approved only.", AutoSize = true };

    public UserAccount? AuthenticatedUser { get; private set; }

    public LoginForm(UserManagementRepository userRepository)
    {
        _userRepository = userRepository;

        Text = "ERP Login";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1200, 760);
        BackColor = ColorTranslator.FromHtml("#0F172A");

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(24)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52F));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48F));

        var heroPanel = BuildHeroPanel();
        var loginPanel = BuildLoginPanel();

        root.Controls.Add(heroPanel, 0, 0);
        root.Controls.Add(loginPanel, 1, 0);
        Controls.Add(root);
    }

    private static Control BuildHeroPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(46, 44, 40, 44),
            BackColor = ColorTranslator.FromHtml("#111827")
        };

        var title = new Label
        {
            Text = "ERP Command Center",
            Font = new Font("Segoe UI", 30F, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Dock = DockStyle.Top
        };

        var subtitle = new Label
        {
            Text = "A clean, full-screen workspace with responsive controls and tabbed workflows.",
            Font = new Font("Segoe UI", 12F, FontStyle.Regular),
            ForeColor = ColorTranslator.FromHtml("#CBD5E1"),
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            Dock = DockStyle.Top,
            Padding = new Padding(0, 14, 0, 0)
        };

        panel.Controls.Add(subtitle);
        panel.Controls.Add(title);
        return panel;
    }

    private Control BuildLoginPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(40, 40, 40, 40),
            BackColor = ColorTranslator.FromHtml("#1F2937")
        };

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(16)
        };

        var title = new Label
        {
            Text = "Sign in",
            AutoSize = true,
            Font = new Font("Segoe UI", 22F, FontStyle.Bold),
            ForeColor = Color.White,
            Margin = new Padding(0, 0, 0, 14)
        };

        StyleInput(_username);
        StyleInput(_password);
        StyleInput(_requestNote);

        var loginButton = new ModernButton
        {
            Text = "Login",
            Width = 340,
            Height = 42,
            CornerRadius = 10,
            Margin = new Padding(0, 10, 0, 18)
        };
        loginButton.Click += async (_, _) => await AttemptLoginAsync();

        var requestGroup = new GroupBox
        {
            Text = "Need an account?",
            Width = 366,
            Height = 265,
            ForeColor = Color.White,
            Padding = new Padding(12)
        };

        var requestLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = false, WrapContents = false };
        var requestButton = new ModernButton { Text = "Submit Account Request", Width = 340, Height = 40, CornerRadius = 10 };
        requestButton.Click += async (_, _) => await SubmitAccountRequestAsync();

        requestLayout.Controls.Add(new Label { Text = "Requested username", AutoSize = true, ForeColor = ColorTranslator.FromHtml("#CBD5E1") });
        requestLayout.Controls.Add(new TextBox
        {
            Width = 340,
            Height = 34,
            Name = "RequestedUsernameTextBox",
            PlaceholderText = "Requested username",
            BackColor = ColorTranslator.FromHtml("#0F172A"),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 6, 0, 8)
        });
        requestLayout.Controls.Add(_requestNote);
        requestLayout.Controls.Add(_requestTerms);
        requestLayout.Controls.Add(requestButton);
        requestGroup.Controls.Add(requestLayout);

        AcceptButton = loginButton;

        layout.Controls.Add(title);
        layout.Controls.Add(_username);
        layout.Controls.Add(_password);
        layout.Controls.Add(loginButton);
        layout.Controls.Add(requestGroup);

        panel.Controls.Add(layout);
        return panel;
    }

    private static void StyleInput(TextBox textBox)
    {
        textBox.BackColor = ColorTranslator.FromHtml("#0F172A");
        textBox.ForeColor = Color.White;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.Margin = new Padding(0, 0, 0, 10);
        if (!textBox.Multiline)
        {
            textBox.Height = 34;
        }
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
