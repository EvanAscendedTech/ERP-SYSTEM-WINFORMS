namespace ERPSystem.WinForms.Forms;

public class LoginForm : Form
{
    private readonly TextBox _username = new() { Width = 220, PlaceholderText = "Username" };
    private readonly TextBox _password = new() { Width = 220, PlaceholderText = "Password", UseSystemPasswordChar = true };

    public string EnteredUsername => _username.Text.Trim();

    public LoginForm()
    {
        Text = "ERP Login";
        Width = 360;
        Height = 220;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label { Text = "Sign in to ERP", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        var fields = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        fields.Controls.Add(_username);
        fields.Controls.Add(_password);

        var loginButton = new Button { Text = "Login", AutoSize = true, DialogResult = DialogResult.OK };
        loginButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_username.Text))
            {
                MessageBox.Show("Enter a username.", "Login", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.None;
            }
        };

        AcceptButton = loginButton;

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(fields, 0, 1);
        layout.Controls.Add(new Label { Text = "", AutoSize = true }, 0, 2);
        layout.Controls.Add(loginButton, 0, 3);

        Controls.Add(layout);
    }
}
