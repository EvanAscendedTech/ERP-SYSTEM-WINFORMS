using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Controls;

public class ArchiveControl : UserControl
{
    private readonly ArchiveService _archiveService;
    private readonly ListBox _archiveList = new() { Dock = DockStyle.Fill };
    private readonly TextBox _entryInput = new() { Width = 360 };

    public ArchiveControl(ArchiveService archiveService)
    {
        _archiveService = archiveService;
        Dock = DockStyle.Fill;

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8) };
        var archive = new Button { Text = "Archive Item", AutoSize = true };
        archive.Click += (_, _) =>
        {
            _archiveService.Archive(_entryInput.Text);
            _entryInput.Clear();
            RefreshList();
        };

        toolbar.Controls.Add(new Label { Text = "Entry:", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        toolbar.Controls.Add(_entryInput);
        toolbar.Controls.Add(archive);

        Controls.Add(_archiveList);
        Controls.Add(toolbar);
    }

    private void RefreshList()
    {
        _archiveList.Items.Clear();
        foreach (var entry in _archiveService.ArchivedItems)
        {
            _archiveList.Items.Add(entry);
        }
    }
}
