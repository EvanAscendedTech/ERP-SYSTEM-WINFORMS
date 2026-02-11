using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Controls;

public class InspectionControl : UserControl
{
    private readonly InspectionService _service;
    private readonly ListBox _findingsList = new() { Dock = DockStyle.Fill };
    private readonly TextBox _newFinding = new() { Width = 320 };

    public InspectionControl(InspectionService service)
    {
        _service = service;
        Dock = DockStyle.Fill;

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8) };
        var add = new Button { Text = "Add Finding", AutoSize = true };
        add.Click += (_, _) =>
        {
            _service.AddFinding(_newFinding.Text);
            _newFinding.Clear();
            RefreshList();
        };

        var close = new Button { Text = "Close Selected", AutoSize = true };
        close.Click += (_, _) =>
        {
            if (_findingsList.SelectedItem is string selected)
            {
                _service.CloseFinding(selected);
            }

            RefreshList();
        };

        toolbar.Controls.Add(new Label { Text = "Finding:", Margin = new Padding(0, 8, 6, 0), AutoSize = true });
        toolbar.Controls.Add(_newFinding);
        toolbar.Controls.Add(add);
        toolbar.Controls.Add(close);

        Controls.Add(_findingsList);
        Controls.Add(toolbar);
    }

    private void RefreshList()
    {
        _findingsList.Items.Clear();
        foreach (var finding in _service.OpenFindings)
        {
            _findingsList.Items.Add(finding);
        }
    }
}
