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

    public void OnPurchaseOrderStageChanged(string stage, byte[]? modelBlob, string modelFileName, ElementHost targetViewportHost)
    {
        if (!string.Equals(stage, "Viewing", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (targetViewportHost.Child is not HelixViewportHostControl targetViewport)
        {
            targetViewport = new HelixViewportHostControl();
            targetViewportHost.Child = targetViewport;
        }

        if (modelBlob is null || modelBlob.Length == 0)
        {
            MessageBox.Show(this, "No model data was provided for the selected line item.", "3D Model Load Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            targetViewport.LoadModelFromBytes(modelBlob, modelFileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Failed to parse or render the 3D model.\n\n{ex.Message}",
                "3D Model Load Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
