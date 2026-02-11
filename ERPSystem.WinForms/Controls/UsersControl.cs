namespace ERPSystem.WinForms.Controls;

public class UsersControl : UserControl
{
    public UsersControl()
    {
        Dock = DockStyle.Fill;

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 260,
            Padding = new Padding(8)
        };

        var usersGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        usersGrid.Columns.Add("username", "Username");
        usersGrid.Columns.Add("display", "Display Name");
        usersGrid.Columns.Add("active", "Active");
        usersGrid.Rows.Add("jdoe", "John Doe", "Yes");
        usersGrid.Rows.Add("qa_user", "QA Inspector", "Yes");

        var rolesGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        rolesGrid.Columns.Add("role", "Role");
        rolesGrid.Columns.Add("permissions", "Permissions");
        rolesGrid.Rows.Add("Administrator", "All");
        rolesGrid.Rows.Add("Manager", "Production, Inspection, Archive");
        rolesGrid.Rows.Add("Inspector", "Inspection, Archive");

        var usersGroup = new GroupBox { Text = "Users", Dock = DockStyle.Fill };
        usersGroup.Controls.Add(usersGrid);

        var rolesGroup = new GroupBox { Text = "Roles", Dock = DockStyle.Fill };
        rolesGroup.Controls.Add(rolesGrid);

        split.Panel1.Controls.Add(usersGroup);
        split.Panel2.Controls.Add(rolesGroup);

        Controls.Add(split);
    }
}
