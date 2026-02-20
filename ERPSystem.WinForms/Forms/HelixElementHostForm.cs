using System.Windows.Forms.Integration;
using ERPSystem.WinForms.WpfControls;

namespace ERPSystem.WinForms.Forms;

public sealed class HelixElementHostForm : Form
{
    private readonly ElementHost _elementHost;

    public HelixElementHostForm()
    {
        Text = "WinForms + WPF HelixViewport3D";
        Width = 1000;
        Height = 700;
        StartPosition = FormStartPosition.CenterScreen;

        _elementHost = new ElementHost
        {
            Dock = DockStyle.Fill
        };

        Controls.Add(_elementHost);
        Load += OnLoad;
    }

    private void OnLoad(object? sender, EventArgs e)
    {
        _elementHost.Child = new HelixViewportHostControl();
    }
}
