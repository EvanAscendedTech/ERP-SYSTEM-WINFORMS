using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Controls;

public class CRMControl : UserControl
{
    private readonly QuoteRepository _quoteRepository;
    private readonly DataGridView _customersGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true };
    private readonly DataGridView _contactsGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true };

    public CRMControl(QuoteRepository quoteRepository)
    {
        _quoteRepository = quoteRepository;
        Dock = DockStyle.Fill;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 60F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 40F));

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        var addCustomer = new Button { Text = "Create New Customer", AutoSize = true };
        var refresh = new Button { Text = "Refresh", AutoSize = true };
        addCustomer.Click += async (_, _) => await CreateCustomerAsync();
        refresh.Click += async (_, _) => await LoadCustomersAsync();
        actions.Controls.Add(addCustomer);
        actions.Controls.Add(refresh);

        _customersGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Code", DataPropertyName = nameof(Customer.Code), Width = 120 });
        _customersGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name", DataPropertyName = nameof(Customer.Name), Width = 220 });
        _customersGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Last interaction", DataPropertyName = nameof(Customer.LastInteractionUtc), Width = 180 });

        _contactsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Contact", DataPropertyName = nameof(CustomerContact.Name), Width = 180 });
        _contactsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Email", DataPropertyName = nameof(CustomerContact.Email), Width = 220 });
        _contactsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Phone", DataPropertyName = nameof(CustomerContact.Phone), Width = 160 });
        _contactsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Notes", DataPropertyName = nameof(CustomerContact.Notes), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

        root.Controls.Add(actions, 0, 0);
        root.Controls.Add(new GroupBox { Text = "Customers", Dock = DockStyle.Fill, Controls = { _customersGrid } }, 0, 1);
        root.Controls.Add(new GroupBox { Text = "Contact Information", Dock = DockStyle.Fill, Controls = { _contactsGrid } }, 0, 2);

        Controls.Add(root);
        _ = LoadCustomersAsync();
    }

    private async Task CreateCustomerAsync()
    {
        var code = Prompt.Show("Customer code");
        var name = Prompt.Show("Customer name");
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var customer = new Customer
        {
            Code = code.Trim(),
            Name = name.Trim(),
            IsActive = true,
            Contacts = new List<CustomerContact>()
        };

        while (true)
        {
            var contactName = Prompt.Show("Contact name (leave empty to finish)");
            if (string.IsNullOrWhiteSpace(contactName))
            {
                break;
            }

            customer.Contacts.Add(new CustomerContact
            {
                Name = contactName.Trim(),
                Email = Prompt.Show("Contact email"),
                Phone = Prompt.Show("Contact phone"),
                Notes = Prompt.Show("Contact notes")
            });
        }

        await _quoteRepository.SaveCustomerAsync(customer);
        await LoadCustomersAsync();
    }

    private async Task LoadCustomersAsync()
    {
        var customers = await _quoteRepository.GetCustomersAsync(activeOnly: false);
        _customersGrid.DataSource = customers.ToList();
        _contactsGrid.DataSource = customers.SelectMany(c => c.Contacts).ToList();
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
